using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class FindGameObjectsByPathHandler : ICommandHandler
    {
        private const int DefaultMax = 50;
        private const int HardMax = 200;

        public async Task<object> HandleAsync(JObject parameters)
        {
            string pathPrefix = parameters?["path_prefix"]?.Value<string>();
            if (string.IsNullOrEmpty(pathPrefix))
                throw new ArgumentException("path_prefix is required");

            int maxResults = parameters?["max_results"]?.Value<int>() ?? DefaultMax;
            if (maxResults > HardMax) maxResults = HardMax;
            if (maxResults < 1) maxResults = 1;

            string capturedPrefix = pathPrefix.StartsWith("/") ? pathPrefix : "/" + pathPrefix;
            int capturedMax = maxResults;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);

                var objects = new JArray();
                int count = 0;

                foreach (GameObject go in allObjects)
                {
                    if (count >= capturedMax) break;
                    if (!go.scene.IsValid()) continue;

                    string scenePath = GameObjectFinder.GetScenePath(go);
                    if (!scenePath.StartsWith(capturedPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                    int depth = GetDepth(go.transform);

                    objects.Add(new JObject
                    {
                        ["name"] = go.name,
                        ["instanceId"] = go.GetInstanceID(),
                        ["scenePath"] = scenePath,
                        ["depth"] = depth
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

        private static int GetDepth(Transform t)
        {
            int depth = 0;
            while (t.parent != null) { t = t.parent; depth++; }
            return depth;
        }
    }
}
