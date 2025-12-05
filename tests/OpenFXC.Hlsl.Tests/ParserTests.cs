using OpenFXC.Hlsl;
using Xunit;

namespace OpenFXC.Hlsl.Tests;

public class ParserTests
{
    private const int FormatVersion = 1;
    private static readonly string RepoRoot = TestPaths.FindRepoRoot();

    [Fact]
    public void ParseBuildsCompilationUnitWithSpans()
    {
        var path = Path.Combine(RepoRoot, "tests", "fixtures", "basic-sm2.hlsl");
        var text = File.ReadAllText(path);
        var (tokens, lexDiagnostics) = HlslLexer.Lex(text);
        var (root, parseDiagnostics) = Parser.Parse(tokens, text.Length);

        Assert.Equal("CompilationUnit", root.Kind);
        Assert.Equal(0, root.Span.Start);
        Assert.Equal(text.Length, root.Span.End);
        Assert.Empty(lexDiagnostics);
        Assert.Empty(parseDiagnostics);
        Assert.NotEmpty(root.Children);
    }

    [Fact]
    public void ParseRecoversMissingSemicolon()
    {
        var path = Path.Combine(RepoRoot, "tests", "fixtures", "parse-missing-semicolon.hlsl");
        var text = File.ReadAllText(path);
        var (tokens, _) = HlslLexer.Lex(text);
        var (_, parseDiagnostics) = Parser.Parse(tokens, text.Length);

        Assert.NotEmpty(parseDiagnostics);
        Assert.Contains(parseDiagnostics, d => d.Id == "HLSL1002");
    }

    [Fact]
    public void ParseMissingBraceProducesDiagnostic()
    {
        var path = Path.Combine(RepoRoot, "tests", "fixtures", "parse-missing-brace.hlsl");
        var text = File.ReadAllText(path);
        var (tokens, _) = HlslLexer.Lex(text);
        var (_, parseDiagnostics) = Parser.Parse(tokens, text.Length);

        Assert.Contains(parseDiagnostics, d => d.Id == "HLSL1004");
    }

    [Fact]
    public void ParseStatementsAndExpressions()
    {
        var path = Path.Combine(RepoRoot, "tests", "fixtures", "parse-statements.hlsl");
        var text = File.ReadAllText(path);
        var (tokens, lexDiagnostics) = HlslLexer.Lex(text);
        var (root, parseDiagnostics) = Parser.Parse(tokens, text.Length);

        Assert.Empty(lexDiagnostics);
        Assert.Empty(parseDiagnostics);
        Assert.Equal("CompilationUnit", root.Kind);
        Assert.True(root.Children.Length > 0);
    }

    [Fact]
    public void ParseSamplerStateAndSampler()
    {
        var path = Path.Combine(RepoRoot, "tests", "fixtures", "sm1-sampler.hlsl");
        var text = File.ReadAllText(path);
        var (tokens, lexDiagnostics) = HlslLexer.Lex(text);
        var (root, parseDiagnostics) = Parser.Parse(tokens, text.Length);

        Assert.Empty(lexDiagnostics);
        Assert.Empty(parseDiagnostics);
        Assert.Equal("CompilationUnit", root.Kind);
    }

    [Fact]
    public void ParseCBuffer()
    {
        var path = Path.Combine(RepoRoot, "tests", "fixtures", "sm4-snapshot.hlsl");
        var text = File.ReadAllText(path);
        var (tokens, lexDiagnostics) = HlslLexer.Lex(text);
        var (_, parseDiagnostics) = Parser.Parse(tokens, text.Length);

        Assert.Empty(lexDiagnostics);
        Assert.Empty(parseDiagnostics);
    }

    [Fact]
    public void ParseStructsAndArrays()
    {
        var path = Path.Combine(RepoRoot, "tests", "fixtures", "parse-structs.hlsl");
        var text = File.ReadAllText(path);
        var (tokens, lexDiagnostics) = HlslLexer.Lex(text);
        var (_, parseDiagnostics) = Parser.Parse(tokens, text.Length);

        Assert.Empty(lexDiagnostics);
        Assert.Empty(parseDiagnostics);
    }

    [Fact]
    public void ParseTypedefsClassesInterfacesAndBindings()
    {
        var path = Path.Combine(RepoRoot, "tests", "fixtures", "parse-m5.hlsl");
        var text = File.ReadAllText(path);
        var (tokens, lexDiagnostics) = HlslLexer.Lex(text);
        var (_, parseDiagnostics) = Parser.Parse(tokens, text.Length);

        Assert.Empty(lexDiagnostics);
        Assert.Empty(parseDiagnostics);
    }
}
