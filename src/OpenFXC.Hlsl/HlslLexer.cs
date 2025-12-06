using System.Linq;

namespace OpenFXC.Hlsl;

public static class HlslLexer
{
    private static readonly Dictionary<string, string> Keywords = new(StringComparer.Ordinal)
    {
        ["float"] = "KeywordFloat",
        ["float2"] = "KeywordFloat2",
        ["float3"] = "KeywordFloat3",
        ["float4"] = "KeywordFloat4",
        ["float2x2"] = "KeywordFloat2x2",
        ["float2x3"] = "KeywordFloat2x3",
        ["float2x4"] = "KeywordFloat2x4",
        ["float3x2"] = "KeywordFloat3x2",
        ["float3x3"] = "KeywordFloat3x3",
        ["float3x4"] = "KeywordFloat3x4",
        ["float4x2"] = "KeywordFloat4x2",
        ["float4x3"] = "KeywordFloat4x3",
        ["float4x4"] = "KeywordFloat4x4",
        ["void"] = "KeywordVoid",
        ["half"] = "KeywordHalf",
        ["int"] = "KeywordInt",
        ["uint"] = "KeywordUInt",
        ["dword"] = "KeywordDword",
        ["double"] = "KeywordDouble",
        ["bool"] = "KeywordBool",
        ["cbuffer"] = "KeywordCBuffer",
        ["tbuffer"] = "KeywordTBuffer",
        ["texture1d"] = "KeywordTexture1D",
        ["texture1darray"] = "KeywordTexture1DArray",
        ["texture2d"] = "KeywordTexture2D",
        ["texture2darray"] = "KeywordTexture2DArray",
        ["texture3d"] = "KeywordTexture3D",
        ["texturecube"] = "KeywordTextureCube",
        ["texturecubearray"] = "KeywordTextureCubeArray",
        ["structuredbuffer"] = "KeywordStructuredBuffer",
        ["rwstructuredbuffer"] = "KeywordRWStructuredBuffer",
        ["rwtexture1d"] = "KeywordRWTexture1D",
        ["rwtexture1darray"] = "KeywordRWTexture1DArray",
        ["rwtexture2d"] = "KeywordRWTexture2D",
        ["rwtexture2darray"] = "KeywordRWTexture2DArray",
        ["rwtexture3d"] = "KeywordRWTexture3D",
        ["rwbuffer"] = "KeywordRWBuffer",
        ["appendstructuredbuffer"] = "KeywordAppendStructuredBuffer",
        ["consumestructuredbuffer"] = "KeywordConsumeStructuredBuffer",
        ["byteaddressbuffer"] = "KeywordByteAddressBuffer",
        ["rwbyteaddressbuffer"] = "KeywordRWByteAddressBuffer",
        ["uniform"] = "KeywordUniform",
        ["const"] = "KeywordConst",
        ["static"] = "KeywordStatic",
        ["extern"] = "KeywordExtern",
        ["volatile"] = "KeywordVolatile",
        ["sampler"] = "KeywordSampler",
        ["sampler1d"] = "KeywordSampler1D",
        ["sampler2d"] = "KeywordSampler2D",
        ["sampler3d"] = "KeywordSampler3D",
        ["samplercube"] = "KeywordSamplerCUBE",
        ["texture"] = "KeywordTexture",
        ["sampler_state"] = "KeywordSamplerState",
        ["struct"] = "KeywordStruct",
        ["typedef"] = "KeywordTypedef",
        ["class"] = "KeywordClass",
        ["interface"] = "KeywordInterface",
        ["if"] = "KeywordIf",
        ["else"] = "KeywordElse",
        ["for"] = "KeywordFor",
        ["while"] = "KeywordWhile",
        ["do"] = "KeywordDo",
        ["break"] = "KeywordBreak",
        ["continue"] = "KeywordContinue",
        ["return"] = "KeywordReturn",
        ["discard"] = "KeywordDiscard",
        ["technique"] = "KeywordTechnique",
        ["pass"] = "KeywordPass",
        ["technique10"] = "KeywordTechnique10",
    };

    private const string DiagnosticUnknown = "HLSL0001";
    private const string DiagnosticUnterminatedComment = "HLSL0002";
    private const string DiagnosticUnterminatedString = "HLSL0003";

