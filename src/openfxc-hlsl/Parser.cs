using System.Text;

namespace OpenFXC.Hlsl;

internal sealed class Parser
{
    private readonly Token[] _tokens;
    private readonly int _length;
    private int _position;
    private int _nextNodeId = 1;
    private readonly List<Diagnostic> _diagnostics = new();

    private Parser(Token[] tokens, int length)
    {
        _tokens = tokens;
        _length = length;
    }

    public static (AstNode Root, Diagnostic[] Diagnostics) Parse(Token[] tokens, int length)
    {
        var parser = new Parser(tokens, length);
        var root = parser.ParseCompilationUnit();
        return (root, parser._diagnostics.ToArray());
    }

    private AstNode ParseCompilationUnit()
    {
        var start = 0;
        var children = new List<AstChild>();

        while (!IsEnd)
        {
            var decl = ParseDeclaration();
            if (decl is not null)
            {
                children.Add(new AstChild { Role = "declaration", Node = decl });
            }
            else
            {
                // Skip one token to avoid infinite loop.
                _position++;
            }
        }

        var end = _length;
        return new AstNode
        {
            Id = NextId(),
            Kind = "CompilationUnit",
            Span = new Span { Start = start, End = end },
            Children = children.ToArray()
        };
    }

    private AstNode? ParseDeclaration()
    {
        var startToken = Current;
        if (startToken is null)
        {
            return null;
        }

        // Heuristic: type identifier ... if followed by "(" treat as function, else variable.
        if (IsTypeLike(startToken) && Peek(1) is { Kind: "Identifier" } id)
        {
            if (Peek(2) is { Kind: "OpenParen" })
            {
                return ParseFunction();
            }

            return ParseVariableDeclaration();
        }

        // Fallback: expression statement at top level.
        return ParseExpressionStatement();
    }

    private AstNode ParseFunction()
    {
        var start = Current!.Span.Start;
        var children = new List<AstChild>();

        // return type
        var returnType = Consume();
        children.Add(new AstChild { Role = "type", Node = Leaf("Type", returnType) });

        var name = Consume(); // identifier
        children.Add(new AstChild { Role = "identifier", Node = Leaf("Identifier", name) });

        ConsumeExpected("OpenParen");
        // Parameters: consume until ')'
        while (!IsEnd && Current!.Kind != "CloseParen")
        {
            Consume();
        }
        ConsumeExpected("CloseParen");

        // Optional return semantics colon IDENT
        if (Match("Colon"))
        {
            var sem = Consume();
            if (Current is not null)
            {
                var semIdent = Consume();
                children.Add(new AstChild { Role = "semantic", Node = Leaf("Semantic", semIdent) });
            }
        }

        AstNode body;
        if (Match("OpenBrace"))
        {
            body = ParseBlock();
        }
        else
        {
            AddDiagnostic("HLSL1002", "Expected '{' to start function body.", CurrentSpan());
            body = new AstNode
            {
                Id = NextId(),
                Kind = "Block",
                Span = CurrentSpan(),
                Children = Array.Empty<AstChild>()
            };
        }

        var end = body.Span.End;
        children.Add(new AstChild { Role = "body", Node = body });

        return new AstNode
        {
            Id = NextId(),
            Kind = "FunctionDeclaration",
            Span = new Span { Start = start, End = end },
            Children = children.ToArray()
        };
    }

    private AstNode ParseVariableDeclaration()
    {
        var start = Current!.Span.Start;
        var children = new List<AstChild>();

        var typeTok = Consume();
        children.Add(new AstChild { Role = "type", Node = Leaf("Type", typeTok) });

        var identTok = Consume();
        children.Add(new AstChild { Role = "identifier", Node = Leaf("Identifier", identTok) });

        // Optional initializer: = expression
        if (Match("Equals"))
        {
            var expr = ParseExpression();
            if (expr is not null)
            {
                children.Add(new AstChild { Role = "initializer", Node = expr });
            }
        }

        ConsumeExpected("Semicolon");

        var end = children.Last().Node.Span.End;
        return new AstNode
        {
            Id = NextId(),
            Kind = "VariableDeclaration",
            Span = new Span { Start = start, End = end },
            Children = children.ToArray()
        };
    }

    private AstNode ParseBlock()
    {
        var start = ConsumeExpected("OpenBrace").Span.Start;
        var statements = new List<AstChild>();

        while (!IsEnd && Current!.Kind != "CloseBrace")
        {
            var stmt = ParseStatement();
            if (stmt is not null)
            {
                statements.Add(new AstChild { Role = "statement", Node = stmt });
            }
            else
            {
                // recovery: skip to semicolon or brace
                SkipToRecoveryPoint();
            }
        }

        var endTok = ConsumeExpected("CloseBrace");
        var end = endTok.Span.End;

        return new AstNode
        {
            Id = NextId(),
            Kind = "Block",
            Span = new Span { Start = start, End = end },
            Children = statements.ToArray()
        };
    }

