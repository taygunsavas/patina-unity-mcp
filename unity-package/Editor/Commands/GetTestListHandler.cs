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
        public async Task<object> HandleAsync(JObject parameters)
        {
            string mode = parameters?["mode"]?.Value<string>() ?? "EditMode";

            if (mode != "EditMode" && mode != "PlayMode")
                throw new ArgumentException($"mode must be 'EditMode' or 'PlayMode', got: {mode}");

            string capturedMode = mode;

            return await MainThreadQueue.EnqueueAsync<JObject>(() =>
            {
                var tcs  = new System.Threading.Tasks.TaskCompletionSource<JArray>();
                var api  = ScriptableObject.CreateInstance<TestRunnerApi>();
                var testMode = capturedMode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode;

                api.RetrieveTestList(testMode, (root) =>
                {
                    var arr = new JArray();
                    CollectTests(root, arr);
                    tcs.TrySetResult(arr);
                });

                // RetrieveTestList calls the callback synchronously on main thread
                var tests = tcs.Task.IsCompletedSuccessfully
                    ? tcs.Task.Result
                    : new JArray();

                return new JObject
                {
                    ["mode"]  = capturedMode,
                    ["count"] = tests.Count,
                    ["tests"] = tests
                };
            });
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
