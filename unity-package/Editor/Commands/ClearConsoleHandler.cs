using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace Patina.Editor.Commands
{
    public sealed class ClearConsoleHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            await MainThreadQueue.EnqueueAsync<object>(() =>
            {
                var logEntries = System.Type.GetType("UnityEditor.LogEntries, UnityEditor");
                var clearMethod = logEntries?.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);

                if (logEntries == null || clearMethod == null)
                    throw new System.Exception("Could not access LogEntries.Clear via reflection");

                clearMethod.Invoke(null, null);
                return null;
            });

            return new JObject { ["success"] = true };
        }
    }
}
