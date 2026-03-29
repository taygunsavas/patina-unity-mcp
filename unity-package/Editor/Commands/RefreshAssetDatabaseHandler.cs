using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class RefreshAssetDatabaseHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string importOptions = parameters?["import_options"]?.Value<string>() ?? "default";
            string capturedOptions = importOptions;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                if (capturedOptions == "force_update")
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                else
                    AssetDatabase.Refresh();

                return new JObject { ["success"] = true };
            });
        }
    }
}
