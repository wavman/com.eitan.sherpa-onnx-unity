using System;
using System.Collections.Generic;

namespace Eitan.SherpaONNXUnity.Runtime.Modules
{
    internal enum SpeechRecognitionFileBindingPolicy
    {
        CanonicalFallbackAllowed,
        ExactRequired
    }

    internal sealed class SpeechRecognitionRuntimeProfile
    {
        public SpeechRecognitionRuntimeProfile(
            string id,
            SpeechRecognitionModelType modelType,
            SherpaONNXSpeechRecognitionRuntimeFamily runtimeFamily,
            bool isOnline,
            string decodingMethod,
            SpeechRecognitionFileBindingPolicy bindingPolicy,
            params SherpaONNXModelFileRequirement[] requiredFiles)
        {
            Id = id;
            ModelType = modelType;
            RuntimeFamily = runtimeFamily;
            IsOnline = isOnline;
            DecodingMethod = decodingMethod;
            BindingPolicy = bindingPolicy;
            RequiredFiles = Array.AsReadOnly(
                requiredFiles ?? Array.Empty<SherpaONNXModelFileRequirement>());
        }

        public string Id { get; }
        public SpeechRecognitionModelType ModelType { get; }
        public SherpaONNXSpeechRecognitionRuntimeFamily RuntimeFamily { get; }
        public bool IsOnline { get; }
        public string DecodingMethod { get; }
        public SpeechRecognitionFileBindingPolicy BindingPolicy { get; }
        public IReadOnlyList<SherpaONNXModelFileRequirement> RequiredFiles { get; }
    }

    internal static class SpeechRecognitionRuntimeProfileRegistry
    {
        internal const string OnlineZipformerTransducer = "sherpa.online.zipformer-transducer.v1";
        internal const string OnlineNemoTransducer = "sherpa.online.nemo-transducer.v1";
        internal const string OfflineQwen3Asr = "sherpa.offline.qwen3-asr.v1";
        internal const string OfflineQwen3AsrExternalData = "sherpa.offline.qwen3-asr.external-data.v1";
        internal const string OfflineFunAsrNano = "sherpa.offline.funasr-nano.v1";
        internal const string OfflineFireRedAsr = "sherpa.offline.fire-red-asr.v1";

        private static readonly IReadOnlyDictionary<string, SpeechRecognitionRuntimeProfile> Profiles =
            new Dictionary<string, SpeechRecognitionRuntimeProfile>(StringComparer.Ordinal)
            {
                [OnlineZipformerTransducer] = new SpeechRecognitionRuntimeProfile(
                    OnlineZipformerTransducer,
                    SpeechRecognitionModelType.Online_Transducer,
                    SherpaONNXSpeechRecognitionRuntimeFamily.ZipformerTransducer,
                    true,
                    "modified_beam_search",
                    SpeechRecognitionFileBindingPolicy.CanonicalFallbackAllowed,
                    File(SherpaONNXModelFileKey.Encoder),
                    File(SherpaONNXModelFileKey.Decoder),
                    File(SherpaONNXModelFileKey.Joiner),
                    File(SherpaONNXModelFileKey.Tokens)),
                [OnlineNemoTransducer] = new SpeechRecognitionRuntimeProfile(
                    OnlineNemoTransducer,
                    SpeechRecognitionModelType.Online_Transducer,
                    SherpaONNXSpeechRecognitionRuntimeFamily.NemoTransducer,
                    true,
                    "greedy_search",
                    SpeechRecognitionFileBindingPolicy.ExactRequired,
                    File(SherpaONNXModelFileKey.Encoder),
                    File(SherpaONNXModelFileKey.Decoder),
                    File(SherpaONNXModelFileKey.Joiner),
                    File(SherpaONNXModelFileKey.Tokens)),
                [OfflineQwen3Asr] = new SpeechRecognitionRuntimeProfile(
                    OfflineQwen3Asr,
                    SpeechRecognitionModelType.Offline_Qwen3Asr,
                    SherpaONNXSpeechRecognitionRuntimeFamily.OtherSupported,
                    false,
                    "greedy_search",
                    SpeechRecognitionFileBindingPolicy.ExactRequired,
                    File(SherpaONNXModelFileKey.ConvFrontend),
                    File(SherpaONNXModelFileKey.Encoder),
                    File(SherpaONNXModelFileKey.Decoder),
                    Directory(SherpaONNXModelFileKey.Tokenizer)),
                [OfflineQwen3AsrExternalData] = new SpeechRecognitionRuntimeProfile(
                    OfflineQwen3AsrExternalData,
                    SpeechRecognitionModelType.Offline_Qwen3Asr,
                    SherpaONNXSpeechRecognitionRuntimeFamily.OtherSupported,
                    false,
                    "greedy_search",
                    SpeechRecognitionFileBindingPolicy.ExactRequired,
                    File(SherpaONNXModelFileKey.ConvFrontend),
                    File(SherpaONNXModelFileKey.Encoder),
                    File(SherpaONNXModelFileKey.EncoderExternalData),
                    File(SherpaONNXModelFileKey.Decoder),
                    File(SherpaONNXModelFileKey.DecoderExternalData),
                    Directory(SherpaONNXModelFileKey.Tokenizer)),
                [OfflineFunAsrNano] = new SpeechRecognitionRuntimeProfile(
                    OfflineFunAsrNano,
                    SpeechRecognitionModelType.Offline_FunAsrNano,
                    SherpaONNXSpeechRecognitionRuntimeFamily.OtherSupported,
                    false,
                    "greedy_search",
                    SpeechRecognitionFileBindingPolicy.ExactRequired,
                    File(SherpaONNXModelFileKey.EncoderAdaptor),
                    File(SherpaONNXModelFileKey.Llm),
                    File(SherpaONNXModelFileKey.Embedding),
                    Directory(SherpaONNXModelFileKey.Tokenizer)),
                [OfflineFireRedAsr] = new SpeechRecognitionRuntimeProfile(
                    OfflineFireRedAsr,
                    SpeechRecognitionModelType.FireRedAsr,
                    SherpaONNXSpeechRecognitionRuntimeFamily.OtherSupported,
                    false,
                    "greedy_search",
                    SpeechRecognitionFileBindingPolicy.ExactRequired,
                    File(SherpaONNXModelFileKey.Encoder),
                    File(SherpaONNXModelFileKey.Decoder),
                    File(SherpaONNXModelFileKey.Tokens))
            };

        internal static bool TryGet(string id, out SpeechRecognitionRuntimeProfile profile)
        {
            return Profiles.TryGetValue(id ?? string.Empty, out profile);
        }

        private static SherpaONNXModelFileRequirement File(SherpaONNXModelFileKey key) =>
            new SherpaONNXModelFileRequirement(key, SherpaONNXModelFileKind.File);

        private static SherpaONNXModelFileRequirement Directory(SherpaONNXModelFileKey key) =>
            new SherpaONNXModelFileRequirement(key, SherpaONNXModelFileKind.Directory);
    }
}
