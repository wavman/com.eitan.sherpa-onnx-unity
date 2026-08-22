using System.Linq;
using Eitan.SherpaONNXUnity.Runtime;
using Eitan.SherpaONNXUnity.Runtime.Constants;
using Eitan.SherpaONNXUnity.Runtime.Modules;
using NUnit.Framework;

namespace Eitan.SherpaONNXUnity.Tests
{
    public sealed class SpeechRecognitionDecodingPolicyTests
    {
        private const string Nemotron35ModelId =
            "sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-560ms-int8-2026-06-11";

        [TestCase(
            "sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-560ms-int8-2026-06-11",
            SpeechRecognitionModelType.Online_Transducer,
            "greedy_search")]
        [TestCase(
            "sherpa-onnx-nemotron-speech-streaming-en-0.6b-int8-2026-01-14",
            SpeechRecognitionModelType.Online_Transducer,
            "greedy_search")]
        [TestCase(
            "sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20",
            SpeechRecognitionModelType.Online_Transducer,
            "modified_beam_search")]
        [TestCase(
            "sherpa-onnx-streaming-zipformer-small-ctc-zh-int8-2025-04-01",
            SpeechRecognitionModelType.Online_Ctc,
            "greedy_search")]
        public void ResolveOnlineDecodingMethod_RespectsModelFamilyCapability(
            string modelId,
            SpeechRecognitionModelType modelType,
            string expected)
        {
            Assert.That(
                SpeechRecognition.ResolveOnlineDecodingMethod(modelId, modelType),
                Is.EqualTo(expected));
        }

        [Test]
        public void BuiltInMetadata_RegistersNemotronTransducerFiles()
        {
            SherpaONNXModelMetadata metadata = SherpaONNXConstants.Models.ASR_MODELS_METADATA_TABLES
                .Single(item => item.modelId == Nemotron35ModelId);

            Assert.That(metadata.modelTypeHint, Is.EqualTo(nameof(SpeechRecognitionModelType.Online_Transducer)));
            CollectionAssert.AreEquivalent(
                new[] { "encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt" },
                metadata.fileBindings.Select(binding => binding.path).ToArray());
        }
    }
}
