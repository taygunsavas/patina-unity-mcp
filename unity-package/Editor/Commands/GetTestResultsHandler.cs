using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class GetTestResultsHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string mode = parameters?["mode"]?.Value<string>() ?? "EditMode";
            string capturedMode = mode;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                TestRunSnapshot snapshot = TestResultBuffer.GetResults(capturedMode);
                var results = snapshot.Results;

                int passed = results.Count(r => r.Status == "Passed");
                int failed = results.Count(r => r.Status == "Failed");
                int skipped = results.Count(r =>
                    r.Status == "Skipped" || r.Status == "Inconclusive"
                );

                var arr = new JArray();
                foreach (var r in results)
                {
                    arr.Add(
                        new JObject
                        {
                            ["name"] = r.Name,
                            ["fullName"] = r.FullName,
                            ["status"] = r.Status,
                            ["durationSeconds"] = r.DurationSeconds,
                            ["message"] = r.Message ?? "",
                        }
                    );
                }

                return new JObject
                {
                    ["mode"] = capturedMode,
                    ["total"] = results.Count,
                    ["passed"] = passed,
                    ["failed"] = failed,
                    ["skipped"] = skipped,
                    ["running"] = snapshot.Running,
                    ["runId"] = snapshot.RunId,
                    ["startedAt"] = snapshot.StartedAt?.ToString("o"),
                    ["finishedAt"] = snapshot.FinishedAt?.ToString("o"),
                    ["results"] = arr,
                };
            });
        }
    }
}
