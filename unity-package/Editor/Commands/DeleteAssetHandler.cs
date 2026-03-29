using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class DeleteAssetHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string assetPath = parameters?["asset_path"]?.Value<string>();

            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("asset_path is required");

            string capturedPath = assetPath;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(capturedPath)))
                    throw new ArgumentException("Asset not found: " + capturedPath);

                bool deleted = AssetDatabase.DeleteAsset(capturedPath);
                if (!deleted)
                    throw new InvalidOperationException("Failed to delete asset: " + capturedPath);

                return new JObject
                {
                    ["deletedPath"] = capturedPath,
                    ["success"] = true
                };
            });
        }
    }
}
