using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class ApplyPrefabOverridesHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string name = parameters?["game_object_name"]?.Value<string>();
            int instanceId = parameters?["instance_id"]?.Value<int>() ?? 0;

            if (string.IsNullOrEmpty(name) && instanceId == 0)
                throw new ArgumentException("game_object_name or instance_id is required");

            string capturedName = name ?? string.Empty;
            int capturedInstanceId = instanceId;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                var go = GameObjectFinder.Find(capturedName, capturedInstanceId);
                if (go == null)
                    throw new ArgumentException($"GameObject not found in scene: {capturedName}");

                if (!PrefabUtility.IsPartOfPrefabInstance(go))
                    throw new InvalidOperationException($"'{capturedName}' is not a prefab instance");

                var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                string appliedToPath = AssetDatabase.GetAssetPath(source);

                PrefabUtility.ApplyPrefabInstance(go, InteractionMode.UserAction);

                return new JObject
                {
                    ["gameObject"] = go.name,
                    ["appliedToPath"] = appliedToPath,
                    ["success"] = true
                };
            });

            return result;
        }
    }
}
