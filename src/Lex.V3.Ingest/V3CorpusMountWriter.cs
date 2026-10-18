using System.Globalization;
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
    string ReportSha256)
{
    /// <summary>The record of the re-derivation the Luxembourg derivations come from, as the report states it; null without one.</summary>
    public SourceArtifactRef? LuxembourgRederivation { get; init; }
}

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
/// Whether a mount's generations hold to its log (<see cref="V3CorpusMountWriter.VerifyGenerationsAsync"/>): what failed,
/// or null; and when they hold, the retention line's decision they carry (null when the mount keeps none).
/// </summary>
public sealed record V3GenerationsVerification(string? Failure, V3RetentionDecision? Decision);

/// <summary>
/// Writes a delivered <see cref="V3FirstMountBuildResult"/> as the <c>v3-corpus</c> directory
/// <c>Lex.V3.Api</c> mounts, and reads such a directory back through the public verifiers.
/// </summary>
/// <remarks>
/// Each file is written to a temporary name beside its final one and moved into place, so a
/// reader never sees a half-written file; the report is written last, and a write that fails
/// removes the temporary files it left. An index is never read into memory (slice S4b): the file
/// the build staged is moved to the temporary name (copied, when an earlier write already moved it
/// into another mount), hashed from disk and held to the digest its two builds compared, and its
/// log is read from a private copy. The report is evidence for the run record, not an input
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
        CancellationToken cancellationToken, DateTimeOffset? derivationInputUtc = null)
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
        // The staged index files this write moved out of the build's staging directory, each with where it was staged.
        var taken = new List<(V3BuiltIndexFile Index, string StagedPath)>();
        try
        {
            foreach (var file in build.Files)
            {
                var finalPath = Path.Combine(target, file.Name);
                var temporaryPath = finalPath + ".writing";
                var (sha256, byteLength) = file.Index is { } index
                    ? await PlaceIndexAsync(file.Name, index, temporaryPath, taken, cancellationToken).ConfigureAwait(false)
                    : await WriteHeldAsync(file.Held, temporaryPath, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, finalPath, overwrite: true);
                if (file.Index is { } placed)
                {
                    placed.Path = finalPath;
                }

                written.Add(new V3CorpusMountWrittenFile(file.Name, byteLength, sha256));
            }

            var writtenIndex = Path.Combine(target, V3FirstMountBuildResult.LuxembourgIndexFileName);
            IReadOnlyList<V3RetainedGeneration> kept;
            if (generations is not null)
            {
                kept = await WriteGenerationsAsync(build, writtenIndex, target, generations, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Written without its predecessor's directory, a chained build holds none of its earlier builds, and says so.
                await WriteRetentionRecordFileAsync(target, build.LuxembourgIndex!.IndexRef, writtenIndex, cancellationToken)
                    .ConfigureAwait(false);
                kept = [];
            }
            var report = RenderReport(build, written, kept, derivationInputUtc);
            await File.WriteAllBytesAsync(reportTemporary, report, cancellationToken).ConfigureAwait(false);
            File.Move(reportTemporary, reportPath, overwrite: true);
            return new V3CorpusMountWrite(target, written, reportPath, Sha256(report))
            {
                LuxembourgRederivation = build.LuxembourgDerivation?.Record.Reference,
            };
        }
        catch
        {
            // A failed write gives the build back the staged files it took (the review of #986): each is moved back to where it
            // was staged, from its temporary name or from its place in this unfinished mount, so the build can be written again.
            GiveBack(taken);
            foreach (var file in build.Files)
            {
                File.Delete(Path.Combine(target, file.Name) + ".writing");
            }

            File.Delete(reportTemporary);
            throw;
        }
    }

    /// <summary>
    /// Moves each index a failed write took out of the build's staging directory back there. Best effort: the write's own
    /// failure is the one reported, and a file that cannot be moved back stays where the failed write left it.
    /// </summary>
    private static void GiveBack(IReadOnlyList<(V3BuiltIndexFile Index, string StagedPath)> taken)
    {
        foreach (var (index, stagedPath) in taken)
        {
            try
            {
                if (File.Exists(index.Path) && !File.Exists(stagedPath))
                {
                    File.Move(index.Path, stagedPath);
                    index.Path = stagedPath;
                    index.Staged = true;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Left where the failed write put it; the build's next write copies it from there or reports it missing.
            }
        }
    }

    /// <summary>
    /// A held file (the corpus, a capability manifest) written to <paramref name="temporaryPath"/> straight from its bytes,
    /// with its digest and length.
    /// </summary>
    private static async Task<(string Sha256, long Length)> WriteHeldAsync(
        ReadOnlyMemory<byte> bytes,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        await using (var stream = new FileStream(
                         temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous))
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        return (Sha256(bytes.Span), bytes.Length);
    }

    /// <summary>
    /// An index the build wrote to a file, placed at <paramref name="temporaryPath"/>: moved out of the build's staging
    /// directory (a rename on one volume; recorded in <paramref name="taken"/>, so a failed write can move it back), or copied
    /// from the mount an earlier write moved it into; then hashed from disk and held to the digest and length its two builds
    /// compared, so what is moved into place is what was compared.
    /// </summary>
    private static async Task<(string Sha256, long Length)> PlaceIndexAsync(
        string name,
        V3BuiltIndexFile index,
        string temporaryPath,
        List<(V3BuiltIndexFile Index, string StagedPath)> taken,
        CancellationToken cancellationToken)
    {
        if (index.Staged)
        {
            taken.Add((index, index.Path));
            File.Move(index.Path, temporaryPath, overwrite: true);
            index.Path = temporaryPath;
            index.Staged = false;
        }
        else
        {
            File.Copy(index.Path, temporaryPath, overwrite: true);
        }

        var (sha256, length) = await V3BuiltIndexFile.Sha256OfFileAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(sha256, index.IndexRef.Sha256, StringComparison.Ordinal) || length != index.ByteLength)
        {
            throw new InvalidDataException(
                $"The {name} the build wrote is {sha256} ({length} bytes), not the {index.IndexRef.Sha256} ({index.ByteLength} bytes) its two builds compared.");
        }

        return (sha256, length);
    }

    /// <summary>
    /// Copies the generations the retention line keeps out of the predecessor's directory and records the decision. The
    /// predecessor must be the index this build's log names as its predecessor; each kept generation is copied file by
    /// file, each file to a temporary name and moved.
    /// </summary>
    private static async Task<IReadOnlyList<V3RetainedGeneration>> WriteGenerationsAsync(
        V3FirstMountBuildResult build,
        string writtenIndex,
        string target,
        V3GenerationSource generations,
        CancellationToken cancellationToken)
    {
        var log = LogOfFile(build.LuxembourgIndex!.IndexRef, writtenIndex);
        if (log.Count < 2)
        {
            throw new ArgumentException("A build whose log has no predecessor keeps no generation.", nameof(generations));
        }

        var predecessorDirectory = Path.GetFullPath(generations.PredecessorDirectory);
        var (predecessorIndex, _) = await V3BuiltIndexFile.Sha256OfFileAsync(
            Path.Combine(predecessorDirectory, V3FirstMountBuildResult.LuxembourgIndexFileName), cancellationToken).ConfigureAwait(false);
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
                cancellationToken.ThrowIfCancellationRequested();
                var temporary = Path.Combine(destination, name + ".writing");
                File.Copy(Path.Combine(source, name), temporary);
                File.Move(temporary, Path.Combine(destination, name));
            }
        }

        await File.WriteAllBytesAsync(Path.Combine(root, RetentionFileName), RenderRetention(decision, generations.Referenced), cancellationToken)
            .ConfigureAwait(false);
        return decision.Retained;
    }

    /// <summary>
    /// Records, for a chained mount, the retention line's decision over the earlier builds held beside it
    /// (<paramref name="present"/>, none by default: each earlier build absent, none claimed). A genesis log has no earlier
    /// build and gets no record. The caller places the held generations; this writes only the record.
    /// </summary>
    internal static Task WriteRetentionRecordAsync(
        string directory,
        SourceArtifactRef indexRef,
        ReadOnlyMemory<byte> indexBytes,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? present = null) =>
        WriteRetentionRecordAsync(directory, LogOf(indexRef, indexBytes.Span), present, cancellationToken);

    /// <summary><see cref="WriteRetentionRecordAsync(string, SourceArtifactRef, ReadOnlyMemory{byte}, CancellationToken, IReadOnlySet{string}?)"/> with the log read from the index file at <paramref name="indexPath"/>.</summary>
    internal static Task WriteRetentionRecordFileAsync(
        string directory,
        SourceArtifactRef indexRef,
        string indexPath,
        CancellationToken cancellationToken) =>
        WriteRetentionRecordAsync(directory, LogOfFile(indexRef, indexPath), present: null, cancellationToken);

    private static async Task WriteRetentionRecordAsync(
        string directory,
        IReadOnlyList<LuxembourgIndexObservation> log,
        IReadOnlySet<string>? present,
        CancellationToken cancellationToken)
    {
        if (log.Count < 2)
        {
            return;
        }

        var none = new HashSet<string>(StringComparer.Ordinal);
        var root = Path.Combine(directory, GenerationsDirectoryName);
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(Path.Combine(root, RetentionFileName), RenderRetention(V3GenerationRetention.Decide(log, present ?? none, none), none), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>A Luxembourg index's log as the reader hands it out, read from its verified bytes.</summary>
    private static IReadOnlyList<LuxembourgIndexObservation> LogOf(SourceArtifactRef indexRef, ReadOnlySpan<byte> indexBytes) =>
        ObservationsOf(LuxembourgIndexPredecessor.TryReadLog(indexRef, indexBytes, out var refusal, out var detail), refusal, detail);

    /// <summary>A Luxembourg index's log as the reader hands it out, read from a private copy of its file, held to its digest.</summary>
    private static IReadOnlyList<LuxembourgIndexObservation> LogOfFile(SourceArtifactRef indexRef, string indexPath) =>
        ObservationsOf(LuxembourgIndexPredecessor.TryReadLogFile(indexRef, indexPath, out var refusal, out var detail), refusal, detail);

    private static IReadOnlyList<LuxembourgIndexObservation> ObservationsOf(
        LuxembourgIndexPredecessor? read, LuxembourgIndexBuildRefusal refusal, string? detail) =>
        (read ?? throw new InvalidDataException($"The Luxembourg index's log does not read: {refusal}: {detail}")).Observations
            .Select(static row => new LuxembourgIndexObservation(row.Observation, row.CorpusSha256, row.PredecessorIndexSha256, row.FirstSeq, row.LastSeq, row.BuiltAt, row.DerivedAt))
            .ToArray();

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
        IReadOnlyList<V3RetainedGeneration> generations, DateTimeOffset? derivationInputUtc)
    {
        var report = new
        {
            schema = "lex-v3-first-mount-report/1",
            writtenUtc = derivationInputUtc is null ? DateTimeOffset.UtcNow : (DateTimeOffset?)null,
            derivationInputUtc,
            corpus = Reference(build.Corpus!.ArtifactRef),
            luxembourgIndex = Reference(build.LuxembourgIndex!.IndexRef),
            luxembourgCapabilityManifest = Reference(build.LuxembourgIndex.CapabilityManifestRef),
            europeIndex = Reference(build.EuropeIndex!.IndexRef),
            europeCapabilityManifest = Reference(build.EuropeIndex.CapabilityManifestRef),
            corpusMembers = build.Corpus.VerifiedSet.Set.Members.Count,
            builtTwiceAndEqual = true,
            files = written,
            generations = generations.Select(static kept => new { indexSha256 = kept.IndexSha256, observation = kept.Observation, builtAt = kept.BuiltAt, reasons = kept.Reasons }).ToArray(),
        };
        var options = new JsonSerializerOptions { WriteIndented = true };
        var resumption = Resumption(build.EuropeResumption, build.LuxembourgResumption);
        var derivation = Derivation(build.LuxembourgDerivation, build.LuxembourgResumption);
        if (resumption is null && derivation is null)
        {
            return JsonSerializer.SerializeToUtf8Bytes(report, options);
        }

        var node = JsonSerializer.SerializeToNode(report, options)!.AsObject();
        if (resumption is not null)
        {
            node["resumption"] = JsonSerializer.SerializeToNode(resumption, options);
        }

        if (derivation is not null)
        {
            node["derivation"] = JsonSerializer.SerializeToNode(derivation, options);
        }

        return JsonSerializer.SerializeToUtf8Bytes(node, options);
    }

    /// <summary>
    /// What a mount whose Luxembourg derivations were re-derived from the acquisition's custody says about it: which
    /// acquisition (for a resumed one, its window and both spends), the derivation time its inputs state, under which rules
    /// and policy, the record that names them, the build of the code that ran it, and that the path holds no publisher
    /// session. Null when the derivations are the acquisition's own, and the report is then exactly what it was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The derivation time is the one the inputs state (<c>derived_at</c>), never a clock read here, never an observation
    /// time, and it never moves the build time: the acquisition observed the population, and its catalog's resumption
    /// (when it resumed) states that window. A resumed catalog's <c>WireSpent</c> is the resumed run's spend only; the
    /// earlier runs' is at least <c>PreviousWireSpentAtLeast</c>, so the statement names both and adds neither.
    /// </para>
    /// <para>
    /// The report states the path (<c>publisherSession</c>), not a count: it is written in whatever process derives, and a
    /// count belongs to a process. The offline tool measures its own and prints it as <c>publisher_requests</c>. The tool
    /// source is here and not in the record, so the record is reproduced by any build that implements the same rule digests.
    /// </para>
    /// </remarks>
    internal static object? Derivation(V3LuxembourgDerivation? derivation, AcquisitionResumption? luxembourgResumption)
    {
        if (derivation is null)
        {
            return null;
        }

        var record = derivation.Record;
        var derivedAt = derivation.DerivedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var acquired = luxembourgResumption is null
            ? $"Acquired by the Luxembourg acquisition whose catalog is {record.AcquisitionCatalogSha256}"
            : $"Acquired by the Luxembourg acquisition whose catalog is {record.AcquisitionCatalogSha256}, resumed on "
              + $"{Day(luxembourgResumption.ResumedAt)} from {Count(luxembourgResumption.PreviousRuns)} earlier run(s) begun on "
              + $"{Day(luxembourgResumption.PreviousStartedAt)} and catalogued on {Day(luxembourgResumption.CatalogedAt)}; the "
              + $"resumed run had spent {Count(luxembourgResumption.WireSpent)} publisher request(s) when it catalogued, after at "
              + $"least {Count(luxembourgResumption.PreviousWireSpentAtLeast)} by the earlier run(s)";
        return new
        {
            statement = $"{acquired}; re-derived from that acquisition's custody under rule set {record.RuleSet} and admission "
                + $"policy {record.AdmissionPolicySha256}, at the derivation time its inputs state ({derivedAt}); the derivation "
                + "path holds no publisher session, and the offline tool measures its process's network activity and refuses to "
                + "finish with any.",
            derivedAt,
            ruleSet = record.RuleSet,
            rules = record.Rules.Select(static rule => new { profile = rule.Profile, sha256 = rule.Sha256 }).ToArray(),
            admissionPolicy = record.AdmissionPolicySha256,
            record = Reference(record.Reference),
            toolSource = AcquisitionJournal.CurrentSource(),
            publisherSession = "none_on_this_path",
        };

        static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

        static string Day(DateTimeOffset value) =>
            value.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// What a mount whose population was acquired across an interruption says about it, half by half; null when neither
    /// half was resumed, and the report is then exactly the uninterrupted one.
    /// </summary>
    /// <remarks>
    /// Each half names its own resumption: the journal it resumed from, the earlier runs that observed its replayed units
    /// and their window and spend bounds, and when the resumed run began observing. The two halves can come from different
    /// journals (a resumed EU population reused by a resumed Luxembourg run), or one half from none (a population a
    /// resumed run reused as it stood), so no window is stated for the mount as a whole. Every value comes from the
    /// catalogs, so an offline derivation writes these bytes exactly as the acquiring build did.
    /// </remarks>
    internal static object? Resumption(AcquisitionResumption? europe, AcquisitionResumption? luxembourg) =>
        europe is null && luxembourg is null ? null : new
        {
            statement = "This population was not observed in one window. A half that names a resumption was acquired by a run "
                + "resumed from a progress journal: its units counted as replayed were observed by the earlier runs it names "
                + "(earlierRuns.runs of them), between earlierRuns.startedAt and earlierRuns.lastJournaledAt, and admitted "
                + "again through their own checked readers; its units counted as live were observed by the resumed run after "
                + "resumedAt. Those earlier runs spent at least earlierRuns.wireSpentAtLeast requests and at most "
                + "earlierRuns.wireCeiling. A half that names no resumption was observed by the run that acquired it, as its "
                + "catalog states.",
            europe = ResumedHalf(europe),
            luxembourg = ResumedHalf(luxembourg),
        };

    /// <summary>One resumed half as its catalog states it: the journal and earlier runs it resumed from, replayed and live units per phase, and the resumed run's spend and time when the catalog was retained.</summary>
    private static object? ResumedHalf(AcquisitionResumption? resumption) => resumption is null ? null : new
    {
        resumedFrom = new
        {
            journalSha256 = resumption.JournalSha256,
            lastSeq = resumption.LastSeq,
            record = Reference(resumption.Record),
        },
        earlierRuns = new
        {
            runs = resumption.PreviousRuns,
            startedAt = resumption.PreviousStartedAt,
            lastJournaledAt = resumption.PreviousLastJournaledAt,
            wireSpentAtLeast = resumption.PreviousWireSpentAtLeast,
            wireCeiling = resumption.PreviousWireCeiling,
        },
        resumedAt = resumption.ResumedAt,
        phases = resumption.Phases.Select(static phase => new { phase = phase.Phase, replayed = phase.Replayed, live = phase.Live }).ToArray(),
        wireSpentThroughCatalog = resumption.WireSpent,
        catalogedAt = resumption.CatalogedAt,
    };

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
            if (!asGeneration && (await VerifyGenerationsAsync(directory, luxembourg, cancellationToken).ConfigureAwait(false)).Failure is { } generationFailure)
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
    /// decision the retention line makes from the generations held and the references recorded. The mount writer's
    /// verification and the API's mount both run it, so a mount is held to the same checks where it is built and where it
    /// is served.
    /// </summary>
    public static async Task<V3GenerationsVerification> VerifyGenerationsAsync(
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
                ? new V3GenerationsVerification($"the mounted log records {log.Count - 1} earlier build(s) and the mount holds no {GenerationsDirectoryName}/{RetentionFileName} saying what became of them", null)
                : new V3GenerationsVerification(null, null);
        }

        var mountedLog = LuxembourgIndexPredecessor.TryReadLogFile(
                mounted.IndexRef,
                Path.Combine(directory, V3FirstMountBuildResult.LuxembourgIndexFileName),
                out var refusal, out var detail)
            ?? throw new InvalidDataException($"The mounted log does not read: {refusal}: {detail}");
        var generations = V3GenerationRetention.GenerationsOf(log).ToDictionary(static generation => generation.IndexSha256, StringComparer.Ordinal);
        if (Directory.GetFiles(root).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray() is not [RetentionFileName])
        {
            return new V3GenerationsVerification($"{GenerationsDirectoryName} holds files other than {RetentionFileName}, or not it", null);
        }

        var held = Directory.GetDirectories(root).Select(static path => Path.GetFileName(path)!).ToHashSet(StringComparer.Ordinal);
        foreach (var name in held.Order(StringComparer.Ordinal))
        {
            var path = Path.Combine(root, name);
            if (!generations.TryGetValue(name, out var generation))
            {
                return new V3GenerationsVerification($"generation {name} is no earlier build of the mounted log", null);
            }

            if (Directory.GetDirectories(path).Length != 0 ||
                !Directory.GetFiles(path).Select(Path.GetFileName).Order(StringComparer.Ordinal).SequenceEqual(GenerationFiles.Order(StringComparer.Ordinal)))
            {
                return new V3GenerationsVerification($"generation {name} does not hold exactly a generation's files", null);
            }

            var verified = await VerifyAsync(path, asGeneration: true, cancellationToken).ConfigureAwait(false);
            if (!verified.Verified)
            {
                return new V3GenerationsVerification($"generation {name} does not verify: {verified.Detail}", null);
            }

            var indexPath = Path.Combine(path, V3FirstMountBuildResult.LuxembourgIndexFileName);
            var (indexSha256, _) = await V3BuiltIndexFile.Sha256OfFileAsync(indexPath, cancellationToken).ConfigureAwait(false);
            var observation = log[(int)generation.Observation - 1];
            if (!string.Equals(indexSha256, name, StringComparison.Ordinal) ||
                !string.Equals(verified.CorpusRef!.Sha256, observation.CorpusSha256, StringComparison.Ordinal))
            {
                return new V3GenerationsVerification($"generation {name} is not the build its observation names", null);
            }

            var own = LuxembourgIndexPredecessor.TryReadLogFile(
                    new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(indexSha256), indexSha256), indexPath, out refusal, out detail)
                ?? throw new InvalidDataException($"generation {name}'s log does not read: {refusal}: {detail}");
            if (!own.Observations.SequenceEqual(mountedLog.Observations.Take((int)generation.Observation)) ||
                !own.Events.SequenceEqual(mountedLog.Events.Take((int)observation.LastSeq)))
            {
                return new V3GenerationsVerification($"generation {name}'s log is not the mounted log up to its observation", null);
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
            return new V3GenerationsVerification($"{RetentionFileName} calls a generation dropped that the mount holds", null);
        }

        var decision = V3GenerationRetention.Decide(log, held.Union(dropped).ToHashSet(StringComparer.Ordinal), referenced);
        if (!held.SetEquals(decision.Retained.Select(static kept => kept.IndexSha256)) ||
            !RenderRetention(decision, referenced).SequenceEqual(recorded))
        {
            return new V3GenerationsVerification($"{RetentionFileName} is not the decision the retention line makes from the generations held", null);
        }

        return new V3GenerationsVerification(null, decision);
    }

    private static object Reference(SourceArtifactRef reference) => new { resourceId = reference.ResourceId, reference.Sha256 };

    private static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
