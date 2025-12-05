using System.Text.Json;
using System.Text.Json.Nodes;
using OpenFXC.Hlsl;
using Xunit;

namespace OpenFXC.Hlsl.Tests;

public class LexingTests
{
    private const int FormatVersion = 1;
    private static readonly string RepoRoot = TestPaths.FindRepoRoot();
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [Fact]
    public void Sm1SnapshotMatches()
    {
        var fixturePath = Path.Combine(RepoRoot, "tests", "fixtures", "sm1-snapshot.hlsl");
        var expectedPath = Path.Combine(RepoRoot, "tests", "snapshots", "sm1-snapshot.lex.json");

        var text = File.ReadAllText(fixturePath);
        var expectedJson = File.ReadAllText(expectedPath);

        var (tokens, diagnostics) = HlslLexer.Lex(text);
        var result = new LexResult(FormatVersion, new SourceInfo("sm1-snapshot.hlsl", text.Length), tokens, diagnostics);

        var actualNode = JsonNode.Parse(JsonSerializer.Serialize(result, SerializerOptions))!;
        var expectedNode = JsonNode.Parse(expectedJson)!;

        Assert.True(JsonNode.DeepEquals(actualNode, expectedNode), "Snapshot mismatch for sm1-snapshot.hlsl");
    }

    [Fact]
    public void UnterminatedBlockCommentProducesDiagnostic()
    {
        var fixturePath = Path.Combine(RepoRoot, "tests", "fixtures", "sm1-unterminated-comment.hlsl");
        var text = File.ReadAllText(fixturePath);

        var (_, diagnostics) = HlslLexer.Lex(text);

        Assert.Single(diagnostics);
        Assert.Equal("HLSL0002", diagnostics[0].Id);
    }

    [Fact]
    public void Sm2FixtureLexesWithoutDiagnostics()
    {
        var fixturePath = Path.Combine(RepoRoot, "tests", "fixtures", "basic-sm2.hlsl");
        var text = File.ReadAllText(fixturePath);

        var (tokens, diagnostics) = HlslLexer.Lex(text);

        Assert.NotEmpty(tokens);
        Assert.Empty(diagnostics);
        Assert.Equal("KeywordFloat4", tokens[0].Kind);
    }

    [Fact]
    public void Sm2SnapshotMatches()
    {
        var fixturePath = Path.Combine(RepoRoot, "tests", "fixtures", "sm2-snapshot.hlsl");
        var expectedPath = Path.Combine(RepoRoot, "tests", "snapshots", "sm2-snapshot.lex.json");

        var text = File.ReadAllText(fixturePath);
        var expectedJson = File.ReadAllText(expectedPath);

        var (tokens, diagnostics) = HlslLexer.Lex(text);
        var result = new LexResult(FormatVersion, new SourceInfo("sm2-snapshot.hlsl", text.Length), tokens, diagnostics);

        var actualNode = JsonNode.Parse(JsonSerializer.Serialize(result, SerializerOptions))!;
        var expectedNode = JsonNode.Parse(expectedJson)!;

        Assert.True(JsonNode.DeepEquals(actualNode, expectedNode), "Snapshot mismatch for sm2-snapshot.hlsl");
    }

    [Fact]
    public void Sm4SnapshotMatches()
    {
        var fixturePath = Path.Combine(RepoRoot, "tests", "fixtures", "sm4-snapshot.hlsl");
        var expectedPath = Path.Combine(RepoRoot, "tests", "snapshots", "sm4-snapshot.lex.json");

        var text = File.ReadAllText(fixturePath);
        var expectedJson = File.ReadAllText(expectedPath);

        var (tokens, diagnostics) = HlslLexer.Lex(text);
        var result = new LexResult(FormatVersion, new SourceInfo("sm4-snapshot.hlsl", text.Length), tokens, diagnostics);

        var actualNode = JsonNode.Parse(JsonSerializer.Serialize(result, SerializerOptions))!;
        var expectedNode = JsonNode.Parse(expectedJson)!;

        Assert.True(JsonNode.DeepEquals(actualNode, expectedNode), "Snapshot mismatch for sm4-snapshot.hlsl");
    }

    [Fact]
    public void FxSnapshotMatches()
    {
        var fixturePath = Path.Combine(RepoRoot, "samples", "dx9", "dec2002", "Samples", "Media", "EffectEdit", "Simple.fx");
        var expectedPath = Path.Combine(RepoRoot, "tests", "snapshots", "fx-simple.lex.json");

        var text = File.ReadAllText(fixturePath);
        var expectedJson = File.ReadAllText(expectedPath);

        var (tokens, diagnostics) = HlslLexer.Lex(text);
        var result = new LexResult(FormatVersion, new SourceInfo("Simple.fx", text.Length), tokens, diagnostics);

        var actualNode = JsonNode.Parse(JsonSerializer.Serialize(result, SerializerOptions))!;
        var expectedNode = JsonNode.Parse(expectedJson)!;

        Assert.True(JsonNode.DeepEquals(actualNode, expectedNode), "Snapshot mismatch for fx-simple");
    }

}
