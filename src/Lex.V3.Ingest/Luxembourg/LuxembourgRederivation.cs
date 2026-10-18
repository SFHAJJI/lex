using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>One rule a re-derivation runs: its profile's name (the profile text's first line) and that text's SHA-256.</summary>
public sealed record LuxembourgRederivationRule(string Profile, string Sha256);

/// <summary>
/// What a re-derivation is asked to run: a rule set this build implements, its exact rule digests, the admission policy, and
/// when the derivation is stated to run (<paramref name="DerivedAt"/>, a whole UTC second, never in the record). When
/// <paramref name="ExpectedRecordSha256"/> is set the inputs were sealed, and the record must be exactly that one.
/// </summary>
public sealed record LuxembourgRederivationRequest(
    string RuleSet,
    IReadOnlyList<LuxembourgRederivationRule> Rules,
    string AdmissionPolicySha256,
    DateTimeOffset DerivedAt,
    string? ExpectedRecordSha256 = null);

/// <summary>
/// The provenance record of one re-derivation, <c>lex-lu-rederivation/1</c>: a custody object whose resource identity derives
/// from its bytes. It names the acquisition it re-derived, the rules and digests that acquisition was verified under, the
/// rules and policy of the re-derivation, its outputs and its counts, and that its path holds no publisher session
/// (<c>publisher_session: none_on_this_path</c>: a statement of what the path is, not a measurement; the offline tool measures
/// its process's network activity and refuses to finish with any). It names no tool commit and no clock, so any build that
/// implements the same rule digests reproduces it byte for byte from the same custody.
/// </summary>
public sealed class LuxembourgRederivationRecord
{
    /// <summary>The record's schema, which is also the scope its resource identity is derived under.</summary>
    public const string Schema = "lex-lu-rederivation/1";

    private readonly byte[] _canonicalBytes;

    internal LuxembourgRederivationRecord(
        byte[] canonicalBytes,
        string acquisitionCatalogSha256,
        string ruleSet,
        IReadOnlyList<LuxembourgRederivationRule> rules,
        string admissionPolicySha256,
        string inventoryIdentitySha256,
        string contentIdentitySha256)
    {
        _canonicalBytes = canonicalBytes ?? throw new ArgumentNullException(nameof(canonicalBytes));
        Reference = new SourceArtifactRef(
            ContentDerivedIdentity.DeriveUuidUrn(Schema, canonicalBytes),
            CustodyDigest.Of(canonicalBytes));
        AcquisitionCatalogSha256 = acquisitionCatalogSha256;
        RuleSet = ruleSet;
        Rules = Array.AsReadOnly(rules.ToArray());
        AdmissionPolicySha256 = admissionPolicySha256;
        InventoryIdentitySha256 = inventoryIdentitySha256;
        ContentIdentitySha256 = contentIdentitySha256;
    }

    /// <summary>The record as a custody object: the resource identity its bytes derive, and their SHA-256.</summary>
    public SourceArtifactRef Reference { get; }

    /// <summary>The SHA-256 of <see cref="CanonicalBytes"/>: what a corpus header names and a sealed input expects.</summary>
    public string Sha256 => Reference.Sha256;

    /// <summary>The record's exact bytes.</summary>
    public ReadOnlyMemory<byte> CanonicalBytes => _canonicalBytes;

    /// <summary>The SHA-256 of the catalog of the acquisition re-derived (its <c>lex-lu-first-mount-acquisition</c> record).</summary>
    public string AcquisitionCatalogSha256 { get; }

    /// <summary>The rule set the re-derivation ran.</summary>
    public string RuleSet { get; }

    /// <summary>The rules of <see cref="RuleSet"/>, each by its profile and digest, in the set's order.</summary>
    public IReadOnlyList<LuxembourgRederivationRule> Rules { get; }

    /// <summary>The digest of the rights admission policy the re-derivation names.</summary>
    public string AdmissionPolicySha256 { get; }

    /// <summary>The identity of the inventory population the re-derivation produced.</summary>
    public string InventoryIdentitySha256 { get; }

    /// <summary>The identity of the legal-content population the re-derivation produced.</summary>
    public string ContentIdentitySha256 { get; }
}

