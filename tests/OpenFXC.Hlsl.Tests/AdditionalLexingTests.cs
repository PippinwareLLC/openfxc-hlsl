using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenFXC.Hlsl;
using Xunit;

namespace OpenFXC.Hlsl.Tests;

public class AdditionalLexingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [Fact]
    public void OperatorsAreRecognized()
    {
        var text = "+ - * / % ++ -- && || ! & | ^ ~ << >> == != < <= > >= = += -= *= /= %= &= |= ^= <<= >>= ? : :: ( ) [ ] { } , ; .";
        var (tokens, diagnostics) = HlslLexer.Lex(text);

        Assert.Empty(diagnostics);
        var kinds = tokens.Select(t => t.Kind).ToHashSet();
        Assert.Contains("Plus", kinds);
        Assert.Contains("MinusMinus", kinds);
        Assert.Contains("AmpersandAmpersand", kinds);
        Assert.Contains("PipePipe", kinds);
        Assert.Contains("LessLessEquals", kinds);
        Assert.Contains("ColonColon", kinds);
        Assert.Contains("Dot", kinds);
    }

    [Fact]
    public void NumericLiteralsCoverHexAndFloat()
    {
        var text = "0xFF 1.5f 2e+3";
        var (tokens, diagnostics) = HlslLexer.Lex(text);

        Assert.Empty(diagnostics);
        Assert.Equal(3, tokens.Length);
        Assert.All(tokens, t => Assert.Equal("NumericLiteral", t.Kind));
        Assert.Equal("0xFF", tokens[0].Text);
        Assert.Equal("1.5f", tokens[1].Text);
        Assert.Equal("2e+3", tokens[2].Text);
    }

    [Fact]
    public void PreprocessorIsCapturedWithTrailingNewline()
    {
        var text = "#define FOO 1\nfloat4 main() { return 0; }";
        var (tokens, diagnostics) = HlslLexer.Lex(text);

        Assert.Empty(diagnostics);
        Assert.True(tokens.Length > 1);
        Assert.Equal("PreprocessorDirective", tokens[0].Kind);
        Assert.Contains(tokens[0].TrailingTrivia, t => t.Kind == "NewLine");
    }

    [Fact]
    public void SamplerVariantsLex()
    {
        var text = "sampler1D s1; sampler3D s3; samplerCUBE sCube;";
        var (tokens, diagnostics) = HlslLexer.Lex(text);

        Assert.Empty(diagnostics);
        Assert.Contains(tokens, t => t.Kind == "KeywordSampler1D");
        Assert.Contains(tokens, t => t.Kind == "KeywordSampler3D");
        Assert.Contains(tokens, t => t.Kind == "KeywordSamplerCUBE");
    }

    [Fact]
    public void UnexpectedCharacterProducesDiagnostic()
    {
        var text = "float4 main() { @ }";
        var (_, diagnostics) = HlslLexer.Lex(text);

        Assert.Single(diagnostics);
        var diag = diagnostics[0];
        Assert.Equal("HLSL0001", diag.Id);
        Assert.True(diag.Span.Start >= 0);
        Assert.True(diag.Span.End > diag.Span.Start);
    }

    [Fact]
    public void SemanticsAndRegistersLexAsIdentifiers()
    {
        var text = "float4 main(float4 pos : POSITION0, float2 uv : TEXCOORD1) : COLOR0 { return pos; }\nfloat4 texVal : register(s1);";
        var (tokens, diagnostics) = HlslLexer.Lex(text);

        Assert.Empty(diagnostics);
        Assert.Contains(tokens, t => t.Text == "POSITION0");
        Assert.Contains(tokens, t => t.Text == "TEXCOORD1");
        Assert.Contains(tokens, t => t.Text == "COLOR0");
        Assert.Contains(tokens, t => t.Text == "register");
        Assert.Contains(tokens, t => t.Text == "s1");
    }

    [Fact]
    public void Sm4ResourceKeywordsLex()
    {
        var text = "cbuffer tbuffer Texture2D Texture2DArray Texture3D TextureCube StructuredBuffer RWStructuredBuffer RWTexture2D RWTexture3D RWBuffer AppendStructuredBuffer ConsumeStructuredBuffer ByteAddressBuffer RWByteAddressBuffer class interface";
        var (tokens, diagnostics) = HlslLexer.Lex(text);

        Assert.Empty(diagnostics);
        var kinds = tokens.Select(t => t.Kind).ToHashSet();
        Assert.Contains("KeywordCBuffer", kinds);
        Assert.Contains("KeywordTBuffer", kinds);
        Assert.Contains("KeywordTexture2D", kinds);
        Assert.Contains("KeywordTexture3D", kinds);
        Assert.Contains("KeywordStructuredBuffer", kinds);
        Assert.Contains("KeywordRWStructuredBuffer", kinds);
        Assert.Contains("KeywordRWTexture3D", kinds);
        Assert.Contains("KeywordRWBuffer", kinds);
        Assert.Contains("KeywordAppendStructuredBuffer", kinds);
        Assert.Contains("KeywordConsumeStructuredBuffer", kinds);
        Assert.Contains("KeywordByteAddressBuffer", kinds);
        Assert.Contains("KeywordRWByteAddressBuffer", kinds);
        Assert.Contains("KeywordClass", kinds);
        Assert.Contains("KeywordInterface", kinds);
    }

    [Fact]
    public void FxKeywordsLex()
    {
        var text = "technique technique10 pass CompileShader SetPixelShader SetVertexShader";
        var (tokens, diagnostics) = HlslLexer.Lex(text);

        Assert.Empty(diagnostics);
        var kinds = tokens.Select(t => t.Text).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("technique", kinds);
        Assert.Contains("technique10", kinds);
        Assert.Contains("pass", kinds);
        Assert.Contains("CompileShader", kinds);
        Assert.Contains("SetPixelShader", kinds);
        Assert.Contains("SetVertexShader", kinds);
    }

    [Fact]
    public void GlowSampleSnapshotMatches()
    {
        var repoRoot = TestPaths.FindRepoRoot();
        var fixturePath = Path.Combine(repoRoot, "tests", "snapshots", "sm1-glow.lex.json");
        var expectedJson = File.ReadAllText(fixturePath);

        var glowPath = Path.Combine(repoRoot, "samples", "dxsdk", "dx9sdk", "Samples", "Media", "EffectEdit", "Glow.fx");
        var glowText = File.ReadAllText(glowPath);

        var (tokens, diagnostics) = HlslLexer.Lex(glowText);
        var result = new LexResult(1, new SourceInfo("Glow.fx", glowText.Length), tokens, diagnostics);

        var actualJson = JsonSerializer.Serialize(result, JsonOptions);
        Assert.Equal(
            NormalizeJson(expectedJson),
            NormalizeJson(actualJson));
    }

    private static string NormalizeJson(string json)
    {
        var node = JsonSerializer.Deserialize<JsonNode>(json);
        return JsonSerializer.Serialize(node, JsonOptions);
    }
}
