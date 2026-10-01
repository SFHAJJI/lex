using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>
/// The two renderer-source artifacts a Luxembourg acquisition binds its requests through: the query
/// plan's renderer and the document-fetch plan's renderer, each the exact bytes of the source file
/// that holds it, retained in the run's custody (Decision 75). The Luxembourg live canary read these
/// two files from the checkout; this type is that pattern made production, as
/// <see cref="Europe.EuRendererSources"/> is for the Union side.
/// </summary>
public sealed class LuxembourgRendererSources
{
    /// <summary>The renderer files, relative to the checkout root, in constructor order.</summary>
    public static IReadOnlyList<string> RendererFiles { get; } = Array.AsReadOnly(new[]
    {
        "src/Lex.V3.Contracts/Source/Luxembourg/LuxembourgQueryPlan.cs",
        "src/Lex.V3.Contracts/Source/Luxembourg/LuxembourgDocumentFetchPlan.cs",
    });

    public LuxembourgRendererSources(MachineQueryRendererSource query, MachineQueryRendererSource documentFetch)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
        DocumentFetch = documentFetch ?? throw new ArgumentNullException(nameof(documentFetch));
    }

    public MachineQueryRendererSource Query { get; }

    public MachineQueryRendererSource DocumentFetch { get; }

    /// <summary>
    /// Reopens the caller's original file-to-artifact mapping from custody, preserving every resource ID.
    /// The mapping must name exactly RendererFiles. No checkout, new receipt or publisher request is used.
    /// These retained bytes identify renderer source; this method does not execute archived code.
    /// </summary>
    public static async Task<LuxembourgRendererSources> FromCustodyAsync(ICustodyStore custodyStore,
        IReadOnlyDictionary<string, SourceArtifactRef> referencesByFile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(referencesByFile);
        cancellationToken.ThrowIfCancellationRequested();
        if (referencesByFile.Count != RendererFiles.Count)
            throw new ArgumentException("The retained renderer mapping has the wrong number of roles.", nameof(referencesByFile));
        var snapshot = referencesByFile.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        if (!snapshot.Keys.Order(StringComparer.Ordinal).SequenceEqual(RendererFiles.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            snapshot.Values.Any(static reference => reference is null))
            throw new ArgumentException("The retained renderer mapping does not name the exact declared files.", nameof(referencesByFile));
        var sources = new MachineQueryRendererSource[RendererFiles.Count];
        for (var index = 0; index < RendererFiles.Count; index++)
        {
            var reference = snapshot[RendererFiles[index]];
            var bytes = await CustodyRestore.ReadByDigestCheckedAsync(custodyStore, reference.Sha256, cancellationToken).ConfigureAwait(false);
            sources[index] = MachineQueryRendererSource.Open(reference, bytes.Span);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new LuxembourgRendererSources(sources[0], sources[1]);
    }

    /// <summary>
    /// Reads the two renderer files under <paramref name="checkoutRoot"/>, holds each in custody and
    /// opens it. Throws when a file is missing or custody refuses: neither is a publisher outcome.
    /// </summary>
    public static async Task<LuxembourgRendererSources> FromCheckoutAsync(
        ICustodyStore custodyStore,
        string checkoutRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkoutRoot);
        var sources = new MachineQueryRendererSource[RendererFiles.Count];
        for (var index = 0; index < RendererFiles.Count; index++)
        {
            var path = Path.Combine(checkoutRoot, RendererFiles[index]);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Renderer source file '{RendererFiles[index]}' is not under the checkout root.", path);
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var (receipt, failure) = await CustodyHold.TryHoldAsync(custodyStore, bytes, cancellationToken)
                .ConfigureAwait(false);
            if (receipt is null)
            {
                throw new InvalidOperationException(
                    $"Custody refused to hold renderer source '{RendererFiles[index]}': {failure}");
            }

            sources[index] = MachineQueryRendererSource.Open(
                new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256),
                bytes);
        }

        return new LuxembourgRendererSources(sources[0], sources[1]);
    }
}