/// <summary>
/// R1, the re-derivation of the Luxembourg design (Release 1, the profiles): over an acquisition R0 verified, it runs the
/// later rule profiles over R0's own held-body population, mints the acquisition result on R0's own run, and holds a
/// provenance record. It holds no transport: it takes a custody store and R0's result, and builds no session. That is a
/// property of the code, which a signature check cannot prove; what proves it is measured: the tests run it inside a
/// <see cref="V3NetworkActivityListener"/> and require zero network operations, and the offline tool enters
/// <see cref="V3OfflineProcess"/> (no session can be built in its process) and refuses to finish when its listener counts
/// any.
/// </summary>
/// <remarks>
/// <para>
/// Rights do not change in Release 1. R1 keeps every member's rights resolution as the acquisition made it, keeps the
/// final manifest, the record set and the accepted set, and re-runs no holding decision; the admission policy it names is
/// policy /1, today's. R1 with rights (reading /2, the re-resolution, the licence-page index) is a later slice.
/// </para>
/// <para>
/// What R1 refuses, each as a <see cref="CustodyIntegrityException"/> before anything is minted: a rule set or rule digest
/// this build does not implement (no rule is substituted); a policy the rule set does not name; an acquisition that is not
/// a verified catalogued one under the frozen rules (inventory /3, content /1); a held body that the frozen inventory read
/// and the re-derivation reads as other bytes than its digest; and, for sealed inputs, a record other than the sealed one.
/// </para>
/// </remarks>
public static class LuxembourgRederivation
{
    /// <summary>
    /// Release 1's rule set: inventory /4 and content /2 over the acquisition's own population, rights unchanged, and the
    /// index's states under state digest /2 (index 9).
    /// </summary>
    public const string Release1RuleSet = "lu-release-1";

    /// <summary>The value of the record's <c>rights_resolution</c> under Release 1: the acquisition's own, re-read by nothing.</summary>
    private const string RightsAsAcquired = "as_acquired";

    /// <summary>
    /// The record's <c>publisher_session</c>: the path holds no transport and builds no session. A statement of the path,
    /// never a count: a count belongs to a process, and the offline tool measures its own.
    /// </summary>
    private const string NoPublisherSession = "none_on_this_path";

    /// <summary>The rule sets this build implements, sorted ordinally.</summary>
    public static IReadOnlyList<string> ImplementedRuleSets { get; } = Array.AsReadOnly(new[] { Release1RuleSet });

    /// <summary>The rules this build runs for <paramref name="ruleSet"/>; a set it does not implement throws.</summary>
    public static IReadOnlyList<LuxembourgRederivationRule> RulesOf(string ruleSet) => ruleSet switch
    {
        Release1RuleSet => Array.AsReadOnly(new[]
        {
            new LuxembourgRederivationRule(
                FirstLine(LuxembourgAknArticleInventoryProducerV4.CopyRuleProfileBytes()),
                LuxembourgAknArticleInventoryProducerV4.RuleProfileSha256),
            new LuxembourgRederivationRule(
                FirstLine(LuxembourgAknLegalContentProfile2Producer.CopyRuleProfileBytes()),
                LuxembourgAknLegalContentProfile2Producer.RuleProfileSha256),
            // State digest /2 (index 9's states): inputs written before index 9 name two rules and refuse here, so no
            // build ever runs them under a state rule they did not name.
            new LuxembourgRederivationRule(
                FirstLine(LuxembourgIndexBuilder.CopyStateRuleProfileBytes()),
                LuxembourgIndexBuilder.StateRuleProfileSha256),
        }),
        _ => throw new ArgumentException(
            $"Rule set '{ruleSet}' is not one this build implements ({string.Join(", ", ImplementedRuleSets)}).",
            nameof(ruleSet)),
    };

    /// <summary>The admission policy <paramref name="ruleSet"/> names: Release 1 changes no rights, so its policy is /1.</summary>
    public static string AdmissionPolicyOf(string ruleSet) => ruleSet switch
    {
        Release1RuleSet => LuxembourgRightsAdmissionPolicies.Policy1Sha256,
        _ => throw new ArgumentException(
            $"Rule set '{ruleSet}' is not one this build implements ({string.Join(", ", ImplementedRuleSets)}).",
            nameof(ruleSet)),
    };

