using System.Text.Json.Serialization;

namespace OpenFXC.Hlsl;

public sealed record SourceInfo(
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("length")] int Length);

public sealed record Span
{
    [JsonPropertyName("start")]
    public int Start { get; init; }

    [JsonPropertyName("end")]
    public int End { get; init; }
}

public sealed record Trivia
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("span")]
    public Span Span { get; init; } = new();
}

public sealed record Token
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("span")]
    public Span Span { get; init; } = new();

    [JsonPropertyName("leadingTrivia")]
    public Trivia[] LeadingTrivia { get; init; } = Array.Empty<Trivia>();

    [JsonPropertyName("trailingTrivia")]
    public Trivia[] TrailingTrivia { get; init; } = Array.Empty<Trivia>();
}

public sealed record Diagnostic
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("span")]
    public Span Span { get; init; } = new();
}

public sealed record LexResult(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("source")] SourceInfo Source,
    [property: JsonPropertyName("tokens")] Token[] Tokens,
    [property: JsonPropertyName("diagnostics")] Diagnostic[] Diagnostics);

public sealed record AstChild
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("node")]
    public AstNode Node { get; init; } = new();
}

public sealed record AstNode
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("span")]
    public Span Span { get; init; } = new();

    [JsonPropertyName("children")]
    public AstChild[] Children { get; init; } = Array.Empty<AstChild>();
}

public sealed record ParseResult(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("source")] SourceInfo Source,
    [property: JsonPropertyName("root")] AstNode Root,
    [property: JsonPropertyName("tokens")] Token[] Tokens,
    [property: JsonPropertyName("diagnostics")] Diagnostic[] Diagnostics);
