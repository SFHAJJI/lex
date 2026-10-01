using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
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
    /// The package was fetched and admitted and its inventory names annex members, but the held work
    /// body does not carry the publisher's XHTML annex convention the annex chain reconciles against
    /// (it is not XHTML, or it lacks the <c>*.fmx</c> unit wrappers), so no annex evidence can be
    /// bound. The ZIP stays in custody; no PDF request was sent.
    /// </summary>
    [JsonStringEnumMemberName("annex_xhtml_not_inventoried")]
    AnnexXhtmlNotInventoried = 5,

    /// <summary>
    /// The package names annexes and the office did not serve the work's PDF the annex chain reads
    /// page labels from (no PDF manifestation listed, or the PDF route did not end in a complete
    /// 200). The ZIP stays in custody.
    /// </summary>
    [JsonStringEnumMemberName("annex_pdf_not_served")]
    AnnexPdfNotServed = 6,

    /// <summary>The Formex and XHTML annex inventories and the PDF did not bind as one annex population (<c>EuAnnexEvidenceBindingRefusal</c> in the detail).</summary>
    [JsonStringEnumMemberName("annex_evidence_not_bound")]
    AnnexEvidenceNotBound = 7,

    /// <summary>The bound annex evidence was not classified against the PDF route (<c>EuBoundAnnexBodyClassificationRefusal</c> in the detail).</summary>
    [JsonStringEnumMemberName("annex_body_not_classified")]
    AnnexBodyNotClassified = 8,

    /// <summary>The acquisition result could not be retained for independent replay.</summary>
    [JsonStringEnumMemberName("checkpoint_not_retained")]
    CheckpointNotRetained = 9,
}

/// <summary>
/// One expression's acquisition: the typed outcome, the publisher requests it spent (robots
/// excluded) and, for an acquired package that names annexes, the classification of those annexes
/// against the work's PDF, which the classification reconciliation requires.
/// </summary>
public sealed class EuFormexPackageAcquisitionResult
{
    public EuFormexPackageAcquisitionResult(
        EuFormexPackageOutcome outcome,
        int productRequestCount,
        EuBoundAnnexBodyClassification? annexClassification = null)
    {
        Outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
        ArgumentOutOfRangeException.ThrowIfNegative(productRequestCount);
        if (annexClassification is not null
            && (outcome.Kind != EuFormexPackageOutcomeKind.Acquired
                || outcome.AcquiredInventory!.Members.Count == 0))
        {
            throw new ArgumentException(
                "An annex classification travels only with an acquired package that names annexes.",
                nameof(annexClassification));
        }

        if (annexClassification is null
            && outcome.Kind == EuFormexPackageOutcomeKind.Acquired
            && outcome.AcquiredInventory!.Members.Count > 0)
        {
            throw new ArgumentException(
                "An acquired package that names annexes carries their classification.",
                nameof(annexClassification));
        }

        ProductRequestCount = productRequestCount;
        AnnexClassification = annexClassification;
    }

    public SourceArtifactRef? CheckpointRef { get; private init; }

    internal EuFormexPackageAcquisitionResult WithCheckpoint(SourceArtifactRef checkpoint) =>
        new(Outcome, ProductRequestCount, AnnexClassification) { CheckpointRef = checkpoint };

    public EuFormexPackageOutcome Outcome { get; }

    public int ProductRequestCount { get; }

