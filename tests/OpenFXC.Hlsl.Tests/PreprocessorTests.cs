using System;
using System.IO;
using System.Linq;
using OpenFXC.Hlsl;
using Xunit;

namespace OpenFXC.Hlsl.Tests;

public class PreprocessorTests
{
    [Fact]
    public void ExpandsMacrosAndIncludes()
    {
        var repoRoot = TestPaths.FindRepoRoot();
        var mainPath = Path.Combine(repoRoot, "samples", "custom", "sm5", "preprocessor_demo_main.hlsl");
        var includeDir = Path.GetDirectoryName(mainPath) ?? repoRoot;
        var text = File.ReadAllText(mainPath);

        var result = Preprocessor.Preprocess(
            text,
            new PreprocessorOptions
            {
                FilePath = mainPath,
                IncludeDirectories = new[] { includeDir }
            });

        Assert.Contains("MultiplyScale(float4 value)", result.Text);
        Assert.Contains("((scaled) * 2.0f)", result.Text);
        Assert.Empty(result.Diagnostics.Where(d => d.Id.StartsWith("HLSL20")));
    }

    [Fact]
    public void EmitsDiagnosticForMissingInclude()
    {
        var text = "#include \"does_not_exist.hlsl\"\nfloat4 main() : SV_Position { return 0; }";
        var result = Preprocessor.Preprocess(
            text,
            new PreprocessorOptions { FilePath = "missing.hlsl", IncludeDirectories = Array.Empty<string>() });

        Assert.Contains(result.Diagnostics, d => d.Id == "HLSL2001");
    }

    [Fact]
    public void AvoidsMacroSelfRecursion()
    {
        var text = "#define LOOP LOOP\nfloat x = LOOP;";
        var result = Preprocessor.Preprocess(text, new PreprocessorOptions());

        Assert.Contains("float x = LOOP;", result.Text);
    }
}
