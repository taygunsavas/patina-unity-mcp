using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class SetLayerHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string gameObjectName = parameters?["game_object_name"]?.Value<string>();
            string layerName = parameters?["layer_name"]?.Value<string>();
            bool applyToChildren = parameters?["apply_to_children"]?.Value<bool?>() ?? false;

            if (string.IsNullOrEmpty(gameObjectName))
                throw new ArgumentException("game_object_name is required");
            if (string.IsNullOrEmpty(layerName))
                throw new ArgumentException("layer_name is required");

            string capturedGoName = gameObjectName;
            string capturedLayerName = layerName;
            bool capturedApplyToChildren = applyToChildren;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                int layerIndex = LayerMask.NameToLayer(capturedLayerName);
                if (layerIndex == -1)
                    throw new InvalidOperationException($"Layer '{capturedLayerName}' is not defined. Add it in Project Settings > Tags and Layers.");

                GameObject go = FindSceneGameObject(capturedGoName);
                if (go == null)
                    throw new InvalidOperationException($"GameObject '{capturedGoName}' not found");

                if (capturedApplyToChildren)
                {
                    foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                    {
                        try { Undo.RecordObject(t.gameObject, "Set Layer"); } catch { /* proceed without undo */ }
                        t.gameObject.layer = layerIndex;
                    }
                }
                else
                {
                    try { Undo.RecordObject(go, "Set Layer"); } catch { /* proceed without undo */ }
                    go.layer = layerIndex;
                }

                EditorSceneManager.MarkSceneDirty(go.scene);

                return new JObject
                {
                    ["gameObject"] = capturedGoName,
                    ["layerName"] = capturedLayerName,
                    ["layerIndex"] = layerIndex,
                    ["success"] = true
                };
            });

            return result;
        }

        private static GameObject FindSceneGameObject(string name)
        {
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go.scene.IsValid() && go.name == name)
                    return go;
            }
            return null;
        }
    }
}
