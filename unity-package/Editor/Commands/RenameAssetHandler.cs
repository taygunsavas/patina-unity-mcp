using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class RenameAssetHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string assetPath = parameters?["asset_path"]?.Value<string>();
            string newName = parameters?["new_name"]?.Value<string>();

            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("asset_path is required");
            if (string.IsNullOrEmpty(newName))
                throw new ArgumentException("new_name is required");

            string capturedPath = assetPath;
            string capturedName = newName;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                AssetDatabase.StartAssetEditing();
                string error;
                try
                {
                    error = AssetDatabase.RenameAsset(capturedPath, capturedName);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                if (!string.IsNullOrEmpty(error))
                    throw new InvalidOperationException(error);

                string dir = System.IO.Path.GetDirectoryName(capturedPath)?.Replace('\\', '/');
                string ext = System.IO.Path.GetExtension(capturedPath);
                string newPath = string.IsNullOrEmpty(dir)
                    ? capturedName + ext
                    : dir + "/" + capturedName + ext;

                return new JObject
                {
                    ["oldPath"] = capturedPath,
                    ["newPath"] = newPath,
                    ["success"] = true
                };
            });
        }
    }
}
