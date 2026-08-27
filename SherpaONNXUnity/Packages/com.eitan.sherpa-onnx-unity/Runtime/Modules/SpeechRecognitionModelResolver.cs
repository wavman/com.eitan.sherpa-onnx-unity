using System;
using System.Collections.Generic;
using System.Linq;
using Eitan.SherpaONNXUnity.Runtime.Utilities;

namespace Eitan.SherpaONNXUnity.Runtime.Modules
{
    internal static class SpeechRecognitionModelResolver
    {
        internal static SherpaONNXSpeechRecognitionModelSpec Resolve(
            string requestedModelId,
            SherpaONNXModelMetadata metadata,
            SherpaONNXSpeechRecognitionModeRequirement modeRequirement = SherpaONNXSpeechRecognitionModeRequirement.Any)
        {
            string modelId = requestedModelId?.Trim() ?? string.Empty;
            if (metadata == null)
            {
                return SherpaONNXSpeechRecognitionModelSpec.Failure(
                    modelId,
                    null,
                    SherpaONNXModelResolutionStatus.Unregistered,
                    $"No Model Definition is registered for speech-recognition model '{modelId}'.");
            }

            if (!metadata.hasModelDefinition)
            {
                return SherpaONNXSpeechRecognitionModelSpec.Failure(
                    modelId,
                    metadata,
                    SherpaONNXModelResolutionStatus.Unregistered,
                    $"Model '{modelId}' has only a Distribution Record and cannot enter runtime initialization.");
            }

            if (metadata.moduleType != SherpaONNXModuleType.SpeechRecognition)
            {
                return SherpaONNXSpeechRecognitionModelSpec.Failure(
                    modelId,
                    metadata,
                    SherpaONNXModelResolutionStatus.ModuleMismatch,
                    $"Model '{modelId}' is registered for {metadata.moduleType}, not SpeechRecognition.");
            }

            if (!string.IsNullOrEmpty(metadata.runtimeProfileId))
            {
                if (!SpeechRecognitionRuntimeProfileRegistry.TryGet(metadata.runtimeProfileId, out var profile))
                {
                    return SherpaONNXSpeechRecognitionModelSpec.Failure(
                        modelId,
                        metadata,
                        SherpaONNXModelResolutionStatus.UnsupportedTopology,
                        $"The registered runtime profile '{metadata.runtimeProfileId}' is unavailable.");
                }
                if (!ValidateRuntimeProfileBindings(metadata, profile, out string bindingDiagnostic))
                {
                    return SherpaONNXSpeechRecognitionModelSpec.Failure(
                        modelId,
                        metadata,
                        SherpaONNXModelResolutionStatus.MetadataConflict,
                        bindingDiagnostic);
                }

                if (modeRequirement == SherpaONNXSpeechRecognitionModeRequirement.Online && !profile.IsOnline)
                {
                    return ModeMismatch(modelId, metadata, "online/realtime", profile.ModelType);
                }
                if (modeRequirement == SherpaONNXSpeechRecognitionModeRequirement.Offline && profile.IsOnline)
                {
                    return ModeMismatch(modelId, metadata, "offline", profile.ModelType);
                }

                return new SherpaONNXSpeechRecognitionModelSpec(
                    modelId,
                    metadata,
                    SherpaONNXModelResolutionStatus.Resolved,
                    SherpaONNXModelResolutionLevel.Explicit,
                    metadata.registrationSource,
                    profile.ModelType,
                    profile.RuntimeFamily,
                    profile.IsOnline,
                    profile.DecodingMethod,
                    profile.RequiredFiles.Select(item => item.Key),
                    $"Resolved from {metadata.registrationSource} Model Definition manifest through runtime profile '{profile.Id}'.",
                    profile.Id,
                    metadata.definitionProvenance,
                    profile.RequiredFiles);
            }

            bool explicitModelType = Enum.TryParse(
                metadata.modelTypeHint,
                true,
                out SpeechRecognitionModelType modelType)
                && modelType != SpeechRecognitionModelType.None;
            if (!explicitModelType)
            {
                modelType = SherpaUtils.Model.GetSpeechRecognitionModelType(modelId);
            }

            if (modelType == SpeechRecognitionModelType.None)
            {
                return SherpaONNXSpeechRecognitionModelSpec.Failure(
                    modelId,
                    metadata,
                    SherpaONNXModelResolutionStatus.UnsupportedTopology,
                    $"The registered model '{modelId}' has no explicit or deterministic speech-recognition topology.");
            }

            bool isOnline = IsOnlineModelType(modelType);
            if (modeRequirement == SherpaONNXSpeechRecognitionModeRequirement.Online && !isOnline)
            {
                return ModeMismatch(modelId, metadata, "online/realtime", modelType);
            }
            if (modeRequirement == SherpaONNXSpeechRecognitionModeRequirement.Offline && isOnline)
            {
                return ModeMismatch(modelId, metadata, "offline", modelType);
            }

            bool explicitFamily = Enum.TryParse(
                metadata.runtimeFamilyHint,
                true,
                out SherpaONNXSpeechRecognitionRuntimeFamily family)
                && family != SherpaONNXSpeechRecognitionRuntimeFamily.Unknown;
            if (!explicitFamily)
            {
                family = ResolveRuntimeFamily(modelId, modelType);
            }
            if (family == SherpaONNXSpeechRecognitionRuntimeFamily.Unknown)
            {
                return SherpaONNXSpeechRecognitionModelSpec.Failure(
                    modelId,
                    metadata,
                    SherpaONNXModelResolutionStatus.UnsupportedTopology,
                    $"The registered model '{modelId}' cannot be mapped to a supported sherpa runtime family.");
            }

            string decodingMethod = ResolveDecodingMethod(modelType, family);
            if (string.IsNullOrEmpty(decodingMethod))
            {
                return SherpaONNXSpeechRecognitionModelSpec.Failure(
                    modelId,
                    metadata,
                    SherpaONNXModelResolutionStatus.UnsupportedTopology,
                    $"No safe decoding method is registered for '{modelId}' ({modelType}, {family}).");
            }

            return new SherpaONNXSpeechRecognitionModelSpec(
                modelId,
                metadata,
                SherpaONNXModelResolutionStatus.Resolved,
                explicitModelType && explicitFamily
                    ? SherpaONNXModelResolutionLevel.Explicit
                    : SherpaONNXModelResolutionLevel.DeterministicFamily,
                metadata.registrationSource,
                modelType,
                family,
                isOnline,
                decodingMethod,
                ResolveRequiredFileKeys(metadata, modelType),
                $"Resolved from {metadata.registrationSource} metadata using "
                + (explicitModelType && explicitFamily ? "explicit semantics." : "a deterministic registered-family rule."));
        }

