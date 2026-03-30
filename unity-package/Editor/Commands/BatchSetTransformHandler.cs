using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class BatchSetTransformHandler : ICommandHandler
    {
        private const int MaxOperations = 100;

        public async Task<object> HandleAsync(JObject parameters)
        {
            var operationsToken = parameters?["operations"] as JArray;
            if (operationsToken == null || operationsToken.Count == 0)
                throw new ArgumentException("operations array is required");
            if (operationsToken.Count > MaxOperations)
                throw new ArgumentException($"operations exceeds max of {MaxOperations}");

            string space = parameters?["space"]?.Value<string>() ?? "world";
            if (space != "world" && space != "local")
                throw new ArgumentException("space must be \"world\" or \"local\"");

            string undoLabel = parameters?["undo_label"]?.Value<string>() ?? "Patina Batch SetTransform";
            var ops = operationsToken;
            bool useWorld = space == "world";
            string capturedLabel = undoLabel;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                Undo.SetCurrentGroupName(capturedLabel);
                int groupIndex = Undo.GetCurrentGroup();

                var results = new JArray();
                foreach (JToken opToken in ops)
                {
                    string goName = opToken["game_object_name"]?.Value<string>();
                    if (string.IsNullOrEmpty(goName))
                    {
                        results.Add(ItemError(goName, "game_object_name is required"));
                        continue;
                    }

                    try
                    {
                        GameObject go = GameObjectFinder.Find(goName);
                        if (go == null) throw new InvalidOperationException($"GameObject '{goName}' not found");

                        Undo.RecordObject(go.transform, "Set Transform");

                        JArray posArr = opToken["position"] as JArray;
                        JArray rotArr = opToken["rotation_euler"] as JArray;
                        JArray scaleArr = opToken["scale"] as JArray;

                        if (posArr != null)
                        {
                            var pos = new Vector3(posArr[0].Value<float>(), posArr[1].Value<float>(), posArr[2].Value<float>());
                            if (useWorld) go.transform.position = pos;
                            else go.transform.localPosition = pos;
                        }
                        if (rotArr != null)
                        {
                            var rot = new Vector3(rotArr[0].Value<float>(), rotArr[1].Value<float>(), rotArr[2].Value<float>());
                            if (useWorld) go.transform.eulerAngles = rot;
                            else go.transform.localEulerAngles = rot;
                        }
                        if (scaleArr != null)
                        {
                            go.transform.localScale = new Vector3(
                                scaleArr[0].Value<float>(), scaleArr[1].Value<float>(), scaleArr[2].Value<float>());
                        }

                        EditorSceneManager.MarkSceneDirty(go.scene);
                        results.Add(new JObject { ["gameObject"] = goName, ["success"] = true });
                    }
                    catch (Exception ex)
                    {
                        results.Add(ItemError(goName, ex.Message));
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

        private static JObject ItemError(string go, string msg) =>
            new JObject
            {
                ["gameObject"] = go ?? string.Empty,
                ["success"] = false,
                ["error"] = msg
            };
    }
}
