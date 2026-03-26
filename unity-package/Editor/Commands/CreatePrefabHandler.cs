using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class CreatePrefabHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string gameObjectName = parameters?["game_object_name"]?.Value<string>();
            string savePath = parameters?["save_path"]?.Value<string>();

            if (string.IsNullOrEmpty(gameObjectName))
                throw new ArgumentException("game_object_name is required");
            if (string.IsNullOrEmpty(savePath))
                throw new ArgumentException("save_path is required");

            string capturedName = gameObjectName;
            string capturedPath = savePath;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject go = GameObject.Find(capturedName);
                if (go == null)
                    throw new InvalidOperationException($"GameObject '{capturedName}' not found");

                string directory = System.IO.Path.GetDirectoryName(capturedPath);
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

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, capturedPath);
                if (prefab == null)
                    throw new InvalidOperationException($"Failed to create prefab at '{capturedPath}'");

                return new JObject
                {
                    ["prefabPath"] = capturedPath,
                    ["sourceObject"] = go.name,
                    ["success"] = true
                };
            });

            return result;
        }
    }
}
