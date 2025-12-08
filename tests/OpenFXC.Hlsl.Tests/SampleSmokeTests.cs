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

        var pre = Preprocessor.Preprocess(
            text,
            new PreprocessorOptions
            {
                FilePath = path,
                IncludeDirectories = new[] { Path.GetDirectoryName(path) ?? string.Empty }
            });

        var (tokens, lexDiagnostics) = HlslLexer.Lex(pre.Text);
        var allLexDiagnostics = pre.Diagnostics.Concat(lexDiagnostics).ToArray();
        Assert.NotEmpty(tokens);

        var (root, parseDiagnostics) = Parser.Parse(tokens, pre.Text.Length);
        Assert.Equal("CompilationUnit", root.Kind);
        Assert.Equal(0, root.Span.Start);
        Assert.Equal(pre.Text.Length, root.Span.End);

        // Parser diagnostics may exist for unsupported FX syntax, but tree must still be produced.
        Assert.NotNull(parseDiagnostics);
        Assert.NotNull(allLexDiagnostics);
    }

    [Theory]
    [MemberData(nameof(AllFxFiles))]
    public void AllFxFilesLexAndParse(string path)
    {
        var text = File.ReadAllText(path);

        var pre = Preprocessor.Preprocess(
            text,
            new PreprocessorOptions
            {
                FilePath = path,
                IncludeDirectories = new[] { Path.GetDirectoryName(path) ?? string.Empty }
            });

        var (tokens, lexDiagnostics) = HlslLexer.Lex(pre.Text);
        var allLexDiagnostics = pre.Diagnostics.Concat(lexDiagnostics).ToArray();
        Assert.NotEmpty(tokens);
        Assert.NotNull(allLexDiagnostics);

        var (root, parseDiagnostics) = Parser.Parse(tokens, pre.Text.Length);
        Assert.Equal("CompilationUnit", root.Kind);
        Assert.Equal(0, root.Span.Start);
        Assert.Equal(pre.Text.Length, root.Span.End);
        Assert.NotNull(parseDiagnostics);
    }
}
