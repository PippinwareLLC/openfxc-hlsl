using System.Text;
using System.Text.Json;
using OpenFXC.Hlsl;
using System.Linq;

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
            var (tokens, diagnostics) = HlslLexer.Lex(input);

            string json = command switch
            {
                "lex" => Serialize(new LexResult(FormatVersion, new SourceInfo(fileName, length), tokens, diagnostics)),
                "parse" => Serialize(BuildParseResult(fileName, length, tokens, diagnostics)),
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

    private static ParseResult BuildParseResult(string fileName, int length, Token[] tokens, Diagnostic[] lexDiagnostics)
    {
        var (root, parseDiagnostics) = Parser.Parse(tokens, length);
        var allDiagnostics = lexDiagnostics.Concat(parseDiagnostics).ToArray();

        return new ParseResult(
            FormatVersion,
            new SourceInfo(fileName, length),
            root,
            tokens,
            allDiagnostics);
    }

    private record CliOptions(string? InputPath, string? OutputPath);
}
