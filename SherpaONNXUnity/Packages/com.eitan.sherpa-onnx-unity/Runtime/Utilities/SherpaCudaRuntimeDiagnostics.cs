using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Eitan.SherpaONNXUnity.Runtime;

namespace Eitan.SherpaONNXUnity.Runtime.Utilities
{
    public enum SherpaCudaProviderDiagnosticStatus
    {
        NotApplicable,
        Passed,
        Failed,
        InspectionUnavailable
    }

    public enum SherpaCudaProviderDiagnosticStage
    {
        None,
        Preflight,
        PostInitializationWarmup,
        PostDecode
    }

    /// <summary>
    /// One system CUDA/cuDNN dependency observation. The path is empty when the
    /// dependency was not found or the platform did not support inspection.
    /// </summary>
    public readonly struct SherpaCudaDependencyObservation
    {
        public SherpaCudaDependencyObservation(string name, bool discoverable, string path)
        {
            Name = name ?? string.Empty;
            Discoverable = discoverable;
            Path = path ?? string.Empty;
        }

        public string Name { get; }
        public bool Discoverable { get; }
        public string Path { get; }
    }

    /// <summary>
    /// One relevant native module observed in the current Unity process.
    /// </summary>
    public readonly struct SherpaCudaLoadedModuleObservation
    {
        public SherpaCudaLoadedModuleObservation(string name, string path, string fileVersion)
        {
            Name = name ?? string.Empty;
            Path = path ?? string.Empty;
            FileVersion = fileVersion ?? string.Empty;
        }

        public string Name { get; }
        public string Path { get; }
        public string FileVersion { get; }
    }

    /// <summary>
    /// Immutable, case-level evidence about a requested CUDA provider. A passed
    /// snapshot proves provider/module loading at the sampling point; it does not
    /// claim that every ONNX operator executed on the GPU.
    /// </summary>
    public sealed class SherpaCudaProviderDiagnostics
    {
        public const string SchemaVersion = "cuda-provider-diagnostics/1.0.0";

        internal SherpaCudaProviderDiagnostics(
            SherpaONNXExecutionProvider requestedProvider,
            SherpaCudaProviderDiagnosticStatus status,
            SherpaCudaProviderDiagnosticStage stage,
            DateTime checkedUtc,
            int processId,
            bool systemDependenciesChecked,
            bool systemDependenciesAvailable,
            IEnumerable<SherpaCudaDependencyObservation> systemDependencies,
            bool providerModuleLoaded,
            IEnumerable<SherpaCudaLoadedModuleObservation> loadedModules,
            IEnumerable<string> missingSystemDependencies,
            IEnumerable<string> missingLoadedModules,
            string message)
        {
            RequestedProvider = requestedProvider;
            Status = status;
            Stage = stage;
            CheckedUtc = checkedUtc;
            ProcessId = processId;
            SystemDependenciesChecked = systemDependenciesChecked;
            SystemDependenciesAvailable = systemDependenciesAvailable;
            SystemDependencies = ReadOnly(systemDependencies);
            ProviderModuleLoaded = providerModuleLoaded;
            LoadedModules = ReadOnly(loadedModules);
            MissingSystemDependencies = ReadOnly(missingSystemDependencies);
            MissingLoadedModules = ReadOnly(missingLoadedModules);
            Message = message ?? string.Empty;
        }

        public string Schema => SchemaVersion;
        public SherpaONNXExecutionProvider RequestedProvider { get; }
        public SherpaCudaProviderDiagnosticStatus Status { get; }
        public SherpaCudaProviderDiagnosticStage Stage { get; }
        public DateTime CheckedUtc { get; }
        public int ProcessId { get; }
        public bool SystemDependenciesChecked { get; }
        public bool SystemDependenciesAvailable { get; }
        public IReadOnlyList<SherpaCudaDependencyObservation> SystemDependencies { get; }
        public bool ProviderModuleLoaded { get; }
        public IReadOnlyList<SherpaCudaLoadedModuleObservation> LoadedModules { get; }
        public IReadOnlyList<string> MissingSystemDependencies { get; }
        public IReadOnlyList<string> MissingLoadedModules { get; }
        public string Message { get; }
        public bool IsPassed => Status == SherpaCudaProviderDiagnosticStatus.Passed;
        public bool IsApplicable => RequestedProvider == SherpaONNXExecutionProvider.Cuda;

