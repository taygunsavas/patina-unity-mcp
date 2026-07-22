namespace Patina.CSharpStyleCheck;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: Patina.CSharpStyleCheck <file.cs> [...]");
            return 2;
        }

        var diagnostics = args.SelectMany(path =>
                StyleChecker.Analyze(File.ReadAllText(path), path)
            )
            .ToArray();
        foreach (var diagnostic in diagnostics)
        {
            var position = diagnostic.Location.StartLinePosition;
            Console.Error.WriteLine(
                $"{diagnostic.Location.Path}({position.Line + 1},{position.Character + 1}): error {diagnostic.Rule}: {diagnostic.Message}"
            );
        }

        return diagnostics.Length == 0 ? 0 : 1;
    }
}
