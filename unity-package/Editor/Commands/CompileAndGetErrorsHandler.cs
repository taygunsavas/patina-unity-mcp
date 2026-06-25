using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class CompileAndGetErrorsHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            // 1. Clear buffer and trigger refresh on main thread
            await MainThreadQueue.EnqueueAsync(() =>
            {
                CompilationErrorBuffer.Clear();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                return true;
            });

            // 2. Wait for compilation to start and finish in background (polling isCompiling on main thread)
            bool compiling = false;
            int waitTime = 0;
            while (waitTime < 1000)
            {
                compiling = await MainThreadQueue.EnqueueAsync(() => EditorApplication.isCompiling);
                if (compiling) break;
                await Task.Delay(50);
                waitTime += 50;
            }

            if (compiling)
            {
                while (compiling)
                {
                    await Task.Delay(100);
                    compiling = await MainThreadQueue.EnqueueAsync(() => EditorApplication.isCompiling);
                }
            }

            // 3. Gather results on main thread
            return await MainThreadQueue.EnqueueAsync(() =>
            {
                var all = CompilationErrorBuffer.GetAll();
                var errors = new JArray();
                int errorCount = 0;
                int warningCount = 0;

                foreach (var entry in all)
                {
                    errors.Add(new JObject
                    {
                        ["file"] = entry.File,
                        ["line"] = entry.Line,
                        ["column"] = entry.Column,
                        ["message"] = entry.Message,
                        ["severity"] = entry.Severity
                    });

                    if (entry.Severity == "error") errorCount++;
                    else warningCount++;
                }

                return new JObject
                {
                    ["errorCount"] = errorCount,
                    ["warningCount"] = warningCount,
                    ["errors"] = errors
                };
            });
        }
    }
}
