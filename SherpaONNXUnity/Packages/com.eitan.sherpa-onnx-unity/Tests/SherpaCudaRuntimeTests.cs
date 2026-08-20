using Eitan.SherpaONNXUnity.Runtime;
using Eitan.SherpaONNXUnity.Runtime.Modules;
using Eitan.SherpaONNXUnity.Runtime.Utilities;
using NUnit.Framework;

namespace Eitan.SherpaONNXUnity.Tests
{
    public sealed class SherpaCudaRuntimeTests
    {
        [Test]
        public void SpeechRecognitionDefaultsToCpuWithWarmupEnabled()
        {
            var options = new SpeechRecognition.Options();

            Assert.That(options.ExecutionProvider, Is.EqualTo(SherpaONNXExecutionProvider.Cpu));
            Assert.That(options.WarmUpOnInitialization, Is.True);
        }

        [Test]
        public void CudaRuntimeManifestListsExpectedSystemDependencies()
        {
            CollectionAssert.AreEquivalent(
                new[] { "cublasLt64_13.dll", "cublas64_13.dll", "cufft64_12.dll", "cudnn64_9.dll" },
                SherpaCudaRuntimeDiagnostics.RequiredSystemDlls);
        }
    }
}