    /// <summary>
    /// Refuses, before any work, a rule set this build does not implement, rule digests other than exactly the set's, and a
    /// policy other than the one the set names: no rule and no policy is ever substituted.
    /// </summary>
    public static void RequireImplemented(string? ruleSet, IReadOnlyList<LuxembourgRederivationRule>? rules, string? admissionPolicySha256)
    {
        if (ruleSet is null || !ImplementedRuleSets.Contains(ruleSet, StringComparer.Ordinal))
        {
            throw new CustodyIntegrityException(
                $"The re-derivation names rule set '{ruleSet}', which this build does not implement "
                + $"({string.Join(", ", ImplementedRuleSets)}); no rule set is substituted.");
        }

        var implemented = RulesOf(ruleSet);
        if (rules is null || rules.Any(static rule => rule is null) || !rules.SequenceEqual(implemented))
        {
            throw new CustodyIntegrityException(
                $"The re-derivation names rule digests [{Describe(rules)}] for {ruleSet}; this build implements exactly "
                + $"[{Describe(implemented)}], and no rule digest is substituted.");
        }

        var policy = AdmissionPolicyOf(ruleSet);
        if (!string.Equals(admissionPolicySha256, policy, StringComparison.Ordinal))
        {
            throw new CustodyIntegrityException(
                $"The re-derivation names admission policy {admissionPolicySha256}; rule set {ruleSet} names policy {policy} "
                + "(its admission is the acquisition's), and no policy is substituted.");
        }
    }

