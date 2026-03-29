using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class SetPlayModeHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string mode = parameters != null && parameters.TryGetValue("mode", out JToken modeToken)
                ? modeToken.Value<string>() ?? ""
                : "";

            await MainThreadQueue.EnqueueAsync<object>(() =>
            {
                switch (mode)
                {
                    case "enter":
                        EditorApplication.isPlaying = true;
                        break;
                    case "exit":
                        EditorApplication.isPlaying = false;
                        break;
                    case "pause":
                        EditorApplication.isPaused = true;
                        break;
                    case "unpause":
                        EditorApplication.isPaused = false;
                        break;
                    case "step":
                        EditorApplication.Step();
                        break;
                    default:
                        throw new System.ArgumentException($"Unknown play mode: {mode}");
                }
                return null;
            });

            return new JObject
            {
                ["requestedMode"] = mode,
                ["success"] = true
            };
        }
    }
}
