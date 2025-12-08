using OpenFXC.Hlsl;
using Xunit;
using System;
using System.Linq;
using Xunit.Abstractions;

namespace OpenFXC.Hlsl.Tests;

public class SampleSmokeTests
{
    private readonly ITestOutputHelper _output;
    private static readonly string RepoRoot = TestPaths.FindRepoRoot();

    public SampleSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

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
        var strict = IsStrictSampleSweep();
        _output.WriteLine($"[sample] {path}");
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
        if (strict)
        {
            Assert.Empty(allLexDiagnostics);
            Assert.Empty(parseDiagnostics);
        }
    }

    [Theory]
    [MemberData(nameof(AllFxFiles))]
    public void AllFxFilesLexAndParse(string path)
    {
        var strict = IsStrictSampleSweep();
        _output.WriteLine($"[sample] {path}");
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
        if (strict)
        {
            Assert.Empty(allLexDiagnostics);
        }

        var (root, parseDiagnostics) = Parser.Parse(tokens, pre.Text.Length);
        Assert.Equal("CompilationUnit", root.Kind);
        Assert.Equal(0, root.Span.Start);
        Assert.Equal(pre.Text.Length, root.Span.End);
        if (strict)
        {
            Assert.Empty(parseDiagnostics);
        }
    }

    private static bool IsStrictSampleSweep() =>
        string.Equals(Environment.GetEnvironmentVariable("OPENFXC_STRICT_SAMPLES"), "1", StringComparison.Ordinal);
}
