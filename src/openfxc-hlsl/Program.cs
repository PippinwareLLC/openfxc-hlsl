using System.Text;
using System.Text.Json;

internal sealed class Program
{
    private const int FormatVersion = 1;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintUsage();
                return args.Length == 0 ? 1 : 0;
            }

            var command = args[0].ToLowerInvariant();
            var options = ParseArgs(args[1..]);

            var input = ReadInput(options.InputPath);
            var fileName = options.InputPath is null ? "stdin" : Path.GetFileName(options.InputPath);
            var length = input.Length;

            string json = command switch
            {
                "lex" => Serialize(BuildLexResult(fileName, length)),
                "parse" => Serialize(BuildParseResult(fileName, length)),
                _ => throw new InvalidOperationException($"Unknown command '{command}'. Expected 'lex' or 'parse'.")
            };

            WriteOutput(options.OutputPath, json);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static bool IsHelp(string arg) =>
        arg is "-h" or "--help" or "/?" or "-?" or "help";

    private static void PrintUsage()
    {
        Console.WriteLine("openfxc-hlsl <lex|parse> [-i <file>] [-o <file>]");
        Console.WriteLine("Reads HLSL from -i or stdin and emits JSON to -o or stdout.");
    }

    private static CliOptions ParseArgs(string[] args)
    {
        string? input = null;
        string? output = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-i":
                case "--input":
                    input = RequireNext(args, ref i, "input path");
                    break;
                case "-o":
                case "--output":
                    output = RequireNext(args, ref i, "output path");
                    break;
                case "--format":
                    // Accept but ignore for now; only JSON supported.
                    RequireNext(args, ref i, "format");
                    break;
                default:
                    throw new InvalidOperationException($"Unknown argument '{args[i]}'.");
            }
        }

        return new CliOptions(input, output);
    }

    private static string RequireNext(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException($"Missing value for {name}.");
        }

        index += 1;
        return args[index];
    }

    private static string ReadInput(string? path)
    {
        if (path is null)
        {
            using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            return reader.ReadToEnd();
        }

        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static void WriteOutput(string? path, string content)
    {
        if (path is null)
        {
            Console.Out.Write(content);
            return;
        }

        File.WriteAllText(path, content, Encoding.UTF8);
    }

    private static string Serialize<T>(T value)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(value, options);
    }

    private static LexResult BuildLexResult(string fileName, int length) =>
        new(FormatVersion, new SourceInfo(fileName, length), Array.Empty<Token>(), Array.Empty<Diagnostic>());

    private static ParseResult BuildParseResult(string fileName, int length)
    {
        var root = new AstNode
        {
            Id = 1,
            Kind = "CompilationUnit",
            Span = new Span { Start = 0, End = length },
            Children = Array.Empty<AstChild>()
        };

        return new ParseResult(
            FormatVersion,
            new SourceInfo(fileName, length),
            root,
            Array.Empty<Token>(),
            Array.Empty<Diagnostic>());
    }

    private record CliOptions(string? InputPath, string? OutputPath);

    private sealed record SourceInfo(string FileName, int Length);

    private sealed record Span
    {
        public int Start { get; init; }
        public int End { get; init; }
    }

    private sealed record Token
    {
        public string Kind { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
        public Span Span { get; init; } = new();
        public object[] LeadingTrivia { get; init; } = Array.Empty<object>();
        public object[] TrailingTrivia { get; init; } = Array.Empty<object>();
    }

    private sealed record Diagnostic
    {
        public string Id { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public Span Span { get; init; } = new();
    }

    private sealed record LexResult(
        int FormatVersion,
        SourceInfo Source,
        Token[] Tokens,
        Diagnostic[] Diagnostics);

    private sealed record AstChild
    {
        public string Role { get; init; } = string.Empty;
        public AstNode Node { get; init; } = new();
    }

    private sealed record AstNode
    {
        public int Id { get; init; }
        public string Kind { get; init; } = string.Empty;
        public Span Span { get; init; } = new();
        public AstChild[] Children { get; init; } = Array.Empty<AstChild>();
    }

    private sealed record ParseResult(
        int FormatVersion,
        SourceInfo Source,
        AstNode Root,
        Token[] Tokens,
        Diagnostic[] Diagnostics);
}
