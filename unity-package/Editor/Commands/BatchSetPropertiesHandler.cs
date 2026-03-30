using Newtonsoft.Json.Linq;
using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class BatchSetPropertiesHandler : ICommandHandler
    {
        private const int MaxOperations = 100;

        public async Task<object> HandleAsync(JObject parameters)
        {
            var operationsToken = parameters?["operations"] as JArray;
            if (operationsToken == null || operationsToken.Count == 0)
                throw new ArgumentException("operations array is required");
            if (operationsToken.Count > MaxOperations)
                throw new ArgumentException($"operations exceeds max of {MaxOperations}");

            string undoLabel = parameters?["undo_label"]?.Value<string>() ?? "Patina Batch SetProperties";
            var ops = operationsToken;
            string capturedLabel = undoLabel;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                Undo.SetCurrentGroupName(capturedLabel);
                int groupIndex = Undo.GetCurrentGroup();

                var results = new JArray();
                foreach (JToken opToken in ops)
                {
                    string goName = opToken["game_object_name"]?.Value<string>();
                    string compType = opToken["component_type"]?.Value<string>();
                    string propName = opToken["property_name"]?.Value<string>();
                    JToken valueToken = opToken["value"];

                    if (string.IsNullOrEmpty(goName) || string.IsNullOrEmpty(compType) || string.IsNullOrEmpty(propName))
                    {
                        results.Add(ItemError(goName, compType, propName, "game_object_name, component_type, and property_name are required"));
                        continue;
                    }

                    try
                    {
                        ApplyProperty(goName, compType, propName, valueToken?.ToString() ?? "null");
                        results.Add(new JObject
                        {
                            ["gameObject"] = goName,
                            ["component"] = compType,
                            ["property"] = propName,
                            ["success"] = true
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(ItemError(goName, compType, propName, ex.Message));
                    }
                }

                Undo.CollapseUndoOperations(groupIndex);

                return new JObject
                {
                    ["success"] = true,
                    ["results"] = results
                };
            });
        }

        private static void ApplyProperty(string goName, string compType, string propName, string jsonValue)
        {
            GameObject go = GameObjectFinder.Find(goName);
            if (go == null) throw new InvalidOperationException($"GameObject '{goName}' not found");

            Component comp = go.GetComponent(compType);
            if (comp == null) throw new InvalidOperationException($"Component '{compType}' not found on '{goName}'");

            Undo.RecordObject(comp, $"Set {propName}");

            Type t = comp.GetType();
            PropertyInfo prop = t.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(comp, ConvertValue(jsonValue, prop.PropertyType));
            }
            else
            {
                FieldInfo field = t.GetField(propName, BindingFlags.Public | BindingFlags.Instance);
                if (field == null) throw new InvalidOperationException($"Property/field '{propName}' not found on '{compType}'");
                field.SetValue(comp, ConvertValue(jsonValue, field.FieldType));
            }

            EditorUtility.SetDirty(comp);
        }

        private static JObject ItemError(string go, string comp, string prop, string msg) =>
            new JObject
            {
                ["gameObject"] = go ?? string.Empty,
                ["component"] = comp ?? string.Empty,
                ["property"] = prop ?? string.Empty,
                ["success"] = false,
                ["error"] = msg
            };

        internal static object ConvertValue(string jsonValue, Type targetType)
        {
            if (targetType == typeof(Vector3))
            {
                JArray arr = JArray.Parse(jsonValue);
                return new Vector3(arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>());
            }
            if (targetType == typeof(Vector2))
            {
                JArray arr = JArray.Parse(jsonValue);
                return new Vector2(arr[0].Value<float>(), arr[1].Value<float>());
            }
            if (targetType == typeof(Color))
            {
                JArray arr = JArray.Parse(jsonValue);
                return new Color(arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>(),
                    arr.Count > 3 ? arr[3].Value<float>() : 1f);
            }
            if (targetType == typeof(Quaternion))
            {
                JArray arr = JArray.Parse(jsonValue);
                return new Quaternion(arr[0].Value<float>(), arr[1].Value<float>(),
                    arr[2].Value<float>(), arr[3].Value<float>());
            }
            if (targetType == typeof(bool)) return bool.Parse(jsonValue);
            if (targetType == typeof(int)) return int.Parse(jsonValue);
            if (targetType == typeof(float)) return float.Parse(jsonValue);
            if (targetType == typeof(string)) return jsonValue.Trim('"');
            return Convert.ChangeType(jsonValue, targetType);
        }
    }
}
