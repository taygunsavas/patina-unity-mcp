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
            JToken capturedValue = valueToken;

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

        private static object ConvertValue(JToken token, Type targetType)
        {
            if (targetType == typeof(Vector3) && token is JArray a3)
                return new Vector3(a3[0].Value<float>(), a3[1].Value<float>(), a3[2].Value<float>());
            if (targetType == typeof(Vector2) && token is JArray a2)
                return new Vector2(a2[0].Value<float>(), a2[1].Value<float>());
            if (targetType == typeof(Color) && token is JArray ac)
                return new Color(ac[0].Value<float>(), ac[1].Value<float>(), ac[2].Value<float>(), ac.Count > 3 ? ac[3].Value<float>() : 1f);
            if (targetType == typeof(Quaternion) && token is JArray aq)
                return new Quaternion(aq[0].Value<float>(), aq[1].Value<float>(), aq[2].Value<float>(), aq[3].Value<float>());
            if (targetType == typeof(bool))
                return token.Value<bool>();
            if (targetType == typeof(int))
                return token.Value<int>();
            if (targetType == typeof(float))
                return token.Value<float>();
            if (targetType == typeof(double))
                return token.Value<double>();
            if (targetType == typeof(string))
                return token.Value<string>();
            if (targetType.IsEnum)
                return Enum.Parse(targetType, token.Value<string>(), ignoreCase: true);

            return Convert.ChangeType(token.Value<string>(), targetType, CultureInfo.InvariantCulture);
        }
    }
}
