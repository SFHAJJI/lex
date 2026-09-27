using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Europe;

/// <summary>
/// Why an eligible expression's Formex package was not requested, or was requested and admitted but
/// not acquired, for a reason that is not the transport's. Closed; the outcome's detail names the
/// case. <c>None</c> is the value of every other outcome kind.
/// </summary>
public enum EuFormexPackageNotAcquiredReason
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>
    /// The run holds no body for this expression (Decision 89: English bodies only until the French
    /// expressions land), and the corpus builder binds every Formex outcome to exactly one held body,
    /// so a package for it could not be served. No request was sent.
    /// </summary>
    [JsonStringEnumMemberName("body_not_held")]
    BodyNotHeld = 1,

    /// <summary>The expression's official language has no document-fetch address (only ENG and FRA do). No request was sent.</summary>
    [JsonStringEnumMemberName("language_not_addressable")]
    LanguageNotAddressable = 2,

    /// <summary>The enumeration lists more than one distinct <c>fmx4</c> manifestation for the expression. No request was sent.</summary>
    [JsonStringEnumMemberName("manifestation_not_singular")]
    ManifestationNotSingular = 3,

    /// <summary>The run's publisher identities do not admit as Cellar WEMI references, or the manifestation is not an admitted address. No request was sent.</summary>
    [JsonStringEnumMemberName("identity_not_admitted")]
    IdentityNotAdmitted = 4,

    /// <summary>
    /// The package was fetched and admitted and its inventory names annex members, and this build
    /// has no annex classification chain in production; the classification reconciliation would
    /// refuse the corpus for an acquired inventory with unclassified annexes, so the package is held
    /// in custody and the expression is not acquired until the annex slice lands.
    /// </summary>
    [JsonStringEnumMemberName("annex_classification_not_built")]
    AnnexClassificationNotBuilt = 5,
}

/// <summary>One expression's acquisition: the typed outcome and the publisher requests it spent, robots excluded.</summary>
public sealed class EuFormexPackageAcquisitionResult
{
    public EuFormexPackageAcquisitionResult(EuFormexPackageOutcome outcome, int productRequestCount)
    {
        Outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
        ArgumentOutOfRangeException.ThrowIfNegative(productRequestCount);
        ProductRequestCount = productRequestCount;
    }

    public EuFormexPackageOutcome Outcome { get; }

    public int ProductRequestCount { get; }
}

/// <summary>
/// Acquires one eligible expression's Formex package from the office, live, and turns it into the
/// typed <see cref="EuFormexPackageOutcome"/> the population closes over.
/// </summary>
/// <remarks>
/// <para>
/// The route is the one the office serves today (observed 2026-09-04 on GDPR and pinned by the
/// reachability tests): <c>GET https://publications.europa.eu/resource/cellar/{manifestation}</c>
/// with <c>Accept: application/zip;mtype=fmx4</c>, a 303 to <c>{manifestation}/zip</c> on the same
/// host, then the 200 ZIP. The manifestation is the exact <c>fmx4</c> manifestation the expression's
/// own enumeration delivered with a proof, never a guess from the work. The fetch goes through the
/// acquisition session (<see cref="EuRepeatedEnumerationExecutor.RunDocumentFetchAsync"/>): its own
/// robots bootstrap, pacing, the shared wire ceiling, and a custody write receipt per hop; the
/// binding then checks the retained route answers the request that was sent and that the terminal
/// names the retained ZIP's receipt.
/// </para>
/// <para>
/// What is not acquired, stated as its own outcome rather than as a transport refusal: an expression
/// whose body the run does not hold (the builder binds every Formex outcome to one held body; today
/// English only, Decision 89), a language without an address, an ambiguous manifestation, an
/// identity the Cellar grammar refuses, and a package whose inventory names annexes (the annex
/// classification chain is not composed in production; the package stays in custody). Each is
/// <c>not_acquired</c> with a <see cref="EuFormexPackageNotAcquiredReason"/>. A publisher answer
/// other than 200 or 404 is <c>route_refused</c> with the status; a 200 that is not an admissible
/// Formex package is <c>package_rejected</c> with the inventory producer's refusal.
/// </para>
/// </remarks>
public sealed class EuFormexPackageAcquisitionProducer
{
    /// <summary>
    /// The annex interpretation profile the inventory producer reads, one fixed text: the same rule
    /// set the reference tests use, now the production profile. Its digest is the inventory's
    /// <c>InterpretationRuleProfileRef</c>.
    /// </summary>
    public const string AnnexInterpretationProfile =
        "lex-v3-eu-formex-annex-interpretation-profile/1\n"
        + "document_root=DOC\n"
        + "annex_root=ANNEX\n"
        + "schema_prefix=http://formex.publications.europa.eu/schema/formex-\n"
        + "member_identity=document_reference_file+sequence\n"
        + "ordering=sequence+package_entry\n"
        + "title=required\n"
        + "page_extent=inclusive_positive_consistent\n";

