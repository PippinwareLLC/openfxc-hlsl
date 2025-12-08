using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenFXC.Hlsl;
using Xunit;

namespace OpenFXC.Hlsl.Tests;

public class Fx10ParsingTests
{
    [Fact]
    public void ParsesFx10StateObjectsAndTechnique10()
    {
        var repoRoot = TestPaths.FindRepoRoot();
        var path = Path.Combine(repoRoot, "samples", "custom", "fx10", "deferred_particles_min.fx");
        var text = File.ReadAllText(path);

        var (tokens, lexDiagnostics) = HlslLexer.Lex(text);
        Assert.Empty(lexDiagnostics);

        var (root, parseDiagnostics) = Parser.Parse(tokens, text.Length);
        Assert.Empty(parseDiagnostics);

        var kinds = CollectKinds(root);
        Assert.Contains("DepthStencilStateDeclaration", kinds);
        Assert.Contains("BlendStateDeclaration", kinds);
        Assert.Contains("RasterizerStateDeclaration", kinds);
        Assert.Contains("SamplerState10Declaration", kinds);
        Assert.Contains("Technique10Declaration", kinds);
    }

    private static HashSet<string> CollectKinds(AstNode node)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<AstNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            set.Add(current.Kind);
            foreach (var child in current.Children)
            {
                stack.Push(child.Node);
            }
        }

        return set;
    }
}
