using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class RevertPrefabOverridesHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string name = parameters?["game_object_name"]?.Value<string>();

            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("game_object_name is required");

            string capturedName = name;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                var go = GameObject.Find(capturedName);
                if (go == null)
                    throw new ArgumentException($"GameObject not found in scene: {capturedName}");

                if (!PrefabUtility.IsPartOfPrefabInstance(go))
                    throw new InvalidOperationException($"'{capturedName}' is not a prefab instance");

                PrefabUtility.RevertPrefabInstance(go, InteractionMode.UserAction);

                return new JObject
                {
                    ["gameObject"] = capturedName,
                    ["success"] = true
                };
            });

            return result;
        }
    }
}
