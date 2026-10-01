using System.Security.Cryptography;
using System.Text.Json;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest;

/// <summary>One file as written: its name, length and digest, for the build report and the caller.</summary>
public sealed record V3CorpusMountWrittenFile(string Name, long ByteLength, string Sha256);

/// <summary>What a write produced: the directory, the five files and the report beside them.</summary>
public sealed record V3CorpusMountWrite(
    string Directory,
    IReadOnlyList<V3CorpusMountWrittenFile> Files,
    string ReportPath,
    string ReportSha256);

/// <summary>
/// Whether a directory reads back as one consistent mount through the same public verifiers the
/// API's mount uses, and the corpus reference the indexes bind to when it does.
/// </summary>
public sealed record V3CorpusMountVerification(bool Verified, string? Detail, SourceArtifactRef? CorpusRef);

/// <summary>
/// Writes a delivered <see cref="V3FirstMountBuildResult"/> as the <c>v3-corpus</c> directory
/// <c>Lex.V3.Api</c> mounts, and reads such a directory back through the public verifiers.
/// </summary>
/// <remarks>
/// Each file is written to a temporary name beside its final one and moved into place, so a
/// reader never sees a half-written file; the report is written last, and a write that fails
/// removes the temporary files it left. The report is evidence for the run record, not an input
/// to the mount: the API opens the five files by name and verifies them itself, and
/// <see cref="VerifyAsync"/> runs the same public verifiers here so a build can be checked where
/// it was written before it is shipped anywhere. The API additionally caps each capability
/// manifest at 4 MiB when it mounts; this reader does not repeat that cap.
/// </remarks>
public static class V3CorpusMountWriter
{
    public static async Task<V3CorpusMountWrite> WriteAsync(
        V3FirstMountBuildResult build,
        string directory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!build.Delivered)
        {
            throw new ArgumentException($"A refused build is not written: {build.Refusal}: {build.Detail}", nameof(build));
        }

        var target = Path.GetFullPath(directory);
        Directory.CreateDirectory(target);
        var written = new List<V3CorpusMountWrittenFile>(build.Files.Count);
        var reportPath = Path.Combine(target, "build-report.json");
        var reportTemporary = reportPath + ".writing";
        try
        {
            foreach (var file in build.Files)
            {
                var finalPath = Path.Combine(target, file.Name);
                var temporaryPath = finalPath + ".writing";
                await File.WriteAllBytesAsync(temporaryPath, file.Bytes.ToArray(), cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, finalPath, overwrite: true);
                written.Add(new V3CorpusMountWrittenFile(file.Name, file.Bytes.Length, Sha256(file.Bytes.Span)));
            }

            var report = RenderReport(build, written);
            await File.WriteAllBytesAsync(reportTemporary, report, cancellationToken).ConfigureAwait(false);
            File.Move(reportTemporary, reportPath, overwrite: true);
            return new V3CorpusMountWrite(target, written, reportPath, Sha256(report));
        }
        catch
        {
            foreach (var file in build.Files)
            {
                File.Delete(Path.Combine(target, file.Name) + ".writing");
            }

            File.Delete(reportTemporary);
            throw;
        }
    }

    private static byte[] RenderReport(V3FirstMountBuildResult build, IReadOnlyList<V3CorpusMountWrittenFile> written) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "lex-v3-first-mount-report/1",
            writtenUtc = DateTimeOffset.UtcNow,
            corpus = Reference(build.Corpus!.ArtifactRef),
            luxembourgIndex = Reference(build.LuxembourgIndex!.IndexRef),
            luxembourgCapabilityManifest = Reference(build.LuxembourgIndex.CapabilityManifestRef),
            europeIndex = Reference(build.EuropeIndex!.IndexRef),
            europeCapabilityManifest = Reference(build.EuropeIndex.CapabilityManifestRef),
            corpusMembers = build.Corpus.VerifiedSet.Set.Members.Count,
            builtTwiceAndEqual = true,
            files = written,
        }, new JsonSerializerOptions { WriteIndented = true });

    public static async Task<V3CorpusMountVerification> VerifyAsync(string directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        try
        {
            var corpusBytes = await File.ReadAllBytesAsync(
                Path.Combine(directory, V3FirstMountBuildResult.CorpusFileName), cancellationToken).ConfigureAwait(false);
            var corpus = VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(corpusBytes);

            var luxembourgManifest = await File.ReadAllBytesAsync(
                Path.Combine(directory, V3FirstMountBuildResult.LuxembourgCapabilityManifestFileName), cancellationToken)
                .ConfigureAwait(false);
            using var luxembourg = await LuxembourgIndexReader.OpenAndVerifyFileAsync(
                    Path.Combine(directory, V3FirstMountBuildResult.LuxembourgIndexFileName), luxembourgManifest, cancellationToken)
                .ConfigureAwait(false);
            if (luxembourg.CorpusRef != corpus.ArtifactRef)
            {
                return new V3CorpusMountVerification(
                    false,
                    $"the Luxembourg index binds corpus {luxembourg.CorpusRef.Sha256}, the corpus file is {corpus.ArtifactRef.Sha256}",
                    corpus.ArtifactRef);
            }

            luxembourg.VerifyEventLogSources(corpus);

            var europeManifest = await File.ReadAllBytesAsync(
                Path.Combine(directory, V3FirstMountBuildResult.EuropeCapabilityManifestFileName), cancellationToken)
                .ConfigureAwait(false);
            using var europe = await EuropeIndexReader.OpenAndVerifyFileAsync(
                    Path.Combine(directory, V3FirstMountBuildResult.EuropeIndexFileName), europeManifest, corpus.ArtifactRef,
                    cancellationToken)
                .ConfigureAwait(false);
            return new V3CorpusMountVerification(true, null, corpus.ArtifactRef);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException
                                              or UnauthorizedAccessException or JsonException)
        {
            return new V3CorpusMountVerification(false, exception.Message, null);
        }
    }

    private static object Reference(SourceArtifactRef reference) => new { resourceId = reference.ResourceId, reference.Sha256 };

    private static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