    private AstNode? ParseStatement()
    {
        if (IsEnd) return null;

        if (Current!.Kind == "KeywordReturn")
        {
            return ParseReturnStatement();
        }

        // Local variable declaration heuristic inside blocks: type Identifier ...
        if (IsTypeLike(Current!) && Peek(1) is { Kind: "Identifier" } && Peek(2)?.Kind != "OpenParen")
        {
            return ParseVariableDeclaration();
        }

        return ParseExpressionStatement();
    }

    private AstNode ParseReturnStatement()
    {
        var start = Consume().Span.Start; // return
        var expr = ParseExpression();
        ConsumeExpected("Semicolon");
        var end = expr?.Span.End ?? CurrentSpan().End;

        var children = new List<AstChild>();
        if (expr is not null)
        {
            children.Add(new AstChild { Role = "expression", Node = expr });
        }

        return new AstNode
        {
            Id = NextId(),
            Kind = "ReturnStatement",
            Span = new Span { Start = start, End = end },
            Children = children.ToArray()
        };
    }

    private AstNode ParseExpressionStatement()
    {
        var expr = ParseExpression();
        ConsumeExpected("Semicolon");
        var span = expr?.Span ?? CurrentSpan();
        return new AstNode
        {
            Id = NextId(),
            Kind = "ExpressionStatement",
            Span = span,
            Children = expr is null ? Array.Empty<AstChild>() : new[] { new AstChild { Role = "expression", Node = expr } }
        };
    }

    private AstNode? ParseExpression()
    {
        var left = ParsePrimary();
        if (left is null)
        {
            return null;
        }

        while (!IsEnd && IsBinaryOperator(Current!))
        {
            var op = Consume();
            var right = ParsePrimary();
            if (right is null)
            {
                AddDiagnostic("HLSL1003", $"Expected expression after '{op.Text}'.", CurrentSpan());
                break;
            }

            left = new AstNode
            {
                Id = NextId(),
                Kind = "BinaryExpression",
                Span = new Span { Start = left.Span.Start, End = right.Span.End },
                Children = new[]
                {
                    new AstChild { Role = "left", Node = left },
                    new AstChild { Role = "operator", Node = Leaf("Operator", op) },
                    new AstChild { Role = "right", Node = right }
                }
            };
        }

        return left;
    }

    private AstNode? ParsePrimary()
    {
        if (IsEnd) return null;
        var tok = Current!;
        switch (tok.Kind)
        {
            case "Identifier":
            case var k when k.StartsWith("Keyword", StringComparison.Ordinal):
                Consume();
                return Leaf("Identifier", tok);
            case "NumericLiteral":
                Consume();
                return Leaf("Literal", tok);
            case "OpenParen":
                Consume();
                var expr = ParseExpression();
                ConsumeExpected("CloseParen");
                return expr;
            default:
                AddDiagnostic("HLSL1001", $"Unexpected token '{tok.Text}'.", tok.Span);
                Consume();
                return null;
        }
    }

    private bool IsTypeLike(Token token) =>
        token.Kind.StartsWith("Keyword", StringComparison.Ordinal) || token.Kind == "Identifier";

    private bool IsBinaryOperator(Token token) =>
        token.Kind is "Plus" or "Minus" or "Star" or "Slash" or "Percent" or "Equals";

    private Token Consume()
    {
        var tok = _tokens[_position];
        _position++;
        return tok;
    }

    private Token ConsumeExpected(string kind)
    {
        if (Current?.Kind == kind)
        {
            return Consume();
        }

        AddDiagnostic("HLSL1002", $"Expected '{kind}'.", CurrentSpan());
        return new Token
        {
            Kind = kind,
            Text = string.Empty,
            Span = CurrentSpan(),
            LeadingTrivia = Array.Empty<Trivia>(),
            TrailingTrivia = Array.Empty<Trivia>()
        };
    }

    private void SkipToRecoveryPoint()
    {
        while (!IsEnd && Current!.Kind != "Semicolon" && Current!.Kind != "CloseBrace")
        {
            _position++;
        }

        if (Match("Semicolon"))
        {
            Consume();
        }
    }

    private bool Match(string kind) => Current?.Kind == kind;

    private Token? Current => _position < _tokens.Length ? _tokens[_position] : null;

    private Token? Peek(int offset) => (_position + offset) < _tokens.Length ? _tokens[_position + offset] : null;

    private bool IsEnd => _position >= _tokens.Length;

    private int NextId() => _nextNodeId++;

    private Span CurrentSpan() => Current?.Span ?? new Span { Start = _length, End = _length };

    private AstNode Leaf(string kind, Token token) => new()
    {
        Id = NextId(),
        Kind = kind,
        Span = token.Span,
        Children = Array.Empty<AstChild>()
    };

    private void AddDiagnostic(string id, string message, Span span)
    {
        _diagnostics.Add(new Diagnostic
        {
            Id = id,
            Message = message,
            Span = span
        });
    }
}