/// <summary>
/// One act, selected the way the live canary selects it: a range of ELI subject IRIs on the
/// publisher's own key order, from <see cref="StartInclusive"/> to <see cref="EndExclusive"/>.
/// <see cref="Name"/> prefixes the three family keys (<c>{name}-s</c>, <c>-a</c>, <c>-g</c>) and
/// must be a lowercase ASCII member key.
/// </summary>
public sealed record LuxembourgActRange
{
    /// <summary>The three act families the acquisition runs over the range, in run order.</summary>
    public static readonly IReadOnlyList<string> Families = ["S", "A", "G"];

    public LuxembourgActRange(string name, string startInclusive, string endExclusive)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("An act range needs a name.", nameof(name))
            : name;
        StartInclusive = startInclusive ?? throw new ArgumentNullException(nameof(startInclusive));
        EndExclusive = string.CompareOrdinal(startInclusive, endExclusive) < 0
            ? endExclusive
            : throw new ArgumentException("An act range must be finite and increasing.", nameof(endExclusive));

        // Every family range the acquisition will send is bound here, so an act the plan cannot
        // name (a name outside the member-key alphabet, a key part the cursor refuses) throws at
        // construction, before the caller has spent a request on anything.
        foreach (var family in Families)
        {
            _ = FamilyRange(family);
        }
    }

    /// <summary>
    /// Every absolute publisher IRI: a scheme starts with an ASCII letter, so A is the least
    /// possible first character. Blank-node subjects have an empty assertion key and remain
    /// outside this range, as they do in S's IRI-only subject universe. S, A and G are proven
    /// over the range; the source profile classifies which discovered bodies may be fetched.
    /// </summary>
    public static LuxembourgActRange WholePopulation { get; } = new("population", "A", "\uffff");

    public string Name { get; }

    public string StartInclusive { get; }

    public string EndExclusive { get; }

    /// <summary>The partition range of one family (<c>{name}-{family}</c>) over the act's ELI keys.</summary>
    public LuxembourgQueryPartitionRange FamilyRange(string family) => new(
        Name + "-" + family.ToLowerInvariant(),
        new LuxembourgQueryCursor(StartInclusive, "", "", "", "", ""),
        new LuxembourgQueryCursor(EndExclusive, "", "", "", "", ""));
}

/// <summary>Which step of the Luxembourg acquisition refused. Closed; the detail carries that step's own reason.</summary>
public enum LuxembourgFirstMountAcquisitionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>A vocabulary partition (P, T, C or O) was refused or its rows could not be reopened.</summary>
    [JsonStringEnumMemberName("vocabulary_refused")]
    VocabularyRefused = 1,

    /// <summary>The observed vocabulary did not open as a verified source profile.</summary>
    [JsonStringEnumMemberName("profile_refused")]
    ProfileRefused = 2,

    /// <summary>The adapter run over the act's families refused, or left no held body to derive from.</summary>
    [JsonStringEnumMemberName("run_refused")]
    RunRefused = 3,
}

/// <summary>
/// The Luxembourg inputs the Stage 3 envelope takes for one act, or one typed refusal. On a refusal
/// after the adapter ran, the run travels on the result so a refused build can still be read.
/// </summary>
public sealed class LuxembourgFirstMountAcquisitionResult
{
    private LuxembourgFirstMountAcquisitionResult(
        LuxembourgQueryExecutionResult? run,
        VerifiedLuxembourgSourceProfile? profile,
        SourceArtifactRef? vocabularyEvidenceRef,
        LuxembourgAknArticleInventoryPopulation? aknInventory,
        LuxembourgAknLegalContentPopulation? aknLegalContent,
        LuxembourgFirstMountAcquisitionRefusal? refusal,
        string? detail)
    {
        Run = run;
        Profile = profile;
        VocabularyEvidenceRef = vocabularyEvidenceRef;
        AknInventory = aknInventory;
        AknLegalContent = aknLegalContent;
        Refusal = refusal;
        Detail = detail;
    }

    /// <summary>The adapter run over the act's families; the refused run after <see cref="LuxembourgFirstMountAcquisitionRefusal.RunRefused"/>.</summary>
    public LuxembourgQueryExecutionResult? Run { get; }

    /// <summary>The source profile opened from the vocabulary this run observed. Present once the profile opened.</summary>
    public VerifiedLuxembourgSourceProfile? Profile { get; }

