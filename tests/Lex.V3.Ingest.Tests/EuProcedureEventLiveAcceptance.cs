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
/// is synthetic, the plan's own orientation probe retained none, and no retained evidence anywhere
/// contains this predicate. A caller-supplied IRI would be a claim with nothing behind it, so the
/// owner required the run to discover its own subjects and PROVE the discovery.
/// </para>
/// <para>
/// THE PREDICATE. The authorizing decision first wrote
/// <c>cdm:procedure_event_belongs_to_procedure_dossier</c>; the owner has since confirmed that was
/// descriptive wording rather than a vocabulary coordinate, and that the authoritative member is the
/// accepted contract's <see cref="EuProcedureEventVocabulary.PartOfDossierPredicateUri"/>,
/// <c>cdm:event_legal_part_of_dossier</c>. Discovery and the producer boundary therefore ask the
/// same predicate, which is what makes the two halves of this operation coherent.
/// </para>
/// <para>
/// WHAT PROVES THE DISCOVERY, CORRECTED. An earlier head of this harness asserted that the request
/// body is never retained, and proved only that a digest had been recorded. That was wrong: the
/// session retains every nonempty outbound body before sending it, and writes, reopens and
/// byte-compares it. The reviewer showed how weak the earlier claim was by replacing the recorded
/// digest with sixty-four zeroes - the candidate built and every guard still passed. So the proof is
/// now the strong form that was available all along: for BOTH windows this run reopens the actual
/// request bytes out of custody by their content address and requires them to equal, byte for byte,
/// the exact query it re-derives. What was asked is proved by the bytes themselves; what the
/// publisher answered is proved by the retained response payload, read back through custody and
/// never from a constant in this file.
/// </para>
/// <para>
/// TWO WINDOWS, NOT TWO IDENTICAL SENDS. The family two-pass enumeration proof
/// (<c>EnumerationDeliveryComparison</c>) is bound to the typed plan families and their cursors, and
/// a two-row bounded discovery has no such partition. So equality here is proved across DIFFERENT
/// WINDOWS of the same ordered result set - <c>LIMIT 2</c> and then <c>LIMIT 3</c>, with the first
/// two required to agree. That catches the real risk, an unordered result set handing back different
/// rows on a second look, which two identical sends would not.
/// </para>
/// <para>
/// NOTHING UNPROVED REACHES THE PUBLISHER TWICE. Strict URI-term decoding, distinctness and window
/// agreement are all established BEFORE the acceptance session is opened. An earlier head invoked
/// the producer as soon as two nonempty strings had been parsed and checked their kind and
/// distinctness afterwards, so a literal, a duplicate pair or an unstable second window could cause
/// traffic - or throw inside the producer's canonicalization - before this run had proved it had two
/// dossiers at all.
/// </para>
/// <para>
/// THE BOUND, AND THE TWO PARTS OF IT THE BUDGET CANNOT ENFORCE. The owner fixed 34 charged
/// requests and 36 actual sends. Verified from source: the query channel registers
/// <c>NoRedirect</c>, admitting only the request target, so a product request is exactly one send;
/// and the EU robots route declares exactly two steps as a closed pre-declared URI list, so a third
/// hop is inadmissible. Each bootstrap therefore costs one charge and up to two sends.
/// </para>
/// <para>
/// Two consequences, each needing its own guard. First, the send ceiling is not enforced by the
/// charge ceiling: at 34 charged, a third bootstrap would put sends at 37 while the charged bound
/// still read as satisfied - so bootstraps are bounded at <see cref="BootstrapCeiling"/>. Second, a
/// REFUSED bootstrap still sends: <c>StartAsync</c> can return no session after the two-hop robots
/// exchange has already happened, for publisher denial, unsafe policy, server failure or a
/// source-profile refusal. An earlier head counted only sessions that opened, so a refusal packet
/// understated the traffic it had caused. Bootstraps are therefore counted BEFORE they are
/// attempted, the enforced upper bound is charged plus bootstraps attempted, and the observed
/// session count is reported separately rather than standing in for it.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class EuProcedureEventLiveAcceptance
{
    private const string EnableVariable = "LEX_E8_EU_PROCEDURE_LIVE";

    /// <summary>The owner-fixed charged-request ceiling for the whole operation.</summary>
    private const int SharedWireCeiling = 34;

    /// <summary>The owner-fixed actual-send ceiling for the whole operation.</summary>
    private const int SendCeiling = 36;

    /// <summary>
    /// Robots bootstraps: one for discovery, one the producer opens for acceptance.
    /// </summary>
    /// <remarks>
    /// Derived, not chosen, and counted rather than inferred from success. Each bootstrap costs one
    /// charge and up to two sends on this origin, so the send ceiling is only binding while the
    /// bootstrap count is - and a bootstrap that was refused has already sent.
    /// </remarks>
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

        // ONE BUDGET FOR THE WHOLE OPERATION, discovery and acceptance alike.
        var budget = WireRequestBudget.OfWireRequests(SharedWireCeiling);
        var accounting = new Accounting();
        var outcome = new Outcome();
        var startedAt = DateTimeOffset.UtcNow;

        // The three artifacts a governed send reopens by reference and no renderer produces. Seeded
        // before the first send so the reopen path finds them in this run's own store.
        foreach (var bytes in new[] { ContentTypeRegistryBytes, QueryRegistryBytes, ParameterProvenanceBytes })
        {
            await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        }

        // THE RENDERER'S SOURCE IS THE FILE THAT IMPLEMENTS IT. An earlier head attributed this
        // bespoke renderer to EuProcedureEventDiscoveryPlan.cs, which does not contain it, so the
        // retained plan named a source that could not account for the query actually sent.
        var rendererSourceBytes = await File.ReadAllBytesAsync(Path.Combine(
            checkout, "tests", "Lex.V3.Ingest.Tests", HarnessFileName));

        var narrow = DiscoveryRequest(limit: 2, rendererSourceBytes);
        var wide = DiscoveryRequest(limit: 3, rendererSourceBytes);

        try
        {
            if (!budget.TryReserveAttempt())
            {
                outcome.Verdict = "BudgetExhaustedBeforeDiscovery";
                return;
            }

            // COUNTED BEFORE IT IS ATTEMPTED. The hops happen whether or not a session results.
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

                outcome.Narrow = await ObserveWindowAsync(
                    glue, session, store, narrow, membership,
                    () => counted, value => counted = value, budget);
                if (outcome.Narrow.Refusal is not null)
                {
                    outcome.Verdict = "DiscoveryWindowRefused";
                    outcome.Refusal = "narrow: " + outcome.Narrow.Refusal;
                    return;
                }

                outcome.Wide = await ObserveWindowAsync(
                    glue, session, store, wide, membership,
                    () => counted, value => counted = value, budget);
                if (outcome.Wide.Refusal is not null)
                {
                    outcome.Verdict = "DiscoveryWindowRefused";
                    outcome.Refusal = "wide: " + outcome.Wide.Refusal;
                    return;
                }
            }

            // ---- THE DISCOVERY IS PROVED BEFORE ANYTHING ELSE IS ASKED ------------------------
            var narrowIris = outcome.Narrow!.Dossiers;
            var wideIris = outcome.Wide!.Dossiers;

            if (!outcome.Narrow.RequestBytesReopenedAndEqual
                || !outcome.Wide.RequestBytesReopenedAndEqual)
            {
                outcome.Verdict = "AskedBytesDidNotReopenEqual";
                return;
            }

            if (narrowIris.Count != 2)
            {
                outcome.Verdict = "DiscoveryDidNotYieldTwoDossiers";
                outcome.Refusal = $"the narrow window returned {narrowIris.Count} URI term(s).";
                return;
            }

            if (string.Equals(narrowIris[0], narrowIris[1], StringComparison.Ordinal))
            {
                outcome.Verdict = "DiscoveryReturnedADuplicatePair";
                outcome.Refusal = narrowIris[0];
                return;
            }

            if (!WindowsAgree(narrowIris, wideIris))
            {
                outcome.Verdict = "DiscoveryWindowsDisagree";
                outcome.Refusal =
                    $"narrow=[{string.Join(", ", narrowIris)}] wide=[{string.Join(", ", wideIris)}]";
                return;
            }

            outcome.Discovered = [narrowIris[0], narrowIris[1]];

            // ---- ACCEPTANCE -------------------------------------------------------------------
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
                    outcome.Discovered,
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
            outcome.Verdict = production.Delivered ? "AcceptanceDelivered" : "AcceptanceRefused";
            if (!production.Delivered)
            {
                outcome.Refusal = production.Refusal + " " + production.Detail;
                return;
            }

            // INTERPRETATION IS LAST, AND IT CAN THROW. EventsOf throws when the delivered result
            // does not carry a dossier this run asked about, which is exactly the material mismatch
            // a terminal packet most needs to record. An earlier head interpreted before retaining
            // and left no index at all on that path; the finally below now covers it.
            foreach (var dossier in outcome.Discovered)
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
        }
        catch (Exception error)
        {
            // A THROW IS AN OUTCOME, AND ITS COST STILL HAS TO BE REPORTED.
            outcome.Verdict = "Faulted";
            outcome.Refusal = error.GetType().Name + ": " + error.Message;
            throw;
        }
        finally
        {
            await RetainAsync(root, store, outcome, accounting, budget, startedAt);
            TestContext?.WriteLine("terminal evidence: " + Path.Combine(root, "terminal-index.json"));
        }

        // ---- JUDGEMENT, ENTIRELY AFTER THE TERMINAL WRITE ------------------------------------
        var offenders = OffendingHosts(root);
        Assert.IsEmpty(
            offenders,
            "this operation may contact the SPARQL endpoint and its robots redirect target and "
            + "nothing else: " + string.Join("; ", offenders.Take(10)));

        Assert.AreEqual(
            "AcceptanceDelivered", outcome.Verdict,
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

        // ---- THE BOUNDS ----------------------------------------------------------------------
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
    /// One discovery window: sent, its request bytes reopened and compared, its answer parsed.
    /// </summary>
    /// <remarks>
    /// THE REQUEST BYTES ARE REOPENED, NOT TRUSTED. The session retains every nonempty outbound
    /// body before sending it, so the exact question is recoverable by content address. Recording
    /// its digest alone proves nothing - a recorded digest of sixty-four zeroes passed every guard
    /// an earlier head had.
    /// </remarks>
    private static async Task<Window> ObserveWindowAsync(
        RepeatedEnumerationDeliveryReopenGlue glue,
        RoutedHttpAcquisitionSession session,
        ICustodyStore store,
        (BoundMachineRequest Request, byte[] Body) bound,
        Dictionary<string, CustodyMembership> membership,
        Func<int> currentCount,
        Action<int> setCount,
        WireRequestBudget budget)
    {
        var window = new Window();
        var observed = await glue.ObserveAsync(
            session, bound.Request, "application/sparql-results+json", membership,
            currentCount, setCount, CancellationToken.None, budget);
        if (observed.Transport is not { } transport)
        {
            window.Refusal = observed.Failure?.Kind.ToString() ?? "no transport and no failure";
            return window;
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
        return window;
    }

    /// <summary>
    /// One bounded discovery window: the distinct dossiers some event declares itself part of.
    /// </summary>
    /// <remarks>
    /// ORDERED, so two windows of the same result set are comparable at all, and LIMITED, so the
    /// question is bounded by this harness rather than by what the publisher happens to hold. The
    /// predicate is the one the frozen contract declares, so discovery and acceptance ask the same
    /// thing.
    /// </remarks>
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

    /// <summary>
    /// Whether the wider window agrees with the narrow one on the rows they share.
    /// </summary>
    /// <remarks>
    /// LIFTED OUT OF THE GATED BODY SO IT CAN BE EXERCISED. Inline, this comparison was the whole
    /// two-window equality claim and nothing offline ever ran it - the instrument stood unchecked
    /// behind a guard that only proved it was wired up. Agreement is prefix equality in order: a
    /// wider window of a stably ordered result set begins with the narrower one. A wide window
    /// SHORTER than the narrow one is disagreement, not a pass by vacuity.
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
    /// Every <c>?dossier</c> URI TERM the publisher returned, in the order it returned them.
    /// </summary>
    /// <remarks>
    /// THE TERM TYPE IS PART OF THE ANSWER. An earlier head accepted any nonempty value, so a
    /// literal or a blank node would have been handed to the producer as though it were a dossier
    /// IRI - and the producer would then have honestly reported finding no events for it. Only
    /// <c>"type":"uri"</c> becomes a subject.
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

    /// <summary>
    /// One terminal index, written in a <c>finally</c> so no outcome can escape without it.
    /// </summary>
    /// <remarks>
    /// Mirrors the integrated draft sweep and then goes further, because the reviewer showed that
    /// "every outcome" was not true of an earlier head: interpretation that throws, malformed JSON
    /// and a dossier mismatch all left no index. This runs in a <c>finally</c>, so a fault is
    /// reported rather than silently having cost traffic, and it distinguishes the ENFORCED send
    /// upper bound from the observed session count.
    /// </remarks>
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
                    + "because a refused bootstrap has already sent. The query channel admits no "
                    + "product redirect and the robots route declares exactly two steps, so this "
                    + "is an upper bound rather than an estimate.",
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
    private sealed class Window
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
    /// Its renderer SOURCE is this harness's own file bytes, because this file is what implements
    /// it. An earlier head pointed the source reference at the frozen procedure-event plan, which
    /// does not contain this renderer, so the retained plan attributed the query to a file that
    /// could not account for it.
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
