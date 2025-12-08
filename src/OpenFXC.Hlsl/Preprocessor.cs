using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace OpenFXC.Hlsl;

public sealed record PreprocessorOptions
{
    public string? FilePath { get; init; }

    public IReadOnlyList<string> IncludeDirectories { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string?> Defines { get; init; } =
        new Dictionary<string, string?>(StringComparer.Ordinal);
}

public sealed record PreprocessResult(string Text, Diagnostic[] Diagnostics, SourceMap SourceMap);

/// <summary>
/// Lightweight preprocessor that handles includes, object/function-like macros, and conditional
/// compilation in a deterministic, syntax-only manner.
/// </summary>
public static class Preprocessor
{
    private const string DiagnosticMissingInclude = "HLSL2001";
    private const string DiagnosticIncludeCycle = "HLSL2002";
    private const string DiagnosticUnterminatedIf = "HLSL2003";
    private const string DiagnosticUnexpectedEndif = "HLSL2004";
    private const string DiagnosticMacroArgument = "HLSL2005";

    public static PreprocessResult Preprocess(string text, PreprocessorOptions options)
    {
        var context = new PreprocessorContext(options);
        var builder = new StringBuilder(text.Length);

        context.ProcessFile(text, options.FilePath ?? "stdin", builder);
        context.Complete(builder.Length);

        return new PreprocessResult(builder.ToString(), context.Diagnostics.ToArray(), context.BuildSourceMap());
    }

    private sealed class PreprocessorContext
    {
        private readonly Dictionary<string, MacroDefinition> _macros = new(StringComparer.Ordinal);
        private readonly HashSet<string> _includeStack = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pragmaOnce = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Diagnostic> _diagnostics = new();
        private readonly PreprocessorOptions _options;
        private readonly List<ConditionalFrame> _conditions = new();
        private readonly List<SourceSegment> _sourceSegments = new();
        private string CurrentPath => _includeStack.LastOrDefault() ?? (_options.FilePath ?? "stdin");

        public PreprocessorContext(PreprocessorOptions options)
        {
            _options = options;
            if (options.Defines is not null)
            {
                foreach (var kvp in options.Defines)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key))
                    {
                        continue;
                    }

