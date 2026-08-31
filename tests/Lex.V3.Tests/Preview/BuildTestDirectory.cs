namespace Lex.V3.Tests.Preview;

internal sealed class BuildTestDirectory : IDisposable
{
    public BuildTestDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "lex-v3-preview-tests",
            Guid.NewGuid().ToString("N"));
    }

    public string Path { get; }

    public void Dispose()
    {
        if (!Directory.Exists(Path))
        {
            return;
        }

        var resolvedPath = System.IO.Path.GetFullPath(Path);
        var resolvedTemp = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        if (!resolvedPath.StartsWith(resolvedTemp, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to remove a test directory outside the temporary root.");
        }

        Directory.Delete(resolvedPath, recursive: true);
    }
}
