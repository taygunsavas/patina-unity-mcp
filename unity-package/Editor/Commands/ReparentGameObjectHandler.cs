using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class ReparentGameObjectHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string gameObjectName = parameters?["game_object_name"]?.Value<string>();
            string newParentName = parameters?["new_parent_name"]?.Value<string>();
            bool worldPositionStays = true;

            if (parameters != null && parameters.TryGetValue("world_position_stays", out JToken wpsToken) && wpsToken.Type != JTokenType.Null)
                worldPositionStays = wpsToken.Value<bool>();

            if (string.IsNullOrEmpty(gameObjectName))
                throw new ArgumentException("game_object_name is required");

            string capturedName = gameObjectName;
            string capturedParent = newParentName;
            bool capturedWps = worldPositionStays;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject go = GameObject.Find(capturedName);
                if (go == null)
                    throw new InvalidOperationException($"GameObject '{capturedName}' not found");

                Transform newParent = null;
                if (!string.IsNullOrEmpty(capturedParent))
                {
                    GameObject parentGo = GameObject.Find(capturedParent);
                    if (parentGo == null)
                        throw new InvalidOperationException($"Parent GameObject '{capturedParent}' not found");
                    newParent = parentGo.transform;
                }

                Undo.SetTransformParent(go.transform, newParent, capturedWps, "Reparent GameObject");
                EditorSceneManager.MarkSceneDirty(go.scene);

                return new JObject
                {
                    ["gameObject"] = go.name,
                    ["newParent"] = newParent != null ? newParent.name : "(scene root)",
                    ["success"] = true
                };
            });

            return result;
        }
    }
}
