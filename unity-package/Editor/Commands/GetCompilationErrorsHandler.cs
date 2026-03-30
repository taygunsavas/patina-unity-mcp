using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace Patina.Editor.Commands
{
    public sealed class GetCompilationErrorsHandler : ICommandHandler
    {
        public Task<object> HandleAsync(JObject parameters)
        {
            var all = CompilationErrorBuffer.GetAll();
            var errors = new JArray();

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
            }

            int errorCount = 0;
            int warningCount = 0;
            foreach (var entry in all)
            {
                if (entry.Severity == "error") errorCount++;
                else warningCount++;
            }

            object result = new JObject
            {
                ["errorCount"] = errorCount,
                ["warningCount"] = warningCount,
                ["hasResults"] = CompilationErrorBuffer.HasResults,
                ["errors"] = errors
            };

            return Task.FromResult(result);
        }
    }
}
