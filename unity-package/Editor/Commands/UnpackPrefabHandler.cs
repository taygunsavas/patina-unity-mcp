using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class UnpackPrefabHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string name = parameters?["game_object_name"]?.Value<string>();
            int instanceId = parameters?["instance_id"]?.Value<int>() ?? 0;
            string mode = parameters?["mode"]?.Value<string>() ?? "outermost";

            if (string.IsNullOrEmpty(name) && instanceId == 0)
                throw new ArgumentException("game_object_name or instance_id is required");

            string capturedName = name ?? string.Empty;
            int capturedInstanceId = instanceId;
            string capturedMode = mode;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                var go = GameObjectFinder.Find(capturedName, capturedInstanceId);
                if (go == null)
                    throw new ArgumentException($"GameObject not found in scene: {capturedName}");

                if (!PrefabUtility.IsPartOfPrefabInstance(go))
                    throw new InvalidOperationException($"'{capturedName}' is not a prefab instance");

                PrefabUnpackMode unpackMode = string.Equals(capturedMode, "completely", StringComparison.OrdinalIgnoreCase)
                    ? PrefabUnpackMode.Completely
                    : PrefabUnpackMode.OutermostRoot;

                PrefabUtility.UnpackPrefabInstance(go, unpackMode, InteractionMode.UserAction);

                return new JObject
                {
                    ["gameObject"] = go.name,
                    ["mode"] = capturedMode,
                    ["success"] = true
                };
            });

            return result;
        }
    }
}
