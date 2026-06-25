using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class OpenPrefabStageHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string assetPath = parameters?["asset_path"]?.Value<string>();
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("asset_path is required");

            string capturedPath = assetPath;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(capturedPath);
                if (prefab == null)
                    throw new InvalidOperationException($"Prefab asset not found at: {capturedPath}");

                bool success = AssetDatabase.OpenAsset(prefab);
                return new JObject
                {
                    ["success"] = success,
                    ["stagePath"] = capturedPath
                };
            });
        }
    }
}
