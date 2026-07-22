using Xunit;

namespace Patina.CSharpStyleCheck.Tests;

public sealed class StyleCheckerTests
{
    [Fact]
    public void ValidDeclarationsPass()
    {
        const string Source =
            "public class Sample { private int _count; private static int s_total; private const int Limit = 1; public Sample() { const int Maximum = 2; } public int Count { get; } public event System.Action? Changed; public void Run() { } }";
        Assert.Empty(StyleChecker.Analyze(Source));
    }

    [Theory]
    [InlineData("public class Sample { private int count; }", "private instance field")]
    [InlineData("public class Sample { private static int total; }", "private static field")]
    [InlineData("public class Sample { private const int limit = 1; }", "const field")]
    [InlineData(
        "public class Sample { public void Run() { const int limit = 1; } }",
        "const local"
    )]
    public void InvalidNamesFail(string source, string expected)
    {
        Assert.Contains(
            StyleChecker.Analyze(source),
            diagnostic => diagnostic.Message.Contains(expected, StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData("class Sample { }")]
    [InlineData("public class Sample { int _count; }")]
    [InlineData("public class Sample { Sample() { } }")]
    [InlineData("public class Sample { void Run() { } }")]
    [InlineData("public class Sample { int Count { get; } }")]
    [InlineData("public class Sample { event System.Action? Changed; }")]
    [InlineData("public class Sample { class Nested { } }")]
    public void MissingAccessibilityFails(string source)
    {
        Assert.Contains(StyleChecker.Analyze(source), diagnostic => diagnostic.Rule == "CS-ACCESS");
    }

    [Fact]
    public void InterfaceAndExplicitInterfaceMembersAreExempt()
    {
        const string Source =
            "public interface IRunner { void Run(); int Count { get; } } public class Runner : IRunner { void IRunner.Run() { } int IRunner.Count => 0; }";
        Assert.Empty(StyleChecker.Analyze(Source));
    }

    [Fact]
    public void InterfaceFieldsAreExempt()
    {
        const string Source = "public interface IConstants { const int limit = 1; }";
        Assert.Empty(StyleChecker.Analyze(Source));
    }

    [Fact]
    public void InvalidClassFieldsStillFail()
    {
        const string Source = "public class Sample { private int count; }";
        Assert.Contains(StyleChecker.Analyze(Source), diagnostic => diagnostic.Rule == "CS-NAME");
    }

    [Fact]
    public void ParseErrorsFail()
    {
        Assert.Contains(
            StyleChecker.Analyze("public class Sample {"),
            diagnostic => diagnostic.Rule == "CS-PARSE"
        );
    }
}
