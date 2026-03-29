using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class FindGameObjectsByLayerHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string layerName = parameters?["layer_name"]?.Value<string>();
            int maxResults = parameters?["max_results"]?.Value<int>() ?? 50;

            if (string.IsNullOrEmpty(layerName))
                throw new ArgumentException("layer_name is required");

            string capturedLayer = layerName;
            int capturedMax = maxResults;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                int layerIndex = LayerMask.NameToLayer(capturedLayer);
                if (layerIndex < 0)
                    throw new InvalidOperationException($"Layer '{capturedLayer}' is not defined in Project Settings → Tags and Layers");

                GameObject[] all = (GameObject[])UnityEngine.Object.FindObjectsByType(
                    typeof(GameObject), FindObjectsSortMode.None);

                var objects = new JArray();
                int totalFound = 0;
                foreach (GameObject go in all)
                {
                    if (go.layer != layerIndex) continue;
                    totalFound++;
                    if (objects.Count < capturedMax)
                    {
                        objects.Add(new JObject
                        {
                            ["name"] = go.name,
                            ["instanceId"] = go.GetInstanceID(),
                            ["scenePath"] = BuildScenePath(go.transform)
                        });
                    }
                }

                return new JObject
                {
                    ["layerName"] = capturedLayer,
                    ["layerIndex"] = layerIndex,
                    ["totalFound"] = totalFound,
                    ["count"] = objects.Count,
                    ["objects"] = objects
                };
            });

            return result;
        }

        private static string BuildScenePath(Transform t)
        {
            var parts = new List<string>();
            Transform current = t;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return "/" + string.Join("/", parts);
        }
    }
}
