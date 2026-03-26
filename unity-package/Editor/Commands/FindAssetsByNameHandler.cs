using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class FindAssetsByNameHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string namePattern = parameters?["name_pattern"]?.Value<string>();
            string searchFolder = parameters?["search_folder"]?.Value<string>();
            int maxResults = 50;

            if (string.IsNullOrEmpty(namePattern))
                throw new ArgumentException("name_pattern is required");

            if (parameters != null && parameters.TryGetValue("max_results", out JToken maxToken) && maxToken.Type != JTokenType.Null)
                maxResults = maxToken.Value<int>();

            string capturedPattern = namePattern;
            string capturedFolder = searchFolder;
            int capturedMax = maxResults;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                string[] searchFolders = string.IsNullOrEmpty(capturedFolder)
                    ? null
                    : new[] { capturedFolder };

                string[] guids = searchFolders != null
                    ? AssetDatabase.FindAssets(capturedPattern, searchFolders)
                    : AssetDatabase.FindAssets(capturedPattern);

                var assets = new JArray();
                int count = Math.Min(guids.Length, capturedMax);
                for (int i = 0; i < count; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    assets.Add(new JObject
                    {
                        ["guid"] = guids[i],
                        ["path"] = path,
                        ["name"] = System.IO.Path.GetFileNameWithoutExtension(path)
                    });
                }

                return new JObject
                {
                    ["pattern"] = capturedPattern,
                    ["totalFound"] = guids.Length,
                    ["returned"] = count,
                    ["assets"] = assets
                };
            });

            return result;
        }
    }
}