        private static SherpaONNXSpeechRecognitionModelSpec ModeMismatch(
            string modelId,
            SherpaONNXModelMetadata metadata,
            string required,
            SpeechRecognitionModelType actual)
        {
            return SherpaONNXSpeechRecognitionModelSpec.Failure(
                modelId,
                metadata,
                SherpaONNXModelResolutionStatus.ModeMismatch,
                $"Model '{modelId}' resolves to {actual} but the caller requires an {required} model.");
        }

        private static bool ValidateRuntimeProfileBindings(
            SherpaONNXModelMetadata metadata,
            SpeechRecognitionRuntimeProfile profile,
            out string diagnostic)
        {
            var allowed = new HashSet<SherpaONNXModelFileKey>(
                profile.RequiredFiles.Select(requirement => requirement.Key));
            var seen = new HashSet<SherpaONNXModelFileKey>();
            foreach (SherpaONNXModelFileBinding binding in
                     metadata.fileBindings ?? new List<SherpaONNXModelFileBinding>())
            {
                if (binding == null
                    || binding.key == SherpaONNXModelFileKey.None
                    || !allowed.Contains(binding.key)
                    || string.IsNullOrWhiteSpace(binding.path))
                {
                    diagnostic =
                        $"Model '{metadata.modelId}' contains an invalid binding for runtime profile '{profile.Id}'.";
                    return false;
                }
                if (!seen.Add(binding.key))
                {
                    diagnostic =
                        $"Model '{metadata.modelId}' binds role '{binding.key}' more than once for runtime profile '{profile.Id}'.";
                    return false;
                }
            }

            if (profile.BindingPolicy == SpeechRecognitionFileBindingPolicy.ExactRequired)
            {
                SherpaONNXModelFileKey[] missing = allowed.Where(key => !seen.Contains(key)).ToArray();
                if (missing.Length > 0)
                {
                    diagnostic =
                        $"Model '{metadata.modelId}' is missing exact bindings required by runtime profile '{profile.Id}': {string.Join(", ", missing)}.";
                    return false;
                }
            }

            diagnostic = string.Empty;
            return true;
        }

