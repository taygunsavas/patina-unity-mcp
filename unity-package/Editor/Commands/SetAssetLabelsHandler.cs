using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class SetAssetLabelsHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string assetPath = parameters?["asset_path"]?.Value<string>();
            JArray labelsToken = parameters?["labels"] as JArray;

            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("asset_path is required");
            if (labelsToken == null)
                throw new ArgumentException("labels is required");

            var labelList = new List<string>();
            foreach (var token in labelsToken)
                labelList.Add(token.Value<string>());

            string capturedPath = assetPath;
            string[] capturedLabels = labelList.ToArray();

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(capturedPath);
                if (asset == null)
                    throw new ArgumentException("Asset not found: " + capturedPath);

                AssetDatabase.StartAssetEditing();
                try
                {
                    AssetDatabase.SetLabels(asset, capturedLabels);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                var labelsArray = new JArray();
                foreach (var lbl in capturedLabels) labelsArray.Add(lbl);

                return new JObject
                {
                    ["path"] = capturedPath,
                    ["labels"] = labelsArray,
                    ["success"] = true
                };
            });
        }
    }
}
