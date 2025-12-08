using System.IO;
using System.Linq;
using OpenFXC.Hlsl;
using Xunit;

namespace OpenFXC.Hlsl.Tests;

public class DiagnosticsOriginTests
{
    [Fact]
    public void ParseDiagnosticsReportIncludedFilePath()
    {
        var repoRoot = TestPaths.FindRepoRoot();
        var rootPath = Path.Combine(repoRoot, "tests", "fixtures", "include_origin_root.hlsl");
        var includePath = Path.Combine(repoRoot, "tests", "fixtures", "include_origin_child.hlsl");
        var text = File.ReadAllText(rootPath);

        var pre = Preprocessor.Preprocess(text, new PreprocessorOptions { FilePath = rootPath });
        var (tokens, lexDiagnostics) = HlslLexer.Lex(pre.Text);
        var mappedLex = pre.SourceMap.AttachOrigins(lexDiagnostics);
        var (_, parseDiagnostics) = Parser.Parse(tokens, pre.Text.Length);
        var mappedParse = pre.SourceMap.AttachOrigins(parseDiagnostics);

        var allDiagnostics = mappedLex.Concat(mappedParse).ToArray();
        Assert.Contains(allDiagnostics, d => d.Origin is not null && d.Origin.FileName == includePath);
    }
}
