using System;
using System.Collections.Generic;
using System.Linq;

namespace Eitan.SherpaONNXUnity.Runtime
{
    public enum SherpaONNXModelFileKind
    {
        File,
        Directory
    }

    public sealed class SherpaONNXModelFileRequirement
    {
        public SherpaONNXModelFileRequirement(
            SherpaONNXModelFileKey key,
            SherpaONNXModelFileKind kind)
        {
            Key = key;
            Kind = kind;
        }

        public SherpaONNXModelFileKey Key { get; }
        public SherpaONNXModelFileKind Kind { get; }
    }

    public sealed class SherpaONNXModelDefinitionProvenance
    {
        public static readonly SherpaONNXModelDefinitionProvenance Unknown =
            new SherpaONNXModelDefinitionProvenance(string.Empty, string.Empty, 0, string.Empty);

        public SherpaONNXModelDefinitionProvenance(
            string sourceId,
            string manifestKind,
            int schemaVersion,
            string contentSha256)
        {
            SourceId = sourceId ?? string.Empty;
            ManifestKind = manifestKind ?? string.Empty;
            SchemaVersion = schemaVersion;
            ContentSha256 = contentSha256 ?? string.Empty;
        }

        public string SourceId { get; }
        public string ManifestKind { get; }
        public int SchemaVersion { get; }
        public string ContentSha256 { get; }
        public bool IsManifestBacked => SchemaVersion > 0 && !string.IsNullOrEmpty(ContentSha256);
    }

    public enum SherpaONNXModelRegistrationSource
    {
        Unknown,
        BuiltIn,
        RemoteCustom,
        LocalCustom,
        DistributionOnly
    }

    public enum SherpaONNXModelResolutionLevel
    {
        Unknown,
        Explicit,
        DeterministicFamily,
        DistributionOnly
    }

    public enum SherpaONNXModelResolutionStatus
    {
        Resolved,
        Unregistered,
        ModuleMismatch,
        UnsupportedTopology,
        ModeMismatch,
        MetadataConflict
    }

    public enum SherpaONNXSpeechRecognitionRuntimeFamily
    {
        Unknown,
        ZipformerTransducer,
        NemoTransducer,
        Paraformer,
        Ctc,
        OtherSupported
    }

    public enum SherpaONNXSpeechRecognitionModeRequirement
    {
        Any,
        Online,
        Offline
    }

    /// <summary>
    /// Immutable, source-aware speech-recognition configuration shared by callers and native setup.
    /// </summary>
    public sealed class SherpaONNXSpeechRecognitionModelSpec
    {
        private readonly SherpaONNXModelMetadata _metadata;

        internal SherpaONNXSpeechRecognitionModelSpec(
            string modelId,
            SherpaONNXModelMetadata metadata,
            SherpaONNXModelResolutionStatus status,
            SherpaONNXModelResolutionLevel resolutionLevel,
            SherpaONNXModelRegistrationSource registrationSource,
            SpeechRecognitionModelType modelType,
            SherpaONNXSpeechRecognitionRuntimeFamily runtimeFamily,
            bool isOnline,
            string decodingMethod,
            IEnumerable<SherpaONNXModelFileKey> requiredFileKeys,
            string diagnostic,
            string runtimeProfileId = null,
            SherpaONNXModelDefinitionProvenance definitionProvenance = null,
            IEnumerable<SherpaONNXModelFileRequirement> requiredFiles = null)
        {
            ModelId = modelId ?? string.Empty;
            _metadata = CloneMetadata(metadata);
            Status = status;
            ResolutionLevel = resolutionLevel;
            RegistrationSource = registrationSource;
            ModelType = modelType;
            RuntimeFamily = runtimeFamily;
            IsOnline = isOnline;
            DecodingMethod = decodingMethod ?? string.Empty;
            RuntimeProfileId = runtimeProfileId ?? string.Empty;
            DefinitionProvenance = definitionProvenance ?? SherpaONNXModelDefinitionProvenance.Unknown;
            var materializedRequirements = (requiredFiles ?? Array.Empty<SherpaONNXModelFileRequirement>())
                .Where(item => item != null && item.Key != SherpaONNXModelFileKey.None)
                .GroupBy(item => item.Key)
                .Select(group => group.First())
                .ToArray();
            var materializedKeys = (requiredFileKeys ?? Array.Empty<SherpaONNXModelFileKey>())
                .Where(key => key != SherpaONNXModelFileKey.None)
                .Distinct()
                .ToArray();
            SherpaONNXModelFileRequirement[] resolvedRequirements = materializedRequirements.Length > 0
                ? materializedRequirements
                : materializedKeys.Select(ToDefaultRequirement).ToArray();
            RequiredFiles = Array.AsReadOnly(resolvedRequirements);
            RequiredFileKeys = Array.AsReadOnly(resolvedRequirements.Select(item => item.Key).ToArray());
            Diagnostic = diagnostic ?? string.Empty;
        }

