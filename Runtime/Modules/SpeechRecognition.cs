// SpeechRecognition.cs (Refactored and Optimized)

namespace Eitan.SherpaONNXUnity.Runtime.Modules
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Runtime.CompilerServices;
    using Eitan.SherpaONNXUnity.Runtime.Native;
    using Eitan.SherpaONNXUnity.Runtime.Utilities;
    using Eitan.SherpaONNXUnity.Runtime.Utilities.Lexicon;

    public class SpeechRecognition : SherpaONNXModule
    {
        public sealed class Options
        {
            public float Rule1MinTrailingSilence { get; set; } = 2.4f;
            public float Rule2MinTrailingSilence { get; set; } = 1.2f;
            public float Rule3MinUtteranceLength { get; set; } = 30f;
            public string Language { get; set; }
            public SherpaONNXExecutionProvider ExecutionProvider { get; set; } = SherpaONNXExecutionProvider.Cpu;
            public bool WarmUpOnInitialization { get; set; } = true;
            public SherpaONNXSpeechRecognitionModeRequirement ModeRequirement { get; set; }
                = SherpaONNXSpeechRecognitionModeRequirement.Any;
        }

        private OnlineRecognizer _onlineRecognizer;
        private OnlineStream _onlineStream;
        private OfflineRecognizer _offlineRecognizer;

        private SpeechRecognitionModelType _modelType;
        private SherpaONNXSpeechRecognitionModelSpec _modelSpec;
        private readonly object _lockObject = new object();
        private int _modelSampleRate;
        private float[] _endpointPaddingBuffer;
        private int _endpointPaddingSampleRate;
        public bool IsOnlineModel { get; private set; }
        private readonly SemaphoreSlim _transcriptionSemaphore = new SemaphoreSlim(1, 1);
        private readonly Options _options;
        private readonly int _maxPendingTranscriptions;
        private readonly bool _dropIfBusy;
        private int _pendingTranscriptions;
        private SherpaCudaProviderDiagnostics _cudaProviderDiagnostics;

        public SherpaONNXExecutionProvider ExecutionProvider => _options.ExecutionProvider;
        public TimeSpan ModelLoadDuration { get; private set; }
        public TimeSpan WarmUpDuration { get; private set; }
        public bool WasWarmedUp { get; private set; }
        public SherpaONNXSpeechRecognitionModelSpec ResolvedModelSpec => _modelSpec;
        public SherpaCudaProviderDiagnostics CudaProviderDiagnostics =>
            _cudaProviderDiagnostics
            ?? SherpaCudaRuntimeDiagnostics.CreateNotApplicable(ExecutionProvider);

        /// <summary>
        /// Re-checks the loaded CUDA provider without changing the recognizer or
        /// execution provider. The returned snapshot is suitable for a Run/Case
        /// diagnostic record.
        /// </summary>
        public SherpaCudaProviderDiagnostics RefreshCudaProviderDiagnostics(
            SherpaCudaProviderDiagnosticStage stage = SherpaCudaProviderDiagnosticStage.PostDecode)
        {
            _cudaProviderDiagnostics = ExecutionProvider == SherpaONNXExecutionProvider.Cuda
                ? SherpaCudaRuntimeDiagnostics.CaptureLoadedCudaProvider(stage)
                : SherpaCudaRuntimeDiagnostics.CreateNotApplicable(ExecutionProvider);
            return _cudaProviderDiagnostics;
        }

        private readonly struct RecognizerConfigContext
        {
            public RecognizerConfigContext(int threadCount, string tokensPath, string int8Keyword, Action<string> fallbackReporter)
            {
                ThreadCount = threadCount;
                TokensPath = tokensPath;
                Int8Keyword = int8Keyword;
                FallbackReporter = fallbackReporter;
            }

            public int ThreadCount { get; }
            public string TokensPath { get; }
            public string Int8Keyword { get; }
            public Action<string> FallbackReporter { get; }
        }

        public enum TranscriptionStatus
        {
            Success,
            NotReady,
            Disposed,
            Cancelled,
            Busy,
            Error
        }

        /// <summary>
        /// Monotonic, managed-side observations of one successful offline
        /// transcription. These values describe the C# call boundaries; in
        /// particular, <see cref="OfflineDecodeCall"/> is not GPU kernel time.
        /// The default value represents an uninstrumented result.
        /// </summary>
        public readonly struct TranscriptionTimings
        {
            public TranscriptionTimings(
                TimeSpan moduleSemaphoreWait,
                TimeSpan workerDispatchWait,
                TimeSpan streamCreate,
                TimeSpan acceptWaveform,
                TimeSpan offlineDecodeCall,
                TimeSpan resultMaterialization,
                TimeSpan postProcessing,
                TimeSpan streamDispose,
                TimeSpan workerTotal,
                TimeSpan moduleTotal)
            {
                ModuleSemaphoreWait = EnsureNonNegative(moduleSemaphoreWait, nameof(moduleSemaphoreWait));
                WorkerDispatchWait = EnsureNonNegative(workerDispatchWait, nameof(workerDispatchWait));
                StreamCreate = EnsureNonNegative(streamCreate, nameof(streamCreate));
                AcceptWaveform = EnsureNonNegative(acceptWaveform, nameof(acceptWaveform));
                OfflineDecodeCall = EnsureNonNegative(offlineDecodeCall, nameof(offlineDecodeCall));
                ResultMaterialization = EnsureNonNegative(resultMaterialization, nameof(resultMaterialization));
                PostProcessing = EnsureNonNegative(postProcessing, nameof(postProcessing));
                StreamDispose = EnsureNonNegative(streamDispose, nameof(streamDispose));
                WorkerTotal = EnsureNonNegative(workerTotal, nameof(workerTotal));
                ModuleTotal = EnsureNonNegative(moduleTotal, nameof(moduleTotal));
                IsAvailable = true;
            }

            public bool IsAvailable { get; }
            public TimeSpan ModuleSemaphoreWait { get; }
            public TimeSpan WorkerDispatchWait { get; }
            public TimeSpan StreamCreate { get; }
            public TimeSpan AcceptWaveform { get; }
            public TimeSpan OfflineDecodeCall { get; }
            public TimeSpan ResultMaterialization { get; }
            public TimeSpan PostProcessing { get; }
            public TimeSpan StreamDispose { get; }
            public TimeSpan WorkerTotal { get; }
            public TimeSpan ModuleTotal { get; }

            internal TranscriptionTimings WithModuleBoundary(
                TimeSpan moduleSemaphoreWait,
                TimeSpan moduleTotal)
            {
                if (!IsAvailable)
                {
                    return default;
                }

                return new TranscriptionTimings(
                    moduleSemaphoreWait,
                    WorkerDispatchWait,
                    StreamCreate,
                    AcceptWaveform,
                    OfflineDecodeCall,
                    ResultMaterialization,
                    PostProcessing,
                    StreamDispose,
                    WorkerTotal,
                    moduleTotal);
            }

            private static TimeSpan EnsureNonNegative(TimeSpan value, string parameterName)
            {
                if (value < TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(parameterName, "Transcription timing values cannot be negative.");
                }

                return value;
            }
        }

        public readonly struct TranscriptionResult
        {
            public TranscriptionResult(
                TranscriptionStatus status,
                string text = "",
                bool isFinal = false,
                Exception error = null,
                string[] tokens = null,
                float[] timestamps = null,
                float[] durations = null,
                TranscriptionTimings timings = default)
            {
                Status = status;
                Text = text ?? string.Empty;
                IsFinal = isFinal;
                Error = error;
                Tokens = tokens ?? Array.Empty<string>();
                Timestamps = timestamps ?? Array.Empty<float>();
                Durations = durations ?? Array.Empty<float>();
                Timings = timings;
            }

            public TranscriptionStatus Status { get; }
            public string Text { get; }
            public bool IsFinal { get; }
            public Exception Error { get; }
            public string[] Tokens { get; }
            public float[] Timestamps { get; }
            public float[] Durations { get; }
            public TranscriptionTimings Timings { get; }

            internal TranscriptionResult WithTimings(TranscriptionTimings timings)
            {
                return new TranscriptionResult(
                    Status,
                    Text,
                    IsFinal,
                    Error,
                    Tokens,
                    Timestamps,
                    Durations,
                    timings);
            }
        }

        protected override SherpaONNXModuleType ModuleType => SherpaONNXModuleType.SpeechRecognition;

        public SpeechRecognition(string modelID, int sampleRate = 16000, SherpaONNXFeedbackReporter reporter = null, bool startImmediately = true, Options options = null, int maxPendingTranscriptions = 2, bool dropIfBusy = true)
            : base(modelID, sampleRate, reporter, startImmediately)
        {
            _options = options ?? new Options();
            _maxPendingTranscriptions = Math.Max(1, maxPendingTranscriptions);
            _dropIfBusy = dropIfBusy;
        }

        protected override void ValidateMetadataBeforePreparation(SherpaONNXModelMetadata metadata)
        {
            _modelSpec = SpeechRecognitionModelResolver.Resolve(
                metadata?.modelId,
                metadata,
                _options.ModeRequirement);
            if (!_modelSpec.CanInitialize)
            {
                throw new InvalidOperationException(_modelSpec.Diagnostic);
            }

            _modelType = _modelSpec.ModelType;
            IsOnlineModel = _modelSpec.IsOnline;
        }

        /// <summary>
        /// Runs the same isolated silent inference used during initialization.
        /// The active streaming stream is never used, so a manual warm-up cannot
        /// inject silence into the next player utterance.
        /// </summary>
        public Task<bool> WarmUpAsync(CancellationToken cancellationToken = default)
        {
            if (!Initialized || IsDisposed)
            {
                return Task.FromResult(false);
            }

            return runner.RunAsync<bool>(ct =>
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
                linkedCts.Token.ThrowIfCancellationRequested();

                if (IsOnlineModel && _onlineRecognizer != null)
                {
                    WarmUpOnlineRecognizer(_modelSampleRate, linkedCts.Token);
                    return Task.FromResult(true);
                }

                if (!IsOnlineModel && _offlineRecognizer != null)
                {
                    WarmUpOfflineRecognizer(_modelSampleRate, linkedCts.Token);
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }, cancellationToken: cancellationToken);
        }

        protected override async Task<bool> Initialization(SherpaONNXModelMetadata metadata, int sampleRate, bool isMobilePlatform, SherpaONNXFeedbackReporter reporter, CancellationToken ct)
        {
            reporter?.Report(new LoadFeedback(metadata, message: $"Start Loading: {metadata.modelId}"));

            if (_modelSpec == null || !_modelSpec.CanInitialize)
            {
                ValidateMetadataBeforePreparation(metadata);
            }

            _modelSampleRate = metadata?.sampleRate > 0 ? metadata.sampleRate : sampleRate;

            if (IsOnlineModel)
            {
                return await LoadOnlineModelAsync(metadata, sampleRate, isMobilePlatform, reporter, ct);
            }
            else
            {
                return await LoadOfflineModelAsync(metadata, sampleRate, isMobilePlatform, reporter, ct);
            }
        }

        private async Task<bool> LoadOnlineModelAsync(SherpaONNXModelMetadata metadata, int sampleRate, bool isMobilePlatform, SherpaONNXFeedbackReporter reporter, CancellationToken ct)
        {
            EnsureExecutionProviderAvailable();
            TryReportAndroid32BitRuntimeRisk(metadata, reporter, "SpeechRecognition");
            var context = BuildConfigContext(metadata, sampleRate, isMobilePlatform, reporter);
            var config = CreateOnlineRecognizerConfig(metadata, sampleRate, context);
            ReportRecognizerConfigDiagnostics(metadata, reporter, sampleRate, context, config);

            return await runner.RunAsync<bool>(cancellationToken =>
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
                linkedCts.Token.ThrowIfCancellationRequested();

                if (IsDisposed) { return Task.FromResult(false); }

                ModelLoadDuration = TimeSpan.Zero;
                WarmUpDuration = TimeSpan.Zero;
                WasWarmedUp = false;
                var loadTimer = System.Diagnostics.Stopwatch.StartNew();
                _onlineRecognizer = new OnlineRecognizer(config);
                var initialized = IsSuccessInitializad(_onlineRecognizer);
                if (initialized)
                {
                    _onlineStream = _onlineRecognizer.CreateStream();
                }
                ModelLoadDuration = loadTimer.Elapsed;
                if (initialized && _options.WarmUpOnInitialization)
                {
                    WarmUpOnlineRecognizer(sampleRate, linkedCts.Token);
                }
                if (ExecutionProvider == SherpaONNXExecutionProvider.Cuda)
                {
                    _cudaProviderDiagnostics = SherpaCudaRuntimeDiagnostics.CaptureLoadedCudaProvider(
                        SherpaCudaProviderDiagnosticStage.PostInitializationWarmup);
                    if (!initialized || !_cudaProviderDiagnostics.IsPassed)
                    {
                        if (!initialized)
                        {
                            throw new InvalidOperationException(
                                "Online recognizer initialization failed before the CUDA provider could be verified.");
                        }

                        _onlineStream?.Dispose();
                        _onlineRecognizer?.Dispose();
                        _onlineStream = null;
                        _onlineRecognizer = null;
                        throw new InvalidOperationException(_cudaProviderDiagnostics.Message);
                    }
                }
                reporter?.Report(new LoadFeedback(metadata, message: $"Loaded online model: {metadata.modelId} ({ExecutionProvider}, load={ModelLoadDuration.TotalMilliseconds:0} ms, warmup={WarmUpDuration.TotalMilliseconds:0} ms)"));
                return Task.FromResult(initialized);
            });
        }

        private async Task<bool> LoadOfflineModelAsync(SherpaONNXModelMetadata metadata, int sampleRate, bool isMobilePlatform, SherpaONNXFeedbackReporter reporter, CancellationToken ct)
        {
            EnsureExecutionProviderAvailable();
            TryReportAndroid32BitRuntimeRisk(metadata, reporter, "SpeechRecognition");
            var context = BuildConfigContext(metadata, sampleRate, isMobilePlatform, reporter);
            var config = CreateOfflineRecognizerConfig(metadata, sampleRate, context);
            ReportRecognizerConfigDiagnostics(metadata, reporter, sampleRate, context, config);

            return await runner.RunAsync<bool>(cancellationToken =>
             {
                 using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
                 linkedCts.Token.ThrowIfCancellationRequested();

                 if (IsDisposed) { return Task.FromResult(false); }

                 try
                 {
                     ModelLoadDuration = TimeSpan.Zero;
                     WarmUpDuration = TimeSpan.Zero;
                     WasWarmedUp = false;
                     var loadTimer = System.Diagnostics.Stopwatch.StartNew();
                     _offlineRecognizer = new OfflineRecognizer(config);
                     var initialized = IsSuccessInitializad(_offlineRecognizer);
                     ModelLoadDuration = loadTimer.Elapsed;

                     if (initialized && _options.WarmUpOnInitialization)
                     {
                         WarmUpOfflineRecognizer(sampleRate, linkedCts.Token);
                     }

                     if (ExecutionProvider == SherpaONNXExecutionProvider.Cuda)
                     {
                         _cudaProviderDiagnostics = SherpaCudaRuntimeDiagnostics.CaptureLoadedCudaProvider(
                             SherpaCudaProviderDiagnosticStage.PostInitializationWarmup);
                         if (!initialized || !_cudaProviderDiagnostics.IsPassed)
                         {
                             if (_offlineRecognizer != null)
                             {
                                 _offlineRecognizer.Dispose();
                                 _offlineRecognizer = null;
                             }

                             throw new InvalidOperationException(
                                 !initialized
                                     ? "Offline recognizer initialization failed before the CUDA provider could be verified."
                                     : _cudaProviderDiagnostics.Message);
                         }
                     }

                     if (initialized)
                     {
                         reporter?.Report(new LoadFeedback(metadata, message: $"Loaded offline model: {metadata.modelId}"));
                     }
                     else
                     {
                         reporter?.Report(new FailedFeedback(metadata, message: $"Failed to initialize offline model: {metadata.modelId}"));
                     }

                     return Task.FromResult(initialized);
                 }
                 catch (OperationCanceledException)
                 {
                     throw;
                 }
                catch (Exception ex)
                {
                    reporter?.Report(new FailedFeedback(metadata, message: ex.Message, exception: ex));
                    if (ExecutionProvider == SherpaONNXExecutionProvider.Cuda)
                    {
                        if (_cudaProviderDiagnostics == null
                            || _cudaProviderDiagnostics.Stage == SherpaCudaProviderDiagnosticStage.Preflight)
                        {
                            _cudaProviderDiagnostics = SherpaCudaRuntimeDiagnostics.CaptureLoadedCudaProvider(
                                SherpaCudaProviderDiagnosticStage.PostInitializationWarmup);
                        }
                        throw;
                    }
                    return Task.FromResult(false);
                }
             });
        }

        private OnlineRecognizerConfig CreateOnlineRecognizerConfig(SherpaONNXModelMetadata metadata, int sampleRate, RecognizerConfigContext context)
        {
            var config = new OnlineRecognizerConfig(true);
            config.FeatConfig.SampleRate = sampleRate;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.Tokens = context.TokensPath;
            config.ModelConfig.NumThreads = context.ThreadCount;
            config.ModelConfig.Debug = 0;
            config.ModelConfig.Provider = ToNativeProvider(ExecutionProvider);
            config.DecodingMethod = _modelSpec.DecodingMethod;
            config.MaxActivePaths = 4;
            config.EnableEndpoint = 1;
            config.Rule1MinTrailingSilence = _options.Rule1MinTrailingSilence;
            config.Rule2MinTrailingSilence = _options.Rule2MinTrailingSilence;
            config.Rule3MinUtteranceLength = _options.Rule3MinUtteranceLength;

            switch (_modelType)
            {
                case SpeechRecognitionModelType.Online_Paraformer:
                    config.ModelConfig.Paraformer.Encoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Paraformer encoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Encoder },
                        ModelFileCriteria.FromKeywords("encoder", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("encoder"));
                    config.ModelConfig.Paraformer.Decoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Paraformer decoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Decoder },
                        ModelFileCriteria.FromKeywords("decoder", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("decoder"));
                    break;
                case SpeechRecognitionModelType.Online_Transducer:
                    config.ModelConfig.Transducer.Encoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Transducer encoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Encoder },
                        ModelFileCriteria.FromKeywords("encoder", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("encoder"));
                    config.ModelConfig.Transducer.Decoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Transducer decoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Decoder },
                        ModelFileCriteria.FromKeywords("decoder", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("decoder"));
                    config.ModelConfig.Transducer.Joiner = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Transducer joiner",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Joiner },
                        ModelFileCriteria.FromKeywords("joiner", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("joiner"));
                    break;
                case SpeechRecognitionModelType.Online_Ctc:
                case SpeechRecognitionModelType.Online_Zipformer2Ctc:
                    config.ModelConfig.Zipformer2Ctc.Model = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "CTC model",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Model },
                        ModelFileCriteria.FromKeywords("model", "ctc", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("model", "ctc"));
                    break;
                case SpeechRecognitionModelType.Online_Nemo_Ctc:
                    config.ModelConfig.NemoCtc.Model = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "NeMo CTC model",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Model },
                        ModelFileCriteria.FromKeywords("model", "ctc", "nemo", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("model", "ctc", "nemo"),
                        ModelFileCriteria.FromKeywords("model", "ctc"));
                    break;
                case SpeechRecognitionModelType.Online_Tone_Ctc:
                    config.ModelConfig.ToneCtc.Model = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Tone CTC model",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Model },
                        ModelFileCriteria.FromKeywords("model", "tone", "ctc", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("model", "tone", "ctc"),
                        ModelFileCriteria.FromKeywords("model", "ctc"));
                    break;
                default:
                    throw new NotSupportedException($"Unsupported online model type: {_modelType}");
            }

            return config;
        }

        private OfflineRecognizerConfig CreateOfflineRecognizerConfig(SherpaONNXModelMetadata metadata, int sampleRate, RecognizerConfigContext context)
        {
            var config = new OfflineRecognizerConfig(true);
            config.FeatConfig.SampleRate = sampleRate;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.Tokens = context.TokensPath;
            config.ModelConfig.NumThreads = context.ThreadCount;
            config.ModelConfig.Provider = ToNativeProvider(ExecutionProvider);
            config.ModelConfig.ModelType = SherpaUtils.Model.GetOfflineModelTypeString(_modelType, metadata);
            config.DecodingMethod = _modelSpec.DecodingMethod;
            config.MaxActivePaths = 4;
            config.RuleFsts = string.Empty;


            switch (_modelType)
            {
                case SpeechRecognitionModelType.Offline_Transducer:

                    config.ModelConfig.Transducer.Encoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Transducer encoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Encoder },
                        ModelFileCriteria.FromKeywords("encoder", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("encoder"));
                    config.ModelConfig.Transducer.Decoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Transducer decoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Decoder },
                        ModelFileCriteria.FromKeywords("decoder", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("decoder"));
                    config.ModelConfig.Transducer.Joiner = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Transducer joiner",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Joiner },
                        ModelFileCriteria.FromKeywords("joiner", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("joiner"));
                    if (config.DecodingMethod == "modified_beam_search")
                    {
                        var hotwordsPath = ModelFileResolver.ResolveOptionalFileWithBindings(
                            metadata,
                            context.FallbackReporter,
                            new[] { SherpaONNXModelFileKey.Hotwords },
                            ModelFileCriteria.FromKeywords("hotwords"));
                        if (!string.IsNullOrEmpty(hotwordsPath))
                        {
                            config.HotwordsFile = hotwordsPath;
                        }
                    }
                    break;

                case SpeechRecognitionModelType.Offline_Paraformer:
                    config.ModelConfig.Paraformer.Model = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Paraformer model",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Model },
                        ModelFileCriteria.FromKeywords("model", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("model"));
                    break;

                case SpeechRecognitionModelType.Offline_ZipformerCtc:
                    config.ModelConfig.ZipformerCtc.Model = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Zipformer CTC model",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Model },
                        ModelFileCriteria.FromKeywords("model", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("model"));
                    break;

                case SpeechRecognitionModelType.Offline_Nemo_Ctc:
                    config.ModelConfig.NeMoCtc.Model = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "NeMo CTC model",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Model },
                        ModelFileCriteria.FromKeywords("model", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("model"));
                    break;
                case SpeechRecognitionModelType.Offline_WenetCtc:
                    config.ModelConfig.WenetCtc.Model = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Wenet CTC model",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Model },
                        ModelFileCriteria.FromKeywords("model", "ctc", "wenet", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("model", "ctc", "wenet"),
                        ModelFileCriteria.FromKeywords("model", "ctc"),
                        ModelFileCriteria.FromKeywords("model"));
                    break;
                case SpeechRecognitionModelType.Offline_MedAsrCtc:
                    config.ModelConfig.MedAsr.Model = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Med ASR CTC model",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Model },
                        ModelFileCriteria.FromKeywords("model", "ctc", "medasr", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("model", "ctc", "medasr"),
                        ModelFileCriteria.FromKeywords("model", "ctc"),
                        ModelFileCriteria.FromKeywords("model"));
                    break;
                case SpeechRecognitionModelType.Offline_FunAsrNano:
                    config.ModelConfig.FunAsrNano.EncoderAdaptor = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "FunASR Nano encoder adaptor",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.EncoderAdaptor },
                        ModelFileCriteria.FromKeywords("encoder", "adaptor", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("encoder", "adapter", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("encoder_adaptor", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("encoder", "adaptor"),
                        ModelFileCriteria.FromKeywords("encoder", "adapter"),
                        ModelFileCriteria.FromKeywords("encoder_adaptor"));
                    config.ModelConfig.FunAsrNano.LLM = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "FunASR Nano LLM",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Llm },
                        ModelFileCriteria.FromKeywords("llm", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("llm"),
                        ModelFileCriteria.FromKeywords("model", "llm", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("model", "llm"));
                    config.ModelConfig.FunAsrNano.Embedding = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "FunASR Nano embedding",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Embedding },
                        ModelFileCriteria.FromKeywords("embedding", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("embedding"));
                    config.ModelConfig.FunAsrNano.Tokenizer = ModelFileResolver.ResolveRequiredDirectoryWithBindings(
                        metadata,
                        "FunASR Nano tokenizer folder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Tokenizer },
                        ModelFileCriteria.FromDirectoryKeywords("qwen3-0.6b"));
                    config.ModelConfig.FunAsrNano.Language = GetConfiguredLanguageOrDefault(string.Empty);
                    config.ModelConfig.Tokens = string.Empty;
                    break;
                case SpeechRecognitionModelType.Offline_Qwen3Asr:
                    config.ModelConfig.Qwen3Asr.ConvFrontend = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Qwen3 ASR conv frontend",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.ConvFrontend },
                        ModelFileCriteria.FromKeywords("conv_frontend", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("conv", "frontend", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("conv_frontend"),
                        ModelFileCriteria.FromKeywords("conv", "frontend"));
                    config.ModelConfig.Qwen3Asr.Encoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Qwen3 ASR encoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Encoder },
                        ModelFileCriteria.FromKeywords("encoder", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("encoder"));
                    config.ModelConfig.Qwen3Asr.Decoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Qwen3 ASR decoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Decoder },
                        ModelFileCriteria.FromKeywords("decoder", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("decoder"));
                    config.ModelConfig.Qwen3Asr.Tokenizer = ModelFileResolver.ResolveRequiredDirectoryWithBindings(
                        metadata,
                        "Qwen3 ASR tokenizer folder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Tokenizer },
                        ModelFileCriteria.FromDirectoryKeywords("tokenizer"),
                        ModelFileCriteria.FromDirectoryKeywords("qwen3"));
                    config.ModelConfig.Tokens = string.Empty;
                    break;
                case SpeechRecognitionModelType.Offline_CohereTranscribe:
                    config.ModelConfig.CohereTranscribe.Decoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Cohere Transcribe decoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Decoder },
                        ModelFileCriteria.FromKeywords("decoder", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("decoder"));

                    config.ModelConfig.CohereTranscribe.Encoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Cohere Transcribe encoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Encoder },
                        ModelFileCriteria.FromKeywords("encoder", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("encoder"));
                    config.ModelConfig.CohereTranscribe.Language = GetConfiguredLanguageOrDefault("en");

                    break;
                case SpeechRecognitionModelType.Dolphin:
                    config.ModelConfig.Dolphin.Model = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Dolphin model",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Model },
                        ModelFileCriteria.FromKeywords("model", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("model"));
                    break;

                case SpeechRecognitionModelType.TeleSpeech:
                    config.ModelConfig.TeleSpeechCtc = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "TeleSpeech model",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Model },
                        ModelFileCriteria.FromKeywords("model", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("model"));
                    break;

                case SpeechRecognitionModelType.Whisper:
                    config.ModelConfig.Whisper.Encoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Whisper encoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Encoder },
                        ModelFileCriteria.FromKeywords("encoder", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("encoder"));
                    config.ModelConfig.Whisper.Decoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Whisper decoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Decoder },
                        ModelFileCriteria.FromKeywords("decoder", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("decoder"));
                    config.ModelConfig.Whisper.Language = GetConfiguredLanguageOrDefault(string.Empty);
                    config.ModelConfig.Whisper.Task = "transcribe";
                    break;

                case SpeechRecognitionModelType.Tdnn:
                    config.ModelConfig.Tdnn.Model = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "TDNN model",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Tdnn, SherpaONNXModelFileKey.Model },
                        ModelFileCriteria.FromKeywords("tdnn", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("tdnn"));
                    break;

                case SpeechRecognitionModelType.SenseVoice:

                    config.ModelConfig.SenseVoice.Model = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "SenseVoice model",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Model },
                        ModelFileCriteria.FromKeywords("model", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("model"));
                    config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
                    config.ModelConfig.SenseVoice.Language = GetConfiguredLanguageOrDefault("auto");
                    break;

                case SpeechRecognitionModelType.Moonshine:
                    config.ModelConfig.Moonshine.Encoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Moonshine encoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Encoder },
                        ModelFileCriteria.FromKeywords("encode", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("encode"));
                    var mergedDecoder = ModelFileResolver.ResolveOptionalFileWithBindings(
                        metadata,
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Decoder },
                        ModelFileCriteria.FromKeywords("decoder_model_merged", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("merged", "decoder", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("decoder_model_merged"),
                        ModelFileCriteria.FromKeywords("merged", "decoder"));

                    if (!string.IsNullOrEmpty(mergedDecoder))
                    {
                        config.ModelConfig.Moonshine.MergedDecoder = mergedDecoder;
                        break;
                    }

                    config.ModelConfig.Moonshine.Preprocessor = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Moonshine preprocessor",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Preprocessor },
                        ModelFileCriteria.FromKeywords("preprocess", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("preprocess"));
                    config.ModelConfig.Moonshine.UncachedDecoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Moonshine uncached decoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.UncachedDecoder },
                        ModelFileCriteria.FromKeywords("uncached_decode", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("uncached_decode"));
                    config.ModelConfig.Moonshine.CachedDecoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Moonshine cached decoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.CachedDecoder },
                        ModelFileCriteria.FromKeywords("cached_decode", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("cached_decode"));
                    break;

                case SpeechRecognitionModelType.FireRedAsr:
                    config.ModelConfig.FireRedAsr.Encoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "FireRed ASR encoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Encoder },
                        ModelFileCriteria.FromKeywords("encoder", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("encoder"));
                    config.ModelConfig.FireRedAsr.Decoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "FireRed ASR decoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Decoder },
                        ModelFileCriteria.FromKeywords("decoder", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("decoder"));
                    break;
                case SpeechRecognitionModelType.Offline_FireRedAsrCtc:
                    config.ModelConfig.FireRedAsrCtc.Model = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "FireRed ASR CTC model",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Model },
                        ModelFileCriteria.FromKeywords("model", "ctc", "fire", "red", "asr", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("model", "ctc", "fire", "red", "asr"),
                        ModelFileCriteria.FromKeywords("model", "fire", "red", "asr", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("model", "fire", "red", "asr"),
                        ModelFileCriteria.FromKeywords("model", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("model"));
                    break;
                case SpeechRecognitionModelType.Offline_Canary:
                    config.ModelConfig.Canary.Encoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Canary encoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Encoder },
                        ModelFileCriteria.FromKeywords("encoder", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("encoder"));
                    config.ModelConfig.Canary.Decoder = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Canary decoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Decoder },
                        ModelFileCriteria.FromKeywords("decoder", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("decoder"));
                    config.ModelConfig.Canary.SrcLang = GetConfiguredLanguageOrDefault("en");
                    config.ModelConfig.Canary.TgtLang = GetConfiguredLanguageOrDefault("en");
                    break;
                case SpeechRecognitionModelType.Omnilingual:
                    config.ModelConfig.Omnilingual.Model = ModelFileResolver.ResolveRequiredFileWithBindings(
                        metadata,
                        "Omnilingual ASR encoder",
                        context.FallbackReporter,
                        new[] { SherpaONNXModelFileKey.Model },
                        ModelFileCriteria.FromKeywords("model", context.Int8Keyword),
                        ModelFileCriteria.FromKeywords("model"));
                    break;

                default:
                    throw new NotSupportedException($"Unsupported offline model type: {_modelType}");
            }


            return config;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string GetConfiguredLanguageOrDefault(string fallback)
        {
            return string.IsNullOrWhiteSpace(_options.Language) ? fallback : _options.Language;
        }

        private void ReportRecognizerConfigDiagnostics(
            SherpaONNXModelMetadata metadata,
            SherpaONNXFeedbackReporter reporter,
            int sampleRate,
            RecognizerConfigContext context,
            OnlineRecognizerConfig config)
        {
            var parts = new List<string>(12)
            {
                $"mode=online",
                $"modelType={_modelType}",
                $"sampleRate={sampleRate}",
                $"threads={context.ThreadCount}",
                $"processBits={IntPtr.Size * 8}"
            };

            AppendPathDiagnostic(parts, "tokens", context.TokensPath);

            switch (_modelType)
            {
                case SpeechRecognitionModelType.Online_Paraformer:
                    AppendPathDiagnostic(parts, "encoder", config.ModelConfig.Paraformer.Encoder);
                    AppendPathDiagnostic(parts, "decoder", config.ModelConfig.Paraformer.Decoder);
                    break;
                case SpeechRecognitionModelType.Online_Transducer:
                    AppendPathDiagnostic(parts, "encoder", config.ModelConfig.Transducer.Encoder);
                    AppendPathDiagnostic(parts, "decoder", config.ModelConfig.Transducer.Decoder);
                    AppendPathDiagnostic(parts, "joiner", config.ModelConfig.Transducer.Joiner);
                    break;
                case SpeechRecognitionModelType.Online_Ctc:
                case SpeechRecognitionModelType.Online_Zipformer2Ctc:
                    AppendPathDiagnostic(parts, "model", config.ModelConfig.Zipformer2Ctc.Model);
                    break;
                case SpeechRecognitionModelType.Online_Nemo_Ctc:
                    AppendPathDiagnostic(parts, "model", config.ModelConfig.NemoCtc.Model);
                    break;
                case SpeechRecognitionModelType.Online_Tone_Ctc:
                    AppendPathDiagnostic(parts, "model", config.ModelConfig.ToneCtc.Model);
                    break;
            }

            ReportDiagnosticMessage(metadata, reporter, parts);
        }

        private void ReportRecognizerConfigDiagnostics(
            SherpaONNXModelMetadata metadata,
            SherpaONNXFeedbackReporter reporter,
            int sampleRate,
            RecognizerConfigContext context,
            OfflineRecognizerConfig config)
        {
            var parts = new List<string>(16)
            {
                $"mode=offline",
                $"modelType={_modelType}",
                $"nativeModelType={config.ModelConfig.ModelType}",
                $"sampleRate={sampleRate}",
                $"threads={context.ThreadCount}",
                $"processBits={IntPtr.Size * 8}"
            };

            AppendPathDiagnostic(parts, "tokens", context.TokensPath);

            switch (_modelType)
            {
                case SpeechRecognitionModelType.Offline_Transducer:
                    AppendPathDiagnostic(parts, "encoder", config.ModelConfig.Transducer.Encoder);
                    AppendPathDiagnostic(parts, "decoder", config.ModelConfig.Transducer.Decoder);
                    AppendPathDiagnostic(parts, "joiner", config.ModelConfig.Transducer.Joiner);
                    AppendPathDiagnostic(parts, "hotwords", config.HotwordsFile);
                    break;
                case SpeechRecognitionModelType.Offline_Paraformer:
                    AppendPathDiagnostic(parts, "model", config.ModelConfig.Paraformer.Model);
                    break;
                case SpeechRecognitionModelType.Offline_ZipformerCtc:
                    AppendPathDiagnostic(parts, "model", config.ModelConfig.ZipformerCtc.Model);
                    break;
                case SpeechRecognitionModelType.Offline_Nemo_Ctc:
                    AppendPathDiagnostic(parts, "model", config.ModelConfig.NeMoCtc.Model);
                    break;
                case SpeechRecognitionModelType.Offline_WenetCtc:
                    AppendPathDiagnostic(parts, "model", config.ModelConfig.WenetCtc.Model);
                    break;
                case SpeechRecognitionModelType.Offline_MedAsrCtc:
                    AppendPathDiagnostic(parts, "model", config.ModelConfig.MedAsr.Model);
                    break;
                case SpeechRecognitionModelType.Offline_FunAsrNano:
                    AppendPathDiagnostic(parts, "encoderAdaptor", config.ModelConfig.FunAsrNano.EncoderAdaptor);
                    AppendPathDiagnostic(parts, "llm", config.ModelConfig.FunAsrNano.LLM);
                    AppendPathDiagnostic(parts, "embedding", config.ModelConfig.FunAsrNano.Embedding);
                    AppendPathDiagnostic(parts, "tokenizer", config.ModelConfig.FunAsrNano.Tokenizer);
                    break;
                case SpeechRecognitionModelType.Offline_Qwen3Asr:
                    AppendPathDiagnostic(parts, "convFrontend", config.ModelConfig.Qwen3Asr.ConvFrontend);
                    AppendPathDiagnostic(parts, "encoder", config.ModelConfig.Qwen3Asr.Encoder);
                    AppendPathDiagnostic(parts, "decoder", config.ModelConfig.Qwen3Asr.Decoder);
                    AppendPathDiagnostic(parts, "tokenizer", config.ModelConfig.Qwen3Asr.Tokenizer);
                    break;
                case SpeechRecognitionModelType.Dolphin:
                    AppendPathDiagnostic(parts, "model", config.ModelConfig.Dolphin.Model);
                    break;
                case SpeechRecognitionModelType.TeleSpeech:
                    AppendPathDiagnostic(parts, "model", config.ModelConfig.TeleSpeechCtc);
                    break;
                case SpeechRecognitionModelType.Whisper:
                    AppendPathDiagnostic(parts, "encoder", config.ModelConfig.Whisper.Encoder);
                    AppendPathDiagnostic(parts, "decoder", config.ModelConfig.Whisper.Decoder);
                    break;
                case SpeechRecognitionModelType.Tdnn:
                    AppendPathDiagnostic(parts, "model", config.ModelConfig.Tdnn.Model);
                    break;
                case SpeechRecognitionModelType.SenseVoice:
                    AppendPathDiagnostic(parts, "model", config.ModelConfig.SenseVoice.Model);
                    break;
                case SpeechRecognitionModelType.Moonshine:
                    AppendPathDiagnostic(parts, "encoder", config.ModelConfig.Moonshine.Encoder);
                    AppendPathDiagnostic(parts, "mergedDecoder", config.ModelConfig.Moonshine.MergedDecoder);
                    AppendPathDiagnostic(parts, "preprocessor", config.ModelConfig.Moonshine.Preprocessor);
                    AppendPathDiagnostic(parts, "cachedDecoder", config.ModelConfig.Moonshine.CachedDecoder);
                    AppendPathDiagnostic(parts, "uncachedDecoder", config.ModelConfig.Moonshine.UncachedDecoder);
                    break;
                case SpeechRecognitionModelType.FireRedAsr:
                    AppendPathDiagnostic(parts, "encoder", config.ModelConfig.FireRedAsr.Encoder);
                    AppendPathDiagnostic(parts, "decoder", config.ModelConfig.FireRedAsr.Decoder);
                    break;
                case SpeechRecognitionModelType.Offline_FireRedAsrCtc:
                    AppendPathDiagnostic(parts, "model", config.ModelConfig.FireRedAsrCtc.Model);
                    break;
                case SpeechRecognitionModelType.Offline_Canary:
                    AppendPathDiagnostic(parts, "encoder", config.ModelConfig.Canary.Encoder);
                    AppendPathDiagnostic(parts, "decoder", config.ModelConfig.Canary.Decoder);
                    break;
                case SpeechRecognitionModelType.Omnilingual:
                    AppendPathDiagnostic(parts, "model", config.ModelConfig.Omnilingual.Model);
                    break;
            }

            ReportDiagnosticMessage(metadata, reporter, parts);
        }

        private static void ReportDiagnosticMessage(
            SherpaONNXModelMetadata metadata,
            SherpaONNXFeedbackReporter reporter,
            List<string> parts)
        {
            if (parts == null || parts.Count == 0)
            {
                return;
            }

            var message = "[SpeechRecognition] Recognizer config: " + string.Join(" | ", parts);
            SherpaLog.Info(message, category: "SpeechRecognition");
            reporter?.Report(new LoadFeedback(metadata, message: message));
        }

        private static void AppendPathDiagnostic(List<string> parts, string label, string path)
        {
            if (parts == null || string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (Directory.Exists(path))
            {
                int entryCount = 0;
                try
                {
                    entryCount = Directory.EnumerateFileSystemEntries(path).Take(4).Count();
                }
                catch
                {
                    entryCount = -1;
                }

                parts.Add($"{label}={path} (dir exists=true entries{(entryCount >= 0 ? ">=" + entryCount : "=unknown")})");
                return;
            }

            bool exists = File.Exists(path);
            long size = -1;
            if (exists)
            {
                try
                {
                    size = new FileInfo(path).Length;
                }
                catch
                {
                    size = -1;
                }
            }

            parts.Add($"{label}={path} (exists={exists}{(size >= 0 ? $", size={size}" : string.Empty)})");
        }

        public async Task<TranscriptionResult> TranscribeAsync(float[] audioSamplesFrame, int sampleRate, CancellationToken cancellationToken = default)
        {
            long moduleStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            if (IsDisposed || runner.IsDisposed)
            {
                return new TranscriptionResult(TranscriptionStatus.Disposed);
            }

            if (!Initialized)
            {
                return new TranscriptionResult(TranscriptionStatus.NotReady);
            }

            if (audioSamplesFrame == null || audioSamplesFrame.Length == 0 || sampleRate <= 0)
            {
                return new TranscriptionResult(TranscriptionStatus.NotReady);
            }

            var expectedSampleRate = _modelSampleRate > 0 ? _modelSampleRate : sampleRate;
            if (expectedSampleRate > 0 && sampleRate != expectedSampleRate)
            {
                SherpaLog.Warning($"[{nameof(SpeechRecognition)}] Sample rate mismatch. Expected {expectedSampleRate} Hz for model '{ModelId}', but received {sampleRate} Hz.");
                return new TranscriptionResult(TranscriptionStatus.Error, error: new InvalidOperationException($"Sample rate mismatch: expected {expectedSampleRate} Hz"));
            }

            CancellationTokenSource linkedCts = null;
            bool acquired = false;
            bool countedPending = false;
            TimeSpan moduleSemaphoreWait = TimeSpan.Zero;
            TranscriptionResult result;
            try
            {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                long semaphoreStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                if (_dropIfBusy)
                {
                    acquired = _transcriptionSemaphore.Wait(0);
                    if (!acquired)
                    {
                        return new TranscriptionResult(TranscriptionStatus.Busy);
                    }
                }
                else
                {
                    await _transcriptionSemaphore.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                    acquired = true;
                }
                moduleSemaphoreWait = ElapsedSince(semaphoreStartedAt);

                var pending = Interlocked.Increment(ref _pendingTranscriptions);
                countedPending = true;
                if (_dropIfBusy && pending > _maxPendingTranscriptions)
                {
                    return new TranscriptionResult(TranscriptionStatus.Busy);
                }

                if (IsDisposed || runner.IsDisposed)
                {
                    return new TranscriptionResult(TranscriptionStatus.Disposed);
                }

                result = IsOnlineModel
                    ? await ProcessOnlineTranscriptionAsync(audioSamplesFrame, expectedSampleRate, linkedCts.Token).ConfigureAwait(false)
                    : await ProcessOfflineTranscriptionAsync(audioSamplesFrame, expectedSampleRate, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException oce)
            {
                return new TranscriptionResult(TranscriptionStatus.Cancelled, error: oce);
            }
            catch (Exception ex)
            {
                return new TranscriptionResult(TranscriptionStatus.Error, error: ex);
            }
            finally
            {
                if (acquired)
                {
                    _transcriptionSemaphore.Release();
                }

                if (countedPending)
                {
                    Interlocked.Decrement(ref _pendingTranscriptions);
                }
                linkedCts?.Dispose();
            }

            if (!IsOnlineModel
                && result.Status == TranscriptionStatus.Success
                && result.Timings.IsAvailable)
            {
                result = result.WithTimings(result.Timings.WithModuleBoundary(
                    moduleSemaphoreWait,
                    ElapsedSince(moduleStartedAt)));
            }

            return result;
        }

        public async Task<string> SpeechTranscriptionAsync(float[] audioSamplesFrame, int sampleRate, CancellationToken cancellationToken = default)
        {
            var result = await TranscribeAsync(audioSamplesFrame, sampleRate, cancellationToken).ConfigureAwait(false);
            switch (result.Status)
            {
                case TranscriptionStatus.Success:
                    return result.Text ?? string.Empty;
                case TranscriptionStatus.Cancelled:
                    throw result.Error as OperationCanceledException ?? new OperationCanceledException("Transcription was cancelled.", result.Error, cancellationToken);
                case TranscriptionStatus.Error:
                    if (result.Error != null)
                    {
                        throw result.Error;
                    }
                    throw new InvalidOperationException("Transcription failed for an unknown reason.");
                case TranscriptionStatus.Busy:
                    SherpaLog.Warning($"[{nameof(SpeechRecognition)}] Dropped transcription request because the recognizer is busy.");
                    return string.Empty;
                case TranscriptionStatus.NotReady:
                case TranscriptionStatus.Disposed:
                default:
                    return string.Empty;
            }
        }

        public bool TrySetOnlineStreamOption(string key, string value)
        {
            if (!IsOnlineModel || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            lock (_lockObject)
            {
                if (IsDisposed || _onlineStream == null)
                {
                    return false;
                }

                SetOnlineStreamOptionCore(key, value);
                return true;
            }
        }

        public bool TryGetOnlineStreamOption(string key, out string value)
        {
            value = string.Empty;
            if (!IsOnlineModel || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            lock (_lockObject)
            {
                if (IsDisposed || _onlineStream == null)
                {
                    return false;
                }

                value = _onlineStream.GetOption(key);
                return true;
            }
        }

        public bool HasOnlineStreamOption(string key)
        {
            if (!IsOnlineModel || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            lock (_lockObject)
            {
                if (IsDisposed || _onlineStream == null)
                {
                    return false;
                }

                return _onlineStream.HasOption(key);
            }
        }

        public bool TrySetParaformerIsFinal(bool isFinal)
        {
            if (_modelType != SpeechRecognitionModelType.Online_Paraformer)
            {
                return false;
            }

            return TrySetOnlineStreamOption("is_final", isFinal ? "true" : "false");
        }

        private Task<TranscriptionResult> ProcessOnlineTranscriptionAsync(float[] audioSamplesFrame, int sampleRate, CancellationToken cancellationToken)
        {
            if (_onlineRecognizer == null || _onlineStream == null)
            {
                return Task.FromResult(new TranscriptionResult(TranscriptionStatus.NotReady));
            }

            lock (_lockObject)
            {
                if (IsDisposed || _onlineStream == null) { return Task.FromResult(new TranscriptionResult(TranscriptionStatus.Disposed)); }

                ApplyParaformerFinalState(isFinal: false);
                _onlineStream.AcceptWaveform(sampleRate, audioSamplesFrame);
            }

            return runner.RunAsync<TranscriptionResult>(ct =>
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
                var combinedCt = linkedCts.Token;

                if (IsDisposed || _onlineRecognizer == null || _onlineStream == null)
                {
                    return Task.FromResult(new TranscriptionResult(TranscriptionStatus.Disposed));
                }

                lock (_lockObject)
                {
                    if (IsDisposed || _onlineStream == null) { return Task.FromResult(new TranscriptionResult(TranscriptionStatus.Disposed)); }

                    var isFinal = false;
                    DecodeOnlineStream(combinedCt);
                    var result = _onlineRecognizer.GetResult(_onlineStream);

                    if (_onlineRecognizer.IsEndpoint(_onlineStream))
                    {
                        isFinal = true;
                        ApplyParaformerFinalState(isFinal: true);
                        HandleEndpointDetection(sampleRate, combinedCt);
                        _onlineStream.InputFinished();
                        DecodeOnlineStream(combinedCt);
                        result = _onlineRecognizer.GetResult(_onlineStream);
                        _onlineRecognizer.Reset(_onlineStream);
                        ApplyParaformerFinalState(isFinal: false);
                    }

                    var text = result?.Text ?? string.Empty;
                    var tokens = result?.Tokens ?? Array.Empty<string>();
                    var timestamps = result?.Timestamps ?? Array.Empty<float>();
                    var cased = PostProcessCasing(text);
                    return Task.FromResult(new TranscriptionResult(TranscriptionStatus.Success, cased, isFinal, tokens: tokens, timestamps: timestamps));
                }
            });
        }

        private RecognizerConfigContext BuildConfigContext(SherpaONNXModelMetadata metadata, int sampleRate, bool isMobilePlatform, SherpaONNXFeedbackReporter reporter)
        {
            var fallbackReporter = CreateFallbackReporter(metadata, reporter);
            var threadCount = ThreadingUtils.GetAdaptiveThreadCount();
            var int8QuantKeyword = isMobilePlatform ? "int8" : null;
            string tokensPath;
            if (_modelType == SpeechRecognitionModelType.Offline_FunAsrNano
                || _modelType == SpeechRecognitionModelType.Offline_Qwen3Asr)
            {   // Some decoder-based offline models use a tokenizer directory instead of tokens.txt.
                tokensPath = string.Empty;
            }
            else
            {
                tokensPath = ModelFileResolver.ResolveRequiredFileWithBindings(
                    metadata,
                    "token file",
                    fallbackReporter,
                    new[] { SherpaONNXModelFileKey.Tokens },
                    ModelFileCriteria.FromKeywords("tokens", "tokens.txt"));
            }

            return new RecognizerConfigContext(threadCount, tokensPath, int8QuantKeyword, fallbackReporter);
        }

        private static string ToNativeProvider(SherpaONNXExecutionProvider provider)
        {
            return provider == SherpaONNXExecutionProvider.Cuda ? "cuda" : "cpu";
        }

        private void EnsureExecutionProviderAvailable()
        {
            if (ExecutionProvider != SherpaONNXExecutionProvider.Cuda)
            {
                _cudaProviderDiagnostics = SherpaCudaRuntimeDiagnostics.CreateNotApplicable(ExecutionProvider);
                return;
            }

            _cudaProviderDiagnostics = SherpaCudaRuntimeDiagnostics.CaptureSystemDependencies();
            if (!_cudaProviderDiagnostics.IsPassed)
            {
                throw new InvalidOperationException(_cudaProviderDiagnostics.Message);
            }
        }

        private void WarmUpOnlineRecognizer(int sampleRate, CancellationToken cancellationToken)
        {
            var warmupTimer = System.Diagnostics.Stopwatch.StartNew();
            using (var warmupStream = _onlineRecognizer.CreateStream())
            {
                cancellationToken.ThrowIfCancellationRequested();
                warmupStream.AcceptWaveform(sampleRate, new float[Math.Max(sampleRate, 16000)]);
                warmupStream.InputFinished();
                while (_onlineRecognizer.IsReady(warmupStream))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _onlineRecognizer.Decode(warmupStream);
                }
            }

            WarmUpDuration = warmupTimer.Elapsed;
            WasWarmedUp = true;
        }

        private void WarmUpOfflineRecognizer(int sampleRate, CancellationToken cancellationToken)
        {
            var warmupTimer = System.Diagnostics.Stopwatch.StartNew();
            using (var warmupStream = _offlineRecognizer.CreateStream())
            {
                cancellationToken.ThrowIfCancellationRequested();
                warmupStream.AcceptWaveform(sampleRate, new float[Math.Max(sampleRate, 16000)]);
                _offlineRecognizer.Decode(warmupStream);
                _ = warmupStream.Result;
            }

            WarmUpDuration = warmupTimer.Elapsed;
            WasWarmedUp = true;
        }

        private void ApplyParaformerFinalState(bool isFinal)
        {
            if (_modelType != SpeechRecognitionModelType.Online_Paraformer || _onlineStream == null)
            {
                return;
            }

            SetOnlineStreamOptionCore("is_final", isFinal ? "true" : "false");
        }

        private void SetOnlineStreamOptionCore(string key, string value)
        {
            _onlineStream?.SetOption(key, value ?? string.Empty);
        }

        private Task<TranscriptionResult> ProcessOfflineTranscriptionAsync(float[] audioSamplesFrame, int sampleRate, CancellationToken cancellationToken)
        {
            if (_offlineRecognizer == null)
            {
                return Task.FromResult(new TranscriptionResult(TranscriptionStatus.NotReady));
            }

            long dispatchStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            return runner.RunAsync<TranscriptionResult>(ct =>
            {
                long workerStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                TimeSpan workerDispatchWait = Elapsed(dispatchStartedAt, workerStartedAt);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken);
                var combinedCt = linkedCts.Token;

                if (IsDisposed || _offlineRecognizer == null)
                {
                    return Task.FromResult(new TranscriptionResult(TranscriptionStatus.Disposed));
                }

                string text;
                string[] tokens;
                float[] timestamps;
                float[] durations;
                TimeSpan streamCreate = TimeSpan.Zero;
                TimeSpan acceptWaveform = TimeSpan.Zero;
                TimeSpan offlineDecodeCall = TimeSpan.Zero;
                TimeSpan resultMaterialization = TimeSpan.Zero;
                TimeSpan postProcessing = TimeSpan.Zero;
                TimeSpan streamDispose = TimeSpan.Zero;
                OfflineStream offlineStream = null;
                try
                {
                    long stageStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                    offlineStream = _offlineRecognizer.CreateStream();
                    streamCreate = ElapsedSince(stageStartedAt);

                    stageStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                    offlineStream.AcceptWaveform(sampleRate, audioSamplesFrame);
                    acceptWaveform = ElapsedSince(stageStartedAt);

                    combinedCt.ThrowIfCancellationRequested();

                    stageStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                    _offlineRecognizer.Decode(offlineStream);
                    offlineDecodeCall = ElapsedSince(stageStartedAt);

                    // Native offline Decode is not interruptible. Honor cancellation as soon as
                    // control returns, before materializing or publishing a stale result.
                    combinedCt.ThrowIfCancellationRequested();

                    stageStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                    var nativeResult = offlineStream.Result;
                    text = nativeResult.Text;
                    tokens = nativeResult.Tokens;
                    timestamps = nativeResult.Timestamps;
                    durations = nativeResult.Durations;
                    resultMaterialization = ElapsedSince(stageStartedAt);

                    stageStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                    text = PostProcessCasing(text);
                    postProcessing = ElapsedSince(stageStartedAt);
                }
                finally
                {
                    if (offlineStream != null)
                    {
                        long disposeStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                        offlineStream.Dispose();
                        streamDispose = ElapsedSince(disposeStartedAt);
                    }
                }

                var timings = new TranscriptionTimings(
                    moduleSemaphoreWait: TimeSpan.Zero,
                    workerDispatchWait,
                    streamCreate,
                    acceptWaveform,
                    offlineDecodeCall,
                    resultMaterialization,
                    postProcessing,
                    streamDispose,
                    workerTotal: ElapsedSince(workerStartedAt),
                    moduleTotal: TimeSpan.Zero);
                return Task.FromResult(new TranscriptionResult(
                    TranscriptionStatus.Success,
                    text,
                    isFinal: true,
                    tokens: tokens,
                    timestamps: timestamps,
                    durations: durations,
                    timings: timings));
            });
        }

        private static TimeSpan ElapsedSince(long startedAt)
        {
            return Elapsed(startedAt, System.Diagnostics.Stopwatch.GetTimestamp());
        }

        private static TimeSpan Elapsed(long startedAt, long endedAt)
        {
            long elapsedTicks = Math.Max(0L, endedAt - startedAt);
            return TimeSpan.FromSeconds(
                elapsedTicks / (double)System.Diagnostics.Stopwatch.Frequency);
        }

        private void DecodeOnlineStream(CancellationToken cancellationToken)
        {
            while (!IsDisposed && _onlineRecognizer != null && _onlineStream != null && _onlineRecognizer.IsReady(_onlineStream))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _onlineRecognizer.Decode(_onlineStream);
            }
        }

        private void HandleEndpointDetection(int sampleRate, CancellationToken cancellationToken)
        {
            if (IsDisposed || _onlineStream == null) { return; }

            var tailPadding = EnsureEndpointPaddingBuffer(sampleRate);
            _onlineStream.AcceptWaveform(sampleRate, tailPadding);

            DecodeOnlineStream(cancellationToken);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float[] EnsureEndpointPaddingBuffer(int sampleRate)
        {
            if (_endpointPaddingBuffer == null || _endpointPaddingSampleRate != sampleRate || _endpointPaddingBuffer.Length < sampleRate)
            {
                _endpointPaddingBuffer = new float[sampleRate];
                _endpointPaddingSampleRate = sampleRate;
            }

            return _endpointPaddingBuffer;
        }

        // --- English sentence casing post-processor (fast + safe for mixed languages) ---
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasAsciiLetter(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }


            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                // Fast bounds check: map to uint to avoid branch mispredictions
                if ((uint)(c - 'A') <= ('Z' - 'A') || (uint)(c - 'a') <= ('z' - 'a'))
                {

                    return true;
                }

            }
            return false;
        }

        /// <summary>
        /// Apply English sentence casing only when the text contains ASCII letters.
        /// /// Non-English scripts (CJK, etc.) are returned unchanged. Mixed content is safe:
        /// non-Latin characters are unaffected by the caser.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string PostProcessCasing(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            // If the text has no ASCII letters, skip casing to avoid touching other languages.

            if (!HasAsciiLetter(text))
            {
                return text;
            }

            // Delegate to the high-performance caser (handles punctuation, acronyms, phrases, etc.)

            return EnglishSentenceCaser.ToSentenceCase(text);
        }

        protected override void OnDestroy()
        {
            lock (_lockObject)
            {
                _onlineStream?.Dispose();
                _onlineRecognizer?.Dispose();
                _offlineRecognizer?.Dispose();

                _onlineStream = null;
                _onlineRecognizer = null;
                _offlineRecognizer = null;
            }
            _transcriptionSemaphore.Dispose();
        }
    }
}
