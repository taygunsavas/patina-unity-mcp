using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class RemoveComponentHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string gameObjectName = parameters?["game_object_name"]?.Value<string>();
            string componentType = parameters?["component_type"]?.Value<string>();

            if (string.IsNullOrEmpty(gameObjectName))
                throw new ArgumentException("game_object_name is required");
            if (string.IsNullOrEmpty(componentType))
                throw new ArgumentException("component_type is required");

            string capturedName = gameObjectName;
            string capturedType = componentType;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject go = GameObject.Find(capturedName);
                if (go == null)
                    throw new InvalidOperationException($"GameObject '{capturedName}' not found");

                Component comp = go.GetComponent(capturedType);
                if (comp == null)
                    throw new InvalidOperationException($"Component '{capturedType}' not found on '{capturedName}'");

                string typeName = comp.GetType().Name;
                Undo.DestroyObjectImmediate(comp);
                EditorUtility.SetDirty(go);

                return new JObject
                {
                    ["gameObject"] = capturedName,
                    ["removedComponent"] = typeName,
                    ["success"] = true
                };
            });

            return result;
        }
    }
}
