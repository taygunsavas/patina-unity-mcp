using Newtonsoft.Json.Linq;
using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class EditPrefabAssetHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string assetPath = parameters?["asset_path"]?.Value<string>();
            JArray actions = parameters?["actions"] as JArray;

            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("asset_path is required");
            if (actions == null || actions.Count == 0)
                throw new ArgumentException("actions array is required and cannot be empty");

            string capturedPath = assetPath;
            JArray capturedActions = actions;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject root = PrefabUtility.LoadPrefabContents(capturedPath);
                if (root == null)
                    throw new InvalidOperationException($"Failed to load prefab contents at: {capturedPath}");

                try
                {
                    foreach (JToken actToken in capturedActions)
                    {
                        JObject action = actToken as JObject;
                        if (action == null) continue;

                        string actionType = action["action_type"]?.Value<string>();
                        string transformPath = action["transform_path"]?.Value<string>();

                        GameObject targetGo = FindGameObjectByPath(root, transformPath);
                        if (targetGo == null)
                            throw new InvalidOperationException($"GameObject not found at path '{transformPath}' inside prefab");

                        switch (actionType)
                        {
                            case "add_component":
                                {
                                    string compType = action["component_type"]?.Value<string>();
                                    if (string.IsNullOrEmpty(compType))
                                        throw new ArgumentException("component_type is required for add_component");
                                    Type type = FindType(compType);
                                    if (type == null)
                                        throw new InvalidOperationException($"Component type '{compType}' not found");
                                    targetGo.AddComponent(type);
                                    break;
                                }
                            case "remove_component":
                                {
                                    string compType = action["component_type"]?.Value<string>();
                                    if (string.IsNullOrEmpty(compType))
                                        throw new ArgumentException("component_type is required for remove_component");
                                    Component comp = targetGo.GetComponent(compType);
                                    if (comp != null)
                                    {
                                        UnityEngine.Object.DestroyImmediate(comp, true);
                                    }
                                    break;
                                }
                            case "add_child":
                                {
                                    string childName = action["child_name"]?.Value<string>();
                                    if (string.IsNullOrEmpty(childName))
                                        throw new ArgumentException("child_name is required for add_child");
                                    GameObject child = new GameObject(childName);
                                    child.transform.SetParent(targetGo.transform, false);
                                    break;
                                }
                            case "remove_child":
                                {
                                    if (targetGo == root && (string.IsNullOrEmpty(transformPath) || transformPath == "/" || transformPath == "."))
                                        throw new InvalidOperationException("Cannot remove the root GameObject of a prefab");
                                    UnityEngine.Object.DestroyImmediate(targetGo, true);
                                    break;
                                }
                            case "set_field":
                                {
                                    string compType = action["component_type"]?.Value<string>();
                                    string fieldName = action["field_name"]?.Value<string>();
                                    JToken value = action["value"];

                                    if (string.IsNullOrEmpty(compType))
                                        throw new ArgumentException("component_type is required for set_field");
                                    if (string.IsNullOrEmpty(fieldName))
                                        throw new ArgumentException("field_name is required for set_field");
                                    if (value == null)
                                        throw new ArgumentException("value is required for set_field");

                                    Component comp = targetGo.GetComponent(compType);
                                    if (comp == null)
                                        throw new InvalidOperationException($"Component '{compType}' not found on path '{transformPath}'");

                                    var so = new SerializedObject(comp);
                                    var sp = so.FindProperty(fieldName);
                                    if (sp == null)
                                    {
                                        var fieldInfo = comp.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                        if (fieldInfo != null)
                                        {
                                            object converted = ConvertValue(value, fieldInfo.FieldType, fieldName);
                                            fieldInfo.SetValue(comp, converted);
                                            EditorUtility.SetDirty(comp);
                                        }
                                        else
                                        {
                                            throw new InvalidOperationException($"SerializedProperty or Field '{fieldName}' not found on '{compType}'");
                                        }
                                    }
                                    else
                                    {
                                        SetSerializedPropertyValue(sp, value);
                                        so.ApplyModifiedProperties();
                                    }
                                    break;
                                }
                            default:
                                throw new ArgumentException($"Unknown action_type: {actionType}");
                        }
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, capturedPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                AssetDatabase.ImportAsset(capturedPath, ImportAssetOptions.ForceUpdate);

                return new JObject
                {
                    ["success"] = true,
                    ["changedAssets"] = new JArray(capturedPath)
                };
            });
        }

        private static GameObject FindGameObjectByPath(GameObject root, string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/" || path == ".")
                return root;

            Transform current = root.transform;
            string[] parts = path.Split('/');
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                Transform child = current.Find(part);
                if (child == null)
                    return null;
                current = child;
            }
            return current.gameObject;
        }

        private static Type FindType(string typeName)
        {
            return AddComponentHandler.FindType(typeName);
        }

        private static object ConvertValue(JToken token, Type targetType, string fieldName)
        {
            if (token is JValue jv && jv.Type == JTokenType.String)
            {
                string s = jv.Value<string>();
                if (s != null && s.TrimStart().StartsWith("["))
                {
                    try { token = JArray.Parse(s); } catch { }
                }
            }

            if (targetType == typeof(int) || targetType == typeof(long)) return token.Value<int>();
            if (targetType == typeof(float)) return token.Value<float>();
            if (targetType == typeof(double)) return token.Value<double>();
            if (targetType == typeof(bool)) return token.Value<bool>();
            if (targetType == typeof(string)) return token.Value<string>();

            if (targetType == typeof(Vector2) && token is JArray a2 && a2.Count >= 2)
                return new Vector2(a2[0].Value<float>(), a2[1].Value<float>());

            if (targetType == typeof(Vector3) && token is JArray a3 && a3.Count >= 3)
                return new Vector3(a3[0].Value<float>(), a3[1].Value<float>(), a3[2].Value<float>());

            if (targetType == typeof(Vector4) && token is JArray a4 && a4.Count >= 4)
                return new Vector4(a4[0].Value<float>(), a4[1].Value<float>(), a4[2].Value<float>(), a4[3].Value<float>());

            if (targetType == typeof(Color) && token is JArray ac)
            {
                float r = ac.Count > 0 ? ac[0].Value<float>() : 0f;
                float g = ac.Count > 1 ? ac[1].Value<float>() : 0f;
                float b = ac.Count > 2 ? ac[2].Value<float>() : 0f;
                float a = ac.Count > 3 ? ac[3].Value<float>() : 1f;
                return new Color(r, g, b, a);
            }

            if (targetType.IsEnum)
            {
                string strVal = token.Value<string>();
                if (Enum.TryParse(targetType, strVal, ignoreCase: true, out var enumVal))
                    return enumVal;
                throw new ArgumentException($"'{strVal}' is not a valid value for enum type '{targetType.Name}'");
            }

            throw new ArgumentException($"Field '{fieldName}' has unsupported type '{targetType.Name}'");
        }

        private static void SetSerializedPropertyValue(SerializedProperty sp, JToken token)
        {
            switch (sp.propertyType)
            {
                case SerializedPropertyType.Integer:
                    sp.intValue = token.Value<int>();
                    break;
                case SerializedPropertyType.Boolean:
                    sp.boolValue = token.Value<bool>();
                    break;
                case SerializedPropertyType.Float:
                    sp.floatValue = token.Value<float>();
                    break;
                case SerializedPropertyType.String:
                    sp.stringValue = token.Value<string>();
                    break;
                case SerializedPropertyType.Color:
                    sp.colorValue = ParseColor(token);
                    break;
                case SerializedPropertyType.Vector2:
                    sp.vector2Value = ParseVector2(token);
                    break;
                case SerializedPropertyType.Vector3:
                    sp.vector3Value = ParseVector3(token);
                    break;
                case SerializedPropertyType.Vector4:
                    sp.vector4Value = ParseVector4(token);
                    break;
                case SerializedPropertyType.Quaternion:
                    sp.quaternionValue = ParseQuaternion(token);
                    break;
                case SerializedPropertyType.Enum:
                    if (token.Type == JTokenType.Integer)
                        sp.enumValueIndex = token.Value<int>();
                    else
                    {
                        string enumStr = token.Value<string>();
                        string[] names = sp.enumNames;
                        int index = Array.IndexOf(names, enumStr);
                        if (index >= 0)
                            sp.enumValueIndex = index;
                        else
                            throw new ArgumentException($"Enum value '{enumStr}' not found in property enum names");
                    }
                    break;
                case SerializedPropertyType.ObjectReference:
                    if (token.Type == JTokenType.Null)
                    {
                        sp.objectReferenceValue = null;
                    }
                    else
                    {
                        string valStr = token.Value<string>();
                        if (valStr.StartsWith("Assets/"))
                        {
                            sp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(valStr);
                        }
                        else
                        {
                            string path = AssetDatabase.GUIDToAssetPath(valStr);
                            if (!string.IsNullOrEmpty(path))
                                sp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                            else
                                throw new ArgumentException($"Could not resolve object reference from: '{valStr}'");
                        }
                    }
                    break;
                default:
                    throw new NotSupportedException($"SerializedPropertyType {sp.propertyType} is not supported");
            }
        }

        private static Color ParseColor(JToken token)
        {
            if (token is JArray arr)
            {
                float r = arr.Count > 0 ? arr[0].Value<float>() : 0f;
                float g = arr.Count > 1 ? arr[1].Value<float>() : 0f;
                float b = arr.Count > 2 ? arr[2].Value<float>() : 0f;
                float a = arr.Count > 3 ? arr[3].Value<float>() : 1f;
                return new Color(r, g, b, a);
            }
            throw new ArgumentException("Color must be a JSON array [r, g, b, a]");
        }

        private static Vector2 ParseVector2(JToken token)
        {
            if (token is JArray arr && arr.Count >= 2)
            {
                return new Vector2(arr[0].Value<float>(), arr[1].Value<float>());
            }
            throw new ArgumentException("Vector2 must be a JSON array [x, y]");
        }

        private static Vector3 ParseVector3(JToken token)
        {
            if (token is JArray arr && arr.Count >= 3)
            {
                return new Vector3(arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>());
            }
            throw new ArgumentException("Vector3 must be a JSON array [x, y, z]");
        }

        private static Vector4 ParseVector4(JToken token)
        {
            if (token is JArray arr && arr.Count >= 4)
            {
                return new Vector4(arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>(), arr[3].Value<float>());
            }
            throw new ArgumentException("Vector4 must be a JSON array [x, y, z, w]");
        }

        private static Quaternion ParseQuaternion(JToken token)
        {
            if (token is JArray arr && arr.Count >= 4)
            {
                return new Quaternion(arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>(), arr[3].Value<float>());
            }
            throw new ArgumentException("Quaternion must be a JSON array [x, y, z, w]");
        }
    }
}
