using System;
using System.Collections.Generic;
using UnityEditor.TestTools.TestRunner.Api;

namespace Patina.Editor
{
    public sealed class TestRunResult
    {
        public string Name;
        public string FullName;
        public string Status;
        public double DurationSeconds;
        public string Message;
    }

    public static class TestResultBuffer
    {
        private static readonly object _lock = new object();
        private static List<TestRunResult> _editModeResults  = new List<TestRunResult>();
        private static List<TestRunResult> _playModeResults  = new List<TestRunResult>();
        private static bool _editModeRunning;
        private static bool _playModeRunning;

        public static void SetRunning(string mode, bool running)
        {
            lock (_lock)
            {
                if (mode == "PlayMode") _playModeRunning = running;
                else                    _editModeRunning = running;
            }
        }

        public static void SetResults(string mode, List<TestRunResult> results)
        {
            lock (_lock)
            {
                if (mode == "PlayMode") { _playModeResults = results; _playModeRunning = false; }
                else                    { _editModeResults = results; _editModeRunning = false; }
            }
        }

        public static (List<TestRunResult> results, bool running) GetResults(string mode)
        {
            lock (_lock)
            {
                if (mode == "PlayMode") return (_playModeResults, _playModeRunning);
                return (_editModeResults, _editModeRunning);
            }
        }
    }

    public sealed class PatinaTestCallbacks : ICallbacks
    {
        private readonly string _mode;
        private readonly List<TestRunResult> _results = new List<TestRunResult>();

        public PatinaTestCallbacks(string mode)
        {
            _mode = mode;
            TestResultBuffer.SetRunning(_mode, true);
        }

        public void RunStarted(ITestAdaptor testsToRun)
        {
            if (CountLeafTests(testsToRun) == 0)
                TestResultBuffer.SetResults(_mode, new List<TestRunResult>());
        }

        private static int CountLeafTests(ITestAdaptor node)
        {
            if (!node.IsSuite) return 1;
            int count = 0;
            if (node.Children != null)
                foreach (var child in node.Children)
                    count += CountLeafTests(child);
            return count;
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            TestResultBuffer.SetResults(_mode, _results);
        }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (!result.Test.IsSuite)
            {
                _results.Add(new TestRunResult
                {
                    Name           = result.Test.Name,
                    FullName       = result.Test.FullName,
                    Status         = result.TestStatus.ToString(),
                    DurationSeconds = result.Duration,
                    Message        = result.Message
                });
            }
        }
    }
}
