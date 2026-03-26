using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class InstantiatePrefabHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string prefabPath = parameters?["prefab_path"]?.Value<string>();
            string name = parameters?["name"]?.Value<string>();
            float[] position = null;

            if (string.IsNullOrEmpty(prefabPath))
                throw new ArgumentException("prefab_path is required");

            if (parameters != null && parameters.TryGetValue("position", out JToken posToken) && posToken is JArray posArr && posArr.Count >= 3)
            {
                position = new float[3];
                for (int i = 0; i < 3; i++)
                    position[i] = posArr[i].Value<float>();
            }

            string capturedPath = prefabPath;
            string capturedName = name;
            float[] capturedPos = position;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(capturedPath);
                if (prefab == null)
                    throw new InvalidOperationException($"Prefab not found at '{capturedPath}'");

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(instance, "Instantiate Prefab");

                if (!string.IsNullOrEmpty(capturedName))
                    instance.name = capturedName;

                if (capturedPos != null)
                    instance.transform.position = new Vector3(capturedPos[0], capturedPos[1], capturedPos[2]);

                EditorSceneManager.MarkSceneDirty(instance.scene);

                return new JObject
                {
                    ["name"] = instance.name,
                    ["instanceId"] = instance.GetInstanceID(),
                    ["prefabPath"] = capturedPath
                };
            });

            return result;
        }
    }
}
