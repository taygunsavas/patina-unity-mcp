using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Patina.Editor
{
    public static class HostSetupManager
    {
        private const string ServerKey = "patina";
        private const string LegacyServerKey = "forgent";

        public static IReadOnlyList<HostSetupResult> RunOneClickSetup()
        {
            string binaryPath = ProcessManager.FindServerBinary();
            if (string.IsNullOrEmpty(binaryPath))
            {
                return new[]
                {
                    new HostSetupResult
                    {
                        Id = "binary",
                        DisplayName = "Server Binary",
                        Mode = HostSetupMode.Automatic,
                        Status = HostSetupStatus.Failed,
                        Summary = "No Patina server binary was found under the packaged plugin folders or local development outputs.",
                        RequiresRestart = false
                    }
                };
            }

            if (ProcessManager.TryGetRuntimeSetupBlocker(out string runtimeBlocker))
            {
                return new[]
                {
                    new HostSetupResult
                    {
                        Id = "runtime-validation",
                        DisplayName = "Runtime Validation",
                        Mode = HostSetupMode.Automatic,
                        Status = HostSetupStatus.Failed,
                        Summary = runtimeBlocker,
                        RequiresRestart = false
                    }
                };
            }

            HostEnvironment environment = BuildEnvironment();
            List<HostSetupResult> results = new List<HostSetupResult>();

            results.Add(SetupClaudeDesktop(binaryPath));
            results.Add(SetupClaudeCode(binaryPath, environment));
            results.Add(SetupCodex(binaryPath, environment));

            HostSetupResult cursorResult = SetupJsonHost(
                "cursor",
                "Cursor",
                environment.CursorConfigPath,
                "servers",
                binaryPath,
                environment.CursorDetected);
            results.Add(cursorResult);

            HostSetupResult vscodeResult = SetupJsonHost(
                "vscode",
                "Visual Studio Code",
                environment.VisualStudioCodeConfigPath,
                "servers",
                binaryPath,
                environment.VisualStudioCodeDetected);
            results.Add(vscodeResult);

            results.Add(SetupJsonHost(
                "rider",
                "JetBrains Junie / Rider",
                environment.JunieConfigPath,
                "mcpServers",
                binaryPath,
                environment.JunieDetected));

            results.Add(SetupJsonHost(
                "gemini-cli",
                "Gemini CLI",
                environment.GeminiConfigPath,
                "mcpServers",
                binaryPath,
                environment.GeminiDetected));

            results.Add(SetupJsonHost(
                "antigravity-cli",
                "Antigravity CLI",
                environment.AntigravityCliConfigPath,
                "mcpServers",
                binaryPath,
                environment.AntigravityCliDetected));

            results.Add(SetupJsonHost(
                "antigravity",
                "Antigravity (IDE)",
                environment.AntigravityConfigPath,
                "mcpServers",
                binaryPath,
                environment.AntigravityDetected));

            results.Add(CreateCopilotLinkedResult(vscodeResult));
            return results;
        }

        public static IReadOnlyList<HostSetupResult> RemovePatinaFromHosts()
        {
            HostEnvironment environment = BuildEnvironment();
            List<HostSetupResult> results = new List<HostSetupResult>
            {
                RemoveClaudeDesktop(),
                RemoveClaudeCode(environment),
                RemoveCodex(environment),
                RemoveJsonHost("cursor", "Cursor", environment.CursorConfigPath, "servers", environment.CursorDetected),
                RemoveJsonHost("vscode", "Visual Studio Code", environment.VisualStudioCodeConfigPath, "servers", environment.VisualStudioCodeDetected),
                RemoveJsonHost("rider", "JetBrains Junie / Rider", environment.JunieConfigPath, "mcpServers", environment.JunieDetected),
                RemoveJsonHost("gemini-cli", "Gemini CLI", environment.GeminiConfigPath, "mcpServers", environment.GeminiDetected),
                RemoveJsonHost("antigravity-cli", "Antigravity CLI", environment.AntigravityCliConfigPath, "mcpServers", environment.AntigravityCliDetected),
                RemoveJsonHost("antigravity", "Antigravity (IDE)", environment.AntigravityConfigPath, "mcpServers", environment.AntigravityDetected),
                RemoveCopilotLinkedResult(environment.VisualStudioCodeConfigPath, environment.VisualStudioCodeDetected)
            };

            return results;
        }

        public static string BuildOverallSummary(IReadOnlyList<HostSetupResult> results)
        {
            if (results == null || results.Count == 0)
                return "No host setup results are available.";

            int configured = results.Count(result => result.Status == HostSetupStatus.Configured);
            int updated = results.Count(result => result.Status == HostSetupStatus.UpdatedStaleEntry);
            int removed = results.Count(result => result.Status == HostSetupStatus.Removed);
            int notPresent = results.Count(result => result.Status == HostSetupStatus.NotPresent);
            int notDetected = results.Count(result => result.Status == HostSetupStatus.NotDetected);
            int failed = results.Count(result => result.Status == HostSetupStatus.Failed);

            List<string> parts = new List<string>();
            if (configured > 0)
                parts.Add(configured + " configured");
            if (updated > 0)
                parts.Add(updated + " stale entries updated");
            if (removed > 0)
                parts.Add(removed + " removed");
            if (notPresent > 0)
                parts.Add(notPresent + " already clean");
            if (notDetected > 0)
                parts.Add(notDetected + " not detected");
            if (failed > 0)
                parts.Add(failed + " failed");

            return parts.Count == 0 ? "No host changes were required." : string.Join(", ", parts) + ".";
        }

        public static string BuildDisplayPath(string binaryPath)
        {
            if (string.IsNullOrEmpty(binaryPath))
                return "Missing binary";

            return binaryPath.Replace("\\", "/");
        }

        private static HostSetupResult SetupClaudeDesktop(string binaryPath)
        {
            string configPath = ProcessManager.GetClaudeDesktopConfigPath();
            if (!TryUpsertJsonServerConfig(configPath, "mcpServers", binaryPath, out HostSetupStatus status, out string summary, out string error))
            {
                return CreateFailureResult(
                    "claude-desktop",
                    "Claude Desktop",
                    configPath,
                    BuildJsonServerConfigSnippet(binaryPath, "mcpServers"),
                    "Automatic config write failed: " + error);
            }

            return new HostSetupResult
            {
                Id = "claude-desktop",
                DisplayName = "Claude Desktop",
                Mode = HostSetupMode.Automatic,
                Status = status,
                ConfigPath = configPath,
                Summary = summary,
                ExportSnippet = BuildJsonServerConfigSnippet(binaryPath, "mcpServers"),
                RequiresRestart = true
            };
        }

        private static HostSetupResult RemoveClaudeDesktop()
        {
            string configPath = ProcessManager.GetClaudeDesktopConfigPath();
            if (!TryRemoveJsonServerConfig(configPath, "mcpServers", out HostSetupStatus status, out string summary, out string error))
            {
                return CreateFailureResult("claude-desktop", "Claude Desktop", configPath, string.Empty, "Removal failed: " + error);
            }

            return new HostSetupResult
            {
                Id = "claude-desktop",
                DisplayName = "Claude Desktop",
                Mode = HostSetupMode.Automatic,
                Status = status,
                ConfigPath = configPath,
                Summary = summary,
                ExportSnippet = string.Empty,
                RequiresRestart = status == HostSetupStatus.Removed
            };
        }

        private static HostSetupResult SetupClaudeCode(string binaryPath, HostEnvironment environment)
        {
            return SetupJsonHost(
                "claude-code",
                "Claude Code",
                environment.ClaudeCodeConfigPath,
                "mcpServers",
                binaryPath,
                environment.ClaudeCodeDetected);
        }

        private static HostSetupResult RemoveClaudeCode(HostEnvironment environment)
        {
            return RemoveJsonHost(
                "claude-code",
                "Claude Code",
                environment.ClaudeCodeConfigPath,
                "mcpServers",
                environment.ClaudeCodeDetected);
        }

        private static HostSetupResult SetupCodex(string binaryPath, HostEnvironment environment)
        {
            string configPath = environment.CodexConfigPath;
            string snippet = BuildCodexTomlSnippet(binaryPath);
            bool hasEntry = CodexEntryExists(configPath);

            if (!environment.CodexDetected)
            {
                return new HostSetupResult
                {
                    Id = "codex-cli",
                    DisplayName = "Codex CLI",
                    Mode = HostSetupMode.Automatic,
                    Status = HostSetupStatus.NotDetected,
                    ConfigPath = configPath,
                    Summary = hasEntry
                        ? "Codex CLI was not detected, but a stale Patina entry is still present in config."
                        : "Codex CLI was not detected on this machine.",
                    ExportSnippet = snippet,
                    RequiresRestart = false
                };
            }

            try
            {
                string configDirectory = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(configDirectory))
                    Directory.CreateDirectory(configDirectory);

                string content = File.Exists(configPath) ? File.ReadAllText(configPath) : string.Empty;
                Match match = GetCodexBlockRegex().Match(content);
                bool existed = match.Success;
                bool stale = existed && !NormalizeLineEndings(match.Value.Trim()).Equals(NormalizeLineEndings(snippet.Trim()), StringComparison.Ordinal);

                string updatedContent;
                if (existed)
                {
                    updatedContent = GetCodexBlockRegex().Replace(content, snippet.TrimEnd() + Environment.NewLine + Environment.NewLine);
                }
                else
                {
                    updatedContent = AppendBlock(content, snippet.TrimEnd());
                }

                File.WriteAllText(configPath, updatedContent);

                return new HostSetupResult
                {
                    Id = "codex-cli",
                    DisplayName = "Codex CLI",
                    Mode = HostSetupMode.Automatic,
                    Status = stale ? HostSetupStatus.UpdatedStaleEntry : HostSetupStatus.Configured,
                    ConfigPath = configPath,
                    Summary = stale
                        ? "Codex CLI contained a stale Patina entry and it was updated automatically."
                        : existed
                            ? "Codex CLI config already existed and was refreshed automatically."
                            : "Codex CLI config was created automatically.",
                    ExportSnippet = snippet,
                    RequiresRestart = false
                };
            }
            catch (Exception ex)
            {
                return CreateFailureResult("codex-cli", "Codex CLI", configPath, snippet, "Automatic Codex CLI setup failed: " + ex.Message);
            }
        }

        private static HostSetupResult RemoveCodex(HostEnvironment environment)
        {
            string configPath = environment.CodexConfigPath;
            try
            {
                if (!File.Exists(configPath))
                {
                    return CreateRemoveResult("codex-cli", "Codex CLI", configPath, environment.CodexDetected, HostSetupStatus.NotPresent, "No Codex CLI config file exists for Patina cleanup.");
                }

                string content = File.ReadAllText(configPath);
                if (!GetCodexBlockRegex().IsMatch(content))
                {
                    return CreateRemoveResult("codex-cli", "Codex CLI", configPath, environment.CodexDetected, HostSetupStatus.NotPresent, "No Patina entry was present in the Codex CLI config.");
                }

                string updated = GetCodexBlockRegex().Replace(content, string.Empty);
                updated = CollapseBlankLines(updated);
                File.WriteAllText(configPath, updated);

                return CreateRemoveResult("codex-cli", "Codex CLI", configPath, environment.CodexDetected, HostSetupStatus.Removed, "Removed the Patina entry from the Codex CLI config.");
            }
            catch (Exception ex)
            {
                return CreateFailureResult("codex-cli", "Codex CLI", configPath, string.Empty, "Removal failed: " + ex.Message);
            }
        }

        private static HostSetupResult SetupJsonHost(string id, string displayName, string configPath, string rootPropertyName, string binaryPath, bool detected)
        {
            string snippet = BuildJsonServerConfigSnippet(binaryPath, rootPropertyName);
            bool hasEntry = JsonEntryExists(configPath, rootPropertyName);

            if (!detected)
            {
                return new HostSetupResult
                {
                    Id = id,
                    DisplayName = displayName,
                    Mode = HostSetupMode.Automatic,
                    Status = HostSetupStatus.NotDetected,
                    ConfigPath = configPath,
                    Summary = hasEntry
                        ? displayName + " was not detected, but a stale Patina entry is still present in config."
                        : displayName + " was not detected on this machine.",
                    ExportSnippet = snippet,
                    RequiresRestart = false
                };
            }

            if (!TryUpsertJsonServerConfig(configPath, rootPropertyName, binaryPath, out HostSetupStatus status, out string summary, out string error))
            {
                return CreateFailureResult(id, displayName, configPath, snippet, "Automatic setup failed: " + error);
            }

            return new HostSetupResult
            {
                Id = id,
                DisplayName = displayName,
                Mode = HostSetupMode.Automatic,
                Status = status,
                ConfigPath = configPath,
                Summary = summary,
                ExportSnippet = snippet,
                RequiresRestart = true
            };
        }

        private static HostSetupResult RemoveJsonHost(string id, string displayName, string configPath, string rootPropertyName, bool detected)
        {
            if (!TryRemoveJsonServerConfig(configPath, rootPropertyName, out HostSetupStatus status, out string summary, out string error))
            {
                return CreateFailureResult(id, displayName, configPath, string.Empty, "Removal failed: " + error);
            }

            return CreateRemoveResult(id, displayName, configPath, detected, status, summary);
        }

        private static HostSetupResult CreateCopilotLinkedResult(HostSetupResult vscodeResult)
        {
            HostSetupStatus status = vscodeResult.Status;
            string summary;

            switch (status)
            {
                case HostSetupStatus.Configured:
                    summary = "GitHub Copilot in Visual Studio Code uses the MCP config that was written automatically.";
                    break;
                case HostSetupStatus.UpdatedStaleEntry:
                    summary = "GitHub Copilot in Visual Studio Code uses the refreshed Visual Studio Code MCP config.";
                    break;
                case HostSetupStatus.NotDetected:
                    summary = "Visual Studio Code was not detected, so this linked host could not be confirmed.";
                    break;
                default:
                    summary = "GitHub Copilot in Visual Studio Code follows the Visual Studio Code MCP config state.";
                    break;
            }

            return new HostSetupResult
            {
                Id = "copilot",
                DisplayName = "GitHub Copilot in VS Code",
                Mode = HostSetupMode.Automatic,
                Status = status,
                ConfigPath = vscodeResult.ConfigPath,
                Summary = summary,
                ExportSnippet = string.Empty,
                RequiresRestart = vscodeResult.RequiresRestart
            };
        }

        private static HostSetupResult RemoveCopilotLinkedResult(string vscodeConfigPath, bool vscodeDetected)
        {
            bool hasEntry = JsonEntryExists(vscodeConfigPath, "servers");
            HostSetupStatus status = hasEntry ? HostSetupStatus.Removed : HostSetupStatus.NotPresent;
            string summary = hasEntry
                ? "GitHub Copilot in Visual Studio Code no longer sees Patina because the Visual Studio Code MCP entry was removed."
                : "No linked Patina entry was present through the Visual Studio Code MCP config.";

            if (!vscodeDetected && !hasEntry)
            {
                status = HostSetupStatus.NotDetected;
                summary = "Visual Studio Code was not detected, so this linked host could not be confirmed.";
            }

            return new HostSetupResult
            {
                Id = "copilot",
                DisplayName = "GitHub Copilot in VS Code",
                Mode = HostSetupMode.Automatic,
                Status = status,
                ConfigPath = vscodeConfigPath,
                Summary = summary,
                ExportSnippet = string.Empty,
                RequiresRestart = status == HostSetupStatus.Removed
            };
        }

        private static bool TryUpsertJsonServerConfig(string configPath, string rootPropertyName, string binaryPath, out HostSetupStatus status, out string summary, out string error)
        {
            status = HostSetupStatus.Failed;
            summary = string.Empty;
            error = string.Empty;

            try
            {
                string configDirectory = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(configDirectory))
                    Directory.CreateDirectory(configDirectory);

                JObject root = ReadJsonRoot(configPath);
                JObject servers = root[rootPropertyName] as JObject;
                if (servers == null)
                {
                    servers = new JObject();
                    root[rootPropertyName] = servers;
                }

                servers.Remove(LegacyServerKey);

                string desiredCommand = BuildDisplayPath(binaryPath);
                string desiredPort = McpBridgeServer.Port.ToString();
                JObject existing = servers[ServerKey] as JObject;
                bool existed = existing != null;
                bool stale = existed && !JsonEntryMatches(existing, desiredCommand, desiredPort);

                servers[ServerKey] = new JObject
                {
                    ["command"] = desiredCommand,
                    ["args"] = new JArray("--port", desiredPort)
                };

                File.WriteAllText(configPath, root.ToString(Newtonsoft.Json.Formatting.Indented));

                status = stale ? HostSetupStatus.UpdatedStaleEntry : HostSetupStatus.Configured;
                if (stale)
                    summary = "An existing Patina entry pointed to an older runtime and was updated automatically.";
                else if (existed)
                    summary = "Existing config was refreshed automatically.";
                else
                    summary = "Config entry was written automatically.";

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryRemoveJsonServerConfig(string configPath, string rootPropertyName, out HostSetupStatus status, out string summary, out string error)
        {
            status = HostSetupStatus.NotPresent;
            summary = string.Empty;
            error = string.Empty;

            try
            {
                if (!File.Exists(configPath))
                {
                    summary = "No config file exists for Patina cleanup.";
                    return true;
                }

                JObject root = ReadJsonRoot(configPath);
                JObject servers = root[rootPropertyName] as JObject;
                if (servers == null || (servers[ServerKey] == null && servers[LegacyServerKey] == null))
                {
                    summary = "No Patina entry was present in the config file.";
                    return true;
                }

                servers.Remove(ServerKey);
                servers.Remove(LegacyServerKey);
                if (!servers.HasValues)
                    root.Remove(rootPropertyName);

                File.WriteAllText(configPath, root.ToString(Newtonsoft.Json.Formatting.Indented));
                status = HostSetupStatus.Removed;
                summary = "Removed the Patina entry from the config file.";
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static JObject ReadJsonRoot(string configPath)
        {
            if (!File.Exists(configPath))
                return new JObject();

            string existing = File.ReadAllText(configPath);
            return string.IsNullOrWhiteSpace(existing) ? new JObject() : JObject.Parse(existing);
        }

        private static bool JsonEntryExists(string configPath, string rootPropertyName)
        {
            if (!File.Exists(configPath))
                return false;

            try
            {
                JObject root = ReadJsonRoot(configPath);
                JObject servers = root[rootPropertyName] as JObject;
                return servers != null && (servers[ServerKey] != null || servers[LegacyServerKey] != null);
            }
            catch
            {
                return false;
            }
        }

        private static bool JsonEntryMatches(JObject entry, string desiredCommand, string desiredPort)
        {
            string existingCommand = entry.Value<string>("command") ?? string.Empty;
            JArray args = entry["args"] as JArray;
            string arg0 = args != null && args.Count > 0 ? args[0]?.ToString() ?? string.Empty : string.Empty;
            string arg1 = args != null && args.Count > 1 ? args[1]?.ToString() ?? string.Empty : string.Empty;

            return string.Equals(existingCommand, desiredCommand, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(arg0, "--port", StringComparison.Ordinal)
                   && string.Equals(arg1, desiredPort, StringComparison.Ordinal);
        }

        private static bool CodexEntryExists(string configPath)
        {
            if (!File.Exists(configPath))
                return false;

            try
            {
                return GetCodexBlockRegex().IsMatch(File.ReadAllText(configPath));
            }
            catch
            {
                return false;
            }
        }

        private static readonly Regex s_codexBlockRegex = new Regex(
            @"(?ms)^\[mcp_servers\.(patina|forgent)\]\s*.*?(?=^\[|\z)",
            RegexOptions.Compiled);

        private static Regex GetCodexBlockRegex()
        {
            return s_codexBlockRegex;
        }

        private static string BuildJsonServerConfigSnippet(string binaryPath, string rootPropertyName)
        {
            JObject root = new JObject
            {
                [rootPropertyName] = new JObject
                {
                    [ServerKey] = new JObject
                    {
                        ["command"] = BuildDisplayPath(binaryPath),
                        ["args"] = new JArray("--port", McpBridgeServer.Port.ToString())
                    }
                }
            };

            return root.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        private static string BuildCodexTomlSnippet(string binaryPath)
        {
            return "[mcp_servers." + ServerKey + "]" + Environment.NewLine +
                   "command = \"" + EscapeTomlString(binaryPath) + "\"" + Environment.NewLine +
                   "args = [\"--port\", \"" + McpBridgeServer.Port + "\"]" + Environment.NewLine +
                   "startup_timeout_sec = 60" + Environment.NewLine;
        }

        private static string EscapeTomlString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string AppendBlock(string content, string block)
        {
            if (string.IsNullOrWhiteSpace(content))
                return block + Environment.NewLine;

            string updated = content;
            if (!updated.EndsWith(Environment.NewLine))
                updated += Environment.NewLine;

            return updated + Environment.NewLine + block + Environment.NewLine;
        }

        private static string CollapseBlankLines(string content)
        {
            string normalized = Regex.Replace(content, @"(\r?\n){3,}", Environment.NewLine + Environment.NewLine);
            return normalized.Trim() + Environment.NewLine;
        }

        private static string NormalizeLineEndings(string value)
        {
            return value.Replace("\r\n", "\n").Trim();
        }

        private static HostSetupResult CreateFailureResult(string id, string displayName, string configPath, string snippet, string summary)
        {
            return new HostSetupResult
            {
                Id = id,
                DisplayName = displayName,
                Mode = HostSetupMode.Automatic,
                Status = HostSetupStatus.Failed,
                ConfigPath = configPath,
                Summary = summary,
                ExportSnippet = snippet,
                RequiresRestart = false
            };
        }

        private static HostSetupResult CreateRemoveResult(string id, string displayName, string configPath, bool detected, HostSetupStatus status, string summary)
        {
            if (!detected && status == HostSetupStatus.NotPresent)
            {
                status = HostSetupStatus.NotDetected;
                summary = displayName + " was not detected and no Patina entry was found.";
            }

            return new HostSetupResult
            {
                Id = id,
                DisplayName = displayName,
                Mode = HostSetupMode.Automatic,
                Status = status,
                ConfigPath = configPath,
                Summary = summary,
                ExportSnippet = string.Empty,
                RequiresRestart = status == HostSetupStatus.Removed
            };
        }

        private static HostEnvironment BuildEnvironment()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return new HostEnvironment
            {
                ClaudeCodeConfigPath = Path.Combine(userProfile, ".claude.json"),
                ClaudeCodeDetected = Directory.Exists(Path.Combine(userProfile, ".claude"))
                                     || FindPathHints("claude.exe", "claude.cmd", "claude").Count > 0,
                CodexConfigPath = Path.Combine(userProfile, ".codex", "config.toml"),
                CursorConfigPath = Path.Combine(userProfile, ".cursor", "mcp.json"),
                CursorDetected = AnyFileExists(
                    Path.Combine(localAppData, "Programs", "cursor", "Cursor.exe"),
                    Path.Combine(localAppData, "cursor", "Cursor.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Cursor", "Cursor.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Cursor", "Cursor.exe"))
                    || FindPathHints("Cursor.exe", "cursor.exe", "cursor").Count > 0,
                VisualStudioCodeConfigPath = Path.Combine(appData, "Code", "User", "mcp.json"),
                VisualStudioCodeDetected = AnyFileExists(
                    Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft VS Code", "Code.exe"))
                    || Directory.Exists(Path.Combine(appData, "Code", "User"))
                    || FindPathHints("Code.exe", "code.cmd", "code").Count > 0,
                JunieConfigPath = Path.Combine(userProfile, ".junie", "mcp", "mcp.json"),
                JunieDetected = Directory.Exists(Path.Combine(userProfile, ".junie")) || Directory.Exists(Path.Combine(appData, "JetBrains")),
                GeminiConfigPath = Path.Combine(userProfile, ".gemini", "settings.json"),
                GeminiDetected = Directory.Exists(Path.Combine(userProfile, ".gemini")) || FindPathHints("gemini.exe", "gemini").Count > 0,
                CodexDetected = File.Exists(Path.Combine(userProfile, ".codex", "config.toml"))
                                || Directory.Exists(Path.Combine(userProfile, ".codex"))
                                || FindPathHints("codex.exe", "codex").Count > 0,
                AntigravityCliConfigPath = Path.Combine(userProfile, ".gemini", "antigravity-cli", "mcp_config.json"),
                AntigravityCliDetected = Directory.Exists(Path.Combine(userProfile, ".gemini", "antigravity-cli"))
                                      || FindPathHints("antigravity-cli.exe", "antigravity-cli", "antigravity.exe", "antigravity").Count > 0,
                AntigravityConfigPath = Path.Combine(userProfile, ".gemini", "antigravity", "mcp_config.json"),
                AntigravityDetected = Directory.Exists(Path.Combine(userProfile, ".gemini", "antigravity"))
            };
        }

        private static bool AnyFileExists(params string[] paths)
        {
            return paths.Any(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
        }

        private static IReadOnlyList<string> FindPathHints(params string[] executableNames)
        {
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string[] paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            List<string> matches = new List<string>();

            foreach (string dir in paths)
            {
                foreach (string executableName in executableNames)
                {
                    string candidate = Path.Combine(dir.Trim(), executableName);
                    if (File.Exists(candidate))
                        matches.Add(candidate);
                }
            }

            return matches.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private sealed class HostEnvironment
        {
            public string ClaudeCodeConfigPath { get; set; }
            public bool ClaudeCodeDetected { get; set; }
            public string CodexConfigPath { get; set; }
            public bool CodexDetected { get; set; }
            public string CursorConfigPath { get; set; }
            public bool CursorDetected { get; set; }
            public string VisualStudioCodeConfigPath { get; set; }
            public bool VisualStudioCodeDetected { get; set; }
            public string JunieConfigPath { get; set; }
            public bool JunieDetected { get; set; }
            public string GeminiConfigPath { get; set; }
            public bool GeminiDetected { get; set; }
            public string AntigravityCliConfigPath { get; set; }
            public bool AntigravityCliDetected { get; set; }
            public string AntigravityConfigPath { get; set; }
            public bool AntigravityDetected { get; set; }
        }
    }
}

