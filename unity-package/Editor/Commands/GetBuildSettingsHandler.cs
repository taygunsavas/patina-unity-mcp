using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class GetBuildSettingsHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                var scenes = new JArray();
                EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
                int enabledCount = 0;
                for (int i = 0; i < buildScenes.Length; i++)
                {
                    EditorBuildSettingsScene s = buildScenes[i];
                    int buildIndex = s.enabled ? enabledCount++ : -1;
                    scenes.Add(new JObject
                    {
                        ["path"] = s.path,
                        ["enabled"] = s.enabled,
                        ["buildIndex"] = buildIndex
                    });
                }

                return new JObject
                {
                    ["activeBuildTarget"] = EditorUserBuildSettings.activeBuildTarget.ToString(),
                    ["scriptingBackend"] = PlayerSettings.GetScriptingBackend(
                        BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget)).ToString(),
                    ["scenes"] = scenes
                };
            });

            return result;
        }
    }
}
