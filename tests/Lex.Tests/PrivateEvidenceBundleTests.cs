using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Ingest;
using Lex.Law;

namespace Lex.Tests;

public sealed class PrivateEvidenceBundleTests : IDisposable
{
    private const string CodeCommit = "0123456789abcdef0123456789abcdef01234567";
    private const string PreviousBundle =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lex-private-evidence-{Guid.NewGuid():N}");

    public PrivateEvidenceBundleTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Capture_seal_and_separate_readback_preserve_exact_bytes_and_identity()
    {
        var staging = EmptyDirectory("staging");
        var bundle = PrivateEvidenceBundle.Create(staging, BundleIdentity());
        var bytes = new byte[] { 0xef, 0xbb, 0xbf, 0x00, 0xc3, 0x28, 0xff };
        var later = Request(1, SourceRequestMethod.Get,
            "https://legilux.public.lu/eli/etat/leg/loi/2020/12/19/a1068/jo");
        var earlier = Request(0, SourceRequestMethod.Post,
            "https://legilux.public.lu/sparqlendpoint");

        var laterStaged = await bundle.CaptureAsync(
            later, Response("application/xml", bodyComplete: false),
            new MemoryStream(bytes, writable: false));
        var earlierStaged = await bundle.CaptureAsync(
            earlier, Response("application/sparql-results+json"),
            new MemoryStream(bytes, writable: false));
        Assert.Throws<InvalidDataException>(() =>
            bundle.VerifyStagedReadback(staging, laterStaged));
        Directory.CreateDirectory(Path.Combine(
            _root, PrivateEvidenceBundle.ObjectsDirectoryName));
        Directory.CreateDirectory(Path.Combine(
            _root, PrivateEvidenceBundle.ReceiptsDirectoryName));
        File.Copy(
            Assert.Single(Directory.EnumerateFiles(Path.Combine(
                staging, PrivateEvidenceBundle.ObjectsDirectoryName))),
            Path.Combine(_root, PrivateEvidenceBundle.ObjectsDirectoryName,
                laterStaged.ObjectSha256 + ".bin"));
        File.Copy(
            Path.Combine(staging, PrivateEvidenceBundle.ReceiptsDirectoryName,
                laterStaged.RequestId + ".json"),
            Path.Combine(_root, PrivateEvidenceBundle.ReceiptsDirectoryName,
                laterStaged.RequestId + ".json"));
        Assert.Throws<InvalidDataException>(() =>
            bundle.VerifyStagedReadback(_root, laterStaged));
        var readback = CopyBundle(staging, "readback");
        var laterRef = bundle.VerifyStagedReadback(readback, laterStaged);
        var earlierRef = bundle.VerifyStagedReadback(readback, earlierStaged);
        var receipt = await bundle.SealAsync();

        var digest = Sha256(bytes);
        Assert.Equal(digest, laterRef.ObjectSha256);
        Assert.Equal(bytes.LongLength, laterRef.ByteLength);
        Assert.Equal(later.RequestId, laterRef.RequestId);
        Assert.Equal(earlier.RequestId, earlierRef.RequestId);
        Assert.DoesNotContain(typeof(EvidenceRef).GetProperties(), property =>
            property.PropertyType == typeof(byte[])
            || typeof(Stream).IsAssignableFrom(property.PropertyType)
            || property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(StagedEvidenceRef).GetProperties(), property =>
            property.PropertyType == typeof(byte[])
            || typeof(Stream).IsAssignableFrom(property.PropertyType)
            || property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotAssignableFrom<IRawResponseSink>(bundle);
        Assert.Single(Directory.EnumerateFiles(
            Path.Combine(staging, PrivateEvidenceBundle.ObjectsDirectoryName),
            "*", SearchOption.TopDirectoryOnly));
        Assert.Equal(2, Directory.EnumerateFiles(
            Path.Combine(staging, PrivateEvidenceBundle.ReceiptsDirectoryName),
            "*", SearchOption.TopDirectoryOnly).Count());
        Assert.False(File.Exists(Path.Combine(
            staging, PrivateEvidenceBundle.CommitMarkerFileName)));

        var manifestBytes = await File.ReadAllBytesAsync(Path.Combine(
            staging, PrivateEvidenceBundle.ManifestFileName));
        Assert.NotEmpty(manifestBytes);
        Assert.NotEqual(0xef, manifestBytes[0]);
        Assert.Equal((byte)'\n', manifestBytes[^1]);
        Assert.DoesNotContain((byte)'\r', manifestBytes);
        using (var manifest = JsonDocument.Parse(manifestBytes))
        {
            var records = manifest.RootElement.GetProperty("records");
            Assert.Equal([0, 1], records.EnumerateArray()
                .Select(record => record.GetProperty("request")
                    .GetProperty("ordinal").GetInt32()).ToArray());
            Assert.All(records.EnumerateArray(), record =>
                Assert.Equal(digest, record.GetProperty("evidence")
                    .GetProperty("object_sha256").GetString()));
        }

        File.Copy(
            Path.Combine(staging, PrivateEvidenceBundle.ManifestFileName),
            Path.Combine(readback, PrivateEvidenceBundle.ManifestFileName));
        var markerBytes = receipt.CreateCommitMarkerBytes();
        using (var marker = JsonDocument.Parse(markerBytes))
            Assert.Equal(2, marker.RootElement.GetProperty("evidence")
                .GetArrayLength());
        await File.WriteAllBytesAsync(
            Path.Combine(readback, PrivateEvidenceBundle.CommitMarkerFileName),
            markerBytes);

        var verified = PrivateEvidenceBundle.VerifyReadback(readback, receipt);
        Assert.Equal(2, verified.Records.Count);
        using var reopened = verified.OpenBody(earlierRef);
        using var copy = new MemoryStream();
        await reopened.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    [Fact]
    public void Request_identity_is_closed_and_response_metadata_is_bounded()
    {
        var post = Request(0, SourceRequestMethod.Post,
            "https://legilux.public.lu/sparqlendpoint");
        var same = Request(0, SourceRequestMethod.Post,
            "https://legilux.public.lu/sparqlendpoint");
        Assert.Equal(post.RequestId, same.RequestId);
        var retry = new SourceRequestIdentity(
            "legilux", "sparql", SourceRequestMethod.Post,
            "https://legilux.public.lu/sparqlendpoint", Sha256("query"), 0,
            physicalAttempt: 2, redirectHop: 0);
        var redirect = new SourceRequestIdentity(
            "legilux", "sparql", SourceRequestMethod.Post,
            "https://legilux.public.lu/sparqlendpoint", Sha256("query"), 0,
            physicalAttempt: 1, redirectHop: 1);
        Assert.NotEqual(post.RequestId, retry.RequestId);
        Assert.NotEqual(post.RequestId, redirect.RequestId);

        Assert.Throws<InvalidDataException>(() => new SourceRequestIdentity(
            "legilux", "sparql", (SourceRequestMethod)99,
            "https://legilux.public.lu/sparqlendpoint", Sha256("query"), 0));
        Assert.Throws<InvalidDataException>(() => new SourceRequestIdentity(
            "legilux", "sparql", SourceRequestMethod.Get,
            "https://legilux.public.lu/source", Sha256("unexpected"), 0));
        Assert.Throws<InvalidDataException>(() => new SourceRequestIdentity(
            "legilux", "sparql", SourceRequestMethod.Post,
            "https://legilux.public.lu/source", null, 0));
        Assert.Throws<InvalidDataException>(() => new SourceRequestIdentity(
            "legilux", "sparql", SourceRequestMethod.Get,
            "http://legilux.public.lu/source", null, 0));
        Assert.Throws<InvalidDataException>(() => new SourceRequestIdentity(
            "legilux", "sparql", SourceRequestMethod.Get,
            "https://user:secret@legilux.public.lu/source", null, 0));

        var fetchedAt = DateTimeOffset.Parse("2026-08-30T10:11:12Z");
        Assert.Throws<InvalidDataException>(() => new BoundedResponseMetadata(
            99, "text/plain", "utf-8", null, null, fetchedAt,
            "https://legilux.public.lu/source", true));
        Assert.Throws<InvalidDataException>(() => new BoundedResponseMetadata(
            200, new string('x', 257), "utf-8", null, null, fetchedAt,
            "https://legilux.public.lu/source", true));
        Assert.Throws<InvalidDataException>(() => new BoundedResponseMetadata(
            200, "   ", "utf-8", null, null, fetchedAt,
            "https://legilux.public.lu/source", true));
        Assert.Throws<InvalidDataException>(() => new BoundedResponseMetadata(
            200, "text/plain", "utf-8", "bad\rvalue", null, fetchedAt,
            "https://legilux.public.lu/source", true));
        Assert.Throws<InvalidDataException>(() => new BoundedResponseMetadata(
            200, "text/plain", "utf-8", null, null,
            DateTimeOffset.Parse("2026-08-30T12:11:12+02:00"),
            "https://legilux.public.lu/source", true));
        Assert.Throws<InvalidDataException>(() => new SourceRequestIdentity(
            "legilux", "sparql", SourceRequestMethod.Get,
            "https://legilux.public.lu/source", null, 0,
            physicalAttempt: 17));
    }

    [Fact]
    public async Task Failed_capture_leaves_no_publishable_object_and_can_be_retried()
    {
        var staging = EmptyDirectory("interrupted");
        var bundle = PrivateEvidenceBundle.Create(staging, BundleIdentity());
        var request = Request(0, SourceRequestMethod.Get,
            "https://legilux.public.lu/source");

        await Assert.ThrowsAsync<IOException>(() => bundle.CaptureAsync(
            request, Response("application/octet-stream"),
            new ThrowAfterPrefixStream([1, 2, 3])));

        Assert.Empty(Directory.EnumerateFileSystemEntries(
            Path.Combine(staging, PrivateEvidenceBundle.ObjectsDirectoryName)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(
            Path.Combine(staging, PrivateEvidenceBundle.ReceiptsDirectoryName)));
        var reference = await bundle.CaptureAsync(
            request, Response("application/octet-stream"),
            new MemoryStream([1, 2, 3], writable: false));
        Assert.Equal(Sha256(new byte[] { 1, 2, 3 }), reference.ObjectSha256);
    }

    [Fact]
    public async Task Evidence_ref_requires_body_and_response_receipt_readback()
    {
        var staging = EmptyDirectory("receipt-boundary");
        var bundle = PrivateEvidenceBundle.Create(staging, BundleIdentity());
        var staged = await bundle.CaptureAsync(
            Request(0, SourceRequestMethod.Get,
                "https://legilux.public.lu/source"),
            Response("application/xml"),
            new MemoryStream([1, 2, 3], writable: false));
        var readback = CopyBundle(staging, "receipt-boundary-readback");
        var receipt = Assert.Single(Directory.EnumerateFiles(Path.Combine(
            readback, PrivateEvidenceBundle.ReceiptsDirectoryName)));
        var bytes = await File.ReadAllBytesAsync(receipt);
        bytes[0] ^= 0xff;
        await File.WriteAllBytesAsync(receipt, bytes);

        Assert.Throws<InvalidDataException>(() =>
            bundle.VerifyStagedReadback(readback, staged));

        File.Copy(
            Assert.Single(Directory.EnumerateFiles(Path.Combine(
                staging, PrivateEvidenceBundle.ReceiptsDirectoryName))),
            receipt,
            overwrite: true);
        var verified = bundle.VerifyStagedReadback(readback, staged);
        Assert.Equal(staged.RequestId, verified.RequestId);
    }

    [Fact]
    public async Task Seal_rejects_incomplete_ordinals_and_capture_after_seal()
    {
        var unverifiedStaging = EmptyDirectory("unverified");
        var unverified = PrivateEvidenceBundle.Create(
            unverifiedStaging, BundleIdentity());
        await unverified.CaptureAsync(
            Request(0, SourceRequestMethod.Get, "https://legilux.public.lu/source"),
            Response("text/plain"), new MemoryStream([1], writable: false));
        await Assert.ThrowsAsync<InvalidDataException>(() => unverified.SealAsync());

        var staging = EmptyDirectory("sealed");
        var bundle = PrivateEvidenceBundle.Create(staging, BundleIdentity());
        var staged = await bundle.CaptureAsync(
            Request(1, SourceRequestMethod.Get, "https://legilux.public.lu/source"),
            Response("text/plain"), new MemoryStream([1], writable: false));
        var stagedReadback = CopyBundle(staging, "sealed-readback");
        bundle.VerifyStagedReadback(stagedReadback, staged);
        await Assert.ThrowsAsync<InvalidDataException>(() => bundle.SealAsync());

        var completeStaging = EmptyDirectory("complete-sealed");
        var complete = PrivateEvidenceBundle.Create(
            completeStaging, BundleIdentity());
        var request = Request(0, SourceRequestMethod.Get,
            "https://legilux.public.lu/source");
        var completeStaged = await complete.CaptureAsync(
            request, Response("text/plain"),
            new MemoryStream([1], writable: false));
        var completeReadback = CopyBundle(
            completeStaging, "complete-sealed-readback");
        complete.VerifyStagedReadback(completeReadback, completeStaged);
        await complete.SealAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            complete.CaptureAsync(request, Response("text/plain"),
                new MemoryStream([2], writable: false)));
    }

    [Fact]
    public void Staging_root_must_be_absolute_empty_and_link_free()
    {
        Assert.Throws<InvalidDataException>(() => PrivateEvidenceBundle.Create(
            "relative-evidence", BundleIdentity()));

        var nonempty = EmptyDirectory("nonempty");
        File.WriteAllText(Path.Combine(nonempty, "foreign.txt"), "foreign");
        Assert.Throws<InvalidDataException>(() => PrivateEvidenceBundle.Create(
            nonempty, BundleIdentity()));

        var target = EmptyDirectory("link-target");
        var link = Path.Combine(_root, "linked-staging");
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception error) when (error is IOException
                                      or UnauthorizedAccessException
                                      or PlatformNotSupportedException)
        {
            return;
        }
        Assert.Throws<InvalidDataException>(() => PrivateEvidenceBundle.Create(
            link, BundleIdentity()));
    }

    [Theory]
    [InlineData("missing-marker")]
    [InlineData("extra-file")]
    [InlineData("corrupt-object")]
    [InlineData("missing-response-receipt")]
    [InlineData("corrupt-response-receipt")]
    [InlineData("marker-run")]
    [InlineData("marker-code")]
    [InlineData("marker-chain")]
    [InlineData("manifest-request")]
    [InlineData("manifest-length")]
    [InlineData("missing-record")]
    public async Task Readback_verification_fails_closed_on_bundle_mutations(string mutation)
    {
        var (staging, receipt) = await CreateSealedBundle(mutation);
        var readback = CopyBundle(staging, $"readback-{mutation}");
        var markerPath = Path.Combine(
            readback, PrivateEvidenceBundle.CommitMarkerFileName);
        await File.WriteAllBytesAsync(markerPath, receipt.CreateCommitMarkerBytes());
        var objectPath = Assert.Single(Directory.EnumerateFiles(Path.Combine(
            readback, PrivateEvidenceBundle.ObjectsDirectoryName)));
        var responseReceiptPath = Assert.Single(Directory.EnumerateFiles(Path.Combine(
            readback, PrivateEvidenceBundle.ReceiptsDirectoryName)));

        switch (mutation)
        {
            case "missing-marker":
                File.Delete(markerPath);
                break;
            case "extra-file":
                await File.WriteAllTextAsync(Path.Combine(readback, "extra"), "x");
                break;
            case "corrupt-object":
                var corrupt = await File.ReadAllBytesAsync(objectPath);
                corrupt[0] ^= 0xff;
                await File.WriteAllBytesAsync(objectPath, corrupt);
                break;
            case "missing-response-receipt":
                File.Delete(responseReceiptPath);
                break;
            case "corrupt-response-receipt":
                var corruptReceipt = await File.ReadAllBytesAsync(responseReceiptPath);
                corruptReceipt[0] ^= 0xff;
                await File.WriteAllBytesAsync(responseReceiptPath, corruptReceipt);
                break;
            case "marker-run":
            case "marker-code":
            case "marker-chain":
                var marker = JsonNode.Parse(await File.ReadAllTextAsync(markerPath))!.AsObject();
                marker["identity"]![mutation[7..] switch
                {
                    "run" => "run_identity",
                    "code" => "code_commit",
                    _ => "previous_bundle_sha256",
                }] = mutation == "marker-code"
                    ? new string('f', 40)
                    : mutation == "marker-chain"
                        ? new string('b', 64)
                        : "gha:wrong-run";
                await WriteJsonNode(markerPath, marker);
                break;
            case "manifest-request":
            case "manifest-length":
            case "missing-record":
                var manifestPath = Path.Combine(
                    readback, PrivateEvidenceBundle.ManifestFileName);
                var manifest = JsonNode.Parse(
                    await File.ReadAllTextAsync(manifestPath))!.AsObject();
                var records = manifest["records"]!.AsArray();
                if (mutation == "manifest-request")
                    records[0]!["request"]!["channel"] = "different";
                else if (mutation == "manifest-length")
                    records[0]!["evidence"]!["byte_length"] = 999;
                else
                    records.Clear();
                await WriteJsonNode(manifestPath, manifest);
                receipt = ReceiptForMutatedManifest(
                    staging, manifestPath, receipt.Evidence);
                await File.WriteAllBytesAsync(
                    markerPath, receipt.CreateCommitMarkerBytes());
                break;
        }

        Assert.Throws<InvalidDataException>(() =>
            PrivateEvidenceBundle.VerifyReadback(readback, receipt));
    }

    [Fact]
    public async Task Strict_manifest_rejects_unknown_members_even_with_matching_receipt()
    {
        var (staging, originalReceipt) = await CreateSealedBundle("unknown-member");
        var readback = CopyBundle(staging, "readback-unknown-member");
        var manifestPath = Path.Combine(
            readback, PrivateEvidenceBundle.ManifestFileName);
        var manifest = JsonNode.Parse(
            await File.ReadAllTextAsync(manifestPath))!.AsObject();
        manifest["unknown"] = true;
        await WriteJsonNode(manifestPath, manifest);
        var receipt = ReceiptForMutatedManifest(
            staging, manifestPath, originalReceipt.Evidence);
        await File.WriteAllBytesAsync(Path.Combine(
            readback, PrivateEvidenceBundle.CommitMarkerFileName),
            receipt.CreateCommitMarkerBytes());

        Assert.Throws<InvalidDataException>(() =>
            PrivateEvidenceBundle.VerifyReadback(readback, receipt));
    }

    [Fact]
    public async Task Readback_must_be_a_separate_directory_and_match_trusted_identity()
    {
        var (staging, receipt) = await CreateSealedBundle("separate");
        await File.WriteAllBytesAsync(Path.Combine(
            staging, PrivateEvidenceBundle.CommitMarkerFileName),
            receipt.CreateCommitMarkerBytes());
        Assert.Throws<InvalidDataException>(() =>
            PrivateEvidenceBundle.VerifyReadback(staging, receipt));

        File.Delete(Path.Combine(staging, PrivateEvidenceBundle.CommitMarkerFileName));
        var readback = CopyBundle(staging, "readback-wrong-identity");
        var wrong = new PrivateEvidenceBundleReceipt(
            staging,
            new PrivateEvidenceBundleIdentity(
                "gha:different", CodeCommit, "legilux", 1, PreviousBundle),
            receipt.ManifestSha256,
            receipt.Evidence);
        await File.WriteAllBytesAsync(Path.Combine(
            readback, PrivateEvidenceBundle.CommitMarkerFileName),
            wrong.CreateCommitMarkerBytes());

        Assert.Throws<InvalidDataException>(() =>
            PrivateEvidenceBundle.VerifyReadback(readback, wrong));
    }

    private async Task<(string Staging, PrivateEvidenceBundleReceipt Receipt)>
        CreateSealedBundle(string name)
    {
        var staging = EmptyDirectory($"staging-{name}");
        var bundle = PrivateEvidenceBundle.Create(staging, BundleIdentity());
        var staged = await bundle.CaptureAsync(
            Request(0, SourceRequestMethod.Post,
                "https://legilux.public.lu/sparqlendpoint"),
            Response("application/sparql-results+json"),
            new MemoryStream(Encoding.UTF8.GetBytes("{\"head\":{}}"), writable: false));
        var readback = CopyBundle(staging, $"object-readback-{name}");
        bundle.VerifyStagedReadback(readback, staged);
        return (staging, await bundle.SealAsync());
    }

    private PrivateEvidenceBundleReceipt ReceiptForMutatedManifest(
        string staging,
        string manifestPath,
        IReadOnlyCollection<EvidenceRef> evidence) => new(
        staging, BundleIdentity(), Sha256(File.ReadAllBytes(manifestPath)), evidence);

    private static PrivateEvidenceBundleIdentity BundleIdentity() => new(
        "gha:2026-08-30T101112Z", CodeCommit, "legilux", 1, PreviousBundle);

    private static SourceRequestIdentity Request(
        int ordinal, SourceRequestMethod method, string uri) => new(
        "legilux", "sparql", method, uri,
        method == SourceRequestMethod.Post ? Sha256("query") : null,
        ordinal);

    private static BoundedResponseMetadata Response(
        string contentType, bool bodyComplete = true) => new(
        200, contentType, "utf-8", "\"publisher-etag\"",
        DateTimeOffset.Parse("2026-08-29T09:00:00Z"),
        DateTimeOffset.Parse("2026-08-30T10:11:12Z"),
        "https://legilux.public.lu/final", bodyComplete);

    private string EmptyDirectory(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private string CopyBundle(string source, string name)
    {
        var destination = Path.Combine(_root, name);
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(
                     source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination,
                Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(
                     source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            File.Copy(file, target);
        }
        return destination;
    }

    private static async Task WriteJsonNode(string path, JsonNode node) =>
        await File.WriteAllTextAsync(path,
            node.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private static string Sha256(string value) =>
        Sha256(Encoding.UTF8.GetBytes(value));

    private static string Sha256(byte[] value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class ThrowAfterPrefixStream(byte[] prefix) : Stream
    {
        private bool _returnedPrefix;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_returnedPrefix) throw new IOException("injected interrupted response");
            _returnedPrefix = true;
            prefix.CopyTo(buffer);
            return ValueTask.FromResult(prefix.Length);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
