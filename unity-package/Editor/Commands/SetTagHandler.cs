using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class SetTagHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string gameObjectName = parameters?["game_object_name"]?.Value<string>();
            string tag = parameters?["tag"]?.Value<string>();

            if (string.IsNullOrEmpty(gameObjectName))
                throw new ArgumentException("game_object_name is required");
            if (string.IsNullOrEmpty(tag))
                throw new ArgumentException("tag is required");

            string capturedGoName = gameObjectName;
            string capturedTag = tag;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject go = FindSceneGameObject(capturedGoName);
                if (go == null)
                    throw new InvalidOperationException($"GameObject '{capturedGoName}' not found");

                Undo.RecordObject(go, "Set Tag");
                try
                {
                    go.tag = capturedTag;
                }
                catch (UnityException)
                {
                    throw new InvalidOperationException($"Tag '{capturedTag}' is not registered. Add it in Project Settings > Tags and Layers.");
                }
                EditorSceneManager.MarkSceneDirty(go.scene);

                return new JObject
                {
                    ["gameObject"] = capturedGoName,
                    ["tag"] = capturedTag,
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
