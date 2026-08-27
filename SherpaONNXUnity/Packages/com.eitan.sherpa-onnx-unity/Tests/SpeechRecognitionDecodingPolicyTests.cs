using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Eitan.Sherpa.Onnx.Unity.Mono.Components;
using Eitan.SherpaONNXUnity.Runtime;
using Eitan.SherpaONNXUnity.Runtime.Constants;
using Eitan.SherpaONNXUnity.Runtime.Modules;
using NUnit.Framework;
using UnityEngine;

namespace Eitan.SherpaONNXUnity.Tests
{
    public sealed class SpeechRecognitionDecodingPolicyTests
    {
        private const string Nemotron35ModelId =
            "sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-560ms-int8-2026-06-11";
        private const string ZipformerModelId =
            "sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20";
        private const string FunAsrNanoInt8ModelId =
            "sherpa-onnx-funasr-nano-int8-2025-12-30";
        private const string Qwen3AsrInt8ModelId =
            "sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25";
        private const string XAsr480MsModelId =
            "sherpa-onnx-x-asr-480ms-streaming-zipformer-transducer-zh-en-punct-2026-06-05";
        private const string FireRedAsr2Int8ModelId =
            "sherpa-onnx-fire-red-asr2-zh_en-int8-2026-02-26";
        private const string Qwen3Asr17BModelId = "Qwen3-ASR-1.7B-ONNX";

        [TearDown]
        public void TearDown()
        {
            SherpaONNXModelRegistry.Instance.Uninitialize();
        }

