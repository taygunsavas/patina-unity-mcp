using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Patina.Editor.Commands
{
    public sealed class GetSceneInfoHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            bool includeAllScenes = false;
            if (parameters != null && parameters.TryGetValue("include_all_scenes", out JToken token) && token.Type != JTokenType.Null)
                includeAllScenes = token.Value<bool>();

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                Scene activeScene = SceneManager.GetActiveScene();

                var info = new JObject
                {
                    ["name"] = activeScene.name,
                    ["path"] = activeScene.path,
                    ["buildIndex"] = activeScene.buildIndex,
                    ["isDirty"] = activeScene.isDirty,
                    ["isLoaded"] = activeScene.isLoaded,
                    ["rootCount"] = activeScene.rootCount
                };

                if (includeAllScenes)
                {
                    var scenes = new JArray();
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        Scene s = SceneManager.GetSceneAt(i);
                        scenes.Add(new JObject
                        {
                            ["name"] = s.name,
                            ["path"] = s.path,
                            ["buildIndex"] = s.buildIndex,
                            ["isLoaded"] = s.isLoaded,
                            ["isDirty"] = s.isDirty,
                            ["rootCount"] = s.rootCount
                        });
                    }
                    info["allScenes"] = scenes;
                }

                return info;
            });

            return result;
        }
    }
}
