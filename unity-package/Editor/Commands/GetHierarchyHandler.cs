using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Patina.Editor.Commands
{
    public sealed class GetHierarchyHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            int maxDepth = int.MaxValue;
            string nameFilter = null;

            if (parameters != null)
            {
                if (parameters.TryGetValue("max_depth", out JToken depthToken) && depthToken.Type != JTokenType.Null)
                    maxDepth = depthToken.Value<int>();

                if (parameters.TryGetValue("name_filter", out JToken filterToken) && filterToken.Type != JTokenType.Null)
                    nameFilter = filterToken.Value<string>();
            }

            List<object> result = await MainThreadQueue.EnqueueAsync(() =>
            {
                var scenes = new List<object>();

                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded)
                        continue;

                    GameObject[] rootObjects = scene.GetRootGameObjects();
                    var children = new JArray();

                    foreach (GameObject root in rootObjects)
                    {
                        JObject node = BuildNode(root, 0, maxDepth, nameFilter);
                        if (node != null)
                            children.Add(node);
                    }

                    scenes.Add(new JObject
                    {
                        ["sceneName"] = scene.name,
                        ["scenePath"] = scene.path,
                        ["rootObjects"] = children
                    });
                }

                return scenes;
            });

            return new JObject
            {
                ["scenes"] = JArray.FromObject(result)
            };
        }

        private static JObject BuildNode(GameObject go, int currentDepth, int maxDepth, string nameFilter)
        {
            bool nameMatches = string.IsNullOrEmpty(nameFilter) ||
                               go.name.IndexOf(nameFilter, System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (currentDepth >= maxDepth && !nameMatches)
                return null;

            var components = new JArray();
            foreach (Component comp in go.GetComponents<Component>())
            {
                if (comp != null)
                    components.Add(comp.GetType().Name);
            }

            var children = new JArray();
            if (currentDepth < maxDepth)
            {
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    Transform child = go.transform.GetChild(i);
                    JObject childNode = BuildNode(child.gameObject, currentDepth + 1, maxDepth, nameFilter);
                    if (childNode != null)
                        children.Add(childNode);
                }
            }

            if (!string.IsNullOrEmpty(nameFilter) && !nameMatches && children.Count == 0)
                return null;

            return new JObject
            {
                ["name"] = go.name,
                ["activeSelf"] = go.activeSelf,
                ["components"] = components,
                ["children"] = children
            };
        }
    }
}
