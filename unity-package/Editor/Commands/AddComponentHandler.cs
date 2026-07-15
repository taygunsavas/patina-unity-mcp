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

        internal static Type FindType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;

            Type type = Type.GetType(typeName);
            if (type != null) return type;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                type = assembly.GetType(typeName);
                if (type != null) return type;
            }

            string[] prefixes = { "UnityEngine.", "UnityEditor." };
            foreach (string prefix in prefixes)
            {
                foreach (var assembly in assemblies)
                {
                    type = assembly.GetType(prefix + typeName);
                    if (type != null) return type;
                }
            }

            bool isQualified = typeName.Contains(".") || typeName.Contains("+");

            foreach (var assembly in assemblies)
            {
                Type[] types = null;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch (Exception)
                {
                    // Ignore other exceptions and continue search
                }

                if (types == null) continue;

                foreach (var t in types)
                {
                    if (t == null) continue;

                    if (isQualified)
                    {
                        if (t.FullName == typeName || (t.FullName != null && (t.FullName.EndsWith("." + typeName) || t.FullName.EndsWith("+" + typeName))))
                        {
                            return t;
                        }
                    }
                    else
                    {
                        if (t.Name == typeName)
                        {
                            return t;
                        }
                    }
                }
            }

            return null;
        }
    }
}
