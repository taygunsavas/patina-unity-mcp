using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class RequestScriptReloadHandler : ICommandHandler
    {
        private const string CommandKey = "request_script_reload";
        private const int DefaultTimeoutMs = 60000;
        private const int MinTimeoutMs = 1000;
        private const int MaxTimeoutMs = 180000;
        private const int PollIntervalMs = 250;
        private const int SettleWindowMs = 2000;

        private const string CompileErrorsNote =
            "Unity does not reload the domain while script compilation is failing. Fix the compile errors, then retry.";
        private const string PlayModeNote =
            "Unity does not reload the domain while the editor is in or entering play mode. Exit play mode, then retry.";
        private const string TimedOutNote =
            "The domain reload did not complete within the timeout. It may still be in progress. Call request_script_reload again, or increase timeout_ms.";

        public async Task<object> HandleAsync(JObject parameters)
        {
            DomainReloadRunRecord completedRecord = await MainThreadQueue.EnqueueAsync(() =>
            {
                DomainReloadTracker.TryCompleteRun(CommandKey, out var record);
                return record;
            });

            if (completedRecord != null)
                return await BuildCompletedResultAsync(completedRecord);

            int timeoutMs = ResolveTimeoutMs(parameters);

            await MainThreadQueue.EnqueueAsync(() =>
            {
                DomainReloadRunStart startResult = DomainReloadTracker.BeginRun(CommandKey);
                if (startResult == DomainReloadRunStart.Started)
                    EditorUtility.RequestScriptReload();
                return true;
            });

            int elapsedMs = 0;
            while (elapsedMs < timeoutMs)
            {
                await Task.Delay(PollIntervalMs);
                elapsedMs += PollIntervalMs;

                if (elapsedMs < SettleWindowMs)
                    continue;

                var poll = await MainThreadQueue.EnqueueAsync(() =>
                    new
                    {
                        IsPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                        IsCompiling = EditorApplication.isCompiling,
                        ScriptCompilationFailed = EditorUtility.scriptCompilationFailed,
                    }
                );

                if (poll.IsPlayingOrWillChangePlaymode)
                    return await BuildFailureResultAsync("playModeBlocksReload", PlayModeNote);

                if (!poll.IsCompiling && poll.ScriptCompilationFailed)
                    return await BuildFailureResultAsync(
                        "compileErrorsBlockReload",
                        CompileErrorsNote
                    );
            }

            return await BuildFailureResultAsync("timedOut", TimedOutNote);
        }

        private static async Task<object> BuildFailureResultAsync(string reason, string note)
        {
            return await MainThreadQueue.EnqueueAsync(() =>
            {
                DomainReloadTracker.ClearRun(CommandKey);
                bool hasCompileErrors = EditorUtility.scriptCompilationFailed;
                bool isPlayingOrWillChangePlaymode =
                    EditorApplication.isPlayingOrWillChangePlaymode;

                return (object)
                    new JObject
                    {
                        ["reloadCompleted"] = false,
                        ["reason"] = reason,
                        ["reloadCount"] = DomainReloadTracker.ReloadCount,
                        ["hasCompileErrors"] = hasCompileErrors,
                        ["isPlayingOrWillChangePlaymode"] = isPlayingOrWillChangePlaymode,
                        ["note"] = note,
                    };
            });
        }

        private static async Task<object> BuildCompletedResultAsync(DomainReloadRunRecord record)
        {
            bool compiling = await MainThreadQueue.EnqueueAsync(() =>
                EditorApplication.isCompiling
            );
            while (compiling)
            {
                await Task.Delay(100);
                compiling = await MainThreadQueue.EnqueueAsync(() => EditorApplication.isCompiling);
            }

            return await MainThreadQueue.EnqueueAsync(() =>
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

                var reloadWindowLogs = new JArray();
                foreach (var logEntry in ConsoleLogBuffer.GetReloadWindowEntries(100))
                {
                    reloadWindowLogs.Add(
                        new JObject
                        {
                            ["type"] = logEntry.Type,
                            ["message"] = logEntry.Message,
                            ["timestamp"] = logEntry.Timestamp,
                            ["phase"] = logEntry.Phase,
                        }
                    );
                }

                return (object)
                    new JObject
                    {
                        ["reloadCompleted"] = true,
                        ["reloadCount"] = DomainReloadTracker.ReloadCount,
                        ["durationMs"] = record.ElapsedMs,
                        ["errorCount"] = errorCount,
                        ["warningCount"] = warningCount,
                        ["errors"] = errors,
                        ["reloadWindowLogs"] = reloadWindowLogs,
                    };
            });
        }

        private static int ResolveTimeoutMs(JObject parameters)
        {
            int timeoutMs = parameters?.Value<int?>("timeout_ms") ?? DefaultTimeoutMs;
            if (timeoutMs < MinTimeoutMs)
                return MinTimeoutMs;
            if (timeoutMs > MaxTimeoutMs)
                return MaxTimeoutMs;
            return timeoutMs;
        }
    }
}