    /// <summary>The retained observation document the profile's snapshot names.</summary>
    public SourceArtifactRef? VocabularyEvidenceRef { get; }

    public LuxembourgAknArticleInventoryPopulation? AknInventory { get; }

    public LuxembourgAknLegalContentPopulation? AknLegalContent { get; }

    public LuxembourgFirstMountAcquisitionRefusal? Refusal { get; }

    public string? Detail { get; }

    public bool Delivered => Refusal is null;

    public static LuxembourgFirstMountAcquisitionResult Success(
        LuxembourgQueryExecutionResult run,
        VerifiedLuxembourgSourceProfile profile,
        SourceArtifactRef vocabularyEvidenceRef,
        LuxembourgAknArticleInventoryPopulation aknInventory,
        LuxembourgAknLegalContentPopulation aknLegalContent)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(vocabularyEvidenceRef);
        ArgumentNullException.ThrowIfNull(aknInventory);
        ArgumentNullException.ThrowIfNull(aknLegalContent);
        if (run.Refusal is not null)
        {
            throw new ArgumentException("A delivered Luxembourg acquisition carries a complete run.", nameof(run));
        }

        return new(run, profile, vocabularyEvidenceRef, aknInventory, aknLegalContent, null, null);
    }

    public static LuxembourgFirstMountAcquisitionResult Refused(
        LuxembourgFirstMountAcquisitionRefusal refusal,
        string detail,
        LuxembourgQueryExecutionResult? run = null,
        VerifiedLuxembourgSourceProfile? profile = null,
        SourceArtifactRef? vocabularyEvidenceRef = null)
    {
        if (refusal == LuxembourgFirstMountAcquisitionRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        return new(run, profile, vocabularyEvidenceRef, null, null, refusal, detail);
    }
}

/// <summary>
/// The Luxembourg half of the first real mount: everything the Stage 3 envelope needs from
/// Legilux for one act, acquired live in one process under one wire ceiling.
/// </summary>
/// <remarks>
/// <para>
/// This is the Luxembourg composition root the code base never had in production: the live
/// adapter canary built it inline, and <c>TestSupport/LuxembourgProfiles</c> records that "the LU
/// composition root is Stage 6". Its order is the canary's. First the vocabulary: the P, T, C and O
/// partitions over the whole store (O over the Creative Commons range), each an enumeration with
/// its own proof, reopened and classified into the publisher's own vocabulary values; those open a
/// <see cref="VerifiedLuxembourgSourceProfile"/>, or refuse it with the missing or conflicting
/// value named. Required values are expectations the observation is checked against, never a
/// source of observed values. Then the act: the S, A and G families over its ELI range through
/// <see cref="LuxembourgQueryExecutionAdapter"/>, whose Gazette loop fetches the bodies. Then the
/// Akoma Ntoso article inventory and legal content over the held bodies.
/// </para>
/// <para>
/// The declared scope (the range and what is fetched for it) is retained as the plan's scope
/// document before the first request, so the run's plan identity names what the run set out to
/// do; the vocabulary observation is retained as the snapshot's evidence. One
/// <see cref="WireRequestBudget"/> bounds the vocabulary partitions, the families and the fetches.
/// </para>
/// </remarks>
public sealed class LuxembourgFirstMountAcquisition
{
    private const string EndOfKeySpace = "￿";
    private const string CreativeCommonsRangeStart = "http://creativecommons.org/licenses/by/4.0/";
    private const string CreativeCommonsRangeEnd = "http://creativecommons.org/licenses/by/4.1/";
    private static readonly string[] VocabularyFamilies = ["P", "T", "C", "O"];
    private static readonly JsonSerializerOptions EvidenceJson = new() { WriteIndented = false };

    private readonly ICustodyStore _custodyStore;
    private readonly TimeProvider _timeProvider;
    private readonly System.Net.Http.HttpMessageHandler? _testHandlerOverride;

    public LuxembourgFirstMountAcquisition(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, testHandlerOverride: null)
    {
    }

    /// <summary>Test-only seam, the same one the Luxembourg executor declares; production calls the public constructor.</summary>
    internal LuxembourgFirstMountAcquisition(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _testHandlerOverride = testHandlerOverride;
    }

