using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class CreateFolderHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string parentPath = parameters?["parent_path"]?.Value<string>();
            string folderName = parameters?["folder_name"]?.Value<string>();

            if (string.IsNullOrEmpty(parentPath))
                throw new ArgumentException("parent_path is required");
            if (string.IsNullOrEmpty(folderName))
                throw new ArgumentException("folder_name is required");

            string capturedParent = parentPath;
            string capturedName = folderName;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                string guid = AssetDatabase.CreateFolder(capturedParent, capturedName);
                string path = AssetDatabase.GUIDToAssetPath(guid);
                return new JObject
                {
                    ["path"] = path,
                    ["guid"] = guid,
                    ["success"] = true
                };
            });
        }
    }
}
