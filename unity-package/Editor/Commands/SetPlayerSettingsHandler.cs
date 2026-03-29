using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class SetPlayerSettingsHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            return await MainThreadQueue.EnqueueAsync<JObject>(() =>
            {
                string groupStr = parameters?["build_target_group"]?.Value<string>() ?? "Standalone";
                BuildTargetGroup group = ParseBuildTargetGroup(groupStr);

                var changed = new JArray();

                string productName = parameters?["product_name"]?.Value<string>();
                if (productName != null) { PlayerSettings.productName = productName; changed.Add("productName"); }

                string companyName = parameters?["company_name"]?.Value<string>();
                if (companyName != null) { PlayerSettings.companyName = companyName; changed.Add("companyName"); }

                string bundleVersion = parameters?["bundle_version"]?.Value<string>();
                if (bundleVersion != null) { PlayerSettings.bundleVersion = bundleVersion; changed.Add("bundleVersion"); }

                string appId = parameters?["application_identifier"]?.Value<string>();
                if (appId != null) { PlayerSettings.SetApplicationIdentifier(group, appId); changed.Add("applicationIdentifier"); }

                if (changed.Count > 0)
                    AssetDatabase.SaveAssets();

                return new JObject
                {
                    ["changed"] = changed,
                    ["success"] = true
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
