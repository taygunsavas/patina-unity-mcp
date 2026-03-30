using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class ForceRecompileHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            return await MainThreadQueue.EnqueueAsync(() =>
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                return new JObject
                {
                    ["triggered"] = true,
                    ["isCompiling"] = EditorApplication.isCompiling
                };
            });
        }
    }
}
