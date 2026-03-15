using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class LogToConsoleHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string message = parameters != null && parameters.TryGetValue("message", out JToken messageToken)
                ? messageToken.Value<string>() ?? "No message"
                : "No message";

            string level = parameters != null && parameters.TryGetValue("level", out JToken levelToken)
                ? (levelToken.Value<string>() ?? "info").ToLowerInvariant()
                : "info";

            await MainThreadQueue.EnqueueAsync<object>(() =>
            {
                switch (level)
                {
                    case "warning":
                        Debug.LogWarning($"[Patina] {message}");
                        break;
                    case "error":
                        Debug.LogError($"[Patina] {message}");
                        break;
                    default:
                        Debug.Log($"[Patina] {message}");
                        break;
                }

                return null;
            });

            return new JObject
            {
                ["success"] = true,
                ["message"] = $"Logged to console at level '{level}'"
            };
        }
    }
}
