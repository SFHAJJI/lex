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
    private const string RederivationSchema = "lex-v3-offline-mount-inputs/3";

    public sealed class BuildClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }

    public static Task<SourceArtifactRef> CaptureAsync(ICustodyStore store,
        EuFirstMountAcquisitionResult europe, LuxembourgFirstMountAcquisitionResult luxembourg,
        IReadOnlyList<string> seeds, LuxembourgActRange act, DateTimeOffset buildTime,
        V3GenerationSource? generations, CancellationToken cancellationToken) =>
        CaptureCoreAsync(store, europe, luxembourg, seeds, act, null, buildTime, generations, cancellationToken);

    public static Task<SourceArtifactRef> CapturePopulationAsync(ICustodyStore store,
        EuFirstMountAcquisitionResult europe, LuxembourgFirstMountAcquisitionResult luxembourg,
        IReadOnlyList<string> seeds, LuxembourgPopulationScope scope, DateTimeOffset buildTime,
        V3GenerationSource? generations, CancellationToken cancellationToken) =>
        CaptureCoreAsync(store, europe, luxembourg, seeds, null,
            scope ?? throw new ArgumentNullException(nameof(scope)), buildTime, generations, cancellationToken);

    private static async Task<SourceArtifactRef> CaptureCoreAsync(ICustodyStore store,
        EuFirstMountAcquisitionResult europe, LuxembourgFirstMountAcquisitionResult luxembourg,
        IReadOnlyList<string> seeds, LuxembourgActRange? act, LuxembourgPopulationScope? populationScope,
        DateTimeOffset buildTime, V3GenerationSource? generations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (act is null && populationScope is null)
            throw new ArgumentException("A complete mount must name its Luxembourg acquisition scope.");
        if (!europe.Delivered || !luxembourg.Delivered || europe.CheckpointRef is null || luxembourg.CheckpointRef is null)
            throw new CustodyRequiredException("Both acquisitions must retain complete checkpoints before offline derivation.");
        var previous = new List<RetainedFile>();
        if (generations is not null)
        {
            await ValidatePredecessorAsync(generations.PredecessorDirectory, cancellationToken).ConfigureAwait(false);
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
        var encoded = populationScope is null ? Encode(root) : Encoding.UTF8.GetBytes(ContractJson.Serialize(
            new PopulationInputs("lex-v3-offline-mount-inputs/2", root.Europe, root.Luxembourg, root.Seeds,
                populationScope.Policy, populationScope.Ranges.ToArray(), root.BuildTime, root.Predecessor, root.Referenced)));
        await HoldAsync(store, encoded, cancellationToken).ConfigureAwait(false);
        return new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", CustodyDigest.Of(encoded));
    }

    /// <summary>Validates the complete predecessor before live acquisition spends any publisher requests.</summary>
    public static async Task ValidatePredecessorAsync(string directory, CancellationToken cancellationToken)
    {
        var verified = await V3CorpusMountWriter.VerifyAsync(directory, cancellationToken).ConfigureAwait(false);
        if (!verified.Verified) throw new CustodyIntegrityException("Predecessor mount does not verify: " + verified.Detail);
        if (V3FirstMountBuild.ReadPredecessor(directory, out var refusal, out var detail) is null)
            throw new CustodyIntegrityException($"Predecessor cannot be chained: {refusal}: {detail}");
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            if (!AdmittedPath(Path.GetRelativePath(directory, file).Replace('\\', '/')))
                throw new CustodyIntegrityException("Predecessor holds an unexpected mount file.");
    }

    /// <summary>
    /// Derives the mount from retained inputs, with no publisher traffic: inputs /1 or /2 reopen the acquisitions (R0) and
    /// build from their own derivations; inputs /3 reopen the same way and then re-derive the Luxembourg profiles over R0's
    /// population (R1, <see cref="LuxembourgRederivation"/>). The input clock describes the original build, never a fresh
    /// publisher observation, and a re-derivation keeps it.
    /// </summary>
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
        var (root, populationScope, rederivation) = await ReadInputsAsync(store, bytes, cancellationToken).ConfigureAwait(false);
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
                    // Written straight from the checked bytes, not from a second copy of them (an index is over a gigabyte).
                    await using var restored = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous);
                    await restored.WriteAsync(retained, cancellationToken).ConfigureAwait(false);
                }
                var verified = await V3CorpusMountWriter.VerifyAsync(staging, cancellationToken).ConfigureAwait(false);
                if (!verified.Verified) throw new CustodyIntegrityException("Retained predecessor fails mount verification: " + verified.Detail);
                predecessor = V3FirstMountBuild.ReadPredecessor(staging, out var refusal, out var detail)
                    ?? throw new CustodyIntegrityException($"Retained predecessor refused: {refusal}: {detail}");
                generations = new(staging, root.Referenced.ToHashSet(StringComparer.Ordinal));
            }
            // The populations, the build and the files it staged live in this call only, so all of them are released before
            // the written mount is read back (S4b).
            var write = await BuildAndWriteAsync(
                store, root, populationScope, rederivation, predecessor, generations, target, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// R0 (and R1 for inputs /3), the build and its write: the acquisitions reopened, the mount built (each artefact twice) and
    /// written to <paramref name="target"/>, and the build disposed, so nothing it held outlives the write.
    /// </summary>
    private static async Task<V3CorpusMountWrite> BuildAndWriteAsync(ICustodyStore store, Inputs root,
        LuxembourgPopulationScope? populationScope, RederivationInputs? rederivation, LuxembourgIndexPredecessor? predecessor,
        V3GenerationSource? generations, string target, CancellationToken cancellationToken)
    {
        var europe = await EuFirstMountAcquisition.ReopenAsync(store, root.Europe, root.Seeds, cancellationToken).ConfigureAwait(false);
        // R0: the acquisition reopened byte for byte, its frozen derivations verified against its catalog. For inputs /3,
        // R1 then runs the later rules over R0's own population; R0's result is handed straight to it and held nowhere
        // here, so its frozen inventory and content populations are released once R1 has read what it records of them.
        var luxembourg = rederivation is null
            ? await ReopenLuxembourgAsync(store, root, populationScope, cancellationToken).ConfigureAwait(false)
            : await LuxembourgRederivation.ApplyAsync(store,
                await ReopenLuxembourgAsync(store, root, populationScope, cancellationToken).ConfigureAwait(false),
                rederivation.Request(), cancellationToken).ConfigureAwait(false);

        // The build time stays the acquisition's: a derivation never moves freshness forward.
        using var build = await new V3FirstMountBuild(store, new BuildClock(root.BuildTime))
            .RunAsync(europe, luxembourg, predecessor, cancellationToken).ConfigureAwait(false);
        if (!build.Delivered) throw new CustodyIntegrityException($"Offline mount derivation refused: {build.Refusal}: {build.Detail}");
        return await V3CorpusMountWriter.WriteAsync(build, target, generations, cancellationToken, root.BuildTime).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes inputs <c>lex-v3-offline-mount-inputs/3</c>: the retained inputs /1 or /2 of an acquisition (its base), the rule
    /// set this build implements with its exact rule digests, the admission policy, and when the re-derivation is stated to
    /// run (a whole UTC second, not before the base's build time). The inputs are unsealed: <c>expected_rederivation</c> is
    /// null until <see cref="SealRederivationAsync"/>. They are held in custody under a resource identity derived from their
    /// bytes, so the same arguments give the same reference. Nothing here reopens the acquisition or reads a body.
    /// </summary>
    public static async Task<SourceArtifactRef> CaptureRederivationAsync(ICustodyStore store, SourceArtifactRef baseInputs,
        string ruleSet, string admissionPolicySha256, DateTimeOffset derivedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(baseInputs);
        cancellationToken.ThrowIfCancellationRequested();
        if (ruleSet is null || !LuxembourgRederivation.ImplementedRuleSets.Contains(ruleSet, StringComparer.Ordinal))
            throw new ArgumentException($"Rule set '{ruleSet}' is not one this build implements ({string.Join(", ", LuxembourgRederivation.ImplementedRuleSets)}).", nameof(ruleSet));
        var inputs = new RederivationInputs(RederivationSchema, baseInputs, ruleSet, LuxembourgRederivation.RulesOf(ruleSet).ToArray(),
            admissionPolicySha256, derivedAt, null);
        var encoded = Encoding.UTF8.GetBytes(ContractJson.Serialize(inputs));
        // The inputs are held only once they read back exactly as a derivation will read them.
        _ = await ReadInputsAsync(store, encoded, cancellationToken).ConfigureAwait(false);
        await HoldAsync(store, encoded, cancellationToken).ConfigureAwait(false);
        return new SourceArtifactRef(ContentDerivedIdentity.DeriveUuidUrn(RederivationSchema, encoded), CustodyDigest.Of(encoded));
    }

    /// <summary>
    /// Seals unsealed inputs /3 after their derivation: the same inputs, now expecting exactly the record
    /// <paramref name="recordSha256"/>, held in custody. A derivation of the sealed inputs refuses any other record. Sealed
    /// inputs are never sealed again; their unsealed inputs stay in custody.
    /// </summary>
    public static async Task<SourceArtifactRef> SealRederivationAsync(ICustodyStore store, SourceArtifactRef unsealedInputs,
        string recordSha256, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(unsealedInputs);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CustodyDigest.IsLowercaseSha256(recordSha256))
            throw new ArgumentException("A seal names the record by its lowercase SHA-256.", nameof(recordSha256));
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, unsealedInputs.Sha256, cancellationToken).ConfigureAwait(false);
        var (_, _, rederivation) = await ReadInputsAsync(store, bytes, cancellationToken).ConfigureAwait(false);
        if (rederivation is null)
            throw new CustodyIntegrityException("Only offline mount inputs /3 name a re-derivation to seal.");
        if (rederivation.ExpectedRederivation is not null)
            throw new CustodyIntegrityException("These inputs are sealed already; seal their unsealed inputs instead.");
        var encoded = Encoding.UTF8.GetBytes(ContractJson.Serialize(rederivation with { ExpectedRederivation = recordSha256 }));
        await HoldAsync(store, encoded, cancellationToken).ConfigureAwait(false);
        return new SourceArtifactRef(ContentDerivedIdentity.DeriveUuidUrn(RederivationSchema, encoded), CustodyDigest.Of(encoded));
    }

    /// <summary>
    /// Reads offline mount inputs: /1 (an act range), /2 (a population scope), or /3 (a re-derivation of a retained /1 or /2,
    /// read from custody by its digest). Each must be its own exact canonical bytes; anything else refuses.
    /// </summary>
    private static async Task<(Inputs Root, LuxembourgPopulationScope? PopulationScope, RederivationInputs? Rederivation)> ReadInputsAsync(
        ICustodyStore store, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        if (SchemaOf(bytes) != RederivationSchema)
        {
            var (root, scope) = ReadBase(bytes);
            return (root, scope, null);
        }

        RederivationInputs rederivation;
        try
        {
            rederivation = ContractJson.Deserialize<RederivationInputs>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (rederivation is null || rederivation.Base is null || rederivation.Rules is null ||
                !bytes.Span.SequenceEqual(Encoding.UTF8.GetBytes(ContractJson.Serialize(rederivation))))
                throw new CustodyIntegrityException("Offline re-derivation inputs framing is invalid.");
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Offline re-derivation inputs cannot be verified.", exception);
        }

        if (!CustodyDigest.IsLowercaseSha256(rederivation.Base.Sha256) ||
            rederivation.DerivedAt.Offset != TimeSpan.Zero || rederivation.DerivedAt != V3FirstMountBuild.BuildTimeOf(rederivation.DerivedAt) ||
            rederivation.ExpectedRederivation is not null && !CustodyDigest.IsLowercaseSha256(rederivation.ExpectedRederivation))
            throw new CustodyIntegrityException("Offline re-derivation inputs name an invalid base, derivation time or sealed record.");
        // No rule and no policy is substituted: the inputs name exactly what this build runs, or nothing is reopened.
        LuxembourgRederivation.RequireImplemented(rederivation.RuleSet, rederivation.Rules, rederivation.AdmissionPolicy);
        var baseBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, rederivation.Base.Sha256, cancellationToken).ConfigureAwait(false);
        if (SchemaOf(baseBytes) == RederivationSchema)
            throw new CustodyIntegrityException("A re-derivation's base is the acquisition's inputs /1 or /2, never another re-derivation.");
        var (baseRoot, baseScope) = ReadBase(baseBytes);
        if (rederivation.DerivedAt < baseRoot.BuildTime)
            throw new CustodyIntegrityException("A re-derivation is not stated to run before the acquisition's build time.");
        return (baseRoot, baseScope, rederivation);
    }

    /// <summary>R0 for the Luxembourg half: the act range's or the population's catalog, reopened and verified.</summary>
    private static Task<LuxembourgFirstMountAcquisitionResult> ReopenLuxembourgAsync(ICustodyStore store, Inputs root,
        LuxembourgPopulationScope? populationScope, CancellationToken cancellationToken) => populationScope is null
        ? LuxembourgFirstMountAcquisition.ReopenAsync(store, root.Luxembourg, root.Act!, cancellationToken)
        : LuxembourgFirstMountAcquisition.ReopenPopulationAsync(store, root.Luxembourg, populationScope, cancellationToken);

    /// <summary>The <c>schema</c> of a retained inputs document, read before it is deserialized strictly.</summary>
    private static string? SchemaOf(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var framing = JsonDocument.Parse(bytes);
            return framing.RootElement.ValueKind == JsonValueKind.Object &&
                framing.RootElement.TryGetProperty("schema", out var schemaProperty) && schemaProperty.ValueKind == JsonValueKind.String
                ? schemaProperty.GetString() : null;
        }
        catch (JsonException exception)
        {
            throw new CustodyIntegrityException("Offline mount catalog cannot be verified.", exception);
        }
    }

    /// <summary>Reads inputs /1 or /2: the acquisitions, the seeds, the Luxembourg act range or population scope, the build time and the predecessor.</summary>
    private static (Inputs Root, LuxembourgPopulationScope? PopulationScope) ReadBase(ReadOnlyMemory<byte> bytes)
    {
        Inputs root;
        LuxembourgPopulationScope? populationScope = null;
        try
        {
            var schema = SchemaOf(bytes);
            if (schema == Schema)
            {
                root = ContractJson.Deserialize<Inputs>(new UTF8Encoding(false, true).GetString(bytes.Span));
                if (root is null || !bytes.Span.SequenceEqual(Encode(root)))
                    throw new CustodyIntegrityException("Offline mount catalog framing is invalid.");
            }
            else if (schema == "lex-v3-offline-mount-inputs/2")
            {
                var population = ContractJson.Deserialize<PopulationInputs>(new UTF8Encoding(false, true).GetString(bytes.Span));
                if (population is null || population.Ranges is null ||
                    !bytes.Span.SequenceEqual(Encoding.UTF8.GetBytes(ContractJson.Serialize(population))))
                    throw new CustodyIntegrityException("Offline population catalog framing is invalid.");
                populationScope = new LuxembourgPopulationScope(population.Policy, population.Ranges);
                root = new Inputs(population.Schema, population.Europe, population.Luxembourg, population.Seeds,
                    null, population.BuildTime, population.Predecessor, population.Referenced);
            }
            else throw new CustodyIntegrityException("Unknown offline mount catalog schema.");
            if (root.Europe is null || root.Luxembourg is null ||
                root.Seeds is null || root.Seeds.Length == 0 || root.Seeds.Any(string.IsNullOrWhiteSpace) || root.Act is null && populationScope is null ||
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

        return (root, populationScope);
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
        LuxembourgActRange? Act, DateTimeOffset BuildTime, RetainedFile[] Predecessor, string[] Referenced);
    private sealed record PopulationInputs(string Schema, SourceArtifactRef Europe, SourceArtifactRef Luxembourg,
        string[] Seeds, string Policy, LuxembourgActRange[] Ranges, DateTimeOffset BuildTime,
        RetainedFile[] Predecessor, string[] Referenced);

    /// <summary>
    /// Inputs /3: the acquisition's retained inputs /1 or /2 (<paramref name="Base"/>), the rule set and its exact digests,
    /// the admission policy, when the re-derivation is stated to run, and the record a sealed derivation must reproduce
    /// (null until sealed).
    /// </summary>
    private sealed record RederivationInputs(string Schema, SourceArtifactRef Base, string RuleSet,
        LuxembourgRederivationRule[] Rules, string AdmissionPolicy, DateTimeOffset DerivedAt, string? ExpectedRederivation)
    {
        public LuxembourgRederivationRequest Request() =>
            new(RuleSet, Rules, AdmissionPolicy, DerivedAt, ExpectedRederivation);
    }
}
