using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class SetBuildScenesHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            if (parameters == null || !parameters.TryGetValue("scene_paths", out JToken pathsToken) || pathsToken.Type == JTokenType.Null)
                throw new ArgumentException("scene_paths is required");

            if (pathsToken.Type != JTokenType.Array)
                throw new ArgumentException("scene_paths must be an array");

            var paths = new List<string>();
            foreach (JToken t in (JArray)pathsToken)
                paths.Add(t.Value<string>());

            List<string> capturedPaths = paths;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                var buildScenes = new EditorBuildSettingsScene[capturedPaths.Count];
                for (int i = 0; i < capturedPaths.Count; i++)
                    buildScenes[i] = new EditorBuildSettingsScene(capturedPaths[i], true);

                EditorBuildSettings.scenes = buildScenes;

                return new JObject
                {
                    ["sceneCount"] = buildScenes.Length,
                    ["success"] = true
                };
            });

            return result;
        }
    }
}
