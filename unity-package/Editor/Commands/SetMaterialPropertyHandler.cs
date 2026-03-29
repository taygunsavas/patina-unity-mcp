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

            if (string.IsNullOrEmpty(materialPath))
                throw new System.ArgumentException("material_path is required");
            if (string.IsNullOrEmpty(propertyName))
                throw new System.ArgumentException("property_name is required");
            if (value == null)
                throw new System.ArgumentException("value is required");

            string capturedPath = materialPath;
            string capturedProp = propertyName;
            JToken capturedValue = value;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(capturedPath);
                if (material == null)
                    throw new System.InvalidOperationException($"Material not found at: {capturedPath}");

                if (!material.HasProperty(capturedProp))
                    throw new System.InvalidOperationException($"Property '{capturedProp}' not found on shader '{material.shader.name}'");

                var shader = material.shader;
                int propIndex = -1;
                for (int pi = 0; pi < ShaderUtil.GetPropertyCount(shader); pi++)
                {
                    if (ShaderUtil.GetPropertyName(shader, pi) == capturedProp)
                    {
                        propIndex = pi;
                        break;
                    }
                }
                if (propIndex < 0)
                    throw new System.InvalidOperationException($"Property '{capturedProp}' not found on shader '{shader.name}'");

                var propType = ShaderUtil.GetPropertyType(shader, propIndex);

                if (capturedValue.Type == JTokenType.Float || capturedValue.Type == JTokenType.Integer)
                {
                    if (propType != ShaderUtil.ShaderPropertyType.Float && propType != ShaderUtil.ShaderPropertyType.Range)
                        throw new System.ArgumentException($"Numeric value requires a Float or Range property, but '{capturedProp}' is '{propType}'");
                    material.SetFloat(capturedProp, capturedValue.Value<float>());
                }
                else if (capturedValue.Type == JTokenType.Boolean)
                {
                    if (propType != ShaderUtil.ShaderPropertyType.Float && propType != ShaderUtil.ShaderPropertyType.Range)
                        throw new System.ArgumentException($"Boolean value requires a Float or Range property, but '{capturedProp}' is '{propType}'");
                    material.SetFloat(capturedProp, capturedValue.Value<bool>() ? 1f : 0f);
                }
                else if (capturedValue.Type == JTokenType.Array)
                {
                    var arr = (JArray)capturedValue;
                    if (arr.Count < 3)
                        throw new System.ArgumentException("Array value must have at least 3 elements");

                    float Get(int i, float def = 0f) => i < arr.Count ? arr[i].Value<float>() : def;

                    if (propType == ShaderUtil.ShaderPropertyType.Color)
                    {
                        material.SetColor(capturedProp, new Color(Get(0), Get(1), Get(2), Get(3, 1f)));
                    }
                    else if (propType == ShaderUtil.ShaderPropertyType.Vector)
                    {
                        material.SetVector(capturedProp, new Vector4(Get(0), Get(1), Get(2), Get(3)));
                    }
                    else
                    {
                        throw new System.ArgumentException($"Array value is not supported for shader property type '{propType}'");
                    }
                }
                else if (capturedValue.Type == JTokenType.String)
                {
                    if (propType != ShaderUtil.ShaderPropertyType.TexEnv)
                        throw new System.ArgumentException($"String value requires a TexEnv property, but '{capturedProp}' is '{propType}'");
                    string texPath = capturedValue.Value<string>();
                    var texture = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                    if (texture == null)
                        throw new System.InvalidOperationException($"Texture not found at: {texPath}");
                    material.SetTexture(capturedProp, texture);
                }
                else
                {
                    throw new System.ArgumentException($"Unsupported value type: {capturedValue.Type}");
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
    }
}