        [Test]
        public void ModelDefinitionResource_MissingDuringEarlyPreloadIsRetriedAfterImport()
        {
            Type providerType = typeof(SherpaONNXUnityAPI).Assembly.GetType(
                "Eitan.SherpaONNXUnity.Runtime.SherpaONNXRuntimeResourceProvider",
                throwOnError: true);
            var definitionsField = providerType.GetField(
                "s_asrModelDefinitions",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var preloadedField = providerType.GetField(
                "s_preloaded",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            Assert.That(definitionsField, Is.Not.Null);
            Assert.That(preloadedField, Is.Not.Null);

            object previousDefinitions = definitionsField.GetValue(null);
            object previousPreloaded = preloadedField.GetValue(null);
            try
            {
                definitionsField.SetValue(null, SherpaONNXModelDefinitionManifestSnapshot.Missing);
                preloadedField.SetValue(null, true);

                SherpaONNXModelDefinitionManifestSnapshot snapshot =
                    SherpaONNXRuntimeResourceProvider.GetSpeechRecognitionModelDefinitionsSnapshot();

                Assert.That(
                    snapshot.IsAvailable,
                    Is.True,
                    "A Resources.Load miss during early Editor preload must not become a permanent negative cache entry.");
            }
            finally
            {
                definitionsField.SetValue(null, previousDefinitions);
                preloadedField.SetValue(null, previousPreloaded);
            }
        }

        [Test]
        public void BuiltInNemotron_ResolvesToSafeOnlineNemoSpec()
        {
            SherpaONNXSpeechRecognitionModelSpec spec = ResolveBuiltIn(Nemotron35ModelId);

            Assert.That(spec.CanInitialize, Is.True, spec.Diagnostic);
            Assert.That(spec.IsOnline, Is.True);
            Assert.That(spec.ModelType, Is.EqualTo(SpeechRecognitionModelType.Online_Transducer));
            Assert.That(spec.RuntimeFamily, Is.EqualTo(SherpaONNXSpeechRecognitionRuntimeFamily.NemoTransducer));
            Assert.That(spec.DecodingMethod, Is.EqualTo("greedy_search"));
            Assert.That(spec.RuntimeProfileId, Is.EqualTo(SpeechRecognitionRuntimeProfileRegistry.OnlineNemoTransducer));
            Assert.That(spec.DefinitionProvenance.IsManifestBacked, Is.True);
            Assert.That(spec.RequiredFiles.All(item => item.Kind == SherpaONNXModelFileKind.File), Is.True);
            CollectionAssert.AreEquivalent(
                new[] { "encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt" },
                spec.Metadata.fileBindings.Select(binding => binding.path).ToArray());
        }

        [Test]
        public void BuiltInZipformer_ResolvesToModifiedBeamSearch()
        {
            SherpaONNXSpeechRecognitionModelSpec spec = ResolveBuiltIn(ZipformerModelId);

            Assert.That(spec.CanInitialize, Is.True, spec.Diagnostic);
            Assert.That(spec.IsOnline, Is.True);
            Assert.That(spec.RuntimeFamily, Is.EqualTo(SherpaONNXSpeechRecognitionRuntimeFamily.ZipformerTransducer));
            Assert.That(spec.DecodingMethod, Is.EqualTo("modified_beam_search"));
            Assert.That(spec.RuntimeProfileId, Is.EqualTo(SpeechRecognitionRuntimeProfileRegistry.OnlineZipformerTransducer));
        }

        [Test]
        public void BuiltInXAsr480Ms_ResolvesAsExplicitOnlineZipformer()
        {
            SherpaONNXSpeechRecognitionModelSpec spec = ResolveBuiltIn(XAsr480MsModelId);

            Assert.That(spec.CanInitialize, Is.True, spec.Diagnostic);
            Assert.That(spec.IsOnline, Is.True);
            Assert.That(spec.ResolutionLevel, Is.EqualTo(SherpaONNXModelResolutionLevel.Explicit));
            Assert.That(spec.RegistrationSource, Is.EqualTo(SherpaONNXModelRegistrationSource.BuiltIn));
            Assert.That(spec.ModelType, Is.EqualTo(SpeechRecognitionModelType.Online_Transducer));
            Assert.That(spec.RuntimeFamily, Is.EqualTo(SherpaONNXSpeechRecognitionRuntimeFamily.ZipformerTransducer));
            Assert.That(spec.DecodingMethod, Is.EqualTo("modified_beam_search"));
            Assert.That(spec.RuntimeProfileId, Is.EqualTo(SpeechRecognitionRuntimeProfileRegistry.OnlineZipformerTransducer));
            CollectionAssert.AreEqual(
                new[]
                {
                    "encoder.onnx",
                    "decoder.onnx",
                    "joiner.onnx",
                    "tokens.txt"
                },
                spec.Metadata.fileBindings.Select(binding => binding.path).ToArray());
        }

        [Test]
        public void BuiltInFireRedAsr2Int8_ResolvesAsExplicitOfflineFireRed()
        {
            SherpaONNXSpeechRecognitionModelSpec spec = ResolveBuiltIn(FireRedAsr2Int8ModelId);

            Assert.That(spec.CanInitialize, Is.True, spec.Diagnostic);
            Assert.That(spec.IsOnline, Is.False);
            Assert.That(spec.ResolutionLevel, Is.EqualTo(SherpaONNXModelResolutionLevel.Explicit));
            Assert.That(spec.RegistrationSource, Is.EqualTo(SherpaONNXModelRegistrationSource.BuiltIn));
            Assert.That(spec.ModelType, Is.EqualTo(SpeechRecognitionModelType.FireRedAsr));
            Assert.That(spec.RuntimeFamily, Is.EqualTo(SherpaONNXSpeechRecognitionRuntimeFamily.OtherSupported));
            Assert.That(spec.DecodingMethod, Is.EqualTo("greedy_search"));
            Assert.That(spec.RuntimeProfileId, Is.EqualTo(SpeechRecognitionRuntimeProfileRegistry.OfflineFireRedAsr));
            Assert.That(spec.Metadata.sampleRate, Is.EqualTo(16000));
            CollectionAssert.AreEqual(
                new[]
                {
                    SherpaONNXModelFileKey.Encoder,
                    SherpaONNXModelFileKey.Decoder,
                    SherpaONNXModelFileKey.Tokens
                },
                spec.RequiredFileKeys);
            CollectionAssert.AreEqual(
                new[] { "encoder.int8.onnx", "decoder.int8.onnx", "tokens.txt" },
                spec.Metadata.fileBindings.Select(binding => binding.path).ToArray());
        }

        [Test]
        public void BuiltInQwen3Asr17B_RequiresExternalDataBeforeNativeInitialization()
        {
            SherpaONNXSpeechRecognitionModelSpec spec = ResolveBuiltIn(Qwen3Asr17BModelId);

            Assert.That(spec.CanInitialize, Is.True, spec.Diagnostic);
            Assert.That(spec.IsOnline, Is.False);
            Assert.That(spec.ResolutionLevel, Is.EqualTo(SherpaONNXModelResolutionLevel.Explicit));
            Assert.That(spec.RegistrationSource, Is.EqualTo(SherpaONNXModelRegistrationSource.BuiltIn));
            Assert.That(spec.ModelType, Is.EqualTo(SpeechRecognitionModelType.Offline_Qwen3Asr));
            Assert.That(spec.RuntimeFamily, Is.EqualTo(SherpaONNXSpeechRecognitionRuntimeFamily.OtherSupported));
            Assert.That(spec.DecodingMethod, Is.EqualTo("greedy_search"));
            Assert.That(spec.RuntimeProfileId, Is.EqualTo(SpeechRecognitionRuntimeProfileRegistry.OfflineQwen3AsrExternalData));
            CollectionAssert.AreEqual(
                new[]
                {
                    SherpaONNXModelFileKey.ConvFrontend,
                    SherpaONNXModelFileKey.Encoder,
                    SherpaONNXModelFileKey.EncoderExternalData,
                    SherpaONNXModelFileKey.Decoder,
                    SherpaONNXModelFileKey.DecoderExternalData,
                    SherpaONNXModelFileKey.Tokenizer
                },
                spec.RequiredFileKeys);
            Assert.That(
                spec.RequiredFiles.Single(item => item.Key == SherpaONNXModelFileKey.Tokenizer).Kind,
                Is.EqualTo(SherpaONNXModelFileKind.Directory));
            Assert.That(
                spec.RequiredFiles.Where(item => item.Key != SherpaONNXModelFileKey.Tokenizer)
                    .All(item => item.Kind == SherpaONNXModelFileKind.File),
                Is.True);
            CollectionAssert.AreEqual(
                new[]
                {
                    "conv_frontend.onnx",
                    "encoder.onnx",
                    "encoder.onnx.data",
                    "decoder.onnx",
                    "decoder.onnx.data",
                    "tokenizer"
                },
                spec.Metadata.fileBindings.Select(binding => binding.path).ToArray());
        }

        [TestCase(FireRedAsr2Int8ModelId, SpeechRecognitionRuntimeProfileRegistry.OfflineFireRedAsr)]
        [TestCase(Qwen3Asr17BModelId, SpeechRecognitionRuntimeProfileRegistry.OfflineQwen3AsrExternalData)]
        public void NewDefinitionChecksumOverlay_PreservesProfileBindingsAndProvenance(
            string modelId,
            string expectedProfile)
        {
            var manifest = new SherpaONNXModelManifest();
            SherpaONNXModelMetadata definition = CloneBuiltIn(modelId);
            SherpaONNXModelFileBinding[] expectedBindings = definition.fileBindings
                .Select(binding => new SherpaONNXModelFileBinding { key = binding.key, path = binding.path })
                .ToArray();
            var distribution = new SherpaONNXModelMetadata
            {
                modelId = modelId,
                downloadUrl = "https://example.invalid/model.tar.bz2",
                downloadFileHash = "distribution-hash"
            };

            SherpaONNXConstants.MergeModelSources(
                manifest,
                new[] { definition },
                new[] { distribution },
                SherpaONNXModuleType.SpeechRecognition);

            SherpaONNXModelMetadata actual = manifest.models.Single();
            Assert.That(actual.runtimeProfileId, Is.EqualTo(expectedProfile));
            Assert.That(actual.definitionProvenance.IsManifestBacked, Is.True);
            CollectionAssert.AreEqual(
                expectedBindings.Select(binding => binding.key).ToArray(),
                actual.fileBindings.Select(binding => binding.key).ToArray());
            CollectionAssert.AreEqual(
                expectedBindings.Select(binding => binding.path).ToArray(),
                actual.fileBindings.Select(binding => binding.path).ToArray());
            Assert.That(actual.downloadUrl, Is.EqualTo(distribution.downloadUrl));
            Assert.That(actual.downloadFileHash, Is.EqualTo(distribution.downloadFileHash));
        }

        [Test]
        public void BuiltInFunAsrNanoInt8_ResolvesFromExplicitOfflineDefinition()
        {
            SherpaONNXSpeechRecognitionModelSpec spec = ResolveBuiltIn(FunAsrNanoInt8ModelId);

            Assert.That(spec.CanInitialize, Is.True, spec.Diagnostic);
            Assert.That(spec.IsOnline, Is.False);
            Assert.That(spec.ResolutionLevel, Is.EqualTo(SherpaONNXModelResolutionLevel.Explicit));
            Assert.That(spec.RegistrationSource, Is.EqualTo(SherpaONNXModelRegistrationSource.BuiltIn));
            Assert.That(spec.ModelType, Is.EqualTo(SpeechRecognitionModelType.Offline_FunAsrNano));
            Assert.That(spec.RuntimeFamily, Is.EqualTo(SherpaONNXSpeechRecognitionRuntimeFamily.OtherSupported));
            Assert.That(spec.DecodingMethod, Is.EqualTo("greedy_search"));
            Assert.That(spec.RuntimeProfileId, Is.EqualTo(SpeechRecognitionRuntimeProfileRegistry.OfflineFunAsrNano));
            Assert.That(spec.Metadata.sampleRate, Is.EqualTo(16000));
            CollectionAssert.AreEqual(
                new[]
                {
                    SherpaONNXModelFileKey.EncoderAdaptor,
                    SherpaONNXModelFileKey.Llm,
                    SherpaONNXModelFileKey.Embedding,
                    SherpaONNXModelFileKey.Tokenizer
                },
                spec.RequiredFileKeys);
            Assert.That(
                spec.RequiredFiles.Single(item => item.Key == SherpaONNXModelFileKey.Tokenizer).Kind,
                Is.EqualTo(SherpaONNXModelFileKind.Directory));
            CollectionAssert.AreEqual(
                new[]
                {
                    "encoder_adaptor.int8.onnx",
                    "llm.int8.onnx",
                    "embedding.int8.onnx",
                    "Qwen3-0.6B"
                },
                spec.Metadata.fileBindings.Select(binding => binding.path).ToArray());
        }

        [Test]
        public void FunAsrChecksumOverlay_PreservesExplicitDefinitionAndBindings()
        {
            var manifest = new SherpaONNXModelManifest();
            SherpaONNXModelMetadata definition = CloneBuiltIn(FunAsrNanoInt8ModelId);
            var distribution = new SherpaONNXModelMetadata
            {
                modelId = FunAsrNanoInt8ModelId,
                downloadUrl = "https://example.invalid/funasr-nano.tar.bz2",
                downloadFileHash = "funasr-hash"
            };

            SherpaONNXConstants.MergeModelSources(
                manifest,
                new[] { definition },
                new[] { distribution },
                SherpaONNXModuleType.SpeechRecognition);

            SherpaONNXModelMetadata actual = manifest.models.Single();
            Assert.That(actual.modelTypeHint, Is.EqualTo(nameof(SpeechRecognitionModelType.Offline_FunAsrNano)));
            Assert.That(actual.runtimeFamilyHint, Is.EqualTo(nameof(SherpaONNXSpeechRecognitionRuntimeFamily.OtherSupported)));
            Assert.That(actual.fileBindings.Select(binding => binding.key), Is.EquivalentTo(new[]
            {
                SherpaONNXModelFileKey.EncoderAdaptor,
                SherpaONNXModelFileKey.Llm,
                SherpaONNXModelFileKey.Embedding,
                SherpaONNXModelFileKey.Tokenizer
            }));
            Assert.That(actual.downloadUrl, Is.EqualTo(distribution.downloadUrl));
            Assert.That(actual.downloadFileHash, Is.EqualTo(distribution.downloadFileHash));
            Assert.That(actual.runtimeProfileId, Is.EqualTo(SpeechRecognitionRuntimeProfileRegistry.OfflineFunAsrNano));
            Assert.That(actual.definitionProvenance.IsManifestBacked, Is.True);
        }

        [Test]
        public void BuiltInQwen3_UsesItsExplicitBindingsAsRequiredRoles()
        {
            SherpaONNXSpeechRecognitionModelSpec spec = ResolveBuiltIn(Qwen3AsrInt8ModelId);

            Assert.That(spec.ResolutionLevel, Is.EqualTo(SherpaONNXModelResolutionLevel.Explicit));
            Assert.That(spec.RuntimeProfileId, Is.EqualTo(SpeechRecognitionRuntimeProfileRegistry.OfflineQwen3Asr));
            Assert.That(
                spec.RequiredFiles.Single(item => item.Key == SherpaONNXModelFileKey.Tokenizer).Kind,
                Is.EqualTo(SherpaONNXModelFileKind.Directory));
            CollectionAssert.AreEqual(
                spec.Metadata.fileBindings.Select(binding => binding.key).ToArray(),
                spec.RequiredFileKeys);
        }

        [Test]
        public void ExactRuntimeProfile_RejectsMetadataThatLostARequiredBinding()
        {
            SherpaONNXModelMetadata definition = CloneBuiltIn(Nemotron35ModelId);
            definition.fileBindings.RemoveAll(binding => binding.key == SherpaONNXModelFileKey.Joiner);

            SherpaONNXSpeechRecognitionModelSpec spec =
                SpeechRecognitionModelResolver.Resolve(Nemotron35ModelId, definition);

            Assert.That(spec.CanInitialize, Is.False);
            Assert.That(spec.Status, Is.EqualTo(SherpaONNXModelResolutionStatus.MetadataConflict));
            Assert.That(spec.Diagnostic, Does.Contain("Joiner"));
        }

        [Test]
        public void ChecksumOverlay_PreservesDefinitionSemanticsAndBindings()
        {
            var manifest = new SherpaONNXModelManifest();
            SherpaONNXModelMetadata definition = CloneBuiltIn(Nemotron35ModelId);
            var distribution = new SherpaONNXModelMetadata
            {
                modelId = Nemotron35ModelId,
                downloadUrl = "https://example.invalid/nemotron.tar.bz2",
                downloadFileHash = "abc123"
            };

            SherpaONNXConstants.MergeModelSources(
                manifest,
                new[] { definition },
                new[] { distribution },
                SherpaONNXModuleType.SpeechRecognition);

            SherpaONNXModelMetadata actual = manifest.models.Single();
            Assert.That(actual.modelTypeHint, Is.EqualTo(nameof(SpeechRecognitionModelType.Online_Transducer)));
            Assert.That(actual.runtimeFamilyHint, Is.EqualTo(nameof(SherpaONNXSpeechRecognitionRuntimeFamily.NemoTransducer)));
            Assert.That(actual.fileBindings.Count, Is.EqualTo(4));
            Assert.That(actual.downloadUrl, Is.EqualTo(distribution.downloadUrl));
            Assert.That(actual.downloadFileHash, Is.EqualTo(distribution.downloadFileHash));
            Assert.That(actual.registrationSource, Is.EqualTo(SherpaONNXModelRegistrationSource.BuiltIn));
            Assert.That(actual.hasModelDefinition, Is.True);
            Assert.That(actual.runtimeProfileId, Is.EqualTo(SpeechRecognitionRuntimeProfileRegistry.OnlineNemoTransducer));
            Assert.That(actual.definitionProvenance.IsManifestBacked, Is.True);
        }

        [Test]
        public void ChecksumOnlyEntry_IsDistributionOnlyAndCannotInitialize()
        {
            const string id = "sherpa-onnx-checksum-only-test-model";
            var manifest = new SherpaONNXModelManifest();
            SherpaONNXConstants.MergeModelSources(
                manifest,
                null,
                new[] { new SherpaONNXModelMetadata { modelId = id, downloadUrl = "https://example.invalid/model.tar.bz2" } },
                SherpaONNXModuleType.SpeechRecognition);

            SherpaONNXSpeechRecognitionModelSpec spec =
                SpeechRecognitionModelResolver.Resolve(id, manifest.models.Single());

            Assert.That(spec.ResolutionLevel, Is.EqualTo(SherpaONNXModelResolutionLevel.DistributionOnly));
            Assert.That(spec.CanInitialize, Is.False);
        }

        [Test]
        public void SameId_CanExistInDifferentModuleCatalogs()
        {
            const string id = "sherpa-onnx-shared-id-test";
            var manifest = new SherpaONNXModelManifest();
            SherpaONNXConstants.MergeModelSources(
                manifest,
                new[] { new SherpaONNXModelMetadata { modelId = id } },
                null,
                SherpaONNXModuleType.SpeechRecognition);
            SherpaONNXConstants.MergeModelSources(
                manifest,
                new[] { new SherpaONNXModelMetadata { modelId = id } },
                null,
                SherpaONNXModuleType.SpeechSynthesis);

            Assert.That(manifest.models.Count(item => item.modelId == id), Is.EqualTo(2));
        }

        [Test]
        public void SameId_InSameDefinitionSource_IsRejectedAsConflict()
        {
            const string id = "sherpa-onnx-duplicate-definition-test";
            var manifest = new SherpaONNXModelManifest();

            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                SherpaONNXConstants.MergeModelSources(
                    manifest,
                    new[]
                    {
                        new SherpaONNXModelMetadata { modelId = id },
                        new SherpaONNXModelMetadata { modelId = id.ToUpperInvariant() }
                    },
                    null,
                    SherpaONNXModuleType.SpeechRecognition));

            Assert.That(error.Message, Does.Contain(id).IgnoreCase);
            Assert.That(error.Message, Does.Contain("duplicate").IgnoreCase);
        }

        [Test]
        public void ResolvedSpec_MetadataIsADefensiveSnapshot()
        {
            SherpaONNXModelMetadata source = CloneBuiltIn(Nemotron35ModelId);
            SherpaONNXSpeechRecognitionModelSpec spec =
                SpeechRecognitionModelResolver.Resolve(Nemotron35ModelId, source);

            source.modelId = "mutated-source";
            source.fileBindings.Clear();
            SherpaONNXModelMetadata exposed = spec.Metadata;
            exposed.modelId = "mutated-result";
            exposed.fileBindings.Clear();

            SherpaONNXModelMetadata reread = spec.Metadata;
            Assert.That(reread.modelId, Is.EqualTo(Nemotron35ModelId));
            Assert.That(reread.fileBindings, Has.Count.EqualTo(4));
        }

        [Test]
        public async Task LocalCustomAsrEntry_DoesNotSuppressBuiltInModuleDefinitions()
        {
            SherpaONNXModelRegistry registry = SherpaONNXModelRegistry.Instance;
            registry.Uninitialize();
            registry.RegisterCustomMetadata(new SherpaONNXModelMetadata
            {
                modelId = "custom-asr-module-load-sentinel",
                moduleType = SherpaONNXModuleType.SpeechRecognition,
                modelTypeHint = nameof(SpeechRecognitionModelType.Online_Transducer),
                runtimeFamilyHint = nameof(SherpaONNXSpeechRecognitionRuntimeFamily.ZipformerTransducer)
            });

            SherpaONNXModelMetadata builtIn = await registry.GetMetadataAsync(
                Nemotron35ModelId,
                SherpaONNXModuleType.SpeechRecognition);

            Assert.That(builtIn, Is.Not.Null);
            Assert.That(builtIn.registrationSource, Is.EqualTo(SherpaONNXModelRegistrationSource.BuiltIn));
        }

        [Test]
        public async Task PackageAndLegacyDefinitionsResolveWhenChecksumFetchIsDisabled()
        {
            const string legacyId = "sherpa-onnx-streaming-zipformer-en-2023-06-26";
            bool previous = SherpaONNXUnityAPI.GetFetchLatestManifest();
            SherpaONNXUnityAPI.SetFetchLatestManifest(false);
            try
            {
                SherpaONNXModelRegistry registry = SherpaONNXModelRegistry.Instance;
                registry.Uninitialize();

                SherpaONNXModelMetadata packageDefinition = await registry.GetMetadataAsync(
                    Nemotron35ModelId,
                    SherpaONNXModuleType.SpeechRecognition);
                SherpaONNXModelMetadata legacyDefinition = await registry.GetMetadataAsync(
                    legacyId,
                    SherpaONNXModuleType.SpeechRecognition);

                Assert.That(packageDefinition, Is.Not.Null);
                Assert.That(packageDefinition.runtimeProfileId, Is.EqualTo(SpeechRecognitionRuntimeProfileRegistry.OnlineNemoTransducer));
                Assert.That(packageDefinition.definitionProvenance.IsManifestBacked, Is.True);
                Assert.That(legacyDefinition, Is.Not.Null);
                Assert.That(legacyDefinition.runtimeProfileId, Is.Null.Or.Empty);
                Assert.That(SpeechRecognitionModelResolver.Resolve(legacyId, legacyDefinition).CanInitialize, Is.True);
            }
            finally
            {
                SherpaONNXModelRegistry.Instance.Uninitialize();
                SherpaONNXUnityAPI.SetFetchLatestManifest(previous);
            }
        }

        [Test]
        public async Task ExpectedModuleLookup_KeepsSameIdEntriesSeparate()
        {
            const string id = "shared-custom-model-id";
            SherpaONNXModelRegistry registry = SherpaONNXModelRegistry.Instance;
            registry.Uninitialize();
            registry.RegisterCustomMetadata(new SherpaONNXModelMetadata
            {
                modelId = id,
                moduleType = SherpaONNXModuleType.SpeechRecognition,
                modelTypeHint = nameof(SpeechRecognitionModelType.Online_Transducer),
                runtimeFamilyHint = nameof(SherpaONNXSpeechRecognitionRuntimeFamily.ZipformerTransducer)
            });
            registry.RegisterCustomMetadata(new SherpaONNXModelMetadata
            {
                modelId = id,
                moduleType = SherpaONNXModuleType.SpeechSynthesis
            });

            SherpaONNXModelMetadata asr = await registry.GetMetadataAsync(
                id,
                SherpaONNXModuleType.SpeechRecognition);
            SherpaONNXModelMetadata tts = await registry.GetMetadataAsync(
                id,
                SherpaONNXModuleType.SpeechSynthesis);

            Assert.That(asr.moduleType, Is.EqualTo(SherpaONNXModuleType.SpeechRecognition));
            Assert.That(tts.moduleType, Is.EqualTo(SherpaONNXModuleType.SpeechSynthesis));
        }

        private static SherpaONNXSpeechRecognitionModelSpec ResolveBuiltIn(string modelId)
        {
            var manifest = new SherpaONNXModelManifest();
            SherpaONNXConstants.MergeModelSources(
                manifest,
                new[] { CloneBuiltIn(modelId) },
                null,
                SherpaONNXModuleType.SpeechRecognition);
            return SpeechRecognitionModelResolver.Resolve(modelId, manifest.models.Single());
        }

        private static SherpaONNXModelMetadata CloneBuiltIn(string modelId)
        {
            SherpaONNXModelDefinitionManifestSnapshot snapshot =
                SherpaONNXRuntimeResourceProvider.GetSpeechRecognitionModelDefinitionsSnapshot();
            Assert.That(snapshot.IsAvailable, Is.True, "The package ASR Model Definition resource was not imported.");
            SherpaONNXModelMetadata source = SpeechRecognitionModelDefinitionManifestLoader.Parse(
                    snapshot.GetContentCopy(),
                    snapshot.SourceId)
                .Definitions
                .Single(item => item.modelId == modelId);
            return new SherpaONNXModelMetadata
            {
                modelId = source.modelId,
                moduleType = source.moduleType,
                moduleTypeHint = source.moduleTypeHint,
                modelTypeHint = source.modelTypeHint,
                runtimeFamilyHint = source.runtimeFamilyHint,
                runtimeProfileId = source.runtimeProfileId,
                definitionProvenance = source.definitionProvenance,
                sampleRate = source.sampleRate,
                fileBindings = source.fileBindings
                    .Select(binding => new SherpaONNXModelFileBinding { key = binding.key, path = binding.path })
                    .ToList()
            };
        }

        [Test]
        public void MigratedDefinitions_AreAbsentFromLegacyCSharpTable()
        {
            string[] migrated =
            {
                ZipformerModelId,
                Nemotron35ModelId,
                Qwen3AsrInt8ModelId,
                FunAsrNanoInt8ModelId,
                XAsr480MsModelId,
                FireRedAsr2Int8ModelId,
                Qwen3Asr17BModelId
            };
            CollectionAssert.IsEmpty(
                SherpaONNXConstants.Models.ASR_MODELS_METADATA_TABLES
                    .Where(item => migrated.Contains(item.modelId, StringComparer.OrdinalIgnoreCase))
                    .ToArray());
        }
    }

