using System;
using System.Collections.Generic;
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

        private static readonly List<CompilerEntry> _entries = new List<CompilerEntry>();
        private static readonly object _lock = new object();
        private static bool _hasResults = false;

        static CompilationErrorBuffer()
        {
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (messages == null || messages.Length == 0)
                return;

            lock (_lock)
            {
                if (!_hasResults)
                {
                    _entries.Clear();
                    _hasResults = true;
                }

                foreach (var msg in messages)
                {
                    string severity = msg.type == CompilerMessageType.Error ? "error" : "warning";
                    _entries.Add(new CompilerEntry(
                        msg.file ?? string.Empty,
                        msg.line,
                        msg.column,
                        msg.message ?? string.Empty,
                        severity));
                }
            }
        }

        public static CompilerEntry[] GetErrors()
        {
            lock (_lock)
            {
                return _entries.FindAll(e => e.Severity == "error").ToArray();
            }
        }

        public static CompilerEntry[] GetWarnings()
        {
            lock (_lock)
            {
                return _entries.FindAll(e => e.Severity == "warning").ToArray();
            }
        }

        public static CompilerEntry[] GetAll()
        {
            lock (_lock)
            {
                return _entries.ToArray();
            }
        }

        public static bool HasResults => _hasResults;

        public static void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
                _hasResults = false;
            }
        }
    }
}
