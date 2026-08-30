using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Artifacts;

[TestClass]
public sealed class ProductionPublishBoundaryTests
{
    [TestMethod]
    public async Task PublishedArtifactLibraryCarriesNoFixtureKeyAdapterOrPayloadFile()
    {
        var repositoryRoot = RepositoryRoot();
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"lex-v3-production-publish-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);

        try
        {
            var start = new ProcessStartInfo("dotnet")
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = repositoryRoot,
            };
            start.ArgumentList.Add("publish");
            start.ArgumentList.Add("src/Lex.V3.Artifacts/Lex.V3.Artifacts.csproj");
            start.ArgumentList.Add("--configuration");
            start.ArgumentList.Add("Release");
            start.ArgumentList.Add("--no-restore");
            start.ArgumentList.Add("--output");
            start.ArgumentList.Add(outputDirectory);

            using var process = Process.Start(start)
                ?? throw new AssertFailedException("Could not start the production publish probe.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            var receipt = (await standardOutput.ConfigureAwait(false)) +
                          (await standardError.ConfigureAwait(false));
            Assert.AreEqual(0, process.ExitCode, receipt);

            var files = Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(outputDirectory, file).Replace('\\', '/'))
                .OrderBy(static file => file, StringComparer.Ordinal)
                .ToArray();
            Assert.IsGreaterThanOrEqualTo(2, files.Length);
            Assert.IsTrue(files.Any(static file =>
                string.Equals(Path.GetFileName(file), "Lex.V3.Artifacts.dll", StringComparison.Ordinal)));
            Assert.IsTrue(files.Any(static file =>
                string.Equals(Path.GetFileName(file), "Lex.V3.Contracts.dll", StringComparison.Ordinal)));

            var forbidden = files.Where(IsForbiddenPublishedPath)
                .ToArray();
            Assert.HasCount(0, forbidden, string.Join(Environment.NewLine, forbidden));
        }
        finally
        {
            var resolvedOutput = Path.GetFullPath(outputDirectory);
            var resolvedTemporaryRoot = Path.GetFullPath(Path.GetTempPath());
            if (!resolvedOutput.StartsWith(resolvedTemporaryRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new AssertFailedException("Publish probe cleanup escaped the temporary root.");
            }

            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("Fixtures/data.bin")]
    [DataRow("SourceAdapter/data.bin")]
    [DataRow("payload/data.bin")]
    [DataRow("keys/privatekey.bin")]
    public void ForbiddenDirectorySegmentsCannotHideBehindSafeLeafNames(string relativePath)
    {
        Assert.IsTrue(IsForbiddenPublishedPath(relativePath));
    }

    private static bool IsForbiddenPublishedPath(string relativePath) =>
        ("/" + relativePath).Contains("/payload/", StringComparison.OrdinalIgnoreCase) ||
        ("/" + relativePath).Contains("/keys/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.Contains("fixture", StringComparison.OrdinalIgnoreCase) ||
        relativePath.Contains("sourceadapter", StringComparison.OrdinalIgnoreCase) ||
        relativePath.Contains("privatekey", StringComparison.OrdinalIgnoreCase) ||
        relativePath.Contains("signingkey", StringComparison.OrdinalIgnoreCase) ||
        relativePath.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) ||
        relativePath.EndsWith(".key", StringComparison.OrdinalIgnoreCase) ||
        relativePath.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) ||
        relativePath.EndsWith(".payload", StringComparison.OrdinalIgnoreCase) ||
        relativePath.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
        relativePath.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ||
        relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
        !relativePath.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase);

    private static string RepositoryRoot()
    {
        foreach (var startingPath in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(startingPath); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new AssertFailedException("Could not locate the V3 repository root.");
    }
}
