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
            bool includeChildren = parameters?["include_children"]?.Value<bool>() ?? false;
            int maxDepth = parameters?["max_depth"]?.Value<int>() ?? 0;

            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("asset_path is required");

            string capturedPath = assetPath;
            string capturedTransform = transformPath;
            bool capturedIncludeChildren = includeChildren;
            int capturedMaxDepth = maxDepth;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(capturedPath);
                if (root == null)
                    throw new InvalidOperationException(
                        $"Prefab asset not found at path: {capturedPath}"
                    );

                GameObject targetGo = ObjectReferenceResolver.FindByPath(root, capturedTransform);
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

                var result = new JObject
                {
                    ["assetPath"] = capturedPath,
                    ["transformPath"] = capturedTransform ?? string.Empty,
                    ["components"] = componentsArray,
                };

                if (capturedIncludeChildren)
                {
                    var childrenArray = new JArray();
                    CollectChildren(
                        targetGo.transform,
                        targetGo.transform,
                        1,
                        capturedMaxDepth,
                        childrenArray
                    );
                    result["children"] = childrenArray;
                }

                return result;
            });
        }

        private static void CollectChildren(
            Transform relativeTo,
            Transform current,
            int depth,
            int maxDepth,
            JArray output
        )
        {
            if (maxDepth > 0 && depth > maxDepth)
                return;

            for (int i = 0; i < current.childCount; i++)
            {
                Transform child = current.GetChild(i);
                string relativePath = GetRelativePath(relativeTo, child);

                var componentTypes = new JArray();
                Component[] comps = child.GetComponents<Component>();
                foreach (Component comp in comps)
                {
                    componentTypes.Add(comp == null ? "MissingScript" : comp.GetType().FullName);
                }

                output.Add(
                    new JObject
                    {
                        ["transformPath"] = relativePath,
                        ["name"] = child.gameObject.name,
                        ["components"] = componentTypes,
                    }
                );

                CollectChildren(relativeTo, child, depth + 1, maxDepth, output);
            }
        }

        private static string GetRelativePath(Transform relativeTo, Transform target)
        {
            var parts = new List<string>();
            Transform t = target;
            while (t != null && t != relativeTo)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            return string.Join("/", parts);
        }
    }
}