        public override string ToString()
        {
            return string.Format(
                "{0} ({1}, provider={2}): {3}",
                Status,
                Stage,
                RequestedProvider,
                Message);
        }

        private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values)
        {
            return new ReadOnlyCollection<T>((values ?? Enumerable.Empty<T>()).ToArray());
        }
    }

    /// <summary>
    /// Performs conservative Windows-only checks for the system CUDA runtime used
    /// by the CUDA-enabled ONNX Runtime binary. NVIDIA DLLs are intentionally not
    /// bundled. The old bool/string methods remain compatibility shims over the
    /// structured snapshot implementation.
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

        public static readonly string[] RequiredLoadedDlls =
            RequiredSystemDlls.Concat(new[] { "onnxruntime_providers_cuda.dll" }).ToArray();

        public static SherpaCudaProviderDiagnostics CreateNotApplicable(
            SherpaONNXExecutionProvider requestedProvider = SherpaONNXExecutionProvider.Cpu)
        {
            return new SherpaCudaProviderDiagnostics(
                requestedProvider,
                SherpaCudaProviderDiagnosticStatus.NotApplicable,
                SherpaCudaProviderDiagnosticStage.None,
                DateTime.UtcNow,
                -1,
                false,
                false,
                null,
                false,
                null,
                null,
                null,
                requestedProvider == SherpaONNXExecutionProvider.Cuda
                    ? "CUDA diagnostics have not been captured yet."
                    : "CUDA provider was not requested.");
        }

        public static SherpaCudaProviderDiagnostics CaptureSystemDependencies()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            DateTime checkedUtc = DateTime.UtcNow;
            var observations = RequiredSystemDlls
                .Select(name => ObserveDependency(name))
                .ToArray();
            var missing = observations
                .Where(item => !item.Discoverable)
                .Select(item => item.Name)
                .ToArray();
            bool passed = missing.Length == 0;
            string message = passed
                ? "CUDA 13.x/cuDNN 9.x system DLLs are discoverable through PATH."
                : "Missing CUDA/cuDNN system DLL(s): " + string.Join(", ", missing)
                  + ". Install CUDA Toolkit 13.x and cuDNN 9.x, add their bin directories to PATH, then restart Unity.";

            return new SherpaCudaProviderDiagnostics(
                SherpaONNXExecutionProvider.Cuda,
                passed
                    ? SherpaCudaProviderDiagnosticStatus.Passed
                    : SherpaCudaProviderDiagnosticStatus.Failed,
                SherpaCudaProviderDiagnosticStage.Preflight,
                checkedUtc,
                CurrentProcessId(),
                true,
                passed,
                observations,
                false,
                null,
                missing,
                null,
                message);
#else
            return UnsupportedPlatformSnapshot(
                SherpaCudaProviderDiagnosticStage.Preflight,
                "CUDA system dependency inspection is only supported on Windows x64.");
#endif
        }

        public static SherpaCudaProviderDiagnostics CaptureLoadedCudaProvider(
            SherpaCudaProviderDiagnosticStage stage = SherpaCudaProviderDiagnosticStage.PostInitializationWarmup)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            SherpaCudaProviderDiagnostics preflight = CaptureSystemDependencies();
            try
            {
                ProcessModule[] processModules = Process.GetCurrentProcess().Modules
                    .Cast<ProcessModule>()
                    .ToArray();
                var relevant = processModules
                    .Where(module => IsRelevantModule(module.ModuleName))
                    .Select(ToModuleObservation)
                    .OrderBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var loadedNames = new HashSet<string>(
                    processModules.Select(module => module.ModuleName),
                    StringComparer.OrdinalIgnoreCase);
                var missingLoaded = RequiredLoadedDlls
                    .Where(name => !loadedNames.Contains(name))
                    .ToArray();
                bool passed = preflight.SystemDependenciesAvailable && missingLoaded.Length == 0;
                string message = passed
                    ? "ONNX Runtime CUDA provider and its required CUDA/cuDNN dependencies are loaded in the Unity process."
                    : BuildLoadedFailureMessage(preflight, missingLoaded);

                return new SherpaCudaProviderDiagnostics(
                    SherpaONNXExecutionProvider.Cuda,
                    passed
                        ? SherpaCudaProviderDiagnosticStatus.Passed
                        : SherpaCudaProviderDiagnosticStatus.Failed,
                    stage,
                    DateTime.UtcNow,
                    CurrentProcessId(),
                    preflight.SystemDependenciesChecked,
                    preflight.SystemDependenciesAvailable,
                    preflight.SystemDependencies,
                    loadedNames.Contains("onnxruntime_providers_cuda.dll"),
                    relevant,
                    preflight.MissingSystemDependencies,
                    missingLoaded,
                    message);
            }
            catch (Exception ex)
            {
                return new SherpaCudaProviderDiagnostics(
                    SherpaONNXExecutionProvider.Cuda,
                    SherpaCudaProviderDiagnosticStatus.InspectionUnavailable,
                    stage,
                    DateTime.UtcNow,
                    CurrentProcessId(),
                    preflight.SystemDependenciesChecked,
                    preflight.SystemDependenciesAvailable,
                    preflight.SystemDependencies,
                    false,
                    null,
                    preflight.MissingSystemDependencies,
                    RequiredLoadedDlls,
                    "Could not inspect loaded native modules: " + ex.Message);
            }
