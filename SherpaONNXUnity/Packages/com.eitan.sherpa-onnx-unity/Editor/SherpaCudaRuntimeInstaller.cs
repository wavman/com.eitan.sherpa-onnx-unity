using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Eitan.SherpaONNXUnity.Runtime.Utilities;
using UnityEditor;
using UnityEngine;

namespace Eitan.SherpaONNXUnity.Editor
{
    internal static class SherpaCudaRuntimeInstaller
    {
        private const string DefaultArchiveUrl =
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.13.6/" +
            "sherpa-onnx-v1.13.6-cuda-13.x-cudnn-9.x-onnxruntime1.27.1-win-x64-cuda.tar.bz2";
        private const long DefaultArchiveSize = 428595212L;
        private const string DefaultArchiveSha256 = "fe4cd35e0639b29eb41e72cfb4460ca9588f1fdd3cf680527b6c2395f2c84aa7";
        private const string DefaultSherpaVersion = "1.13.6";
        private const string DefaultOnnxRuntimeVersion = "1.27.1";
        private const string PackageRelativeDirectory = "Assets/Plugins/SherpaONNX/Windows/x86_64";
        private static readonly string[] RuntimeDlls =
        {
            "onnxruntime.dll",
            "onnxruntime_providers_cuda.dll",
            "onnxruntime_providers_shared.dll",
            "sherpa-onnx-c-api.dll"
        };

        [Serializable]
        private sealed class InstalledRuntimeInfo
        {
            public string sherpaVersion;
            public string onnxRuntimeVersion;
            public string archiveUrl;
            public string archiveSha256;
            public string installedUtc;
            public string[] files;
        }

        [Serializable]
        private sealed class RuntimeManifest
        {
            public string sherpaVersion = DefaultSherpaVersion;
            public string onnxRuntimeVersion = DefaultOnnxRuntimeVersion;
            public string archiveUrl = DefaultArchiveUrl;
            public long archiveSize = DefaultArchiveSize;
            public string archiveSha256 = DefaultArchiveSha256;
            public string[] files = RuntimeDlls;
        }

        [MenuItem("Tools/SherpaONNX/CUDA Runtime/Install or Repair")]
        private static void InstallOrRepair()
        {
            var manifest = LoadManifest();
            var projectDirectory = Directory.GetParent(Application.dataPath).FullName;
            var cacheDirectory = Path.Combine(projectDirectory, "Library", "SherpaONNX", "CudaRuntime");
            var archivePath = Path.Combine(cacheDirectory, Path.GetFileName(new Uri(manifest.archiveUrl).AbsolutePath));
            var extractDirectory = Path.Combine(cacheDirectory, "extracted");

            try
            {
                Directory.CreateDirectory(cacheDirectory);
                if (!File.Exists(archivePath) || new FileInfo(archivePath).Length != manifest.archiveSize ||
                    !string.Equals(ComputeSha256(archivePath), manifest.archiveSha256, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(archivePath))
                    {
                        File.Delete(archivePath);
                    }

                    EditorUtility.DisplayProgressBar("SherpaONNX CUDA runtime", "Downloading official release...", 0.1f);
                    using (var client = new WebClient())
                    {
                        client.DownloadFile(manifest.archiveUrl, archivePath);
                    }
                }

                EditorUtility.DisplayProgressBar("SherpaONNX CUDA runtime", "Verifying archive...", 0.35f);
                if (new FileInfo(archivePath).Length != manifest.archiveSize ||
                    !string.Equals(ComputeSha256(archivePath), manifest.archiveSha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(archivePath);
                    throw new InvalidDataException("The downloaded archive failed its pinned size/SHA-256 verification.");
                }

                if (Directory.Exists(extractDirectory))
                {
                    Directory.Delete(extractDirectory, true);
                }
                Directory.CreateDirectory(extractDirectory);

                EditorUtility.DisplayProgressBar("SherpaONNX CUDA runtime", "Extracting selected DLLs...", 0.55f);
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "tar.exe",
                    Arguments = $"-xjf \"{archivePath}\" -C \"{extractDirectory}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException("tar.exe failed: " + process.StandardError.ReadToEnd());
                    }
                }

                var destinationDirectory = Path.Combine(projectDirectory, PackageRelativeDirectory.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(destinationDirectory);
                foreach (var dll in manifest.files)
                {
                    var source = Directory.GetFiles(extractDirectory, dll, SearchOption.AllDirectories).FirstOrDefault();
                    if (string.IsNullOrEmpty(source))
                    {
                        throw new FileNotFoundException("The official archive did not contain the required DLL.", dll);
                    }
                    File.Copy(source, Path.Combine(destinationDirectory, dll), true);
                }

                var info = new InstalledRuntimeInfo
                {
                    sherpaVersion = manifest.sherpaVersion,
                    onnxRuntimeVersion = manifest.onnxRuntimeVersion,
                    archiveUrl = manifest.archiveUrl,
                    archiveSha256 = manifest.archiveSha256,
                    installedUtc = DateTime.UtcNow.ToString("O"),
                    files = manifest.files
                };
                File.WriteAllText(
                    Path.Combine(destinationDirectory, "sherpa-cuda-runtime.json"),
                    JsonUtility.ToJson(info, true),
                    Encoding.UTF8);

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                ConfigurePluginImporters();
                EditorUtility.DisplayDialog("SherpaONNX CUDA runtime", "Installed sherpa-onnx 1.13.6 / ONNX Runtime 1.27.1 CUDA runtime. Restart Unity after installing or changing CUDA/cuDNN system dependencies.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("[SherpaONNX] CUDA runtime installation failed: " + ex);
                EditorUtility.DisplayDialog("SherpaONNX CUDA runtime", ex.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("Tools/SherpaONNX/CUDA Runtime/Check System Dependencies")]
        private static void CheckSystemDependencies()
        {
            var ok = SherpaCudaRuntimeDiagnostics.CheckSystemDependencies(out var message);
            if (ok)
            {
                Debug.Log("[SherpaONNX] " + message);
            }
            else
            {
                EditorUtility.DisplayDialog("SherpaONNX CUDA runtime", message, "OK");
                Debug.LogError("[SherpaONNX] " + message);
            }
        }

        private static void ConfigurePluginImporters()
        {
            foreach (var dll in RuntimeDlls)
            {
                var assetPath = PackageRelativeDirectory + "/" + dll;
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                var importer = PluginImporter.GetAtPath(assetPath);
                if (importer == null)
                {
                    continue;
                }

                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(true);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
                importer.SaveAndReimport();
            }
        }

        private static RuntimeManifest LoadManifest()
        {
            const string assetPath = "Packages/com.eitan.sherpa-onnx-unity/Editor/Resources/SherpaCudaRuntimeManifest.json";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                return new RuntimeManifest();
            }

            try
            {
                var manifest = JsonUtility.FromJson<RuntimeManifest>(asset.text);
                return manifest ?? new RuntimeManifest();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SherpaONNX] Invalid CUDA runtime manifest; using pinned fallback: " + ex.Message);
                return new RuntimeManifest();
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
