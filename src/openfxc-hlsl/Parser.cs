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
        if (IsTypeLike(startToken) && Peek(1) is { Kind: "Identifier" })
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

        Span endSpan;
        if (Match("CloseBrace"))
        {
            var endTok = Consume();
            endSpan = endTok.Span;
        }
        else
        {
            AddDiagnostic("HLSL1004", "Expected '}' to close block.", CurrentSpan());
            endSpan = new Span { Start = start, End = start };
        }

        return new AstNode
        {
            Id = NextId(),
            Kind = "Block",
            Span = new Span { Start = start, End = endSpan.End },
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

        if (Current!.Kind == "KeywordIf")
        {
            return ParseIfStatement();
        }

        if (Current!.Kind == "KeywordWhile")
        {
            return ParseWhileStatement();
        }

        if (Current!.Kind == "KeywordDo")
        {
            return ParseDoWhileStatement();
        }

        if (Current!.Kind == "KeywordFor")
        {
            return ParseForStatement();
        }

        if (Current!.Kind == "KeywordBreak" || Current!.Kind == "KeywordContinue")
        {
            var kind = Current!.Kind == "KeywordBreak" ? "BreakStatement" : "ContinueStatement";
            var start = Consume().Span.Start;
            ConsumeExpected("Semicolon");
            return new AstNode
            {
                Id = NextId(),
                Kind = kind,
                Span = new Span { Start = start, End = CurrentSpan().End },
                Children = Array.Empty<AstChild>()
            };
        }

        if (Current!.Kind == "KeywordDiscard")
        {
            var start = Consume().Span.Start;
            ConsumeExpected("Semicolon");
            return new AstNode
            {
                Id = NextId(),
                Kind = "DiscardStatement",
                Span = new Span { Start = start, End = CurrentSpan().End },
                Children = Array.Empty<AstChild>()
            };
        }

        // Local variable declaration heuristic inside blocks: type Identifier ...
        if (IsTypeLike(Current!) && Peek(1) is { Kind: "Identifier" } && Peek(2)?.Kind != "OpenParen")
        {
            return ParseVariableDeclaration();
        }

        return ParseExpressionStatement();
    }

    private AstNode ParseIfStatement()
    {
        var start = Consume().Span.Start; // if
        ConsumeExpected("OpenParen");
        var condition = ParseExpression();
        ConsumeExpected("CloseParen");
        var thenStmt = ParseStatement() ?? new AstNode { Id = NextId(), Kind = "EmptyStatement", Span = CurrentSpan(), Children = Array.Empty<AstChild>() };
        AstNode? elseStmt = null;
        if (Match("KeywordElse"))
        {
            Consume();
            elseStmt = ParseStatement();
        }

        var end = elseStmt?.Span.End ?? thenStmt.Span.End;
        var children = new List<AstChild>
        {
            new AstChild { Role = "condition", Node = condition ?? Leaf("Missing", new Token { Span = CurrentSpan() }) },
            new AstChild { Role = "then", Node = thenStmt }
        };
        if (elseStmt is not null)
        {
            children.Add(new AstChild { Role = "else", Node = elseStmt });
        }

        return new AstNode
        {
            Id = NextId(),
            Kind = "IfStatement",
            Span = new Span { Start = start, End = end },
            Children = children.ToArray()
        };
    }

    private AstNode ParseWhileStatement()
    {
        var start = Consume().Span.Start; // while
        ConsumeExpected("OpenParen");
        var condition = ParseExpression();
        ConsumeExpected("CloseParen");
        var body = ParseStatement() ?? new AstNode { Id = NextId(), Kind = "EmptyStatement", Span = CurrentSpan(), Children = Array.Empty<AstChild>() };
        var end = body.Span.End;
        return new AstNode
        {
            Id = NextId(),
            Kind = "WhileStatement",
            Span = new Span { Start = start, End = end },
            Children = new[]
            {
                new AstChild { Role = "condition", Node = condition ?? Leaf("Missing", new Token { Span = CurrentSpan() }) },
                new AstChild { Role = "body", Node = body }
            }
        };
    }

    private AstNode ParseDoWhileStatement()
    {
        var start = Consume().Span.Start; // do
        var body = ParseStatement() ?? new AstNode { Id = NextId(), Kind = "EmptyStatement", Span = CurrentSpan(), Children = Array.Empty<AstChild>() };
        ConsumeExpected("KeywordWhile");
        ConsumeExpected("OpenParen");
        var condition = ParseExpression();
        ConsumeExpected("CloseParen");
        ConsumeExpected("Semicolon");
        var end = CurrentSpan().End;
        return new AstNode
        {
            Id = NextId(),
            Kind = "DoWhileStatement",
            Span = new Span { Start = start, End = end },
            Children = new[]
            {
                new AstChild { Role = "body", Node = body },
                new AstChild { Role = "condition", Node = condition ?? Leaf("Missing", new Token { Span = CurrentSpan() }) }
            }
        };
    }

    private AstNode ParseForStatement()
    {
        var start = Consume().Span.Start; // for
        ConsumeExpected("OpenParen");

        AstNode? init = null;
        if (!Match("Semicolon"))
        {
            if (IsTypeLike(Current!) && Peek(1) is { Kind: "Identifier" } && Peek(2)?.Kind != "OpenParen")
            {
                init = ParseVariableDeclaration();
            }
            else
            {
                init = ParseExpressionStatement();
            }
        }
        else
        {
            Consume(); // ;
        }

        AstNode? condition = null;
        if (!Match("Semicolon"))
        {
            condition = ParseExpression();
        }
        ConsumeExpected("Semicolon");

        AstNode? increment = null;
        if (!Match("CloseParen"))
        {
            increment = ParseExpression();
        }
        ConsumeExpected("CloseParen");

        var body = ParseStatement() ?? new AstNode { Id = NextId(), Kind = "EmptyStatement", Span = CurrentSpan(), Children = Array.Empty<AstChild>() };

        var end = body.Span.End;
        var children = new List<AstChild>();
        if (init is not null) children.Add(new AstChild { Role = "initializer", Node = init });
        if (condition is not null) children.Add(new AstChild { Role = "condition", Node = condition });
        if (increment is not null) children.Add(new AstChild { Role = "increment", Node = increment });
        children.Add(new AstChild { Role = "body", Node = body });

        return new AstNode
        {
            Id = NextId(),
            Kind = "ForStatement",
            Span = new Span { Start = start, End = end },
            Children = children.ToArray()
        };
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

    private AstNode? ParseExpression(int precedence = 0)
    {
        var left = ParseUnary();
        if (left is null)
        {
            return null;
        }

        while (true)
        {
            var op = Current;
            if (op is null) break;
            var opPrec = GetPrecedence(op.Kind);
            if (opPrec < precedence) break;

            var associativity = op.Kind == "Equals" ? Precedence.Right : Precedence.Left;
            Consume();
            var right = ParseExpression(opPrec + (associativity == Precedence.Left ? 1 : 0));
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

    private AstNode? ParseUnary()
    {
        if (IsEnd) return null;
        var tok = Current!;
        if (tok.Kind is "Plus" or "Minus" or "Bang" or "Tilde")
        {
            Consume();
            var operand = ParseUnary();
            if (operand is null)
            {
                AddDiagnostic("HLSL1003", $"Expected expression after '{tok.Text}'.", CurrentSpan());
                return null;
            }

            return new AstNode
            {
                Id = NextId(),
                Kind = "UnaryExpression",
                Span = new Span { Start = tok.Span.Start, End = operand.Span.End },
                Children = new[]
                {
                    new AstChild { Role = "operator", Node = Leaf("Operator", tok) },
                    new AstChild { Role = "operand", Node = operand }
                }
            };
        }

        return ParsePrimary();
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

    private enum Precedence
    {
        Left,
        Right
    }

    private static int GetPrecedence(string kind) => kind switch
    {
        "Equals" => 1,
        "PipePipe" => 2,
        "AmpersandAmpersand" => 3,
        "Pipe" => 4,
        "Caret" => 5,
        "Ampersand" => 6,
        "EqualsEquals" or "BangEquals" => 7,
        "Less" or "LessEquals" or "Greater" or "GreaterEquals" => 8,
        "LessLess" or "GreaterGreater" => 9,
        "Plus" or "Minus" => 10,
        "Star" or "Slash" or "Percent" => 11,
        _ => -1
    };

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