    public sealed class SpeechRecognitionModelDefinitionManifestTests
    {
        [Test]
        public void ValidManifest_ProducesManifestBackedDefinitionWithStableProvenance()
        {
            const string json = "{\"manifestKind\":\"sherpa-onnx-unity-model-definitions\",\"schemaVersion\":1,\"module\":\"speech-recognition\",\"definitions\":[{\"modelId\":\"test-model\",\"runtimeProfile\":\"sherpa.online.nemo-transducer.v1\",\"sampleRate\":16000,\"fileBindings\":[{\"role\":\"Encoder\",\"path\":\"encoder.onnx\"},{\"role\":\"Decoder\",\"path\":\"decoder.onnx\"},{\"role\":\"Joiner\",\"path\":\"joiner.onnx\"},{\"role\":\"Tokens\",\"path\":\"tokens.txt\"}]}]}";

            byte[] rawBytes = Encoding.UTF8.GetBytes(json);
            SherpaONNXModelDefinitionSourceResult result =
                SpeechRecognitionModelDefinitionManifestLoader.Parse(rawBytes, "test-source");

            Assert.That(result.Definitions, Has.Count.EqualTo(1));
            Assert.That(result.Definitions[0].modelId, Is.EqualTo("test-model"));
            Assert.That(result.Definitions[0].runtimeProfileId, Is.EqualTo("sherpa.online.nemo-transducer.v1"));
            Assert.That(result.Provenance.SourceId, Is.EqualTo("test-source"));
            Assert.That(result.Provenance.ManifestKind, Is.EqualTo("sherpa-onnx-unity-model-definitions"));
            Assert.That(result.Provenance.SchemaVersion, Is.EqualTo(1));
            Assert.That(result.Provenance.ContentSha256, Has.Length.EqualTo(64));
            using (var sha = SHA256.Create())
            {
                string expected = string.Concat(sha.ComputeHash(rawBytes).Select(value => value.ToString("x2")));
                Assert.That(result.Provenance.ContentSha256, Is.EqualTo(expected));
            }
        }

