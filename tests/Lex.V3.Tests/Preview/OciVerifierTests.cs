using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class OciVerifierTests
{
    private static readonly byte[] GraphArtifactBytes = Encoding.UTF8.GetBytes("{\"schema\":\"lex-v3-synthetic-slice-artifact/1\"}");

    [TestMethod]
    public void CleanArchiveAndPublicGraphProduceBoundedResult()
    {
        using var root = new BuildTestDirectory();
        Directory.CreateDirectory(root.Path);
        var graphRoot = Path.Combine(root.Path, "graph");
        Directory.CreateDirectory(graphRoot);
        File.WriteAllBytes(Path.Combine(graphRoot, "artifact.json"), GraphArtifactBytes);

        var archivePath = Path.Combine(root.Path, "preview.tar");
        WriteOciArchive(archivePath, CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))));
        var resultPath = Path.Combine(root.Path, "verification.json");
        var dockerArchivePath = Path.Combine(root.Path, "runtime-docker.tar");

        var result = RunVerifier(archivePath, graphRoot, resultPath, dockerArchivePath: dockerArchivePath);

        Assert.AreEqual(0, result.ExitCode, result.Output);
        using var document = JsonDocument.Parse(File.ReadAllBytes(resultPath));
        var rootElement = document.RootElement;
        Assert.AreEqual("lex-v3-s0-05-oci-verification/1", rootElement.GetProperty("schema").GetString());
        Assert.AreEqual(BaseDigestFor(archivePath), rootElement.GetProperty("base_image_digest").GetString());
        Assert.AreEqual(2, rootElement.GetProperty("layer_count").GetInt32());
        Assert.IsTrue(rootElement.GetProperty("checks").EnumerateObject().All(property => property.Value.GetBoolean()));

        var dockerFiles = ReadTarFiles(dockerArchivePath);
        var configName = rootElement.GetProperty("config_digest").GetString()!["sha256:".Length..] + ".json";
        Assert.AreEqual(4, dockerFiles.Count);
        Assert.IsTrue(dockerFiles.ContainsKey(configName));
        Assert.AreEqual(configName[..^".json".Length], Sha256(dockerFiles[configName]));
        using var dockerManifest = JsonDocument.Parse(dockerFiles["manifest.json"]);
        var dockerImage = dockerManifest.RootElement[0];
        Assert.AreEqual(configName, dockerImage.GetProperty("Config").GetString());
        CollectionAssert.AreEqual(
            new[] { "lex-v3-s0-05:test" },
            dockerImage.GetProperty("RepoTags").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.AreEqual(2, dockerImage.GetProperty("Layers").GetArrayLength());
        Assert.IsTrue(
            dockerImage.GetProperty("Layers").EnumerateArray().All(
                value => value.GetString() is { } path && path.EndsWith("/layer.tar", StringComparison.Ordinal) && dockerFiles.ContainsKey(path)));
        Assert.AreEqual(Sha256(File.ReadAllBytes(dockerArchivePath)), rootElement.GetProperty("docker_archive_sha256").GetString());

        var secondResultPath = Path.Combine(root.Path, "verification-second.json");
        var secondDockerArchivePath = Path.Combine(root.Path, "runtime-docker-second.tar");
        var secondResult = RunVerifier(archivePath, graphRoot, secondResultPath, dockerArchivePath: secondDockerArchivePath);
        Assert.AreEqual(0, secondResult.ExitCode, secondResult.Output);
        CollectionAssert.AreEqual(File.ReadAllBytes(resultPath), File.ReadAllBytes(secondResultPath));
        CollectionAssert.AreEqual(File.ReadAllBytes(dockerArchivePath), File.ReadAllBytes(secondDockerArchivePath));
    }

    [TestMethod]
    public void GzipLayerIsExpandedHashedAndScanned()
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        var archivePath = Path.Combine(root.Path, "preview.tar");
        WriteOciArchive(
            archivePath,
            CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))),
            gzipLayer: true);

        var result = RunVerifier(archivePath, graphRoot, Path.Combine(root.Path, "verification.json"));

        Assert.AreEqual(0, result.ExitCode, result.Output);

        var dockerMediaArchive = Path.Combine(root.Path, "preview-docker-media.tar");
        WriteOciArchive(
            dockerMediaArchive,
            CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))),
            gzipLayer: true,
            dockerLayerMediaType: true);
        var dockerMediaResult = RunVerifier(dockerMediaArchive, graphRoot, Path.Combine(root.Path, "verification-docker-media.json"));
        Assert.AreEqual(0, dockerMediaResult.ExitCode, dockerMediaResult.Output);
    }

    [TestMethod]
    public void SafeRelativeLinkIsInspectedWithoutBeingFollowed()
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        var archivePath = Path.Combine(root.Path, "preview.tar");
        WriteOciArchive(archivePath, CreateLinkLayer("app/link", "target"));

        var result = RunVerifier(archivePath, graphRoot, Path.Combine(root.Path, "verification.json"));

        Assert.AreEqual(0, result.ExitCode, result.Output);
    }

    [TestMethod]
    [DataRow("../escape")]
    [DataRow("/absolute")]
    [DataRow("app/line\nfeed")]
    public void ParentAndAbsoluteLayerPathsFailClosed(string path)
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        var archivePath = Path.Combine(root.Path, "preview.tar");
        WriteOciArchive(archivePath, CreateLayer((path, Encoding.ASCII.GetBytes("payload"))));
        var dockerArchivePath = Path.Combine(root.Path, "runtime-docker.tar");

        var result = RunVerifier(
            archivePath,
            graphRoot,
            Path.Combine(root.Path, "verification.json"),
            dockerArchivePath: dockerArchivePath);

        Assert.AreNotEqual(0, result.ExitCode);
        Assert.IsFalse(File.Exists(dockerArchivePath));
    }

    [TestMethod]
    public void ParentPathInOuterArchiveFailsBeforeAnyBlobIsTrusted()
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        var archivePath = Path.Combine(root.Path, "preview.tar");
        WriteOciArchive(
            archivePath,
            CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))),
            extraOuterEntry: ("../outside", Encoding.ASCII.GetBytes("payload")));

        var result = RunVerifier(archivePath, graphRoot, Path.Combine(root.Path, "verification.json"));

        Assert.AreNotEqual(0, result.ExitCode);
    }

    [TestMethod]
    public void DuplicateNormalizedLayerPathFailsClosed()
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        var archivePath = Path.Combine(root.Path, "preview.tar");
        WriteOciArchive(
            archivePath,
            CreateLayer(
                ("app/value", Encoding.ASCII.GetBytes("first")),
                ("app/./value", Encoding.ASCII.GetBytes("second"))));

        var result = RunVerifier(archivePath, graphRoot, Path.Combine(root.Path, "verification.json"));

        Assert.AreNotEqual(0, result.ExitCode);
    }

    [TestMethod]
    public void EscapingLinkDeviceAndWhiteoutFailClosed()
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        foreach (var layer in new[]
                 {
                     CreateLinkLayer("app/link", "../../../outside"),
                     CreateSpecialLayer(TarEntryType.CharacterDevice, "dev/escape"),
                     CreateLayer(("app/.wh.hidden", Array.Empty<byte>())),
                 })
        {
            var archivePath = Path.Combine(root.Path, $"preview-{Guid.NewGuid():N}.tar");
            WriteOciArchive(archivePath, layer);
            var result = RunVerifier(archivePath, graphRoot, Path.Combine(root.Path, $"verification-{Guid.NewGuid():N}.json"));
            Assert.AreNotEqual(0, result.ExitCode, "A hostile link, node, or whiteout was admitted.");
        }
    }

    [TestMethod]
    [DataRow("-----BEGIN PRIVATE KEY-----")]
    [DataRow("Lex.V3.Preview.dll")]
    [DataRow("lex-index/2")]
    [DataRow("lex-artifacts/1")]
    public void ForbiddenLayerCanariesFailClosed(string canary)
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        var archivePath = Path.Combine(root.Path, "preview.tar");
        WriteOciArchive(archivePath, CreateLayer(("app/payload.bin", Encoding.UTF8.GetBytes(canary))));

        var result = RunVerifier(archivePath, graphRoot, Path.Combine(root.Path, "verification.json"));

        Assert.AreNotEqual(0, result.ExitCode);
    }

    [TestMethod]
    public void BinaryPkcs8PrivateKeyFailsClosed()
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var archivePath = Path.Combine(root.Path, "preview.tar");
        WriteOciArchive(archivePath, CreateLayer(("app/key.der", key.ExportPkcs8PrivateKey())));

        var result = RunVerifier(archivePath, graphRoot, Path.Combine(root.Path, "verification.json"));

        Assert.AreNotEqual(0, result.ExitCode);
    }

    [TestMethod]
    public void ExactPrivateCanaryRawBytesFailClosed()
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        var canary = SHA256.HashData(Encoding.ASCII.GetBytes("private canary material"));
        var canaryDigest = Convert.ToHexStringLower(canary);
        var archivePath = Path.Combine(root.Path, "preview.tar");
        WriteOciArchive(archivePath, CreateLayer(("app/canary.bin", canary)));

        var result = RunVerifier(archivePath, graphRoot, Path.Combine(root.Path, "verification.json"), canaryDigest);

        Assert.AreNotEqual(0, result.ExitCode);
    }

    [TestMethod]
    public void SecretEnvironmentAndNondeterministicLabelFailClosed()
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        var cleanLayer = CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api")));

        var secretArchive = Path.Combine(root.Path, "secret-env.tar");
        WriteOciArchive(secretArchive, cleanLayer, environment: new[] { "API_KEY=value" });
        Assert.AreNotEqual(0, RunVerifier(secretArchive, graphRoot, Path.Combine(root.Path, "secret-result.json")).ExitCode);

        var timestampArchive = Path.Combine(root.Path, "timestamp-label.tar");
        WriteOciArchive(timestampArchive, cleanLayer, additionalLabel: ("org.opencontainers.image.created", "2026-08-30T00:00:00Z"));
        Assert.AreNotEqual(0, RunVerifier(timestampArchive, graphRoot, Path.Combine(root.Path, "timestamp-result.json")).ExitCode);
    }

    [TestMethod]
    public void DescriptorSizeMismatchAndUnreferencedBlobFailClosed()
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        var cleanLayer = CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api")));

        var badSizeArchive = Path.Combine(root.Path, "bad-size.tar");
        WriteOciArchive(badSizeArchive, cleanLayer, layerSizeDelta: 1);
        Assert.AreNotEqual(0, RunVerifier(badSizeArchive, graphRoot, Path.Combine(root.Path, "bad-size-result.json")).ExitCode);

        var extraBlobArchive = Path.Combine(root.Path, "extra-blob.tar");
        WriteOciArchive(extraBlobArchive, cleanLayer, extraBlob: Encoding.ASCII.GetBytes("unreferenced"));
        Assert.AreNotEqual(0, RunVerifier(extraBlobArchive, graphRoot, Path.Combine(root.Path, "extra-blob-result.json")).ExitCode);

        var badDigestArchive = Path.Combine(root.Path, "bad-digest.tar");
        WriteOciArchive(badDigestArchive, cleanLayer, corruptLayerDigest: true);
        Assert.AreNotEqual(0, RunVerifier(badDigestArchive, graphRoot, Path.Combine(root.Path, "bad-digest-result.json")).ExitCode);
    }

    [TestMethod]
    public void EnvironmentCountBoundFailsClosed()
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        var archivePath = Path.Combine(root.Path, "preview.tar");
        WriteOciArchive(
            archivePath,
            CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))),
            environment: Enumerable.Range(0, 65).Select(index => $"VALUE_{index}=safe").ToArray());

        var result = RunVerifier(archivePath, graphRoot, Path.Combine(root.Path, "verification.json"));

        Assert.AreNotEqual(0, result.ExitCode);
    }

    [TestMethod]
    public void PublicGraphCanaryFailsWithoutLeavingAStaleResult()
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        File.WriteAllText(Path.Combine(graphRoot, "leak.txt"), "-----BEGIN EC PRIVATE KEY-----", Encoding.ASCII);
        var archivePath = Path.Combine(root.Path, "preview.tar");
        WriteOciArchive(archivePath, CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))));
        var resultPath = Path.Combine(root.Path, "verification.json");
        File.WriteAllText(resultPath, "stale pass", Encoding.ASCII);

        var result = RunVerifier(archivePath, graphRoot, resultPath);

        Assert.AreNotEqual(0, result.ExitCode);
        Assert.IsFalse(File.Exists(resultPath));
    }

    [TestMethod]
    public void MissingChangedAndExtraPackagedGraphFilesFailClosed()
    {
        using var root = CreateRootWithGraph(out var graphRoot);

        var missingArchive = Path.Combine(root.Path, "missing.tar");
        WriteOciArchive(missingArchive, CreateLayerWithoutGraph(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))));
        Assert.AreNotEqual(0, RunVerifier(missingArchive, graphRoot, Path.Combine(root.Path, "missing-result.json")).ExitCode);

        var changedArchive = Path.Combine(root.Path, "changed.tar");
        WriteOciArchive(
            changedArchive,
            CreateLayerWithoutGraph(
                ("app/preview-graph/artifact.json", Encoding.ASCII.GetBytes("changed")),
                ("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))));
        Assert.AreNotEqual(0, RunVerifier(changedArchive, graphRoot, Path.Combine(root.Path, "changed-result.json")).ExitCode);

        var extraArchive = Path.Combine(root.Path, "extra.tar");
        WriteOciArchive(
            extraArchive,
            CreateLayer(
                ("app/preview-graph/unreviewed.json", Encoding.ASCII.GetBytes("{}")),
                ("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))));
        Assert.AreNotEqual(0, RunVerifier(extraArchive, graphRoot, Path.Combine(root.Path, "extra-result.json")).ExitCode);

        var lowerLayerArchive = Path.Combine(root.Path, "lower-layer.tar");
        WriteOciArchive(
            lowerLayerArchive,
            CreateLayerWithoutGraph(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))),
            lowerLayer: CreateLayer(("base/file", Encoding.ASCII.GetBytes("base"))));
        Assert.AreNotEqual(0, RunVerifier(lowerLayerArchive, graphRoot, Path.Combine(root.Path, "lower-layer-result.json")).ExitCode);
    }

    [TestMethod]
    public void VerificationResultCannotBeWrittenIntoThePublicGraph()
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        var archivePath = Path.Combine(root.Path, "preview.tar");
        WriteOciArchive(archivePath, CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))));
        var resultPath = Path.Combine(graphRoot, "verification.json");

        var result = RunVerifier(archivePath, graphRoot, resultPath);

        Assert.AreNotEqual(0, result.ExitCode);
        Assert.IsFalse(File.Exists(resultPath));
    }

    [TestMethod]
    public void DockerArchiveCannotAliasTrustedBaseInputs()
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        var archivePath = Path.Combine(root.Path, "preview.tar");
        WriteOciArchive(archivePath, CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))));
        var baseIndexPath = BaseIndexPathFor(archivePath);
        var baseIndexBytes = File.ReadAllBytes(baseIndexPath);

        var result = RunVerifier(
            archivePath,
            graphRoot,
            Path.Combine(root.Path, "verification.json"),
            dockerArchivePath: baseIndexPath);

        Assert.AreNotEqual(0, result.ExitCode);
        StringAssert.Contains(result.Output, "Docker runtime archive aliases the trusted base index.");
        CollectionAssert.AreEqual(baseIndexBytes, File.ReadAllBytes(baseIndexPath));
    }

    [TestMethod]
    public void TrustedBaseIndexSubstitutionFailsClosed()
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        var indexArchive = Path.Combine(root.Path, "index-substitution.tar");
        WriteOciArchive(indexArchive, CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))));
        var pinnedDigest = BaseDigestFor(indexArchive);
        File.AppendAllText(BaseIndexPathFor(indexArchive), " ", Encoding.ASCII);
        Assert.AreNotEqual(
            0,
            RunVerifier(indexArchive, graphRoot, Path.Combine(root.Path, "index-result.json"), expectedBaseDigest: pinnedDigest).ExitCode);
    }

    [TestMethod]
    public void TrustedBaseChildSubstitutionFailsClosed()
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        var childArchive = Path.Combine(root.Path, "child-substitution.tar");
        WriteOciArchive(childArchive, CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))));
        var manifestText = File.ReadAllText(BaseManifestPathFor(childArchive), Encoding.UTF8);
        File.WriteAllText(
            BaseManifestPathFor(childArchive),
            manifestText.Replace(new string('c', 64), new string('d', 64), StringComparison.Ordinal),
            new UTF8Encoding(false));
        Assert.AreNotEqual(0, RunVerifier(childArchive, graphRoot, Path.Combine(root.Path, "child-result.json")).ExitCode);
    }

    [TestMethod]
    public void BaseDigestLabelMustMatchTheTrustedPlatformManifest()
    {
        using var root = CreateRootWithGraph(out var graphRoot);
        var archive = Path.Combine(root.Path, "wrong-base-label.tar");
        WriteOciArchive(
            archive,
            CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))),
            baseDigestLabelOverride: "sha256:" + new string('0', 64));

        Assert.AreNotEqual(
            0,
            RunVerifier(archive, graphRoot, Path.Combine(root.Path, "wrong-base-label-result.json")).ExitCode);
    }

    [TestMethod]
    public void ChangedBasePrefixFailsClosedWhileBoundBaseWhiteoutIsAccepted()
    {
        using var root = CreateRootWithGraph(out var graphRoot);

        var changedPrefixArchive = Path.Combine(root.Path, "changed-prefix.tar");
        WriteOciArchive(
            changedPrefixArchive,
            CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))),
            changeFinalBasePrefix: true);
        Assert.AreNotEqual(0, RunVerifier(changedPrefixArchive, graphRoot, Path.Combine(root.Path, "changed-prefix-result.json")).ExitCode);

        var baseWhiteoutArchive = Path.Combine(root.Path, "base-whiteout.tar");
        WriteOciArchive(
            baseWhiteoutArchive,
            CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))),
            lowerLayer: CreateLayerWithoutGraph(
                ("usr/share/.wh.retired", Array.Empty<byte>()),
                ("usr/share/base", Encoding.ASCII.GetBytes("base"))));
        var baseWhiteoutResult = RunVerifier(baseWhiteoutArchive, graphRoot, Path.Combine(root.Path, "base-whiteout-result.json"));
        Assert.AreEqual(0, baseWhiteoutResult.ExitCode, baseWhiteoutResult.Output);
    }

    [TestMethod]
    public void ConfigAndApplicationHistoryCreatedFieldsMustBeWellFormedUtc()
    {
        using var root = CreateRootWithGraph(out var graphRoot);

        var validCreatedArchive = Path.Combine(root.Path, "valid-created.tar");
        WriteOciArchive(
            validCreatedArchive,
            CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))),
            topLevelCreated: "2026-08-31T00:00:00.1234567Z",
            appHistoryCreated: "2026-08-31T00:00:00.1234567Z");
        Assert.AreEqual(
            0,
            RunVerifier(
                validCreatedArchive,
                graphRoot,
                Path.Combine(root.Path, "valid-created-result.json")).ExitCode);

        var malformedConfigArchive = Path.Combine(root.Path, "malformed-config-created.tar");
        WriteOciArchive(
            malformedConfigArchive,
            CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))),
            topLevelCreated: "not-an-instant");
        Assert.AreNotEqual(
            0,
            RunVerifier(
                malformedConfigArchive,
                graphRoot,
                Path.Combine(root.Path, "malformed-config-created-result.json")).ExitCode);

        var nonUtcHistoryArchive = Path.Combine(root.Path, "non-utc-history-created.tar");
        WriteOciArchive(
            nonUtcHistoryArchive,
            CreateLayer(("app/Lex.V3.Api.dll", Encoding.ASCII.GetBytes("clean-api"))),
            appHistoryCreated: "2026-08-31T01:00:00+01:00");
        Assert.AreNotEqual(
            0,
            RunVerifier(
                nonUtcHistoryArchive,
                graphRoot,
                Path.Combine(root.Path, "non-utc-history-created-result.json")).ExitCode);
    }

    private static ProcessResult RunVerifier(
        string archivePath,
        string graphRoot,
        string resultPath,
        string? canaryDigest = null,
        string? expectedBaseDigest = null,
        string? dockerArchivePath = null)
    {
        var repositoryRoot = FindRepositoryRoot();
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "eng", "verify-s0-05-preview.ps1"));
        startInfo.ArgumentList.Add("-ArchivePath");
        startInfo.ArgumentList.Add(archivePath);
        startInfo.ArgumentList.Add("-PublicGraphRoot");
        startInfo.ArgumentList.Add(graphRoot);
        startInfo.ArgumentList.Add("-ExpectedBaseImageDigest");
        startInfo.ArgumentList.Add(expectedBaseDigest ?? BaseDigestFor(archivePath));
        startInfo.ArgumentList.Add("-BaseIndexPath");
        startInfo.ArgumentList.Add(BaseIndexPathFor(archivePath));
        startInfo.ArgumentList.Add("-BaseManifestPath");
        startInfo.ArgumentList.Add(BaseManifestPathFor(archivePath));
        startInfo.ArgumentList.Add("-ResultPath");
        startInfo.ArgumentList.Add(resultPath);
        if (dockerArchivePath is not null)
        {
            startInfo.ArgumentList.Add("-DockerArchivePath");
            startInfo.ArgumentList.Add(dockerArchivePath);
            startInfo.ArgumentList.Add("-DockerImageReference");
            startInfo.ArgumentList.Add("lex-v3-s0-05:test");
        }
        if (canaryDigest is not null)
        {
            startInfo.ArgumentList.Add("-ExpectedPrivateKeyCanarySha256");
            startInfo.ArgumentList.Add(canaryDigest);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(standardOutput, standardError);
        return new ProcessResult(process.ExitCode, standardOutput.Result + standardError.Result);
    }

    private static Dictionary<string, byte[]> ReadTarFiles(string path)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var stream = File.OpenRead(path);
        using var reader = new TarReader(stream);
        while (reader.GetNextEntry() is { } entry)
        {
            Assert.AreEqual(TarEntryType.RegularFile, entry.EntryType);
            using var content = new MemoryStream();
            entry.DataStream!.CopyTo(content);
            files.Add(entry.Name, content.ToArray());
        }

        return files;
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "eng", "verify-v3-tree.ps1")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static BuildTestDirectory CreateRootWithGraph(out string graphRoot)
    {
        var root = new BuildTestDirectory();
        Directory.CreateDirectory(root.Path);
        graphRoot = Path.Combine(root.Path, "graph");
        Directory.CreateDirectory(graphRoot);
        File.WriteAllBytes(Path.Combine(graphRoot, "artifact.json"), GraphArtifactBytes);
        return root;
    }

    private static byte[] CreateLayer(params (string Name, byte[] Bytes)[] entries)
    {
        return CreateLayerCore(true, entries);
    }

    private static byte[] CreateLayerWithoutGraph(params (string Name, byte[] Bytes)[] entries)
    {
        return CreateLayerCore(false, entries);
    }

    private static byte[] CreateLayerCore(bool includeGraph, params (string Name, byte[] Bytes)[] entries)
    {
        using var output = new MemoryStream();
        using (var writer = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: true))
        {
            if (includeGraph)
            {
                WriteEntry(writer, "app/preview-graph/artifact.json", GraphArtifactBytes);
            }
            foreach (var entry in entries)
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, entry.Name)
                {
                    DataStream = new MemoryStream(entry.Bytes, writable: false),
                });
            }
        }

        return output.ToArray();
    }

    private static byte[] CreateLinkLayer(string name, string target)
    {
        using var output = new MemoryStream();
        using (var writer = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: true))
        {
            WriteEntry(writer, "app/preview-graph/artifact.json", GraphArtifactBytes);
            writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, name) { LinkName = target });
        }
        return output.ToArray();
    }

    private static byte[] CreateSpecialLayer(TarEntryType type, string name)
    {
        using var output = new MemoryStream();
        using (var writer = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: true))
        {
            WriteEntry(writer, "app/preview-graph/artifact.json", GraphArtifactBytes);
            writer.WriteEntry(new PaxTarEntry(type, name));
        }
        return output.ToArray();
    }

    private static void WriteOciArchive(
        string archivePath,
        byte[] layerBytes,
        bool gzipLayer = false,
        string[]? environment = null,
        (string Name, string Value)? additionalLabel = null,
        int layerSizeDelta = 0,
        byte[]? extraBlob = null,
        bool corruptLayerDigest = false,
        (string Name, byte[] Bytes)? extraOuterEntry = null,
        byte[]? lowerLayer = null,
        bool dockerLayerMediaType = false,
        bool changeFinalBasePrefix = false,
        string? topLevelCreated = null,
        string? appHistoryCreated = null,
        string? baseDigestLabelOverride = null)
    {
        var layers = new List<(byte[] Raw, byte[] Stored, string Digest, string MediaType)>();
        var baseLayer = lowerLayer ?? CreateLayerWithoutGraph(("usr/share/lex-base", Encoding.ASCII.GetBytes("trusted-base")));
        layers.Add((baseLayer, baseLayer, Sha256(baseLayer), "application/vnd.oci.image.layer.v1.tar"));
        var storedLayerBytes = gzipLayer ? Gzip(layerBytes) : layerBytes;
        layers.Add((
            layerBytes,
            storedLayerBytes,
            corruptLayerDigest ? new string('b', 64) : Sha256(storedLayerBytes),
            gzipLayer
                ? dockerLayerMediaType
                    ? "application/vnd.docker.image.rootfs.diff.tar.gzip"
                    : "application/vnd.oci.image.layer.v1.tar+gzip"
                : dockerLayerMediaType
                    ? "application/vnd.docker.image.rootfs.diff.tar"
                    : "application/vnd.oci.image.layer.v1.tar"));

        var baseManifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            mediaType = "application/vnd.oci.image.manifest.v1+json",
            config = new
            {
                mediaType = "application/vnd.oci.image.config.v1+json",
                digest = "sha256:" + new string('c', 64),
                size = 2,
            },
            layers = new[]
            {
                new
                {
                    mediaType = layers[0].MediaType,
                    digest = "sha256:" + layers[0].Digest,
                    size = layers[0].Stored.Length,
                },
            },
        });
        var baseManifestDigest = Sha256(baseManifest);
        var baseIndex = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            mediaType = "application/vnd.oci.image.index.v1+json",
            manifests = new[]
            {
                new
                {
                    mediaType = "application/vnd.oci.image.manifest.v1+json",
                    digest = "sha256:" + baseManifestDigest,
                    size = baseManifest.Length,
                    platform = new { architecture = "amd64", os = "linux" },
                },
            },
        });
        File.WriteAllBytes(BaseManifestPathFor(archivePath), baseManifest);
        File.WriteAllBytes(BaseIndexPathFor(archivePath), baseIndex);
        var baseDigest = "sha256:" + Sha256(baseIndex);
        if (changeFinalBasePrefix)
        {
            var changedBaseLayer = CreateLayerWithoutGraph(("usr/share/changed-base", Encoding.ASCII.GetBytes("changed")));
            layers[0] = (changedBaseLayer, changedBaseLayer, Sha256(changedBaseLayer), "application/vnd.oci.image.layer.v1.tar");
        }
        var labels = new Dictionary<string, string>
        {
            ["org.opencontainers.image.authors"] = "Lex.V3.Api",
            ["org.opencontainers.image.version"] = "1.0.0",
            ["org.opencontainers.image.base.name"] =
                "mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled@" + baseDigest,
            ["net.dot.runtime.majorminor"] = "10.0",
            ["net.dot.sdk.version"] = "10.0.400",
            ["org.opencontainers.image.base.digest"] =
                baseDigestLabelOverride ?? "sha256:" + baseManifestDigest,
        };
        if (additionalLabel is { } label)
        {
            labels[label.Name] = label.Value;
        }
        var config = JsonSerializer.SerializeToUtf8Bytes(new
        {
            architecture = "amd64",
            os = "linux",
            config = new
            {
                User = "1654",
                Env = environment ?? new[] { "ASPNETCORE_HTTP_PORTS=8080" },
                Labels = labels,
            },
            rootfs = new { type = "layers", diff_ids = layers.Select(layer => "sha256:" + Sha256(layer.Raw)).ToArray() },
            history = layers.Select((_, index) => new { created_by = index == 0 ? "trusted base" : "dotnet publish" }).ToArray(),
        });
        if (topLevelCreated is not null || appHistoryCreated is not null)
        {
            var configNode = JsonNode.Parse(config)!.AsObject();
            if (topLevelCreated is not null)
            {
                configNode["created"] = topLevelCreated;
            }
            if (appHistoryCreated is not null)
            {
                configNode["history"]!.AsArray()[^1]!.AsObject()["created"] = appHistoryCreated;
            }
            config = JsonSerializer.SerializeToUtf8Bytes(configNode);
        }
        var configDigest = Sha256(config);
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            mediaType = "application/vnd.oci.image.manifest.v1+json",
            config = new
            {
                mediaType = "application/vnd.oci.image.config.v1+json",
                digest = "sha256:" + configDigest,
                size = config.Length,
            },
            layers = layers.Select((layer, index) => new
            {
                mediaType = layer.MediaType,
                digest = "sha256:" + layer.Digest,
                size = layer.Stored.Length + (index == layers.Count - 1 ? layerSizeDelta : 0),
            }).ToArray(),
        });
        var manifestDigest = Sha256(manifest);
        var index = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            manifests = new[]
            {
                new
                {
                    mediaType = "application/vnd.oci.image.manifest.v1+json",
                    digest = "sha256:" + manifestDigest,
                    size = manifest.Length,
                },
            },
        });

        using var output = File.Create(archivePath);
        using var writer = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: false);
        WriteEntry(writer, "oci-layout", Encoding.ASCII.GetBytes("{\"imageLayoutVersion\":\"1.0.0\"}"));
        WriteEntry(writer, "index.json", index);
        WriteEntry(writer, $"blobs/sha256/{manifestDigest}", manifest);
        WriteEntry(writer, $"blobs/sha256/{configDigest}", config);
        foreach (var layer in layers)
        {
            WriteEntry(writer, $"blobs/sha256/{layer.Digest}", layer.Stored);
        }
        if (extraBlob is not null)
        {
            WriteEntry(writer, $"blobs/sha256/{Sha256(extraBlob)}", extraBlob);
        }
        if (extraOuterEntry is { } outerEntry)
        {
            WriteEntry(writer, outerEntry.Name, outerEntry.Bytes);
        }
    }

    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(bytes);
        }
        return output.ToArray();
    }

    private static void WriteEntry(TarWriter writer, string name, byte[] bytes)
    {
        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
        {
            DataStream = new MemoryStream(bytes, writable: false),
        });
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string BaseIndexPathFor(string archivePath) => archivePath + ".base-index.json";

    private static string BaseManifestPathFor(string archivePath) => archivePath + ".base-manifest.json";

    private static string BaseDigestFor(string archivePath) => "sha256:" + Sha256(File.ReadAllBytes(BaseIndexPathFor(archivePath)));

    private sealed record ProcessResult(int ExitCode, string Output);
}
