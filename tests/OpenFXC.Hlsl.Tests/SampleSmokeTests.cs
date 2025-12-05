using OpenFXC.Hlsl;
using Xunit;
using System.Linq;

namespace OpenFXC.Hlsl.Tests;

public class SampleSmokeTests
{
    private static readonly string RepoRoot = TestPaths.FindRepoRoot();

    public static IEnumerable<object[]> SampleFiles =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot, "samples"), "*.fx", SearchOption.AllDirectories)
            .Take(5)
            .Select(p => new object[] { p });

    public static IEnumerable<object[]> AllFxFiles =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot, "samples"), "*.fx", SearchOption.AllDirectories)
            .Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void LexAndParseSamplesProduceCompilationUnit(string path)
    {
        var text = File.ReadAllText(path);

        var (tokens, lexDiagnostics) = HlslLexer.Lex(text);
        Assert.NotEmpty(tokens);
        Assert.Empty(lexDiagnostics);

        var (root, parseDiagnostics) = Parser.Parse(tokens, text.Length);
        Assert.Equal("CompilationUnit", root.Kind);
        Assert.Equal(0, root.Span.Start);
        Assert.Equal(text.Length, root.Span.End);

        // Parser diagnostics may exist for unsupported FX syntax, but tree must still be produced.
        Assert.NotNull(parseDiagnostics);
    }

    [Theory]
    [MemberData(nameof(AllFxFiles))]
    public void AllFxFilesLexAndParse(string path)
    {
        var text = File.ReadAllText(path);

        var (tokens, lexDiagnostics) = HlslLexer.Lex(text);
        Assert.NotEmpty(tokens);
        Assert.NotNull(lexDiagnostics);

        var (root, parseDiagnostics) = Parser.Parse(tokens, text.Length);
        Assert.Equal("CompilationUnit", root.Kind);
        Assert.Equal(0, root.Span.Start);
        Assert.Equal(text.Length, root.Span.End);
        Assert.NotNull(parseDiagnostics);
    }
}
