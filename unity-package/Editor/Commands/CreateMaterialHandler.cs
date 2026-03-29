using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class CreateMaterialHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string materialName = parameters?["material_name"]?.Value<string>();
            string savePath = parameters?["save_path"]?.Value<string>();
            string shaderName = parameters?["shader_name"]?.Value<string>() ?? "Universal Render Pipeline/Lit";

            if (string.IsNullOrEmpty(materialName))
                return Error("material_name is required");
            if (string.IsNullOrEmpty(savePath))
                return Error("save_path is required");

            string capturedName = materialName;
            string capturedPath = savePath;
            string capturedShader = shaderName;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                Shader shader = Shader.Find(capturedShader);
                if (shader == null)
                    return Error($"Shader not found: {capturedShader}");

                var material = new Material(shader);
                string assetPath = $"{capturedPath.TrimEnd('/')}/{capturedName}.mat";
                AssetDatabase.CreateAsset(material, assetPath);
                AssetDatabase.SaveAssets();

                return new JObject
                {
                    ["path"] = assetPath,
                    ["shader"] = capturedShader,
                    ["success"] = true
                };
            });
        }

        private static JObject Error(string message) =>
            new JObject { ["error"] = message, ["success"] = false };
    }
}
