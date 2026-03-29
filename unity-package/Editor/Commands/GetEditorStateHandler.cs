using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class GetEditorStateHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            return await MainThreadQueue.EnqueueAsync<JObject>(() =>
            {
                return new JObject
                {
                    ["isCompiling"] = EditorApplication.isCompiling,
                    ["isPlaying"] = EditorApplication.isPlaying,
                    ["isPaused"] = EditorApplication.isPaused,
                    ["isUpdating"] = EditorApplication.isUpdating,
                    ["hasCompileErrors"] = EditorUtility.scriptCompilationFailed,
                    ["unityVersion"] = Application.unityVersion,
                    ["projectPath"] = Application.dataPath
                };
            });
        }
    }
}