        [Test]
        public void DuplicateJsonProperty_IsRejectedAtTheManifestSeam()
        {
            const string json = "{\"manifestKind\":\"sherpa-onnx-unity-model-definitions\",\"manifestKind\":\"shadow\",\"schemaVersion\":1,\"module\":\"speech-recognition\",\"definitions\":[]}";

            Assert.Throws<InvalidDataException>(() => Parse(json));
        }

        [Test]
        public void UnknownJsonProperty_IsRejectedAtTheManifestSeam()
        {
            const string json = "{\"manifestKind\":\"sherpa-onnx-unity-model-definitions\",\"schemaVersion\":1,\"module\":\"speech-recognition\",\"definitions\":[],\"unexpected\":true}";

            Assert.Throws<InvalidDataException>(() => Parse(json));
        }

        [Test]
        public void UnknownRuntimeProfile_IsRejectedBeforeRegistryMerge()
        {
            const string json = "{\"manifestKind\":\"sherpa-onnx-unity-model-definitions\",\"schemaVersion\":1,\"module\":\"speech-recognition\",\"definitions\":[{\"modelId\":\"test-model\",\"runtimeProfile\":\"unknown.profile\",\"sampleRate\":16000}]}";

            Assert.Throws<InvalidDataException>(() => Parse(json));
        }

