using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor
{
    [UnityEditor.InitializeOnLoad]
    public static class ConsoleLogBuffer
    {
        public sealed class LogEntry
        {
            public string Type { get; }
            public string Message { get; }
            public string StackTrace { get; }
            public string Timestamp { get; }
            public string Phase { get; }
            internal int WindowId { get; }

            public LogEntry(
                string type,
                string message,
                string stackTrace,
                string timestamp,
                string phase,
                int windowId
            )
            {
                Type = type;
                Message = message;
                StackTrace = stackTrace;
                Timestamp = timestamp;
                Phase = phase;
                WindowId = windowId;
            }
        }

        private const int BufferSize = 500;
        private const int PersistLimit = 200;
        private const int MessageTruncateLimit = 4096;
        private const int StackTraceTruncateLimit = 2048;
        private const string TruncationMarker = "... [truncated]";
        private const string HistorySessionStateKey = "Patina.ConsoleLogBuffer.HistorySnapshot";
        private const string TeardownSessionStateKey = "Patina.ConsoleLogBuffer.TeardownSnapshot";
        private const string ReloadMarkerSessionStateKey = "Patina.ConsoleLogBuffer.ReloadMarker";
        private const string PhaseNormal = "normal";
        private const string PhaseReloadTeardown = "reloadTeardown";
        private const string PhaseReloadStartup = "reloadStartup";

        private static readonly LogEntry[] s_buffer = new LogEntry[BufferSize];
        private static int s_head = 0;
        private static int s_count = 0;
        private static readonly object s_lock = new object();
        private static readonly int s_mainThreadId;
        private static volatile string s_phase = PhaseNormal;
        private static readonly System.Collections.Generic.List<LogEntry> s_teardownEntries =
            new System.Collections.Generic.List<LogEntry>();
        private static bool s_teardownDirty;

        static ConsoleLogBuffer()
        {
            s_mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

            bool hadReloadMarker = SessionState.GetBool(ReloadMarkerSessionStateKey, false);

            RestoreFromSessionState();

            try
            {
                SessionState.EraseBool(ReloadMarkerSessionStateKey);
            }
            catch { }

            if (hadReloadMarker)
            {
                s_phase = PhaseReloadStartup;
                EditorApplication.update += CompleteStartupPhase;
            }

            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            Application.logMessageReceivedThreaded += OnLogReceived;
        }

        private static void CompleteStartupPhase()
        {
            EditorApplication.update -= CompleteStartupPhase;
            s_phase = PhaseNormal;
        }

        private static void OnBeforeAssemblyReload()
        {
            s_phase = PhaseReloadTeardown;

            lock (s_lock)
            {
                s_teardownEntries.Clear();
                s_teardownDirty = false;
            }

            TryPersistHistorySnapshot();
            TrySetReloadMarker();
        }

        private static bool IsMainThread()
        {
            return System.Threading.Thread.CurrentThread.ManagedThreadId == s_mainThreadId;
        }

        private static void OnLogReceived(string message, string stackTrace, LogType logType)
        {
            string type;
            switch (logType)
            {
                case LogType.Warning:
                    type = "warning";
                    break;
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    type = "error";
                    break;
                default:
                    type = "log";
                    break;
            }

            string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string phase = s_phase;
            int windowId = McpBridgeServer.ReloadCount;
            var entry = new LogEntry(type, message, stackTrace, timestamp, phase, windowId);

            bool shouldFlushTeardown = false;

            lock (s_lock)
            {
                AddToBufferUnlocked(entry);

                if (phase == PhaseReloadTeardown)
                {
                    s_teardownEntries.Add(entry);
                    if (s_teardownEntries.Count > PersistLimit)
                        s_teardownEntries.RemoveAt(0);
                    s_teardownDirty = true;
                    shouldFlushTeardown = IsMainThread();
                }
            }

            if (shouldFlushTeardown)
                TryFlushTeardownSnapshot();
        }

        private static void AddToBufferUnlocked(LogEntry entry)
        {
            s_buffer[s_head] = entry;
            s_head = (s_head + 1) % BufferSize;
            if (s_count < BufferSize)
                s_count++;
        }

        private static void TryPersistHistorySnapshot()
        {
            try
            {
                PersistHistorySnapshot();
            }
            catch { }
        }

        private static void PersistHistorySnapshot()
        {
            JArray array;

            lock (s_lock)
            {
                int take = System.Math.Min(s_count, PersistLimit);
                var items = new LogEntry[take];
                for (int i = 0; i < take; i++)
                {
                    int idx = ((s_head - 1 - i) % BufferSize + BufferSize) % BufferSize;
                    items[i] = s_buffer[idx];
                }

                var ordered = new System.Collections.Generic.List<LogEntry>(take);
                for (int i = take - 1; i >= 0; i--)
                {
                    if (items[i] != null)
                        ordered.Add(items[i]);
                }

                array = BuildSnapshotArray(ordered);
            }

            SessionState.SetString(
                HistorySessionStateKey,
                array.ToString(Newtonsoft.Json.Formatting.None)
            );
        }

        private static void TryFlushTeardownSnapshot()
        {
            try
            {
                FlushTeardownSnapshot();
            }
            catch { }
        }

        private static void FlushTeardownSnapshot()
        {
            JArray array;

            lock (s_lock)
            {
                if (!s_teardownDirty)
                    return;

                array = BuildSnapshotArray(s_teardownEntries);
                s_teardownDirty = false;
            }

            SessionState.SetString(
                TeardownSessionStateKey,
                array.ToString(Newtonsoft.Json.Formatting.None)
            );
        }

        private static void TrySetReloadMarker()
        {
            try
            {
                SessionState.SetBool(ReloadMarkerSessionStateKey, true);
            }
            catch { }
        }

        private static JArray BuildSnapshotArray(
            System.Collections.Generic.IEnumerable<LogEntry> entries
        )
        {
            var array = new JArray();

            foreach (var entry in entries)
            {
                if (entry == null)
                    continue;

                array.Add(
                    new JObject
                    {
                        ["type"] = entry.Type,
                        ["message"] = Truncate(entry.Message, MessageTruncateLimit),
                        ["stackTrace"] = Truncate(entry.StackTrace, StackTraceTruncateLimit),
                        ["timestamp"] = entry.Timestamp,
                        ["phase"] = entry.Phase,
                        ["windowId"] = entry.WindowId,
                    }
                );
            }

            return array;
        }

        private static void RestoreFromSessionState()
        {
            RestoreSnapshot(HistorySessionStateKey);
            RestoreSnapshot(TeardownSessionStateKey);

            try
            {
                SessionState.EraseString(HistorySessionStateKey);
            }
            catch { }

            try
            {
                SessionState.EraseString(TeardownSessionStateKey);
            }
            catch { }
        }

        private static void RestoreSnapshot(string key)
        {
            string json = SessionState.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(json))
                return;

            try
            {
                JArray array = JArray.Parse(json);

                lock (s_lock)
                {
                    foreach (JToken token in array)
                    {
                        if (!(token is JObject obj))
                            continue;

                        string type = obj.Value<string>("type") ?? "log";
                        string message = obj.Value<string>("message") ?? string.Empty;
                        string stackTrace = obj.Value<string>("stackTrace") ?? string.Empty;
                        string timestamp = obj.Value<string>("timestamp") ?? string.Empty;
                        string phase = obj.Value<string>("phase") ?? PhaseNormal;
                        int windowId = obj.Value<int?>("windowId") ?? 0;

                        AddToBufferUnlocked(
                            new LogEntry(type, message, stackTrace, timestamp, phase, windowId)
                        );
                    }
                }
            }
            catch { }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value;

            return value.Substring(0, maxLength) + TruncationMarker;
        }

        public static void Clear()
        {
            lock (s_lock)
            {
                System.Array.Clear(s_buffer, 0, s_buffer.Length);
                s_head = 0;
                s_count = 0;
                s_teardownEntries.Clear();
                s_teardownDirty = false;
            }

            s_phase = PhaseNormal;

            try
            {
                SessionState.EraseString(HistorySessionStateKey);
            }
            catch { }

            try
            {
                SessionState.EraseString(TeardownSessionStateKey);
            }
            catch { }

            try
            {
                SessionState.EraseBool(ReloadMarkerSessionStateKey);
            }
            catch { }
        }

        public static LogEntry[] GetEntries(string filter, int maxResults)
        {
            if (maxResults <= 0)
                return new LogEntry[0];

            filter = (filter ?? "all").ToLowerInvariant();

            lock (s_lock)
            {
                var results = new System.Collections.Generic.List<LogEntry>(
                    System.Math.Min(maxResults, s_count)
                );
                int collected = 0;

                for (int i = 0; i < s_count && collected < maxResults; i++)
                {
                    int idx = ((s_head - 1 - i) % BufferSize + BufferSize) % BufferSize;
                    var entry = s_buffer[idx];
                    if (entry == null)
                        continue;

                    if (
                        filter == "all"
                        || filter == "errors" && entry.Type == "error"
                        || filter == "warnings" && entry.Type == "warning"
                        || filter == "logs" && entry.Type == "log"
                    )
                    {
                        results.Add(entry);
                        collected++;
                    }
                }

                return results.ToArray();
            }
        }

        public static LogEntry[] GetReloadWindowEntries(int maxResults)
        {
            if (maxResults <= 0)
                return new LogEntry[0];

            lock (s_lock)
            {
                int windowId = 0;
                bool windowFound = false;

                for (int i = 0; i < s_count; i++)
                {
                    int idx = ((s_head - 1 - i) % BufferSize + BufferSize) % BufferSize;
                    var entry = s_buffer[idx];
                    if (entry == null)
                        continue;

                    if (entry.Phase != PhaseNormal)
                    {
                        windowId = entry.WindowId;
                        windowFound = true;
                        break;
                    }
                }

                if (!windowFound)
                    return new LogEntry[0];

                var matches = new System.Collections.Generic.List<LogEntry>();

                for (int i = 0; i < s_count; i++)
                {
                    int idx = ((s_head - 1 - i) % BufferSize + BufferSize) % BufferSize;
                    var entry = s_buffer[idx];
                    if (entry == null)
                        continue;

                    if (entry.Phase != PhaseNormal && entry.WindowId == windowId)
                        matches.Add(entry);
                }

                matches.Reverse();

                if (matches.Count > maxResults)
                    matches = matches.GetRange(matches.Count - maxResults, maxResults);

                return matches.ToArray();
            }
        }
    }
}
