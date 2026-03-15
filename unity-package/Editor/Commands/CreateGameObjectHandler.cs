using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class CreateGameObjectHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string name = "GameObject";
            string primitiveType = null;
            float[] position = null;

            if (parameters != null)
            {
                if (parameters.TryGetValue("name", out JToken nameToken) && nameToken.Type != JTokenType.Null)
                    name = nameToken.Value<string>() ?? "GameObject";

                if (parameters.TryGetValue("primitive_type", out JToken primitiveToken) && primitiveToken.Type != JTokenType.Null)
                    primitiveType = primitiveToken.Value<string>();

                if (parameters.TryGetValue("position", out JToken positionToken) && positionToken is JArray positionArray && positionArray.Count >= 3)
                {
                    position = new float[3];
                    for (int i = 0; i < 3; i++)
                        position[i] = positionArray[i].Value<float>();
                }
            }

            string capturedName = name;
            string capturedPrimitive = primitiveType;
            float[] capturedPosition = position;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject gameObject;

                if (!string.IsNullOrEmpty(capturedPrimitive) && TryParsePrimitive(capturedPrimitive, out PrimitiveType primitive))
                {
                    gameObject = ObjectFactory.CreatePrimitive(primitive);
                    gameObject.name = capturedName;
                }
                else
                {
                    gameObject = ObjectFactory.CreateGameObject(capturedName);
                }

                if (capturedPosition != null)
                {
                    gameObject.transform.position = new Vector3(capturedPosition[0], capturedPosition[1], capturedPosition[2]);
                }

                EditorSceneManager.MarkSceneDirty(gameObject.scene);

                return new JObject
                {
                    ["name"] = gameObject.name,
                    ["instanceId"] = gameObject.GetInstanceID()
                };
            });

            return result;
        }

        private static bool TryParsePrimitive(string value, out PrimitiveType result)
        {
            return System.Enum.TryParse(value, true, out result);
        }
    }
}
