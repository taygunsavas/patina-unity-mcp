using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Patina.Editor
{
    public static class ProcessManager
    {
        private const string PackageName = "com.taygunsavas.patina-unity-mcp";
        private const string ServerBinaryName = "patina-server";
        private const string LocalRuntimeOverridePrefsKey = "Patina.LocalRuntimeOverride";
        private const string LegacyDevelopmentModePrefsKey = "Patina.DevelopmentMode";

        public static bool IsLocalRuntimeOverrideRequested
        {
            get
            {
                if (UnityEditor.EditorPrefs.HasKey(LocalRuntimeOverridePrefsKey))
                    return UnityEditor.EditorPrefs.GetBool(LocalRuntimeOverridePrefsKey, false);

                return UnityEditor.EditorPrefs.GetBool(LegacyDevelopmentModePrefsKey, false);
            }
            set { UnityEditor.EditorPrefs.SetBool(LocalRuntimeOverridePrefsKey, value); }
        }

        public static bool IsLocalRuntimeOverrideEnabled
        {
            get { return IsContributorModeAvailable() && IsLocalRuntimeOverrideRequested; }
        }

        public static string GetPackageId()
        {
            return PackageName;
        }

        public static string FindServerBinary()
        {
            foreach (string candidate in EnumerateBinaryCandidates())
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }

            return string.Empty;
        }

        public static string GetClaudeDesktopConfigPath()
        {
#if UNITY_EDITOR_WIN
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Claude", "claude_desktop_config.json");
#elif UNITY_EDITOR_OSX
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "Claude", "claude_desktop_config.json");
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

        public static bool IsContributorModeAvailable()
        {
            string packageRoot = GetPackageRoot();
            string binaryName = ServerBinaryName + GetBinaryExtension();
            string platformDir = GetPlatformDirectory();
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string repoRoot = TryGetSourceRepoRoot(packageRoot);

            if (!string.IsNullOrEmpty(repoRoot))
                return true;

            string projectDevRuntime = Path.Combine(projectRoot, "dist", "dev-runtime", "current", platformDir, binaryName);
            if (File.Exists(projectDevRuntime))
                return true;

            string localRustServer = Path.Combine(projectRoot, "rust-server");
            return Directory.Exists(localRustServer);
        }

        private static string GetPackageRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", PackageName));
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(normalized));
        }

        private static IEnumerable<string> EnumerateBinaryCandidates()
        {
            string packageRoot = GetPackageRoot();
            string pluginsDir = Path.Combine(packageRoot, "Plugins");
            string binaryName = ServerBinaryName + GetBinaryExtension();
            string platformDir = GetPlatformDirectory();
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string repoRoot = TryGetSourceRepoRoot(packageRoot);

            if (IsLocalRuntimeOverrideEnabled)
            {
                foreach (string candidate in EnumerateDevelopmentModeCandidates(projectRoot, repoRoot, binaryName, platformDir))
                    yield return candidate;
            }

            yield return Path.Combine(pluginsDir, platformDir, binaryName);
            yield return Path.Combine(projectRoot, "rust-server", "target", "release", binaryName);
            yield return Path.Combine(projectRoot, "rust-server", "target", "debug", binaryName);

            if (!string.IsNullOrEmpty(repoRoot))
            {
                yield return Path.Combine(repoRoot, "rust-server", "target", "release", binaryName);
                yield return Path.Combine(repoRoot, "rust-server", "target", "debug", binaryName);
                yield return Path.Combine(repoRoot, "dist", "local-upm", PackageName, "Plugins", platformDir, binaryName);
            }

            foreach (string candidate in EnumeratePluginCandidates(pluginsDir, binaryName))
                yield return candidate;

            foreach (string candidate in EnumeratePathCandidates(binaryName))
                yield return candidate;
        }

        private static IEnumerable<string> EnumerateDevelopmentModeCandidates(string projectRoot, string repoRoot, string binaryName, string platformDir)
        {
            yield return Path.Combine(projectRoot, "dist", "dev-runtime", "current", platformDir, binaryName);

            if (!string.IsNullOrEmpty(repoRoot))
                yield return Path.Combine(repoRoot, "dist", "dev-runtime", "current", platformDir, binaryName);
        }

        private static string TryGetSourceRepoRoot(string packageRoot)
        {
            try
            {
                DirectoryInfo packageDirectory = new DirectoryInfo(packageRoot);
                DirectoryInfo parent = packageDirectory.Parent;
                if (parent == null)
                    return string.Empty;

                string siblingRustServer = Path.Combine(parent.FullName, "rust-server", "Cargo.toml");
                if (File.Exists(siblingRustServer))
                    return parent.FullName;
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string[] EnumeratePluginCandidates(string pluginsDir, string binaryName)
        {
            if (!Directory.Exists(pluginsDir))
                return Array.Empty<string>();

            return Directory.GetDirectories(pluginsDir)
                .Select(dir => Path.Combine(dir, binaryName))
                .ToArray();
        }

        private static string[] EnumeratePathCandidates(string binaryName)
        {
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            return pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(dir => Path.Combine(dir.Trim(), binaryName))
                .ToArray();
        }

        private static string GetPlatformDirectory()
        {
#if UNITY_EDITOR_WIN
            return "x86_64-win";
#elif UNITY_EDITOR_OSX
            if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
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
    }
}


