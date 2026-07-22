using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class GetProjectSettingsHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                string scriptingBackend = PlayerSettings
                    .GetScriptingBackend(
                        NamedBuildTarget.FromBuildTargetGroup(
                            BuildPipeline.GetBuildTargetGroup(
                                EditorUserBuildSettings.activeBuildTarget
                            )
                        )
                    )
                    .ToString();

                Vector3 gravity = Physics.gravity;

                return new JObject
                {
                    ["unityVersion"] = Application.unityVersion,
                    ["productName"] = Application.productName,
                    ["companyName"] = Application.companyName,
                    ["activeBuildTarget"] = EditorUserBuildSettings.activeBuildTarget.ToString(),
                    ["colorSpace"] = PlayerSettings.colorSpace.ToString(),
                    ["isPlaying"] = EditorApplication.isPlaying,
                    ["isCompiling"] = EditorApplication.isCompiling,
                    ["scriptingBackend"] = scriptingBackend,
                    ["physicsGravity"] = new JArray(gravity.x, gravity.y, gravity.z),
                };
            });

            return result;
        }
    }
}
