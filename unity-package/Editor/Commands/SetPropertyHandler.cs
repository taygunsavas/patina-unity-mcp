using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class SetPropertyHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string gameObjectName = parameters?["game_object_name"]?.Value<string>();
            string componentType = parameters?["component_type"]?.Value<string>();
            string propertyName = parameters?["property_name"]?.Value<string>();
            JToken valueToken = parameters?["value"];

            if (string.IsNullOrEmpty(gameObjectName))
                throw new ArgumentException("game_object_name is required");
            if (string.IsNullOrEmpty(componentType))
                throw new ArgumentException("component_type is required");
            if (string.IsNullOrEmpty(propertyName))
                throw new ArgumentException("property_name is required");

            string capturedGoName = gameObjectName;
            string capturedCompType = componentType;
            string capturedPropName = propertyName;
            string capturedValue = valueToken?.ToString() ?? "null";

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject go = GameObject.Find(capturedGoName);
                if (go == null)
                    throw new InvalidOperationException($"GameObject '{capturedGoName}' not found");

                Component comp = go.GetComponent(capturedCompType);
                if (comp == null)
                    throw new InvalidOperationException($"Component '{capturedCompType}' not found on '{capturedGoName}'");

                Undo.RecordObject(comp, $"Set {capturedPropName}");

                Type compTypeObj = comp.GetType();
                PropertyInfo prop = compTypeObj.GetProperty(capturedPropName, BindingFlags.Public | BindingFlags.Instance);

                if (prop != null && prop.CanWrite)
                {
                    object converted = ConvertValue(capturedValue, prop.PropertyType);
                    prop.SetValue(comp, converted);
                }
                else
                {
                    FieldInfo field = compTypeObj.GetField(capturedPropName, BindingFlags.Public | BindingFlags.Instance);
                    if (field == null)
                        throw new InvalidOperationException($"Property or field '{capturedPropName}' not found on '{capturedCompType}'");

                    object converted = ConvertValue(capturedValue, field.FieldType);
                    field.SetValue(comp, converted);
                }

                EditorUtility.SetDirty(comp);

                return new JObject
                {
                    ["gameObject"] = capturedGoName,
                    ["component"] = capturedCompType,
                    ["property"] = capturedPropName,
                    ["success"] = true
                };
            });

            return result;
        }

        private static object ConvertValue(string jsonValue, Type targetType)
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
                return new Color(arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>(), arr.Count > 3 ? arr[3].Value<float>() : 1f);
            }
            if (targetType == typeof(Quaternion))
            {
                JArray arr = JArray.Parse(jsonValue);
                return new Quaternion(arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>(), arr[3].Value<float>());
            }
            if (targetType == typeof(bool))
                return bool.Parse(jsonValue);
            if (targetType == typeof(int))
                return int.Parse(jsonValue, CultureInfo.InvariantCulture);
            if (targetType == typeof(float))
                return float.Parse(jsonValue, CultureInfo.InvariantCulture);
            if (targetType == typeof(double))
                return double.Parse(jsonValue, CultureInfo.InvariantCulture);
            if (targetType == typeof(string))
                return jsonValue.Trim('"');
            if (targetType.IsEnum)
                return Enum.Parse(targetType, jsonValue.Trim('"'), ignoreCase: true);

            return Convert.ChangeType(jsonValue, targetType, CultureInfo.InvariantCulture);
        }
    }
}
