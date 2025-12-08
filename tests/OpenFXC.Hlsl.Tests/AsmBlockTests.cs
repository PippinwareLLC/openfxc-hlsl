using OpenFXC.Hlsl;
using Xunit;

namespace OpenFXC.Hlsl.Tests;

public class AsmBlockTests
{
    [Fact]
    public void ParsesAsmBlockInitializerWithoutDiagnostics()
    {
        var repoRoot = TestPaths.FindRepoRoot();
        var path = Path.Combine(repoRoot, "tests", "fixtures", "asm-block.fx");
        var text = File.ReadAllText(path);

        var (tokens, lexDiagnostics) = HlslLexer.Lex(text);
        Assert.Empty(lexDiagnostics);

        var (root, parseDiagnostics) = Parser.Parse(tokens, text.Length);
        Assert.Empty(parseDiagnostics);

        Assert.Contains(FlattenKinds(root), k => k == "AsmBlock");
    }

    private static HashSet<string> FlattenKinds(AstNode root)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<AstNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            set.Add(node.Kind);
            foreach (var child in node.Children)
            {
                stack.Push(child.Node);
            }
        }

        return set;
    }
}
