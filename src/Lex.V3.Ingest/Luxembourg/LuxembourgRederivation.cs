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
    /// Release 1's rule set: inventory /4 and content /3 over the acquisition's own population, rights unchanged, and the
    /// index's states under state digest /2 (index 9). Content /3 replaced content /2 here before any inputs were sealed
    /// under this set (a driver decision of 2026-10-18, S3b): it reads every article /2 admits to the same stored tokens and
    /// admits the quoted, deleted and other shapes the design's /3 table names, so inputs naming content /2 refuse under
    /// this build and no rule is substituted.
    /// </summary>
    public const string Release1RuleSet = "lu-release-1";

    /// <summary>
    /// Release 2's rule set (LU-DATA-DESIGN v2 §1.1, §1.2, §2.2 "R1 with rights"): Release 1's rules, and before them in-file
    /// rights reading /2 and resolution /2 over every held body, under admission policy A. A member the acquisition admitted
    /// (<c>agreed_same_run_cc_by</c>) keeps its resolution once reading /2 agrees with it; every other member takes its
    /// resolution /2.
    /// </summary>
    public const string Release2RuleSet = "lu-release-2";

    /// <summary>
    /// Release 3's rule set: Release 2's rules, digest for digest, under the policy the owner's rulings L1, L1b and L2 name
    /// with option (a), every licence page of the acquisition counting (LU-DATA-DESIGN v2 §5; the owner's standing yes of
    /// 2026-10-27 to the driver's recommendation). A rule set names its policy, and inputs sealed under
    /// <see cref="Release2RuleSet"/> name policy A, so the switches are Release 3's and Release 2 keeps policy A: a derive of
    /// Release 2's sealed inputs still runs as it did.
    /// </summary>
    public const string Release3RuleSet = "lu-release-3";

    /// <summary>The value of the record's <c>rights_resolution</c> under Release 1: the acquisition's own, re-read by nothing.</summary>
    private const string RightsAsAcquired = "as_acquired";

    /// <summary>
    /// The value of the record's <c>rights_resolution</c> under Release 2 and Release 3: every held body re-read by reading /2
    /// and re-resolved by resolution /2, the acquisition's admissions kept where /2 agrees with them.
    /// </summary>
    private const string RightsReResolved = "re_resolved_reading_2_keeping_agreed_admissions";

    /// <summary>The schema of the in-file rights evidence index /2 Release 2 holds (every reading, refused ones too).</summary>
    internal const string InFileEvidenceTwoSchema = "lex-lu-in-file-rights-evidence/2";

    /// <summary>
    /// The record's <c>publisher_session</c>: the path holds no transport and builds no session. A statement of the path,
    /// never a count: a count belongs to a process, and the offline tool measures its own.
    /// </summary>
    private const string NoPublisherSession = "none_on_this_path";

    /// <summary>The rule sets this build implements, sorted ordinally.</summary>
    public static IReadOnlyList<string> ImplementedRuleSets { get; } =
        Array.AsReadOnly(new[] { Release1RuleSet, Release2RuleSet, Release3RuleSet });

    /// <summary>Whether <paramref name="ruleSet"/> re-reads and re-resolves the rights of every held body (Release 2 and 3).</summary>
    private static bool RederivesRights(string? ruleSet) =>
        string.Equals(ruleSet, Release2RuleSet, StringComparison.Ordinal) || string.Equals(ruleSet, Release3RuleSet, StringComparison.Ordinal);

    /// <summary>
    /// The rules this build runs for <paramref name="ruleSet"/>; a set it does not implement throws. Release 2 names the rights
    /// rules first (reading /2, then resolution /2), then Release 1's three; Release 3 names Release 2's five.
    /// </summary>
    public static IReadOnlyList<LuxembourgRederivationRule> RulesOf(string ruleSet) => ruleSet switch
    {
        Release1RuleSet => Array.AsReadOnly(ProfileRules()),
        Release2RuleSet or Release3RuleSet => Array.AsReadOnly(new[]
        {
            new LuxembourgRederivationRule(
                FirstLine(LuxembourgInFileRightsReader.CopyReadingTwoProfileBytes()),
                LuxembourgInFileRightsReader.ReadingTwoProfileSha256),
            new LuxembourgRederivationRule(
                FirstLine(LuxembourgRightsChannels.CopyResolutionTwoProfileBytes()),
                LuxembourgRightsChannels.ResolutionTwoProfileSha256),
        }.Concat(ProfileRules()).ToArray()),
        _ => throw new ArgumentException(
            $"Rule set '{ruleSet}' is not one this build implements ({string.Join(", ", ImplementedRuleSets)}).",
            nameof(ruleSet)),
    };

    private static LuxembourgRederivationRule[] ProfileRules() =>
    [
        new LuxembourgRederivationRule(
            FirstLine(LuxembourgAknArticleInventoryProducerV4.CopyRuleProfileBytes()),
            LuxembourgAknArticleInventoryProducerV4.RuleProfileSha256),
        new LuxembourgRederivationRule(
            FirstLine(LuxembourgAknLegalContentProfile3Producer.CopyRuleProfileBytes()),
            LuxembourgAknLegalContentProfile3Producer.RuleProfileSha256),
        // State digest /2 (index 9's states): inputs written before index 9 name two rules and refuse here, so no
        // build ever runs them under a state rule they did not name.
        new LuxembourgRederivationRule(
            FirstLine(LuxembourgIndexBuilder.CopyStateRuleProfileBytes()),
            LuxembourgIndexBuilder.StateRuleProfileSha256),
    ];

    // ---- The owner's rulings Release 3 applies (LU-DATA-DESIGN v2 §5, asked 2026-10-27). ----
    // A yes sets its switch here and gives the policy entry the owner's words (LuxembourgRightsAdmissionPolicies); the rule
    // set then names that entry, its digest changes, and so do the inputs a derive is sealed with. Nothing else changes. The
    // owner's standing yes of 2026-10-27 to the driver's recommendation set all three, with option (a). Release 2 names
    // policy A, as its sealed inputs do, so its derive is unchanged.

    /// <summary>L2: files whose only licence statement names another identity, on the SPARQL declaration alone.</summary>
    private const bool Release3RulingL2 = true;

    /// <summary>L1: files that state no licence at all, on the SPARQL declaration alone.</summary>
    private const bool Release3RulingL1 = true;

    /// <summary>L1b: files whose own block states no licence, on the SPARQL declaration alone.</summary>
    private const bool Release3RulingL1b = true;

    /// <summary>Option (b) of the rulings above: only licence pages the acquisition observed live. Option (a) when false.</summary>
    private const bool Release3LivePagesOnly = false;

    /// <summary>
    /// The admission policy <paramref name="ruleSet"/> names: Release 1 changes no rights, so its policy is /1; Release 2
    /// names policy A, which adds <c>agreed_cc_by_rederived</c>; Release 3 names policy A with the owner's rulings its
    /// switches turn on (L1, L1b and L2, option (a): <c>lex-lu-rights-admission-policy/A+L1+L1b+L2(a)</c>).
    /// </summary>
    public static string AdmissionPolicyOf(string ruleSet) => ruleSet switch
    {
        Release1RuleSet => LuxembourgRightsAdmissionPolicies.Policy1Sha256,
        Release2RuleSet => LuxembourgRightsAdmissionPolicies.PolicyASha256,
        Release3RuleSet => LuxembourgRightsAdmissionPolicies.PolicyFor(
            Release3RulingL1, Release3RulingL1b, Release3RulingL2, Release3LivePagesOnly),
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
                $"The re-derivation names admission policy {admissionPolicySha256}; rule set {ruleSet} names policy {policy}, "
                + "and no policy is substituted.");
        }
    }

    /// <summary>
    /// R1 over R0's verified result: inventory /4 and content /3 over R0's own held-body population, the acquisition result
    /// minted on R0's own run with R0's checkpoint, resumption and scope-manifest references, and the record held in
    /// custody. The returned result holds no reference to R0's inventory /3 or content /1 population, so a caller that drops
    /// <paramref name="verified"/> drops them.
    /// </summary>
    public static Task<LuxembourgFirstMountAcquisitionResult> ApplyAsync(
        ICustodyStore custodyStore,
        LuxembourgFirstMountAcquisitionResult verified,
        LuxembourgRederivationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireImplemented(request.RuleSet, request.Rules, request.AdmissionPolicySha256);
        return ApplyCoreAsync(custodyStore, verified, request, cancellationToken);
    }

    /// <summary>
    /// A rights rule set (Release 2's or Release 3's) under another of the owner's rulings than the one it names: the rule set's
    /// rules exactly, under a registered policy over policy A (<see cref="LuxembourgRightsAdmissionPolicies.PolicyFor"/>). What
    /// a ruling or an option would serve is measured and tested this way; the derive's path (<see cref="ApplyAsync"/>, inputs
    /// /3) never substitutes a policy, and the record names the policy this ran under.
    /// </summary>
    internal static Task<LuxembourgFirstMountAcquisitionResult> ApplyUnderOwnersRulingAsync(
        ICustodyStore custodyStore,
        LuxembourgFirstMountAcquisitionResult verified,
        LuxembourgRederivationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireImplemented(request.RuleSet, request.Rules, AdmissionPolicyOf(request.RuleSet));
        if (!RederivesRights(request.RuleSet) || !LuxembourgRightsAdmissionPolicies.IsRegistered(request.AdmissionPolicySha256) ||
            !LuxembourgRightsAdmissionPolicies.Admits(request.AdmissionPolicySha256, LuxembourgRightsChannelDisposition.AgreedCcByRederived))
        {
            throw new CustodyIntegrityException(
                $"The owner's rulings are measured under a rights rule set and a registered policy over policy A, not {request.AdmissionPolicySha256}.");
        }

        return ApplyCoreAsync(custodyStore, verified, request, cancellationToken);
    }

    private static async Task<LuxembourgFirstMountAcquisitionResult> ApplyCoreAsync(
        ICustodyStore custodyStore,
        LuxembourgFirstMountAcquisitionResult verified,
        LuxembourgRederivationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(verified);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
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

        // ---- Release 2 and 3: rights re-read by reading /2 and re-resolved by resolution /2, then bound on a run of R0's own. ----
        var acquiredDispositions = BodyCounts(population.Inputs.Select(static input => ContractWire.NameOf(input.RightsResolution!.Disposition)));
        RightsPart? rights = null;
        if (RederivesRights(request.RuleSet))
        {
            (var rebound, rights) = await RederiveRightsAsync(custodyStore, run.TypedAssertions, population, acquiredDispositions,
                    request.AdmissionPolicySha256, resumption?.ResumedAt, cancellationToken)
                .ConfigureAwait(false);

            // 6. A run of R0's own carrying the re-bound population: the run is re-made here, at R1's one door onto it, by
            // the run's own copy, which refuses a refused run and a population that is not its own members'.
            run = run.WithHeldBodyDerivationPopulation(rebound);
            population = run.HeldBodyDerivationPopulation!;
        }

        // ---- The later rules over the run's own population object, through each producer's production door. ----
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

        var content = await new LuxembourgAknLegalContentProfile3Producer(custodyStore)
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
            new RulesPart(request.RuleSet, request.Rules.ToArray(), request.AdmissionPolicySha256,
                rights is null ? RightsAsAcquired : RightsReResolved),
            new OutputsPart(inventory.IdentitySha256, content.IdentitySha256),
            new CountsPart(
                population.Inputs.Count,
                BodyCounts(population.Inputs.Select(static input => ContractWire.NameOf(input.RightsResolution!.Disposition))),
                BodyCounts(inventory.Outcomes.Select(static outcome => ContractWire.NameOf(outcome.Disposition))),
                inventory.Outcomes.Count(static outcome =>
                    outcome.Disposition == LuxembourgAknArticleInventoryDisposition.PublisherFileNamesOtherState),
                OutcomeCounts(content.Outcomes.Select(static outcome => ContractWire.NameOf(outcome.Disposition))),
                content.Outcomes.Count(static outcome => outcome.Disposition == LuxembourgAknLegalContentDisposition.Admitted)),
            PublisherSession: NoPublisherSession)
        {
            Rights = rights,
        };
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

    /// <summary>
    /// R1 with rights (LU-DATA-DESIGN v2 §2.2, Release 2, steps 1 to 6): every held body of <paramref name="population"/>, in
    /// ordinal order, read by its digest under reading /2; the readings held as the in-file evidence index /2; each body
    /// re-resolved by resolution /2 against R0's own SPARQL channel for it, the rights' bound run identity and the catalogue
    /// licences of the run's <paramref name="typedAssertions"/>; the checks; then the population re-bound. It hands back the
    /// re-bound population, never a run: the caller re-makes R0's run around it.
    /// </summary>
    /// <remarks>
    /// Refused, before anything is minted: a resolution that is not terminal evidence (<c>channel_enumeration_unproven</c>,
    /// <c>evidence_not_independent</c>) or that is <c>non_admitting_licence_scl</c> (holding would have differed), each
    /// naming its members; and a member the acquisition admitted that reading /2 does not agree with. A member the acquisition
    /// admitted keeps its own resolution; every other member takes its resolution /2.
    /// </remarks>
    private static async Task<(LuxembourgHeldBodyDerivationPopulation Population, RightsPart Rights)> RederiveRightsAsync(
        ICustodyStore custodyStore,
        IReadOnlyList<LuxembourgTypedAssertion> typedAssertions,
        LuxembourgHeldBodyDerivationPopulation population,
        BodyCount[] acquiredDispositions,
        string admissionPolicySha256,
        DateTimeOffset? resumedAt,
        CancellationToken cancellationToken)
    {
        // 1. Every held body under reading /2, by its digest.
        var readings = new List<(LuxembourgHeldBodyDerivationInput Input, LuxembourgInFileRightsReadingTwo Reading)>(population.Inputs.Count);
        foreach (var input in population.Inputs.OrderBy(static input => input.ObjectOrdinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var r0 = input.RightsResolution!;
            if (!string.Equals(r0.SelectedManifestationIri, input.SelectedWemiCandidate.ManifestationIri, StringComparison.Ordinal))
            {
                throw new CustodyIntegrityException(
                    $"The acquisition's rights of {input.CorpusRecord.ObjectRef.PublisherUri} resolve {r0.SelectedManifestationIri}, "
                    + $"not its selected manifestation {input.SelectedWemiCandidate.ManifestationIri}.");
            }

            var bodySha256 = input.Receipt.Reference.ContentSha256;
            var bodyRef = new SourceArtifactRef(
                ContentDerivedIdentity.DeriveUuidUrn("lex-lu-in-file-body-digest/1", Encoding.UTF8.GetBytes(bodySha256)), bodySha256);
            readings.Add((input, await LuxembourgInFileRightsReader.ReadTwoAsync(
                custodyStore, bodyRef, input.SelectedWemiCandidate.ManifestationIri, input.Address.UserFormatToken,
                cancellationToken).ConfigureAwait(false)));
        }

        // 2. The in-file evidence index /2: every reading, refused ones too, under the rule digests, referencing the /1 index.
        var index = new EvidenceIndexDocument(
            InFileEvidenceTwoSchema,
            LuxembourgInFileRightsReader.ReadingTwoProfileSha256,
            LuxembourgRightsChannels.ResolutionTwoProfileSha256,
            population.Inputs.Select(static input => input.RightsResolution!.InFileObservations.EnumerationRef)
                .Distinct().OrderBy(static reference => reference.Sha256, StringComparer.Ordinal).ToArray(),
            readings.Select(static entry => new EvidenceReading(
                entry.Input.ObjectOrdinal,
                entry.Reading.ManifestationIri,
                entry.Reading.BodyRef.Sha256,
                ContractWire.NameOf(entry.Reading.Status),
                entry.Reading.OwnBlockLicenceIris.ToArray(),
                entry.Reading.DeclaredLicenceIris.ToArray(),
                entry.Reading.NamedIdentities.Select(static named => new EvidenceNamedIdentity(named.Identity, named.LicenceIris.ToArray())).ToArray(),
                entry.Reading.FrbrManifestationIdentities.ToArray())).ToArray());
        var indexBytes = Encoding.UTF8.GetBytes(ContractJson.Serialize(index));
        index = null!;
        var (indexReceipt, indexFailure) = await CustodyHold.TryHoldAsync(custodyStore, indexBytes, cancellationToken).ConfigureAwait(false);
        if (indexReceipt is null)
        {
            throw new CustodyRequiredException("The in-file rights evidence index /2 cannot be retained: " + indexFailure);
        }

        var indexRef = new SourceArtifactRef(
            ContentDerivedIdentity.DeriveUuidUrn(InFileEvidenceTwoSchema, indexBytes), indexReceipt.Reference.ContentSha256);
        if (!string.Equals(indexRef.Sha256, CustodyDigest.Of(indexBytes), StringComparison.Ordinal))
        {
            throw new CustodyIntegrityException("The in-file rights evidence index /2's receipt names other bytes.");
        }

        // 3. Resolution /2 against R0's own SPARQL channel, the bound run identity and the run's catalogue licences.
        var catalogue = LuxembourgRightsCatalogue.From(typedAssertions
            .Where(static assertion => assertion.FactDisposition.Predicate == LuxembourgAssertionPredicate.License)
            .Select(static assertion => (
                assertion.Assertion.SubjectIri,
                assertion.Assertion.ObjectKind == LuxembourgAssertionObjectKind.Iri &&
                assertion.Assertion.DatatypeIriOrEmpty.Length == 0 && assertion.Assertion.LanguageTagOrEmpty.Length == 0
                    ? assertion.Assertion.ObjectIriOrLexical
                    : (string?)null)));
        var resolved = readings.Select(entry =>
        {
            var r0 = entry.Input.RightsResolution!;
            return (entry.Input, R0: r0, Two: LuxembourgRightsChannels.ResolveTwo(
                entry.Input.SelectedWemiCandidate.ManifestationIri, r0.BoundRunIdentity, r0.SparqlObservations,
                r0.BoundRunIdentity, indexRef, entry.Reading, catalogue));
        }).ToArray();
        readings.Clear();

        // 4. The checks, each naming its members.
        Refuse(resolved.Where(static entry => entry.Two.Disposition is LuxembourgRightsChannelDisposition.ChannelEnumerationUnproven
                or LuxembourgRightsChannelDisposition.EvidenceNotIndependent),
            "resolves to a non-terminal disposition under resolution /2");
        Refuse(resolved.Where(static entry => entry.Two.Disposition == LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl),
            "resolves to non_admitting_licence_scl under resolution /2, so its holding would have differed");
        Refuse(resolved.Where(static entry => entry.R0.Disposition == LuxembourgRightsChannelDisposition.AgreedSameRunCcBy &&
                entry.Two.Disposition != LuxembourgRightsChannelDisposition.AgreedCcByRederived),
            "was admitted by the acquisition (agreed_same_run_cc_by) and reading /2 does not agree");

        // 4b. Under one of the owner's rulings, each single-channel resolution the policy names is bound to the retained SPARQL
        // page holding its licence row (the licence-page index), or the re-derivation refuses: no single-channel admission
        // without its page. Under policy A alone no page is read.
        string? licencePagesSha256 = null;
        if (LuxembourgRightsAdmissionPolicies.AdmitsSingleChannel(admissionPolicySha256))
        {
            var indexes = new Dictionary<string, LuxembourgSparqlLicencePageIndex>(StringComparer.Ordinal);
            var pageIndexDigests = new List<string>();
            for (var position = 0; position < resolved.Length; position++)
            {
                var entry = resolved[position];
                if (!LuxembourgRightsChannelResolution.IsSingleChannel(entry.Two.Disposition) ||
                    !LuxembourgRightsAdmissionPolicies.Admits(admissionPolicySha256, entry.Two.Disposition))
                {
                    continue;
                }

                var rightsIndex = entry.Two.SparqlObservations.EnumerationRef.Sha256;
                if (!indexes.TryGetValue(rightsIndex, out var pages))
                {
                    pages = await LuxembourgSparqlLicencePageIndex.BuildAsync(custodyStore, rightsIndex, resumedAt, cancellationToken)
                        .ConfigureAwait(false);
                    var (pagesReceipt, pagesFailure) = await CustodyHold.TryHoldAsync(custodyStore, pages.CanonicalBytes, cancellationToken)
                        .ConfigureAwait(false);
                    if (pagesReceipt is null || pagesReceipt.Reference.ContentSha256 != pages.Sha256)
                    {
                        throw new CustodyRequiredException("The licence-page index cannot be retained: " + pagesFailure);
                    }

                    indexes[rightsIndex] = pages;
                    pageIndexDigests.Add(pages.Sha256);
                }

                if (pages.PageOf(entry.Two.SelectedManifestationIri) is not { } page ||
                    !page.LicenceIris.SequenceEqual(entry.Two.SparqlObservation!.LicenceIris, StringComparer.Ordinal))
                {
                    throw new CustodyIntegrityException(
                        $"The single-channel admission of {entry.Input.CorpusRecord.ObjectRef.PublisherUri} "
                        + $"({ContractWire.NameOf(entry.Two.Disposition)}) finds no retained licence page holding exactly its SPARQL licence.");
                }

                resolved[position] = entry with { Two = entry.Two.WithLicencePage(page) };
            }

            licencePagesSha256 = string.Join(",", pageIndexDigests.Order(StringComparer.Ordinal));
        }

        // 5. The acquisition's admissions kept; every other member re-bound.
        var final = resolved.ToDictionary(
            static entry => entry.Input.ObjectOrdinal,
            static entry => entry.R0.Disposition == LuxembourgRightsChannelDisposition.AgreedSameRunCcBy ? entry.R0 : entry.Two);
        var rights = new RightsPart(
            LuxembourgInFileRightsReader.ReadingTwoProfileSha256,
            LuxembourgRightsChannels.ResolutionTwoProfileSha256,
            indexRef.Sha256,
            catalogue.Count,
            acquiredDispositions,
            BodyCounts(resolved.Select(static entry => ContractWire.NameOf(entry.Two.Disposition))),
            resolved.Count(static entry => entry.R0.Disposition == LuxembourgRightsChannelDisposition.AgreedSameRunCcBy))
        {
            LicencePagesSha256 = licencePagesSha256,
            SingleChannelAdmitted = licencePagesSha256 is null
                ? null
                : resolved.Count(entry => LuxembourgRightsChannelResolution.IsSingleChannel(entry.Two.Disposition) &&
                    LuxembourgRightsAdmissionPolicies.Admits(admissionPolicySha256, entry.Two)),
        };
        return (population.WithRederivedRights(final), rights);

        static void Refuse(
            IEnumerable<(LuxembourgHeldBodyDerivationInput Input, LuxembourgRightsChannelResolution R0, LuxembourgRightsChannelResolution Two)> members,
            string what)
        {
            var named = members.ToArray();
            if (named.Length != 0)
            {
                throw new CustodyIntegrityException(
                    $"{named.Length} held member(s) {what}: "
                    + string.Join("; ", named.Take(20).Select(static entry =>
                        $"{entry.Input.CorpusRecord.ObjectRef.PublisherUri} ({ContractWire.NameOf(entry.R0.Disposition)} -> "
                        + $"{ContractWire.NameOf(entry.Two.Disposition)})"))
                    + (named.Length > 20 ? "; …" : ""));
            }
        }
    }

    // ---- The record's shape (snake_case on the wire, through ContractJson). ----
    private sealed record RecordDocument(
        string Schema,
        AcquisitionPart Acquisition,
        VerifiedUnderPart VerifiedUnder,
        RulesPart Rules,
        OutputsPart Outputs,
        CountsPart Counts,
        string PublisherSession)
    {
        /// <summary>Release 2's rights re-resolution; absent from the wire under Release 1, whose record keeps its bytes.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public RightsPart? Rights { get; init; }
    }

    /// <summary>
    /// Release 2's rights: the reading /2 and resolution /2 digests, the in-file evidence index /2 they produced, how many
    /// manifestations the catalogue licensed, the dispositions as acquired and under resolution /2 (every member), and how many
    /// acquisition admissions were kept (each agreeing under /2). The final dispositions are <c>counts.rights_dispositions</c>.
    /// </summary>
    private sealed record RightsPart(
        string InFileReadingProfileSha256,
        string ResolutionProfileSha256,
        string InFileEvidenceSha256,
        int CatalogueManifestations,
        BodyCount[] DispositionsAsAcquired,
        BodyCount[] DispositionsUnderResolution2,
        int AcquiredAdmissionsKept)
    {
        /// <summary>Under one of the owner's rulings: the licence-page index the single-channel admissions cite; absent otherwise.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? LicencePagesSha256 { get; init; }

        /// <summary>Under one of the owner's rulings: how many single-channel bodies the policy admits (within its evidence window).</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public int? SingleChannelAdmitted { get; init; }
    }

    private sealed record EvidenceIndexDocument(
        string Schema,
        string ReadingProfileSha256,
        string ResolutionProfileSha256,
        SourceArtifactRef[] InFileEvidenceV1,
        EvidenceReading[] Readings);

    private sealed record EvidenceReading(
        int ObjectOrdinal,
        string ManifestationIri,
        string BodySha256,
        string Status,
        string[] OwnBlockLicenceIris,
        string[] DeclaredLicenceIris,
        EvidenceNamedIdentity[] NamedIdentities,
        string[] FrbrManifestationIdentities);

    private sealed record EvidenceNamedIdentity(string Identity, string[] LicenceIris);

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