    public static (Token[] Tokens, Diagnostic[] Diagnostics) Lex(string text)
    {
        var tokens = new List<Token>();
        var diagnostics = new List<Diagnostic>();

        var reader = new TextReader(text);
        while (!reader.IsEnd)
        {
            var wasAtLineStart = reader.AtLineStart;
            var leading = ReadTrivia(reader, diagnostics, treatNewLinesAsTrivia: true);
            var lineStartAfterTrivia = (wasAtLineStart && leading.All(t => t.Kind is "Whitespace")) || leading.Any(t => t.Kind is "NewLine");
            if (reader.IsEnd)
            {
                break;
            }

            // Preprocessor at start of line (ignoring leading trivia on that line).
            if (lineStartAfterTrivia && reader.Current == '#')
            {
                tokens.Add(ReadPreprocessor(reader, leading, diagnostics));
                continue;
            }

            var c = reader.Current;
            if (IsIdentifierStart(c))
            {
                tokens.Add(ReadIdentifierOrKeyword(reader, leading, diagnostics));
            }
            else if (char.IsDigit(c) || (c == '.' && reader.Peek(1) is char p && char.IsDigit(p)))
            {
                tokens.Add(ReadNumber(reader, leading, diagnostics));
            }
            else if (c == '"')
            {
                tokens.Add(ReadStringLiteral(reader, leading, diagnostics));
            }
            else if (IsOperatorStart(c))
            {
                tokens.Add(ReadOperator(reader, leading, diagnostics));
            }
            else
            {
                var start = reader.Position;
                diagnostics.Add(new Diagnostic
                {
                    Id = DiagnosticUnknown,
                    Message = $"Unexpected character '{c}'.",
                    Span = new Span { Start = start, End = start + 1 }
                });
                reader.Advance();
            }
        }

        return (tokens.ToArray(), diagnostics.ToArray());
    }

    private static Token ReadStringLiteral(TextReader reader, List<Trivia> leading, List<Diagnostic> diagnostics)
    {
        var start = reader.Position;
        reader.Advance(); // opening quote
        var terminated = false;
        while (!reader.IsEnd)
        {
            var c = reader.Current;
            if (c == '\\')
            {
                // skip escaped character
                reader.Advance(2);
                continue;
            }
            if (c == '"')
            {
                reader.Advance(); // closing quote
                terminated = true;
                break;
            }
            // Allow newlines; stop if EOF
            reader.Advance();
        }

        if (!terminated)
        {
            diagnostics.Add(new Diagnostic
            {
                Id = DiagnosticUnterminatedString,
                Message = "Unterminated string literal.",
                Span = new Span { Start = start, End = reader.Position }
            });
        }

        var end = reader.Position;
        var trailing = ReadTrivia(reader, diagnostics, treatNewLinesAsTrivia: false);
        return new Token
        {
            Kind = "StringLiteral",
            Text = reader.Slice(start, end),
            Span = new Span { Start = start, End = end },
            LeadingTrivia = leading.ToArray(),
            TrailingTrivia = trailing.ToArray()
        };
    }

    private static Token ReadPreprocessor(TextReader reader, List<Trivia> leading, List<Diagnostic> diagnostics)
    {
        var start = reader.Position;
        reader.Advance(); // consume '#'
        while (!reader.IsEnd && !reader.IsNewLine(reader.Current))
        {
            reader.Advance();
        }

        var end = reader.Position;
        // Capture newline as trailing trivia to preserve line boundaries.
        var trailing = ReadTrivia(reader, diagnostics, treatNewLinesAsTrivia: true, stopAtTokenStart: false);

        return new Token
        {
            Kind = "PreprocessorDirective",
            Text = reader.Slice(start, end),
            Span = new Span { Start = start, End = end },
            LeadingTrivia = leading.ToArray(),
            TrailingTrivia = trailing.ToArray()
        };
    }

    private static Token ReadIdentifierOrKeyword(TextReader reader, List<Trivia> leading, List<Diagnostic> diagnostics)
    {
        var start = reader.Position;
        reader.Advance(); // consume first
        while (!reader.IsEnd && IsIdentifierPart(reader.Current))
        {
            reader.Advance();
        }

        var text = reader.Slice(start, reader.Position);
        var key = text.ToLowerInvariant();
        var kind = Keywords.TryGetValue(key, out var kw) ? kw : "Identifier";

        var end = reader.Position;
        var trailing = ReadTrivia(reader, diagnostics, treatNewLinesAsTrivia: false);

        return new Token
        {
            Kind = kind,
            Text = text,
            Span = new Span { Start = start, End = end },
            LeadingTrivia = leading.ToArray(),
            TrailingTrivia = trailing.ToArray()
        };
    }

