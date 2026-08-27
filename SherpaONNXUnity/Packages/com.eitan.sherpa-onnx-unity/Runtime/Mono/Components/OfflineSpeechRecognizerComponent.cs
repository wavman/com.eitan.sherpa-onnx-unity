// Runtime: Packages/com.eitan.sherpa-onnx-unity/Runtime/Mono/Components/OfflineSpeechRecognizerComponent.cs

namespace Eitan.Sherpa.Onnx.Unity.Mono.Components
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Eitan.SherpaONNXUnity.Runtime;
    using Eitan.SherpaONNXUnity.Runtime.Modules;
    using Eitan.SherpaONNXUnity.Runtime.Utilities;
    using UnityEngine;
    using UnityEngine.Events;
    using UnityEngine.Serialization;

    [AddComponentMenu("SherpaONNX/Speech Recognition/Offline Speech Recognizer")]
    [DisallowMultipleComponent]
    public sealed class OfflineSpeechRecognizerComponent : SherpaModuleComponent<SpeechRecognition>
    {
        [SerializeField] private VoiceActivityDetectionComponent voiceActivitySource;
        [SerializeField] private bool autoBindVoiceActivitySource = true;
        [SerializeField] private bool startModuleImmediately = true;
        [SerializeField] private SherpaONNXExecutionProvider executionProvider = SherpaONNXExecutionProvider.Cpu;
        [SerializeField] private bool warmUpOnInitialization = true;
        [SerializeField, HideInInspector, FormerlySerializedAs("language")] private string recognitionLanguage = string.Empty;
        [SerializeField] private int maxPendingSegments = 32;
        [SerializeField] private UnityEvent<SpeechRecognition.TranscriptionResult> onTranscriptReady = new UnityEvent<SpeechRecognition.TranscriptionResult>();
        [SerializeField] private UnityEvent<string> onTranscriptionFailed = new UnityEvent<string>();

        public UnityEvent<SpeechRecognition.TranscriptionResult> TranscriptReadyEvent => onTranscriptReady;
        public UnityEvent<string> TranscriptionFailedEvent => onTranscriptionFailed;
        public string RecognitionLanguage { get => recognitionLanguage; set => recognitionLanguage = value ?? string.Empty; }
        public SherpaONNXExecutionProvider ExecutionProvider { get => executionProvider; set => executionProvider = value; }
        public bool WarmUpOnInitialization { get => warmUpOnInitialization; set => warmUpOnInitialization = value; }
        public TimeSpan ModelLoadDuration => Module?.ModelLoadDuration ?? TimeSpan.Zero;
        public TimeSpan WarmUpDuration => Module?.WarmUpDuration ?? TimeSpan.Zero;
        public bool WasWarmedUp => Module?.WasWarmedUp ?? false;
        public SherpaCudaProviderDiagnostics CudaProviderDiagnostics =>
            Module?.CudaProviderDiagnostics
            ?? SherpaCudaRuntimeDiagnostics.CreateNotApplicable(executionProvider);
        public int PendingSegmentCount { get { lock (queueLock) return pendingSegments.Count; } }
        public int ActiveTranscriptionCount => Volatile.Read(ref activeTranscriptionCount);
        public int DroppedSegmentCount => Volatile.Read(ref droppedSegmentCount);
        public int BusySegmentCount => Volatile.Read(ref busySegmentCount);

        private readonly Queue<AudioChunk> pendingSegments = new Queue<AudioChunk>();
        private readonly object queueLock = new object();
        private readonly object requestLock = new object();
        private CancellationTokenSource processingCts;
        private VoiceActivityDetectionComponent boundSource;
        private Task queueDrainTask = Task.CompletedTask;
        private Task<SpeechRecognition.TranscriptionResult> activeTranscriptionTask;
        private Task<SpeechRecognition.TranscriptionResult> activeTranscriptionCompletionTask;
        private CancellationTokenSource activeTranscriptionCts;
        private bool drainingQueue;
        private int activeTranscriptionCount;
        private int droppedSegmentCount;
        private int busySegmentCount;
        private float lastDropLog;

        protected override SpeechRecognition CreateModule(string resolvedModelId, int resolvedSampleRate, SherpaONNXFeedbackReporter resolvedReporter)
        {
            var options = new SpeechRecognition.Options
            {
                Language = recognitionLanguage,
                ExecutionProvider = executionProvider,
                WarmUpOnInitialization = warmUpOnInitialization,
                ModeRequirement = SherpaONNXSpeechRecognitionModeRequirement.Offline
            };
            return new SpeechRecognition(resolvedModelId, resolvedSampleRate, resolvedReporter, startImmediately: startModuleImmediately, options: options);
        }

        private void OnEnable()
        {
            processingCts = new CancellationTokenSource();
            if (Application.isPlaying && autoBindVoiceActivitySource) BindVoiceActivitySource(voiceActivitySource);
        }

        private void OnDisable()
        {
            UnbindVoiceActivitySource(boundSource);
            _ = CancelAndDrainAsync();
        }

        public void BindVoiceActivitySource(VoiceActivityDetectionComponent source)
        {
            if (boundSource == source) return;
            UnbindVoiceActivitySource(boundSource);
            if (source == null) return;
            source.SpeechSegmentReady += HandleSpeechSegment;
            boundSource = source;
        }

        public void UnbindVoiceActivitySource(VoiceActivityDetectionComponent source)
        {
            if (source == null) return;
            source.SpeechSegmentReady -= HandleSpeechSegment;
            if (boundSource == source) boundSource = null;
        }

        public void FeedSegment(float[] samples, int sampleRate) => HandleSpeechSegment(samples, sampleRate);

        public Task StartModuleInitializationAsync(CancellationToken cancellationToken = default)
        {
            if (Module == null && !TryLoadModule())
            {
                RaiseError("Failed to load offline speech recognizer module.");
                return Task.CompletedTask;
            }
            return Module?.StartInitialization(cancellationToken) ?? Task.CompletedTask;
        }

        public async Task<SpeechRecognition.TranscriptionResult> TranscribeClipAsync(AudioClip clip, CancellationToken cancellationToken = default)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            var data = new float[clip.samples * clip.channels];
            clip.GetData(data, 0);
            return await TranscribeSamplesAsync(DownmixToMono(data, clip.channels), clip.frequency, cancellationToken).ConfigureAwait(false);
        }

        public Task<SpeechRecognition.TranscriptionResult> TranscribeSamplesAsync(float[] samples, int sampleRate, CancellationToken cancellationToken = default)
        {
            if (samples == null || samples.Length == 0 || sampleRate <= 0)
                return Task.FromResult(new SpeechRecognition.TranscriptionResult(SpeechRecognition.TranscriptionStatus.NotReady));
            lock (requestLock)
            {
                if (activeTranscriptionCount != 0)
                {
                    Interlocked.Increment(ref busySegmentCount);
                    return Task.FromResult(new SpeechRecognition.TranscriptionResult(SpeechRecognition.TranscriptionStatus.Busy));
                }
                Interlocked.Increment(ref activeTranscriptionCount);
                activeTranscriptionCts = CancellationTokenSource.CreateLinkedTokenSource(
                    processingCts?.Token ?? CancellationToken.None,
                    cancellationToken);
                activeTranscriptionTask = TranscribeSamplesCoreAsync(
                    samples,
                    sampleRate,
                    activeTranscriptionCts.Token);
                activeTranscriptionCompletionTask = ObserveTranscriptionAsync(activeTranscriptionTask);
                return activeTranscriptionCompletionTask;
            }
        }

        private async Task<SpeechRecognition.TranscriptionResult> ObserveTranscriptionAsync(
            Task<SpeechRecognition.TranscriptionResult> task)
        {
            try { return await task.ConfigureAwait(false); }
            finally
            {
                lock (requestLock)
                {
                    Interlocked.Decrement(ref activeTranscriptionCount);
                    activeTranscriptionTask = null;
                    activeTranscriptionCompletionTask = null;
                    activeTranscriptionCts?.Dispose();
                    activeTranscriptionCts = null;
                }
            }
        }

        private async Task<SpeechRecognition.TranscriptionResult> TranscribeSamplesCoreAsync(float[] samples, int sampleRate, CancellationToken cancellationToken)
        {
            if (!EnsureModuleReady(out SpeechRecognition module))
                return new SpeechRecognition.TranscriptionResult(SpeechRecognition.TranscriptionStatus.NotReady);
            try
            {
                SpeechRecognition.TranscriptionResult result = await module.TranscribeAsync(samples, sampleRate, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }
            catch (OperationCanceledException ex)
            {
                return new SpeechRecognition.TranscriptionResult(SpeechRecognition.TranscriptionStatus.Cancelled, error: ex);
            }
            catch (Exception ex)
            {
                return new SpeechRecognition.TranscriptionResult(SpeechRecognition.TranscriptionStatus.Error, error: ex);
            }
        }

        public async Task<bool> WarmUpAsync(CancellationToken cancellationToken = default)
        {
            if (Module == null || !IsInitialized) return false;
            return await Module.WarmUpAsync(cancellationToken).ConfigureAwait(false);
        }

        public SherpaCudaProviderDiagnostics RefreshCudaProviderDiagnostics(
            SherpaCudaProviderDiagnosticStage stage = SherpaCudaProviderDiagnosticStage.PostDecode)
        {
            if (Module == null)
            {
                return CudaProviderDiagnostics;
            }

            return Module.RefreshCudaProviderDiagnostics(stage);
        }

        public void ClearPendingSegments()
        {
            lock (queueLock) pendingSegments.Clear();
        }

        public async Task CancelAndDrainAsync(CancellationToken cancellationToken = default)
        {
            processingCts?.Cancel();
            Task queueTask;
            Task transcriptionTask;
            Task transcriptionCompletionTask;
            lock (queueLock)
            {
                pendingSegments.Clear();
                queueTask = queueDrainTask;
            }
            lock (requestLock)
            {
                transcriptionTask = activeTranscriptionTask;
                transcriptionCompletionTask = activeTranscriptionCompletionTask;
            }
            await Task.WhenAll(
                AwaitIgnoringFailure(queueTask),
                AwaitIgnoringFailure(transcriptionTask),
                AwaitIgnoringFailure(transcriptionCompletionTask)).ConfigureAwait(false);
            ClearPendingSegments();
            CancellationTokenSource old = processingCts;
            processingCts = null;
            old?.Dispose();
            if (isActiveAndEnabled) processingCts = new CancellationTokenSource();
            cancellationToken.ThrowIfCancellationRequested();
        }

        public new async Task DisposeModuleAsync()
        {
            await CancelAndDrainAsync().ConfigureAwait(false);
            await base.DisposeModuleAsync().ConfigureAwait(false);
        }

        private static async Task AwaitIgnoringFailure(Task task)
        {
            if (task == null) return;
            try { await task.ConfigureAwait(false); } catch { }
        }

        private void HandleSpeechSegment(float[] samples, int sampleRate)
        {
            if (samples == null || samples.Length == 0) return;
            var buffer = new float[samples.Length];
            Array.Copy(samples, buffer, samples.Length);
            EnqueueWithBackPressure(new AudioChunk(buffer, sampleRate));
        }

        private async Task DrainQueueAsync()
        {
            try
            {
                while (true)
                {
                    if (processingCts == null || processingCts.IsCancellationRequested) return;
                    AudioChunk chunk;
                    lock (queueLock)
                    {
                        if (pendingSegments.Count == 0) return;
                        chunk = pendingSegments.Dequeue();
                    }
                    await TranscribeChunkAsync(chunk).ConfigureAwait(false);
                }
            }
            finally
            {
                lock (queueLock) drainingQueue = false;
            }
        }

        private async Task TranscribeChunkAsync(AudioChunk chunk)
        {
            SpeechRecognition.TranscriptionResult result = await TranscribeSamplesAsync(chunk.Samples, chunk.SampleRate, processingCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            if (result.Status == SpeechRecognition.TranscriptionStatus.Success && !string.IsNullOrWhiteSpace(result.Text))
                DispatchToUnity(() => onTranscriptReady?.Invoke(result));
            else if (result.Status == SpeechRecognition.TranscriptionStatus.Error && result.Error != null)
            {
                string message = result.Error.Message;
                DispatchToUnity(() => onTranscriptionFailed?.Invoke(message));
                RaiseError(message);
            }
        }

        private void EnqueueWithBackPressure(AudioChunk chunk)
        {
            bool dropped = false;
            lock (queueLock)
            {
                if (maxPendingSegments > 0 && pendingSegments.Count >= maxPendingSegments)
                {
                    pendingSegments.Dequeue();
                    Interlocked.Increment(ref droppedSegmentCount);
                    dropped = true;
                }
                pendingSegments.Enqueue(chunk);
                if (!drainingQueue)
                {
                    drainingQueue = true;
                    queueDrainTask = DrainQueueAsync();
                }
            }
            if (dropped && Time.realtimeSinceStartup - lastDropLog >= 1f)
            {
                lastDropLog = Time.realtimeSinceStartup;
                SherpaLog.Warning($"[{nameof(OfflineSpeechRecognizerComponent)}] Dropped pending segment(s) due to back-pressure.");
            }
        }

        private static float[] DownmixToMono(float[] data, int channels)
        {
            if (data == null) return Array.Empty<float>();
            if (channels <= 1)
            {
                var clone = new float[data.Length];
                Array.Copy(data, clone, data.Length);
                return clone;
            }
            int frameCount = data.Length / channels;
            var mono = new float[frameCount];
            for (int frame = 0; frame < frameCount; frame++)
            {
                float sum = 0f;
                int offset = frame * channels;
                for (int channel = 0; channel < channels; channel++) sum += data[offset + channel];
                mono[frame] = sum / channels;
            }
            return mono;
        }

        private readonly struct AudioChunk
        {
            public AudioChunk(float[] samples, int sampleRate) { Samples = samples; SampleRate = sampleRate; }
            public float[] Samples { get; }
            public int SampleRate { get; }
        }
    }
}
