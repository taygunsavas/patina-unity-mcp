using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class SetTransformHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string gameObjectName = parameters?["game_object_name"]?.Value<string>();
            if (string.IsNullOrEmpty(gameObjectName))
                throw new ArgumentException("game_object_name is required");

            JArray posArr = parameters?["position"] as JArray;
            JArray rotArr = parameters?["rotation_euler"] as JArray;
            JArray scaleArr = parameters?["scale"] as JArray;
            string space = parameters?["space"]?.Value<string>() ?? "world";

            if (space != "world" && space != "local")
                throw new ArgumentException("space must be \"world\" or \"local\"");

            string capturedGoName = gameObjectName;
            Vector3? capturedPosition = posArr != null
                ? (Vector3?)new Vector3(posArr[0].Value<float>(), posArr[1].Value<float>(), posArr[2].Value<float>())
                : null;
            Vector3? capturedRotation = rotArr != null
                ? (Vector3?)new Vector3(rotArr[0].Value<float>(), rotArr[1].Value<float>(), rotArr[2].Value<float>())
                : null;
            Vector3? capturedScale = scaleArr != null
                ? (Vector3?)new Vector3(scaleArr[0].Value<float>(), scaleArr[1].Value<float>(), scaleArr[2].Value<float>())
                : null;
            bool useWorld = space == "world";

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject go = FindSceneGameObject(capturedGoName);
                if (go == null)
                    throw new InvalidOperationException($"GameObject '{capturedGoName}' not found");

                Undo.RecordObject(go.transform, "Set Transform");

                if (capturedPosition.HasValue)
                {
                    if (useWorld) go.transform.position = capturedPosition.Value;
                    else go.transform.localPosition = capturedPosition.Value;
                }
                if (capturedRotation.HasValue)
                {
                    if (useWorld) go.transform.eulerAngles = capturedRotation.Value;
                    else go.transform.localEulerAngles = capturedRotation.Value;
                }
                if (capturedScale.HasValue)
                {
                    go.transform.localScale = capturedScale.Value;
                }

                EditorSceneManager.MarkSceneDirty(go.scene);

                Vector3 finalPos = useWorld ? go.transform.position : go.transform.localPosition;
                Vector3 finalRot = useWorld ? go.transform.eulerAngles : go.transform.localEulerAngles;
                Vector3 finalScale = go.transform.localScale;

                return new JObject
                {
                    ["gameObject"] = capturedGoName,
                    ["position"] = new JArray(finalPos.x, finalPos.y, finalPos.z),
                    ["rotationEuler"] = new JArray(finalRot.x, finalRot.y, finalRot.z),
                    ["scale"] = new JArray(finalScale.x, finalScale.y, finalScale.z),
                    ["success"] = true
                };
            });

            return result;
        }

        private static GameObject FindSceneGameObject(string name)
        {
            foreach (GameObject go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (go.scene.IsValid() && go.name == name)
                    return go;
            }
            return null;
        }
    }
}
