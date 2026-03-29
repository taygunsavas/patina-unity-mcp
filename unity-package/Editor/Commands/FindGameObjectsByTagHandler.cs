using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class FindGameObjectsByTagHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string tag = parameters?["tag"]?.Value<string>();
            int maxResults = parameters?["max_results"]?.Value<int>() ?? 50;

            if (string.IsNullOrEmpty(tag))
                throw new ArgumentException("tag is required");

            string capturedTag = tag;
            int capturedMax = maxResults;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject[] found;
                try
                {
                    found = GameObject.FindGameObjectsWithTag(capturedTag);
                }
                catch (UnityException)
                {
                    throw new InvalidOperationException($"Tag '{capturedTag}' is not defined in the project");
                }

                var objects = new JArray();
                int limit = Math.Min(found.Length, capturedMax);
                for (int i = 0; i < limit; i++)
                {
                    GameObject go = found[i];
                    objects.Add(new JObject
                    {
                        ["name"] = go.name,
                        ["instanceId"] = go.GetInstanceID(),
                        ["scenePath"] = BuildScenePath(go.transform)
                    });
                }

                return new JObject
                {
                    ["tag"] = capturedTag,
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
