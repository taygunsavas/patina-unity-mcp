using Newtonsoft.Json.Linq;
using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class GetScriptableObjectHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string assetPath = parameters?["asset_path"]?.Value<string>();
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("asset_path is required");

            string capturedPath = assetPath;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(capturedPath);
                if (so == null)
                    throw new InvalidOperationException($"ScriptableObject not found at: {capturedPath}");

                var type = so.GetType();
                var fields = new JObject();

                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null)
                        continue;
                    if (field.GetCustomAttribute<HideInInspector>() != null)
                        continue;

                    try
                    {
                        var value = field.GetValue(so);
                        fields[field.Name] = SerializeFieldValue(value);
                    }
                    catch
                    {
                        fields[field.Name] = "<error>";
                    }
                }

                return new JObject
                {
                    ["assetPath"] = capturedPath,
                    ["typeName"] = type.FullName,
                    ["fields"] = fields
                };
            });
        }

        private static JToken SerializeFieldValue(object value)
        {
            if (value == null) return JValue.CreateNull();
            switch (value)
            {
                case int i:    return i;
                case float f:  return f;
                case double d: return d;
                case bool b:   return b;
                case string s: return s;
                case long l:   return l;
                case Enum e:   return e.ToString();
                case Vector2 v2: return new JArray(v2.x, v2.y);
                case Vector3 v3: return new JArray(v3.x, v3.y, v3.z);
                case Vector4 v4: return new JArray(v4.x, v4.y, v4.z, v4.w);
                case Color c:  return new JArray(c.r, c.g, c.b, c.a);
                case UnityEngine.Object obj: return obj != null ? $"@{obj.GetType().Name}:{obj.name}" : null;
                default:
                    try { return JToken.FromObject(value); }
                    catch { return value.ToString(); }
            }
        }
    }
}
