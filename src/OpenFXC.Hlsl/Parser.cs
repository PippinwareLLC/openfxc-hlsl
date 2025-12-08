using System.Text;
using System.Linq;

namespace OpenFXC.Hlsl;

public sealed class Parser
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
        var attributes = ParseAttributes();
        var startToken = Current;
        if (startToken is null)
        {
            return null;
        }

        AstNode? decl = null;

        if (startToken.Kind == "KeywordTypedef")
        {
            decl = ParseTypedef();
        }
        else if (startToken.Kind is "KeywordStruct" or "KeywordClass" or "KeywordInterface")
        {
            decl = ParseStructLike(startToken.Kind);
        }
        else if (startToken.Kind is "KeywordTechnique" or "KeywordTechnique10" or "KeywordTechnique11")
        {
            decl = ParseTechnique(startToken.Kind);
        }
        else if (startToken.Kind is "KeywordCBuffer" or "KeywordTBuffer")
        {
            decl = ParseCBuffer();
        }
        else if (startToken.Kind == "KeywordSamplerState")
        {
            decl = ParseSamplerState();
        }
        var isFx10StateKeyword = startToken.Kind is "KeywordDepthStencilState" or "KeywordBlendState" or "KeywordRasterizerState" or "KeywordSamplerState10";
        if (decl is null && isFx10StateKeyword && Peek(1) is { Kind: "Identifier" } && Peek(2) is { Kind: "OpenBrace" })
        {
            decl = ParseFx10StateObject(startToken.Kind);
        }

        if (decl is null)
        {
            var typeIndex = SkipModifiersFrom(_position);
            var typeToken = typeIndex < _tokens.Length ? _tokens[typeIndex] : null;

            if (typeToken is not null && IsTypeLike(typeToken))
            {
                if (LooksLikeFunctionSignature(typeIndex))
                {
                    decl = ParseFunction();
                }
                else
                {
                    var nextIndex = typeIndex + 1;
                    var nextToken = PeekAbsolute(nextIndex);
                    if (nextToken is { Kind: "Identifier" } || nextToken is { Kind: "Less" })
                    {
                        decl = ParseVariableDeclaration();
                    }
                }
            }
            else
            {
                // Fallback: expression statement at top level.
                decl = ParseExpressionStatement();
            }
        }

        return WrapDeclarationAttributes(attributes, decl);
    }

    private AstNode ParseFunction() => ParseFunctionLike(allowSignatureOnly: false);

    private AstNode ParseFunctionLike(bool allowSignatureOnly)
    {
        var start = Current!.Span.Start;
        var children = new List<AstChild>();

        var modifiers = new List<Token>();
        while (!IsEnd && IsModifier(Current!))
        {
            modifiers.Add(Consume());
        }

        var returnType = Consume();
        AstNode typeNode = Leaf("Type", returnType);
        if (modifiers.Count > 0)
        {
            typeNode = new AstNode
            {
                Id = NextId(),
                Kind = "Type",
                Span = new Span { Start = modifiers[0].Span.Start, End = returnType.Span.End },
                Children = modifiers.Select(m => new AstChild { Role = "modifier", Node = Leaf("Modifier", m) }).ToArray()
            };
        }
        children.Add(new AstChild { Role = "type", Node = typeNode });

        if (Match("Less"))
        {
            ConsumeTemplateArguments();
        }

        Token name;
        if (Match("Identifier"))
        {
            name = Consume();
        }
        else
        {
            name = new Token { Kind = "Identifier", Text = string.Empty, Span = CurrentSpan(), LeadingTrivia = Array.Empty<Trivia>(), TrailingTrivia = Array.Empty<Trivia>() };
            AddDiagnostic("HLSL1001", "Expected identifier in function declaration.", CurrentSpan());
        }
        children.Add(new AstChild { Role = "identifier", Node = Leaf("Identifier", name) });

        var parameters = ParseParameterList();
        children.AddRange(parameters.Select(p => new AstChild { Role = "parameter", Node = p }));

        foreach (var annotation in ParseAnnotations(new[] { "OpenBrace", "Semicolon" }))
        {
            children.Add(annotation);
        }

        AstNode body;
        if (Match("OpenBrace"))
        {
            body = ParseBlock();
        }
        else if (allowSignatureOnly && Match("Semicolon"))
        {
            var semi = Consume();
            body = new AstNode
            {
                Id = NextId(),
                Kind = "EmptyBody",
                Span = semi.Span,
                Children = Array.Empty<AstChild>()
            };
        }
        else if (allowSignatureOnly)
        {
            AddDiagnostic("HLSL1002", "Expected '{' or ';' after function signature.", CurrentSpan());
            body = new AstNode
            {
                Id = NextId(),
                Kind = "EmptyBody",
                Span = CurrentSpan(),
                Children = Array.Empty<AstChild>()
            };
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

        var modifiers = new List<Token>();
        while (!IsEnd && IsModifier(Current!))
        {
            modifiers.Add(Consume());
        }

        var typeTok = Consume();
        var typeNode = Leaf("Type", typeTok);
        if (modifiers.Count > 0)
        {
            var typeSpan = new Span { Start = modifiers[0].Span.Start, End = typeTok.Span.End };
            typeNode = new AstNode
            {
                Id = NextId(),
                Kind = "Type",
                Span = typeSpan,
                Children = modifiers.Select(m => new AstChild { Role = "modifier", Node = Leaf("Modifier", m) }).ToArray()
            };
        }
        children.Add(new AstChild { Role = "type", Node = typeNode });

        List<AstChild> ParseDeclarator(out Span span)
        {
            var declChildren = new List<AstChild>();
            Token identTok;
            if (Match("Less"))
            {
                ConsumeTemplateArguments();
            }

            if (Match("Identifier"))
            {
                identTok = Consume();
            }
            else
            {
                identTok = new Token { Kind = "Identifier", Text = string.Empty, Span = CurrentSpan(), LeadingTrivia = Array.Empty<Trivia>(), TrailingTrivia = Array.Empty<Trivia>() };
                AddDiagnostic("HLSL1001", "Expected identifier in declaration.", CurrentSpan());
            }
            declChildren.Add(new AstChild { Role = "identifier", Node = Leaf("Identifier", identTok) });

            while (Match("OpenBracket"))
            {
                var startArr = Consume().Span.Start;
                var sizeExpr = ParseExpression();
                ConsumeExpected("CloseBracket");
                var endArr = sizeExpr?.Span.End ?? CurrentSpan().End;
                declChildren.Add(new AstChild
                {
                    Role = "array",
                    Node = new AstNode
                    {
                        Id = NextId(),
                        Kind = "ArrayDeclarator",
                        Span = new Span { Start = startArr, End = endArr },
                        Children = sizeExpr is null ? Array.Empty<AstChild>() : new[] { new AstChild { Role = "size", Node = sizeExpr } }
                    }
                });
            }

            foreach (var annotation in ParseAnnotations(new[] { "Semicolon", "Equals", "Comma" }))
            {
                declChildren.Add(annotation);
            }

            if (Match("Colon"))
            {
                var colon = Consume();
                var semTokens = new List<Token>();
                while (!IsEnd && !Match("Semicolon") && !Match("Equals") && !Match("Comma") && !Match("Less") && !Match("OpenBrace"))
                {
                    semTokens.Add(Consume());
                }

                var semEnd = semTokens.Count > 0 ? semTokens[^1].Span.End : colon.Span.End;
                declChildren.Add(new AstChild
                {
                    Role = "semantic",
                    Node = new AstNode
                    {
                        Id = NextId(),
                        Kind = "Semantic",
                        Span = new Span { Start = colon.Span.Start, End = semEnd },
                        Children = semTokens.Select(t => new AstChild { Role = "token", Node = Leaf("Token", t) }).ToArray()
                    }
                });
            }

            while (Match("Less"))
            {
                var annSpan = ConsumeAngleBlockSpan();
                declChildren.Add(new AstChild
                {
                    Role = "annotation",
                    Node = new AstNode
                    {
                        Id = NextId(),
                        Kind = "Annotation",
                        Span = annSpan,
                        Children = Array.Empty<AstChild>()
                    }
                });
            }

            if (Match("Equals"))
            {
                Consume(); // =
                AstNode? initializer = null;
                if (Match("KeywordAsm"))
                {
                    initializer = ParseAsmBlock();
                }
                else if (IsSamplerType(typeTok) && Match("KeywordSamplerState"))
                {
                    initializer = ParseInlineSamplerState();
                }
                else if (Match("OpenBrace"))
                {
                    var spanList = ConsumeBlockSpan();
                    initializer = new AstNode
                    {
                        Id = NextId(),
                        Kind = "InitializerList",
                        Span = spanList,
                        Children = Array.Empty<AstChild>()
                    };
                }
                else
                {
                    initializer = ParseExpression();
                }

                if (initializer is not null)
                {
                    declChildren.Add(new AstChild { Role = "initializer", Node = initializer });
                }
            }

            var declEnd = declChildren.LastOrDefault()?.Node.Span.End ?? identTok.Span.End;
            span = new Span { Start = identTok.Span.Start, End = declEnd };
            return declChildren;
        }

        var firstDeclChildren = ParseDeclarator(out var firstSpan);

        if (Match("Comma"))
        {
            var declarators = new List<AstNode>
            {
                new AstNode
                {
                    Id = NextId(),
                    Kind = "VariableDeclarator",
                    Span = firstSpan,
                    Children = firstDeclChildren.ToArray()
                }
            };

            while (Match("Comma"))
            {
                Consume();
                var nextDeclChildren = ParseDeclarator(out var declSpan);
                declarators.Add(new AstNode
                {
                    Id = NextId(),
                    Kind = "VariableDeclarator",
                    Span = declSpan,
                    Children = nextDeclChildren.ToArray()
                });
            }

            var semi = ConsumeExpected("Semicolon");
            var end = semi.Kind == "Semicolon" ? declarators.Last().Span.End : semi.Span.End;
            children.AddRange(declarators.Select(d => new AstChild { Role = "declarator", Node = d }));

            return new AstNode
            {
                Id = NextId(),
                Kind = "VariableDeclaration",
                Span = new Span { Start = start, End = end },
                Children = children.ToArray()
            };
        }
        else
        {
            var semi = ConsumeExpected("Semicolon");
            var end = semi.Kind == "Semicolon"
                ? firstDeclChildren.LastOrDefault()?.Node.Span.End ?? typeNode.Span.End
                : semi.Span.End;
            children.AddRange(firstDeclChildren);

            return new AstNode
            {
                Id = NextId(),
                Kind = "VariableDeclaration",
                Span = new Span { Start = start, End = end },
                Children = children.ToArray()
            };
        }
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

    private AstNode ParseAsmBlock()
    {
        var asmTok = ConsumeExpected("KeywordAsm");
        Span span;
        if (Match("OpenBrace"))
        {
            span = ConsumeBlockSpan();
        }
        else
        {
            AddDiagnostic("HLSL1002", "Expected '{' to start asm block.", CurrentSpan());
            span = asmTok.Span;
        }

        return new AstNode
        {
            Id = NextId(),
            Kind = "AsmBlock",
            Span = span,
            Children = Array.Empty<AstChild>()
        };
    }

    private AstNode ParseInlineSamplerState()
    {
        var start = ConsumeExpected("KeywordSamplerState").Span.Start;
        Span bodySpan;
        if (Match("OpenBrace"))
        {
            bodySpan = ConsumeBlockSpan();
        }
        else
        {
            AddDiagnostic("HLSL1002", "Expected '{' to start sampler_state body.", CurrentSpan());
            bodySpan = CurrentSpan();
        }

        return new AstNode
        {
            Id = NextId(),
            Kind = "SamplerStateInitializer",
            Span = new Span { Start = start, End = bodySpan.End },
            Children = Array.Empty<AstChild>()
        };
    }

    private AstNode ParseCompileExpression()
    {
        var compileTok = ConsumeExpected("Identifier");
        var children = new List<AstChild>
        {
            new AstChild { Role = "keyword", Node = Leaf("Identifier", compileTok) }
        };

        Token profileTok;
        if (Match("Identifier"))
        {
            profileTok = Consume();
        }
        else
        {
            profileTok = new Token { Kind = "Identifier", Text = string.Empty, Span = CurrentSpan(), LeadingTrivia = Array.Empty<Trivia>(), TrailingTrivia = Array.Empty<Trivia>() };
            AddDiagnostic("HLSL1001", "Expected profile after 'compile'.", CurrentSpan());
        }
        children.Add(new AstChild { Role = "profile", Node = Leaf("Identifier", profileTok) });

        AstNode entryNode;
        if (Match("Identifier"))
        {
            entryNode = Leaf("Identifier", Consume());
        }
        else
        {
            var missing = new Token { Kind = "Identifier", Text = string.Empty, Span = CurrentSpan(), LeadingTrivia = Array.Empty<Trivia>(), TrailingTrivia = Array.Empty<Trivia>() };
            entryNode = Leaf("Identifier", missing);
            AddDiagnostic("HLSL1001", "Expected entry identifier after profile.", CurrentSpan());
        }

        if (Match("OpenParen"))
        {
            var args = new List<AstChild>();
            Consume();
            while (!IsEnd && !Match("CloseParen"))
            {
                var argExpr = ParseExpression();
                if (argExpr is not null)
                {
                    args.Add(new AstChild { Role = "argument", Node = argExpr });
                }
                if (Match("Comma"))
                {
                    Consume();
                    continue;
                }
                else
                {
                    break;
                }
            }
            ConsumeExpected("CloseParen");

            entryNode = new AstNode
            {
                Id = NextId(),
                Kind = "CallExpression",
                Span = new Span { Start = entryNode.Span.Start, End = CurrentSpan().End },
                Children = new[] { new AstChild { Role = "callee", Node = entryNode } }.Concat(args).ToArray()
            };
        }

        var end = entryNode.Span.End;
        children.Add(new AstChild { Role = "entry", Node = entryNode });

        return new AstNode
        {
            Id = NextId(),
            Kind = "CompileExpression",
            Span = new Span { Start = compileTok.Span.Start, End = end },
            Children = children.ToArray()
        };
    }

    private AstNode ParseCastExpression()
    {
        var start = ConsumeExpected("OpenParen").Span.Start;
        Token typeTok;
        if (Current is not null && IsTypeLike(Current))
        {
            typeTok = Consume();
        }
        else
        {
            typeTok = new Token { Kind = "Identifier", Text = string.Empty, Span = CurrentSpan(), LeadingTrivia = Array.Empty<Trivia>(), TrailingTrivia = Array.Empty<Trivia>() };
            AddDiagnostic("HLSL1001", "Expected type in cast.", CurrentSpan());
        }

        if (Match("Less"))
        {
            ConsumeTemplateArguments();
        }

        ConsumeExpected("CloseParen");
        var operand = ParseUnary();
        var end = operand?.Span.End ?? CurrentSpan().End;

        var typeNode = Leaf("Type", typeTok);
        var children = new List<AstChild> { new AstChild { Role = "type", Node = typeNode } };
        if (operand is not null)
        {
            children.Add(new AstChild { Role = "operand", Node = operand });
        }

        return new AstNode
        {
            Id = NextId(),
            Kind = "CastExpression",
            Span = new Span { Start = start, End = end },
            Children = children.ToArray()
        };
    }

    private AstNode? ParseStatement()
    {
        if (IsEnd) return null;

        var attributes = ParseAttributes();

        if (Current!.Kind == "KeywordReturn")
        {
            return ParseReturnStatement();
        }

        if (Current!.Kind == "OpenBrace")
        {
            return ParseBlock();
        }

        if (Current!.Kind == "KeywordIf")
        {
            return ParseIfStatement();
        }

        if (Current!.Kind == "KeywordWhile")
        {
            return ParseWhileStatement();
        }

        if (Current!.Kind == "KeywordSwitch")
        {
            return ParseSwitchStatement();
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
            var stmt = new AstNode
            {
                Id = NextId(),
                Kind = "DiscardStatement",
                Span = new Span { Start = start, End = CurrentSpan().End },
                Children = Array.Empty<AstChild>()
            };
            return WrapWithAttributes(attributes, stmt);
        }

        if (Current!.Kind == "Semicolon")
        {
            var semi = Consume();
            var stmt = new AstNode
            {
                Id = NextId(),
                Kind = "EmptyStatement",
                Span = semi.Span,
                Children = Array.Empty<AstChild>()
            };
            return WrapWithAttributes(attributes, stmt);
        }

        // Local variable declaration heuristic inside blocks: type Identifier ...
        var typeIndex = SkipModifiersFrom(_position);
        var typeTok = PeekAbsolute(typeIndex);
        if (typeTok is not null && IsTypeLike(typeTok) && (PeekAbsolute(typeIndex + 1)?.Kind == "Identifier" || PeekAbsolute(typeIndex + 1)?.Kind == "Less") && PeekAbsolute(typeIndex + 2)?.Kind != "OpenParen")
        {
            var decl = ParseVariableDeclaration();
            return WrapWithAttributes(attributes, decl);
        }

        var exprStmt = ParseExpressionStatement();
        return WrapWithAttributes(attributes, exprStmt);
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

    private AstNode ParseSwitchStatement()
    {
        var start = Consume().Span.Start; // switch
        ConsumeExpected("OpenParen");
        var expr = ParseExpression();
        ConsumeExpected("CloseParen");
        ConsumeExpected("OpenBrace");

        var sections = new List<AstChild>();
        while (!IsEnd && !Match("CloseBrace"))
        {
            var labels = new List<AstChild>();
            while (Match("KeywordCase") || Match("KeywordDefault"))
            {
                var labelStart = Consume().Span.Start;
                AstNode labelNode;
                if (Match("KeywordDefault"))
                {
                    // already consumed default
                    ConsumeExpected("Colon");
                    labelNode = new AstNode
                    {
                        Id = NextId(),
                        Kind = "DefaultLabel",
                        Span = new Span { Start = labelStart, End = CurrentSpan().End },
                        Children = Array.Empty<AstChild>()
                    };
                }
                else
                {
                    // case
                    var value = ParseExpression();
                    ConsumeExpected("Colon");
                    labelNode = new AstNode
                    {
                        Id = NextId(),
                        Kind = "CaseLabel",
                        Span = new Span { Start = labelStart, End = value?.Span.End ?? CurrentSpan().End },
                        Children = value is null ? Array.Empty<AstChild>() : new[] { new AstChild { Role = "value", Node = value } }
                    };
                }

                labels.Add(new AstChild { Role = "label", Node = labelNode });
            }

            var statements = new List<AstChild>();
            while (!IsEnd && !Match("CloseBrace") && !Match("KeywordCase") && !Match("KeywordDefault"))
            {
                var stmt = ParseStatement();
                if (stmt is not null)
                {
                    statements.Add(new AstChild { Role = "statement", Node = stmt });
                }
                else
                {
                    // recovery: skip token
                    Consume();
                }
            }

            var secEnd = statements.LastOrDefault()?.Node.Span.End ?? labels.LastOrDefault()?.Node.Span.End ?? CurrentSpan().End;
            sections.Add(new AstChild
            {
                Role = "section",
                Node = new AstNode
                {
                    Id = NextId(),
                    Kind = "SwitchSection",
                    Span = new Span { Start = labels.FirstOrDefault()?.Node.Span.Start ?? CurrentSpan().Start, End = secEnd },
                    Children = labels.Concat(statements).ToArray()
                }
            });
        }

        var end = Match("CloseBrace") ? Consume().Span.End : CurrentSpan().End;
        return new AstNode
        {
            Id = NextId(),
            Kind = "SwitchStatement",
            Span = new Span { Start = start, End = end },
            Children = new[]
            {
                new AstChild { Role = "expression", Node = expr ?? Leaf("Missing", new Token { Span = CurrentSpan() }) }
            }.Concat(sections).ToArray()
        };
    }

    private AstNode ParseForStatement()
    {
        var start = Consume().Span.Start; // for
        ConsumeExpected("OpenParen");

        AstNode? init = null;
        if (!Match("Semicolon"))
        {
            var typeIndex = SkipModifiersFrom(_position);
            var typeTok = PeekAbsolute(typeIndex);
            var identAfterType = PeekAbsolute(typeIndex + 1);
            if (typeTok is not null && IsTypeLike(typeTok) && identAfterType is { Kind: "Identifier" } && PeekAbsolute(typeIndex + 2)?.Kind != "OpenParen")
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

    private AstNode[] ParseParameterList()
    {
        var parameters = new List<AstNode>();
        ConsumeExpected("OpenParen");
        while (!IsEnd && !Match("CloseParen"))
        {
            var paramStart = CurrentSpan().Start;
            var modifiers = new List<Token>();
            while (!IsEnd && IsParameterModifier(Current!))
            {
                modifiers.Add(Consume());
            }

            if (!IsEnd && IsTypeLike(Current!))
            {
                var typeTok = Consume(); // type
                if (Match("Less"))
                {
                    ConsumeTemplateArguments();
                }

                var identTok = Match("Identifier")
                    ? Consume()
                    : new Token { Kind = "Identifier", Text = string.Empty, Span = CurrentSpan(), LeadingTrivia = Array.Empty<Trivia>(), TrailingTrivia = Array.Empty<Trivia>() };
                var children = new List<AstChild>();

                var typeNode = Leaf("Type", typeTok);
                if (modifiers.Count > 0)
                {
                    var typeSpan = new Span { Start = modifiers[0].Span.Start, End = typeTok.Span.End };
                    typeNode = new AstNode
                    {
                        Id = NextId(),
                        Kind = "Type",
                        Span = typeSpan,
                        Children = modifiers.Select(m => new AstChild { Role = "modifier", Node = Leaf("Modifier", m) }).ToArray()
                    };
                }

                children.Add(new AstChild { Role = "type", Node = typeNode });
                children.Add(new AstChild { Role = "identifier", Node = Leaf("Identifier", identTok) });

                while (Match("OpenBracket"))
                {
                    var arrStart = Consume().Span.Start;
                    var sizeExpr = ParseExpression();
                    ConsumeExpected("CloseBracket");
                    var arrEnd = sizeExpr?.Span.End ?? CurrentSpan().End;
                    children.Add(new AstChild
                    {
                        Role = "array",
                        Node = new AstNode
                        {
                            Id = NextId(),
                            Kind = "ArrayDeclarator",
                            Span = new Span { Start = arrStart, End = arrEnd },
                            Children = sizeExpr is null ? Array.Empty<AstChild>() : new[] { new AstChild { Role = "size", Node = sizeExpr } }
                        }
                    });
                }

                children.AddRange(ParseAnnotations(new[] { "Comma", "CloseParen" }));

                parameters.Add(new AstNode
                {
                    Id = NextId(),
                    Kind = "Parameter",
                    Span = new Span { Start = paramStart, End = children.Last().Node.Span.End },
                    Children = children.ToArray()
                });
            }
            else
            {
                // recovery: consume token
                _position++;
            }

            if (Match("Comma"))
            {
                Consume();
                continue;
            }
            else
            {
                break;
            }
        }
        if (Match("CloseParen"))
        {
            Consume();
        }
        else
        {
            AddDiagnostic("HLSL1002", "Expected ')' to close parameter list.", CurrentSpan());
        }
        return parameters.ToArray();
    }

    private IEnumerable<AstChild> ParseAnnotations(IEnumerable<string> terminators)
    {
        var termSet = new HashSet<string>(terminators);
        var annotations = new List<AstChild>();
        while (!IsEnd && Match("Colon"))
        {
            var colon = Consume();
            if (Current is null || termSet.Contains(Current.Kind))
            {
                break;
            }

            var start = colon.Span.Start;
            var tokens = new List<Token>();
            while (!IsEnd && !termSet.Contains(Current!.Kind) && Current!.Kind != "Comma" && Current!.Kind != "Equals" && Current!.Kind != "OpenBrace" && Current!.Kind != "Less")
            {
                tokens.Add(Consume());
            }

            var end = tokens.Count > 0 ? tokens.Last().Span.End : colon.Span.End;
            annotations.Add(new AstChild
            {
                Role = "annotation",
                Node = new AstNode
                {
                    Id = NextId(),
                    Kind = "Annotation",
                    Span = new Span { Start = start, End = end },
                    Children = tokens.Select(t => new AstChild { Role = "token", Node = Leaf("Token", t) }).ToArray()
                }
            });
        }

        return annotations;
    }

    private AstNode ParseSamplerState()
    {
        var start = Consume().Span.Start; // sampler_state
        var name = Match("Identifier") ? Consume() : new Token { Span = CurrentSpan() };
        var children = new List<AstChild>
        {
            new AstChild { Role = "identifier", Node = Leaf("Identifier", name) }
        };

        if (Match("OpenBrace"))
        {
            var blockSpan = ConsumeBlockSpan();
            children.Add(new AstChild
            {
                Role = "body",
                Node = new AstNode
                {
                    Id = NextId(),
                    Kind = "SamplerStateBody",
                    Span = blockSpan,
                    Children = Array.Empty<AstChild>()
                }
            });
        }
        ConsumeExpected("Semicolon");

        var end = children.Last().Node.Span.End;
        return new AstNode
        {
            Id = NextId(),
            Kind = "SamplerStateDeclaration",
            Span = new Span { Start = start, End = end },
            Children = children.ToArray()
        };
    }

    private AstNode ParseFx10StateObject(string keywordKind)
    {
        var start = Consume().Span.Start; // keyword
        var name = Match("Identifier") ? Consume() : new Token { Span = CurrentSpan() };
        var children = new List<AstChild>
        {
            new AstChild { Role = "identifier", Node = Leaf("Identifier", name) }
        };

        Span bodySpan;
        if (Match("OpenBrace"))
        {
            bodySpan = ConsumeBlockSpan();
            children.Add(new AstChild
            {
                Role = "body",
                Node = new AstNode
                {
                    Id = NextId(),
                    Kind = "StateObjectBody",
                    Span = bodySpan,
                    Children = Array.Empty<AstChild>()
                }
            });
        }
        else
        {
            AddDiagnostic("HLSL1002", "Expected '{' to start state object body.", CurrentSpan());
            bodySpan = CurrentSpan();
        }

        if (Match("Semicolon"))
        {
            Consume();
        }

        var end = children.Last().Node.Span.End;
        var declKind = keywordKind switch
        {
            "KeywordDepthStencilState" => "DepthStencilStateDeclaration",
            "KeywordBlendState" => "BlendStateDeclaration",
            "KeywordRasterizerState" => "RasterizerStateDeclaration",
            "KeywordSamplerState10" => "SamplerState10Declaration",
            _ => "StateObjectDeclaration"
        };

        return new AstNode
        {
            Id = NextId(),
            Kind = declKind,
            Span = new Span { Start = start, End = end },
            Children = children.ToArray()
        };
    }

    private AstNode ParseCBuffer()
    {
        var start = Consume().Span.Start; // cbuffer/tbuffer
        var name = Match("Identifier") ? Consume() : new Token { Span = CurrentSpan() };
        var children = new List<AstChild>
        {
            new AstChild { Role = "identifier", Node = Leaf("Identifier", name) }
        };

        if (Match("Colon"))
        {
            Consume(); // :
            while (!IsEnd && !Match("OpenBrace"))
            {
                Consume();
            }
        }

        if (Match("OpenBrace"))
        {
            var span = ConsumeBlockSpan();
            children.Add(new AstChild
            {
                Role = "body",
                Node = new AstNode
                {
                    Id = NextId(),
                    Kind = "BufferBody",
                    Span = span,
                    Children = Array.Empty<AstChild>()
                }
            });
            Token? terminator = null;
            if (Match("Semicolon"))
            {
                terminator = Consume();
            }
            return new AstNode
            {
                Id = NextId(),
                Kind = "BufferDeclaration",
                Span = new Span { Start = start, End = terminator?.Span.End ?? span.End },
                Children = children.ToArray()
            };
        }
        else
        {
            AddDiagnostic("HLSL1002", "Expected '{' to start buffer body.", CurrentSpan());
            return new AstNode
            {
                Id = NextId(),
                Kind = "BufferDeclaration",
                Span = CurrentSpan(),
                Children = children.ToArray()
            };
        }
    }

    private AstNode ParseTypedef()
    {
        var start = Consume().Span.Start; // typedef
        var children = new List<AstChild>();

        AstNode typeNode;
        if (Match("KeywordStruct") || Match("KeywordClass") || Match("KeywordInterface"))
        {
            typeNode = ParseStructLike(Current!.Kind, consumeTrailingSemicolon: false);
        }
        else
        {
            var typeTok = Consume();
            typeNode = Leaf("Type", typeTok);
            if (Match("Less"))
            {
                ConsumeTemplateArguments();
            }
        }
        children.Add(new AstChild { Role = "type", Node = typeNode });

        Token aliasTok;
        if (Match("Identifier"))
        {
            aliasTok = Consume();
        }
        else
        {
            aliasTok = new Token { Kind = "Identifier", Text = string.Empty, Span = CurrentSpan(), LeadingTrivia = Array.Empty<Trivia>(), TrailingTrivia = Array.Empty<Trivia>() };
            AddDiagnostic("HLSL1001", "Expected identifier in typedef.", CurrentSpan());
        }
        children.Add(new AstChild { Role = "identifier", Node = Leaf("Identifier", aliasTok) });

        while (Match("OpenBracket"))
        {
            var arrStart = Consume().Span.Start;
            var sizeExpr = ParseExpression();
            ConsumeExpected("CloseBracket");
            var arrEnd = sizeExpr?.Span.End ?? CurrentSpan().End;
            children.Add(new AstChild
            {
                Role = "array",
                Node = new AstNode
                {
                    Id = NextId(),
                    Kind = "ArrayDeclarator",
                    Span = new Span { Start = arrStart, End = arrEnd },
                    Children = sizeExpr is null ? Array.Empty<AstChild>() : new[] { new AstChild { Role = "size", Node = sizeExpr } }
                }
            });
        }

        foreach (var annotation in ParseAnnotations(new[] { "Semicolon" }))
        {
            children.Add(annotation);
        }

        ConsumeExpected("Semicolon");

        var end = children.Last().Node.Span.End;
        return new AstNode
        {
            Id = NextId(),
            Kind = "TypedefDeclaration",
            Span = new Span { Start = start, End = end },
            Children = children.ToArray()
        };
    }

    private AstNode ParseStructLike(string kind, bool consumeTrailingSemicolon = true)
    {
        var start = Consume().Span.Start; // struct/class/interface
        var name = Match("Identifier") ? Consume() : new Token { Span = CurrentSpan() };
        var children = new List<AstChild> { new AstChild { Role = "identifier", Node = Leaf("Identifier", name) } };

        if (Match("Colon"))
        {
            Consume();
            while (!IsEnd && !Match("OpenBrace"))
            {
                Consume();
            }
        }

        if (Match("OpenBrace"))
        {
            var body = ParseStructBody();
            children.Add(new AstChild { Role = "body", Node = body });
            Token? terminator = null;
            if (consumeTrailingSemicolon)
            {
                terminator = ConsumeExpected("Semicolon");
            }
            return new AstNode
            {
                Id = NextId(),
                Kind = kind == "KeywordStruct" ? "StructDeclaration" : kind == "KeywordClass" ? "ClassDeclaration" : "InterfaceDeclaration",
                Span = new Span { Start = start, End = terminator?.Span.End ?? body.Span.End },
                Children = children.ToArray()
            };
        }
        else
        {
            AddDiagnostic("HLSL1002", "Expected '{' to start type body.", CurrentSpan());
            return new AstNode
            {
                Id = NextId(),
                Kind = kind,
                Span = CurrentSpan(),
                Children = children.ToArray()
            };
        }
    }

    private AstNode ParseTechnique(string kind)
    {
        var start = Consume().Span.Start; // technique/technique10
        var name = Match("Identifier") ? Consume() : new Token { Span = CurrentSpan() };
        var children = new List<AstChild> { new AstChild { Role = "identifier", Node = Leaf("Identifier", name) } };

        while (Match("Less"))
        {
            var annSpan = ConsumeAngleBlockSpan();
            children.Add(new AstChild
            {
                Role = "annotation",
                Node = new AstNode
                {
                    Id = NextId(),
                    Kind = "Annotation",
                    Span = annSpan,
                    Children = Array.Empty<AstChild>()
                }
            });
        }

        if (!Match("OpenBrace"))
        {
            AddDiagnostic("HLSL1002", "Expected '{' to start technique body.", CurrentSpan());
            return new AstNode
            {
                Id = NextId(),
                Kind = "TechniqueDeclaration",
                Span = CurrentSpan(),
                Children = children.ToArray()
            };
        }

        var bodyStartTok = ConsumeExpected("OpenBrace");
        var bodyMembers = new List<AstChild>();
        while (!IsEnd && !Match("CloseBrace"))
        {
            if (Match("KeywordPass"))
            {
                var pass = ParsePass();
                bodyMembers.Add(new AstChild { Role = "pass", Node = pass });
                continue;
            }

            // Skip unexpected tokens inside technique body.
            AddDiagnostic("HLSL1001", $"Unexpected token '{Current?.Text}'.", CurrentSpan());
            Consume();
        }

        var bodyEnd = Match("CloseBrace") ? Consume().Span.End : CurrentSpan().End;
        var bodyNode = new AstNode
        {
            Id = NextId(),
            Kind = "TechniqueBody",
            Span = new Span { Start = bodyStartTok.Span.Start, End = bodyEnd },
            Children = bodyMembers.ToArray()
        };
        children.Add(new AstChild { Role = "body", Node = bodyNode });

        if (Match("Semicolon"))
        {
            Consume();
        }

        return new AstNode
        {
            Id = NextId(),
            Kind = kind switch
            {
                "KeywordTechnique10" => "Technique10Declaration",
                "KeywordTechnique11" => "Technique11Declaration",
                _ => "TechniqueDeclaration"
            },
            Span = new Span { Start = start, End = bodyEnd },
            Children = children.ToArray()
        };
    }

    private AstNode ParsePass()
    {
        var start = Consume().Span.Start; // pass
        var name = Match("Identifier") ? Consume() : new Token { Span = CurrentSpan() };
        var children = new List<AstChild> { new AstChild { Role = "identifier", Node = Leaf("Identifier", name) } };

        while (Match("Less"))
        {
            var annSpan = ConsumeAngleBlockSpan();
            children.Add(new AstChild
            {
                Role = "annotation",
                Node = new AstNode
                {
                    Id = NextId(),
                    Kind = "Annotation",
                    Span = annSpan,
                    Children = Array.Empty<AstChild>()
                }
            });
        }

        AstNode body;
        if (Match("OpenBrace"))
        {
            body = ParseBlock();
        }
        else
        {
            AddDiagnostic("HLSL1002", "Expected '{' to start pass body.", CurrentSpan());
            body = new AstNode
            {
                Id = NextId(),
                Kind = "Block",
                Span = CurrentSpan(),
                Children = Array.Empty<AstChild>()
            };
        }

        children.Add(new AstChild { Role = "body", Node = body });
        return new AstNode
        {
            Id = NextId(),
            Kind = "PassDeclaration",
            Span = new Span { Start = start, End = body.Span.End },
            Children = children.ToArray()
        };
    }

    private Span ConsumeBlockSpan()
    {
        var startTok = ConsumeExpected("OpenBrace");
        var depth = 1;
        var end = startTok.Span.End;
        while (!IsEnd && depth > 0)
        {
            if (Match("OpenBrace"))
            {
                depth++;
            }
            else if (Match("CloseBrace"))
            {
                depth--;
            }
            end = CurrentSpan().End;
            Consume();
        }
        return new Span { Start = startTok.Span.Start, End = end };
    }

    private Span ConsumeAngleBlockSpan()
    {
        var startTok = ConsumeExpected("Less");
        var depth = 1;
        var end = startTok.Span.End;
        while (!IsEnd && depth > 0)
        {
            if (Match("Less"))
            {
                depth++;
            }
            else if (Match("Greater"))
            {
                depth--;
            }
            end = CurrentSpan().End;
            Consume();
        }

        return new Span { Start = startTok.Span.Start, End = end };
    }

    private AstNode ParseAngleExpression()
    {
        var startTok = ConsumeExpected("Less");
        var children = new List<AstChild>();
        var depth = 1;
        var end = startTok.Span.End;

        while (!IsEnd && depth > 0)
        {
            var tok = Current!;
            if (tok.Kind == "Less")
            {
                depth++;
                var nested = ConsumeAngleBlockSpan();
                children.Add(new AstChild
                {
                    Role = "annotation",
                    Node = new AstNode
                    {
                        Id = NextId(),
                        Kind = "Annotation",
                        Span = nested,
                        Children = Array.Empty<AstChild>()
                    }
                });
                end = nested.End;
                continue;
            }

            if (tok.Kind == "Greater")
            {
                depth--;
                end = tok.Span.End;
                Consume();
                continue;
            }

            if (tok.Kind == "Semicolon")
            {
                // statement separator inside annotation block
                Consume();
                continue;
            }

            if (tok.Kind == "Identifier")
            {
                var ident = Consume();
                AstNode? expr = null;
                if (Match("Equals"))
                {
                    Consume();
                    expr = ParseExpression();
                }

                var annNode = new AstNode
                {
                    Id = NextId(),
                    Kind = "AnnotationEntry",
                    Span = new Span { Start = ident.Span.Start, End = expr?.Span.End ?? ident.Span.End },
                    Children = expr is null
                        ? new[] { new AstChild { Role = "identifier", Node = Leaf("Identifier", ident) } }
                        : new[]
                        {
                            new AstChild { Role = "identifier", Node = Leaf("Identifier", ident) },
                            new AstChild { Role = "value", Node = expr }
                        }
                };
                children.Add(new AstChild { Role = "entry", Node = annNode });
                end = annNode.Span.End;
                continue;
            }

            // Fallback: consume token to avoid infinite loop.
            end = tok.Span.End;
            Consume();
        }

        return new AstNode
        {
            Id = NextId(),
            Kind = "AngleExpression",
            Span = new Span { Start = startTok.Span.Start, End = end },
            Children = children.ToArray()
        };
    }

    private AstNode ParseStructBody()
    {
        var start = ConsumeExpected("OpenBrace").Span.Start;
        var members = new List<AstChild>();

        while (!IsEnd && !Match("CloseBrace"))
        {
            if (Match("Semicolon"))
            {
                Consume();
                continue;
            }

            if (Match("KeywordStruct") || Match("KeywordClass") || Match("KeywordInterface"))
            {
                var nested = ParseStructLike(Current!.Kind);
                members.Add(new AstChild { Role = "member", Node = nested });
                continue;
            }

            var typeIndex = SkipModifiersFrom(_position);
            var typeTok = PeekAbsolute(typeIndex);
            if (typeTok is not null && IsTypeLike(typeTok))
            {
                if (LooksLikeFunctionSignature(typeIndex))
                {
                    var method = ParseFunctionLike(allowSignatureOnly: true);
                    members.Add(new AstChild { Role = "member", Node = method });
                    continue;
                }

                if (PeekAbsolute(typeIndex + 1) is { Kind: "Identifier" } || PeekAbsolute(typeIndex + 1) is { Kind: "Less" })
                {
                    var decl = ParseVariableDeclaration();
                    members.Add(new AstChild { Role = "member", Node = decl });
                    continue;
                }
            }

            AddDiagnostic("HLSL1001", $"Unexpected token '{Current?.Text}'.", CurrentSpan());
            Consume();
        }

        var end = Match("CloseBrace") ? Consume().Span.End : CurrentSpan().End;
        return new AstNode
        {
            Id = NextId(),
            Kind = "TypeBody",
            Span = new Span { Start = start, End = end },
            Children = members.ToArray()
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

            if (op.Kind == "Question")
            {
                var condPrec = GetPrecedence(op.Kind);
                if (condPrec < precedence) break;

                Consume(); // ?
                var whenTrue = ParseExpression();
                ConsumeExpected("Colon");
                var whenFalse = ParseExpression(condPrec + 1);

                if (whenTrue is null || whenFalse is null)
                {
                    AddDiagnostic("HLSL1003", $"Expected expression after '{op.Text}'.", CurrentSpan());
                    break;
                }

                left = new AstNode
                {
                    Id = NextId(),
                    Kind = "ConditionalExpression",
                    Span = new Span { Start = left.Span.Start, End = whenFalse.Span.End },
                    Children = new[]
                    {
                        new AstChild { Role = "condition", Node = left },
                        new AstChild { Role = "whenTrue", Node = whenTrue },
                        new AstChild { Role = "whenFalse", Node = whenFalse }
                    }
                };

                continue;
            }

            var opPrec = GetPrecedence(op.Kind);
            if (opPrec < precedence) break;

            var associativity = IsAssignmentOperator(op.Kind) ? Precedence.Right : Precedence.Left;
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
        if (tok.Kind is "Plus" or "Minus" or "Bang" or "Tilde" or "PlusPlus" or "MinusMinus")
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

        return ParsePostfix();
    }

    private AstNode? ParsePostfix()
    {
        var primary = ParsePrimary();
        if (primary is null) return null;

        while (!IsEnd)
        {
            if (Match("Dot"))
            {
                var dot = Consume();
                var ident = Match("Identifier") ? Consume() : null;
                if (ident is null)
                {
                    AddDiagnostic("HLSL1003", "Expected identifier after '.'.", CurrentSpan());
                    break;
                }

                primary = new AstNode
                {
                    Id = NextId(),
                    Kind = "MemberAccessExpression",
                    Span = new Span { Start = primary.Span.Start, End = ident.Span.End },
                    Children = new[]
                    {
                        new AstChild { Role = "expression", Node = primary },
                        new AstChild { Role = "member", Node = Leaf("Identifier", ident) }
                    }
                };
                continue;
            }

            if (Match("OpenParen"))
            {
                var args = new List<AstChild>();
                Consume(); // (
                while (!IsEnd && !Match("CloseParen"))
                {
                    var argExpr = ParseExpression();
                    if (argExpr is not null)
                    {
                        args.Add(new AstChild { Role = "argument", Node = argExpr });
                    }
                    if (Match("Comma"))
                    {
                        Consume();
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }
                ConsumeExpected("CloseParen");

                primary = new AstNode
                {
                    Id = NextId(),
                    Kind = "CallExpression",
                    Span = new Span { Start = primary.Span.Start, End = CurrentSpan().End },
                    Children = new[] { new AstChild { Role = "callee", Node = primary } }.Concat(args).ToArray()
                };
                continue;
            }

            if (Match("OpenBracket"))
            {
                Consume(); // [
                var indexExpr = ParseExpression();
                ConsumeExpected("CloseBracket");
                if (indexExpr is null)
                {
                    break;
                }

                primary = new AstNode
                {
                    Id = NextId(),
                    Kind = "IndexExpression",
                    Span = new Span { Start = primary.Span.Start, End = indexExpr.Span.End },
                    Children = new[]
                    {
                        new AstChild { Role = "expression", Node = primary },
                        new AstChild { Role = "index", Node = indexExpr }
                    }
                };
                continue;
            }

            if (Match("PlusPlus") || Match("MinusMinus"))
            {
                var op = Consume();
                primary = new AstNode
                {
                    Id = NextId(),
                    Kind = "PostfixExpression",
                    Span = new Span { Start = primary.Span.Start, End = op.Span.End },
                    Children = new[]
                    {
                        new AstChild { Role = "expression", Node = primary },
                        new AstChild { Role = "operator", Node = Leaf("Operator", op) }
                    }
                };
                continue;
            }

            break;
        }

        return primary;
    }

    private AstNode? ParsePrimary()
    {
        if (IsEnd) return null;
        var tok = Current!;
        switch (tok.Kind)
        {
            case "Identifier" when string.Equals(tok.Text, "compile", StringComparison.OrdinalIgnoreCase):
                return ParseCompileExpression();
            case "Identifier":
            case var k when k.StartsWith("Keyword", StringComparison.Ordinal):
                Consume();
                return Leaf("Identifier", tok);
            case "NumericLiteral":
                Consume();
                return Leaf("Literal", tok);
            case "StringLiteral":
                Consume();
                return Leaf("StringLiteral", tok);
            case "Less":
                return ParseAngleExpression();
            case "OpenParen" when LooksLikeCast():
                return ParseCastExpression();
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

    private bool IsTypeLike(Token token) =>
        token.Kind.StartsWith("Keyword", StringComparison.Ordinal) || token.Kind == "Identifier";

    private bool IsSamplerType(Token token) =>
        token.Kind.StartsWith("KeywordSampler", StringComparison.Ordinal)
        && token.Kind is not "KeywordSamplerState" and not "KeywordSamplerState10";

    private bool IsModifier(Token token) =>
        token.Kind is "KeywordStatic" or "KeywordConst" or "KeywordUniform" or "KeywordExtern" or "KeywordVolatile" ||
        (token.Kind == "Identifier" && (string.Equals(token.Text, "row_major", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(token.Text, "column_major", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(token.Text, "unsigned", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(token.Text, "signed", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(token.Text, "long", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(token.Text, "short", StringComparison.OrdinalIgnoreCase)));

    private bool IsParameterModifier(Token token) =>
        IsModifier(token) ||
        (token.Kind == "Identifier" && (string.Equals(token.Text, "in", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(token.Text, "out", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(token.Text, "inout", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(token.Text, "triangle", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(token.Text, "line", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(token.Text, "point", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(token.Text, "lineadj", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(token.Text, "triangleadj", StringComparison.OrdinalIgnoreCase)));

    private bool IsAssignmentOperator(string kind) =>
        kind is "Equals" or "PlusEquals" or "MinusEquals" or "StarEquals" or "SlashEquals" or "PercentEquals" or "AmpersandEquals" or "PipeEquals" or "CaretEquals" or "LessLessEquals" or "GreaterGreaterEquals";

    private int SkipModifiersFrom(int index)
    {
        while (index < _tokens.Length && IsModifier(_tokens[index]))
        {
            index++;
        }

        return index;
    }

    private Token? PeekAbsolute(int index) =>
        index >= 0 && index < _tokens.Length ? _tokens[index] : null;

    private bool IsBinaryOperator(Token token) =>
        token.Kind is "Plus" or "Minus" or "Star" or "Slash" or "Percent" or "Equals";

    private bool LooksLikeFunctionSignature(int typeIndex = -1)
    {
        var index = typeIndex >= 0 ? typeIndex : _position;
        var typeTok = PeekAbsolute(index);
        if (typeTok is null || !IsTypeLike(typeTok))
        {
            return false;
        }

        var lookahead = 1;
        if (PeekAbsolute(index + lookahead)?.Kind == "Less")
        {
            var depth = 0;
            while (true)
            {
                var tok = PeekAbsolute(index + lookahead);
                if (tok is null)
                {
                    return false;
                }

                if (tok.Kind == "Less")
                {
                    depth++;
                }
                else if (tok.Kind == "Greater")
                {
                    depth--;
                    if (depth <= 0)
                    {
                        lookahead++;
                        break;
                    }
                }
                lookahead++;
            }
        }

        var nameTok = PeekAbsolute(index + lookahead);
        if (nameTok?.Kind != "Identifier")
        {
            return false;
        }

        return PeekAbsolute(index + lookahead + 1)?.Kind == "OpenParen";
    }

    private bool LooksLikeCast()
    {
        var typeTok = Peek(1);
        if (typeTok is null || !IsTypeLike(typeTok))
        {
            return false;
        }

        var index = 2;
        while (true)
        {
            var tok = Peek(index);
            if (tok is null)
            {
                return false;
            }

            if (tok.Kind == "OpenBracket")
            {
                // Skip ahead to the matching close bracket (simple heuristic: advance until one is found).
                index++;
                while (Peek(index) is not null && Peek(index)!.Kind != "CloseBracket")
                {
                    index++;
                }

                if (Peek(index)?.Kind != "CloseBracket")
                {
                    return false;
                }

                index++; // past ]
                continue;
            }

            if (tok.Kind == "CloseParen")
            {
                index++;
                break;
            }

            return false;
        }

        var after = Peek(index);
        if (after is null)
        {
            return false;
        }

        var startsExpression = after.Kind is "Identifier" or "NumericLiteral" or "StringLiteral" or "OpenParen" or "Plus" or "Minus" or "Less" ||
                               after.Kind.StartsWith("Keyword", StringComparison.Ordinal);

        return startsExpression;
    }

    private AstNode WrapWithAttributes(List<AstChild> attributes, AstNode? statement)
    {
        var stmtNode = statement ?? new AstNode { Id = NextId(), Kind = "EmptyStatement", Span = CurrentSpan(), Children = Array.Empty<AstChild>() };
        if (attributes.Count == 0)
        {
            return stmtNode;
        }

        var start = attributes[0].Node.Span.Start;
        var end = stmtNode.Span.End;
        var children = new List<AstChild>(attributes) { new AstChild { Role = "statement", Node = stmtNode } };

        return new AstNode
        {
            Id = NextId(),
            Kind = "AttributedStatement",
            Span = new Span { Start = start, End = end },
            Children = children.ToArray()
        };
    }

    private AstNode? WrapDeclarationAttributes(List<AstChild> attributes, AstNode? declaration)
    {
        if (attributes.Count == 0)
        {
            return declaration;
        }

        var declNode = declaration ?? new AstNode { Id = NextId(), Kind = "EmptyDeclaration", Span = CurrentSpan(), Children = Array.Empty<AstChild>() };
        var start = attributes[0].Node.Span.Start;
        var end = declNode.Span.End;
        var children = new List<AstChild>(attributes) { new AstChild { Role = "declaration", Node = declNode } };

        return new AstNode
        {
            Id = NextId(),
            Kind = "AttributedDeclaration",
            Span = new Span { Start = start, End = end },
            Children = children.ToArray()
        };
    }

    private List<AstChild> ParseAttributes()
    {
        var attributes = new List<AstChild>();
        while (Match("OpenBracket"))
        {
            var start = Consume().Span.Start;
            while (!IsEnd && !Match("CloseBracket"))
            {
                Consume();
            }
            var end = Match("CloseBracket") ? Consume().Span.End : CurrentSpan().End;
            attributes.Add(new AstChild
            {
                Role = "attribute",
                Node = new AstNode
                {
                    Id = NextId(),
                    Kind = "Attribute",
                    Span = new Span { Start = start, End = end },
                    Children = Array.Empty<AstChild>()
                }
            });
        }

        return attributes;
    }

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

    private static int GetPrecedence(string kind) => kind switch
    {
        "Equals" or "PlusEquals" or "MinusEquals" or "StarEquals" or "SlashEquals" or "PercentEquals" or "AmpersandEquals" or "PipeEquals" or "CaretEquals" or "LessLessEquals" or "GreaterGreaterEquals" => 1,
        "Question" => 2,
        "PipePipe" => 3,
        "AmpersandAmpersand" => 4,
        "Pipe" => 5,
        "Caret" => 6,
        "Ampersand" => 7,
        "EqualsEquals" or "BangEquals" => 8,
        "Less" or "LessEquals" or "Greater" or "GreaterEquals" => 9,
        "LessLess" or "GreaterGreater" => 10,
        "Plus" or "Minus" => 11,
        "Star" or "Slash" or "Percent" => 12,
        _ => -1
    };

    private void ConsumeTemplateArguments()
    {
        if (!Match("Less"))
        {
            return;
        }

        var depth = 0;
        while (!IsEnd)
        {
            if (Match("Less"))
            {
                depth++;
            }
            else if (Match("Greater"))
            {
                depth--;
                Consume();
                if (depth <= 0)
                {
                    break;
                }
                continue;
            }
            Consume();
        }
    }
}
