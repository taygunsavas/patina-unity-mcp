using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class BatchAddComponentsHandler : ICommandHandler
    {
        private const int MaxOperations = 100;

        public async Task<object> HandleAsync(JObject parameters)
        {
            var operationsToken = parameters?["operations"] as JArray;
            if (operationsToken == null || operationsToken.Count == 0)
                throw new ArgumentException("operations array is required");
            if (operationsToken.Count > MaxOperations)
                throw new ArgumentException($"operations exceeds max of {MaxOperations}");

            string undoLabel = parameters?["undo_label"]?.Value<string>() ?? "Patina Batch AddComponents";
            var ops = operationsToken;
            string capturedLabel = undoLabel;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                Undo.SetCurrentGroupName(capturedLabel);
                int groupIndex = Undo.GetCurrentGroup();

                var results = new JArray();
                foreach (JToken opToken in ops)
                {
                    string goName = opToken["game_object_name"]?.Value<string>();
                    string compType = opToken["component_type"]?.Value<string>();

                    if (string.IsNullOrEmpty(goName) || string.IsNullOrEmpty(compType))
                    {
                        results.Add(ItemError(goName, compType, "game_object_name and component_type are required"));
                        continue;
                    }

                    try
                    {
                        GameObject go = GameObjectFinder.Find(goName);
                        if (go == null) throw new InvalidOperationException($"GameObject '{goName}' not found");

                        Type type = AddComponentHandler.FindType(compType);
                        if (type == null) throw new InvalidOperationException($"Component type '{compType}' not found");

                        if (go.GetComponent(type) != null)
                        {
                            results.Add(new JObject
                            {
                                ["gameObject"] = goName,
                                ["component"] = type.Name,
                                ["success"] = true,
                                ["skipped"] = true,
                                ["reason"] = "Component already exists"
                            });
                            continue;
                        }

                        Component comp = Undo.AddComponent(go, type);
                        EditorUtility.SetDirty(go);

                        results.Add(new JObject
                        {
                            ["gameObject"] = goName,
                            ["component"] = comp.GetType().Name,
                            ["instanceId"] = comp.GetInstanceID(),
                            ["success"] = true
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(ItemError(goName, compType, ex.Message));
                    }
                }

                Undo.CollapseUndoOperations(groupIndex);

                return new JObject
                {
                    ["success"] = true,
                    ["results"] = results
                };
            });
        }

        private static JObject ItemError(string go, string comp, string msg) =>
            new JObject
            {
                ["gameObject"] = go ?? string.Empty,
                ["component"] = comp ?? string.Empty,
                ["success"] = false,
                ["error"] = msg
            };
    }
}
