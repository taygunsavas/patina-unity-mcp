using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
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
                        ApplyProperty(goName, compType, propName, valueToken);
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

        private static void ApplyProperty(string goName, string compType, string propName, JToken valueToken)
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
                prop.SetValue(comp, ConvertValue(valueToken, prop.PropertyType));
            }
            else
            {
                FieldInfo field = t.GetField(propName, BindingFlags.Public | BindingFlags.Instance);
                if (field == null) throw new InvalidOperationException($"Property/field '{propName}' not found on '{compType}'");
                field.SetValue(comp, ConvertValue(valueToken, field.FieldType));
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

        internal static object ConvertValue(JToken token, Type targetType)
        {
            if (token == null || token.Type == JTokenType.Null)
                throw new ArgumentException("value is required");

            if (targetType == typeof(Vector3))
            {
                JArray a = ParseAsArray(token, "Vector3");
                if (a.Count < 3) throw new ArgumentException("Vector3 requires at least 3 elements");
                return new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>());
            }
            if (targetType == typeof(Vector2))
            {
                JArray a = ParseAsArray(token, "Vector2");
                if (a.Count < 2) throw new ArgumentException("Vector2 requires at least 2 elements");
                return new Vector2(a[0].Value<float>(), a[1].Value<float>());
            }
            if (targetType == typeof(Color))
            {
                JArray a = ParseAsArray(token, "Color");
                if (a.Count < 3) throw new ArgumentException("Color requires at least 3 elements");
                return new Color(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>(),
                    a.Count > 3 ? a[3].Value<float>() : 1f);
            }
            if (targetType == typeof(Quaternion))
            {
                JArray a = ParseAsArray(token, "Quaternion");
                if (a.Count < 4) throw new ArgumentException("Quaternion requires at least 4 elements");
                return new Quaternion(a[0].Value<float>(), a[1].Value<float>(),
                    a[2].Value<float>(), a[3].Value<float>());
            }
            if (targetType == typeof(bool)) return token.Value<bool>();
            if (targetType == typeof(int)) return token.Value<int>();
            if (targetType == typeof(float)) return token.Value<float>();
            if (targetType == typeof(double)) return token.Value<double>();
            if (targetType == typeof(string)) return token.Value<string>();
            if (targetType.IsEnum) return Enum.Parse(targetType, token.Value<string>(), ignoreCase: true);
            return Convert.ChangeType(token.Value<string>(), targetType, CultureInfo.InvariantCulture);
        }

        private static JArray ParseAsArray(JToken token, string typeName)
        {
            if (token is JArray arr) return arr;
            if (token.Type == JTokenType.String)
            {
                try { return JArray.Parse(token.Value<string>()); }
                catch (Exception ex) { throw new ArgumentException($"{typeName} value must be a JSON array or a JSON-array-encoded string: {ex.Message}"); }
            }
            throw new ArgumentException($"{typeName} value must be an array");
        }
    }
}
