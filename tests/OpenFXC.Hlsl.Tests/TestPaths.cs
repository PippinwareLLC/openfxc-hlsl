namespace OpenFXC.Hlsl.Tests;

internal static class TestPaths
{
    public static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "README.md")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate repository root.");
        }

        return dir;
    }
}
