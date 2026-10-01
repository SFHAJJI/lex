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
/// Where a chained build's earlier generations come from: the predecessor's own directory (its mount, and the generations
/// it kept), and the generations a published permalink or evidence bundle references, which nothing else records.
/// </summary>
public sealed record V3GenerationSource(string PredecessorDirectory, IReadOnlySet<string> Referenced);

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
    /// <summary>The directory beside a mount's files holding the earlier generations it keeps, one directory per Luxembourg index digest.</summary>
    public const string GenerationsDirectoryName = "generations";

    /// <summary>The retention line's record in <see cref="GenerationsDirectoryName"/>: the policy, the sets it was given and what it decided.</summary>
    public const string RetentionFileName = "retention.json";

    private const string ReportFileName = "build-report.json";

    /// <summary>The files one generation holds: the five mount files and the build report, nothing else (no nested generations).</summary>
    private static readonly string[] GenerationFiles =
    [
        V3FirstMountBuildResult.CorpusFileName,
        V3FirstMountBuildResult.LuxembourgIndexFileName,
        V3FirstMountBuildResult.LuxembourgCapabilityManifestFileName,
        V3FirstMountBuildResult.EuropeIndexFileName,
        V3FirstMountBuildResult.EuropeCapabilityManifestFileName,
        ReportFileName,
    ];

    public static Task<V3CorpusMountWrite> WriteAsync(
        V3FirstMountBuildResult build,
        string directory,
        CancellationToken cancellationToken) =>
        WriteAsync(build, directory, generations: null, cancellationToken);

    /// <summary>
    /// Writes the build and, for a chained build, the earlier generations the retention line keeps
    /// (<see cref="V3GenerationRetention"/>): each copied whole from the predecessor's directory (the predecessor itself,
    /// or a generation it kept) into <see cref="GenerationsDirectoryName"/>, with <see cref="RetentionFileName"/> recording
    /// the decision. Nothing is deleted anywhere: a generation the line drops is simply not copied, and the predecessor's
    /// directory is only read. The output must not hold generations already.
    /// </summary>
    public static async Task<V3CorpusMountWrite> WriteAsync(
        V3FirstMountBuildResult build,
        string directory,
        V3GenerationSource? generations,
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
        if (Directory.Exists(Path.Combine(target, GenerationsDirectoryName)))
        {
            throw new ArgumentException("The output directory already holds generations; a build writes them afresh.", nameof(directory));
        }

        var written = new List<V3CorpusMountWrittenFile>(build.Files.Count);
        var reportPath = Path.Combine(target, ReportFileName);
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

            var kept = generations is null
                ? Array.Empty<V3RetainedGeneration>()
                : await WriteGenerationsAsync(build, target, generations, cancellationToken).ConfigureAwait(false);
            var report = RenderReport(build, written, kept);
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

    /// <summary>
    /// Copies the generations the retention line keeps out of the predecessor's directory and records the decision. The
    /// predecessor must be the index this build's log names as its predecessor; each kept generation is copied file by
    /// file, each file to a temporary name and moved, and its digest checked against the source it was read from.
    /// </summary>
    private static async Task<IReadOnlyList<V3RetainedGeneration>> WriteGenerationsAsync(
        V3FirstMountBuildResult build,
        string target,
        V3GenerationSource generations,
        CancellationToken cancellationToken)
    {
        var log = LogOf(build.LuxembourgIndex!.IndexRef, build.LuxembourgIndex.IndexBytes.Span);
        if (log.Count < 2)
        {
            throw new ArgumentException("A build whose log has no predecessor keeps no generation.", nameof(generations));
        }

        var predecessorDirectory = Path.GetFullPath(generations.PredecessorDirectory);
        var predecessorIndex = Sha256(await File.ReadAllBytesAsync(
            Path.Combine(predecessorDirectory, V3FirstMountBuildResult.LuxembourgIndexFileName), cancellationToken).ConfigureAwait(false));
        if (!string.Equals(predecessorIndex, log[^1].PredecessorIndexSha256, StringComparison.Ordinal))
        {
            throw new ArgumentException("The predecessor directory is not the build this log names as its predecessor.", nameof(generations));
        }

        // Every generation that can be copied: the predecessor itself, and each generation it kept.
        var sources = new Dictionary<string, string>(StringComparer.Ordinal) { [predecessorIndex] = predecessorDirectory };
        var predecessorGenerations = Path.Combine(predecessorDirectory, GenerationsDirectoryName);
        if (Directory.Exists(predecessorGenerations))
        {
            foreach (var held in Directory.GetDirectories(predecessorGenerations))
            {
                sources[Path.GetFileName(held)] = held;
            }
        }

        var decision = V3GenerationRetention.Decide(log, sources.Keys.ToHashSet(StringComparer.Ordinal), generations.Referenced);
        var root = Path.Combine(target, GenerationsDirectoryName);
        Directory.CreateDirectory(root);
        foreach (var kept in decision.Retained)
        {
            var source = sources[kept.IndexSha256];
            var destination = Path.Combine(root, kept.IndexSha256);
            Directory.CreateDirectory(destination);
            foreach (var name in GenerationFiles)
            {
                var bytes = await File.ReadAllBytesAsync(Path.Combine(source, name), cancellationToken).ConfigureAwait(false);
                var temporary = Path.Combine(destination, name + ".writing");
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
                File.Move(temporary, Path.Combine(destination, name));
            }
        }

        await File.WriteAllBytesAsync(Path.Combine(root, RetentionFileName), RenderRetention(decision, generations.Referenced), cancellationToken)
            .ConfigureAwait(false);
        return decision.Retained;
    }

    /// <summary>A Luxembourg index's log as the reader hands it out, read from its verified bytes.</summary>
    private static IReadOnlyList<LuxembourgIndexObservation> LogOf(SourceArtifactRef indexRef, ReadOnlySpan<byte> indexBytes)
    {
        var read = LuxembourgIndexPredecessor.TryRead(indexRef, indexBytes, out var refusal, out var detail)
                   ?? throw new InvalidDataException($"The Luxembourg index's log does not read: {refusal}: {detail}");
        return read.Observations
            .Select(static row => new LuxembourgIndexObservation(row.Observation, row.CorpusSha256, row.PredecessorIndexSha256, row.FirstSeq, row.LastSeq, row.BuiltAt))
            .ToArray();
    }

    private static byte[] RenderRetention(V3RetentionDecision decision, IReadOnlySet<string> referenced) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            policy = decision.PolicyId,
            evaluated_at = decision.EvaluatedAt,
            nightly_days = V3GenerationRetention.NightlyDays,
            referenced = referenced.Order(StringComparer.Ordinal).ToArray(),
            retained = decision.Retained.Select(static kept => new
            {
                index_sha256 = kept.IndexSha256, observation = kept.Observation, built_at = kept.BuiltAt, reasons = kept.Reasons,
            }).ToArray(),
            dropped = decision.Dropped.Select(Generation).ToArray(),
            absent = decision.Absent.Select(Generation).ToArray(),
        }, new JsonSerializerOptions { WriteIndented = true });

    private static object Generation(V3Generation generation) =>
        new { index_sha256 = generation.IndexSha256, observation = generation.Observation, built_at = generation.BuiltAt };

    private static byte[] RenderReport(
        V3FirstMountBuildResult build,
        IReadOnlyList<V3CorpusMountWrittenFile> written,
        IReadOnlyList<V3RetainedGeneration> generations) =>
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
            generations = generations.Select(static kept => new { indexSha256 = kept.IndexSha256, observation = kept.Observation, builtAt = kept.BuiltAt, reasons = kept.Reasons }).ToArray(),
        }, new JsonSerializerOptions { WriteIndented = true });

    public static Task<V3CorpusMountVerification> VerifyAsync(string directory, CancellationToken cancellationToken) =>
        VerifyAsync(directory, asGeneration: false, cancellationToken);

    /// <summary>
    /// A directory verified as a mount, or as a generation: a generation is an earlier build kept beside a mount, its own
    /// earlier builds recorded by the mount that keeps it, so it holds no generations of its own and is not asked for them.
    /// </summary>
    private static async Task<V3CorpusMountVerification> VerifyAsync(string directory, bool asGeneration, CancellationToken cancellationToken)
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
            if (!asGeneration && await VerifyGenerationsAsync(directory, luxembourg, cancellationToken).ConfigureAwait(false) is { } generationFailure)
            {
                return new V3CorpusMountVerification(false, generationFailure, corpus.ArtifactRef);
            }

            return new V3CorpusMountVerification(true, null, corpus.ArtifactRef);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException
                                              or UnauthorizedAccessException or JsonException)
        {
            return new V3CorpusMountVerification(false, exception.Message, null);
        }
    }

    /// <summary>
    /// The generations a mount keeps, each held to the mounted log: its name an index this log names as a predecessor, its
    /// files exactly a generation's, itself a mount that verifies, its Luxembourg index that digest and its corpus the one
    /// its observation names, its log exactly the mounted log up to its own observation; and the retention record the
    /// decision the retention line makes from the generations held and the references recorded. Null when every check
    /// holds or there are none; otherwise what failed.
    /// </summary>
    private static async Task<string?> VerifyGenerationsAsync(
        string directory,
        LuxembourgIndexReader mounted,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(directory, GenerationsDirectoryName);
        var log = mounted.ResolveObservations();
        if (!Directory.Exists(root))
        {
            // A genesis log has no earlier build; a chained one must say what became of each (review of #880): without the
            // record, a mount whose generations were all removed would verify as one that never had any.
            return log.Count > 1
                ? $"the mounted log records {log.Count - 1} earlier build(s) and the mount holds no {GenerationsDirectoryName}/{RetentionFileName} saying what became of them"
                : null;
        }

        var mountedLog = LuxembourgIndexPredecessor.TryRead(
                mounted.IndexRef,
                await File.ReadAllBytesAsync(Path.Combine(directory, V3FirstMountBuildResult.LuxembourgIndexFileName), cancellationToken).ConfigureAwait(false),
                out var refusal, out var detail)
            ?? throw new InvalidDataException($"The mounted log does not read: {refusal}: {detail}");
        var generations = V3GenerationRetention.GenerationsOf(log).ToDictionary(static generation => generation.IndexSha256, StringComparer.Ordinal);
        if (Directory.GetFiles(root).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray() is not [RetentionFileName])
        {
            return $"{GenerationsDirectoryName} holds files other than {RetentionFileName}, or not it";
        }

        var held = Directory.GetDirectories(root).Select(static path => Path.GetFileName(path)!).ToHashSet(StringComparer.Ordinal);
        foreach (var name in held.Order(StringComparer.Ordinal))
        {
            var path = Path.Combine(root, name);
            if (!generations.TryGetValue(name, out var generation))
            {
                return $"generation {name} is no earlier build of the mounted log";
            }

            if (Directory.GetDirectories(path).Length != 0 ||
                !Directory.GetFiles(path).Select(Path.GetFileName).Order(StringComparer.Ordinal).SequenceEqual(GenerationFiles.Order(StringComparer.Ordinal)))
            {
                return $"generation {name} does not hold exactly a generation's files";
            }

            var verified = await VerifyAsync(path, asGeneration: true, cancellationToken).ConfigureAwait(false);
            if (!verified.Verified)
            {
                return $"generation {name} does not verify: {verified.Detail}";
            }

            var indexBytes = await File.ReadAllBytesAsync(Path.Combine(path, V3FirstMountBuildResult.LuxembourgIndexFileName), cancellationToken).ConfigureAwait(false);
            var indexSha256 = Sha256(indexBytes);
            var observation = log[(int)generation.Observation - 1];
            if (!string.Equals(indexSha256, name, StringComparison.Ordinal) ||
                !string.Equals(verified.CorpusRef!.Sha256, observation.CorpusSha256, StringComparison.Ordinal))
            {
                return $"generation {name} is not the build its observation names";
            }

            var own = LuxembourgIndexPredecessor.TryRead(
                    new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(indexSha256), indexSha256), indexBytes, out refusal, out detail)
                ?? throw new InvalidDataException($"generation {name}'s log does not read: {refusal}: {detail}");
            if (!own.Observations.SequenceEqual(mountedLog.Observations.Take((int)generation.Observation)) ||
                !own.Events.SequenceEqual(mountedLog.Events.Take((int)observation.LastSeq)))
            {
                return $"generation {name}'s log is not the mounted log up to its observation";
            }
        }

        // The line decided over the generations the writer could copy: those it kept, which are held here, and those it
        // dropped, which are not (review of #880: deciding over the held ones alone would call a dropped nightly absent). A
        // generation the record calls dropped must have no directory, and the record must be exactly the decision.
        var recorded = await File.ReadAllBytesAsync(Path.Combine(root, RetentionFileName), cancellationToken).ConfigureAwait(false);
        using var record = JsonDocument.Parse(recorded);
        var referenced = record.RootElement.GetProperty("referenced").EnumerateArray().Select(static value => value.GetString()!).ToHashSet(StringComparer.Ordinal);
        var dropped = record.RootElement.GetProperty("dropped").EnumerateArray()
            .Select(static value => value.GetProperty("index_sha256").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        if (dropped.Overlaps(held))
        {
            return $"{RetentionFileName} calls a generation dropped that the mount holds";
        }

        var decision = V3GenerationRetention.Decide(log, held.Union(dropped).ToHashSet(StringComparer.Ordinal), referenced);
        if (!held.SetEquals(decision.Retained.Select(static kept => kept.IndexSha256)) ||
            !RenderRetention(decision, referenced).SequenceEqual(recorded))
        {
            return $"{RetentionFileName} is not the decision the retention line makes from the generations held";
        }

        return null;
    }

    private static object Reference(SourceArtifactRef reference) => new { resourceId = reference.ResourceId, reference.Sha256 };

    private static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
