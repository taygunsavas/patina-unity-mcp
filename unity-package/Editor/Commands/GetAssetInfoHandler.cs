using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class GetAssetInfoHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string assetPath = parameters?["asset_path"]?.Value<string>();
            bool includeImporterSettings = parameters?["include_importer_settings"]?.Value<bool?>() ?? false;

            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("asset_path is required");

            string capturedPath = assetPath;
            bool capturedIncludeImporter = includeImporterSettings;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                string guid = AssetDatabase.AssetPathToGUID(capturedPath);
                if (string.IsNullOrEmpty(guid))
                    throw new ArgumentException("Asset not found: " + capturedPath);

                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(capturedPath);
                string assetType = asset != null ? asset.GetType().Name : "Unknown";

                long fileSize = 0;
                string fullPath = Path.GetFullPath(capturedPath);
                if (File.Exists(fullPath))
                    fileSize = new FileInfo(fullPath).Length;

                string[] labels = asset != null ? AssetDatabase.GetLabels(asset) : System.Array.Empty<string>();

                AssetImporter importer = AssetImporter.GetAtPath(capturedPath);
                string importerType = importer != null ? importer.GetType().Name : null;

                JObject importerSettings = null;
                if (capturedIncludeImporter && importer != null)
                {
                    importerSettings = new JObject();
                    var so = new UnityEditor.SerializedObject(importer);
                    var prop = so.GetIterator();
                    if (prop.NextVisible(true))
                    {
                        do
                        {
                            if (prop.depth > 2) continue;
                            if (prop.name == "m_ObjectHideFlags") continue;
                            try
                            {
                                JToken value;
                                switch (prop.propertyType)
                                {
                                    case UnityEditor.SerializedPropertyType.Integer:
                                    case UnityEditor.SerializedPropertyType.Enum:
                                        value = prop.intValue; break;
                                    case UnityEditor.SerializedPropertyType.Boolean:
                                        value = prop.boolValue; break;
                                    case UnityEditor.SerializedPropertyType.Float:
                                        value = prop.floatValue; break;
                                    case UnityEditor.SerializedPropertyType.String:
                                        value = prop.stringValue; break;
                                    default:
                                        continue;
                                }
                                importerSettings[prop.name] = value;
                            }
                            catch { }
                        } while (prop.NextVisible(false));
                    }
                }

                var labelsArray = new JArray();
                foreach (var lbl in labels) labelsArray.Add(lbl);

                var result = new JObject
                {
                    ["path"] = capturedPath,
                    ["guid"] = guid,
                    ["assetType"] = assetType,
                    ["fileSize"] = fileSize,
                    ["labels"] = labelsArray,
                    ["importer"] = importerType
                };
                if (capturedIncludeImporter)
                    result["importerSettings"] = importerSettings;
                return result;
            });
        }
    }
}
