using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class RunTestsHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string mode = parameters?["mode"]?.Value<string>() ?? "EditMode";
            string filter = parameters?["filter"]?.Value<string>();

            if (mode != "EditMode" && mode != "PlayMode")
                throw new ArgumentException($"mode must be 'EditMode' or 'PlayMode', got: {mode}");

            int? matchedCount = null;
            string[] matchedNames = null;

            if (!string.IsNullOrEmpty(filter))
            {
                JArray available = await GetTestListHandler.RetrieveTestsAsync(mode);
                matchedNames = available
                    .Where(t => MatchesFilter(t, filter))
                    .Select(t => t.Value<string>("fullName"))
                    .ToArray();

                if (matchedNames.Length == 0)
                    throw new ArgumentException(
                        $"filter '{filter}' matched 0 tests in {mode} ({available.Count} tests available)"
                    );

                matchedCount = matchedNames.Length;
            }

            string runId = Guid.NewGuid().ToString();

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                var callbacks = new PatinaTestCallbacks(mode, runId, api);
                api.RegisterCallbacks(callbacks);

                var testMode = mode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode;

                var runFilter = new Filter { testMode = testMode };

                if (matchedNames != null)
                    runFilter.testNames = matchedNames;

                bool began = false;
                try
                {
                    if (mode == "PlayMode")
                        EditorSceneManager.SaveOpenScenes();

                    TestResultBuffer.BeginRun(mode, runId);
                    began = true;
                    api.Execute(new ExecutionSettings(runFilter));
                }
                catch
                {
                    if (began)
                        TestResultBuffer.SetResults(mode, new List<TestRunResult>());

                    callbacks.Release();
                    throw;
                }

                return new JObject
                {
                    ["started"] = true,
                    ["runId"] = runId,
                    ["mode"] = mode,
                    ["filter"] = filter ?? "",
                    ["matchedCount"] = matchedCount,
                };
            });
        }

        private static bool MatchesFilter(JToken test, string filter)
        {
            return test.Value<string>("name").Contains(filter, StringComparison.Ordinal)
                || test.Value<string>("fullName").Contains(filter, StringComparison.Ordinal);
        }
    }
}