        [TestCase(2, "speech-recognition")]
        [TestCase(1, "speech-synthesis")]
        public void UnknownSchemaOrModule_IsRejected(int schemaVersion, string module)
        {
            string json = "{\"manifestKind\":\"sherpa-onnx-unity-model-definitions\",\"schemaVersion\":"
                          + schemaVersion
                          + ",\"module\":\""
                          + module
                          + "\",\"definitions\":[{\"modelId\":\"test\",\"runtimeProfile\":\"sherpa.online.zipformer-transducer.v1\",\"sampleRate\":16000}]}";

            Assert.Throws<InvalidDataException>(() => Parse(json));
        }

        [Test]
        public void MissingRequiredProperty_IsRejected()
        {
            const string json = "{\"manifestKind\":\"sherpa-onnx-unity-model-definitions\",\"schemaVersion\":1,\"module\":\"speech-recognition\",\"definitions\":[{\"modelId\":\"test\",\"sampleRate\":16000}]}";

            Assert.Throws<InvalidDataException>(() => Parse(json));
        }

        [Test]
        public void UnknownFileRole_IsRejected()
        {
            const string json = "{\"manifestKind\":\"sherpa-onnx-unity-model-definitions\",\"schemaVersion\":1,\"module\":\"speech-recognition\",\"definitions\":[{\"modelId\":\"test\",\"runtimeProfile\":\"sherpa.online.nemo-transducer.v1\",\"sampleRate\":16000,\"fileBindings\":[{\"role\":\"Weights\",\"path\":\"encoder.onnx\"}]}]}";

            Assert.Throws<InvalidDataException>(() => Parse(json));
        }

