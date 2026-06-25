using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class ClosePrefabStageHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            bool saveChanges = parameters != null &&
                parameters.TryGetValue("save_changes", out var saveToken) &&
                saveToken.Value<bool>();

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage == null)
                {
                    return new JObject
                    {
                        ["success"] = false,
                        ["message"] = "No active prefab stage found",
                        ["saved"] = false,
                        ["changedAssets"] = new JArray()
                    };
                }

                string assetPath = stage.assetPath;
                bool saved = false;

                if (saveChanges)
                {
                    EditorSceneManager.SaveScene(stage.scene);
                    saved = true;
                }

                StageUtility.GoToMainStage();

                return new JObject
                {
                    ["success"] = true,
                    ["saved"] = saved,
                    ["changedAssets"] = saved ? new JArray(assetPath) : new JArray()
                };
            });
        }
    }
}
