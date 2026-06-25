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
                : 20;

            bool includeStackTrace = parameters != null &&
                parameters.TryGetValue("include_stack_trace", out JToken stackToken) &&
                stackToken.Type != JTokenType.Null &&
                stackToken.Value<bool>();

            var logEntries = ConsoleLogBuffer.GetEntries(filter, maxResults);

            var entries = new JArray();
            foreach (var entry in logEntries)
            {
                var obj = new JObject
                {
                    ["type"] = entry.Type,
                    ["message"] = entry.Message,
                    ["timestamp"] = entry.Timestamp
                };
                if (includeStackTrace)
                    obj["stackTrace"] = entry.StackTrace;
                entries.Add(obj);
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
