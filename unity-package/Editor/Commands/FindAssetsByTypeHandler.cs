using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class FindAssetsByTypeHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string typeFilter = parameters?["type_filter"]?.Value<string>();
            string searchFolder = parameters?["search_folder"]?.Value<string>();
            int maxResults = 50;

            if (string.IsNullOrEmpty(typeFilter))
                throw new ArgumentException("type_filter is required");

            if (parameters != null && parameters.TryGetValue("max_results", out JToken maxToken) && maxToken.Type != JTokenType.Null)
                maxResults = maxToken.Value<int>();

            string capturedFilter = typeFilter;
            string capturedFolder = searchFolder;
            int capturedMax = maxResults;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                string[] searchFolders = string.IsNullOrEmpty(capturedFolder)
                    ? null
                    : new[] { capturedFolder };

                string[] guids = searchFolders != null
                    ? AssetDatabase.FindAssets(capturedFilter, searchFolders)
                    : AssetDatabase.FindAssets(capturedFilter);

                var assets = new JArray();
                int count = Math.Min(guids.Length, capturedMax);
                for (int i = 0; i < count; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    assets.Add(new JObject
                    {
                        ["guid"] = guids[i],
                        ["path"] = path
                    });
                }

                return new JObject
                {
                    ["filter"] = capturedFilter,
                    ["totalFound"] = guids.Length,
                    ["returned"] = count,
                    ["assets"] = assets
                };
            });

            return result;
        }
    }
}
