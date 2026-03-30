using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class ListAnimationClipsHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string searchPath = parameters?["search_path"]?.Value<string>();
            string capturedPath = searchPath;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                string[] folders = string.IsNullOrEmpty(capturedPath)
                    ? null
                    : new[] { capturedPath };

                string[] guids = folders != null
                    ? AssetDatabase.FindAssets("t:AnimationClip", folders)
                    : AssetDatabase.FindAssets("t:AnimationClip");

                var arr = new JArray();
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (clip == null) continue;

                    arr.Add(new JObject
                    {
                        ["name"]          = clip.name,
                        ["assetPath"]     = path,
                        ["length"]        = clip.length,
                        ["frameRate"]     = clip.frameRate,
                        ["isLooping"]     = clip.isLooping,
                        ["isHumanMotion"] = clip.isHumanMotion
                    });
                }

                return new JObject
                {
                    ["count"] = arr.Count,
                    ["clips"] = arr
                };
            });
        }
    }
}
