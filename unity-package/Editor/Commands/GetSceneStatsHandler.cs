using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Patina.Editor.Commands
{
    public sealed class GetSceneStatsHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            bool includeTypeCounts = parameters?["include_per_type_counts"]?.Value<bool>() ?? false;
            bool capturedTypeCounts = includeTypeCounts;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                var allObjects = Object.FindObjectsByType<GameObject>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);

                int objectCount = allObjects.Length;
                int activeCount = 0;
                int componentCount = 0;
                int scriptCount = 0;
                int prefabInstanceCount = 0;
                int maxDepth = 0;
                var typeFreq = new Dictionary<string, int>();
                var typeSet = new HashSet<string>();

                foreach (GameObject go in allObjects)
                {
                    if (go.activeInHierarchy) activeCount++;
                    if (PrefabUtility.IsPartOfAnyPrefab(go) && go.transform.parent == null
                        || PrefabUtility.IsOutermostPrefabInstanceRoot(go))
                        prefabInstanceCount++;

                    Component[] comps = go.GetComponents<Component>();
                    componentCount += comps.Length;

                    foreach (Component comp in comps)
                    {
                        if (comp == null) continue;
                        string typeName = comp.GetType().Name;
                        typeSet.Add(typeName);

                        if (capturedTypeCounts)
                        {
                            if (!typeFreq.ContainsKey(typeName)) typeFreq[typeName] = 0;
                            typeFreq[typeName]++;
                        }

                        if (comp is MonoBehaviour) scriptCount++;
                    }

                    int depth = GetDepth(go.transform);
                    if (depth > maxDepth) maxDepth = depth;
                }

                // Scene file size
                long sceneSize = 0;
                Scene active = SceneManager.GetActiveScene();
                if (!string.IsNullOrEmpty(active.path))
                {
                    string projectRoot = Path.GetFullPath(
                        Path.Combine(UnityEngine.Application.dataPath, ".."));
                    string fullPath = Path.Combine(projectRoot, active.path);
                    if (File.Exists(fullPath))
                        sceneSize = new FileInfo(fullPath).Length;
                }

                var result = new JObject
                {
                    ["objectCount"] = objectCount,
                    ["activeObjectCount"] = activeCount,
                    ["componentCount"] = componentCount,
                    ["uniqueComponentTypes"] = typeSet.Count,
                    ["scriptCount"] = scriptCount,
                    ["prefabInstanceCount"] = prefabInstanceCount,
                    ["maxHierarchyDepth"] = maxDepth,
                    ["sceneSize"] = sceneSize
                };

                if (capturedTypeCounts)
                {
                    var typeCounts = new JObject();
                    foreach (var kv in typeFreq)
                        typeCounts[kv.Key] = kv.Value;
                    result["componentTypeCounts"] = typeCounts;
                }

                return (object)result;
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
