using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Patina.Editor.Commands
{
    public sealed class GetMaterialPropertiesHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string materialPath = parameters?["material_path"]?.Value<string>();
            if (string.IsNullOrEmpty(materialPath))
                throw new System.ArgumentException("material_path is required");

            string capturedPath = materialPath;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(capturedPath);
                if (material == null)
                    throw new System.InvalidOperationException(
                        $"Material not found at: {capturedPath}"
                    );

                var shader = material.shader;
                int propCount = shader.GetPropertyCount();
                var props = new JArray();

                for (int i = 0; i < propCount; i++)
                {
                    ShaderPropertyType propType = shader.GetPropertyType(i);
                    string propName = shader.GetPropertyName(i);
                    string typeName = propType.ToString();
                    JToken propValue;

                    switch (propType)
                    {
                        case ShaderPropertyType.Color:
                            var c = material.GetColor(propName);
                            propValue = new JArray(c.r, c.g, c.b, c.a);
                            break;
                        case ShaderPropertyType.Vector:
                            var v = material.GetVector(propName);
                            propValue = new JArray(v.x, v.y, v.z, v.w);
                            break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            propValue = material.GetFloat(propName);
                            break;
                        case ShaderPropertyType.Texture:
                            var tex = material.GetTexture(propName);
                            propValue =
                                tex != null
                                    ? (JToken)AssetDatabase.GetAssetPath(tex)
                                    : JValue.CreateNull();
                            typeName = "Texture";
                            break;
                        default:
                            propValue = JValue.CreateNull();
                            break;
                    }

                    props.Add(
                        new JObject
                        {
                            ["name"] = propName,
                            ["type"] = typeName,
                            ["value"] = propValue,
                        }
                    );
                }

                return new JObject
                {
                    ["materialPath"] = capturedPath,
                    ["shader"] = shader.name,
                    ["properties"] = props,
                };
            });
        }
    }
}