    private static Token ReadNumber(TextReader reader, List<Trivia> leading, List<Diagnostic> diagnostics)
    {
        var start = reader.Position;

        if (reader.Current == '0' && (reader.Peek(1) is 'x' or 'X'))
        {
            reader.Advance(); // 0
            reader.Advance(); // x
            while (!reader.IsEnd && IsHexDigit(reader.Current))
            {
                reader.Advance();
            }
        }
        else
        {
            ReadDigits(reader);
            if (!reader.IsEnd && reader.Current == '.')
            {
                reader.Advance();
                ReadDigits(reader);
            }

            if (!reader.IsEnd && (reader.Current is 'e' or 'E'))
            {
                reader.Advance();
                if (!reader.IsEnd && (reader.Current is '+' or '-'))
                {
                    reader.Advance();
                }
                ReadDigits(reader);
            }

            if (!reader.IsEnd && (reader.Current is 'f' or 'F'))
            {
                reader.Advance();
            }
        }

        var end = reader.Position;
        var trailing = ReadTrivia(reader, diagnostics, treatNewLinesAsTrivia: false);

        return new Token
        {
            Kind = "NumericLiteral",
            Text = reader.Slice(start, end),
            Span = new Span { Start = start, End = end },
            LeadingTrivia = leading.ToArray(),
            TrailingTrivia = trailing.ToArray()
        };
    }

    private static Token ReadOperator(TextReader reader, List<Trivia> leading, List<Diagnostic> diagnostics)
    {
        var start = reader.Position;
        var three = reader.PeekThree();
        var two = reader.PeekTwo();
        var op = three switch
        {
            "<<=" or ">>=" => three,
            _ => two switch
            {
                "<<" or ">>" or "&&" or "||" or "==" or "!=" or "<=" or ">=" or "+=" or "-=" or "*=" or "/=" or "%=" or "&=" or "|=" or "^=" or "::" or "++" or "--"
                    => two,
                _ => reader.Current.ToString()
            }
        };

        reader.Advance(op.Length);
        var end = reader.Position;
        var trailing = ReadTrivia(reader, diagnostics, treatNewLinesAsTrivia: false);

        return new Token
        {
            Kind = OperatorKind(op),
            Text = op,
            Span = new Span { Start = start, End = end },
            LeadingTrivia = leading.ToArray(),
            TrailingTrivia = trailing.ToArray()
        };
    }

    private static string OperatorKind(string op) => op switch
    {
        "+" => "Plus",
        "-" => "Minus",
        "*" => "Star",
        "/" => "Slash",
        "%" => "Percent",
        "++" => "PlusPlus",
        "--" => "MinusMinus",
        "&&" => "AmpersandAmpersand",
        "||" => "PipePipe",
        "!" => "Bang",
        "&" => "Ampersand",
        "|" => "Pipe",
        "^" => "Caret",
        "~" => "Tilde",
        "<<" => "LessLess",
        ">>" => "GreaterGreater",
        "==" => "EqualsEquals",
        "!=" => "BangEquals",
        "<" => "Less",
        "<=" => "LessEquals",
        ">" => "Greater",
        ">=" => "GreaterEquals",
        "=" => "Equals",
        "+=" => "PlusEquals",
        "-=" => "MinusEquals",
        "*=" => "StarEquals",
        "/=" => "SlashEquals",
        "%=" => "PercentEquals",
        "&=" => "AmpersandEquals",
        "|=" => "PipeEquals",
        "^=" => "CaretEquals",
        "<<=" => "LessLessEquals",
        ">>=" => "GreaterGreaterEquals",
        "?" => "Question",
        ":" => "Colon",
        "::" => "ColonColon",
        "(" => "OpenParen",
        ")" => "CloseParen",
        "[" => "OpenBracket",
        "]" => "CloseBracket",
        "{" => "OpenBrace",
        "}" => "CloseBrace",
        "," => "Comma",
        ";" => "Semicolon",
        "." => "Dot",
        _ => "UnknownOperator"
    };

