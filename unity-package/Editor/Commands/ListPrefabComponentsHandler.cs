using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class ListPrefabComponentsHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string assetPath = parameters?["asset_path"]?.Value<string>();
            string transformPath = parameters?["transform_path"]?.Value<string>();

            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("asset_path is required");

            string capturedPath = assetPath;
            string capturedTransform = transformPath;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(capturedPath);
                if (root == null)
                    throw new InvalidOperationException(
                        $"Prefab asset not found at path: {capturedPath}"
                    );

                GameObject targetGo = FindGameObjectByPath(root, capturedTransform);
                if (targetGo == null)
                    throw new InvalidOperationException(
                        $"GameObject not found at path '{capturedTransform}' inside prefab"
                    );

                Component[] comps = targetGo.GetComponents<Component>();
                var componentsArray = new JArray();

                foreach (Component comp in comps)
                {
                    if (comp == null)
                    {
                        componentsArray.Add(
                            new JObject { ["type"] = "MissingScript", ["instanceId"] = 0 }
                        );
                        continue;
                    }

                    componentsArray.Add(
                        new JObject
                        {
                            ["type"] = comp.GetType().FullName,
                            ["instanceId"] = comp.GetInstanceID(),
                        }
                    );
                }

                return new JObject
                {
                    ["assetPath"] = capturedPath,
                    ["transformPath"] = capturedTransform ?? string.Empty,
                    ["components"] = componentsArray,
                };
            });
        }

        private static GameObject FindGameObjectByPath(GameObject root, string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/" || path == ".")
                return root;

            Transform current = root.transform;
            string[] parts = path.Split('/');
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                    continue;
                Transform child = current.Find(part);
                if (child == null)
                    return null;
                current = child;
            }
            return current.gameObject;
        }
    }
}
