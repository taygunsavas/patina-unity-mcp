using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Patina.CSharpStyleCheck;

public sealed record StyleDiagnostic(string Rule, string Message, FileLinePositionSpan Location);

public static class StyleChecker
{
    public static IReadOnlyList<StyleDiagnostic> Analyze(string source, string path = "input.cs")
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: path);
        var diagnostics = new List<StyleDiagnostic>();

        foreach (
            var parseDiagnostic in tree.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        )
        {
            diagnostics.Add(
                Create(
                    "CS-PARSE",
                    parseDiagnostic.GetMessage(),
                    tree,
                    parseDiagnostic.Location.SourceSpan
                )
            );
        }

        var root = tree.GetRoot();
        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            if (field.Parent is InterfaceDeclarationSyntax)
            {
                continue;
            }

            CheckAccessibility(field, field.Modifiers, diagnostics, tree, "field");
            var isPrivate =
                HasModifier(field.Modifiers, SyntaxKind.PrivateKeyword)
                || !HasAccessibility(field.Modifiers);
            if (!isPrivate)
            {
                continue;
            }

            var isConst = HasModifier(field.Modifiers, SyntaxKind.ConstKeyword);
            var isStatic = HasModifier(field.Modifiers, SyntaxKind.StaticKeyword);
            foreach (var variable in field.Declaration.Variables)
            {
                CheckName(
                    variable.Identifier,
                    isConst ? "const field"
                        : isStatic ? "private static field"
                        : "private instance field",
                    isConst ? IsPascalCase
                        : isStatic ? IsStaticFieldName
                        : IsInstanceFieldName,
                    diagnostics,
                    tree
                );
            }
        }

        foreach (
            var local in root.DescendantNodes()
                .OfType<LocalDeclarationStatementSyntax>()
                .Where(local => HasModifier(local.Modifiers, SyntaxKind.ConstKeyword))
        )
        {
            foreach (var variable in local.Declaration.Variables)
            {
                CheckName(variable.Identifier, "const local", IsPascalCase, diagnostics, tree);
            }
        }

        foreach (var member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            if (member.Parent is InterfaceDeclarationSyntax || member is InterfaceDeclarationSyntax)
            {
                continue;
            }

            switch (member)
            {
                case BaseTypeDeclarationSyntax type:
                    CheckAccessibility(type, type.Modifiers, diagnostics, tree, "type");
                    break;
                case DelegateDeclarationSyntax @delegate:
                    CheckAccessibility(
                        @delegate,
                        @delegate.Modifiers,
                        diagnostics,
                        tree,
                        "delegate"
                    );
                    break;
                case MethodDeclarationSyntax method when method.ExplicitInterfaceSpecifier is null:
                    CheckAccessibility(method, method.Modifiers, diagnostics, tree, "method");
                    break;
                case ConstructorDeclarationSyntax constructor
                    when !HasModifier(constructor.Modifiers, SyntaxKind.StaticKeyword):
                    CheckAccessibility(
                        constructor,
                        constructor.Modifiers,
                        diagnostics,
                        tree,
                        "constructor"
                    );
                    break;
                case PropertyDeclarationSyntax property
                    when property.ExplicitInterfaceSpecifier is null:
                    CheckAccessibility(property, property.Modifiers, diagnostics, tree, "property");
                    break;
                case IndexerDeclarationSyntax indexer
                    when indexer.ExplicitInterfaceSpecifier is null:
                    CheckAccessibility(indexer, indexer.Modifiers, diagnostics, tree, "indexer");
                    break;
                case EventDeclarationSyntax @event when @event.ExplicitInterfaceSpecifier is null:
                    CheckAccessibility(@event, @event.Modifiers, diagnostics, tree, "event");
                    break;
                case EventFieldDeclarationSyntax eventField:
                    CheckAccessibility(
                        eventField,
                        eventField.Modifiers,
                        diagnostics,
                        tree,
                        "event field"
                    );
                    break;
            }
        }

        return diagnostics;
    }

    private static void CheckAccessibility(
        SyntaxNode node,
        SyntaxTokenList modifiers,
        List<StyleDiagnostic> diagnostics,
        SyntaxTree tree,
        string kind
    )
    {
        if (!HasAccessibility(modifiers))
        {
            diagnostics.Add(
                Create(
                    "CS-ACCESS",
                    $"{kind} requires an explicit accessibility modifier.",
                    tree,
                    node.Span
                )
            );
        }
    }

    private static void CheckName(
        SyntaxToken identifier,
        string kind,
        Func<string, bool> isValid,
        List<StyleDiagnostic> diagnostics,
        SyntaxTree tree
    )
    {
        if (!isValid(identifier.ValueText))
        {
            diagnostics.Add(
                Create(
                    "CS-NAME",
                    $"{kind} '{identifier.ValueText}' does not follow the required naming convention.",
                    tree,
                    identifier.Span
                )
            );
        }
    }

    private static StyleDiagnostic Create(
        string rule,
        string message,
        SyntaxTree tree,
        TextSpan span
    ) => new(rule, message, tree.GetLineSpan(span));

    private static bool HasAccessibility(SyntaxTokenList modifiers) =>
        modifiers.Any(modifier =>
            modifier.IsKind(SyntaxKind.PublicKeyword)
            || modifier.IsKind(SyntaxKind.PrivateKeyword)
            || modifier.IsKind(SyntaxKind.ProtectedKeyword)
            || modifier.IsKind(SyntaxKind.InternalKeyword)
            || modifier.IsKind(SyntaxKind.FileKeyword)
        );

    private static bool HasModifier(SyntaxTokenList modifiers, SyntaxKind kind) =>
        modifiers.Any(modifier => modifier.IsKind(kind));

    private static bool IsInstanceFieldName(string name) =>
        name.Length > 1 && name[0] == '_' && char.IsLower(name[1]) && IsCamelCase(name[1..]);

    private static bool IsStaticFieldName(string name) =>
        name.Length > 2
        && name.StartsWith("s_", StringComparison.Ordinal)
        && char.IsLower(name[2])
        && IsCamelCase(name[2..]);

    private static bool IsPascalCase(string name) => name.Length > 0 && char.IsUpper(name[0]);

    private static bool IsCamelCase(string name) => name.Length > 0 && !name.Contains('_');
}