    public EuBoundAnnexBodyClassification? AnnexClassification { get; }
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
/// A package whose inventory names annexes goes on through the annex chain, because the
/// classification reconciliation refuses a build carrying an acquired inventory with unclassified
/// annexes: the held work body is read as the publisher's XHTML annex inventory
/// (<see cref="EuXhtmlAnnexInventoryProducer"/>, one profile naming the held bytes), the work's PDF
/// is fetched on the document-fetch route (<c>GET cellar/{work}</c> with the <c>pdfa2a</c> or
/// <c>pdf</c> accept the enumeration lists, the office's 303 to the PDF item, then the 200), the
/// three are bound as one annex population with the PDF page labels
/// (<see cref="EuAnnexEvidenceBinder"/>, on the manifestation-level transport) and the bound
/// members are classified against the PDF route (<see cref="EuBoundAnnexBodyClassifier"/>). The
/// classification travels on the result to the reconciliation. One extra GET per annex-bearing
/// expression, robots included in its own session.
/// </para>
/// <para>
/// What is not acquired, stated as its own outcome rather than as a transport refusal: an expression
/// whose body the run does not hold (the builder binds every Formex outcome to one held body; today
/// English only, Decision 89), a language without an address, an ambiguous manifestation, an
/// identity the Cellar grammar refuses, and an annex-bearing package whose chain does not close (the
/// held body without the XHTML annex convention, the PDF not served, the evidence not bound, the
/// body not classified); the ZIP stays in custody in every annex case. Each is <c>not_acquired</c>
/// with a <see cref="EuFormexPackageNotAcquiredReason"/>. A publisher answer other than 200 or 404
/// on the package route is <c>route_refused</c> with the status; a 200 that is not an admissible
/// Formex package is <c>package_rejected</c> with the inventory producer's refusal.
/// </para>
/// </remarks>
public sealed partial class EuFormexPackageAcquisitionProducer
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
    private const string XhtmlInventoryProfileHeader = "lex-v3-eu-xhtml-annex-inventory-profile/1";
    private const string XhtmlNamespace = "http://www.w3.org/1999/xhtml";
    private const string ReconciliationProfileHeader = "lex-v3-eu-annex-evidence-reconciliation-profile/1";
    private const string ReconciliationRule = "rule=pdf_page_label_bijection/1";
    private const string ClassificationProfileHeader = "lex-v3-eu-bound-annex-body-classification-profile/1";
    private const string ClassificationRule = "classification=pdfpig-0.1.11:no_glyphs+image";
    private const string PdfA2aTypeToken = "pdfa2a";
    private const string PdfTypeToken = "pdf";
    private static readonly string[] CellarOrigins =
    [
        "http://publications.europa.eu/resource/cellar/",
        "https://publications.europa.eu/resource/cellar/",
    ];

