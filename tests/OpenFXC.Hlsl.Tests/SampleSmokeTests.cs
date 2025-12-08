using OpenFXC.Hlsl;
using Xunit;
using System;
using System.Linq;
using Xunit.Abstractions;
using System.Text.Json;

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

        var includeDirs = ResolveIncludeDirectories(path);
        var pre = Preprocessor.Preprocess(
            text,
            new PreprocessorOptions
            {
                FilePath = path,
                IncludeDirectories = includeDirs
            });

        var (tokens, lexDiagnostics) = HlslLexer.Lex(pre.Text);
        var mappedLexDiagnostics = pre.SourceMap.AttachOrigins(lexDiagnostics);
        var allLexDiagnostics = pre.Diagnostics.Concat(mappedLexDiagnostics).ToArray();
        Assert.NotEmpty(tokens);

        var (root, parseDiagnostics) = Parser.Parse(tokens, pre.Text.Length);
        var mappedParseDiagnostics = pre.SourceMap.AttachOrigins(parseDiagnostics);
        DumpDiagnostics(allLexDiagnostics.Concat(mappedParseDiagnostics));
        Assert.Equal("CompilationUnit", root.Kind);
        Assert.Equal(0, root.Span.Start);
        Assert.Equal(pre.Text.Length, root.Span.End);
        if (strict)
        {
            Assert.Empty(allLexDiagnostics);
            Assert.Empty(mappedParseDiagnostics);
        }
    }

    [Theory]
    [MemberData(nameof(AllFxFiles))]
    public void AllFxFilesLexAndParse(string path)
    {
        var strict = IsStrictSampleSweep();
        _output.WriteLine($"[sample] {path}");
        var text = File.ReadAllText(path);

        var includeDirs = ResolveIncludeDirectories(path);
        var pre = Preprocessor.Preprocess(
            text,
            new PreprocessorOptions
            {
                FilePath = path,
                IncludeDirectories = includeDirs
            });

        var (tokens, lexDiagnostics) = HlslLexer.Lex(pre.Text);
        var mappedLexDiagnostics = pre.SourceMap.AttachOrigins(lexDiagnostics);
        var allLexDiagnostics = pre.Diagnostics.Concat(mappedLexDiagnostics).ToArray();
        Assert.NotEmpty(tokens);
        if (strict)
        {
            Assert.Empty(allLexDiagnostics);
        }

        var (root, parseDiagnostics) = Parser.Parse(tokens, pre.Text.Length);
        var mappedParseDiagnostics = pre.SourceMap.AttachOrigins(parseDiagnostics);
        DumpDiagnostics(allLexDiagnostics.Concat(mappedParseDiagnostics));
        Assert.Equal("CompilationUnit", root.Kind);
        Assert.Equal(0, root.Span.Start);
        Assert.Equal(pre.Text.Length, root.Span.End);
        if (strict)
        {
            Assert.Empty(mappedParseDiagnostics);
        }
    }

    private static bool IsStrictSampleSweep() =>
        string.Equals(Environment.GetEnvironmentVariable("OPENFXC_STRICT_SAMPLES"), "1", StringComparison.Ordinal);

    private static string[] ResolveIncludeDirectories(string path)
    {
        var dirs = new List<string>();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            dirs.Add(dir);
        }

        var dxsdkRoot = Path.Combine(RepoRoot, "samples", "dxsdk");
        var cursor = dir;
        while (!string.IsNullOrEmpty(cursor) && cursor.StartsWith(dxsdkRoot, StringComparison.OrdinalIgnoreCase))
        {
            var configPath = Path.Combine(cursor, "includes.json");
            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            if (element.ValueKind == JsonValueKind.String)
                            {
                                var rel = element.GetString() ?? string.Empty;
                                if (!string.IsNullOrWhiteSpace(rel))
                                {
                                    var includeDir = Path.GetFullPath(Path.Combine(cursor, rel));
                                    dirs.Add(includeDir);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // ignore malformed include configs to keep sweeps running
                }
            }

            cursor = Path.GetDirectoryName(cursor);
        }

        return dirs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void DumpDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diag in diagnostics)
        {
            var origin = diag.Origin is null
                ? string.Empty
                : $" (origin: {diag.Origin.FileName}@{diag.Origin.Span.Start}-{diag.Origin.Span.End})";
            _output.WriteLine($"[diag] {diag.Id} {diag.Message} span {diag.Span.Start}-{diag.Span.End}{origin}");
        }
    }
}
