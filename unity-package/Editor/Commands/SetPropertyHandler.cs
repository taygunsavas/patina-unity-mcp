using System;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
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
                    throw new InvalidOperationException(
                        $"Component '{capturedCompType}' not found on '{capturedGoName}'"
                    );

                Undo.RecordObject(comp, $"Set {capturedPropName}");

                Type compTypeObj = comp.GetType();
                PropertyInfo prop = compTypeObj.GetProperty(
                    capturedPropName,
                    BindingFlags.Public | BindingFlags.Instance
                );

                var refContext = new ObjectReferenceContext
                {
                    SearchRoot = go.transform.root.gameObject,
                    SelfAssetPath = null,
                    AllowSceneObjects = true,
                };

                if (prop != null && prop.CanWrite)
                {
                    object converted = ConvertValue(
                        capturedValue,
                        prop.PropertyType,
                        refContext,
                        capturedPropName
                    );
                    prop.SetValue(comp, converted);
                }
                else
                {
                    FieldInfo field = compTypeObj.GetField(
                        capturedPropName,
                        BindingFlags.Public | BindingFlags.Instance
                    );
                    if (field == null)
                        throw new InvalidOperationException(
                            $"Property or field '{capturedPropName}' not found on '{capturedCompType}'"
                        );

                    object converted = ConvertValue(
                        capturedValue,
                        field.FieldType,
                        refContext,
                        capturedPropName
                    );
                    field.SetValue(comp, converted);
                }

                EditorUtility.SetDirty(comp);

                return new JObject
                {
                    ["gameObject"] = capturedGoName,
                    ["component"] = capturedCompType,
                    ["property"] = capturedPropName,
                    ["success"] = true,
                };
            });

            return result;
        }

        private static object ConvertValue(
            JToken token,
            Type targetType,
            ObjectReferenceContext context,
            string propertyName
        )
        {
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
            {
                if (
                    !ObjectReferenceResolver.TryResolve(
                        token,
                        targetType,
                        context,
                        out UnityEngine.Object resolved,
                        out string error
                    )
                )
                    throw new ArgumentException($"Property '{propertyName}': {error}");
                return resolved;
            }

            if (token == null || token.Type == JTokenType.Null)
                throw new ArgumentException("value is required");

            if (targetType == typeof(Vector3))
            {
                JArray a = ParseAsArray(token, "Vector3");
                if (a.Count < 3)
                    throw new ArgumentException("Vector3 requires at least 3 elements");
                return new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>());
            }
            if (targetType == typeof(Vector2))
            {
                JArray a = ParseAsArray(token, "Vector2");
                if (a.Count < 2)
                    throw new ArgumentException("Vector2 requires at least 2 elements");
                return new Vector2(a[0].Value<float>(), a[1].Value<float>());
            }
            if (targetType == typeof(Color))
            {
                JArray a = ParseAsArray(token, "Color");
                if (a.Count < 3)
                    throw new ArgumentException("Color requires at least 3 elements");
                return new Color(
                    a[0].Value<float>(),
                    a[1].Value<float>(),
                    a[2].Value<float>(),
                    a.Count > 3 ? a[3].Value<float>() : 1f
                );
            }
            if (targetType == typeof(Quaternion))
            {
                JArray a = ParseAsArray(token, "Quaternion");
                if (a.Count < 4)
                    throw new ArgumentException("Quaternion requires at least 4 elements");
                return new Quaternion(
                    a[0].Value<float>(),
                    a[1].Value<float>(),
                    a[2].Value<float>(),
                    a[3].Value<float>()
                );
            }
            if (targetType == typeof(bool))
            {
                ObjectReferenceResolver.EnsureScalar(token, $"Property '{propertyName}'");
                return token.Value<bool>();
            }
            if (targetType == typeof(int))
            {
                ObjectReferenceResolver.EnsureScalar(token, $"Property '{propertyName}'");
                return token.Value<int>();
            }
            if (targetType == typeof(float))
            {
                ObjectReferenceResolver.EnsureScalar(token, $"Property '{propertyName}'");
                return token.Value<float>();
            }
            if (targetType == typeof(double))
            {
                ObjectReferenceResolver.EnsureScalar(token, $"Property '{propertyName}'");
                return token.Value<double>();
            }
            if (targetType == typeof(string))
            {
                ObjectReferenceResolver.EnsureScalar(token, $"Property '{propertyName}'");
                return token.Value<string>();
            }
            if (targetType.IsEnum)
            {
                ObjectReferenceResolver.EnsureScalar(token, $"Property '{propertyName}'");
                return Enum.Parse(targetType, token.Value<string>(), ignoreCase: true);
            }

            ObjectReferenceResolver.EnsureScalar(token, $"Property '{propertyName}'");
            return Convert.ChangeType(
                token.Value<string>(),
                targetType,
                CultureInfo.InvariantCulture
            );
        }

        private static JArray ParseAsArray(JToken token, string typeName)
        {
            if (token is JArray arr)
                return arr;
            if (token.Type == JTokenType.String)
            {
                try
                {
                    return JArray.Parse(token.Value<string>());
                }
                catch (Exception ex)
                {
                    throw new ArgumentException(
                        $"{typeName} value must be a JSON array or a JSON-array-encoded string: {ex.Message}"
                    );
                }
            }
            throw new ArgumentException($"{typeName} value must be an array");
        }
    }
}