        public string ModelId { get; }
        /// <summary>
        /// Returns a defensive snapshot so callers cannot mutate registry state through a resolved spec.
        /// </summary>
        public SherpaONNXModelMetadata Metadata => CloneMetadata(_metadata);
        public SherpaONNXModelResolutionStatus Status { get; }
        public SherpaONNXModelResolutionLevel ResolutionLevel { get; }
        public SherpaONNXModelRegistrationSource RegistrationSource { get; }
        public SpeechRecognitionModelType ModelType { get; }
        public SherpaONNXSpeechRecognitionRuntimeFamily RuntimeFamily { get; }
        public bool IsOnline { get; }
        public string DecodingMethod { get; }
        public string RuntimeProfileId { get; }
        public SherpaONNXModelDefinitionProvenance DefinitionProvenance { get; }
        public IReadOnlyList<SherpaONNXModelFileRequirement> RequiredFiles { get; }
        public IReadOnlyList<SherpaONNXModelFileKey> RequiredFileKeys { get; }
        public string Diagnostic { get; }
        public bool CanInitialize => Status == SherpaONNXModelResolutionStatus.Resolved;

        internal static SherpaONNXSpeechRecognitionModelSpec Failure(
            string modelId,
            SherpaONNXModelMetadata metadata,
            SherpaONNXModelResolutionStatus status,
            string diagnostic)
        {
            return new SherpaONNXSpeechRecognitionModelSpec(
                modelId,
                metadata,
                status,
                metadata != null && metadata.registrationSource == SherpaONNXModelRegistrationSource.DistributionOnly
                    ? SherpaONNXModelResolutionLevel.DistributionOnly
                    : SherpaONNXModelResolutionLevel.Unknown,
                metadata?.registrationSource ?? SherpaONNXModelRegistrationSource.Unknown,
                SpeechRecognitionModelType.None,
                SherpaONNXSpeechRecognitionRuntimeFamily.Unknown,
                false,
                string.Empty,
                Array.Empty<SherpaONNXModelFileKey>(),
                diagnostic);
        }

        private static SherpaONNXModelFileRequirement ToDefaultRequirement(SherpaONNXModelFileKey key)
        {
            return new SherpaONNXModelFileRequirement(
                key,
                key == SherpaONNXModelFileKey.Tokenizer
                    ? SherpaONNXModelFileKind.Directory
                    : SherpaONNXModelFileKind.File);
        }

        private static SherpaONNXModelMetadata CloneMetadata(SherpaONNXModelMetadata source)
        {
            if (source == null)
            {
                return null;
            }

            return new SherpaONNXModelMetadata
            {
                modelId = source.modelId,
                moduleType = source.moduleType,
                moduleTypeHint = source.moduleTypeHint,
                downloadUrl = source.downloadUrl,
                downloadFileHash = source.downloadFileHash,
                modelTypeHint = source.modelTypeHint,
                runtimeFamilyHint = source.runtimeFamilyHint,
                runtimeProfileId = source.runtimeProfileId,
                definitionProvenance = source.definitionProvenance,
                fileBindings = (source.fileBindings ?? new List<SherpaONNXModelFileBinding>())
                    .Where(binding => binding != null)
                    .Select(binding => new SherpaONNXModelFileBinding
                    {
                        key = binding.key,
                        path = binding.path
                    })
                    .ToList(),
                numberOfSpeakers = source.numberOfSpeakers,
                sampleRate = source.sampleRate,
                registrationSource = source.registrationSource,
                hasModelDefinition = source.hasModelDefinition,
                hasDistributionRecord = source.hasDistributionRecord
            };
        }
    }
}
