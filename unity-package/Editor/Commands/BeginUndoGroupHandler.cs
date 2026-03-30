using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class BeginUndoGroupHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string label = parameters?["label"]?.Value<string>();
            if (string.IsNullOrEmpty(label))
                throw new ArgumentException("label is required");

            string capturedLabel = label;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                Undo.SetCurrentGroupName(capturedLabel);
                int groupIndex = Undo.GetCurrentGroup();

                return new JObject
                {
                    ["groupIndex"] = groupIndex,
                    ["label"] = capturedLabel,
                    ["success"] = true
                };
            });
        }
    }
}
