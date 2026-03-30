using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class RunTestsHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string mode   = parameters?["mode"]?.Value<string>() ?? "EditMode";
            string filter = parameters?["filter"]?.Value<string>();

            if (mode != "EditMode" && mode != "PlayMode")
                throw new ArgumentException($"mode must be 'EditMode' or 'PlayMode', got: {mode}");

            string capturedMode   = mode;
            string capturedFilter = filter;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                var api       = ScriptableObject.CreateInstance<TestRunnerApi>();
                var callbacks = new PatinaTestCallbacks(capturedMode);
                api.RegisterCallbacks(callbacks);

                var testMode = capturedMode == "PlayMode"
                    ? TestMode.PlayMode
                    : TestMode.EditMode;

                var filter = new Filter
                {
                    testMode = testMode
                };

                if (!string.IsNullOrEmpty(capturedFilter))
                    filter.testNames = new[] { capturedFilter };

                api.Execute(new ExecutionSettings(filter));

                return new JObject
                {
                    ["started"] = true,
                    ["mode"]    = capturedMode,
                    ["filter"]  = capturedFilter ?? ""
                };
            });
        }
    }
}
