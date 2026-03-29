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

            public LogEntry(string type, string message, string stackTrace)
            {
                Type = type;
                Message = message;
                StackTrace = stackTrace;
            }
        }

        private const int BufferSize = 500;
        private static readonly LogEntry[] _buffer = new LogEntry[BufferSize];
        private static int _head = 0;
        private static int _count = 0;
        private static readonly object _lock = new object();

        static ConsoleLogBuffer()
        {
            Application.logMessageReceivedThreaded += OnLogReceived;
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

            var entry = new LogEntry(type, message, stackTrace);

            lock (_lock)
            {
                _buffer[_head] = entry;
                _head = (_head + 1) % BufferSize;
                if (_count < BufferSize)
                    _count++;
            }
        }

        public static LogEntry[] GetEntries(string filter, int maxResults)
        {
            if (maxResults <= 0)
                return new LogEntry[0];

            filter = (filter ?? "all").ToLowerInvariant();

            lock (_lock)
            {
                var results = new System.Collections.Generic.List<LogEntry>(System.Math.Min(maxResults, _count));
                int collected = 0;

                // Walk backwards from the most recent entry
                for (int i = 0; i < _count && collected < maxResults; i++)
                {
                    int idx = ((_head - 1 - i) % BufferSize + BufferSize) % BufferSize;
                    var entry = _buffer[idx];
                    if (entry == null)
                        continue;

                    if (filter == "all"
                        || filter == "errors" && entry.Type == "error"
                        || filter == "warnings" && entry.Type == "warning"
                        || filter == "logs" && entry.Type == "log")
                    {
                        results.Add(entry);
                        collected++;
                    }
                }

                return results.ToArray();
            }
        }
    }
}