    public async Task<LuxembourgFirstMountAcquisitionResult> RunAsync(
        LuxembourgActRange act,
        LuxembourgRendererSources rendererSources,
        WireRequestBudget wireBudget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(act);
        ArgumentNullException.ThrowIfNull(rendererSources);
        ArgumentNullException.ThrowIfNull(wireBudget);

        var scopeRef = await HoldAsync(JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "lex-lu-first-mount-scope/1",
            act = new { act.Name, start = act.StartInclusive, end = act.EndExclusive },
            vocabulary = "P/T/C whole key space; O Creative Commons BY 4.0 range",
            documents = "the manifest's selected body of each admitted object, and every accepted Gazette listing of each as-published act",
        }, EvidenceJson), cancellationToken).ConfigureAwait(false);
        var plan = LuxembourgQueryPlan.CreateDefaultGraph(scopeRef);
        var planId = NewUrn();
        var executor = new LuxembourgRepeatedEnumerationExecutor(_custodyStore, _timeProvider, _testHandlerOverride);
        var reopenGlue = new RepeatedEnumerationDeliveryReopenGlue(_custodyStore);

        // ---- Vocabulary: four partitions, each proven, reopened and classified. ----
        var measured = new List<object>();
        var observed = new List<ObservedVocabulary>();
        foreach (var family in VocabularyFamilies)
        {
            var range = family == "O"
                ? Range("vocabulary-o", CreativeCommonsRangeStart, CreativeCommonsRangeEnd)
                : Range("vocabulary-" + family.ToLowerInvariant(), string.Empty, EndOfKeySpace);
            var request = new LuxembourgPartitionRunRequest(plan, planId, family, range, rendererSources.Query);
            var witness = plan.BindCount(planId, NewUrn(), NewUrn(), family, LuxembourgQueryPass.Pass1, range, rendererSources.Query);
            var outcome = await executor.RunPartitionAsync(request, witness.Request, wireBudget, cancellationToken)
                .ConfigureAwait(false);
            measured.Add(new { family, outcome.ProductRequestCount, refusal = Describe(outcome.Refusal) });
            if (outcome.Receipt is not { } receipt)
            {
                // The executor's refusal carries the count it learned and the requests the class
                // needs, precisely so a run that was too small still says how big the class is; the
                // detail keeps all of it, since the next run is sized from this one.
                return LuxembourgFirstMountAcquisitionResult.Refused(
                    LuxembourgFirstMountAcquisitionRefusal.VocabularyRefused,
                    $"vocabulary family {family}: {Describe(outcome.Refusal)}");
            }

            var proof = AbsenceFamilyEnumerationProof.TryCreate(
                range.PartitionId, receipt.Delivery, receipt.RetainedFloor, out var proofRefusal);
            if (proof is null)
            {
                return LuxembourgFirstMountAcquisitionResult.Refused(
                    LuxembourgFirstMountAcquisitionRefusal.VocabularyRefused,
                    $"vocabulary family {family}: enumeration proof refused: {proofRefusal}");
            }

            var interpretation = plan.CreateDeliveryProfile(planId, family);
            var pages = new List<RepeatedEnumerationResolvedEvidence>(receipt.Delivery.PagesA.Pages.Count);
            foreach (var page in receipt.Delivery.PagesA.Pages.OrderBy(static page => page.Ordinal))
            {
                pages.Add(await reopenGlue.ReopenPageEvidenceAsync(page.Evidence, cancellationToken).ConfigureAwait(false));
            }

            var rows = VerifiedRepeatedEnumerationRows.TryOpen(
                proof, receipt.Delivery, interpretation, receipt.Delivery.InterpretationProfileRef,
                receipt.Delivery.CountA.HttpEvidenceRef, pages, out var rowRefusal);
            if (rows is null)
            {
                return LuxembourgFirstMountAcquisitionResult.Refused(
                    LuxembourgFirstMountAcquisitionRefusal.VocabularyRefused,
                    $"vocabulary family {family}: reopened rows refused: {rowRefusal}");
            }

            var keyOrdinal = interpretation.ProjectionVariables.ToList().IndexOf("key_1");
            foreach (var row in rows)
            {
                var value = row.Terms[keyOrdinal].Value;
                if (value is null)
                {
                    return LuxembourgFirstMountAcquisitionResult.Refused(
                        LuxembourgFirstMountAcquisitionRefusal.VocabularyRefused,
                        $"vocabulary family {family}: a delivered row carries no key_1");
                }

                observed.AddRange(ClassifyVocabulary(family, value));
            }
        }

        var vocabulary = observed
            .Where(static value => value.Kind is not null)
            .Select(static value => new LuxembourgIriVocabularyValue(value.Kind!.Value, value.Iri))
            .Distinct()
            .ToArray();
        var missing = VerifiedLuxembourgSourceProfile.RequiredIriVocabulary.Except(vocabulary).ToArray();
        var (vocabularyReceipt, vocabularyFailure) = await CustodyHold.TryHoldAsync(
                _custodyStore,
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    schema = "lex-lu-vocabulary-observation/1",
                    measured,
                    observed = observed.Select(static value => new { value.Family, kind = value.Kind?.ToString(), value.Iri, value.Disposition }),
                    missing = missing.Select(static value => new { kind = value.Kind.ToString(), value.FullIri }),
                    limitation = "Observed publisher values only; required values are expectations, never a source of observed values.",
                }, EvidenceJson),
                cancellationToken)
            .ConfigureAwait(false);
        if (vocabularyReceipt is null)
        {
            return LuxembourgFirstMountAcquisitionResult.Refused(
                LuxembourgFirstMountAcquisitionRefusal.VocabularyRefused,
                $"custody refused to hold the vocabulary observation: {vocabularyFailure}");
        }

        var vocabularyEvidenceRef = new SourceArtifactRef(NewUrn(), vocabularyReceipt.Reference.ContentSha256);
        var snapshot = new LuxembourgVocabularySnapshot(vocabularyEvidenceRef, vocabularyEvidenceRef, vocabulary, []);
        var profile = VerifiedLuxembourgSourceProfile.TryOpen(snapshot, out var profileFailure);
        if (profile is null)
        {
            return LuxembourgFirstMountAcquisitionResult.Refused(
                LuxembourgFirstMountAcquisitionRefusal.ProfileRefused,
                $"{profileFailure?.Code}: {profileFailure?.Subject}; {missing.Length} required value(s) not observed",
                vocabularyEvidenceRef: vocabularyEvidenceRef);
        }

        // ---- The act: S, A and G over its range, through the adapter and its Gazette loop. ----
        var families = new List<(LuxembourgPartitionRunRequest, BoundMachineRequest, LuxembourgPartitionChain?)>(LuxembourgActRange.Families.Count);
        foreach (var family in LuxembourgActRange.Families)
        {
            var range = act.FamilyRange(family);
            var request = new LuxembourgPartitionRunRequest(plan, planId, family, range, rendererSources.Query);
            var witness = plan.BindCount(planId, NewUrn(), NewUrn(), family, LuxembourgQueryPass.Pass1, range, rendererSources.Query);
            families.Add((request, witness.Request, null));
        }

        var adapter = new LuxembourgQueryExecutionAdapter(_custodyStore, executor, profile);
        var run = act == LuxembourgActRange.WholePopulation
            ? await adapter.RunAdaptiveScopedAsync(families,
                [new LuxembourgScopePartitionFamilies(act.Name + "-s", act.Name + "-a", act.Name + "-g")],
                rendererSources.DocumentFetch, wireBudget, cancellationToken).ConfigureAwait(false)
            : await adapter.RunAsync(
                families, act.Name + "-g", act.Name + "-s", act.Name + "-a",
                rendererSources.DocumentFetch, wireBudget, cancellationToken)
            .ConfigureAwait(false);
        if (run.Refusal is { } runRefusal)
        {
            return LuxembourgFirstMountAcquisitionResult.Refused(
                LuxembourgFirstMountAcquisitionRefusal.RunRefused,
                $"{runRefusal.Code}: {runRefusal.Detail}",
                run, profile, vocabularyEvidenceRef);
        }

        if (run.HeldBodyDerivationPopulation is not { } heldBodies)
        {
            return LuxembourgFirstMountAcquisitionResult.Refused(
                LuxembourgFirstMountAcquisitionRefusal.RunRefused,
                "the run completed without a held-body derivation population, so nothing can be derived from it",
                run, profile, vocabularyEvidenceRef);
        }

        var inventory = await new LuxembourgAknArticleInventoryProducer(_custodyStore)
            .RunAsync(heldBodies, cancellationToken).ConfigureAwait(false);
        var legalContent = await new LuxembourgAknLegalContentProfileProducer(_custodyStore)
            .RunAsync(inventory, cancellationToken).ConfigureAwait(false);
        return LuxembourgFirstMountAcquisitionResult.Success(run, profile, vocabularyEvidenceRef, inventory, legalContent);
    }

    /// <summary>
    /// One observed key_1 value of one vocabulary family, classified by the family and the value's
    /// own authority root into the publisher vocabulary kind it can be, or into a typed quarantine
    /// when it is none. Ported from the live adapter canary unchanged in substance.
    /// </summary>
    internal static IEnumerable<ObservedVocabulary> ClassifyVocabulary(string family, string value)
    {
        var required = VerifiedLuxembourgSourceProfile.RequiredIriVocabulary;
        if (family == "P")
        {
            var kinds = required
                .Where(item => item.FullIri == value && item.Kind is
                    LuxembourgVocabularyKind.AssertionPredicate or LuxembourgVocabularyKind.RelationPredicate)
                .Select(static item => item.Kind)
                .Distinct()
                .ToArray();
            return kinds.Length == 0
                ? [new ObservedVocabulary(family, null, value, "typed_quarantine_unruled_predicate")]
                : kinds.Select(kind => new ObservedVocabulary(family, kind, value, "governed_predicate_observed"));
        }

        LuxembourgVocabularyKind? category = family == "T" ? LuxembourgVocabularyKind.ResourceClass : null;
        if (family is "C" or "O")
        {
            (string Root, LuxembourgVocabularyKind Kind)[] roots =
            [
                ("http://data.legilux.public.lu/resource/authority/resource-type/", LuxembourgVocabularyKind.TypeDocument),
                ("http://data.legilux.public.lu/resource/authority/user-format/", LuxembourgVocabularyKind.UserFormat),
                ("http://data.legilux.public.lu/resource/authority/statut-version/", LuxembourgVocabularyKind.LegalValue),
                ("http://publications.europa.eu/resource/authority/language/", LuxembourgVocabularyKind.Language),
                ("http://data.legilux.public.lu/resource/authority/license/", LuxembourgVocabularyKind.Licence),
                ("http://creativecommons.org/licenses/", LuxembourgVocabularyKind.Licence),
            ];
            category = roots
                .Where(root => value.StartsWith(root.Root, StringComparison.Ordinal))
                .Select(static root => (LuxembourgVocabularyKind?)root.Kind)
                .SingleOrDefault();
        }

        var governed = category is { } kind && required.Any(item => item.Kind == kind && item.FullIri == value);
        return [new ObservedVocabulary(family, category, value, governed ? "governed_value_observed" : "typed_quarantine_unruled_value")];
    }

    internal sealed record ObservedVocabulary(string Family, LuxembourgVocabularyKind? Kind, string Iri, string Disposition);

    /// <summary>Everything the executor's refusal says, in one line: code, count, status and its own detail.</summary>
    private static string? Describe(LuxembourgEnumerationRefusalDetail? refusal) => refusal is null
        ? null
        : $"{refusal.Code}; count={refusal.ObservedCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}; "
          + $"status={refusal.TerminalStatus?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}; "
          + (refusal.CoreRefusalDetail ?? "no further detail");

    private static LuxembourgQueryPartitionRange Range(string partitionId, string start, string end) => new(
        partitionId,
        new LuxembourgQueryCursor(start, "", "", "", "", ""),
        new LuxembourgQueryCursor(end, "", "", "", "", ""));

    private async Task<SourceArtifactRef> HoldAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        var (receipt, failure) = await CustodyHold.TryHoldAsync(_custodyStore, bytes, cancellationToken)
            .ConfigureAwait(false);
        if (receipt is null)
        {
            throw new InvalidOperationException($"Custody refused to hold the declared scope: {failure}");
        }

        return new SourceArtifactRef(NewUrn(), receipt.Reference.ContentSha256);
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}
