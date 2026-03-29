using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class SetSelectionHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            var gameObjectNames = parameters?["game_object_names"]?.ToObject<List<string>>();
            var assetPaths = parameters?["asset_paths"]?.ToObject<List<string>>();

            return await MainThreadQueue.EnqueueAsync<JObject>(() =>
            {
                var objects = new List<Object>();

                if (gameObjectNames != null)
                {
                    foreach (var name in gameObjectNames)
                    {
                        var go = GameObject.Find(name);
                        if (go == null)
                            throw new System.ArgumentException($"GameObject not found: {name}");
                        objects.Add(go);
                    }
                }

                if (assetPaths != null)
                {
                    foreach (var path in assetPaths)
                    {
                        var asset = AssetDatabase.LoadMainAssetAtPath(path);
                        if (asset == null)
                            throw new System.ArgumentException($"Asset not found at path: {path}");
                        objects.Add(asset);
                    }
                }

                Selection.objects = objects.ToArray();

                return new JObject
                {
                    ["selectedCount"] = Selection.count,
                    ["success"] = true
                };
            });
        }
    }
}
