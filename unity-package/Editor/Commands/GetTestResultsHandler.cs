using Newtonsoft.Json.Linq;
using System.Linq;
using System.Threading.Tasks;
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
                var (results, running) = TestResultBuffer.GetResults(capturedMode);

                int passed  = results.Count(r => r.Status == "Passed");
                int failed  = results.Count(r => r.Status == "Failed");
                int skipped = results.Count(r => r.Status == "Skipped"
                                                  || r.Status == "Inconclusive");

                var arr = new JArray();
                foreach (var r in results)
                {
                    arr.Add(new JObject
                    {
                        ["name"]            = r.Name,
                        ["fullName"]        = r.FullName,
                        ["status"]          = r.Status,
                        ["durationSeconds"] = r.DurationSeconds,
                        ["message"]         = r.Message ?? ""
                    });
                }

                return new JObject
                {
                    ["mode"]    = capturedMode,
                    ["total"]   = results.Count,
                    ["passed"]  = passed,
                    ["failed"]  = failed,
                    ["skipped"] = skipped,
                    ["running"] = running,
                    ["results"] = arr
                };
            });
        }
    }
}
