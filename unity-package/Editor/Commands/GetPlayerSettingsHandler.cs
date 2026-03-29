using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class GetPlayerSettingsHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            return await MainThreadQueue.EnqueueAsync<JObject>(() =>
            {
                string groupStr = parameters?["build_target_group"]?.Value<string>() ?? "Standalone";
                BuildTargetGroup group = ParseBuildTargetGroup(groupStr);

                return new JObject
                {
                    ["productName"] = PlayerSettings.productName,
                    ["companyName"] = PlayerSettings.companyName,
                    ["bundleVersion"] = PlayerSettings.bundleVersion,
                    ["applicationIdentifier"] = PlayerSettings.GetApplicationIdentifier(group),
                    ["scriptingBackend"] = PlayerSettings.GetScriptingBackend(group).ToString(),
                    ["apiCompatibilityLevel"] = PlayerSettings.GetApiCompatibilityLevel(group).ToString(),
                    ["colorSpace"] = PlayerSettings.colorSpace.ToString()
                };
            });
        }

        private static BuildTargetGroup ParseBuildTargetGroup(string value)
        {
            switch (value)
            {
                case "Android": return BuildTargetGroup.Android;
                case "iOS": return BuildTargetGroup.iOS;
                case "WebGL": return BuildTargetGroup.WebGL;
                default: return BuildTargetGroup.Standalone;
            }
        }
    }
}
