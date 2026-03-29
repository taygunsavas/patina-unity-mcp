using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class ExecuteMenuItemHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string menuPath = parameters != null && parameters.TryGetValue("menu_path", out JToken pathToken)
                ? pathToken.Value<string>() ?? ""
                : "";

            bool ok = await MainThreadQueue.EnqueueAsync<bool>(() =>
            {
                return EditorApplication.ExecuteMenuItem(menuPath);
            });

            return new JObject
            {
                ["menuPath"] = menuPath,
                ["success"] = ok
            };
        }
    }
}
