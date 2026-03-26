using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class AddComponentHandler : ICommandHandler
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

                Type type = FindType(capturedType);
                if (type == null)
                    throw new InvalidOperationException($"Component type '{capturedType}' not found");

                Component comp = Undo.AddComponent(go, type);
                EditorUtility.SetDirty(go);

                return new JObject
                {
                    ["gameObject"] = go.name,
                    ["component"] = comp.GetType().Name,
                    ["instanceId"] = comp.GetInstanceID()
                };
            });

            return result;
        }

        private static Type FindType(string typeName)
        {
            Type type = Type.GetType(typeName);
            if (type != null) return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null) return type;
            }

            string[] prefixes = { "UnityEngine.", "UnityEditor." };
            foreach (string prefix in prefixes)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(prefix + typeName);
                    if (type != null) return type;
                }
            }

            return null;
        }
    }
}