#else
            return UnsupportedPlatformSnapshot(
                stage,
                "CUDA provider module inspection is only supported on Windows x64.");
#endif
        }

        public static bool CheckSystemDependencies(out string message)
        {
            SherpaCudaProviderDiagnostics snapshot = CaptureSystemDependencies();
            message = snapshot.Message;
            return snapshot.IsPassed;
        }

        public static bool CheckLoadedCudaProvider(out string message)
        {
            SherpaCudaProviderDiagnostics snapshot = CaptureLoadedCudaProvider();
            message = snapshot.Message;
            return snapshot.IsPassed;
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private static SherpaCudaDependencyObservation ObserveDependency(string name)
        {
            string path = FindOnPath(name);
            return new SherpaCudaDependencyObservation(name, !string.IsNullOrEmpty(path), path);
        }

        private static string FindOnPath(string name)
        {
            string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string rawEntry in pathVariable.Split(Path.PathSeparator))
            {
                string entry = rawEntry?.Trim();
                if (string.IsNullOrEmpty(entry)) continue;
                try
                {
                    string candidate = Path.Combine(entry, name);
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
                catch
                {
                    // Keep checking the remaining PATH entries; the snapshot records
                    // the dependency as missing if none can be inspected.
                }
            }

            return string.Empty;
        }

        private static SherpaCudaLoadedModuleObservation ToModuleObservation(ProcessModule module)
        {
            string path = string.Empty;
            string version = string.Empty;
            try { path = module.FileName ?? string.Empty; } catch { }
            try
            {
                version = string.IsNullOrEmpty(path)
                    ? string.Empty
                    : FileVersionInfo.GetVersionInfo(path).FileVersion ?? string.Empty;
            }
            catch { }
            return new SherpaCudaLoadedModuleObservation(module.ModuleName, path, version);
        }

        private static bool IsRelevantModule(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.Equals("onnxruntime.dll", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("onnxruntime_providers_cuda.dll", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("onnxruntime_providers_shared.dll", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("sherpa-onnx-c-api.dll", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("cublas", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("cufft", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("cudnn", StringComparison.OrdinalIgnoreCase);
        }

        private static int CurrentProcessId()
        {
            try { return Process.GetCurrentProcess().Id; }
            catch { return -1; }
        }
#endif

        private static string BuildLoadedFailureMessage(
            SherpaCudaProviderDiagnostics preflight,
            IEnumerable<string> missingLoaded)
        {
            var parts = new List<string>();
            if (!preflight.SystemDependenciesAvailable)
            {
                parts.Add(preflight.Message);
            }

            string[] missing = (missingLoaded ?? Enumerable.Empty<string>()).ToArray();
            if (missing.Length > 0)
            {
                parts.Add("Missing loaded module(s): " + string.Join(", ", missing));
            }

            parts.Add("The CUDA request was rejected; no CPU fallback was selected.");
            return "CUDA provider was not fully observed after initialization. " + string.Join(" ", parts);
        }

        private static SherpaCudaProviderDiagnostics UnsupportedPlatformSnapshot(
            SherpaCudaProviderDiagnosticStage stage,
            string message)
        {
            return new SherpaCudaProviderDiagnostics(
                SherpaONNXExecutionProvider.Cuda,
                SherpaCudaProviderDiagnosticStatus.InspectionUnavailable,
                stage,
                DateTime.UtcNow,
                -1,
                false,
                false,
                null,
                false,
                null,
                null,
                RequiredLoadedDlls,
                message);
        }
    }
}
