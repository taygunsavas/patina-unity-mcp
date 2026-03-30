using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class UndoHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            int count = parameters?["count"]?.Value<int>() ?? 1;
            if (count < 1) count = 1;
            int capturedCount = count;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                for (int i = 0; i < capturedCount; i++)
                    Undo.PerformUndo();

                return new JObject
                {
                    ["performedCount"] = capturedCount,
                    ["success"] = true
                };
            });
        }
    }
}
