using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class FindGameObjectsByComponentHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string componentType = parameters?["component_type"]?.Value<string>();
            int maxResults = parameters?["max_results"]?.Value<int>() ?? 50;

            if (string.IsNullOrEmpty(componentType))
                throw new ArgumentException("component_type is required");

            string capturedType = componentType;
            int capturedMax = maxResults;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                Type type = AddComponentHandler.FindType(capturedType);
                if (type == null)
                    throw new InvalidOperationException($"Component type '{capturedType}' not found");
                if (!typeof(Component).IsAssignableFrom(type))
                    throw new InvalidOperationException($"'{capturedType}' is not a Component type");

                Component[] found = (Component[])UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None);

                var objects = new JArray();
                int limit = Math.Min(found.Length, capturedMax);
                for (int i = 0; i < limit; i++)
                {
                    GameObject go = found[i].gameObject;
                    objects.Add(new JObject
                    {
                        ["name"] = go.name,
                        ["instanceId"] = go.GetInstanceID(),
                        ["scenePath"] = BuildScenePath(go.transform)
                    });
                }

                return new JObject
                {
                    ["componentType"] = type.Name,
                    ["totalFound"] = found.Length,
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
