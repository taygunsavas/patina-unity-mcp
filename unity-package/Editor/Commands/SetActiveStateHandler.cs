using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class SetActiveStateHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string gameObjectName = parameters?["game_object_name"]?.Value<string>();
            bool? activeValue = parameters?["active"]?.Value<bool>();

            if (string.IsNullOrEmpty(gameObjectName))
                throw new ArgumentException("game_object_name is required");
            if (activeValue == null)
                throw new ArgumentException("active is required");

            string capturedGoName = gameObjectName;
            bool capturedActive = activeValue.Value;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject go = FindSceneGameObject(capturedGoName);
                if (go == null)
                    throw new InvalidOperationException($"GameObject '{capturedGoName}' not found");

                Undo.RecordObject(go, "Set Active State");
                go.SetActive(capturedActive);
                EditorSceneManager.MarkSceneDirty(go.scene);

                return new JObject
                {
                    ["gameObject"] = capturedGoName,
                    ["active"] = capturedActive,
                    ["success"] = true
                };
            });

            return result;
        }

        private static GameObject FindSceneGameObject(string name)
        {
            foreach (GameObject go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (go.scene.IsValid() && go.name == name)
                    return go;
            }
            return null;
        }
    }
}