                    _macros[kvp.Key] = new MacroDefinition(kvp.Key, Array.Empty<string>(), kvp.Value ?? string.Empty);
                }
            }
        }

        public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

        public bool IsMacroDefined(string name) => _macros.ContainsKey(name);

        public SourceMap BuildSourceMap() => new(_sourceSegments);

        public void Complete(int outputLength)
        {
            if (_conditions.Count > 0)
            {
                _diagnostics.Add(new Diagnostic
                {
                    Id = DiagnosticUnterminatedIf,
                    Message = "Unterminated conditional block.",
                    Span = new Span { Start = outputLength, End = outputLength },
                    Origin = new DiagnosticOrigin(CurrentPath, new Span { Start = outputLength, End = outputLength })
                });
            }
        }

        public void ProcessFile(string text, string path, StringBuilder output)
        {
            if (_pragmaOnce.Contains(path))
            {
                return;
            }

            if (_includeStack.Contains(path))
            {
                _diagnostics.Add(new Diagnostic
                {
                    Id = DiagnosticIncludeCycle,
                    Message = $"Include cycle detected for '{path}'.",
                    Span = new Span { Start = output.Length, End = output.Length },
                    Origin = new DiagnosticOrigin(path, new Span { Start = 0, End = 0 })
                });
                return;
            }

            _includeStack.Add(path);
            var index = 0;
            while (index < text.Length)
            {
                var lineStart = index;
                var logicalLine = ReadLogicalLine(text, ref index, out var newline, out var inputLength);
                var trimmed = logicalLine.AsSpan().TrimStart();

                var isDirective = trimmed.Length > 0 && trimmed[0] == '#';
                if (isDirective)
                {
                    HandleDirective(path, lineStart, logicalLine, inputLength, trimmed, newline, output);
                    continue;
                }

                if (!IsActive)
                {
                    output.Append(newline);
                    AddSegment(path, lineStart, inputLength, output.Length - newline.Length, newline.Length);
                    continue;
                }

                var outputStart = output.Length;
                var expanded = ExpandMacros(logicalLine, outputStart, allowDirectives: false);
                output.Append(expanded);
                output.Append(newline);
                AddSegment(path, lineStart, inputLength, outputStart, expanded.Length + newline.Length);
            }

            _includeStack.Remove(path);
        }

        private static string ReadLine(string text, ref int index, out string newline)
        {
            var start = index;
            while (index < text.Length && text[index] != '\n' && text[index] != '\r')
            {
                index++;
            }

            var end = index;
            newline = string.Empty;

            if (index < text.Length)
            {
                if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    newline = "\r\n";
                    index += 2;
                }
                else
                {
                    newline = text[index].ToString();
                    index++;
                }
            }

            return text.Substring(start, end - start);
        }

        private string ReadLogicalLine(string text, ref int index, out string newline, out int inputLength)
        {
            var logical = new StringBuilder();
            var totalInput = 0;
            string lastNewline = string.Empty;

            while (index < text.Length)
            {
                var lineStart = index;
                var line = ReadLine(text, ref index, out var nl);
                totalInput += (index - lineStart);
                lastNewline = nl;

                var trimmed = line.AsSpan().TrimEnd();
                var hasContinuation = trimmed.Length > 0 && trimmed[^1] == '\\';
                if (hasContinuation)
                {
                    // Drop the trailing backslash for logical line content.
                    logical.Append(line[..^1]);
                    continue;
                }

                logical.Append(line);
                break;
            }

            newline = lastNewline;
            inputLength = totalInput;
            return logical.ToString();
        }

        private void HandleDirective(string currentPath, int lineStart, string line, int inputLength, ReadOnlySpan<char> trimmed, string newline, StringBuilder output)
        {
            // Remove leading '#' and any whitespace after it.
            var directive = trimmed[1..].TrimStart();
            var keyword = ReadIdentifier(directive, out var rest);
            var keywordText = keyword.ToString().ToLowerInvariant();

            var outputStart = output.Length;
            var mapDirective = keywordText != "include" || !IsActive;
            switch (keywordText)
            {
                case "include":
                    HandleInclude(currentPath, lineStart, rest, newline, output);
                    break;
                case "define":
                    HandleDefine(rest);
                    output.Append(newline);
                    break;
                case "undef":
                    HandleUndef(rest);
                    output.Append(newline);
                    break;
                case "ifdef":
                    HandleIfDef(rest, isNegated: false);
                    output.Append(newline);
                    break;
                case "ifndef":
                    HandleIfDef(rest, isNegated: true);
                    output.Append(newline);
                    break;
                case "if":
                    HandleIf(rest);
                    output.Append(newline);
                    break;
                case "elif":
                    HandleElif(rest);
                    output.Append(newline);
                    break;
                case "else":
                    HandleElse();
                    output.Append(newline);
                    break;
                case "endif":
                    HandleEndif(lineStart, output.Length);
                    output.Append(newline);
                    break;
                case "pragma":
                    HandlePragma(currentPath, rest);
                    output.Append(newline);
                    break;
                case "error":
                    HandleError(lineStart, directive.ToString());
                    output.Append(newline);
                    break;
                default:
                    // Unknown directive: preserve newline to keep spans aligned.
                    output.Append(newline);
                    break;
            }

            var outputEnd = output.Length;
            if (mapDirective && outputEnd > outputStart)
            {
                AddSegment(currentPath, lineStart, inputLength, outputStart, outputEnd - outputStart);
            }
        }

        private void HandleInclude(string currentPath, int lineStart, ReadOnlySpan<char> rest, string newline, StringBuilder output)
        {
            if (!IsActive)
            {
                output.Append(newline);
                return;
            }

            rest = rest.TrimStart();
            if (rest.IsEmpty)
            {
                _diagnostics.Add(new Diagnostic
                {
                    Id = DiagnosticMissingInclude,
                    Message = "Missing include path.",
                    Span = new Span { Start = output.Length, End = output.Length },
                    Origin = new DiagnosticOrigin(currentPath, new Span { Start = lineStart, End = lineStart })
                });
                output.Append(newline);
                return;
            }

            var path = ParseIncludePath(rest, out var isAngle);
            if (string.IsNullOrWhiteSpace(path))
            {
                _diagnostics.Add(new Diagnostic
                {
                    Id = DiagnosticMissingInclude,
                    Message = "Include path was not well-formed.",
                    Span = new Span { Start = output.Length, End = output.Length },
                    Origin = new DiagnosticOrigin(currentPath, new Span { Start = lineStart, End = lineStart })
                });
                output.Append(newline);
                return;
            }

            var resolved = ResolveInclude(path!, currentPath, isAngle);
            if (resolved is null)
            {
                _diagnostics.Add(new Diagnostic
                {
                    Id = DiagnosticMissingInclude,
                    Message = $"Could not resolve include '{path}'.",
                    Span = new Span { Start = output.Length, End = output.Length },
                    Origin = new DiagnosticOrigin(currentPath, new Span { Start = lineStart, End = lineStart })
                });
                output.Append(newline);
                return;
            }

            if (_pragmaOnce.Contains(resolved))
            {
                output.Append(newline);
                return;
            }

            var includeText = File.ReadAllText(resolved);
            ProcessFile(includeText, resolved, output);
            output.Append(newline);
        }

        private string? ResolveInclude(string includePath, string currentPath, bool isAngle)
        {
            var searchDirectories = new List<string>();

            if (!isAngle)
            {
                var currentDirectory = Path.GetDirectoryName(currentPath);
                if (!string.IsNullOrEmpty(currentDirectory))
                {
                    searchDirectories.Add(currentDirectory);
                }
            }

            if (_options.IncludeDirectories is not null)
            {
                searchDirectories.AddRange(_options.IncludeDirectories);
            }

            foreach (var dir in searchDirectories)
            {
                if (string.IsNullOrWhiteSpace(dir))
                {
                    continue;
                }

                var candidate = Path.GetFullPath(Path.Combine(dir, includePath));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string? ParseIncludePath(ReadOnlySpan<char> rest, out bool isAngle)
        {
            isAngle = false;
            if (rest.StartsWith("\""))
            {
                var closing = rest[1..].IndexOf('"');
                if (closing >= 0)
                {
                    return rest.Slice(1, closing).ToString();
                }
            }

            if (rest.StartsWith("<"))
            {
                var closing = rest[1..].IndexOf('>');
                if (closing >= 0)
                {
                    isAngle = true;
                    return rest.Slice(1, closing).ToString();
                }
            }

            return null;
        }

        private void HandleDefine(ReadOnlySpan<char> rest)
        {
            if (!IsActive)
            {
                return;
            }

            rest = rest.TrimStart();
            if (rest.IsEmpty)
            {
                return;
            }

            var nameSpan = ReadIdentifier(rest, out var afterName);
            if (nameSpan.IsEmpty)
            {
                return;
            }

            var name = nameSpan.ToString();
            var parameters = Array.Empty<string>();
            var body = string.Empty;

            // Function-like macro only if '(' appears immediately after the name.
            if (!afterName.IsEmpty && afterName[0] == '(')
            {
                afterName = afterName[1..];
                var list = new List<string>();
                while (true)
                {
                    afterName = afterName.TrimStart();
                    var ident = ReadIdentifier(afterName, out var afterIdent);
                    if (ident.IsEmpty)
                    {
                        list.Add(string.Empty);
                    }
                    else
                    {
                        list.Add(ident.ToString());
                    }

                    afterName = afterIdent.TrimStart();
                    if (!afterName.IsEmpty && afterName[0] == ')')
                    {
                        afterName = afterName[1..];
                        break;
                    }

                    if (!afterName.IsEmpty && afterName[0] == ',')
                    {
                        afterName = afterName[1..];
                        continue;
                    }

                    // Unterminated parameter list; treat as no parameters.
                    list.Clear();
                    afterName = ReadToLineEnd(afterName);
                    break;
                }

                parameters = list.ToArray();
                body = afterName.ToString().TrimStart();
            }
            else
            {
                body = afterName.ToString().TrimStart();
            }

            body = StripLineComment(body).TrimEnd();

            _macros[name] = new MacroDefinition(name, parameters, body);
        }

        private void HandleUndef(ReadOnlySpan<char> rest)
        {
            if (!IsActive)
            {
                return;
            }

            var nameSpan = ReadIdentifier(rest.TrimStart(), out _);
            if (!nameSpan.IsEmpty)
            {
                _macros.Remove(nameSpan.ToString());
            }
        }

        private void HandleIf(ReadOnlySpan<char> rest)
        {
            var parentActive = IsActive;
            var condition = parentActive && EvaluateExpression(rest);
            _conditions.Add(new ConditionalFrame(parentActive, condition, condition));
        }

        private void HandleIfDef(ReadOnlySpan<char> rest, bool isNegated)
        {
            var parentActive = IsActive;
            var name = ReadIdentifier(rest.TrimStart(), out _);
            var defined = !name.IsEmpty && _macros.ContainsKey(name.ToString());
            var condition = parentActive && (isNegated ? !defined : defined);

            _conditions.Add(new ConditionalFrame(parentActive, condition, condition));
        }

        private static string StripLineComment(string text)
        {
            var inString = false;
            for (var i = 0; i < text.Length - 1; i++)
            {
                var c = text[i];
                if (c == '"')
                {
                    inString = !inString;
                }

                if (!inString && c == '/' && text[i + 1] == '/')
                {
                    return text.Substring(0, i);
                }
            }

            return text;
        }

        private void HandleElif(ReadOnlySpan<char> rest)
        {
            if (_conditions.Count == 0)
            {
                _diagnostics.Add(new Diagnostic
                {
                    Id = DiagnosticUnexpectedEndif,
                    Message = "Encountered #elif without matching #if.",
                    Span = new Span { Start = 0, End = 0 },
                    Origin = new DiagnosticOrigin(CurrentPath, new Span { Start = 0, End = 0 })
                });
                return;
            }

            var frame = _conditions[^1];
            if (!frame.ParentActive)
            {
                _conditions[^1] = frame with { CurrentActive = false };
                return;
            }

            if (frame.HasTakenTrue)
            {
                _conditions[^1] = frame with { CurrentActive = false };
                return;
            }

            var condition = EvaluateExpression(rest);
            var newFrame = frame with
            {
                CurrentActive = condition,
                HasTakenTrue = condition
            };
            _conditions[^1] = newFrame;
        }

        private void HandleElse()
        {
            if (_conditions.Count == 0)
            {
                _diagnostics.Add(new Diagnostic
                {
                    Id = DiagnosticUnexpectedEndif,
                    Message = "Encountered #else without matching #if.",
                    Span = new Span { Start = 0, End = 0 },
                    Origin = new DiagnosticOrigin(CurrentPath, new Span { Start = 0, End = 0 })
                });
                return;
            }

            var frame = _conditions[^1];
            if (!frame.ParentActive)
            {
                _conditions[^1] = frame with { CurrentActive = false };
                return;
            }

            var newFrame = frame with
            {
                CurrentActive = !frame.HasTakenTrue,
                HasTakenTrue = true
            };

            _conditions[^1] = newFrame;
        }

        private void HandleEndif(int lineStart, int outputLength)
        {
            if (_conditions.Count == 0)
            {
                _diagnostics.Add(new Diagnostic
                {
                    Id = DiagnosticUnexpectedEndif,
                    Message = "Encountered #endif without matching #if.",
                    Span = new Span { Start = outputLength, End = outputLength },
                    Origin = new DiagnosticOrigin(CurrentPath, new Span { Start = outputLength, End = outputLength })
                });
                return;
            }

            _conditions.RemoveAt(_conditions.Count - 1);
        }

        private void HandlePragma(string currentPath, ReadOnlySpan<char> rest)
        {
            rest = rest.TrimStart();
            if (rest.StartsWith("once", StringComparison.OrdinalIgnoreCase))
            {
                _pragmaOnce.Add(currentPath);
            }
        }

        private void HandleError(int lineStart, string directive)
        {
            _diagnostics.Add(new Diagnostic
            {
                Id = "HLSL2006",
                Message = $"#error: {directive}",
                Span = new Span { Start = lineStart, End = lineStart },
                Origin = new DiagnosticOrigin(_includeStack.LastOrDefault() ?? string.Empty, new Span { Start = lineStart, End = lineStart })
            });
        }

        private void AddSegment(string path, int inputStart, int inputLength, int outputStart, int outputLength)
        {
            if (outputLength <= 0)
            {
                return;
            }

            _sourceSegments.Add(new SourceSegment(
                new Span { Start = outputStart, End = outputStart + outputLength },
                path,
                new Span { Start = inputStart, End = inputStart + inputLength }));
        }

        private bool EvaluateExpression(ReadOnlySpan<char> expression)
        {
            expression = expression.Trim();
            if (expression.IsEmpty)
            {
                return false;
            }

            var evaluator = new ExpressionEvaluator(this, ExpandMacros(expression.ToString(), 0, allowDirectives: false));
            return evaluator.ParseOr();
        }

        private bool IsActive => _conditions.All(c => c.CurrentActive);

        private string ExpandMacros(string text, int basePosition, bool allowDirectives, HashSet<string>? expansionStack = null)
        {
            expansionStack ??= new HashSet<string>(StringComparer.Ordinal);
            var builder = new StringBuilder(text.Length);
            var index = 0;
            while (index < text.Length)
            {
                var current = text[index];

                if (current == '"' || current == '\'')
                {
                    builder.Append(ReadString(text, ref index));
                    continue;
                }

                if (current == '/' && index + 1 < text.Length && text[index + 1] == '/')
                {
                    builder.Append(text.Substring(index));
                    break;
                }

                if (current == '/' && index + 1 < text.Length && text[index + 1] == '*')
                {
                    builder.Append(ReadBlockComment(text, ref index));
                    continue;
                }

                if (allowDirectives && current == '#' && IsAtLineStart(text, index))
                {
                    builder.Append(text[index]);
                    index++;
                    continue;
                }

                if (IsIdentifierStart(current))
                {
                    var start = index;
                    var identifier = ReadIdentifier(text, ref index);
                    var name = identifier.ToString();

                    if (_macros.TryGetValue(name, out var macro))
                    {
                        if (expansionStack.Contains(name))
                        {
                            builder.Append(name);
                            continue;
                        }

                        expansionStack.Add(name);
                        if (macro.Parameters.Length == 0)
                        {
                            builder.Append(ExpandMacros(macro.Body, basePosition + start, allowDirectives: false, expansionStack));
                        }
                        else if (TryReadArguments(text, ref index, out var arguments, basePosition + start))
                        {
                            var substituted = SubstituteParameters(macro, arguments);
                            builder.Append(ExpandMacros(substituted, basePosition + start, allowDirectives: false, expansionStack));
                        }
                        else
                        {
                            builder.Append(name);
                        }

                        expansionStack.Remove(name);
                        continue;
                    }

                    builder.Append(name);
                    continue;
                }

                builder.Append(current);
                index++;
            }

            return builder.ToString();
        }

        private bool TryReadArguments(string text, ref int index, out List<string> arguments, int diagnosticBase)
        {
            var lookahead = index;
            while (lookahead < text.Length && char.IsWhiteSpace(text[lookahead]) && text[lookahead] is not '\r' and not '\n')
            {
                lookahead++;
            }

            arguments = new List<string>();

            if (lookahead >= text.Length || text[lookahead] != '(')
            {
                return false;
            }

            lookahead++; // skip '('
            var depth = 0;
            var argStart = lookahead;

            while (lookahead < text.Length)
            {
                var c = text[lookahead];
                if (c == '"' || c == '\'')
                {
                    ReadString(text, ref lookahead);
                    continue;
                }

                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    if (depth == 0)
                    {
                        var value = text.Substring(argStart, lookahead - argStart);
                        arguments.Add(value.Trim());
                        index = lookahead + 1;
                        return true;
                    }

                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    var value = text.Substring(argStart, lookahead - argStart);
                    arguments.Add(value.Trim());
                    argStart = lookahead + 1;
                }

                lookahead++;
            }

            _diagnostics.Add(new Diagnostic
            {
                Id = DiagnosticMacroArgument,
                Message = "Unterminated macro argument list.",
                Span = new Span { Start = diagnosticBase, End = diagnosticBase },
                Origin = new DiagnosticOrigin(CurrentPath, new Span { Start = diagnosticBase, End = diagnosticBase })
            });

            return false;
        }

        private static string SubstituteParameters(MacroDefinition macro, List<string> arguments)
        {
            var builder = new StringBuilder();
            var body = macro.Body;

            var index = 0;
            while (index < body.Length)
            {
                if (IsIdentifierStart(body[index]))
                {
                    var ident = ReadIdentifier(body, ref index).ToString();
                    var parameterIndex = Array.IndexOf(macro.Parameters, ident);
                    if (parameterIndex >= 0 && parameterIndex < arguments.Count)
                    {
                        builder.Append(arguments[parameterIndex]);
                    }
                    else
                    {
                        builder.Append(ident);
                    }
                }
                else
                {
                    builder.Append(body[index]);
                    index++;
                }
            }

            return builder.ToString();
        }

        private static string ReadString(string text, ref int index)
        {
            var start = index;
            var quote = text[index];
            index++; // consume quote
            while (index < text.Length)
            {
                var c = text[index++];
                if (c == '\\' && index < text.Length)
                {
                    index++;
                    continue;
                }

                if (c == quote)
                {
                    break;
                }
            }

            return text.Substring(start, index - start);
        }

        private static string ReadBlockComment(string text, ref int index)
        {
            var start = index;
            index += 2; // /*
            while (index < text.Length)
            {
                if (text[index] == '*' && index + 1 < text.Length && text[index + 1] == '/')
                {
                    index += 2;
                    break;
                }

                index++;
            }

            return text.Substring(start, index - start);
        }

        private static bool IsAtLineStart(string text, int index)
        {
            if (index == 0)
            {
                return true;
            }

            var i = index - 1;
            while (i >= 0 && (text[i] == ' ' || text[i] == '\t'))
            {
                i--;
            }

            return i < 0 || text[i] == '\n' || text[i] == '\r';
        }

        private static ReadOnlySpan<char> ReadIdentifier(ReadOnlySpan<char> text, out ReadOnlySpan<char> rest)
        {
            var index = 0;
            while (index < text.Length && IsIdentifierStart(text[index]))
            {
                index++;
                while (index < text.Length && IsIdentifierPart(text[index]))
                {
                    index++;
                }

                break;
            }

            rest = text.Slice(index);
            return text.Slice(0, index);
        }

        private static ReadOnlySpan<char> ReadIdentifier(string text, ref int index)
        {
            var start = index;
            if (index < text.Length && IsIdentifierStart(text[index]))
            {
                index++;
                while (index < text.Length && IsIdentifierPart(text[index]))
                {
                    index++;
                }
            }

            return text.AsSpan(start, index - start);
        }

        private static ReadOnlySpan<char> ReadToLineEnd(ReadOnlySpan<char> text)
        {
            var index = 0;
            while (index < text.Length && text[index] is not '\r' and not '\n')
            {
                index++;
            }

            return text[..index];
        }

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

        private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';
    }

    private sealed record MacroDefinition(string Name, string[] Parameters, string Body);

    private sealed record ConditionalFrame(bool ParentActive, bool CurrentActive, bool HasTakenTrue);

    private sealed class ExpressionEvaluator
    {
        private readonly string _text;
        private readonly PreprocessorContext _context;
        private int _index;

        public ExpressionEvaluator(PreprocessorContext context, string text)
        {
            _context = context;
            _text = text;
            _index = 0;
        }

        public bool ParseOr()
        {
            var left = ParseAnd();
            while (Match("||"))
            {
                var right = ParseAnd();
                left = left || right;
            }

            return left;
        }

        private bool ParseAnd()
        {
            var left = ParseEquality();
            while (Match("&&"))
            {
                var right = ParseEquality();
                left = left && right;
            }

            return left;
        }

        private bool ParseEquality()
        {
            var left = ParseUnary();
            while (true)
            {
                if (Match("=="))
                {
                    var right = ParseUnary();
                    left = left == right;
                }
                else if (Match("!="))
                {
                    var right = ParseUnary();
                    left = left != right;
                }
                else
                {
                    break;
                }
            }

            return left;
        }

        private bool ParseUnary()
        {
            if (Match("!"))
            {
                return !ParseUnary();
            }

            return ParsePrimary();
        }

        private bool ParsePrimary()
        {
            SkipWhitespace();

            if (Match("("))
            {
                var value = ParseOr();
                Match(")");
                return value;
            }

            if (MatchIdentifier("defined"))
            {
                SkipWhitespace();
                if (Match("("))
                {
                    var name = ReadIdentifierToken();
                    Match(")");
                    return !string.IsNullOrEmpty(name) && _context.IsMacroDefined(name);
                }

                var bare = ReadIdentifierToken();
                return !string.IsNullOrEmpty(bare) && _context.IsMacroDefined(bare);
            }

            var number = ReadNumber();
            if (number.HasValue)
            {
                return number.Value != 0;
            }

            var identifier = ReadIdentifierToken();
            return !string.IsNullOrEmpty(identifier) && _context.IsMacroDefined(identifier);
        }

        private void SkipWhitespace()
        {
            while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
            {
                _index++;
            }
        }

        private bool Match(string token)
        {
            SkipWhitespace();
            if (_text.AsSpan(_index).StartsWith(token, StringComparison.Ordinal))
            {
                _index += token.Length;
                return true;
            }

            return false;
        }

        private bool MatchIdentifier(string token)
        {
            SkipWhitespace();
            var start = _index;
            if (_text.AsSpan(_index).StartsWith(token, StringComparison.Ordinal))
            {
                _index += token.Length;
                if (_index < _text.Length && (char.IsLetterOrDigit(_text[_index]) || _text[_index] == '_'))
                {
                    _index = start;
                    return false;
                }

                return true;
            }

            return false;
        }

        private string? ReadIdentifierToken()
        {
            SkipWhitespace();
            var start = _index;
            if (_index < _text.Length && (char.IsLetter(_text[_index]) || _text[_index] == '_'))
            {
                _index++;
                while (_index < _text.Length && (char.IsLetterOrDigit(_text[_index]) || _text[_index] == '_'))
                {
                    _index++;
                }

                return _text[start.._index];
            }

            return null;
        }

        private int? ReadNumber()
        {
            SkipWhitespace();
            var start = _index;
            var isHex = false;

            if (_index + 1 < _text.Length && _text[_index] == '0' && (_text[_index + 1] is 'x' or 'X'))
            {
                isHex = true;
                _index += 2;
                while (_index < _text.Length && IsHexDigit(_text[_index]))
                {
                    _index++;
                }
            }
            else
            {
                while (_index < _text.Length && char.IsDigit(_text[_index]))
                {
                    _index++;
                }
            }

            if (start == _index)
            {
                return null;
            }

            var slice = _text[start.._index];
            try
            {
                return isHex ? Convert.ToInt32(slice, 16) : int.Parse(slice);
            }
            catch
            {
                return 0;
            }
        }

        private static bool IsHexDigit(char c) =>
            c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }
}
