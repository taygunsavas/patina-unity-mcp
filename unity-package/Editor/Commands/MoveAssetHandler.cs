using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class MoveAssetHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string sourcePath = parameters?["source_path"]?.Value<string>();
            string destinationPath = parameters?["destination_path"]?.Value<string>();

            if (string.IsNullOrEmpty(sourcePath))
                throw new ArgumentException("source_path is required");
            if (string.IsNullOrEmpty(destinationPath))
                throw new ArgumentException("destination_path is required");

            string capturedSrc = sourcePath;
            string capturedDst = destinationPath;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                string error = AssetDatabase.MoveAsset(capturedSrc, capturedDst);
                if (!string.IsNullOrEmpty(error))
                    throw new InvalidOperationException(error);

                return new JObject
                {
                    ["sourcePath"] = capturedSrc,
                    ["destinationPath"] = capturedDst,
                    ["success"] = true
                };
            });
        }
    }
}
