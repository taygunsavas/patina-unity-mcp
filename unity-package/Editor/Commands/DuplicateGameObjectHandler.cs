using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class DuplicateGameObjectHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string gameObjectName = parameters?["game_object_name"]?.Value<string>();
            string newName = parameters?["new_name"]?.Value<string>();

            if (string.IsNullOrEmpty(gameObjectName))
                throw new ArgumentException("game_object_name is required");

            string capturedName = gameObjectName;
            string capturedNewName = newName;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject go = GameObject.Find(capturedName);
                if (go == null)
                    throw new InvalidOperationException($"GameObject '{capturedName}' not found");

                GameObject duplicate = UnityEngine.Object.Instantiate(go, go.transform.parent);
                Undo.RegisterCreatedObjectUndo(duplicate, "Duplicate GameObject");

                if (!string.IsNullOrEmpty(capturedNewName))
                    duplicate.name = capturedNewName;

                EditorSceneManager.MarkSceneDirty(duplicate.scene);

                return new JObject
                {
                    ["originalName"] = go.name,
                    ["duplicateName"] = duplicate.name,
                    ["instanceId"] = duplicate.GetInstanceID()
                };
            });

            return result;
        }
    }
}
