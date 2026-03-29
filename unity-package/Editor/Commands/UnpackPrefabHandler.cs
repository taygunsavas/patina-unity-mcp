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
            string mode = parameters?["mode"]?.Value<string>() ?? "outermost";

            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("game_object_name is required");

            string capturedName = name;
            string capturedMode = mode;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                var go = GameObject.Find(capturedName);
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
                    ["gameObject"] = capturedName,
                    ["mode"] = capturedMode,
                    ["success"] = true
                };
            });

            return result;
        }
    }
}
