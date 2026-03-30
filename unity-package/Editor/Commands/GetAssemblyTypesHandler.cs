using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class GetAssemblyTypesHandler : ICommandHandler
    {
        public Task<object> HandleAsync(JObject parameters)
        {
            string assemblyName = parameters?["assembly_name"]?.Value<string>();
            if (string.IsNullOrEmpty(assemblyName))
                throw new ArgumentException("assembly_name is required");

            int maxResults = parameters?["max_results"]?.Value<int>() ?? 200;
            if (maxResults < 1) maxResults = 1;
            if (maxResults > 1000) maxResults = 1000;

            System.Reflection.Assembly target = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(asm.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    target = asm;
                    break;
                }
            }

            if (target == null)
                throw new InvalidOperationException($"Assembly '{assemblyName}' not found");

            Type[] types;
            try { types = target.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types; }

            var typeArray = new JArray();
            int count = 0;
            foreach (Type t in types)
            {
                if (t == null || !t.IsPublic) continue;
                if (count >= maxResults) break;

                bool isMono = typeof(MonoBehaviour).IsAssignableFrom(t);
                bool isSO = typeof(ScriptableObject).IsAssignableFrom(t);
                bool isEditor = t.Namespace != null && t.Namespace.Contains("Editor")
                    || t.FullName != null && t.FullName.Contains("Editor");

                typeArray.Add(new JObject
                {
                    ["name"] = t.Name,
                    ["fullName"] = t.FullName,
                    ["isMonoBehaviour"] = isMono,
                    ["isScriptableObject"] = isSO,
                    ["isEditor"] = isEditor
                });
                count++;
            }

            object result = new JObject
            {
                ["assemblyName"] = assemblyName,
                ["typeCount"] = count,
                ["types"] = typeArray
            };

            return Task.FromResult(result);
        }
    }
}
