using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class GetTestListHandler : ICommandHandler
    {
        private static readonly TimeSpan s_retrieveTimeout = TimeSpan.FromSeconds(30);

        public async Task<object> HandleAsync(JObject parameters)
        {
            string mode = parameters?["mode"]?.Value<string>() ?? "EditMode";

            if (mode != "EditMode" && mode != "PlayMode")
                throw new ArgumentException($"mode must be 'EditMode' or 'PlayMode', got: {mode}");

            JArray tests = await RetrieveTestsAsync(mode);

            return new JObject
            {
                ["mode"]  = mode,
                ["count"] = tests.Count,
                ["tests"] = tests
            };
        }

        internal static async Task<JArray> RetrieveTestsAsync(string mode)
        {
            Task<JArray> pending = await MainThreadQueue.EnqueueAsync<Task<JArray>>(() =>
            {
                var tcs  = new TaskCompletionSource<JArray>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var api  = ScriptableObject.CreateInstance<TestRunnerApi>();
                var testMode = mode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode;

                api.RetrieveTestList(testMode, root =>
                {
                    var arr = new JArray();
                    CollectTests(root, arr);
                    tcs.TrySetResult(arr);
                });

                return tcs.Task;
            });

            Task finished = await Task.WhenAny(pending, Task.Delay(s_retrieveTimeout));
            if (finished != pending)
                throw new TimeoutException($"Timed out retrieving the {mode} test list");

            return await pending;
        }

        private static void CollectTests(ITestAdaptor node, JArray arr)
        {
            if (!node.IsSuite)
            {
                arr.Add(new JObject
                {
                    ["name"]     = node.Name,
                    ["fullName"] = node.FullName,
                    ["category"] = node.Categories != null && node.Categories.Length > 0
                        ? string.Join(",", node.Categories)
                        : ""
                });
            }
            else
            {
                foreach (var child in node.Children)
                    CollectTests(child, arr);
            }
        }
    }
}
