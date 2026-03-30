using Newtonsoft.Json.Linq;
using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class SetScriptableObjectFieldHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string assetPath  = parameters?["asset_path"]?.Value<string>();
            string fieldName  = parameters?["field_name"]?.Value<string>();
            JToken value      = parameters?["value"];

            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("asset_path is required");
            if (string.IsNullOrEmpty(fieldName))
                throw new ArgumentException("field_name is required");
            if (value == null)
                throw new ArgumentException("value is required");

            string capturedPath  = assetPath;
            string capturedField = fieldName;
            JToken capturedValue = value;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(capturedPath);
                if (so == null)
                    throw new InvalidOperationException($"ScriptableObject not found at: {capturedPath}");

                var type = so.GetType();
                var field = type.GetField(capturedField,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (field == null)
                    throw new InvalidOperationException(
                        $"Field '{capturedField}' not found on type '{type.Name}'");

                if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null)
                    throw new InvalidOperationException(
                        $"Field '{capturedField}' is not serialized");

                object converted = ConvertValue(capturedValue, field.FieldType, capturedField);
                field.SetValue(so, converted);

                EditorUtility.SetDirty(so);
                AssetDatabase.SaveAssets();

                return new JObject
                {
                    ["assetPath"] = capturedPath,
                    ["field"] = capturedField,
                    ["success"] = true
                };
            });
        }

        private static object ConvertValue(JToken token, Type targetType, string fieldName)
        {
            if (targetType == typeof(int)    || targetType == typeof(long))   return token.Value<int>();
            if (targetType == typeof(float))                                   return token.Value<float>();
            if (targetType == typeof(double))                                  return token.Value<double>();
            if (targetType == typeof(bool))                                    return token.Value<bool>();
            if (targetType == typeof(string))                                  return token.Value<string>();

            if (targetType == typeof(Vector2) && token is JArray a2 && a2.Count >= 2)
                return new Vector2(a2[0].Value<float>(), a2[1].Value<float>());

            if (targetType == typeof(Vector3) && token is JArray a3 && a3.Count >= 3)
                return new Vector3(a3[0].Value<float>(), a3[1].Value<float>(), a3[2].Value<float>());

            if (targetType == typeof(Vector4) && token is JArray a4 && a4.Count >= 4)
                return new Vector4(a4[0].Value<float>(), a4[1].Value<float>(),
                                   a4[2].Value<float>(), a4[3].Value<float>());

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
                throw new ArgumentException(
                    $"'{strVal}' is not a valid value for enum type '{targetType.Name}'");
            }

            throw new ArgumentException(
                $"Field '{fieldName}' has unsupported type '{targetType.Name}'");
        }
    }
}
