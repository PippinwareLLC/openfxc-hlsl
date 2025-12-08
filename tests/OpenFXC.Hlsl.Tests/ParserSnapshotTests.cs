using System.Text.Json;
using System.Text.Json.Nodes;
using OpenFXC.Hlsl;
using Xunit;
using System.Linq;

namespace OpenFXC.Hlsl.Tests;

public class ParserSnapshotTests
{
    private const int FormatVersion = 1;
    private static readonly string RepoRoot = TestPaths.FindRepoRoot();
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [Fact]
    public void Sm1ParserSnapshotMatches()
    {
        AssertSnapshot("tests/fixtures/sm1-snapshot.hlsl", "tests/snapshots/sm1-snapshot.parse.json");
    }

    [Fact]
    public void Sm2ParserSnapshotMatches()
    {
        AssertSnapshot("tests/fixtures/sm2-snapshot.hlsl", "tests/snapshots/sm2-snapshot.parse.json");
    }

    [Fact]
    public void Sm4ParserSnapshotMatches()
    {
        AssertSnapshot("tests/fixtures/sm4-snapshot.hlsl", "tests/snapshots/sm4-snapshot.parse.json");
    }

    [Fact]
    public void Sm5ParserSnapshotMatches()
    {
        AssertSnapshot("tests/fixtures/sm5-snapshot.hlsl", "tests/snapshots/sm5-snapshot.parse.json");
    }

    [Fact]
    public void FxParserSnapshotMatches()
    {
        AssertSnapshot("tests/fixtures/parse-fx.hlsl", "tests/snapshots/parse-fx.parse.json");
    }

    private static void AssertSnapshot(string fixtureRelativePath, string snapshotRelativePath)
    {
        var fixturePath = Path.Combine(RepoRoot, fixtureRelativePath);
        var snapshotPath = Path.Combine(RepoRoot, snapshotRelativePath);

        var expectedJson = File.ReadAllText(snapshotPath);
        var result = BuildParseResult(fixturePath);

        var actualNode = JsonNode.Parse(JsonSerializer.Serialize(result, SerializerOptions))!;
        var expectedNode = JsonNode.Parse(expectedJson)!;

        Assert.True(JsonNode.DeepEquals(actualNode, expectedNode), $"Snapshot mismatch for {fixtureRelativePath}");
    }

    private static ParseResult BuildParseResult(string fixturePath)
    {
        var text = File.ReadAllText(fixturePath);
        var pre = Preprocessor.Preprocess(text, new PreprocessorOptions
        {
            FilePath = fixturePath,
            IncludeDirectories = new[] { Path.GetDirectoryName(fixturePath) ?? string.Empty }
        });

        var (tokens, lexDiagnostics) = HlslLexer.Lex(pre.Text);
        var (root, parseDiagnostics) = Parser.Parse(tokens, pre.Text.Length);
        var allDiagnostics = lexDiagnostics.Concat(parseDiagnostics).Concat(pre.Diagnostics).ToArray();

        return new ParseResult(
            FormatVersion,
            new SourceInfo(fixturePath, pre.Text.Length),
            root,
            tokens,
            allDiagnostics);
    }
}
