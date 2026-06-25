using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class ResolveScriptTypeHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string typeName = parameters?["type_name"]?.Value<string>();
            if (string.IsNullOrEmpty(typeName))
                throw new ArgumentException("type_name is required");

            string capturedType = typeName;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                string[] guids = AssetDatabase.FindAssets("t:MonoScript");
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    if (script != null)
                    {
                        Type scriptClass = script.GetClass();
                        if (scriptClass != null && scriptClass.FullName == capturedType)
                        {
                            return new JObject
                            {
                                ["guid"] = guid,
                                ["assetPath"] = path,
                                ["found"] = true
                            };
                        }
                    }
                }

                return new JObject
                {
                    ["guid"] = string.Empty,
                    ["assetPath"] = string.Empty,
                    ["found"] = false
                };
            });
        }
    }
}
