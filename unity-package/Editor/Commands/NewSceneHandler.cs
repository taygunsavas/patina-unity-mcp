using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Patina.Editor.Commands
{
    public sealed class NewSceneHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string name = parameters?["name"]?.Value<string>();
            string savePath = parameters?["save_path"]?.Value<string>();
            string setup = parameters?["setup"]?.Value<string>() ?? "default_game_objects";

            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name is required");

            string capturedSavePath = string.IsNullOrEmpty(savePath)
                ? $"Assets/Scenes/{name}.unity"
                : savePath;
            string capturedSetup = setup;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                Scene existing = SceneManager.GetActiveScene();
                if (existing.isDirty)
                    throw new InvalidOperationException(
                        $"DIRTY_SCENE: Active scene '{existing.name}' has unsaved changes. " +
                        "Call save_scene first. dirty_warning=true");

                NewSceneSetup sceneSetup = capturedSetup == "empty"
                    ? NewSceneSetup.EmptyScene
                    : NewSceneSetup.DefaultGameObjects;

                var scene = EditorSceneManager.NewScene(sceneSetup, NewSceneMode.Single);

                string directory = Path.GetDirectoryName(capturedSavePath);
                if (!string.IsNullOrEmpty(directory) && !AssetDatabase.IsValidFolder(directory))
                {
                    string[] parts = directory.Replace("\\", "/").Split('/');
                    string current = parts[0];
                    for (int i = 1; i < parts.Length; i++)
                    {
                        string next = current + "/" + parts[i];
                        if (!AssetDatabase.IsValidFolder(next))
                            AssetDatabase.CreateFolder(current, parts[i]);
                        current = next;
                    }
                }

                bool saved = EditorSceneManager.SaveScene(scene, capturedSavePath);
                if (!saved)
                    throw new InvalidOperationException($"Failed to save new scene at '{capturedSavePath}'");

                return new JObject
                {
                    ["name"] = scene.name,
                    ["path"] = scene.path,
                    ["success"] = true
                };
            });

            return result;
        }
    }
}
