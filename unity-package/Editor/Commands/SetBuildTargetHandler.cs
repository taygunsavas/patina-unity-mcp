using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class SetBuildTargetHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            return await MainThreadQueue.EnqueueAsync<JObject>(() =>
            {
                string targetStr = parameters?["build_target"]?.Value<string>();
                if (string.IsNullOrEmpty(targetStr))
                    throw new ArgumentException("build_target is required");

                BuildTarget newTarget;
                try
                {
                    newTarget = (BuildTarget)Enum.Parse(typeof(BuildTarget), targetStr, true);
                }
                catch
                {
                    throw new ArgumentException($"Invalid build target: '{targetStr}'. Valid values include StandaloneWindows64, StandaloneOSX, Android, iOS, WebGL.");
                }

                BuildTarget previousTarget = EditorUserBuildSettings.activeBuildTarget;
                BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(newTarget);
                bool success = EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, newTarget);

                return new JObject
                {
                    ["previousTarget"] = previousTarget.ToString(),
                    ["newTarget"] = newTarget.ToString(),
                    ["success"] = success
                };
            });
        }
    }
}
