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
                throw new System.ArgumentException("material_name is required");
            if (string.IsNullOrEmpty(savePath))
                throw new System.ArgumentException("save_path is required");

            string capturedName = materialName;
            string capturedPath = savePath;
            string capturedShader = shaderName;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                if (!AssetDatabase.IsValidFolder(capturedPath))
                    throw new System.ArgumentException($"save_path folder does not exist: {capturedPath}");

                Shader shader = Shader.Find(capturedShader);
                if (shader == null)
                    throw new System.InvalidOperationException($"Shader not found: {capturedShader}");

                string assetPath = $"{capturedPath.TrimEnd('/')}/{capturedName}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(assetPath) != null)
                    throw new System.InvalidOperationException($"Material already exists at: {assetPath}");

                var material = new Material(shader);
                AssetDatabase.CreateAsset(material, assetPath);
                AssetDatabase.SaveAssets();

                if (AssetDatabase.LoadAssetAtPath<Material>(assetPath) == null)
                    throw new System.InvalidOperationException($"AssetDatabase.CreateAsset failed for path: {assetPath}");

                return new JObject
                {
                    ["path"] = assetPath,
                    ["shader"] = capturedShader,
                    ["success"] = true
                };
            });
        }
    }
}
