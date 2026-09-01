using System;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Patina.Editor
{
    public enum DomainReloadRunStart
    {
        Started,
        Joined,
    }

    public sealed class DomainReloadRunRecord
    {
        public long StartedUtcTicks { get; set; }
        public int StartReloadCount { get; set; }
        public long ElapsedMs { get; set; }
    }

    public static class DomainReloadTracker
    {
        private const string RunRecordSessionStateKeyPrefix = "Patina.DomainReloadTracker.Run.";
        private const long StaleRunThresholdMs = 200000;

        public static int ReloadCount => McpBridgeServer.ReloadCount;

        public static DomainReloadRunStart BeginRun(string commandKey)
        {
            string key = RunRecordSessionStateKeyFor(commandKey);
            JObject existing = ReadRecord(key);

            if (existing != null && !IsStale(existing) && !HasReloadObserved(existing))
            {
                int pendingCount = existing.Value<int?>("pendingCount") ?? 1;
                existing["pendingCount"] = pendingCount + 1;
                WriteRecord(key, existing);
                return DomainReloadRunStart.Joined;
            }

            var record = new JObject
            {
                ["startedUtcTicks"] = DateTime.UtcNow.Ticks,
                ["startReloadCount"] = ReloadCount,
                ["pendingCount"] = 1,
            };
            WriteRecord(key, record);
            return DomainReloadRunStart.Started;
        }

        public static bool TryCompleteRun(string commandKey, out DomainReloadRunRecord record)
        {
            record = null;
            string key = RunRecordSessionStateKeyFor(commandKey);
            JObject parsed = ReadRecord(key);
            if (parsed == null)
                return false;

            if (!HasReloadObserved(parsed))
            {
                if (IsStale(parsed))
                    SessionState.EraseString(key);
                return false;
            }

            long startedUtcTicks = parsed.Value<long?>("startedUtcTicks") ?? 0;
            int startReloadCount = parsed.Value<int?>("startReloadCount") ?? 0;
            int pendingCount = (parsed.Value<int?>("pendingCount") ?? 1) - 1;

            if (pendingCount <= 0)
                SessionState.EraseString(key);
            else
            {
                parsed["pendingCount"] = pendingCount;
                WriteRecord(key, parsed);
            }

            record = new DomainReloadRunRecord
            {
                StartedUtcTicks = startedUtcTicks,
                StartReloadCount = startReloadCount,
                ElapsedMs = ElapsedMsSince(startedUtcTicks),
            };
            return true;
        }

        public static void ClearRun(string commandKey)
        {
            string key = RunRecordSessionStateKeyFor(commandKey);
            JObject parsed = ReadRecord(key);
            if (parsed == null)
                return;

            int pendingCount = (parsed.Value<int?>("pendingCount") ?? 1) - 1;
            if (pendingCount <= 0)
                SessionState.EraseString(key);
            else
            {
                parsed["pendingCount"] = pendingCount;
                WriteRecord(key, parsed);
            }
        }

        private static bool HasReloadObserved(JObject record)
        {
            int startReloadCount = record.Value<int?>("startReloadCount") ?? 0;
            return ReloadCount > startReloadCount;
        }

        private static bool IsStale(JObject record)
        {
            long startedUtcTicks = record.Value<long?>("startedUtcTicks") ?? 0;
            return ElapsedMsSince(startedUtcTicks) > StaleRunThresholdMs;
        }

        private static long ElapsedMsSince(long startedUtcTicks)
        {
            return (long)new TimeSpan(DateTime.UtcNow.Ticks - startedUtcTicks).TotalMilliseconds;
        }

        private static JObject ReadRecord(string key)
        {
            string raw = SessionState.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(raw))
                return null;

            try
            {
                return JObject.Parse(raw);
            }
            catch (Exception)
            {
                SessionState.EraseString(key);
                return null;
            }
        }

        private static void WriteRecord(string key, JObject record)
        {
            SessionState.SetString(key, record.ToString(Newtonsoft.Json.Formatting.None));
        }

        private static string RunRecordSessionStateKeyFor(string commandKey)
        {
            return RunRecordSessionStateKeyPrefix + commandKey;
        }
    }
}
