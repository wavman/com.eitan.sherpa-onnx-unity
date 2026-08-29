namespace Eitan.SherpaONNXUnity.Tests
{
    using Eitan.Sherpa.Onnx.Unity.Mono.Components;
    using Eitan.SherpaONNXUnity.Runtime.Modules;
    using NUnit.Framework;

    public sealed class StreamingTranscriptPublicationPolicyTests
    {
        [Test]
        public void SameTextPartialThenFinal_PublishesBothStateTransitions()
        {
            var policy = new StreamingTranscriptPublicationPolicy();
            var partial = Result("same transcript", isFinal: false);
            var final = Result("same transcript", isFinal: true);

            Assert.That(policy.ShouldPublish(partial, deduplicate: true), Is.True);
            Assert.That(policy.ShouldPublish(final, deduplicate: true), Is.True);
        }

        [Test]
        public void RepeatedPartial_IsSuppressedWhenDeduplicationIsEnabled()
        {
            var policy = new StreamingTranscriptPublicationPolicy();
            var partial = Result("same transcript", isFinal: false);

            Assert.That(policy.ShouldPublish(partial, deduplicate: true), Is.True);
            Assert.That(policy.ShouldPublish(partial, deduplicate: true), Is.False);
        }

        [Test]
        public void FinalEndsDeduplicationStateForNextUtterance()
        {
            var policy = new StreamingTranscriptPublicationPolicy();
            var partial = Result("repeatable command", isFinal: false);
            var final = Result("repeatable command", isFinal: true);

            Assert.That(policy.ShouldPublish(partial, deduplicate: true), Is.True);
            Assert.That(policy.ShouldPublish(final, deduplicate: true), Is.True);
            Assert.That(policy.ShouldPublish(partial, deduplicate: true), Is.True);
        }

        [Test]
        public void DeduplicationDisabled_PublishesRepeatedPartials()
        {
            var policy = new StreamingTranscriptPublicationPolicy();
            var partial = Result("same transcript", isFinal: false);

            Assert.That(policy.ShouldPublish(partial, deduplicate: false), Is.True);
            Assert.That(policy.ShouldPublish(partial, deduplicate: false), Is.True);
        }

        private static SpeechRecognition.TranscriptionResult Result(string text, bool isFinal)
        {
            return new SpeechRecognition.TranscriptionResult(
                SpeechRecognition.TranscriptionStatus.Success,
                text,
                isFinal);
        }
    }
}
