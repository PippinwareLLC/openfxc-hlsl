using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace OpenFXC.Hlsl.Tests;

public class CliTests
{
    private const int ExitSuccess = 0;
    private static readonly string RepoRoot = TestPaths.FindRepoRoot();
    private static readonly string ExePath = Path.Combine(RepoRoot, "src", "openfxc-hlsl", "bin", "Debug", "net8.0", "openfxc-hlsl.exe");

    [Fact]
    public void LexCommandProducesTokens()
    {
        var fixture = Path.Combine(RepoRoot, "tests", "fixtures", "basic-sm2.hlsl");
        var json = RunCli("lex", fixture);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(ExitSuccess, 0);
        Assert.Equal("basic-sm2.hlsl", root.GetProperty("source").GetProperty("fileName").GetString());
        Assert.True(root.GetProperty("tokens").GetArrayLength() > 0);
    }

    [Fact]
    public void ParseCommandProducesCompilationUnit()
    {
        var fixture = Path.Combine(RepoRoot, "tests", "fixtures", "basic-sm2.hlsl");
        var json = RunCli("parse", fixture);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("CompilationUnit", root.GetProperty("root").GetProperty("kind").GetString());
        Assert.True(root.GetProperty("tokens").GetArrayLength() > 0);
    }

    private static string RunCli(string command, string inputPath)
    {
        if (!File.Exists(ExePath))
        {
            throw new InvalidOperationException($"CLI executable not found at {ExePath}. Build the project before running tests.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            Arguments = $"{command} -i \"{inputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start CLI process.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != ExitSuccess)
        {
            throw new InvalidOperationException($"CLI exited with code {process.ExitCode}: {error}");
        }

        return output;
    }
}
