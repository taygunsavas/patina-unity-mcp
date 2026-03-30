using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class EndUndoGroupHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            int groupIndex = parameters?["group_index"]?.Value<int>() ?? -1;
            if (groupIndex < 0)
                throw new ArgumentException("group_index is required and must be >= 0");

            int capturedIndex = groupIndex;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                Undo.CollapseUndoOperations(capturedIndex);

                return new JObject
                {
                    ["groupIndex"] = capturedIndex,
                    ["success"] = true
                };
            });
        }
    }
}
