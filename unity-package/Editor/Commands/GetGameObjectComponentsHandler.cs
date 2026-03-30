using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class GetGameObjectComponentsHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string gameObjectName = parameters?["game_object_name"]?.Value<string>();
            if (string.IsNullOrEmpty(gameObjectName))
                throw new ArgumentException("game_object_name is required");

            string capturedName = gameObjectName;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject go = GameObjectFinder.Find(capturedName);
                if (go == null)
                    throw new InvalidOperationException($"GameObject '{capturedName}' not found");

                var components = new JArray();
                foreach (Component comp in go.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    components.Add(new JObject
                    {
                        ["type"] = comp.GetType().Name,
                        ["instanceId"] = comp.GetInstanceID()
                    });
                }

                return new JObject
                {
                    ["name"] = go.name,
                    ["instanceId"] = go.GetInstanceID(),
                    ["components"] = components
                };
            });
        }
    }
}