    private readonly ICustodyStore _custodyStore;
    private readonly EuRepeatedEnumerationExecutor _executor;
    private readonly EuFormexAnnexInventoryProducer _inventories;
    private readonly EuXhtmlAnnexInventoryProducer _xhtmlInventories;
    private readonly EuAnnexEvidenceBinder _binder;
    private readonly EuBoundAnnexBodyClassifier _classifier;

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
        _xhtmlInventories = new EuXhtmlAnnexInventoryProducer(custodyStore);
        _binder = new EuAnnexEvidenceBinder(custodyStore);
        _classifier = new EuBoundAnnexBodyClassifier(custodyStore);
    }

    /// <param name="enumeration">The expression's delivered, Formex-eligible manifestation enumeration.</param>
    /// <param name="corpusRecordSet">The run's corpus record set; an expression bound to none of its held bodies is not acquired.</param>
    /// <param name="workCelex">The CELEX of the work the run acquired, carried on the annex binding.</param>
    /// <param name="documentFetchRendererSource">The renderer-source artifact of the document-fetch plan.</param>
    /// <param name="wireBudget">The run's one ceiling, robots included.</param>
    private async Task<EuFormexPackageAcquisitionResult> RunCoreAsync(
        EuFormexManifestationEnumerationResult enumeration,
        VerifiedCorpusRecordSet? corpusRecordSet,
        string workCelex,
        MachineQueryRendererSource documentFetchRendererSource,
        WireRequestBudget wireBudget,
        PackageReplayContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(enumeration);
        ArgumentException.ThrowIfNullOrWhiteSpace(workCelex);
        ArgumentNullException.ThrowIfNull(documentFetchRendererSource);
        ArgumentNullException.ThrowIfNull(wireBudget);
        if (!enumeration.IsFormexEligible)
        {
            throw new ArgumentException("Only a delivered, Formex-eligible enumeration is acquired.", nameof(enumeration));
        }

        var expression = enumeration.Expression;

        // ---- Decided before any traffic. ----
        var heldRecords = (corpusRecordSet?.Set.Records ?? [])
            .Where(record => record.Body.Kind == CorpusBodyRecordKind.Held
                && LexCorpus6Builder.FormexMainBodyBelongsTo(expression, record.ObjectRef))
            .Take(2)
            .ToArray();
        if (heldRecords.Length != 1)
        {
            return NotAcquired(expression, EuFormexPackageNotAcquiredReason.BodyNotHeld,
                heldRecords.Length == 0
                    ? "the run holds no body for this expression, so the corpus could bind no Formex outcome to it"
                    : "the run holds more than one body this expression binds to");
        }

        var heldRecord = heldRecords[0];
        EuDocumentLanguage language;
        string languageCode;
        switch (expression.OfficialLanguage)
        {
            case EnglishLanguageAuthorityIri:
                language = EuDocumentLanguage.Eng;
                languageCode = "EN";
                break;
            case FrenchLanguageAuthorityIri:
                language = EuDocumentLanguage.Fra;
                languageCode = "FR";
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
        if (!TryAdmitReferences(expression, manifestationIri, out var boundary, out var workRef, out var expressionRef, out var manifestationRef, out var identityDetail))
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
        var attempt = await FetchAsync(address, documentFetchRendererSource, wireBudget, context, cancellationToken).ConfigureAwait(false);
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

        var request = await TryRestoreRequestAsync(route.Hops[0], cancellationToken).ConfigureAwait(false);
        if (request.Request is null)
        {
            return new(EuFormexPackageOutcome.RouteRefused(expression, terminal.Status,
                "the request the session sent is not retained: " + request.Detail), requests);
        }

        EuFormexAnnexTransportBinding binding;
        try
        {
            binding = new EuFormexAnnexTransportBinding(boundary!, expressionRef!, manifestationRef, request.Request, route, zipReceipt);
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

        if (inventory.Inventory!.Members.Count == 0)
        {
            return new(EuFormexPackageOutcome.Acquired(expression, inventory.Inventory), requests);
        }

        // ---- The package names annexes: the annex chain, or a typed reason with the ZIP retained. ----
        var retained = $"the ZIP is retained under {zipReceipt.Reference.ContentSha256}";
        var xhtmlReceipt = heldRecord.Body.Receipt!;
        var xhtmlProfile = context.Profile(
            XhtmlInventoryProfileHeader, "transport_sha256=" + xhtmlReceipt.Reference.ContentSha256, "xhtml_namespace=" + XhtmlNamespace);
        var xhtml = await _xhtmlInventories.RunAsync(xhtmlReceipt, xhtmlProfile.Bytes, xhtmlProfile.Reference, cancellationToken)
            .ConfigureAwait(false);
        if (!xhtml.Produced)
        {
            return new(EuFormexPackageOutcome.NotAcquired(expression, EuFormexPackageNotAcquiredReason.AnnexXhtmlNotInventoried,
                $"the package names {inventory.Inventory.Members.Count} annex member(s) and the held work body carries no publisher "
                + $"annex inventory: {xhtml.Refusal}: {xhtml.Detail}; {retained}"), requests);
        }

        var pdfMediaType = PdfMediaTypeListed(enumeration);
        if (pdfMediaType is null)
        {
            return new(EuFormexPackageOutcome.NotAcquired(expression, EuFormexPackageNotAcquiredReason.AnnexPdfNotServed,
                $"the package names {inventory.Inventory.Members.Count} annex member(s) and the enumeration lists no pdf or pdfa2a "
                + $"manifestation for the expression; {retained}"), requests);
        }

        var pdfAddress = EuDocumentFetchAddress.TryCreate("cellar", workRef!.CanonicalKey, pdfMediaType.Value, language, out var pdfAddressRefusal);
        if (pdfAddress is null)
        {
            return new(EuFormexPackageOutcome.NotAcquired(expression, EuFormexPackageNotAcquiredReason.AnnexPdfNotServed,
                $"the work {workRef.PublisherUri} is not a document-fetch address: {pdfAddressRefusal}; {retained}"), requests);
        }

        var pdfAttempt = await FetchAsync(pdfAddress, documentFetchRendererSource, wireBudget, context, cancellationToken).ConfigureAwait(false);
        if (pdfAttempt.Evidence is null)
        {
            return new(EuFormexPackageOutcome.NotAcquired(expression, EuFormexPackageNotAcquiredReason.AnnexPdfNotServed,
                $"the PDF route was not executed: {pdfAttempt.Refusal}: {pdfAttempt.Detail}; {retained}"), requests);
        }

        var pdfRoute = pdfAttempt.Evidence;
        requests += pdfRoute.Hops.Count;
        var pdfTerminal = pdfRoute.Hops[^1];
        var pdfFetch = EuDocumentFetchOutcome.Classify(pdfRoute);
        if (pdfRoute.Outcome is not CompleteHttpRouteOutcome || pdfFetch.ObservedStatus != 200
            || pdfAttempt.HopWriteReceiptsByObservationId is null
            || !pdfAttempt.HopWriteReceiptsByObservationId.TryGetValue(pdfTerminal.ObservationId, out var pdfReceipt))
        {
            return new(EuFormexPackageOutcome.NotAcquired(expression, EuFormexPackageNotAcquiredReason.AnnexPdfNotServed,
                $"the office answered {pdfFetch.ObservedStatus?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "no status"} "
                + $"({pdfRoute.Outcome.GetType().Name}) for {pdfAddress.ResourceUri}; {retained}"), requests);
        }

        var pdfManifestationRef = TryAdmitPdfManifestation(boundary!, expressionRef!, expression, pdfTerminal.RequestUri, out var pdfManifestationDetail);
        if (pdfManifestationRef is null)
        {
            return new(EuFormexPackageOutcome.NotAcquired(expression, EuFormexPackageNotAcquiredReason.AnnexEvidenceNotBound,
                $"the PDF the office served is not a manifestation of this expression: {pdfManifestationDetail}; {retained}"), requests);
        }

        var reconciliationProfile = context.Profile(
            ReconciliationProfileHeader,
            "formex_inventory_sha256=" + inventory.Inventory.IdentitySha256,
            "xhtml_inventory_sha256=" + xhtml.Inventory!.IdentitySha256,
            "pdf_transport_sha256=" + pdfReceipt.Reference.ContentSha256,
            ReconciliationRule);
        var bound = await _binder.BindTransportAsync(
                boundary!, binding, workCelex, languageCode, pdfManifestationRef, corpusRecordSet!, inventory.Inventory, xhtml.Inventory,
                pdfReceipt, reconciliationProfile.Bytes, reconciliationProfile.Reference, cancellationToken)
            .ConfigureAwait(false);
        if (bound.Binding is null)
        {
            return new(EuFormexPackageOutcome.NotAcquired(expression, EuFormexPackageNotAcquiredReason.AnnexEvidenceNotBound,
                $"{bound.Refusal}: {bound.Detail}; {retained}"), requests);
        }

        var officialRequest = await TryRestoreRequestAsync(pdfRoute.Hops[0], cancellationToken).ConfigureAwait(false);
        var terminalRequest = await TryRestoreRequestAsync(pdfTerminal, cancellationToken).ConfigureAwait(false);
        if (officialRequest.Request is null || terminalRequest.Request is null)
        {
            return new(EuFormexPackageOutcome.NotAcquired(expression, EuFormexPackageNotAcquiredReason.AnnexBodyNotClassified,
                $"the PDF requests the session sent are not retained: {officialRequest.Detail ?? terminalRequest.Detail}; {retained}"), requests);
        }

        var classificationProfile = context.Profile(
            ClassificationProfileHeader,
            "binding_identity_sha256=" + bound.Binding.IdentitySha256,
            "pdf_transport_sha256=" + pdfReceipt.Reference.ContentSha256,
            ClassificationRule);
        var classified = await _classifier.RunAsync(
                bound.Binding, pdfAddress, officialRequest.Request, terminalRequest.Request, pdfRoute,
                classificationProfile.Bytes, classificationProfile.Reference, cancellationToken)
            .ConfigureAwait(false);
        if (classified.Classification is null)
        {
            return new(EuFormexPackageOutcome.NotAcquired(expression, EuFormexPackageNotAcquiredReason.AnnexBodyNotClassified,
                $"{classified.Refusal}: {classified.Detail}; {retained}"), requests);
        }

        return new(EuFormexPackageOutcome.Acquired(expression, inventory.Inventory), requests, classified.Classification);
    }

    private async Task<EuDocumentFetchAttemptResult> FetchAsync(
        EuDocumentFetchAddress address,
        MachineQueryRendererSource documentFetchRendererSource,
        WireRequestBudget wireBudget,
        PackageReplayContext context,
        CancellationToken cancellationToken)
    {
        if (context.IsReplay)
            return await context.FetchAsync(_custodyStore, address, cancellationToken).ConfigureAwait(false);
        var bound = new EuDocumentFetchPlan(address).Bind(NewUrn(), NewUrn(), documentFetchRendererSource);
        var result = await _executor.RunDocumentFetchAsync(bound.Request, bound.Request, wireBudget, cancellationToken)
            .ConfigureAwait(false);
        context.Capture(address, result);
        return result;
    }

    private async Task<(HttpLogicalRequest? Request, string? Detail)> TryRestoreRequestAsync(
        RoutedHttpHop hop, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await CustodyRestore.ReadByDigestCheckedAsync(_custodyStore, hop.LogicalRequestSha256, cancellationToken)
                .ConfigureAwait(false);
            return (HttpLogicalRequest.ParseAndVerify(bytes.Span), null);
        }
        catch (Exception exception) when (exception is CustodyIntegrityException or CustodyRequiredException or ArgumentException)
        {
            return (null, exception.Message);
        }
    }

    /// <summary>The PDF accept the enumeration proves the office lists for the expression: <c>pdfa2a</c> first, else <c>pdf</c>, else none.</summary>
    private static EuManifestationMediaType? PdfMediaTypeListed(EuFormexManifestationEnumerationResult enumeration)
    {
        var types = enumeration.ManifestationTypes!.Select(static type => type.PublisherType).ToHashSet(StringComparer.Ordinal);
        if (types.Contains(PdfA2aTypeToken))
        {
            return EuManifestationMediaType.PdfTypePdfa2a;
        }

        return types.Contains(PdfTypeToken) ? EuManifestationMediaType.ApplicationPdf : null;
    }

    /// <summary>
    /// The office answers the work-level PDF request with a 303 to the PDF item of the manifestation it
    /// selected (<c>{manifestation}/DOC_1</c>, observed 2026-09-04); the manifestation is read from
    /// that terminal and must descend from this expression, or the served PDF is another
    /// expression's and binds to nothing here.
    /// </summary>
    private static SourceObjectRef? TryAdmitPdfManifestation(
        EuWemiIdentityBoundary boundary,
        SourceObjectRef expressionRef,
        LanguageScopedExpression expression,
        string terminalUri,
        out string? detail)
    {
        detail = null;
        if (!TryCellarKey(terminalUri, out var terminalKey))
        {
            detail = $"the PDF terminal {terminalUri} is not a Cellar resource";
            return null;
        }

        var slash = terminalKey.IndexOf('/', StringComparison.Ordinal);
        var manifestationKey = slash < 0 ? terminalKey : terminalKey[..slash];
        var manifestationIri = expression.Identity.PublisherExpressionId[..^expressionRef.CanonicalKey.Length] + manifestationKey;
        try
        {
            return boundary.Require(
                Reference(expressionRef.EntityKind.RegistryRef, expressionRef.IdentityProfileRef, EuWemiRole.Manifestation,
                    manifestationIri, manifestationKey, expressionRef),
                EuWemiRole.Manifestation, nameof(terminalUri));
        }
        catch (ArgumentException exception)
        {
            detail = exception.Message;
            return null;
        }
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
        out SourceObjectRef? workRef,
        out SourceObjectRef? expressionRef,
        out SourceObjectRef? manifestationRef,
        out string? detail)
    {
        boundary = null;
        workRef = null;
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
            workRef = work;
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
