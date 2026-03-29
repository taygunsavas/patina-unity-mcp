using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace Patina.Editor.Commands
{
    public sealed class GetConsoleLogsHandler : ICommandHandler
    {
        public Task<object> HandleAsync(JObject parameters)
        {
            string filter = parameters != null && parameters.TryGetValue("filter", out JToken filterToken)
                ? filterToken.Value<string>() ?? "all"
                : "all";

            int maxResults = parameters != null && parameters.TryGetValue("max_results", out JToken maxToken)
                ? maxToken.Value<int>()
                : 50;

            var logEntries = ConsoleLogBuffer.GetEntries(filter, maxResults);

            var entries = new JArray();
            foreach (var entry in logEntries)
            {
                entries.Add(new JObject
                {
                    ["type"] = entry.Type,
                    ["message"] = entry.Message,
                    ["stackTrace"] = entry.StackTrace
                });
            }

            object result = new JObject
            {
                ["totalReturned"] = entries.Count,
                ["entries"] = entries
            };

            return Task.FromResult(result);
        }
    }
}
