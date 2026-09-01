using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class CompileAndGetErrorsHandler : ICommandHandler
    {
        private const string CommandKey = "compile_and_get_errors";

        public async Task<object> HandleAsync(JObject parameters)
        {
            DomainReloadRunRecord replayRecord = await MainThreadQueue.EnqueueAsync(() =>
            {
                DomainReloadTracker.TryCompleteRun(CommandKey, out var record);
                return record;
            });

            if (replayRecord != null)
            {
                bool replayEverCompiling = await WaitWhileCompilingAsync();
                return await MainThreadQueue.EnqueueAsync(() =>
                    BuildResultObject(
                        replayRecord.StartedUtcTicks,
                        replayRecord.StartReloadCount,
                        replayEverCompiling
                    )
                );
            }

            long startedUtcTicks = 0;
            int startReloadCount = await MainThreadQueue.EnqueueAsync(() =>
            {
                startedUtcTicks = DateTime.UtcNow.Ticks;
                DomainReloadTracker.BeginRun(CommandKey);
                CompilationErrorBuffer.Clear();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                return DomainReloadTracker.ReloadCount;
            });

            bool everCompiling = false;
            bool compiling = false;
            int waitTime = 0;
            while (waitTime < 1000)
            {
                compiling = await MainThreadQueue.EnqueueAsync(() => EditorApplication.isCompiling);
                if (compiling)
                {
                    everCompiling = true;
                    break;
                }
                await Task.Delay(50);
                waitTime += 50;
            }

            if (compiling)
            {
                while (compiling)
                {
                    await Task.Delay(100);
                    compiling = await MainThreadQueue.EnqueueAsync(() =>
                        EditorApplication.isCompiling
                    );
                }
            }

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                DomainReloadTracker.ClearRun(CommandKey);
                return BuildResultObject(startedUtcTicks, startReloadCount, everCompiling);
            });
        }

        private static async Task<bool> WaitWhileCompilingAsync()
        {
            bool everCompiling = await MainThreadQueue.EnqueueAsync(() =>
                EditorApplication.isCompiling
            );
            bool compiling = everCompiling;
            while (compiling)
            {
                await Task.Delay(100);
                compiling = await MainThreadQueue.EnqueueAsync(() => EditorApplication.isCompiling);
            }
            return everCompiling;
        }

        private static object BuildResultObject(
            long startedUtcTicks,
            int startReloadCount,
            bool everCompiling
        )
        {
            var all = CompilationErrorBuffer.GetAll();
            var errors = new JArray();
            int errorCount = 0;
            int warningCount = 0;

            foreach (var entry in all)
            {
                errors.Add(
                    new JObject
                    {
                        ["file"] = entry.File,
                        ["line"] = entry.Line,
                        ["column"] = entry.Column,
                        ["message"] = entry.Message,
                        ["severity"] = entry.Severity,
                    }
                );

                if (entry.Severity == "error")
                    errorCount++;
                else
                    warningCount++;
            }

            bool compilationRan =
                everCompiling
                || CompilationErrorBuffer.HasResults
                || CompilationErrorBuffer.LastCompilationStartedUtcTicks >= startedUtcTicks;
            bool domainReloadObserved = DomainReloadTracker.ReloadCount > startReloadCount;

            var result = new JObject
            {
                ["errorCount"] = errorCount,
                ["warningCount"] = warningCount,
                ["errors"] = errors,
                ["compilationRan"] = compilationRan,
                ["domainReloadObserved"] = domainReloadObserved,
                ["reloadCount"] = DomainReloadTracker.ReloadCount,
            };

            if (!compilationRan && !domainReloadObserved)
            {
                result["note"] =
                    "No compilation and no domain reload happened in this window, so a zero error count does not mean the project is clean. Call request_script_reload to force a real domain reload, then read get_console_logs.";
            }

            return result;
        }
    }
}
