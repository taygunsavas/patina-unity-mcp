using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

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
                    throw new System.InvalidOperationException($"Material not found at: {capturedPath}");

                var shader = material.shader;
                int propCount = ShaderUtil.GetPropertyCount(shader);
                var props = new JArray();

                for (int i = 0; i < propCount; i++)
                {
                    var propType = ShaderUtil.GetPropertyType(shader, i);
                    string propName = ShaderUtil.GetPropertyName(shader, i);
                    string typeName = propType.ToString();
                    JToken propValue;

                    switch (propType)
                    {
                        case ShaderUtil.ShaderPropertyType.Color:
                            var c = material.GetColor(propName);
                            propValue = new JArray(c.r, c.g, c.b, c.a);
                            break;
                        case ShaderUtil.ShaderPropertyType.Vector:
                            var v = material.GetVector(propName);
                            propValue = new JArray(v.x, v.y, v.z, v.w);
                            break;
                        case ShaderUtil.ShaderPropertyType.Float:
                        case ShaderUtil.ShaderPropertyType.Range:
                            propValue = material.GetFloat(propName);
                            break;
                        case ShaderUtil.ShaderPropertyType.TexEnv:
                            var tex = material.GetTexture(propName);
                            propValue = tex != null ? (JToken)AssetDatabase.GetAssetPath(tex) : JValue.CreateNull();
                            typeName = "Texture";
                            break;
                        default:
                            propValue = JValue.CreateNull();
                            break;
                    }

                    props.Add(new JObject
                    {
                        ["name"] = propName,
                        ["type"] = typeName,
                        ["value"] = propValue
                    });
                }

                return new JObject
                {
                    ["materialPath"] = capturedPath,
                    ["shader"] = shader.name,
                    ["properties"] = props
                };
            });
        }
    }
}
