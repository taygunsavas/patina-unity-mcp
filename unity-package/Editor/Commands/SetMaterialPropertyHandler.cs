using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

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
                    throw new System.InvalidOperationException(
                        $"Material not found at: {capturedPath}"
                    );

                if (!material.HasProperty(capturedProp))
                    throw new System.InvalidOperationException(
                        $"Property '{capturedProp}' not found on shader '{material.shader.name}'"
                    );

                var shader = material.shader;
                int propIndex = -1;
                for (int pi = 0; pi < shader.GetPropertyCount(); pi++)
                {
                    if (shader.GetPropertyName(pi) == capturedProp)
                    {
                        propIndex = pi;
                        break;
                    }
                }
                if (propIndex < 0)
                    throw new System.InvalidOperationException(
                        $"Property '{capturedProp}' not found on shader '{shader.name}'"
                    );

                ShaderPropertyType propType = shader.GetPropertyType(propIndex);

                if (
                    capturedValue.Type == JTokenType.Float
                    || capturedValue.Type == JTokenType.Integer
                )
                {
                    if (
                        propType != ShaderPropertyType.Float
                        && propType != ShaderPropertyType.Range
                    )
                        throw new System.ArgumentException(
                            $"Numeric value requires a Float or Range property, but '{capturedProp}' is '{propType}'"
                        );
                    material.SetFloat(capturedProp, capturedValue.Value<float>());
                }
                else if (capturedValue.Type == JTokenType.Boolean)
                {
                    if (
                        propType != ShaderPropertyType.Float
                        && propType != ShaderPropertyType.Range
                    )
                        throw new System.ArgumentException(
                            $"Boolean value requires a Float or Range property, but '{capturedProp}' is '{propType}'"
                        );
                    material.SetFloat(capturedProp, capturedValue.Value<bool>() ? 1f : 0f);
                }
                else if (capturedValue.Type == JTokenType.Array)
                {
                    ApplyArrayValue(material, capturedProp, propType, (JArray)capturedValue);
                }
                else if (capturedValue.Type == JTokenType.String)
                {
                    string strVal = capturedValue.Value<string>();

                    // Agent may have stringified the JSON value (e.g. "[0.2,0.8,0.4,1.0]" or "0.5").
                    // Try to parse and re-dispatch before treating as texture path.
                    JToken parsed = null;
                    try
                    {
                        parsed = JToken.Parse(strVal);
                    }
                    catch { }

                    if (parsed != null && parsed.Type != JTokenType.String)
                    {
                        // Re-dispatch with the correctly-typed token
                        if (parsed.Type == JTokenType.Float || parsed.Type == JTokenType.Integer)
                        {
                            if (
                                propType != ShaderPropertyType.Float
                                && propType != ShaderPropertyType.Range
                            )
                                throw new System.ArgumentException(
                                    $"Numeric value requires a Float or Range property, but '{capturedProp}' is '{propType}'"
                                );
                            material.SetFloat(capturedProp, parsed.Value<float>());
                        }
                        else if (parsed.Type == JTokenType.Array)
                        {
                            ApplyArrayValue(material, capturedProp, propType, (JArray)parsed);
                        }
                        else
                        {
                            throw new System.ArgumentException(
                                $"Unsupported parsed value type: {parsed.Type}"
                            );
                        }
                    }
                    else if (propType == ShaderPropertyType.Texture)
                    {
                        var texture = AssetDatabase.LoadAssetAtPath<Texture>(strVal);
                        if (texture == null)
                            throw new System.InvalidOperationException(
                                $"Texture not found at: {strVal}"
                            );
                        material.SetTexture(capturedProp, texture);
                    }
                    else
                    {
                        throw new System.ArgumentException(
                            $"String value requires a TexEnv property, but '{capturedProp}' is '{propType}'"
                        );
                    }
                }
                else
                {
                    throw new System.ArgumentException(
                        $"Unsupported value type: {capturedValue.Type}"
                    );
                }

                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();

                return new JObject
                {
                    ["materialPath"] = capturedPath,
                    ["property"] = capturedProp,
                    ["success"] = true,
                };
            });
        }

        private static void ApplyArrayValue(
            Material material,
            string prop,
            ShaderPropertyType propType,
            JArray arr
        )
        {
            if (arr.Count < 3)
                throw new System.ArgumentException("Array value must have at least 3 elements");

            float Get(int i, float def = 0f) => i < arr.Count ? arr[i].Value<float>() : def;

            if (propType == ShaderPropertyType.Color)
            {
                material.SetColor(prop, new Color(Get(0), Get(1), Get(2), Get(3, 1f)));
            }
            else if (propType == ShaderPropertyType.Vector)
            {
                material.SetVector(prop, new Vector4(Get(0), Get(1), Get(2), Get(3)));
            }
            else
            {
                throw new System.ArgumentException(
                    $"Array value is not supported for shader property type '{propType}'"
                );
            }
        }
    }
}
