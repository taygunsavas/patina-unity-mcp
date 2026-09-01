using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;

namespace Patina.Editor
{
    [InitializeOnLoad]
    public static class CompilationErrorBuffer
    {
        public sealed class CompilerEntry
        {
            public string File { get; }
            public int Line { get; }
            public int Column { get; }
            public string Message { get; }
            public string Severity { get; }

            public CompilerEntry(string file, int line, int column, string message, string severity)
            {
                File = file;
                Line = line;
                Column = column;
                Message = message;
                Severity = severity;
            }
        }

        private const int PersistLimit = 200;
        private const int MessageTruncateLimit = 4096;
        private const string TruncationMarker = "... [truncated]";
        private const string SnapshotSessionStateKey =
            "Patina.CompilationErrorBuffer.ReloadSnapshot";
        private const string LastCompilationStartedSessionStateKey =
            "Patina.CompilationErrorBuffer.LastCompilationStartedUtcTicks";

        private static readonly List<CompilerEntry> s_entries = new List<CompilerEntry>();
        private static readonly object s_lock = new object();
        private static bool s_hasResults = false;

        static CompilationErrorBuffer()
        {
            RestoreFromSessionState();
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        public static long LastCompilationStartedUtcTicks =>
            long.TryParse(
                SessionState.GetString(LastCompilationStartedSessionStateKey, "0"),
                out long ticks
            )
                ? ticks
                : 0;

        private static void OnCompilationStarted(object context)
        {
            SessionState.SetString(
                LastCompilationStartedSessionStateKey,
                DateTime.UtcNow.Ticks.ToString()
            );
        }

        private static void OnAssemblyCompilationFinished(
            string assemblyPath,
            CompilerMessage[] messages
        )
        {
            if (messages == null || messages.Length == 0)
                return;

            lock (s_lock)
            {
                if (!s_hasResults)
                {
                    s_entries.Clear();
                    s_hasResults = true;
                }

                foreach (var msg in messages)
                {
                    string severity = msg.type == CompilerMessageType.Error ? "error" : "warning";
                    s_entries.Add(
                        new CompilerEntry(
                            msg.file ?? string.Empty,
                            msg.line,
                            msg.column,
                            msg.message ?? string.Empty,
                            severity
                        )
                    );
                }
            }
        }

        private static void OnBeforeAssemblyReload()
        {
            PersistSnapshot();
        }

        private static void PersistSnapshot()
        {
            lock (s_lock)
            {
                if (!s_hasResults)
                {
                    SessionState.EraseString(SnapshotSessionStateKey);
                    return;
                }

                var array = new JArray();
                int take = Math.Min(s_entries.Count, PersistLimit);
                for (int i = 0; i < take; i++)
                {
                    var entry = s_entries[i];
                    array.Add(
                        new JObject
                        {
                            ["file"] = entry.File,
                            ["line"] = entry.Line,
                            ["column"] = entry.Column,
                            ["message"] = Truncate(entry.Message, MessageTruncateLimit),
                            ["severity"] = entry.Severity,
                        }
                    );
                }

                var snapshot = new JObject { ["hasResults"] = true, ["entries"] = array };
                SessionState.SetString(
                    SnapshotSessionStateKey,
                    snapshot.ToString(Newtonsoft.Json.Formatting.None)
                );
            }
        }

        private static void RestoreFromSessionState()
        {
            string json = SessionState.GetString(SnapshotSessionStateKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return;

            try
            {
                JObject snapshot = JObject.Parse(json);
                bool hasResults = snapshot.Value<bool?>("hasResults") ?? false;
                JArray array = snapshot["entries"] as JArray ?? new JArray();

                lock (s_lock)
                {
                    s_entries.Clear();
                    foreach (JToken token in array)
                    {
                        if (!(token is JObject obj))
                            continue;

                        s_entries.Add(
                            new CompilerEntry(
                                obj.Value<string>("file") ?? string.Empty,
                                obj.Value<int?>("line") ?? 0,
                                obj.Value<int?>("column") ?? 0,
                                obj.Value<string>("message") ?? string.Empty,
                                obj.Value<string>("severity") ?? "error"
                            )
                        );
                    }
                    s_hasResults = hasResults;
                }

                SessionState.EraseString(SnapshotSessionStateKey);
            }
            catch (Exception) { }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value;

            return value.Substring(0, maxLength) + TruncationMarker;
        }

        public static CompilerEntry[] GetErrors()
        {
            lock (s_lock)
            {
                return s_entries.FindAll(e => e.Severity == "error").ToArray();
            }
        }

        public static CompilerEntry[] GetWarnings()
        {
            lock (s_lock)
            {
                return s_entries.FindAll(e => e.Severity == "warning").ToArray();
            }
        }

        public static CompilerEntry[] GetAll()
        {
            lock (s_lock)
            {
                return s_entries.ToArray();
            }
        }

        public static bool HasResults => s_hasResults;

        public static void Clear()
        {
            lock (s_lock)
            {
                s_entries.Clear();
                s_hasResults = false;
            }

            SessionState.EraseString(SnapshotSessionStateKey);
        }
    }
}