        [TestCase("../encoder.onnx")]
        [TestCase("C:/models/encoder.onnx")]
        [TestCase("//server/share/encoder.onnx")]
        [TestCase("https://example.invalid/encoder.onnx")]
        [TestCase("./encoder.onnx")]
        [TestCase("nested\\encoder.onnx")]
        public void UnsafeOrNonCanonicalBindingPath_IsRejected(string path)
        {
            string json = "{\"manifestKind\":\"sherpa-onnx-unity-model-definitions\",\"schemaVersion\":1,\"module\":\"speech-recognition\",\"definitions\":[{\"modelId\":\"test-model\",\"runtimeProfile\":\"sherpa.online.nemo-transducer.v1\",\"sampleRate\":16000,\"fileBindings\":[{\"role\":\"Encoder\",\"path\":\""
                          + path.Replace("\\", "\\\\")
                          + "\"},{\"role\":\"Decoder\",\"path\":\"decoder.onnx\"},{\"role\":\"Joiner\",\"path\":\"joiner.onnx\"},{\"role\":\"Tokens\",\"path\":\"tokens.txt\"}]}]}";

            Assert.Throws<InvalidDataException>(() => Parse(json));
        }

        [Test]
        public void DuplicateNormalizedModelId_IsRejected()
        {
            const string json = "{\"manifestKind\":\"sherpa-onnx-unity-model-definitions\",\"schemaVersion\":1,\"module\":\"speech-recognition\",\"definitions\":[{\"modelId\":\"Same-ID\",\"runtimeProfile\":\"sherpa.online.zipformer-transducer.v1\",\"sampleRate\":16000},{\"modelId\":\"same-id\",\"runtimeProfile\":\"sherpa.online.zipformer-transducer.v1\",\"sampleRate\":16000}]}";

            Assert.Throws<InvalidDataException>(() => Parse(json));
        }

