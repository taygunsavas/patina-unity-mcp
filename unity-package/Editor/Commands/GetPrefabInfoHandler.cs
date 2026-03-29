using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class GetPrefabInfoHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string target = parameters?["target"]?.Value<string>();
            string targetType = parameters?["target_type"]?.Value<string>();

            if (string.IsNullOrEmpty(target))
                throw new ArgumentException("target is required");

            string capturedTarget = target;
            string capturedTargetType = targetType;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                bool tryAsset = capturedTargetType == "asset" || capturedTarget.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);

                if (tryAsset || capturedTargetType == null)
                {
                    var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(capturedTarget);
                    if (prefabAsset != null)
                    {
                        var assetType = PrefabUtility.GetPrefabAssetType(prefabAsset);
                        return new JObject
                        {
                            ["prefabAssetPath"] = capturedTarget,
                            ["prefabAssetType"] = assetType.ToString(),
                            ["isInstance"] = false,
                            ["hasOverrides"] = false,
                            ["overrides"] = new JArray()
                        };
                    }

                    if (capturedTargetType == "asset")
                        throw new InvalidOperationException($"No prefab asset found at path: {capturedTarget}");
                }

                var go = GameObject.Find(capturedTarget);
                if (go == null)
                    throw new ArgumentException($"GameObject not found in scene: {capturedTarget}");

                bool isInstance = PrefabUtility.IsPartOfPrefabInstance(go);
                if (!isInstance)
                {
                    return new JObject
                    {
                        ["prefabAssetPath"] = (string)null,
                        ["prefabAssetType"] = "NotAPrefab",
                        ["isInstance"] = false,
                        ["hasOverrides"] = false,
                        ["overrides"] = new JArray()
                    };
                }

                var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                string assetPath = AssetDatabase.GetAssetPath(source);
                var prefabAssetType = PrefabUtility.GetPrefabAssetType(go);
                var mods = PrefabUtility.GetPropertyModifications(go);
                bool hasOverrides = mods != null && mods.Length > 0;

                var overridesArray = new JArray();
                if (mods != null)
                {
                    foreach (var mod in mods)
                    {
                        var entry = new JObject
                        {
                            ["propertyPath"] = mod.propertyPath,
                            ["instanceValue"] = mod.value
                        };

                        if (mod.target != null)
                        {
                            try
                            {
                                var so = new SerializedObject(mod.target);
                                var sp = so.FindProperty(mod.propertyPath);
                                entry["prefabValue"] = sp != null ? GetPropertyValueString(sp) : null;
                            }
                            catch
                            {
                                entry["prefabValue"] = null;
                            }
                        }
                        else
                        {
                            entry["prefabValue"] = null;
                        }

                        overridesArray.Add(entry);
                    }
                }

                return new JObject
                {
                    ["prefabAssetPath"] = assetPath,
                    ["prefabAssetType"] = prefabAssetType.ToString(),
                    ["isInstance"] = true,
                    ["hasOverrides"] = hasOverrides,
                    ["overrides"] = overridesArray
                };
            });

            return result;
        }

        private static string GetPropertyValueString(SerializedProperty sp)
        {
            switch (sp.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.ArraySize:
                    return sp.intValue.ToString();
                case SerializedPropertyType.Boolean:
                    return sp.boolValue.ToString();
                case SerializedPropertyType.Float:
                    return sp.floatValue.ToString("G");
                case SerializedPropertyType.String:
                    return sp.stringValue;
                case SerializedPropertyType.Vector2:
                    return $"[{sp.vector2Value.x},{sp.vector2Value.y}]";
                case SerializedPropertyType.Vector3:
                    return $"[{sp.vector3Value.x},{sp.vector3Value.y},{sp.vector3Value.z}]";
                case SerializedPropertyType.Vector4:
                    return $"[{sp.vector4Value.x},{sp.vector4Value.y},{sp.vector4Value.z},{sp.vector4Value.w}]";
                case SerializedPropertyType.Color:
                    return $"[{sp.colorValue.r},{sp.colorValue.g},{sp.colorValue.b},{sp.colorValue.a}]";
                case SerializedPropertyType.Quaternion:
                    return $"[{sp.quaternionValue.x},{sp.quaternionValue.y},{sp.quaternionValue.z},{sp.quaternionValue.w}]";
                case SerializedPropertyType.ObjectReference:
                    return sp.objectReferenceValue != null ? sp.objectReferenceValue.name : "null";
                default:
                    return sp.stringValue ?? string.Empty;
            }
        }
    }
}
