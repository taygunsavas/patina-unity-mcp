using Newtonsoft.Json.Linq;
using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class CreateScriptHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            if (parameters == null)
                throw new ArgumentException("Parameters are required");

            string scriptName = parameters["script_name"]?.Value<string>();
            if (string.IsNullOrEmpty(scriptName))
                throw new ArgumentException("script_name is required");

            string folderPath = parameters["folder_path"]?.Value<string>();
            if (string.IsNullOrEmpty(folderPath))
                throw new ArgumentException("folder_path is required");

            string template = parameters["template"]?.Value<string>() ?? "monobehaviour";
            string namespaceName = parameters["namespace"]?.Value<string>();
            string content = parameters["content"]?.Value<string>();

            if (scriptName.IndexOfAny(new[] { '/', '\\' }) >= 0 || scriptName.Contains(".."))
                throw new ArgumentException("script_name cannot contain path separators or traversal sequences");

            if (!System.CodeDom.Compiler.CodeGenerator.IsValidLanguageIndependentIdentifier(scriptName))
                throw new ArgumentException("script_name must be a valid C# identifier (no spaces, special characters, or leading digits)");

            string capturedScript = scriptName;
            string capturedFolder = folderPath.Replace('\\', '/').Trim().TrimEnd('/');
            string capturedTemplate = template;
            string capturedNamespace = namespaceName;
            string capturedContent = content;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                if (!AssetDatabase.IsValidFolder(capturedFolder))
                    throw new ArgumentException($"Folder does not exist: {capturedFolder}");

                string assetPath = capturedFolder + "/" + capturedScript + ".cs";
                string fullPath = Path.GetFullPath(assetPath);

                if (File.Exists(fullPath))
                    throw new ArgumentException($"Script already exists: {assetPath}");

                string fileContent = capturedContent ?? GenerateTemplate(capturedScript, capturedTemplate, capturedNamespace);
                File.WriteAllText(fullPath, fileContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

                return new JObject
                {
                    ["path"] = assetPath,
                    ["className"] = capturedScript,
                    ["template"] = capturedContent != null ? "custom" : capturedTemplate,
                    ["success"] = true
                };
            });

            return result;
        }

        private static string GenerateTemplate(string className, string template, string namespaceName)
        {
            string usings;
            string classBody;

            switch (template.ToLowerInvariant())
            {
                case "scriptableobject":
                    usings = "using UnityEngine;";
                    classBody = $"[CreateAssetMenu(fileName = \"{className}\", menuName = \"{className}\")]\npublic class {className} : ScriptableObject\n{{\n}}";
                    break;
                case "editor_window":
                    usings = "using UnityEditor;";
                    classBody = $"public class {className} : EditorWindow\n{{\n    [MenuItem(\"Window/{className}\")]\n    public static void Open()\n    {{\n        GetWindow<{className}>(\"{className}\");\n    }}\n\n    private void OnGUI()\n    {{\n    }}\n}}";
                    break;
                case "plain_class":
                    usings = null;
                    classBody = $"public class {className}\n{{\n}}";
                    break;
                case "interface":
                    usings = null;
                    classBody = $"public interface {className}\n{{\n}}";
                    break;
                case "monobehaviour":
                default:
                    if (template.ToLowerInvariant() != "monobehaviour")
                        throw new ArgumentException($"Unknown template '{template}'. Valid values: monobehaviour, scriptableobject, editor_window, plain_class, interface");
                    usings = "using UnityEngine;";
                    classBody = $"public class {className} : MonoBehaviour\n{{\n}}";
                    break;
            }

            if (string.IsNullOrEmpty(namespaceName))
            {
                return string.IsNullOrEmpty(usings)
                    ? classBody + "\n"
                    : usings + "\n\n" + classBody + "\n";
            }

            string indentedBody = IndentLines(classBody, "    ");
            string nsBlock = $"namespace {namespaceName}\n{{\n{indentedBody}\n}}\n";

            return string.IsNullOrEmpty(usings)
                ? nsBlock
                : usings + "\n\n" + nsBlock;
        }

        private static string IndentLines(string text, string indent)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var sb = new StringBuilder();
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                    sb.Append('\n');
                sb.Append(string.IsNullOrEmpty(lines[i]) ? lines[i] : indent + lines[i]);
            }
            return sb.ToString();
        }
    }
}
