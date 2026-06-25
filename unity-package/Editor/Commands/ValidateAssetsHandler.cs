using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class ValidateAssetsHandler : ICommandHandler
    {
        private const int MaxIssues = 500;

        public async Task<object> HandleAsync(JObject parameters)
        {
            string path = parameters?["path"]?.Value<string>();
            string severityFilter = parameters?["severity_filter"]?.Value<string>() ?? "all";
            bool recursive = parameters?["recursive"]?.Value<bool>() ?? true;

            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path is required");

            string capturedPath = path;
            string capturedFilter = severityFilter.ToLowerInvariant();
            bool capturedRecursive = recursive;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                var issues = new List<JObject>();
                bool truncated = false;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                List<string> prefabPaths = new List<string>();
                if (Directory.Exists(capturedPath))
                {
                    string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { capturedPath });
                    foreach (var guid in guids)
                    {
                        string p = AssetDatabase.GUIDToAssetPath(guid);
                        if (!capturedRecursive)
                        {
                            string dir = Path.GetDirectoryName(p)?.Replace('\\', '/');
                            string targetDir = capturedPath.Replace('\\', '/');
                            if (dir != targetDir) continue;
                        }
                        prefabPaths.Add(p);
                    }
                }
                else if (File.Exists(capturedPath))
                {
                    if (capturedPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        prefabPaths.Add(capturedPath);
                    }
                }
                else
                {
                    var obj = AssetDatabase.LoadAssetAtPath<GameObject>(capturedPath);
                    if (obj != null && capturedPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        prefabPaths.Add(capturedPath);
                    }
                    else
                    {
                        throw new ArgumentException($"Path does not exist as file or folder: {capturedPath}");
                    }
                }

                foreach (string prefabPath in prefabPaths)
                {
                    if (issues.Count >= MaxIssues) { truncated = true; break; }
                    if (sw.ElapsedMilliseconds > 8000)
                    {
                        Debug.LogWarning("[Patina] validate_assets exceeded 8s soft timeout — results may be incomplete.");
                        truncated = true;
                        break;
                    }

                    ValidatePrefab(prefabPath, issues, capturedFilter, ref truncated, sw);
                }

                return new JObject
                {
                    ["issueCount"] = issues.Count,
                    ["truncated"] = truncated,
                    ["issues"] = new JArray(issues)
                };
            });
        }

        private static void ValidatePrefab(string assetPath, List<JObject> issues, string filter, ref bool truncated, System.Diagnostics.Stopwatch sw)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) return;

            var allTransforms = prefab.GetComponentsInChildren<Transform>(true);
            foreach (var t in allTransforms)
            {
                if (issues.Count >= MaxIssues) { truncated = true; return; }
                if (sw.ElapsedMilliseconds > 8000) { truncated = true; return; }

                GameObject go = t.gameObject;
                Component[] comps = go.GetComponents<Component>();
                foreach (var comp in comps)
                {
                    if (issues.Count >= MaxIssues) { truncated = true; return; }

                    if (comp == null)
                    {
                        AddIssue(issues, filter, assetPath, GetTransformPath(prefab.transform, t), "MissingScript", null, "Missing script slot (null component)", "error");
                        continue;
                    }

                    try
                    {
                        var so = new SerializedObject(comp);
                        var prop = so.GetIterator();
                        bool enterChildren = true;
                        while (prop.NextVisible(enterChildren))
                        {
                            enterChildren = prop.propertyType != SerializedPropertyType.String;
                            if (prop.propertyType == SerializedPropertyType.ObjectReference)
                            {
                                if (prop.objectReferenceValue == null)
                                {
                                    if (prop.objectReferenceInstanceIDValue != 0)
                                    {
                                        AddIssue(issues, filter, assetPath, GetTransformPath(prefab.transform, t), comp.GetType().Name,
                                            prop.name, "Missing object reference (broken reference)", "error");
                                    }
                                    else
                                    {
                                        var fieldInfo = comp.GetType().GetField(prop.name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                        if (fieldInfo != null)
                                        {
                                            bool isRequired = false;
                                            foreach (var attr in fieldInfo.GetCustomAttributes(true))
                                            {
                                                string attrName = attr.GetType().Name;
                                                if (attrName.IndexOf("Required", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                    attrName.IndexOf("NotNull", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                    attrName.IndexOf("NonNull", StringComparison.OrdinalIgnoreCase) >= 0)
                                                {
                                                    isRequired = true;
                                                    break;
                                                }
                                            }
                                            if (isRequired)
                                            {
                                                AddIssue(issues, filter, assetPath, GetTransformPath(prefab.transform, t), comp.GetType().Name,
                                                    prop.name, "Null required serialized field", "error");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { /* skip unserializable components */ }
                }

                if (PrefabUtility.IsPrefabAssetMissing(go))
                {
                    AddIssue(issues, filter, assetPath, GetTransformPath(prefab.transform, t), "PrefabInstance", null,
                        "Missing prefab source asset", "error");
                }
            }
        }

        private static string GetTransformPath(Transform root, Transform current)
        {
            if (current == root) return "/";
            string path = current.name;
            Transform p = current.parent;
            while (p != null && p != root)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }
            return path;
        }

        private static void AddIssue(List<JObject> issues, string filter, string assetPath, string transformPath,
            string component, string field, string message, string severity)
        {
            if (filter != "all" && filter != severity) return;
            issues.Add(new JObject
            {
                ["assetPath"] = assetPath,
                ["transformPath"] = transformPath,
                ["component"] = component ?? string.Empty,
                ["field"] = field ?? string.Empty,
                ["issueType"] = message,
                ["severity"] = severity
            });
        }
    }
}