        [Test]
        public void DuplicateBindingRole_IsRejected()
        {
            const string json = "{\"manifestKind\":\"sherpa-onnx-unity-model-definitions\",\"schemaVersion\":1,\"module\":\"speech-recognition\",\"definitions\":[{\"modelId\":\"test\",\"runtimeProfile\":\"sherpa.online.nemo-transducer.v1\",\"sampleRate\":16000,\"fileBindings\":[{\"role\":\"Encoder\",\"path\":\"encoder.onnx\"},{\"role\":\"Encoder\",\"path\":\"encoder-2.onnx\"},{\"role\":\"Decoder\",\"path\":\"decoder.onnx\"},{\"role\":\"Joiner\",\"path\":\"joiner.onnx\"},{\"role\":\"Tokens\",\"path\":\"tokens.txt\"}]}]}";

            Assert.Throws<InvalidDataException>(() => Parse(json));
        }

        [Test]
        public void MissingExactBinding_IsRejected()
        {
            const string json = "{\"manifestKind\":\"sherpa-onnx-unity-model-definitions\",\"schemaVersion\":1,\"module\":\"speech-recognition\",\"definitions\":[{\"modelId\":\"test\",\"runtimeProfile\":\"sherpa.offline.qwen3-asr.v1\",\"sampleRate\":16000,\"fileBindings\":[{\"role\":\"ConvFrontend\",\"path\":\"conv.onnx\"},{\"role\":\"Encoder\",\"path\":\"encoder.onnx\"},{\"role\":\"Decoder\",\"path\":\"decoder.onnx\"}]}]}";

            Assert.Throws<InvalidDataException>(() => Parse(json));
        }

