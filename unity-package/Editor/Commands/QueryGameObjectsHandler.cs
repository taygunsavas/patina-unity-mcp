using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class QueryGameObjectsHandler : ICommandHandler
    {
        private const int DefaultMax = 50;
        private const int HardMax = 200;

        public async Task<object> HandleAsync(JObject parameters)
        {
            var tagsToken = parameters?["tags"] as JArray;
            var compsToken = parameters?["components"] as JArray;
            string layerName = parameters?["layer_name"]?.Value<string>();
            string pathPrefix = parameters?["path_prefix"]?.Value<string>();
            bool activeOnly = parameters?["active_only"]?.Value<bool>() ?? true;
            int maxResults = parameters?["max_results"]?.Value<int>() ?? DefaultMax;
            if (maxResults > HardMax) maxResults = HardMax;
            if (maxResults < 1) maxResults = 1;

            string[] tags = tagsToken != null ? tagsToken.ToObject<string[]>() : null;
            string[] components = compsToken != null ? compsToken.ToObject<string[]>() : null;
            string capturedLayer = layerName;
            string capturedPath = pathPrefix;
            bool capturedActive = activeOnly;
            int capturedMax = maxResults;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                FindObjectsInactive inactive = capturedActive
                    ? FindObjectsInactive.Exclude
                    : FindObjectsInactive.Include;

                var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(inactive, FindObjectsSortMode.None);

                int layerIndex = -1;
                if (!string.IsNullOrEmpty(capturedLayer))
                {
                    layerIndex = LayerMask.NameToLayer(capturedLayer);
                    if (layerIndex < 0)
                        throw new InvalidOperationException($"Layer '{capturedLayer}' is not defined");
                }

                var objects = new JArray();
                int count = 0;

                foreach (GameObject go in allObjects)
                {
                    if (count >= capturedMax) break;
                    if (!go.scene.IsValid()) continue;

                    // Tag filters (all must match)
                    if (tags != null && tags.Length > 0)
                    {
                        bool allTags = true;
                        foreach (string tag in tags)
                        {
                            if (!go.CompareTag(tag)) { allTags = false; break; }
                        }
                        if (!allTags) continue;
                    }

                    // Layer filter
                    if (layerIndex >= 0 && go.layer != layerIndex) continue;

                    // Component filters (all must be present)
                    if (components != null && components.Length > 0)
                    {
                        bool allComps = true;
                        foreach (string compName in components)
                        {
                            if (go.GetComponent(compName) == null) { allComps = false; break; }
                        }
                        if (!allComps) continue;
                    }

                    // Path prefix filter
                    if (!string.IsNullOrEmpty(capturedPath))
                    {
                        string scenePath = GameObjectFinder.GetScenePath(go);
                        string normalizedPrefix = capturedPath.StartsWith("/") ? capturedPath : "/" + capturedPath;
                        if (!scenePath.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    }

                    objects.Add(new JObject
                    {
                        ["name"] = go.name,
                        ["instanceId"] = go.GetInstanceID(),
                        ["scenePath"] = GameObjectFinder.GetScenePath(go)
                    });
                    count++;
                }

                return new JObject
                {
                    ["count"] = count,
                    ["objects"] = objects
                };
            });
        }
    }
}
