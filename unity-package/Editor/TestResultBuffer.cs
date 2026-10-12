using System;
using System.Collections.Generic;
using UnityEditor;
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

    public sealed class TestRunSnapshot
    {
        public List<TestRunResult> Results;
        public bool Running;
        public string RunId;
        public DateTime? StartedAt;
        public DateTime? FinishedAt;
    }

    public static class TestResultBuffer
    {
        private static readonly object s_lock = new object();
        private static readonly TestRunSnapshot s_editMode = new TestRunSnapshot
        {
            Results = new List<TestRunResult>(),
        };
        private static readonly TestRunSnapshot s_playMode = new TestRunSnapshot
        {
            Results = new List<TestRunResult>(),
        };

        private static TestRunSnapshot StateFor(string mode)
        {
            return mode == "PlayMode" ? s_playMode : s_editMode;
        }

        public static void BeginRun(string mode, string runId)
        {
            lock (s_lock)
            {
                TestRunSnapshot state = StateFor(mode);
                state.Results = new List<TestRunResult>();
                state.Running = true;
                state.RunId = runId;
                state.StartedAt = DateTime.UtcNow;
                state.FinishedAt = null;
            }
        }

        public static bool IsCurrent(string mode, string runId)
        {
            lock (s_lock)
            {
                return StateFor(mode).RunId == runId;
            }
        }

        public static void SetResults(string mode, List<TestRunResult> results)
        {
            lock (s_lock)
            {
                TestRunSnapshot state = StateFor(mode);
                state.Results = results;
                state.Running = false;
                state.FinishedAt = DateTime.UtcNow;
            }
        }

        public static TestRunSnapshot GetResults(string mode)
        {
            lock (s_lock)
            {
                TestRunSnapshot state = StateFor(mode);
                return new TestRunSnapshot
                {
                    Results = state.Results,
                    Running = state.Running,
                    RunId = state.RunId,
                    StartedAt = state.StartedAt,
                    FinishedAt = state.FinishedAt,
                };
            }
        }
    }

    public sealed class PatinaTestCallbacks : ICallbacks
    {
        private readonly string _mode;
        private readonly string _runId;
        private readonly TestRunnerApi _api;
        private bool _released;
        private readonly List<TestRunResult> _results = new List<TestRunResult>();

        public PatinaTestCallbacks(string mode, string runId, TestRunnerApi api)
        {
            _mode = mode;
            _runId = runId;
            _api = api;
        }

        public void RunStarted(ITestAdaptor testsToRun)
        {
            if (!TestResultBuffer.IsCurrent(_mode, _runId))
                return;

            if (CountLeafTests(testsToRun) == 0)
                TestResultBuffer.SetResults(_mode, new List<TestRunResult>());
        }

        public void Release()
        {
            if (_released)
                return;

            _released = true;
            _api.UnregisterCallbacks(this);
            TestRunnerApi api = _api;
            EditorApplication.delayCall += () =>
            {
                if (api != null)
                    UnityEngine.Object.DestroyImmediate(api);
            };
        }

        private static int CountLeafTests(ITestAdaptor node)
        {
            if (!node.IsSuite)
                return 1;
            int count = 0;
            if (node.Children != null)
                foreach (var child in node.Children)
                    count += CountLeafTests(child);
            return count;
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            if (TestResultBuffer.IsCurrent(_mode, _runId))
                TestResultBuffer.SetResults(_mode, _results);

            Release();
        }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (!TestResultBuffer.IsCurrent(_mode, _runId))
                return;

            if (!result.Test.IsSuite)
            {
                _results.Add(
                    new TestRunResult
                    {
                        Name = result.Test.Name,
                        FullName = result.Test.FullName,
                        Status = result.TestStatus.ToString(),
                        DurationSeconds = result.Duration,
                        Message = result.Message,
                    }
                );
            }
        }
    }
}
