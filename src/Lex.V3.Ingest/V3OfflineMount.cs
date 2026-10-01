using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest;

/// <summary>Checked acquisition inputs for a complete mount rebuilt without publisher traffic.</summary>
public static class V3OfflineMount
{
    private const string Schema = "lex-v3-offline-mount-inputs/1";

    public sealed class BuildClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }

    public static async Task<SourceArtifactRef> CaptureAsync(ICustodyStore store,
        EuFirstMountAcquisitionResult europe, LuxembourgFirstMountAcquisitionResult luxembourg,
        IReadOnlyList<string> seeds, LuxembourgActRange act, DateTimeOffset buildTime,
        V3GenerationSource? generations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!europe.Delivered || !luxembourg.Delivered || europe.CheckpointRef is null || luxembourg.CheckpointRef is null)
            throw new CustodyRequiredException("Both acquisitions must retain complete checkpoints before offline derivation.");
        var previous = new List<RetainedFile>();
        if (generations is not null)
        {
            var verified = await V3CorpusMountWriter.VerifyAsync(generations.PredecessorDirectory, cancellationToken).ConfigureAwait(false);
            if (!verified.Verified) throw new CustodyIntegrityException("Predecessor mount does not verify: " + verified.Detail);
            foreach (var file in Directory.EnumerateFiles(generations.PredecessorDirectory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(generations.PredecessorDirectory, file).Replace('\\', '/');
                if (!AdmittedPath(relative)) throw new CustodyIntegrityException("Predecessor holds an unexpected mount file: " + relative);
                var bytes = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
                await HoldAsync(store, bytes, cancellationToken).ConfigureAwait(false);
                previous.Add(new(relative, CustodyDigest.Of(bytes)));
            }
        }
        var root = new Inputs(Schema, europe.CheckpointRef, luxembourg.CheckpointRef,
            seeds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), act,
            V3FirstMountBuild.BuildTimeOf(buildTime), previous.OrderBy(static file => file.Path, StringComparer.Ordinal).ToArray(),
            generations?.Referenced.Order(StringComparer.Ordinal).ToArray() ?? []);
        var encoded = Encode(root);
        await HoldAsync(store, encoded, cancellationToken).ConfigureAwait(false);
        return new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", CustodyDigest.Of(encoded));
    }

    /// <summary>The input clock describes the original build, never a fresh publisher observation.</summary>
    public static async Task<V3CorpusMountWrite> DeriveAsync(ICustodyStore store, SourceArtifactRef checkpoint,
        string outputDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        var target = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            throw new ArgumentException("Offline derivation requires an empty output directory.", nameof(outputDirectory));
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        Inputs root;
        try
        {
            root = ContractJson.Deserialize<Inputs>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (root.Schema != Schema || !bytes.Span.SequenceEqual(Encode(root)) || root.Europe is null || root.Luxembourg is null ||
                root.Seeds is null || root.Seeds.Length == 0 || root.Seeds.Any(string.IsNullOrWhiteSpace) || root.Act is null ||
                !root.Seeds.SequenceEqual(root.Seeds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                root.BuildTime.Offset != TimeSpan.Zero || root.BuildTime != V3FirstMountBuild.BuildTimeOf(root.BuildTime) ||
                root.Predecessor is null || root.Referenced is null || root.Predecessor.Any(static file => file is null || !AdmittedPath(file.Path)) ||
                !root.Predecessor.Select(static file => file.Path).SequenceEqual(root.Predecessor.Select(static file => file.Path).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                root.Referenced.Any(static digest => digest is null || digest.Length != 64 || digest.Any(static c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) ||
                root.Predecessor.Length == 0 && root.Referenced.Length != 0)
                throw new CustodyIntegrityException("Offline mount catalog framing or scope is invalid.");
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Offline mount catalog cannot be verified.", exception);
        }
        var staging = Path.Combine(Path.GetTempPath(), "lex-v3-offline-predecessor-" + Guid.NewGuid().ToString("N"));
        try
        {
            LuxembourgIndexPredecessor? predecessor = null;
            V3GenerationSource? generations = null;
            if (root.Predecessor.Length != 0)
            {
                foreach (var file in root.Predecessor)
                {
                    var retained = await CustodyRestore.ReadByDigestCheckedAsync(store, file.Sha256, cancellationToken).ConfigureAwait(false);
                    var path = Path.Combine(staging, file.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllBytesAsync(path, retained.ToArray(), cancellationToken).ConfigureAwait(false);
                }
                var verified = await V3CorpusMountWriter.VerifyAsync(staging, cancellationToken).ConfigureAwait(false);
                if (!verified.Verified) throw new CustodyIntegrityException("Retained predecessor fails mount verification: " + verified.Detail);
                predecessor = V3FirstMountBuild.ReadPredecessor(staging, out var refusal, out var detail)
                    ?? throw new CustodyIntegrityException($"Retained predecessor refused: {refusal}: {detail}");
                generations = new(staging, root.Referenced.ToHashSet(StringComparer.Ordinal));
            }
            var europe = await EuFirstMountAcquisition.ReopenAsync(store, root.Europe, root.Seeds, cancellationToken).ConfigureAwait(false);
            var luxembourg = await LuxembourgFirstMountAcquisition.ReopenAsync(store, root.Luxembourg, root.Act, cancellationToken).ConfigureAwait(false);
            var build = await new V3FirstMountBuild(store, new BuildClock(root.BuildTime))
                .RunAsync(europe, luxembourg, predecessor, cancellationToken).ConfigureAwait(false);
            if (!build.Delivered) throw new CustodyIntegrityException($"Offline mount derivation refused: {build.Refusal}: {build.Detail}");
            var write = await V3CorpusMountWriter.WriteAsync(build, target, generations, cancellationToken, root.BuildTime).ConfigureAwait(false);
            var verification = await V3CorpusMountWriter.VerifyAsync(target, cancellationToken).ConfigureAwait(false);
            if (!verification.Verified) throw new CustodyIntegrityException("Derived mount does not reopen: " + verification.Detail);
            return write;
        }
        finally
        {
            // This unpredictable directory is created solely for this invocation's verified inputs.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static bool AdmittedPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var parts = path.Split('/');
        var files = new[] { V3FirstMountBuildResult.CorpusFileName, V3FirstMountBuildResult.LuxembourgIndexFileName,
            V3FirstMountBuildResult.LuxembourgCapabilityManifestFileName, V3FirstMountBuildResult.EuropeIndexFileName,
            V3FirstMountBuildResult.EuropeCapabilityManifestFileName, "build-report.json" };
        return parts.Length == 1 && files.Contains(parts[0], StringComparer.Ordinal) ||
            parts.Length == 2 && parts[0] == "generations" && parts[1] == "retention.json" ||
            parts.Length == 3 && parts[0] == "generations" && parts[1].Length == 64 &&
            parts[1].All(static c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')) && files.Contains(parts[2], StringComparer.Ordinal);
    }
    private static async Task HoldAsync(ICustodyStore store, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        var (receipt, failure) = await CustodyHold.TryHoldAsync(store, bytes, cancellationToken).ConfigureAwait(false);
        if (receipt is null) throw new CustodyRequiredException("Offline mount input cannot be retained: " + failure);
        if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes.Span) || receipt.Reference.ByteLength != bytes.Length)
            throw new CustodyIntegrityException("Offline mount input receipt differs from its bytes.");
    }
    private static byte[] Encode(Inputs root) => Encoding.UTF8.GetBytes(ContractJson.Serialize(root));
    private sealed record RetainedFile(string Path, string Sha256);
    private sealed record Inputs(string Schema, SourceArtifactRef Europe, SourceArtifactRef Luxembourg, string[] Seeds,
        LuxembourgActRange Act, DateTimeOffset BuildTime, RetainedFile[] Predecessor, string[] Referenced);
}