    /// <summary>
    /// R1 over R0's verified result: inventory /4 and content /2 over R0's own held-body population, the acquisition result
    /// minted on R0's own run with R0's checkpoint, resumption and scope-manifest references, and the record held in
    /// custody. The returned result holds no reference to R0's inventory /3 or content /1 population, so a caller that drops
    /// <paramref name="verified"/> drops them.
    /// </summary>
    public static async Task<LuxembourgFirstMountAcquisitionResult> ApplyAsync(
        ICustodyStore custodyStore,
        LuxembourgFirstMountAcquisitionResult verified,
        LuxembourgRederivationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(verified);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        RequireImplemented(request.RuleSet, request.Rules, request.AdmissionPolicySha256);
        if (request.DerivedAt.Offset != TimeSpan.Zero || request.DerivedAt.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new CustodyIntegrityException("A re-derivation's derived_at is a whole UTC second.");
        }

        if (request.ExpectedRecordSha256 is { } expected && !CustodyDigest.IsLowercaseSha256(expected))
        {
            throw new CustodyIntegrityException("A sealed re-derivation expects a lowercase SHA-256 record digest.");
        }

        // ---- R0's verified result: a delivered, catalogued acquisition under the frozen rules, over its own population. ----
        if (!verified.Delivered || verified.Run is not { Refusal: null } run || verified.Profile is not { } profile ||
            verified.VocabularyEvidenceRef is not { } vocabularyEvidence || verified.CheckpointRef is not { } catalog ||
            run.HeldBodyDerivationPopulation is not { } population ||
            verified.AknInventory is not { } frozenInventory || verified.AknLegalContent is not { } frozenContent)
        {
            throw new CustodyIntegrityException(
                "A re-derivation runs only over a delivered, catalogued acquisition with its held-body population and its derivations.");
        }

        if (verified.Rederivation is not null ||
            !string.Equals(frozenInventory.RuleProfileSha256, LuxembourgAknArticleInventoryProducer.RuleProfileSha256, StringComparison.Ordinal) ||
            !string.Equals(frozenContent.RuleProfileSha256, LuxembourgAknLegalContentProfileProducer.RuleProfileSha256, StringComparison.Ordinal))
        {
            throw new CustodyIntegrityException(
                "A re-derivation runs over the acquisition as R0 verified it (inventory /3, content /1), never over a re-derived one.");
        }

        if (!ReferenceEquals(frozenInventory.SourcePopulation, population) ||
            !ReferenceEquals(frozenContent.SourceInventoryPopulation, frozenInventory))
        {
            throw new CustodyIntegrityException("The verified derivations do not belong to the acquisition's own held-body population.");
        }

        if (run.AcquisitionCheckpointRef is not { } queryCheckpoint || run.CorpusRecordSetRef is not { } recordSet ||
            run.ObservedObjectIdentitySetRef is not { } observedSet)
        {
            throw new CustodyIntegrityException(
                "A re-derivation names the acquisition's query checkpoint, record set and observed set, and this run lacks one.");
        }

        if (population.Inputs.Count == 0 || population.Inputs.Any(static input => input.RightsResolution is null))
        {
            throw new CustodyIntegrityException(
                "A re-derivation reads a held-body population whose every member carries its final rights resolution.");
        }

        var acquisition = new AcquisitionPart(
            catalog.Sha256,
            queryCheckpoint.Sha256,
            recordSet.Sha256,
            observedSet.Sha256,
            Distinct(population.CorpusRecordSet.Set.Records.Select(static record => record.ManifestRef.Sha256)),
            Distinct(population.CorpusRecordSet.Set.Records.Select(static record => record.RunIdentity.Sha256)),
            Distinct(population.Inputs.Select(static input => input.RightsResolution!.BoundRunIdentity.Sha256)),
            verified.PopulationScopeManifestRef?.Sha256);
        var verifiedUnder = new VerifiedUnderPart(
            LuxembourgAknArticleInventoryProducer.RuleProfileSha256,
            LuxembourgAknLegalContentProfileProducer.RuleProfileSha256,
            frozenInventory.IdentitySha256,
            frozenContent.IdentitySha256);
        var frozenUnreadable = frozenInventory.Outcomes
            .Where(static outcome => outcome.Disposition == LuxembourgAknArticleInventoryDisposition.RetainedBytesUnavailable)
            .Select(static outcome => outcome.Input.ObjectOrdinal)
            .ToHashSet();
        var vocabularyCheckpoint = verified.VocabularyCheckpointRef;
        var resumption = verified.Resumption;
        var scopeManifest = verified.PopulationScopeManifestRef;

        // R0's digests matched when it was reopened, and R1 has taken every fact it records from it: from here on R1 holds
        // R0's run and population but not its frozen inventory and content, so a caller that passed its result straight in
        // lets them go while the later rules run.
        verified = null!;
        frozenInventory = null!;
        frozenContent = null!;

        // ---- The later rules over R0's own population object, through each producer's production door. ----
        var inventory = await new LuxembourgAknArticleInventoryProducerV4(custodyStore)
            .RunAsync(run, cancellationToken).ConfigureAwait(false);
        foreach (var outcome in inventory.Outcomes)
        {
            if (outcome.Disposition == LuxembourgAknArticleInventoryDisposition.RetainedBytesUnavailable &&
                !frozenUnreadable.Contains(outcome.Input.ObjectOrdinal))
            {
                throw new CustodyIntegrityException(
                    $"The held body {outcome.TransportReceipt.Reference.ContentSha256} of {outcome.Input.CorpusRecord.ObjectRef.PublisherUri} "
                    + $"no longer reads as its digest, though the verified acquisition read it: {outcome.Detail}");
            }
        }

        var content = await new LuxembourgAknLegalContentProfile2Producer(custodyStore)
            .RunAsync(inventory, cancellationToken).ConfigureAwait(false);
        var unreadable = content.Outcomes.FirstOrDefault(static outcome =>
            outcome.Disposition == LuxembourgAknLegalContentDisposition.RetainedBytesUnavailable);
        if (unreadable is not null)
        {
            throw new CustodyIntegrityException(
                $"The held body {unreadable.TransportReceipt.Reference.ContentSha256} of "
                + $"{unreadable.SourceInventoryOutcome.Input.CorpusRecord.ObjectRef.PublisherUri} no longer reads as its digest, "
                + $"though the inventory read it: {unreadable.Detail}");
        }

        // ---- The record: no tool commit, no clock. ----
        var document = new RecordDocument(
            LuxembourgRederivationRecord.Schema,
            acquisition,
            verifiedUnder,
            new RulesPart(request.RuleSet, request.Rules.ToArray(), request.AdmissionPolicySha256, RightsAsAcquired),
            new OutputsPart(inventory.IdentitySha256, content.IdentitySha256),
            new CountsPart(
                population.Inputs.Count,
                BodyCounts(population.Inputs.Select(static input => ContractWire.NameOf(input.RightsResolution!.Disposition))),
                BodyCounts(inventory.Outcomes.Select(static outcome => ContractWire.NameOf(outcome.Disposition))),
                inventory.Outcomes.Count(static outcome =>
                    outcome.Disposition == LuxembourgAknArticleInventoryDisposition.PublisherFileNamesOtherState),
                OutcomeCounts(content.Outcomes.Select(static outcome => ContractWire.NameOf(outcome.Disposition))),
                content.Outcomes.Count(static outcome => outcome.Disposition == LuxembourgAknLegalContentDisposition.Admitted)),
            PublisherSession: NoPublisherSession);
        var bytes = Encoding.UTF8.GetBytes(ContractJson.Serialize(document));
        var record = new LuxembourgRederivationRecord(
            bytes, catalog.Sha256, request.RuleSet, request.Rules, request.AdmissionPolicySha256,
            inventory.IdentitySha256, content.IdentitySha256);
        if (request.ExpectedRecordSha256 is { } sealedRecord &&
            !string.Equals(sealedRecord, record.Sha256, StringComparison.Ordinal))
        {
            throw new CustodyIntegrityException(
                $"The re-derivation record is {record.Sha256}, not the {sealedRecord} its sealed inputs expect.");
        }

        var (receipt, failure) = await CustodyHold.TryHoldAsync(custodyStore, bytes, cancellationToken).ConfigureAwait(false);
        if (receipt is null)
        {
            throw new CustodyRequiredException("The re-derivation record cannot be retained: " + failure);
        }

        if (!string.Equals(receipt.Reference.ContentSha256, record.Sha256, StringComparison.Ordinal) ||
            receipt.Reference.ByteLength != bytes.Length)
        {
            throw new CustodyIntegrityException("The re-derivation record's receipt names other bytes.");
        }

        // ---- The acquisition result on R0's own run, keeping R0's references. ----
        var result = LuxembourgFirstMountAcquisitionResult.Success(run, profile, vocabularyEvidence, inventory, content);
        if (vocabularyCheckpoint is not null)
        {
            result = result.WithVocabularyCheckpoint(vocabularyCheckpoint);
        }

        result = result.WithCheckpoint(catalog, resumption);
        if (scopeManifest is not null)
        {
            result = result.WithPopulationScopeManifest(scopeManifest);
        }

        return result.WithRederivation(record, request.DerivedAt);
    }

