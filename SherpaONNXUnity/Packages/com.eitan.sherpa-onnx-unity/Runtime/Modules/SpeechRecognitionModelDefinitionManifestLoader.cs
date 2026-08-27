using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Eitan.SherpaONNXUnity.Runtime.Modules
{
    internal sealed class SherpaONNXModelDefinitionSourceResult
    {
        public SherpaONNXModelDefinitionSourceResult(
            IReadOnlyList<SherpaONNXModelMetadata> definitions,
            SherpaONNXModelDefinitionProvenance provenance)
        {
            Definitions = Array.AsReadOnly(
                definitions?.ToArray() ?? Array.Empty<SherpaONNXModelMetadata>());
            Provenance = provenance ?? SherpaONNXModelDefinitionProvenance.Unknown;
        }

        public IReadOnlyList<SherpaONNXModelMetadata> Definitions { get; }
        public SherpaONNXModelDefinitionProvenance Provenance { get; }
    }

    internal static class SpeechRecognitionModelDefinitionManifestLoader
    {
        internal const string ManifestKind = "sherpa-onnx-unity-model-definitions";
        internal const int SchemaVersion = 1;
        internal const string ResourcePath = "SherpaONNX/ModelDefinitions/asr-model-definitions.v1";
        internal const string BuiltInSourceId = "package-resource:asr-model-definitions.v1.json";
        private const int MaxDepth = 32;

        internal static SherpaONNXModelDefinitionSourceResult Parse(byte[] utf8, string sourceId)
        {
            if (utf8 == null || utf8.Length == 0)
            {
                throw new InvalidDataException("Model Definition manifest is empty.");
            }
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                throw new ArgumentException("Model Definition source ID cannot be empty.", nameof(sourceId));
            }

            try
            {
                return ParseCore(utf8, sourceId.Trim());
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is JsonException
                || ex is DecoderFallbackException
                || ex is ArgumentException)
            {
                throw new InvalidDataException(
                    $"Invalid Model Definition manifest '{sourceId}': {ex.Message}",
                    ex);
            }
        }

        private static SherpaONNXModelDefinitionSourceResult ParseCore(byte[] utf8, string sourceId)
        {
            JObject root;
            using (var stream = new MemoryStream(utf8, writable: false))
            using (var textReader = new StreamReader(
                       stream,
                       new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                       detectEncodingFromByteOrderMarks: true))
            using (var jsonReader = new JsonTextReader(textReader)
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Decimal,
                MaxDepth = MaxDepth,
                SupportMultipleContent = false
            })
            {
                root = JObject.Load(jsonReader, new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Load,
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    LineInfoHandling = LineInfoHandling.Load
                });

                if (root.DescendantsAndSelf().Any(token => token.Type == JTokenType.Comment))
                {
                    throw new InvalidDataException("JSON comments are not allowed in a Model Definition manifest.");
                }
                if (jsonReader.Read())
                {
                    throw new InvalidDataException("Trailing JSON content is not allowed in a Model Definition manifest.");
                }
            }

            var serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Decimal,
                MissingMemberHandling = MissingMemberHandling.Error,
                TypeNameHandling = TypeNameHandling.None
            });
            var dto = root.ToObject<ModelDefinitionManifestDto>(serializer)
                      ?? throw new InvalidDataException("Model Definition manifest could not be deserialized.");

            if (!string.Equals(dto.ManifestKind, ManifestKind, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported manifestKind '{dto.ManifestKind}'.");
            }
            if (dto.SchemaVersion != SchemaVersion)
            {
                throw new InvalidDataException($"Unsupported Model Definition schemaVersion '{dto.SchemaVersion}'.");
            }
            if (!string.Equals(dto.Module, "speech-recognition", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Manifest module '{dto.Module}' is not speech-recognition.");
            }
            if (dto.Definitions == null || dto.Definitions.Count == 0)
            {
                throw new InvalidDataException("Model Definition manifest contains no definitions.");
            }

            ValidateExactString(dto.ManifestKind, "manifestKind");
            ValidateExactString(dto.Module, "module");

            string hash = ComputeSha256(utf8);
            var provenance = new SherpaONNXModelDefinitionProvenance(
                sourceId,
                dto.ManifestKind,
                dto.SchemaVersion,
                hash);
            var normalizedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var definitions = new List<SherpaONNXModelMetadata>(dto.Definitions.Count);
            for (int index = 0; index < dto.Definitions.Count; index++)
            {
                ModelDefinitionDto item = dto.Definitions[index]
                    ?? throw new InvalidDataException($"definitions[{index}] cannot be null.");
                ValidateExactString(item.ModelId, $"definitions[{index}].modelId");
                ValidateExactString(item.RuntimeProfile, $"definitions[{index}].runtimeProfile");
                if (!normalizedIds.Add(item.ModelId))
                {
                    throw new InvalidDataException(
                        $"Duplicate normalized modelId '{item.ModelId}' in the Model Definition manifest.");
                }
                if (item.SampleRate <= 0)
                {
                    throw new InvalidDataException(
                        $"Model '{item.ModelId}' has invalid sampleRate '{item.SampleRate}'.");
                }
                if (!SpeechRecognitionRuntimeProfileRegistry.TryGet(item.RuntimeProfile, out var profile))
                {
                    throw new InvalidDataException(
                        $"Model '{item.ModelId}' references unknown runtimeProfile '{item.RuntimeProfile}'.");
                }

                List<SherpaONNXModelFileBinding> bindings = ValidateBindings(item, profile);
                definitions.Add(new SherpaONNXModelMetadata
                {
                    modelId = item.ModelId,
                    moduleType = SherpaONNXModuleType.SpeechRecognition,
                    sampleRate = item.SampleRate,
                    modelTypeHint = profile.ModelType.ToString(),
                    runtimeFamilyHint = profile.RuntimeFamily.ToString(),
                    runtimeProfileId = profile.Id,
                    definitionProvenance = provenance,
                    fileBindings = bindings,
                    registrationSource = SherpaONNXModelRegistrationSource.BuiltIn,
                    hasModelDefinition = true
                });
            }

            return new SherpaONNXModelDefinitionSourceResult(definitions, provenance);
        }

        private static List<SherpaONNXModelFileBinding> ValidateBindings(
            ModelDefinitionDto definition,
            SpeechRecognitionRuntimeProfile profile)
        {
            var bindings = new List<SherpaONNXModelFileBinding>();
            var seenRoles = new HashSet<SherpaONNXModelFileKey>();
            var allowedRoles = new HashSet<SherpaONNXModelFileKey>(
                profile.RequiredFiles.Select(requirement => requirement.Key));

            foreach (ModelFileBindingDto binding in definition.FileBindings ?? new List<ModelFileBindingDto>())
            {
                if (binding == null)
                {
                    throw new InvalidDataException(
                        $"Model '{definition.ModelId}' contains a null file binding.");
                }
                ValidateExactString(binding.Role, $"{definition.ModelId}.fileBindings.role");
                ValidateExactString(binding.Path, $"{definition.ModelId}.fileBindings.path");
                if (!Enum.TryParse(binding.Role, ignoreCase: false, out SherpaONNXModelFileKey key)
                    || key == SherpaONNXModelFileKey.None
                    || !allowedRoles.Contains(key))
                {
                    throw new InvalidDataException(
                        $"Model '{definition.ModelId}' has unknown or unsupported file role '{binding.Role}' for profile '{profile.Id}'.");
                }
                if (!seenRoles.Add(key))
                {
                    throw new InvalidDataException(
                        $"Model '{definition.ModelId}' binds role '{key}' more than once.");
                }
                ValidateRelativePath(binding.Path, definition.ModelId, key);
                bindings.Add(new SherpaONNXModelFileBinding { key = key, path = binding.Path });
            }

            if (profile.BindingPolicy == SpeechRecognitionFileBindingPolicy.ExactRequired)
            {
                SherpaONNXModelFileKey[] missing = allowedRoles.Where(role => !seenRoles.Contains(role)).ToArray();
                if (missing.Length > 0)
                {
                    throw new InvalidDataException(
                        $"Model '{definition.ModelId}' is missing exact bindings required by profile '{profile.Id}': {string.Join(", ", missing)}.");
                }
            }

            return bindings;
        }

        private static void ValidateExactString(string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"'{field}' cannot be empty.");
            }
            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidDataException($"'{field}' must not contain leading or trailing whitespace.");
            }
        }

        private static void ValidateRelativePath(
            string path,
            string modelId,
            SherpaONNXModelFileKey role)
        {
            if (path.IndexOf('\\') >= 0
                || path.StartsWith("/", StringComparison.Ordinal)
                || path.IndexOf(':') >= 0
                || path.IndexOf('\0') >= 0
                || path.IndexOf('*') >= 0
                || path.IndexOf('?') >= 0
                || Uri.TryCreate(path, UriKind.Absolute, out _))
            {
                throw InvalidBindingPath(modelId, role, path);
            }

            string[] segments = path.Split('/');
            if (segments.Length == 0 || segments.Any(segment =>
                    string.IsNullOrEmpty(segment)
                    || string.Equals(segment, ".", StringComparison.Ordinal)
                    || string.Equals(segment, "..", StringComparison.Ordinal)))
            {
                throw InvalidBindingPath(modelId, role, path);
            }
        }

        private static InvalidDataException InvalidBindingPath(
            string modelId,
            SherpaONNXModelFileKey role,
            string path)
        {
            return new InvalidDataException(
                $"Model '{modelId}' binding '{role}' has non-canonical or unsafe relative path '{path}'.");
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class ModelDefinitionManifestDto
        {
            [JsonProperty("manifestKind", Required = Required.Always)]
            public string ManifestKind { get; set; }

            [JsonProperty("schemaVersion", Required = Required.Always)]
            public int SchemaVersion { get; set; }

            [JsonProperty("module", Required = Required.Always)]
            public string Module { get; set; }

            [JsonProperty("definitions", Required = Required.Always)]
            public List<ModelDefinitionDto> Definitions { get; set; }
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class ModelDefinitionDto
        {
            [JsonProperty("modelId", Required = Required.Always)]
            public string ModelId { get; set; }

            [JsonProperty("runtimeProfile", Required = Required.Always)]
            public string RuntimeProfile { get; set; }

            [JsonProperty("sampleRate", Required = Required.Always)]
            public int SampleRate { get; set; }

            [JsonProperty("fileBindings")]
            public List<ModelFileBindingDto> FileBindings { get; set; }
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class ModelFileBindingDto
        {
            [JsonProperty("role", Required = Required.Always)]
            public string Role { get; set; }

            [JsonProperty("path", Required = Required.Always)]
            public string Path { get; set; }
        }
    }
}
