using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class GetSelectionHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            return await MainThreadQueue.EnqueueAsync<JObject>(() =>
            {
                var gameObjects = new JArray();
                foreach (var go in Selection.gameObjects)
                {
                    gameObjects.Add(new JObject
                    {
                        ["name"] = go.name,
                        ["instanceId"] = go.GetInstanceID()
                    });
                }

                var assetPaths = new JArray();
                foreach (var guid in Selection.assetGUIDs)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path))
                        assetPaths.Add(path);
                }

                return new JObject
                {
                    ["gameObjects"] = gameObjects,
                    ["assetPaths"] = assetPaths,
                    ["count"] = Selection.count
                };
            });
        }
    }
}