    public static SourceArtifactRef AnnexInterpretationProfileRef { get; } = new(
        "urn:uuid:5b1d7c2e-9a3f-4e61-8c4b-2d7f0a9e6c13",
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(AnnexInterpretationProfile))));

    private const string EnglishLanguageAuthorityIri = "http://publications.europa.eu/resource/authority/language/ENG";
    private const string FrenchLanguageAuthorityIri = "http://publications.europa.eu/resource/authority/language/FRA";
    private static readonly string[] CellarOrigins =
    [
        "http://publications.europa.eu/resource/cellar/",
        "https://publications.europa.eu/resource/cellar/",
    ];

    private readonly ICustodyStore _custodyStore;
    private readonly EuRepeatedEnumerationExecutor _executor;
    private readonly EuFormexAnnexInventoryProducer _inventories;

    public EuFormexPackageAcquisitionProducer(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, testHandlerOverride: null)
    {
    }

    /// <summary>Test-only seam, the same one every Europe producer declares.</summary>
    internal EuFormexPackageAcquisitionProducer(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
        ArgumentNullException.ThrowIfNull(timeProvider);
        _executor = new EuRepeatedEnumerationExecutor(custodyStore, timeProvider, testHandlerOverride);
        _inventories = new EuFormexAnnexInventoryProducer(custodyStore);
    }

    /// <param name="enumeration">The expression's delivered, Formex-eligible manifestation enumeration.</param>
    /// <param name="heldCorpusObjects">The run's held corpus bodies; an expression bound to none is not acquired.</param>
    /// <param name="documentFetchRendererSource">The renderer-source artifact of the document-fetch plan.</param>
    /// <param name="wireBudget">The run's one ceiling, robots included.</param>
    public async Task<EuFormexPackageAcquisitionResult> RunAsync(
        EuFormexManifestationEnumerationResult enumeration,
        IReadOnlyList<SourceObjectRef> heldCorpusObjects,
        MachineQueryRendererSource documentFetchRendererSource,
        WireRequestBudget wireBudget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(enumeration);
        ArgumentNullException.ThrowIfNull(heldCorpusObjects);
        ArgumentNullException.ThrowIfNull(documentFetchRendererSource);
        ArgumentNullException.ThrowIfNull(wireBudget);
        if (!enumeration.IsFormexEligible)
        {
            throw new ArgumentException("Only a delivered, Formex-eligible enumeration is acquired.", nameof(enumeration));
        }

        var expression = enumeration.Expression;

        // ---- Decided before any traffic. ----
        if (!heldCorpusObjects.Any(held => LexCorpus6Builder.FormexMainBodyBelongsTo(expression, held)))
        {
            return NotAcquired(expression, EuFormexPackageNotAcquiredReason.BodyNotHeld,
                "the run holds no body for this expression, so the corpus could bind no Formex outcome to it");
        }

        EuDocumentLanguage language;
        switch (expression.OfficialLanguage)
        {
            case EnglishLanguageAuthorityIri:
                language = EuDocumentLanguage.Eng;
                break;
            case FrenchLanguageAuthorityIri:
                language = EuDocumentLanguage.Fra;
                break;
            default:
                return NotAcquired(expression, EuFormexPackageNotAcquiredReason.LanguageNotAddressable,
                    $"no document-fetch address exists for the language {expression.OfficialLanguage}");
        }

        var manifestations = enumeration.ManifestationTypes!
            .Where(static type => type.IsFormex)
            .Select(static type => type.PublisherManifestationIri)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (manifestations.Length != 1)
        {
            return NotAcquired(expression, EuFormexPackageNotAcquiredReason.ManifestationNotSingular,
                $"the enumeration lists {manifestations.Length} distinct fmx4 manifestations: {string.Join(", ", manifestations)}");
        }

        var manifestationIri = manifestations[0];
        if (!TryAdmitReferences(expression, manifestationIri, out var boundary, out var expressionRef, out var manifestationRef, out var identityDetail))
        {
            return NotAcquired(expression, EuFormexPackageNotAcquiredReason.IdentityNotAdmitted, identityDetail!);
        }

        var address = EuDocumentFetchAddress.TryCreate(
            "cellar", manifestationRef!.CanonicalKey, EuManifestationMediaType.ZipMtypeFmx4, language, out var addressRefusal);
        if (address is null)
        {
            return NotAcquired(expression, EuFormexPackageNotAcquiredReason.IdentityNotAdmitted,
                $"the manifestation {manifestationIri} is not a document-fetch address: {addressRefusal}");
        }

        // ---- The one GET, through the session; robots is evaluated against the manifestation path itself. ----
        var bound = new EuDocumentFetchPlan(address).Bind(NewUrn(), NewUrn(), documentFetchRendererSource);
        var attempt = await _executor.RunDocumentFetchAsync(bound.Request, bound.Request, wireBudget, cancellationToken)
            .ConfigureAwait(false);
        if (attempt.Evidence is null)
        {
            return new EuFormexPackageAcquisitionResult(
                EuFormexPackageOutcome.Refused(
                    expression,
                    attempt.Refusal ?? EuDocumentFetchAttemptRefusal.ObservationNotExecuted,
                    attempt.Detail ?? "the document-fetch attempt produced no route"),
                0);
        }

        var route = attempt.Evidence;
        var requests = route.Hops.Count;
        var fetch = EuDocumentFetchOutcome.Classify(route);
        if (fetch.Refusal == EuDocumentFetchRefusal.RequestedRepresentationNotServed && fetch.ObservedStatus == 404)
        {
            return new(EuFormexPackageOutcome.Unavailable(expression, fetch.Refusal.Value, 404), requests);
        }

        if (route.Outcome is not CompleteHttpRouteOutcome || fetch.ObservedStatus != 200)
        {
            return new(EuFormexPackageOutcome.RouteRefused(expression, fetch.ObservedStatus,
                $"the office answered {fetch.ObservedStatus?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "no status"} "
                + $"({route.Outcome.GetType().Name}) for {address.ResourceUri}"), requests);
        }

        var terminal = route.Hops[^1];
        if (attempt.HopWriteReceiptsByObservationId is null
            || !attempt.HopWriteReceiptsByObservationId.TryGetValue(terminal.ObservationId, out var zipReceipt))
        {
            return new(EuFormexPackageOutcome.RouteRefused(expression, terminal.Status,
                "the session retained the ZIP hop without a custody write receipt"), requests);
        }

        HttpLogicalRequest request;
        try
        {
            var requestBytes = await CustodyRestore.ReadByDigestCheckedAsync(
                    _custodyStore, route.Hops[0].LogicalRequestSha256, cancellationToken)
                .ConfigureAwait(false);
            request = HttpLogicalRequest.ParseAndVerify(requestBytes.Span);
        }
        catch (Exception exception) when (exception is CustodyIntegrityException or CustodyRequiredException or ArgumentException)
        {
            return new(EuFormexPackageOutcome.RouteRefused(expression, terminal.Status,
                "the request the session sent is not retained: " + exception.Message), requests);
        }

        EuFormexAnnexTransportBinding binding;
        try
        {
            binding = new EuFormexAnnexTransportBinding(boundary!, expressionRef!, manifestationRef, request, route, zipReceipt);
        }
        catch (ArgumentException exception)
        {
            return new(EuFormexPackageOutcome.RouteRefused(expression, terminal.Status,
                "the retained route does not bind as a Formex package transport: " + exception.Message), requests);
        }

        var inventory = await _inventories.RunAsync(
                binding, Encoding.UTF8.GetBytes(AnnexInterpretationProfile), AnnexInterpretationProfileRef, cancellationToken)
            .ConfigureAwait(false);
        if (!inventory.Produced)
        {
            return new(EuFormexPackageOutcome.PackageRejected(expression, inventory.Refusal, inventory.Detail!), requests);
        }

        if (inventory.Inventory!.Members.Count > 0)
        {
            return new(EuFormexPackageOutcome.NotAcquired(expression, EuFormexPackageNotAcquiredReason.AnnexClassificationNotBuilt,
                $"the package names {inventory.Inventory.Members.Count} annex member(s) and no annex classification chain is composed; "
                + $"the ZIP is retained under {zipReceipt.Reference.ContentSha256}"), requests);
        }

        return new(EuFormexPackageOutcome.Acquired(expression, inventory.Inventory), requests);
    }

    private static EuFormexPackageAcquisitionResult NotAcquired(
        LanguageScopedExpression expression, EuFormexPackageNotAcquiredReason reason, string detail) =>
        new(EuFormexPackageOutcome.NotAcquired(expression, reason, detail), 0);

    /// <summary>
    /// The run's expression names its work and itself by publisher IRI and carries the registry and
    /// identity profile its own reference was minted under; the Cellar WEMI references the binding
    /// admits are rebuilt from exactly those, so the package attaches to the run's identities and
    /// nothing else.
    /// </summary>
    private static bool TryAdmitReferences(
        LanguageScopedExpression expression,
        string manifestationIri,
        out EuWemiIdentityBoundary? boundary,
        out SourceObjectRef? expressionRef,
        out SourceObjectRef? manifestationRef,
        out string? detail)
    {
        boundary = null;
        expressionRef = null;
        manifestationRef = null;
        detail = null;
        if (!TryCellarKey(expression.Identity.PublisherWorkId, out var workKey)
            || !TryCellarKey(expression.Identity.PublisherExpressionId, out var expressionKey)
            || !TryCellarKey(manifestationIri, out var manifestationKey))
        {
            detail = $"a publisher identity is not a Cellar resource: work={expression.Identity.PublisherWorkId}; "
                + $"expression={expression.Identity.PublisherExpressionId}; manifestation={manifestationIri}";
            return false;
        }

        var registry = expression.SourceObject.EntityKind.RegistryRef;
        var identityProfile = expression.SourceObject.IdentityProfileRef;
        try
        {
            var candidateBoundary = new EuWemiIdentityBoundary(registry, identityProfile);
            var work = candidateBoundary.Require(
                Reference(registry, identityProfile, EuWemiRole.Work, expression.Identity.PublisherWorkId, workKey, null),
                EuWemiRole.Work, nameof(expression));
            var expressionCandidate = candidateBoundary.Require(
                Reference(registry, identityProfile, EuWemiRole.Expression, expression.Identity.PublisherExpressionId, expressionKey, work),
                EuWemiRole.Expression, nameof(expression));
            var manifestationCandidate = candidateBoundary.Require(
                Reference(registry, identityProfile, EuWemiRole.Manifestation, manifestationIri, manifestationKey, expressionCandidate),
                EuWemiRole.Manifestation, nameof(manifestationIri));
            boundary = candidateBoundary;
            expressionRef = expressionCandidate;
            manifestationRef = manifestationCandidate;
            return true;
        }
        catch (ArgumentException exception)
        {
            detail = exception.Message;
            return false;
        }
    }

    private static SourceObjectRef Reference(
        SourceArtifactRef registry,
        SourceArtifactRef identityProfile,
        EuWemiRole role,
        string publisherUri,
        string canonicalKey,
        SourceObjectRef? parent) => new(
        SourceCoreSchemaIds.SourceObjectRef,
        SourceAuthority.Cellar,
        new SourceRegistryMemberRef(registry, EuWemiIdentityBoundary.MemberKeyOf(role)),
        publisherUri,
        canonicalKey,
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalKey))),
        identityProfile,
        parent is null
            ? null
            : new SourceObjectKeyRef(parent.EntityKind, parent.PublisherUri, parent.CanonicalKey, parent.CanonicalKeySha256));

    private static bool TryCellarKey(string iri, out string key)
    {
        key = string.Empty;
        foreach (var origin in CellarOrigins)
        {
            if (iri.StartsWith(origin, StringComparison.Ordinal) && iri.Length > origin.Length)
            {
                key = iri[origin.Length..];
                return true;
            }
        }

        return false;
    }

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}
