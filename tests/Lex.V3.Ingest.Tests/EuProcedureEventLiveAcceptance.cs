using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// E8's missing half: two real procedure dossiers, discovered from the publisher and then asked
/// about through the integrated producer.
/// </summary>
/// <remarks>
/// <para>
/// SKIPPED BY DEFAULT under <see cref="EnableVariable"/>. Authorized by owner decision of
/// 2026-09-12 as one bounded discovery-plus-acceptance operation, executed once under
/// S2-LIVE-415-417 after independent review and green CI.
/// </para>
/// <para>
/// WHY DISCOVERY EXISTS AT ALL. This family is asked inversely - an event names its dossier, never
/// the reverse - so a run must be handed dossier IRIs. Every dossier IRI in this repository's tests
/// is synthetic and no retained evidence anywhere contains this predicate, so a caller-supplied IRI
/// would be a claim with nothing behind it. The owner required the run to discover its own subjects
/// and PROVE the discovery.
/// </para>
/// <para>
/// THE PREDICATE. The authorizing decision first wrote
/// <c>cdm:procedure_event_belongs_to_procedure_dossier</c>; the owner has confirmed that was
/// descriptive wording rather than a vocabulary coordinate, and that the authoritative member is the
/// accepted contract's <see cref="EuProcedureEventVocabulary.PartOfDossierPredicateUri"/>. Discovery
/// and the producer boundary therefore ask the same predicate.
/// </para>
/// <para>
/// A CONTROLLED STOP IS A FAILURE, NOT A QUIET EXIT. Every stop below used to <c>return</c> out of
/// the gated method: the terminal index was written by the <c>finally</c> and the method then
/// returned normally, so MSTest reported green. A refused one-shot operation would have been
/// retained as <c>DiscoveryBootstrapRefused</c> or <c>AcceptanceRefused</c> and reported as a pass -
/// the single worst failure available to a run that gets one attempt. The operation is now a local
/// function whose early returns exit only IT; the retention still runs in a <c>finally</c>, and the
/// judgement that follows is unconditional, so every stop reaches
/// <see cref="DeliveredVerdict"/>'s assertion and fails there with its evidence already on disk.
/// </para>
/// <para>
/// WHAT PROVES THE DISCOVERY. The session retains every nonempty outbound body before sending it,
/// and writes, reopens and byte-compares it. An earlier head claimed the opposite and recorded only
/// a digest, which the reviewer showed was vacuous by replacing that digest with sixty-four zeroes.
/// Both windows now reopen the actual request bytes out of custody by content address and require
/// them to equal, byte for byte, the query this run re-derives. What was asked is proved by the
/// bytes; what the publisher answered is proved by the retained payload.
/// </para>
/// <para>
/// AND THE PROOF SURVIVES A FAULT. Each window is attached to the outcome BEFORE the operations that
/// can throw, and populated in place, so a fault inside the reopen or the parse still retains the
/// asked identity and digest gathered up to that point. Filling a local and assigning it on return
/// meant a throw serialized that window as null while the repair claimed otherwise.
/// </para>
/// <para>
/// TWO WINDOWS, NOT TWO IDENTICAL SENDS. Equality is proved across different windows of one ordered
/// result set - <c>LIMIT 2</c> then <c>LIMIT 3</c>, first two required to agree - which catches an
/// unordered result set handing back different rows. The family two-pass proof is bound to the typed
/// plan families and their cursors, and a two-row bounded discovery has no such partition.
/// </para>
/// <para>
/// NOTHING UNPROVED REACHES THE PUBLISHER, AND "PROVED" MEANS THE PRODUCER'S OWN TEST. A SPARQL
/// <c>"type":"uri"</c> label is the publisher's claim, not proof that the value is an IRI: the
/// reviewer's probe showed <c>{"type":"uri","value":"not an iri"}</c> was admitted. Worse, the
/// producer opens its robots bootstrap BEFORE canonicalizing its batch, so a raw-distinct
/// <c>http</c>/<c>https</c> pair that reduces to one member would have caused the acceptance
/// bootstrap and then thrown. Both discovered values are therefore reduced through
/// <see cref="EuPackRootCanonicalForm.TryCanonicalize"/> - the producer's own boundary - and proved
/// distinct in that canonical form, before the acceptance bootstrap is counted or opened.
/// </para>
/// <para>
/// THE BOUND. The owner fixed 34 charged requests and 36 actual sends. The query channel registers
/// <c>NoRedirect</c> and admits only the request target, so a product request is exactly one send;
/// the EU robots route declares exactly two steps as a closed pre-declared list, so a third hop is
/// inadmissible. Each bootstrap therefore costs one charge and up to two sends. The send ceiling is
/// consequently not enforced by the charge ceiling - at 34 charged a third bootstrap would make 37
/// sends - so bootstraps are bounded at <see cref="BootstrapCeiling"/>; and because a REFUSED
/// bootstrap has already sent, they are counted before they are attempted rather than when a session
/// comes back.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class EuProcedureEventLiveAcceptance
{
    private const string EnableVariable = "LEX_E8_EU_PROCEDURE_LIVE";

    /// <summary>The one verdict that is a pass. Every other outcome fails the judgement.</summary>
    internal const string DeliveredVerdict = "AcceptanceDelivered";

    /// <summary>The owner-fixed charged-request ceiling for the whole operation.</summary>
    private const int SharedWireCeiling = 34;

    /// <summary>The owner-fixed actual-send ceiling for the whole operation.</summary>
    private const int SendCeiling = 36;

    /// <summary>Robots bootstraps: one for discovery, one the producer opens for acceptance.</summary>
    private const int BootstrapCeiling = 2;

    private const string EuQueryUri = "https://publications.europa.eu/webapi/rdf/sparql";
    private const string CdmPrefix = "http://publications.europa.eu/ontology/cdm#";
    private const string HarnessFileName = "EuProcedureEventLiveAcceptance.cs";

    /// <summary>The hosts this operation may contact: the endpoint and its robots redirect target.</summary>
    private static readonly string[] AdmittedHosts = ["publications.europa.eu", "op.europa.eu"];

    private static readonly byte[] ContentTypeRegistryBytes = Encoding.UTF8.GetBytes(
        "{\"schema\":\"e8-eu-discovery-content-type-registry/1\","
        + "\"members\":[\"application/sparql-query\"]}\n");

    private static readonly byte[] QueryRegistryBytes = Encoding.UTF8.GetBytes(
        "{\"schema\":\"e8-eu-discovery-query-registry/1\","
        + "\"members\":[\"eu-procedure-dossier-discovery\"]}\n");

    private static readonly byte[] ParameterProvenanceBytes = Encoding.UTF8.GetBytes(
        "e8-eu-discovery-parameter-provenance/1\n"
        + "parameter=limit\n"
        + "why=the discovery window is bounded by the owner decision, not by the publisher\n");

    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task TwoDiscoveredDossiersAreAnsweredByThePublisher()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                $"Set {EnableVariable}=1 for E8's bounded EU procedure-event discovery and live "
                + "acceptance. Skipped by default so the suite sends no unasked traffic.");
        }

        var checkout = CheckoutRoot();
        var root = Path.Combine(
            checkout, "artifacts", "e8-eu-procedure-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new FileSystemCustodyStore(root);

        var budget = WireRequestBudget.OfWireRequests(SharedWireCeiling);
        var accounting = new Accounting();
        var outcome = new Outcome();
        var startedAt = DateTimeOffset.UtcNow;

        foreach (var bytes in new[] { ContentTypeRegistryBytes, QueryRegistryBytes, ParameterProvenanceBytes })
        {
            await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        }

        var rendererSourceBytes = await File.ReadAllBytesAsync(Path.Combine(
            checkout, "tests", "Lex.V3.Ingest.Tests", HarnessFileName));
        var narrow = DiscoveryRequest(limit: 2, rendererSourceBytes);
        var wide = DiscoveryRequest(limit: 3, rendererSourceBytes);

        // THE OPERATION IS A LOCAL FUNCTION SO ITS STOPS CANNOT SKIP THE JUDGEMENT. An early return
        // here leaves only this function; the finally below still retains, and the judgement after
        // it always runs.
        async Task RunOperationAsync()
        {
            if (!budget.TryReserveAttempt())
            {
                outcome.Verdict = "BudgetExhaustedBeforeDiscovery";
                return;
            }

            accounting.BootstrapsAttempted++;
            var start = await RoutedHttpAcquisitionSession.StartAsync(
                narrow.Request, store, CancellationToken.None);
            accounting.DiscoveryBootstrapEvidencePresent = start.Evidence is not null;

            if (start.Session is null)
            {
                outcome.Verdict = "DiscoveryBootstrapRefused";
                outcome.Refusal = $"{start.Kind} safety={start.LocalSafetyReason} "
                    + $"operational={start.OperationalReason}";
                return;
            }

            accounting.SessionsOpened++;
            using (var session = start.Session)
            {
                var glue = new RepeatedEnumerationDeliveryReopenGlue(store);
                var membership = new Dictionary<string, CustodyMembership>(StringComparer.Ordinal);
                var counted = 0;

                // ATTACHED BEFORE THE OPERATIONS THAT CAN THROW, then populated in place.
                outcome.Narrow = new Window();
                await ObserveWindowAsync(
                    outcome.Narrow, glue, session, store, narrow, membership,
                    () => counted, value => counted = value, budget);
                if (outcome.Narrow.Refusal is not null)
                {
                    outcome.Verdict = "DiscoveryWindowRefused";
                    outcome.Refusal = "narrow: " + outcome.Narrow.Refusal;
                    return;
                }

                outcome.Wide = new Window();
                await ObserveWindowAsync(
                    outcome.Wide, glue, session, store, wide, membership,
                    () => counted, value => counted = value, budget);
                if (outcome.Wide.Refusal is not null)
                {
                    outcome.Verdict = "DiscoveryWindowRefused";
                    outcome.Refusal = "wide: " + outcome.Wide.Refusal;
                    return;
                }
            }

            if (!outcome.Narrow!.RequestBytesReopenedAndEqual
                || !outcome.Wide!.RequestBytesReopenedAndEqual)
            {
                outcome.Verdict = "AskedBytesDidNotReopenEqual";
                return;
            }

            var proof = ProveTwoDistinctDossiers(outcome.Narrow.Dossiers, outcome.Wide.Dossiers);
            outcome.Verdict = proof.Verdict;
            outcome.Refusal = proof.Detail;
            outcome.CanonicalDiscovered = proof.Canonical;
            if (proof.Canonical is null)
            {
                return;
            }

            outcome.Discovered = proof.Raw;

            if (accounting.BootstrapsAttempted >= BootstrapCeiling)
            {
                outcome.Verdict = "BootstrapCeilingReachedBeforeAcceptance";
                return;
            }

            var spentBeforeAcceptance = budget.Spent;
            accounting.BootstrapsAttempted++;
            var production = await new EuProcedureEventProducer(store, TimeProvider.System).RunAsync(
                new EuProcedureEventRunRequest(
                    EuProcedureEventDiscoveryPlan.Create(),
                    outcome.Discovered!,
                    NewUrn(),
                    MachineQueryRendererSource.Open(
                        new SourceArtifactRef(NewUrn(), Sha256(rendererSourceBytes)),
                        rendererSourceBytes),
                    budget),
                EuAcquisitionTestFixture.SourceWitness(),
                CancellationToken.None);
            if (budget.Spent > spentBeforeAcceptance)
            {
                accounting.SessionsOpened++;
            }

            outcome.ProductRequestCount = production.ProductRequestCount;
            outcome.Delivered = production.Delivered;
            outcome.CompletionEvidenceSha256 = production.CompletionEvidenceRef?.Sha256;
            outcome.ObservationCount = production.Observations?.Count;
            outcome.ExcludedEventCount = production.ExcludedEvents?.Count;
            outcome.DossiersAskedAbout = production.DossiersAskedAbout?.ToArray();
            if (!production.Delivered)
            {
                outcome.Verdict = "AcceptanceRefused";
                outcome.Refusal = production.Refusal + " " + production.Detail;
                return;
            }

            // INTERPRETATION IS LAST, AND IT CAN THROW: EventsOf throws on a dossier the delivered
            // result does not carry, which is exactly the mismatch a packet most needs to record.
            foreach (var dossier in outcome.Discovered!)
            {
                var candidate = production.EventsOf(dossier).FirstOrDefault(
                    static observation => observation.ObservedTypeIris.Count > 0
                        && observation.RawDateLexical.Length > 0);
                if (candidate is not null)
                {
                    outcome.PositiveDossier = dossier;
                    outcome.PositiveEventIri = candidate.EventIri;
                    outcome.PositiveRawDate = candidate.RawDateLexical;
                    outcome.PositiveDateDatatypeIri = candidate.DateDatatypeIri;
                    outcome.PositiveTypeIris = [.. candidate.ObservedTypeIris];
                    break;
                }
            }

            outcome.Verdict = DeliveredVerdict;
        }

        try
        {
            await RunOperationAsync();
        }
        catch (Exception error)
        {
            outcome.Verdict = "Faulted";
            outcome.Refusal = error.GetType().Name + ": " + error.Message;
            throw;
        }
        finally
        {
            await RetainAsync(root, store, outcome, accounting, budget, startedAt);
            TestContext?.WriteLine("terminal evidence: " + Path.Combine(root, "terminal-index.json"));
        }

        // ---- JUDGEMENT, UNCONDITIONAL AND ENTIRELY AFTER THE TERMINAL WRITE ------------------
        var offenders = OffendingHosts(root);
        Assert.IsEmpty(
            offenders,
            "this operation may contact the SPARQL endpoint and its robots redirect target and "
            + "nothing else: " + string.Join("; ", offenders.Take(10)));

        // EVERY CONTROLLED STOP FAILS HERE. This is the assertion an early return used to skip.
        Assert.AreEqual(
            DeliveredVerdict, outcome.Verdict,
            $"the operation did not deliver: {outcome.Verdict} {outcome.Refusal}. "
            + $"Evidence retained under {root}.");

        Assert.IsTrue(
            outcome.Narrow!.RequestBytesReopenedAndEqual,
            "the narrow window's request bytes must reopen from custody and equal, byte for byte, "
            + "the exact query this run re-derived.");
        Assert.IsTrue(
            outcome.Wide!.RequestBytesReopenedAndEqual,
            "and so must the wider window's.");

        Assert.IsNotNull(
            outcome.PositiveDossier,
            "the decision requires at least one POSITIVE typed observation carrying its date; the "
            + $"run delivered {outcome.ObservationCount ?? 0} observation(s) and "
            + $"{outcome.ExcludedEventCount ?? 0} excluded. Evidence retained under {root}.");
        Assert.IsNotEmpty(outcome.PositiveEventIri!, "a positive observation names its event node.");
        Assert.IsNotEmpty(
            outcome.PositiveDateDatatypeIri!,
            "the date arrives with the datatype the publisher gave it, never widened or guessed.");
        Assert.IsNotNull(
            outcome.CompletionEvidenceSha256,
            "provenance: a delivered run cites the acquisition run that produced it.");
        Assert.IsNotNull(outcome.DossiersAskedAbout);
        foreach (var dossier in outcome.Discovered!)
        {
            Assert.Contains(
                dossier, outcome.DossiersAskedAbout!,
                "the producer must have asked about exactly the discovered dossiers.");
        }

        Assert.IsLessThanOrEqualTo(
            BootstrapCeiling, accounting.BootstrapsAttempted,
            $"bootstraps are bounded at {BootstrapCeiling} because the send ceiling depends on it.");
        Assert.IsLessThanOrEqualTo(
            SharedWireCeiling, budget.Spent,
            $"charged requests are bounded at {SharedWireCeiling}.");
        Assert.IsLessThanOrEqualTo(
            SendCeiling, budget.Spent + accounting.BootstrapsAttempted,
            "actual sends are charged plus one robots redirect hop per bootstrap ATTEMPTED, "
            + $"bounded at {SendCeiling}.");
    }

    /// <summary>
    /// Two discovered values proved to be two distinct dossiers on the producer's own terms.
    /// </summary>
    /// <remarks>
    /// A PURE FUNCTION SO IT CAN BE EXERCISED OFFLINE, and it applies the producer's boundary rather
    /// than an approximation of it: <see cref="EuPackRootCanonicalForm.TryCanonicalize"/> is what
    /// <c>CanonicalizeBatch</c> itself uses, and the producer runs it only AFTER opening its robots
    /// bootstrap. A SPARQL <c>"type":"uri"</c> label is the publisher's claim, so a non-IRI or a
    /// raw-distinct pair reducing to one member would otherwise have caused that bootstrap and then
    /// thrown.
    /// </remarks>
    internal static (string Verdict, string? Detail, string[]? Raw, string[]? Canonical)
        ProveTwoDistinctDossiers(IReadOnlyList<string> narrow, IReadOnlyList<string> wide)
    {
        ArgumentNullException.ThrowIfNull(narrow);
        ArgumentNullException.ThrowIfNull(wide);

        if (narrow.Count != 2)
        {
            return ("DiscoveryDidNotYieldTwoDossiers",
                $"the narrow window returned {narrow.Count} URI term(s).", null, null);
        }

        if (string.Equals(narrow[0], narrow[1], StringComparison.Ordinal))
        {
            return ("DiscoveryReturnedADuplicatePair", narrow[0], null, null);
        }

        if (!WindowsAgree(narrow, wide))
        {
            return ("DiscoveryWindowsDisagree",
                $"narrow=[{string.Join(", ", narrow)}] wide=[{string.Join(", ", wide)}]", null, null);
        }

        var canonical = new string[2];
        for (var index = 0; index < 2; index++)
        {
            var reduced = EuPackRootCanonicalForm.TryCanonicalize(narrow[index], out var refusal);
            if (reduced is null)
            {
                return ("DiscoveryValueIsNotCanonical",
                    $"'{narrow[index]}' does not reduce: {refusal}", null, null);
            }

            canonical[index] = reduced;
        }

        if (string.Equals(canonical[0], canonical[1], StringComparison.Ordinal))
        {
            return ("DiscoveryReturnedACanonicalDuplicate",
                $"both reduce to '{canonical[0]}'", null, null);
        }

        return (DeliveredVerdict, null, [narrow[0], narrow[1]], canonical);
    }

    /// <summary>
    /// One discovery window: sent, its request bytes reopened and compared, its answer parsed.
    /// </summary>
    /// <remarks>
    /// The window is supplied by the caller and populated IN PLACE, so evidence gathered before a
    /// throw is still attached to the outcome when the terminal index is written.
    /// </remarks>
    private static async Task ObserveWindowAsync(
        Window window,
        RepeatedEnumerationDeliveryReopenGlue glue,
        RoutedHttpAcquisitionSession session,
        ICustodyStore store,
        (BoundMachineRequest Request, byte[] Body) bound,
        Dictionary<string, CustodyMembership> membership,
        Func<int> currentCount,
        Action<int> setCount,
        WireRequestBudget budget)
    {
        var observed = await glue.ObserveAsync(
            session, bound.Request, "application/sparql-results+json", membership,
            currentCount, setCount, CancellationToken.None, budget);
        if (observed.Transport is not { } transport)
        {
            window.Refusal = observed.Failure?.Kind.ToString() ?? "no transport and no failure";
            return;
        }

        window.AskedBodySha256 = transport.LogicalRequest.Body.Sha256;
        window.AskedBodyLength = (long)transport.LogicalRequest.Body.Length;
        window.PayloadSha256 = transport.DurableWriteReceipt.Reference.ContentSha256;

        var reopened = await store.ReadByDigestAsync(
            window.AskedBodySha256, CancellationToken.None);
        window.RequestBytesReopenedAndEqual =
            window.AskedBodyLength == bound.Body.Length
            && reopened.Span.SequenceEqual(bound.Body);

        window.Dossiers = ParseDossiers(transport.RetainedPayloadBytes.Span);
    }

    /// <summary>One bounded discovery window over the contract's dossier predicate.</summary>
    private static (BoundMachineRequest Request, byte[] Body) DiscoveryRequest(
        int limit, byte[] rendererSourceBytes)
    {
        var body = Encoding.UTF8.GetBytes(
            "PREFIX cdm: <" + CdmPrefix + ">\n"
            + "SELECT DISTINCT ?dossier WHERE {\n"
            + "  ?event <" + EuProcedureEventVocabulary.PartOfDossierPredicateUri + "> ?dossier .\n"
            + "}\nORDER BY STR(?dossier)\nLIMIT "
            + limit.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");

        var queryRegistry = new SourceArtifactRef(NewUrn(), Sha256(QueryRegistryBytes));
        var contentTypeRegistry = new SourceArtifactRef(NewUrn(), Sha256(ContentTypeRegistryBytes));
        var parameterProvenance = new SourceArtifactRef(NewUrn(), Sha256(ParameterProvenanceBytes));
        var queryFamily = new SourceRegistryMemberRef(queryRegistry, "eu-procedure-dossier-discovery");
        var cardinality = new MachineResponseCardinality(
            MachineResponseCardinalityKind.OpaqueBody, null, null, null);

        var input = MachineQueryInputArtifact.Create(
            NewUrn(),
            queryFamily,
            "eu-procedure-dossier-discovery",
            cardinality,
            [
                new MachineQueryParameter(
                    "limit", MachineQueryParameterKind.BoundedInteger, limit, null, parameterProvenance),
            ]);

        var rendererProfileBytes = Encoding.UTF8.GetBytes(
            "e8-eu-discovery-renderer-profile/1\nprofile=bounded-ordered-distinct-dossier\n");
        var rendererProfileRef = new SourceArtifactRef(NewUrn(), Sha256(rendererProfileBytes));
        var rendererSourceRef = new SourceArtifactRef(NewUrn(), Sha256(rendererSourceBytes));

        var targetBytes = Encoding.ASCII.GetBytes(new Uri(EuQueryUri).PathAndQuery);
        var plan = new MachineQueryPlan(
            MachineQueryPlan.SchemaId,
            queryFamily,
            rendererProfileRef,
            rendererSourceRef,
            HttpRequestMethod.Post,
            EuQueryUri,
            targetBytes.LongLength,
            Sha256(targetBytes),
            cardinality,
            new SourceRegistryMemberRef(contentTypeRegistry, "application/sparql-query"),
            MachineQueryCharset.Utf8,
            MachineQueryInputMode.RendererInputs,
            input.ArtifactRef,
            input.PartitionBinding,
            body.LongLength,
            Sha256(body));

        var planRef = MachineQueryPlanIdentity.Create(NewUrn(), plan);
        var request = MachineQueryBinder.BindForSend(
            plan, planRef, input,
            new DiscoveryRenderer(
                rendererProfileRef, rendererSourceRef, rendererProfileBytes, rendererSourceBytes,
                EuQueryUri, body));
        return (request, body);
    }

    /// <summary>Whether the wider window agrees with the narrow one on the rows they share.</summary>
    /// <remarks>
    /// Prefix equality in order: a wider window of a stably ordered result set begins with the
    /// narrower one. A wide window SHORTER than the narrow one is disagreement, and two empty
    /// windows do not agree - prefix equality over empty sequences is trivially true, which would
    /// let a run that discovered nothing twice satisfy its own check.
    /// </remarks>
    internal static bool WindowsAgree(IReadOnlyList<string> narrow, IReadOnlyList<string> wide)
    {
        ArgumentNullException.ThrowIfNull(narrow);
        ArgumentNullException.ThrowIfNull(wide);
        if (narrow.Count == 0 || wide.Count < narrow.Count)
        {
            return false;
        }

        for (var index = 0; index < narrow.Count; index++)
        {
            if (!string.Equals(narrow[index], wide[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Every <c>?dossier</c> URI-labelled term the publisher returned, in delivery order.
    /// </summary>
    /// <remarks>
    /// The term label is checked here because a literal or blank node must not even be considered;
    /// but a label is the publisher's claim and not proof, so whether a value is really an IRI is
    /// settled by <see cref="ProveTwoDistinctDossiers"/> against the producer's own canonical form.
    /// </remarks>
    internal static IReadOnlyList<string> ParseDossiers(ReadOnlySpan<byte> payload)
    {
        using var document = JsonDocument.Parse(payload.ToArray());
        var rows = new List<string>();
        if (!document.RootElement.TryGetProperty("results", out var results)
            || !results.TryGetProperty("bindings", out var bindings)
            || bindings.ValueKind != JsonValueKind.Array)
        {
            return rows;
        }

        foreach (var binding in bindings.EnumerateArray())
        {
            if (binding.TryGetProperty("dossier", out var dossier)
                && dossier.TryGetProperty("type", out var kind)
                && string.Equals(kind.GetString(), "uri", StringComparison.Ordinal)
                && dossier.TryGetProperty("value", out var value)
                && value.GetString() is { Length: > 0 } iri)
            {
                rows.Add(iri);
            }
        }

        return rows;
    }

    /// <summary>One terminal index, written in a finally so no outcome escapes without it.</summary>
    private static async Task RetainAsync(
        string root,
        FileSystemCustodyStore store,
        Outcome outcome,
        Accounting accounting,
        WireRequestBudget budget,
        DateTimeOffset startedAt)
    {
        var index = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                purpose = "E8 EU procedure-event bounded discovery and live acceptance: terminal "
                    + "accounting, retained on every outcome including safety stops and faults.",
                verdict = outcome.Verdict,
                verdictIsPass = string.Equals(outcome.Verdict, DeliveredVerdict, StringComparison.Ordinal),
                refusal = outcome.Refusal,
                observedFromUtc = startedAt.UtcDateTime.ToString("O"),
                observedToUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                predicate = EuProcedureEventVocabulary.PartOfDossierPredicateUri,
                predicateNote = "The authorizing decision first wrote "
                    + "cdm:procedure_event_belongs_to_procedure_dossier; the owner confirmed that "
                    + "was descriptive wording and that the authoritative member is the accepted "
                    + "contract's cdm:event_legal_part_of_dossier, which is what this run asked.",
                narrowWindow = Describe(outcome.Narrow),
                wideWindow = Describe(outcome.Wide),
                discovered = outcome.Discovered,
                canonicalDiscovered = outcome.CanonicalDiscovered,
                dossiersAskedAbout = outcome.DossiersAskedAbout,
                positive = outcome.PositiveDossier is null ? null : new
                {
                    dossier = outcome.PositiveDossier,
                    eventIri = outcome.PositiveEventIri,
                    rawDateLexical = outcome.PositiveRawDate,
                    dateDatatypeIri = outcome.PositiveDateDatatypeIri,
                    typeIris = outcome.PositiveTypeIris,
                },
                chargedRequests = budget.Spent,
                chargedCeiling = budget.Limit,
                exhausted = budget.Exhausted,
                bootstrapsAttempted = accounting.BootstrapsAttempted,
                bootstrapCeiling = BootstrapCeiling,
                sessionsOpened = accounting.SessionsOpened,
                discoveryBootstrapEvidencePresent = accounting.DiscoveryBootstrapEvidencePresent,
                enforcedSendUpperBound = budget.Spent + accounting.BootstrapsAttempted,
                sendCeiling = SendCeiling,
                sendDerivation = "One send per charged product request, plus up to one uncharged "
                    + "robots redirect hop per bootstrap ATTEMPTED - counted before the attempt, "
                    + "because a refused bootstrap has already sent.",
                productRequestCount = outcome.ProductRequestCount,
                delivered = outcome.Delivered,
                observationCount = outcome.ObservationCount,
                excludedEventCount = outcome.ExcludedEventCount,
                completionEvidenceSha256 = outcome.CompletionEvidenceSha256,
                root,
            },
            new JsonSerializerOptions { WriteIndented = true });

        await store.CreateAsync(index, CustodyClass.NightlyFloor90d, CancellationToken.None);
        await File.WriteAllBytesAsync(Path.Combine(root, "terminal-index.json"), index);
    }

    private static object? Describe(Window? window) => window is null ? null : new
    {
        askedBodySha256 = window.AskedBodySha256,
        askedBodyLength = window.AskedBodyLength,
        requestBytesReopenedAndEqual = window.RequestBytesReopenedAndEqual,
        payloadSha256 = window.PayloadSha256,
        dossiers = window.Dossiers,
        refusal = window.Refusal,
    };

    /// <summary>Any host in the retained evidence that this operation was not permitted to contact.</summary>
    private static IReadOnlyList<string> OffendingHosts(string root)
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (var token in new[] { "https://", "http://" })
            {
                var index = text.IndexOf(token, StringComparison.Ordinal);
                while (index >= 0)
                {
                    var end = text.IndexOfAny(['/', '"', '\n', '\t', ' '], index + token.Length);
                    var host = end < 0
                        ? text[(index + token.Length)..]
                        : text[(index + token.Length)..end];
                    if (host.Length > 0
                        && !AdmittedHosts.Contains(host, StringComparer.OrdinalIgnoreCase)
                        && !host.StartsWith("www.w3.org", StringComparison.OrdinalIgnoreCase)
                        && !host.StartsWith("data.europa.eu", StringComparison.OrdinalIgnoreCase)
                        && !host.StartsWith("lex.invalid", StringComparison.OrdinalIgnoreCase)
                        && !offenders.Contains(host, StringComparer.OrdinalIgnoreCase))
                    {
                        offenders.Add(host);
                    }

                    index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal);
                }
            }
        }

        return offenders;
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string NewUrn() => "urn:uuid:" + Guid.NewGuid().ToString("D");

    private static string CheckoutRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "the checkout root was not found above the test binaries.");
        return directory.FullName;
    }

    /// <summary>What this run spent, counted rather than inferred from success.</summary>
    private sealed class Accounting
    {
        internal int BootstrapsAttempted { get; set; }

        internal int SessionsOpened { get; set; }

        internal bool DiscoveryBootstrapEvidencePresent { get; set; }
    }

    /// <summary>One discovery window's retained identities and its answer.</summary>
    internal sealed class Window
    {
        internal string? AskedBodySha256 { get; set; }

        internal long AskedBodyLength { get; set; }

        internal bool RequestBytesReopenedAndEqual { get; set; }

        internal string? PayloadSha256 { get; set; }

        internal IReadOnlyList<string> Dossiers { get; set; } = [];

        internal string? Refusal { get; set; }
    }

    /// <summary>Everything the terminal index reports, filled in as the run proceeds.</summary>
    private sealed class Outcome
    {
        internal string Verdict { get; set; } = "NotStarted";

        internal string? Refusal { get; set; }

        internal Window? Narrow { get; set; }

        internal Window? Wide { get; set; }

        internal string[]? Discovered { get; set; }

        internal string[]? CanonicalDiscovered { get; set; }

        internal string[]? DossiersAskedAbout { get; set; }

        internal int? ProductRequestCount { get; set; }

        internal bool? Delivered { get; set; }

        internal int? ObservationCount { get; set; }

        internal int? ExcludedEventCount { get; set; }

        internal string? CompletionEvidenceSha256 { get; set; }

        internal string? PositiveDossier { get; set; }

        internal string? PositiveEventIri { get; set; }

        internal string? PositiveRawDate { get; set; }

        internal string? PositiveDateDatatypeIri { get; set; }

        internal string[]? PositiveTypeIris { get; set; }
    }

    /// <summary>
    /// The renderer for this run's discovery question.
    /// </summary>
    /// <remarks>
    /// Its renderer SOURCE is this harness's own file bytes, because this file implements it. An
    /// earlier head pointed the source reference at the frozen procedure-event plan, which does not
    /// contain this renderer.
    /// </remarks>
    private sealed class DiscoveryRenderer(
        SourceArtifactRef rendererProfileRef,
        SourceArtifactRef rendererSourceRef,
        byte[] rendererProfileBytes,
        byte[] rendererSourceBytes,
        string target,
        byte[] body) : IMachineQueryRenderer
    {
        public SourceArtifactRef RendererProfileRef { get; } = rendererProfileRef;

        public SourceArtifactRef RendererSourceRef { get; } = rendererSourceRef;

        // Statement bodies deliberately: a conditional expression over a null array hands back a
        // present, empty ReadOnlyMemory, which reads as producing zero bytes rather than declining.
        public ReadOnlyMemory<byte>? CopyRendererProfileBytes()
        {
            return rendererProfileBytes;
        }

        public ReadOnlyMemory<byte>? CopyRendererSourceBytes()
        {
            return rendererSourceBytes;
        }

        public MachineQueryRenderOutput Render(
            MachineQueryPlan plan, MachineQueryInputArtifact orderedParameterSet) =>
            new(target, body);
    }
}
