using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class GetEditorStateHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            double mainThreadUpdateAgeSeconds = MainThreadQueue.TimeSinceLastUpdate.TotalSeconds;
            int mainThreadQueuePendingCount = MainThreadQueue.PendingCount;

            return await MainThreadQueue.EnqueueAsync<JObject>(() =>
            {
                return new JObject
                {
                    ["isServiceable"] = true,
                    ["isCompiling"] = EditorApplication.isCompiling,
                    ["isPlaying"] = EditorApplication.isPlaying,
                    ["isPaused"] = EditorApplication.isPaused,
                    ["isUpdating"] = EditorApplication.isUpdating,
                    ["hasCompileErrors"] = EditorUtility.scriptCompilationFailed,
                    ["mainThreadUpdateAgeSeconds"] = mainThreadUpdateAgeSeconds,
                    ["mainThreadQueuePendingCount"] = mainThreadQueuePendingCount,
                    ["blockedByModalDialogLikely"] =
                        mainThreadUpdateAgeSeconds >= MainThreadQueue.BlockedThresholdSeconds,
                    ["unityVersion"] = Application.unityVersion,
                    ["projectPath"] = Application.dataPath,
                    ["isAutomatedMode"] = McpBridgeServer.IsAutomatedMode,
                    ["dialogAutomationAvailable"] = DialogAutomation.IsAvailable,
                };
            });
        }
    }
}
