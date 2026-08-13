using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Patina.Editor
{
    public static class ProcessManager
    {
        private const string PackageName = "com.taygunsavas.patina-unity-mcp";
        private const string ServerBinaryName = "patina-server";
        private const string LocalRuntimeOverridePrefsKey = "Patina.LocalRuntimeOverride";
        private const string LegacyDevelopmentModePrefsKey = "Patina.DevelopmentMode";

        private static string s_lastManagedRuntimeError = string.Empty;
        private static bool s_loggedManagedRuntimeLockedWarning;

        public enum RuntimeSourceKind
        {
            Packaged,
            Contributor,
            Missing,
        }

        public static bool IsLocalRuntimeOverrideRequested
        {
            get
            {
                if (UnityEditor.EditorPrefs.HasKey(LocalRuntimeOverridePrefsKey))
                    return UnityEditor.EditorPrefs.GetBool(LocalRuntimeOverridePrefsKey, false);

                bool legacyValue = UnityEditor.EditorPrefs.GetBool(
                    LegacyDevelopmentModePrefsKey,
                    false
                );
                UnityEditor.EditorPrefs.SetBool(LocalRuntimeOverridePrefsKey, legacyValue);
                UnityEditor.EditorPrefs.DeleteKey(LegacyDevelopmentModePrefsKey);
                return legacyValue;
            }
            set
            {
                UnityEditor.EditorPrefs.SetBool(LocalRuntimeOverridePrefsKey, value);
                UnityEditor.EditorPrefs.DeleteKey(LegacyDevelopmentModePrefsKey);
            }
        }

        public static bool IsLocalRuntimeOverrideEnabled
        {
            get { return IsLocalRuntimeOverrideRequested; }
        }

        public static string GetPackageId()
        {
            return PackageName;
        }

        public static string FindServerBinary()
        {
            if (TryGetActiveRuntimePath(out string runtimePath))
            {
#if !UNITY_EDITOR_WIN
                EnsureExecutablePermission(runtimePath);
#endif
                return runtimePath;
            }
            return string.Empty;
        }

#if !UNITY_EDITOR_WIN
        private static void EnsureExecutablePermission(string binaryPath)
        {
            try
            {
                if (File.Exists(binaryPath))
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = "+x \"" + binaryPath + "\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using (var process = System.Diagnostics.Process.Start(startInfo))
                    {
                        process?.WaitForExit();
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning(
                    $"[Patina] Failed to set executable permission on {binaryPath}: {ex.Message}"
                );
            }
        }
#endif

        public static string GetClaudeDesktopConfigPath()
        {
#if UNITY_EDITOR_WIN
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Claude", "claude_desktop_config.json");
#elif UNITY_EDITOR_OSX
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(
                home,
                "Library",
                "Application Support",
                "Claude",
                "claude_desktop_config.json"
            );
#else
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "Claude", "claude_desktop_config.json");
#endif
        }

        public static string GetExpectedBinaryLocation()
        {
            string packageRoot = GetPackageRoot();
            string binaryName = ServerBinaryName + GetBinaryExtension();
            return Path.Combine(packageRoot, "Plugins", GetPlatformDirectory(), binaryName);
        }

        public static string GetManagedRuntimeLocation()
        {
            return Path.Combine(
                GetManagedRuntimeDirectory(),
                ServerBinaryName + GetBinaryExtension()
            );
        }

        public static RuntimeSourceKind GetRuntimeSourceKind()
        {
            if (IsLocalRuntimeOverrideEnabled)
                return TryGetContributorRuntimePath(out _)
                    ? RuntimeSourceKind.Contributor
                    : RuntimeSourceKind.Missing;

            return TryGetManagedPackagedRuntimePath(out _)
                ? RuntimeSourceKind.Packaged
                : RuntimeSourceKind.Missing;
        }

        public static string GetRuntimeSourceLabel()
        {
            switch (GetRuntimeSourceKind())
            {
                case RuntimeSourceKind.Packaged:
                    return "Managed packaged runtime";
                case RuntimeSourceKind.Contributor:
                    return "Contributor runtime";
                default:
                    return IsLocalRuntimeOverrideRequested
                        ? "Missing contributor runtime"
                        : "Missing packaged runtime";
            }
        }

        public static string GetRuntimeStatusMessage()
        {
            if (IsLocalRuntimeOverrideEnabled)
            {
                if (TryGetContributorRuntimePath(out string contributorPath))
                {
                    if (TryGetContributorRuntimeStaleReason(out string staleReason))
                        return staleReason + " Active path: " + contributorPath;

                    return "Contributor runtime active: " + contributorPath;
                }

                return "Contributor runtime override is enabled, but no explicit runtime was found. Expected dist/dev-runtime/current/<platform>/patina-server.";
            }

            if (TryGetManagedPackagedRuntimePath(out string managedPath))
                return "Managed packaged runtime active: " + managedPath;

            if (!string.IsNullOrEmpty(s_lastManagedRuntimeError))
                return s_lastManagedRuntimeError;

            return "Packaged runtime is missing. Expected " + GetExpectedBinaryLocation() + ".";
        }

        public static bool TryGetActiveRuntimePath(out string runtimePath)
        {
            if (IsLocalRuntimeOverrideEnabled)
                return TryGetContributorRuntimePath(out runtimePath);

            return TryGetManagedPackagedRuntimePath(out runtimePath);
        }

        public static bool TryGetRuntimeSetupBlocker(out string blockerMessage)
        {
            if (
                IsLocalRuntimeOverrideEnabled
                && TryGetContributorRuntimeStaleReason(out blockerMessage)
            )
                return true;

            if (!IsLocalRuntimeOverrideEnabled && !TryGetManagedPackagedRuntimePath(out _))
            {
                blockerMessage = !string.IsNullOrEmpty(s_lastManagedRuntimeError)
                    ? s_lastManagedRuntimeError
                    : "Managed packaged runtime could not be prepared.";
                return true;
            }

            blockerMessage = string.Empty;
            return false;
        }

        public static bool IsContributorModeAvailable()
        {
            return HasContributorSourceLayout() || HasExplicitContributorRuntime();
        }

        private static string GetPackageRoot()
        {
            try
            {
                PackageInfo packageInfo = PackageInfo.FindForAssembly(
                    typeof(ProcessManager).Assembly
                );
                if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
                    return Path.GetFullPath(packageInfo.resolvedPath);
            }
            catch { }

            return Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Packages", PackageName)
            );
        }

        private static bool TryGetPackagedRuntimePath(out string runtimePath)
        {
            runtimePath = GetExpectedBinaryLocation();
            return File.Exists(runtimePath);
        }

        private static bool TryGetManagedPackagedRuntimePath(out string runtimePath)
        {
            runtimePath = string.Empty;
            s_lastManagedRuntimeError = string.Empty;

            if (!TryGetPackagedRuntimePath(out string packagedPath))
                return false;

            string managedPath = GetManagedRuntimeLocation();
            string metadataPath = Path.Combine(GetManagedRuntimeDirectory(), "runtime.json");

            try
            {
                Directory.CreateDirectory(GetManagedRuntimeDirectory());

                FileInfo source = new FileInfo(packagedPath);
                RuntimeMetadata existingMetadata = ReadRuntimeMetadata(metadataPath);
                string packageVersion = GetPackageVersion();
                bool shouldCopy =
                    !File.Exists(managedPath)
                    || existingMetadata == null
                    || !string.Equals(
                        existingMetadata.PackageVersion,
                        packageVersion,
                        StringComparison.Ordinal
                    )
                    || existingMetadata.SourceLastWriteTimeUtcTicks != source.LastWriteTimeUtc.Ticks
                    || existingMetadata.SourceLength != source.Length;

                if (shouldCopy)
                {
                    if (!TryCopyManagedRuntime(packagedPath, managedPath, out string copyError))
                        throw new IOException(copyError);

#if !UNITY_EDITOR_WIN
                    EnsureExecutablePermission(managedPath);
#endif
                    WriteRuntimeMetadata(metadataPath, packagedPath, source, packageVersion);
                    Debug.Log("[Patina] Managed runtime synchronized: " + managedPath);
                }

                runtimePath = managedPath;
                return File.Exists(runtimePath);
            }
            catch (Exception ex)
            {
                s_lastManagedRuntimeError =
                    "Failed to prepare managed Patina runtime at "
                    + managedPath
                    + ": "
                    + ex.Message;
                Debug.LogError("[Patina] " + s_lastManagedRuntimeError);
                return false;
            }
        }

        private static bool TryCopyManagedRuntime(
            string packagedPath,
            string managedPath,
            out string error
        )
        {
            error = string.Empty;

            try
            {
                File.Copy(packagedPath, managedPath, true);
                s_loggedManagedRuntimeLockedWarning = false;
                return true;
            }
            catch (Exception ex)
            {
                if (File.Exists(managedPath))
                {
                    error =
                        "Managed runtime is in use and could not be refreshed. Close MCP hosts to allow Patina to update it, then run One-Click Setup again. "
                        + ex.Message;

                    if (!s_loggedManagedRuntimeLockedWarning)
                    {
                        Debug.LogWarning("[Patina] " + error);
                        s_loggedManagedRuntimeLockedWarning = true;
                    }

                    return false;
                }

                throw;
            }
        }

        private static RuntimeMetadata ReadRuntimeMetadata(string metadataPath)
        {
            try
            {
                if (!File.Exists(metadataPath))
                    return null;

                return JsonConvert.DeserializeObject<RuntimeMetadata>(
                    File.ReadAllText(metadataPath)
                );
            }
            catch
            {
                return null;
            }
        }

        private static void WriteRuntimeMetadata(
            string metadataPath,
            string sourcePath,
            FileInfo source,
            string packageVersion
        )
        {
            RuntimeMetadata metadata = new RuntimeMetadata
            {
                PackageName = PackageName,
                PackageVersion = packageVersion,
                Platform = GetPlatformDirectory(),
                SourcePath = sourcePath,
                SourceLastWriteTimeUtcTicks = source.LastWriteTimeUtc.Ticks,
                SourceLength = source.Length,
                SyncedAtUtc = DateTime.UtcNow.ToString("O"),
            };

            string json = JsonConvert.SerializeObject(metadata, Formatting.Indented);
            File.WriteAllText(metadataPath, json);
        }

        private static string GetPackageVersion()
        {
            try
            {
                string manifestPath = Path.Combine(GetPackageRoot(), "package.json");
                if (!File.Exists(manifestPath))
                    return string.Empty;

                PackageManifest manifest = JsonConvert.DeserializeObject<PackageManifest>(
                    File.ReadAllText(manifestPath)
                );
                return manifest != null ? manifest.version ?? string.Empty : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetManagedRuntimeDirectory()
        {
#if UNITY_EDITOR_WIN
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "Patina", "UnityMcp", "runtime", GetPlatformDirectory());
#elif UNITY_EDITOR_OSX
            string root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(
                root,
                "Library",
                "Application Support",
                "Patina",
                "UnityMcp",
                "runtime",
                GetPlatformDirectory()
            );
#else
            string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            string root = string.IsNullOrWhiteSpace(dataHome)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local",
                    "share"
                )
                : dataHome;
            return Path.Combine(root, "patina-unity-mcp", "runtime", GetPlatformDirectory());
#endif
        }

        private static bool TryGetContributorRuntimePath(out string runtimePath)
        {
            string binaryName = ServerBinaryName + GetBinaryExtension();
            string platformDir = GetPlatformDirectory();
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string repoRoot = TryGetSourceRepoRoot(GetPackageRoot());

            string projectRuntime = Path.Combine(
                projectRoot,
                "dist",
                "dev-runtime",
                "current",
                platformDir,
                binaryName
            );
            if (File.Exists(projectRuntime))
            {
                runtimePath = Path.GetFullPath(projectRuntime);
                return true;
            }

            if (!string.IsNullOrEmpty(repoRoot))
            {
                string repoRuntime = Path.Combine(
                    repoRoot,
                    "dist",
                    "dev-runtime",
                    "current",
                    platformDir,
                    binaryName
                );
                if (File.Exists(repoRuntime))
                {
                    runtimePath = Path.GetFullPath(repoRuntime);
                    return true;
                }
            }

            runtimePath = string.Empty;
            return false;
        }

        private static bool TryGetContributorRuntimeStaleReason(out string staleReason)
        {
            staleReason = string.Empty;

            if (!TryGetContributorRuntimePath(out string runtimePath))
                return false;

            string repoRoot = TryGetSourceRepoRoot(GetPackageRoot());
            if (string.IsNullOrEmpty(repoRoot))
                return false;

            string rustRoot = Path.Combine(repoRoot, "rust-server");
            if (!Directory.Exists(rustRoot))
                return false;

            DateTime runtimeWriteTimeUtc = File.GetLastWriteTimeUtc(runtimePath);
            DateTime latestSourceWriteTimeUtc = GetLatestRustSourceWriteTimeUtc(rustRoot);
            if (latestSourceWriteTimeUtc <= runtimeWriteTimeUtc)
                return false;

            staleReason =
                "Contributor runtime is older than the Rust source tree. Run `cargo build --release`, then `pwsh -File scripts/publish-dev-runtime.ps1`, and rerun One-Click Setup.";
            return true;
        }

        private static DateTime GetLatestRustSourceWriteTimeUtc(string rustRoot)
        {
            IEnumerable<string> candidateFiles = Directory
                .EnumerateFiles(Path.Combine(rustRoot, "src"), "*.rs", SearchOption.AllDirectories)
                .Concat(new[] { Path.Combine(rustRoot, "Cargo.toml") })
                .Where(File.Exists);

            DateTime latestWriteTimeUtc = DateTime.MinValue;
            foreach (string candidateFile in candidateFiles)
            {
                DateTime writeTimeUtc = File.GetLastWriteTimeUtc(candidateFile);
                if (writeTimeUtc > latestWriteTimeUtc)
                    latestWriteTimeUtc = writeTimeUtc;
            }

            return latestWriteTimeUtc;
        }

        private static bool HasContributorSourceLayout()
        {
            string packageRoot = GetPackageRoot();
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            if (!string.IsNullOrEmpty(TryGetSourceRepoRoot(packageRoot)))
                return true;

            string localRustServer = Path.Combine(projectRoot, "rust-server");
            return Directory.Exists(localRustServer);
        }

        private static bool HasExplicitContributorRuntime()
        {
            return TryGetContributorRuntimePath(out _);
        }

        private static string TryGetSourceRepoRoot(string packageRoot)
        {
            try
            {
                DirectoryInfo packageDirectory = new DirectoryInfo(packageRoot);
                DirectoryInfo parent = packageDirectory.Parent;
                if (parent == null)
                    return string.Empty;

                string siblingRustServer = Path.Combine(
                    parent.FullName,
                    "rust-server",
                    "Cargo.toml"
                );
                if (File.Exists(siblingRustServer))
                    return parent.FullName;
            }
            catch { }

            return string.Empty;
        }

        private static string GetPlatformDirectory()
        {
#if UNITY_EDITOR_WIN
            return "x86_64-win";
#elif UNITY_EDITOR_OSX
            if (
                System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
                == System.Runtime.InteropServices.Architecture.Arm64
            )
                return "aarch64-macos";
            return "x86_64-macos";
#else
            return "x86_64-linux";
#endif
        }

        private static string GetBinaryExtension()
        {
#if UNITY_EDITOR_WIN
            return ".exe";
#else
            return "";
#endif
        }

        private sealed class RuntimeMetadata
        {
            public string PackageName { get; set; }
            public string PackageVersion { get; set; }
            public string Platform { get; set; }
            public string SourcePath { get; set; }
            public long SourceLastWriteTimeUtcTicks { get; set; }
            public long SourceLength { get; set; }
            public string SyncedAtUtc { get; set; }
        }

        private sealed class PackageManifest
        {
            public string version { get; set; }
        }
    }
}
