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
                bool wasDirty = stage.scene.isDirty;
                bool saved = false;
                bool discardedChanges = false;
                string dialogAutomation = "not-needed";

                if (!wasDirty)
                {
                    StageUtility.GoToMainStage();
                }
                else if (DialogAutomation.IsAvailable)
                {
                    dialogAutomation = "interaction-context";
                    using (
                        DialogAutomation.Scope(
                            ("Prefab Has Been Modified", saveChanges ? "Save" : "Discard Changes")
                        )
                    )
                    {
                        StageUtility.GoToMainStage();
                    }
                    if (saveChanges)
                        saved = true;
                    else
                        discardedChanges = true;
                }
                else
                {
                    dialogAutomation = "public-api";
                    if (saveChanges)
                    {
                        EditorSceneManager.SaveScene(stage.scene);
                        saved = true;
                    }
                    else
                    {
                        stage.ClearDirtiness();
                        discardedChanges = true;
                    }
                    StageUtility.GoToMainStage();
                }

                if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                {
                    return new JObject
                    {
                        ["success"] = false,
                        ["message"] = "The prefab stage remained open; the close was cancelled by Unity. The prefab may be in an immutable folder, or an unexpected dialog was shown.",
                        ["saved"] = false,
                        ["changedAssets"] = new JArray(),
                        ["wasDirty"] = wasDirty,
                        ["discardedChanges"] = false,
                        ["dialogAutomation"] = dialogAutomation
                    };
                }

                return new JObject
                {
                    ["success"] = true,
                    ["saved"] = saved,
                    ["changedAssets"] = saved ? new JArray(assetPath) : new JArray(),
                    ["wasDirty"] = wasDirty,
                    ["discardedChanges"] = discardedChanges,
                    ["dialogAutomation"] = dialogAutomation
                };
            });
        }
    }
}
