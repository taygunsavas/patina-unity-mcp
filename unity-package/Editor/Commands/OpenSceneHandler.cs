using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Patina.Editor.Commands
{
    public sealed class OpenSceneHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string scenePath = parameters?["scene_path"]?.Value<string>();
            string modeStr = parameters?["mode"]?.Value<string>() ?? "single";

            if (string.IsNullOrEmpty(scenePath))
                throw new ArgumentException("scene_path is required");

            string capturedPath = scenePath;
            string capturedMode = modeStr;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                OpenSceneMode mode = capturedMode == "additive"
                    ? OpenSceneMode.Additive
                    : OpenSceneMode.Single;

                if (mode == OpenSceneMode.Single)
                {
                    Scene active = SceneManager.GetActiveScene();
                    if (active.isDirty)
                    {
                        throw new InvalidOperationException(
                            $"DIRTY_SCENE: Active scene '{active.name}' has unsaved changes. " +
                            "Call save_scene first, or open with mode=\"additive\". dirty_warning=true");
                    }
                }

                Scene opened = EditorSceneManager.OpenScene(capturedPath, mode);
                if (!opened.IsValid())
                    throw new InvalidOperationException($"Failed to open scene at '{capturedPath}'");

                return new JObject
                {
                    ["name"] = opened.name,
                    ["path"] = opened.path,
                    ["mode"] = capturedMode,
                    ["success"] = true
                };
            });

            return result;
        }
    }
}
