using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Eitan.SherpaONNXUnity.Runtime.Utilities
{
    /// <summary>
    /// Performs conservative, Windows-only checks for the system CUDA runtime used by
    /// the CUDA-enabled ONNX Runtime binary. NVIDIA DLLs are intentionally not bundled.
    /// </summary>
    public static class SherpaCudaRuntimeDiagnostics
    {
        public static readonly string[] RequiredSystemDlls =
        {
            "cublasLt64_13.dll",
            "cublas64_13.dll",
            "cufft64_12.dll",
            "cudnn64_9.dll"
        };

        public static bool CheckSystemDependencies(out string message)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            var missing = RequiredSystemDlls
                .Where(name => !pathEntries.Any(directory =>
                    File.Exists(Path.Combine(directory.Trim(), name))))
                .ToArray();

            if (missing.Length > 0)
            {
                message = "Missing CUDA/cuDNN system DLL(s): " + string.Join(", ", missing) +
                    ". Install CUDA Toolkit 13.x and cuDNN 9.x, add their bin directories to PATH, then restart Unity.";
                return false;
            }

            message = "CUDA 13.x/cuDNN 9.x system DLLs are discoverable through PATH.";
            return true;
#else
            message = "CUDA execution provider is only supported by this fork on Windows x64.";
            return false;
#endif
        }

        public static bool CheckLoadedCudaProvider(out string message)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try
            {
                var loadedModules = new HashSet<string>(
                    Process.GetCurrentProcess().Modules
                        .Cast<ProcessModule>()
                        .Select(module => module.ModuleName),
                    StringComparer.OrdinalIgnoreCase);

                var missing = new List<string>();
                foreach (var name in RequiredSystemDlls.Concat(new[] { "onnxruntime_providers_cuda.dll" }))
                {
                    if (!loadedModules.Contains(name))
                    {
                        missing.Add(name);
                    }
                }

                if (missing.Count > 0)
                {
                    message = "CUDA provider was not observed in the Unity process after initialization. " +
                        "Missing loaded module(s): " + string.Join(", ", missing) +
                        ". The CUDA request was rejected; no CPU fallback was selected.";
                    return false;
                }

                message = "ONNX Runtime CUDA provider and its CUDA/cuDNN dependencies are loaded in the Unity process.";
                return true;
            }
            catch (Exception ex)
            {
                message = "Could not inspect loaded native modules: " + ex.Message;
                return false;
            }
#else
            message = "CUDA provider module inspection is only supported on Windows x64.";
            return false;
#endif
        }
    }
}
