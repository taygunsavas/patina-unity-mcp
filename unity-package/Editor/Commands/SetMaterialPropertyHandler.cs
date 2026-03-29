using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class SetMaterialPropertyHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string materialPath = parameters?["material_path"]?.Value<string>();
            string propertyName = parameters?["property_name"]?.Value<string>();
            JToken value = parameters?["value"];

            if (string.IsNullOrEmpty(materialPath)) return Error("material_path is required");
            if (string.IsNullOrEmpty(propertyName)) return Error("property_name is required");
            if (value == null) return Error("value is required");

            string capturedPath = materialPath;
            string capturedProp = propertyName;
            JToken capturedValue = value;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(capturedPath);
                if (material == null)
                    return Error($"Material not found at: {capturedPath}");

                if (!material.HasProperty(capturedProp))
                    return Error($"Property '{capturedProp}' not found on shader '{material.shader.name}'");

                if (capturedValue.Type == JTokenType.Float || capturedValue.Type == JTokenType.Integer)
                {
                    material.SetFloat(capturedProp, capturedValue.Value<float>());
                }
                else if (capturedValue.Type == JTokenType.Boolean)
                {
                    material.SetFloat(capturedProp, capturedValue.Value<bool>() ? 1f : 0f);
                }
                else if (capturedValue.Type == JTokenType.Array)
                {
                    var arr = (JArray)capturedValue;
                    if (arr.Count >= 4)
                    {
                        material.SetColor(capturedProp, new Color(
                            arr[0].Value<float>(), arr[1].Value<float>(),
                            arr[2].Value<float>(), arr[3].Value<float>()));
                    }
                    else if (arr.Count >= 3)
                    {
                        material.SetVector(capturedProp, new Vector4(
                            arr[0].Value<float>(), arr[1].Value<float>(),
                            arr[2].Value<float>(), 0f));
                    }
                    else
                    {
                        return Error("Array value must have at least 3 elements");
                    }
                }
                else if (capturedValue.Type == JTokenType.String)
                {
                    string texPath = capturedValue.Value<string>();
                    var texture = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                    if (texture == null)
                        return Error($"Texture not found at: {texPath}");
                    material.SetTexture(capturedProp, texture);
                }
                else
                {
                    return Error($"Unsupported value type: {capturedValue.Type}");
                }

                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();

                return new JObject
                {
                    ["materialPath"] = capturedPath,
                    ["property"] = capturedProp,
                    ["success"] = true
                };
            });
        }

        private static JObject Error(string message) =>
            new JObject { ["error"] = message, ["success"] = false };
    }
}
