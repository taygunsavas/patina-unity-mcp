using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class AssignMaterialHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string gameObjectName = parameters?["game_object_name"]?.Value<string>();
            string materialPath = parameters?["material_path"]?.Value<string>();
            int materialIndex = parameters?["material_index"]?.Value<int>() ?? 0;

            if (string.IsNullOrEmpty(gameObjectName))
                throw new System.ArgumentException("game_object_name is required");
            if (string.IsNullOrEmpty(materialPath))
                throw new System.ArgumentException("material_path is required");
            if (materialIndex < 0)
                throw new System.ArgumentException("material_index must be >= 0");

            string capturedGoName = gameObjectName;
            string capturedMatPath = materialPath;
            int capturedIndex = materialIndex;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                var go = GameObject.Find(capturedGoName);
                if (go == null)
                    throw new System.InvalidOperationException($"GameObject not found: {capturedGoName}");

                var renderer = go.GetComponent<Renderer>();
                if (renderer == null)
                    throw new System.InvalidOperationException($"GameObject '{capturedGoName}' has no Renderer component");

                var material = AssetDatabase.LoadAssetAtPath<Material>(capturedMatPath);
                if (material == null)
                    throw new System.InvalidOperationException($"Material not found at: {capturedMatPath}");

                Undo.RecordObject(renderer, "Assign Material");

                var mats = renderer.sharedMaterials;
                if (capturedIndex >= mats.Length)
                {
                    var expanded = new Material[capturedIndex + 1];
                    mats.CopyTo(expanded, 0);
                    mats = expanded;
                }
                mats[capturedIndex] = material;
                renderer.sharedMaterials = mats;

                EditorUtility.SetDirty(renderer);

                return new JObject
                {
                    ["gameObject"] = capturedGoName,
                    ["materialPath"] = capturedMatPath,
                    ["materialIndex"] = capturedIndex,
                    ["success"] = true
                };
            });
        }
    }
}