    private static List<Trivia> ReadTrivia(TextReader reader, List<Diagnostic> diagnostics, bool treatNewLinesAsTrivia, bool stopAtTokenStart = true)
    {
        var trivia = new List<Trivia>();
        while (!reader.IsEnd)
        {
            var c = reader.Current;
            if (char.IsWhiteSpace(c))
            {
                if (reader.IsNewLine(c))
                {
                    if (!treatNewLinesAsTrivia)
                    {
                        break;
                    }

                    trivia.Add(ReadNewLine(reader));
                    reader.MarkLineStart();
                }
                else
                {
                    trivia.Add(ReadWhitespace(reader));
                }
                continue;
            }

            if (c == '/' && reader.Peek(1) == '/')
            {
                trivia.Add(ReadSingleLineComment(reader));
                reader.MarkLineStart();
                continue;
            }

            if (c == '/' && reader.Peek(1) == '*')
            {
                trivia.Add(ReadMultiLineComment(reader, diagnostics));
                continue;
            }

            // Stop collecting trivia if we hit non-trivia.
            if (stopAtTokenStart)
            {
                break;
            }

            // Otherwise include as part of trailing run.
            break;
        }

        return trivia;
    }

    private static Trivia ReadWhitespace(TextReader reader)
    {
        var start = reader.Position;
        while (!reader.IsEnd && char.IsWhiteSpace(reader.Current) && !reader.IsNewLine(reader.Current))
        {
            reader.Advance();
        }

        return new Trivia
        {
            Kind = "Whitespace",
            Text = reader.Slice(start, reader.Position),
            Span = new Span { Start = start, End = reader.Position }
        };
    }

    private static Trivia ReadNewLine(TextReader reader)
    {
        var start = reader.Position;
        if (reader.Current == '\r' && reader.Peek(1) == '\n')
        {
            reader.Advance(2);
        }
        else
        {
            reader.Advance();
        }

        return new Trivia
        {
            Kind = "NewLine",
            Text = reader.Slice(start, reader.Position),
            Span = new Span { Start = start, End = reader.Position }
        };
    }

    private static Trivia ReadSingleLineComment(TextReader reader)
    {
        var start = reader.Position;
        reader.Advance(2); // //
        while (!reader.IsEnd && !reader.IsNewLine(reader.Current))
        {
            reader.Advance();
        }

        return new Trivia
        {
            Kind = "SingleLineComment",
            Text = reader.Slice(start, reader.Position),
            Span = new Span { Start = start, End = reader.Position }
        };
    }

    private static Trivia ReadMultiLineComment(TextReader reader, List<Diagnostic> diagnostics)
    {
        var start = reader.Position;
        reader.Advance(2); // /*
        while (!reader.IsEnd && !(reader.Current == '*' && reader.Peek(1) == '/'))
        {
            reader.Advance();
        }

        if (reader.IsEnd)
        {
            diagnostics.Add(new Diagnostic
            {
                Id = DiagnosticUnterminatedComment,
                Message = "Unterminated block comment.",
                Span = new Span { Start = start, End = reader.Position }
            });
        }
        else
        {
            reader.Advance(2); // */
        }

        return new Trivia
        {
            Kind = "MultiLineComment",
            Text = reader.Slice(start, reader.Position),
            Span = new Span { Start = start, End = reader.Position }
        };
    }

    private static void ReadDigits(TextReader reader)
    {
        while (!reader.IsEnd && char.IsDigit(reader.Current))
        {
            reader.Advance();
        }
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') ||
        (c >= 'a' && c <= 'f') ||
        (c >= 'A' && c <= 'F');

    private static bool IsOperatorStart(char c) => "+-*/%&|^~!<>=?:()[]{}.,;".Contains(c, StringComparison.Ordinal);

    private sealed class TextReader
    {
        private readonly string _text;
        public int Position { get; private set; }
        public bool IsEnd => Position >= _text.Length;
        public char Current => _text[Position];
        public bool AtLineStart { get; private set; } = true;

        public TextReader(string text)
        {
            _text = text ?? string.Empty;
        }

        public char Peek(int offset)
        {
            var idx = Position + offset;
            return idx >= 0 && idx < _text.Length ? _text[idx] : '\0';
        }

        public string PeekTwo()
        {
            if (IsEnd)
            {
                return string.Empty;
            }

            var remaining = Math.Min(2, _text.Length - Position);
            return _text.AsSpan(Position, remaining).ToString();
        }

        public string PeekThree()
        {
            if (IsEnd)
            {
                return string.Empty;
            }

            var remaining = Math.Min(3, _text.Length - Position);
            return _text.AsSpan(Position, remaining).ToString();
        }

        public void Advance(int count = 1)
        {
            Position = Math.Min(_text.Length, Position + count);
            AtLineStart = false;
        }

        public void MarkLineStart()
        {
            AtLineStart = true;
        }

        public bool IsNewLine(char c) => c is '\n' or '\r';

        public string Slice(int start, int end) => _text[start..end];
    }
}
