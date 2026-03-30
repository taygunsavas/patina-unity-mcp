using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Patina.Editor.Commands
{
    public sealed class GetScriptContentHandler : ICommandHandler
    {
        private const int MaxBytes = 50 * 1024;

        public Task<object> HandleAsync(JObject parameters)
        {
            string assetPath = parameters?["asset_path"]?.Value<string>();
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("asset_path is required");
            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("asset_path must start with Assets/");
            if (assetPath.Contains(".."))
                throw new ArgumentException("asset_path must not contain path traversal sequences");

            string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            string fullPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));

            if (!fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Resolved path is outside the project directory");
            if (!File.Exists(fullPath))
                throw new InvalidOperationException($"Script not found: {assetPath}");

            byte[] bytes = File.ReadAllBytes(fullPath);
            if (bytes.Length > MaxBytes)
                throw new InvalidOperationException($"Script exceeds 50 KB limit ({bytes.Length} bytes). Read a subsection or refactor the file.");

            string content = System.Text.Encoding.UTF8.GetString(bytes);
            int lineCount = 0;
            foreach (char c in content)
                if (c == '\n') lineCount++;
            if (content.Length > 0) lineCount++;

            object result = new JObject
            {
                ["assetPath"] = assetPath,
                ["content"] = content,
                ["lineCount"] = lineCount,
                ["byteSize"] = bytes.Length
            };

            return Task.FromResult(result);
        }
    }
}
