using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Patina.Editor.Commands
{
    public sealed class SaveSceneHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string scenePath = parameters?["scene_path"]?.Value<string>();
            string saveAsPath = parameters?["save_as_path"]?.Value<string>();

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                Scene scene;
                if (!string.IsNullOrEmpty(scenePath))
                {
                    scene = SceneManager.GetSceneByPath(scenePath);
                    if (!scene.IsValid())
                        throw new InvalidOperationException($"No loaded scene found at path '{scenePath}'");
                }
                else
                {
                    scene = SceneManager.GetActiveScene();
                }

                bool success;
                string savedPath;

                if (!string.IsNullOrEmpty(saveAsPath))
                {
                    success = EditorSceneManager.SaveScene(scene, saveAsPath);
                    savedPath = saveAsPath;
                }
                else
                {
                    if (string.IsNullOrEmpty(scene.path))
                        throw new InvalidOperationException(
                            "Scene has no path. Provide save_as_path to save a new unsaved scene.");
                    success = EditorSceneManager.SaveScene(scene);
                    savedPath = scene.path;
                }

                if (!success)
                    throw new InvalidOperationException($"Failed to save scene '{scene.name}'");

                return new JObject
                {
                    ["savedPath"] = savedPath,
                    ["success"] = true
                };
            });

            return result;
        }
    }
}
