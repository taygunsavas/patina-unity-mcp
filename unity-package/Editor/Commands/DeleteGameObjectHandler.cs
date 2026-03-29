using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class DeleteGameObjectHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string gameObjectName = parameters?["game_object_name"]?.Value<string>();

            if (string.IsNullOrEmpty(gameObjectName))
                throw new ArgumentException("game_object_name is required");

            string capturedName = gameObjectName;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject go = GameObject.Find(capturedName);
                if (go == null)
                    throw new InvalidOperationException($"GameObject '{capturedName}' not found");

                string name = go.name;
                int instanceId = go.GetInstanceID();
                try
                {
                    Undo.DestroyObjectImmediate(go);
                }
                catch
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }

                return new JObject
                {
                    ["deletedObject"] = name,
                    ["instanceId"] = instanceId,
                    ["success"] = true
                };
            });

            return result;
        }
    }
}