        private static bool IsOnlineModelType(SpeechRecognitionModelType modelType)
        {
            switch (modelType)
            {
                case SpeechRecognitionModelType.Online_Transducer:
                case SpeechRecognitionModelType.Online_Paraformer:
                case SpeechRecognitionModelType.Online_Ctc:
                case SpeechRecognitionModelType.Online_Nemo_Ctc:
                case SpeechRecognitionModelType.Online_Zipformer2Ctc:
                case SpeechRecognitionModelType.Online_Tone_Ctc:
                    return true;
                default:
                    return false;
            }
        }

        private static SherpaONNXSpeechRecognitionRuntimeFamily ResolveRuntimeFamily(
            string modelId,
            SpeechRecognitionModelType modelType)
        {
            string normalized = modelId?.ToLowerInvariant() ?? string.Empty;
            switch (modelType)
            {
                case SpeechRecognitionModelType.Online_Transducer:
                case SpeechRecognitionModelType.Offline_Transducer:
                    if (normalized.Contains("nemo") || normalized.Contains("nemotron"))
                    {
                        return SherpaONNXSpeechRecognitionRuntimeFamily.NemoTransducer;
                    }
                    return SherpaONNXSpeechRecognitionRuntimeFamily.ZipformerTransducer;
                case SpeechRecognitionModelType.Online_Paraformer:
                case SpeechRecognitionModelType.Offline_Paraformer:
                    return SherpaONNXSpeechRecognitionRuntimeFamily.Paraformer;
                case SpeechRecognitionModelType.Online_Ctc:
                case SpeechRecognitionModelType.Online_Nemo_Ctc:
                case SpeechRecognitionModelType.Online_Zipformer2Ctc:
                case SpeechRecognitionModelType.Online_Tone_Ctc:
                case SpeechRecognitionModelType.Offline_ZipformerCtc:
                case SpeechRecognitionModelType.Offline_Nemo_Ctc:
                case SpeechRecognitionModelType.Offline_WenetCtc:
                case SpeechRecognitionModelType.Offline_MedAsrCtc:
                case SpeechRecognitionModelType.Offline_FireRedAsrCtc:
                    return SherpaONNXSpeechRecognitionRuntimeFamily.Ctc;
                default:
                    return SherpaONNXSpeechRecognitionRuntimeFamily.OtherSupported;
            }
        }

        private static string ResolveDecodingMethod(
            SpeechRecognitionModelType modelType,
            SherpaONNXSpeechRecognitionRuntimeFamily family)
        {
            if (modelType == SpeechRecognitionModelType.Online_Transducer)
            {
                return family == SherpaONNXSpeechRecognitionRuntimeFamily.NemoTransducer
                    ? "greedy_search"
                    : "modified_beam_search";
            }
            if (modelType == SpeechRecognitionModelType.Offline_Transducer)
            {
                return "modified_beam_search";
            }
            return "greedy_search";
        }

        private static IEnumerable<SherpaONNXModelFileKey> ResolveRequiredFileKeys(
            SherpaONNXModelMetadata metadata,
            SpeechRecognitionModelType modelType)
        {
            if (metadata?.fileBindings != null && metadata.fileBindings.Count > 0)
            {
                SherpaONNXModelFileKey[] explicitKeys = metadata.fileBindings
                    .Where(binding => binding != null && binding.key != SherpaONNXModelFileKey.None)
                    .Select(binding => binding.key)
                    .Distinct()
                    .ToArray();
                if (explicitKeys.Length > 0)
                {
                    return explicitKeys;
                }
            }

            switch (modelType)
            {
                case SpeechRecognitionModelType.Online_Transducer:
                case SpeechRecognitionModelType.Offline_Transducer:
                    return new[]
                    {
                        SherpaONNXModelFileKey.Encoder,
                        SherpaONNXModelFileKey.Decoder,
                        SherpaONNXModelFileKey.Joiner,
                        SherpaONNXModelFileKey.Tokens
                    };
                case SpeechRecognitionModelType.Online_Paraformer:
                case SpeechRecognitionModelType.Offline_Paraformer:
                    return new[]
                    {
                        SherpaONNXModelFileKey.Encoder,
                        SherpaONNXModelFileKey.Decoder,
                        SherpaONNXModelFileKey.Tokens
                    };
                default:
                    return new[]
                    {
                        SherpaONNXModelFileKey.Model,
                        SherpaONNXModelFileKey.Tokens
                    };
            }
        }
    }
}
