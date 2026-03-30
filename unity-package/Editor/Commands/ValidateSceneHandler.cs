using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class ValidateSceneHandler : ICommandHandler
    {
        private const int MaxIssues = 500;

        public async Task<object> HandleAsync(JObject parameters)
        {
            string severityFilter = parameters?["severity_filter"]?.Value<string>() ?? "all";
            string capturedFilter = severityFilter.ToLowerInvariant();

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                var issues = new List<JObject>();
                bool truncated = false;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);

                foreach (GameObject go in allObjects)
                {
                    if (issues.Count >= MaxIssues) { truncated = true; break; }
                    if (sw.ElapsedMilliseconds > 5000)
                    {
                        Debug.LogWarning("[Patina] validate_scene exceeded 5s soft timeout — results may be incomplete.");
                        truncated = true;
                        break;
                    }

                    Component[] comps = go.GetComponents<Component>();
                    foreach (Component comp in comps)
                    {
                        if (issues.Count >= MaxIssues) { truncated = true; break; }

                        if (comp == null)
                        {
                            AddIssue(issues, capturedFilter, go.name, "MissingScript", null, "Missing script slot (null component)", "error");
                            continue;
                        }

                        // Serialize and scan for null object references
                        try
                        {
                            var so = new SerializedObject(comp);
                            SerializedProperty prop = so.GetIterator();
                            bool enterChildren = true;
                            while (prop.NextVisible(enterChildren))
                            {
                                enterChildren = prop.propertyType != SerializedPropertyType.String;
                                if (prop.propertyType == SerializedPropertyType.ObjectReference
                                    && prop.objectReferenceValue == null
                                    && prop.objectReferenceInstanceIDValue != 0)
                                {
                                    AddIssue(issues, capturedFilter, go.name, comp.GetType().Name,
                                        prop.name, "Missing object reference", "error");
                                    if (issues.Count >= MaxIssues) { truncated = true; break; }
                                }
                            }
                        }
                        catch { /* skip unserializable components */ }
                    }

                    // Missing prefab source asset (visible as "Missing Prefab" in hierarchy)
                    if (PrefabUtility.IsPrefabAssetMissing(go))
                    {
                        AddIssue(issues, capturedFilter, go.name, "PrefabInstance", null,
                            "Missing prefab source asset", "error");
                    }
                }

                return new JObject
                {
                    ["issueCount"] = issues.Count,
                    ["truncated"] = truncated,
                    ["issues"] = new JArray(issues)
                };
            });
        }

        private static void AddIssue(List<JObject> issues, string filter, string goName,
            string component, string field, string message, string severity)
        {
            if (filter != "all" && filter != severity) return;
            issues.Add(new JObject
            {
                ["gameObject"] = goName,
                ["component"] = component,
                ["field"] = field ?? string.Empty,
                ["issueType"] = message,
                ["severity"] = severity
            });
        }
    }
}