    private static string FirstLine(byte[] profile)
    {
        var text = Encoding.UTF8.GetString(profile);
        var end = text.IndexOf('\n', StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
    }

    private static string Describe(IReadOnlyList<LuxembourgRederivationRule>? rules) => rules is null
        ? "none"
        : string.Join(", ", rules.Select(static rule => rule is null ? "null" : $"{rule.Profile}={rule.Sha256}"));

    private static string[] Distinct(IEnumerable<string> values) =>
        values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static BodyCount[] BodyCounts(IEnumerable<string> names) => names
        .GroupBy(static name => name, StringComparer.Ordinal)
        .OrderBy(static group => group.Key, StringComparer.Ordinal)
        .Select(static group => new BodyCount(group.Key, group.Count()))
        .ToArray();

    private static OutcomeCount[] OutcomeCounts(IEnumerable<string> names) => names
        .GroupBy(static name => name, StringComparer.Ordinal)
        .OrderBy(static group => group.Key, StringComparer.Ordinal)
        .Select(static group => new OutcomeCount(group.Key, group.Count()))
        .ToArray();

    // ---- The record's shape (snake_case on the wire, through ContractJson). ----
    private sealed record RecordDocument(
        string Schema,
        AcquisitionPart Acquisition,
        VerifiedUnderPart VerifiedUnder,
        RulesPart Rules,
        OutputsPart Outputs,
        CountsPart Counts,
        string PublisherSession);

    /// <summary>The acquisition re-derived: its catalog, query checkpoint, record set and observed set, the final manifests,
    /// run identities and rights-bound run identities its records name, and its scope manifest (null for an act range).</summary>
    private sealed record AcquisitionPart(
        string CatalogSha256,
        string QueryCheckpointSha256,
        string RecordSetSha256,
        string ObservedSetSha256,
        string[] FinalManifestSha256s,
        string[] RunIdentitySha256s,
        string[] RightsBoundRunIdentitySha256s,
        string? ScopeManifestSha256);

    /// <summary>The frozen rules R0 verified the acquisition under, and the two derivation identities its catalog records.</summary>
    private sealed record VerifiedUnderPart(
        string InventoryProfileSha256,
        string ContentProfileSha256,
        string InventorySha256,
        string ContentSha256);

    private sealed record RulesPart(
        string RuleSet,
        LuxembourgRederivationRule[] Profiles,
        string AdmissionPolicySha256,
        string RightsResolution);

    private sealed record OutputsPart(string InventorySha256, string ContentSha256);

    private sealed record CountsPart(
        int HeldBodies,
        BodyCount[] RightsDispositions,
        BodyCount[] InventoryDispositions,
        int ContentConsistencyRefusals,
        OutcomeCount[] ContentDispositions,
        int AdmittedArticles);

    private sealed record BodyCount(string Disposition, int Bodies);

    private sealed record OutcomeCount(string Disposition, int Outcomes);
}