        [Test]
        public void TypeInjectionProperty_IsRejected()
        {
            const string json = "{\"manifestKind\":\"sherpa-onnx-unity-model-definitions\",\"schemaVersion\":1,\"module\":\"speech-recognition\",\"definitions\":[],\"$type\":\"System.Version\"}";

            Assert.Throws<InvalidDataException>(() => Parse(json));
        }

        [Test]
        public void TrailingJsonDocument_IsRejected()
        {
            const string json = "{\"manifestKind\":\"sherpa-onnx-unity-model-definitions\",\"schemaVersion\":1,\"module\":\"speech-recognition\",\"definitions\":[]} {}";

            Assert.Throws<InvalidDataException>(() => Parse(json));
        }

        private static SherpaONNXModelDefinitionSourceResult Parse(string json)
        {
            return SpeechRecognitionModelDefinitionManifestLoader.Parse(
                Encoding.UTF8.GetBytes(json),
                "test-source");
        }
    }

    public sealed class OfflineSpeechRecognizerComponentContractTests
    {
        [Test]
        public void TranscriptionTimings_DefaultAndUninstrumentedResult_AreUnavailable()
        {
            var timings = default(SpeechRecognition.TranscriptionTimings);
            var result = new SpeechRecognition.TranscriptionResult(
                SpeechRecognition.TranscriptionStatus.NotReady);

            Assert.That(timings.IsAvailable, Is.False);
            Assert.That(timings.ModuleTotal, Is.EqualTo(TimeSpan.Zero));
            Assert.That(result.Timings.IsAvailable, Is.False);
        }

        [Test]
        public void TranscriptionResult_PreservesCompleteOfflineTimingBreakdown()
        {
            var timings = new SpeechRecognition.TranscriptionTimings(
                moduleSemaphoreWait: TimeSpan.FromMilliseconds(1),
                workerDispatchWait: TimeSpan.FromMilliseconds(2),
                streamCreate: TimeSpan.FromMilliseconds(3),
                acceptWaveform: TimeSpan.FromMilliseconds(4),
                offlineDecodeCall: TimeSpan.FromMilliseconds(5),
                resultMaterialization: TimeSpan.FromMilliseconds(6),
                postProcessing: TimeSpan.FromMilliseconds(7),
                streamDispose: TimeSpan.FromMilliseconds(8),
                workerTotal: TimeSpan.FromMilliseconds(35),
                moduleTotal: TimeSpan.FromMilliseconds(40));

            var result = new SpeechRecognition.TranscriptionResult(
                SpeechRecognition.TranscriptionStatus.Success,
                text: "fixture result",
                isFinal: true,
                timings: timings);

            Assert.That(result.Timings.IsAvailable, Is.True);
            Assert.That(result.Timings.ModuleSemaphoreWait.TotalMilliseconds, Is.EqualTo(1d));
            Assert.That(result.Timings.WorkerDispatchWait.TotalMilliseconds, Is.EqualTo(2d));
            Assert.That(result.Timings.StreamCreate.TotalMilliseconds, Is.EqualTo(3d));
            Assert.That(result.Timings.AcceptWaveform.TotalMilliseconds, Is.EqualTo(4d));
            Assert.That(result.Timings.OfflineDecodeCall.TotalMilliseconds, Is.EqualTo(5d));
            Assert.That(result.Timings.ResultMaterialization.TotalMilliseconds, Is.EqualTo(6d));
            Assert.That(result.Timings.PostProcessing.TotalMilliseconds, Is.EqualTo(7d));
            Assert.That(result.Timings.StreamDispose.TotalMilliseconds, Is.EqualTo(8d));
            Assert.That(result.Timings.WorkerTotal.TotalMilliseconds, Is.EqualTo(35d));
            Assert.That(result.Timings.ModuleTotal.TotalMilliseconds, Is.EqualTo(40d));
        }

        [Test]
        public async Task DirectTranscription_WhenRecognizerIsNotReady_ReturnsStructuredNotReady()
        {
            var host = new GameObject("offline-recognizer-contract-test");
            try
            {
                var recognizer = host.AddComponent<OfflineSpeechRecognizerComponent>();

                SpeechRecognition.TranscriptionResult result = await recognizer.TranscribeSamplesAsync(
                    new float[160],
                    16000);

                Assert.That(result.Status, Is.EqualTo(SpeechRecognition.TranscriptionStatus.NotReady));
                Assert.That(result.Timings.IsAvailable, Is.False);
                Assert.That(recognizer.ActiveTranscriptionCount, Is.Zero);
                Assert.That(recognizer.PendingSegmentCount, Is.Zero);
                Assert.That(recognizer.DroppedSegmentCount, Is.Zero);
                Assert.That(recognizer.BusySegmentCount, Is.Zero);
                Assert.That(recognizer.ModelLoadDuration, Is.EqualTo(TimeSpan.Zero));
                Assert.That(recognizer.WarmUpDuration, Is.EqualTo(TimeSpan.Zero));
                Assert.That(recognizer.WasWarmedUp, Is.False);

                recognizer.ClearPendingSegments();
                await recognizer.CancelAndDrainAsync();
                await recognizer.DisposeModuleAsync();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
