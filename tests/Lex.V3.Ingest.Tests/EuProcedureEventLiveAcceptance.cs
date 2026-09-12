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
/// THE PREDICATE, AND A CORRECTION TO THE DECISION THAT AUTHORIZED THIS. The decision names
/// <c>cdm:procedure_event_belongs_to_procedure_dossier</c>. That string occurs exactly once in this
/// repository and it is prose inside a doc comment on
/// <c>EuRepeatedEnumerationExecutor</c>; it is not an identifier any code uses. The predicate the
/// frozen contract asks is <see cref="EuProcedureEventVocabulary.PartOfDossierPredicateUri"/>,
/// <c>cdm:event_legal_part_of_dossier</c>, which the contract records from the authority's own
/// description. Discovery MUST ask the same predicate the producer asks, or it hands the producer
/// dossiers it cannot find events for and the one authorized run comes back empty for a reason that
/// has nothing to do with the publisher. This is reported on #417 rather than resolved silently, and
/// it is not an inference from a similar-looking identifier: it is the identifier the contract
/// declares.
/// </para>
/// <para>
/// WHAT PROVES THE DISCOVERY. The request body is never retained as a custody artifact - the session
/// records only <c>body={length}\t{sha256}</c> in the request policy - so the proof is digest-bound
/// rather than a text read-back, which is stronger because it binds the exact bytes: this harness
/// re-derives the query bytes and shows their SHA-256 equals the digest the retained logical request
/// carries, and the two dossier IRIs are parsed out of the RETAINED RESPONSE PAYLOAD read back
/// through custody, never from a constant in this file. What was asked is proved by digest; what the
/// publisher answered is proved by the retained payload.
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
/// THE BOUND, AND THE PART OF IT THE BUDGET CANNOT ENFORCE. The owner fixed 34 charged requests and
/// 36 actual sends. Verified from source: the query channel registers <c>NoRedirect</c>, admitting
/// only the request target, so a product request is exactly one send; and the EU robots route
/// declares exactly two steps as a closed pre-declared URI list, so a third hop is inadmissible.
/// Therefore <c>sends = charged + sessionsOpened</c> exactly. The 36-send ceiling is consequently
/// NOT enforced by the 34-charge ceiling alone: at 34 charged, a third session would put sends at 37
/// while the charged bound still read as satisfied. This harness therefore bounds sessions at
/// <see cref="SessionCeiling"/> as its own guard, counts them by observation, and reports charged,
/// sessions and derived sends separately.
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
    /// Two sessions: one for discovery, one the producer opens for acceptance.
    /// </summary>
    /// <remarks>
    /// Derived, not chosen. Sends exceed charges by exactly one per session on this origin, so the
    /// send ceiling is only binding while the session count is. A third session would satisfy the
    /// charged ceiling and breach the send ceiling at the same time.
    /// </remarks>
    private const int SessionCeiling = 2;

    private const string EuQueryUri = "https://publications.europa.eu/webapi/rdf/sparql";
    private const string CdmPrefix = "http://publications.europa.eu/ontology/cdm#";

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
        var sessionsOpened = 0;
        var startedAt = DateTimeOffset.UtcNow;

        // The three artifacts a governed send reopens by reference and no renderer produces. Seeded
        // before the first send so the reopen path finds them in this run's own store.
        foreach (var bytes in new[] { ContentTypeRegistryBytes, QueryRegistryBytes, ParameterProvenanceBytes })
        {
            await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        }

        var rendererSourceBytes = await File.ReadAllBytesAsync(Path.Combine(
            checkout, "src/Lex.V3.Contracts/Source/Europe/EuProcedureEventDiscoveryPlan.cs"));

        // ---- DISCOVERY ------------------------------------------------------------------------
        var narrow = DiscoveryRequest(limit: 2, rendererSourceBytes);
        var wide = DiscoveryRequest(limit: 3, rendererSourceBytes);

        // The robots plan item, reserved before the session exists: two sends, charged once.
        if (!budget.TryReserveAttempt())
        {
            await RetainAsync(
                root, store, "BudgetExhaustedBeforeDiscovery", null, budget, sessionsOpened,
                null, null, startedAt);
            Assert.Fail("the budget was spent before discovery opened a session.");
        }

        var spentBeforeDiscovery = budget.Spent;
        var start = await RoutedHttpAcquisitionSession.StartAsync(
            narrow.Request, store, CancellationToken.None);
        if (budget.Spent > spentBeforeDiscovery || start.Session is not null)
        {
            sessionsOpened++;
        }

        if (start.Session is null)
        {
            await RetainAsync(
                root, store, "DiscoverySessionRefused",
                $"{start.Kind} safety={start.LocalSafetyReason} operational={start.OperationalReason}",
                budget, sessionsOpened, null, null, startedAt);
            Assert.Fail(
                $"the governed session did not start: {start.Kind}. Evidence retained under {root}.");
        }

        IReadOnlyList<string> narrowDossiers;
        IReadOnlyList<string> wideDossiers;
        string? narrowBodySha;
        using (var session = start.Session)
        {
            var glue = new RepeatedEnumerationDeliveryReopenGlue(store);
            var membership = new Dictionary<string, CustodyMembership>(StringComparer.Ordinal);
            var counted = 0;

            var narrowOutcome = await glue.ObserveAsync(
                session, narrow.Request, "application/sparql-results+json", membership,
                () => counted, value => counted = value, CancellationToken.None, budget);
            if (narrowOutcome.Transport is not { } narrowTransport)
            {
                await RetainAsync(
                    root, store, "DiscoveryRefused", narrowOutcome.Failure?.Kind.ToString(),
                    budget, sessionsOpened, null, null, startedAt);
                Assert.Fail(
                    $"the narrow discovery window was refused: {narrowOutcome.Failure?.Kind}. "
                    + $"Evidence retained under {root}.");
                return;
            }

            var wideOutcome = await glue.ObserveAsync(
                session, wide.Request, "application/sparql-results+json", membership,
                () => counted, value => counted = value, CancellationToken.None, budget);
            if (wideOutcome.Transport is not { } wideTransport)
            {
                await RetainAsync(
                    root, store, "DiscoveryWideWindowRefused", wideOutcome.Failure?.Kind.ToString(),
                    budget, sessionsOpened, null, null, startedAt);
                Assert.Fail(
                    $"the wide discovery window was refused: {wideOutcome.Failure?.Kind}. "
                    + $"Evidence retained under {root}.");
                return;
            }

            // WHAT WAS ASKED, PROVED BY DIGEST. The body is not on disk; its length and SHA-256 are.
            narrowBodySha = narrowTransport.LogicalRequest.Body.Sha256;
            wideDossiers = ParseDossiers(wideTransport.RetainedPayloadBytes.Span);
            narrowDossiers = ParseDossiers(narrowTransport.RetainedPayloadBytes.Span);
        }

        // ---- ACCEPTANCE ----------------------------------------------------------------------
        // The producer opens its own session, so discovery's is disposed above first.
        string? acceptanceRefusal = null;
        EuProcedureEventProductionResult? production = null;
        var discovered = narrowDossiers.Take(2).ToArray();

        if (discovered.Length == 2 && sessionsOpened < SessionCeiling)
        {
            var spentBeforeAcceptance = budget.Spent;
            production = await new EuProcedureEventProducer(store, TimeProvider.System).RunAsync(
                new EuProcedureEventRunRequest(
                    EuProcedureEventDiscoveryPlan.Create(),
                    discovered,
                    NewUrn(),
                    MachineQueryRendererSource.Open(
                        new SourceArtifactRef(
                            NewUrn(), Convert.ToHexStringLower(SHA256.HashData(rendererSourceBytes))),
                        rendererSourceBytes),
                    budget),
                EuAcquisitionTestFixture.SourceWitness(),
                CancellationToken.None);
            if (budget.Spent > spentBeforeAcceptance)
            {
                sessionsOpened++;
            }

            acceptanceRefusal = production.Delivered ? null : production.Refusal.ToString();
        }

        // ---- RETAINED BEFORE IT IS JUDGED ----------------------------------------------------
        // A POSITIVE OBSERVATION, FOUND THROUGH THE RESULT'S OWN ACCESSOR. EventsOf is how the
        // contract answers "what did this dossier hold", and it distinguishes an evidenced empty
        // answer from a dossier never asked about. Every delivered observation carries a date by
        // construction - an undated event refuses the row by name rather than arriving null - so
        // "positive" is checked as typed AND dated rather than as non-null.
        EuProcedureEventObservation? positive = null;
        string? positiveDossier = null;
        if (production is { Delivered: true })
        {
            foreach (var dossier in discovered)
            {
                var candidate = production.EventsOf(dossier).FirstOrDefault(
                    static observation => observation.ObservedTypeIris.Count > 0
                        && observation.RawDateLexical.Length > 0);
                if (candidate is not null)
                {
                    positive = candidate;
                    positiveDossier = dossier;
                    break;
                }
            }
        }
        await RetainAsync(
            root, store,
            production is null ? "AcceptanceNotAttempted"
                : production.Delivered ? "AcceptanceDelivered" : "AcceptanceRefused",
            acceptanceRefusal, budget, sessionsOpened, discovered, production, startedAt,
            narrowBodySha, narrowDossiers, wideDossiers);

        var offenders = OffendingHosts(root);

        // ---- JUDGEMENT -----------------------------------------------------------------------
        Assert.IsEmpty(
            offenders,
            "this operation may contact the SPARQL endpoint and its robots redirect target and "
            + "nothing else: " + string.Join("; ", offenders.Take(10)));

        Assert.HasCount(
            2, discovered,
            "the decision requires exactly two distinct discovered dossiers; the publisher returned "
            + $"{narrowDossiers.Count}. Evidence retained under {root}.");
        Assert.AreEqual(
            discovered.Length, discovered.Distinct(StringComparer.Ordinal).Count(),
            "the two discovered dossiers must be distinct.");
        Assert.IsTrue(
            WindowsAgree(discovered, wideDossiers),
            "the wider window must agree with the narrow one on its first rows, or the result set is "
            + "not stably ordered and neither window discovered anything: "
            + $"narrow=[{string.Join(", ", discovered)}] wide=[{string.Join(", ", wideDossiers)}]");

        Assert.IsNotNull(
            production,
            $"acceptance was never attempted. Evidence retained under {root}.");
        Assert.IsTrue(
            production.Delivered,
            $"the producer refused: {production.Refusal} {production.Detail}. "
            + $"Evidence retained under {root}.");
        Assert.IsNotNull(
            positive,
            "the decision requires at least one POSITIVE typed observation carrying its date; the run "
            + $"delivered {production.Observations?.Count ?? 0} observation(s), "
            + $"{production.ExcludedEvents?.Count ?? 0} excluded, and none was both typed and dated. "
            + $"Evidence retained under {root}.");
        Assert.IsNotEmpty(
            positive.EventIri,
            "a positive observation names the event node it is about.");
        Assert.IsNotEmpty(
            positive.DateDatatypeIri,
            "the date arrives with the datatype the publisher gave it, never widened or guessed.");
        Assert.IsNotNull(positiveDossier);
        Assert.Contains(
            positiveDossier, discovered,
            "the observation was reached through a dossier this run discovered.");
        Assert.IsNotNull(production.DossiersAskedAbout);
        foreach (var dossier in discovered)
        {
            Assert.Contains(
                dossier, production.DossiersAskedAbout,
                "the producer must have asked about exactly the discovered dossiers.");
        }
        Assert.IsNotNull(
            production.CompletionEvidenceRef,
            "provenance: a delivered run cites the acquisition run that produced it.");

        // ---- THE BOUNDS ----------------------------------------------------------------------
        Assert.IsLessThanOrEqualTo(
            SessionCeiling, sessionsOpened,
            $"sessions are bounded at {SessionCeiling} because the send ceiling depends on it.");
        Assert.IsLessThanOrEqualTo(
            SharedWireCeiling, budget.Spent,
            $"charged requests are bounded at {SharedWireCeiling}.");
        Assert.IsLessThanOrEqualTo(
            SendCeiling, budget.Spent + sessionsOpened,
            $"actual sends are charged plus one robots redirect hop per session, bounded at "
            + $"{SendCeiling}.");
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
    internal static bool WindowsAgree(
        IReadOnlyList<string> narrow, IReadOnlyList<string> wide)
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

    /// <summary>Every <c>?dossier</c> IRI the publisher returned, in the order it returned them.</summary>
    internal static IReadOnlyList<string> ParseDossiers(ReadOnlySpan<byte> payload)
    {
        using var document = JsonDocument.Parse(payload.ToArray());
        var rows = new List<string>();
        if (!document.RootElement.TryGetProperty("results", out var results)
            || !results.TryGetProperty("bindings", out var bindings))
        {
            return rows;
        }

        foreach (var binding in bindings.EnumerateArray())
        {
            if (binding.TryGetProperty("dossier", out var dossier)
                && dossier.TryGetProperty("value", out var value)
                && value.GetString() is { Length: > 0 } iri)
            {
                rows.Add(iri);
            }
        }

        return rows;
    }

    /// <summary>
    /// One terminal index, written on every outcome before any of it is judged.
    /// </summary>
    /// <remarks>
    /// Mirrors the integrated draft sweep. A run that stopped is the run whose cost most needs
    /// stating, so this is written for refusals exactly as for success, and it reports charged
    /// requests, sessions and DERIVED SENDS separately because the two ceilings are different
    /// promises.
    /// </remarks>
    private static async Task RetainAsync(
        string root,
        FileSystemCustodyStore store,
        string verdict,
        string? refusal,
        WireRequestBudget budget,
        int sessionsOpened,
        IReadOnlyList<string>? discovered,
        EuProcedureEventProductionResult? production,
        DateTimeOffset startedAt,
        string? askedBodySha256 = null,
        IReadOnlyList<string>? narrowWindow = null,
        IReadOnlyList<string>? wideWindow = null)
    {
        var index = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                purpose = "E8 EU procedure-event bounded discovery and live acceptance: terminal "
                    + "accounting, retained on every outcome including safety stops.",
                verdict,
                refusal,
                observedFromUtc = startedAt.UtcDateTime.ToString("O"),
                observedToUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
                predicate = EuProcedureEventVocabulary.PartOfDossierPredicateUri,
                predicateNote = "The owner decision named "
                    + "cdm:procedure_event_belongs_to_procedure_dossier, which exists nowhere in "
                    + "this repository except one prose doc comment. This run asked the predicate "
                    + "the frozen contract declares, so that discovery and acceptance ask the same "
                    + "thing. Reported on #417.",
                askedBodySha256,
                narrowWindow,
                wideWindow,
                discovered,
                chargedRequests = budget.Spent,
                chargedCeiling = budget.Limit,
                exhausted = budget.Exhausted,
                sessionsOpened,
                sessionCeiling = SessionCeiling,
                derivedActualSends = budget.Spent + sessionsOpened,
                sendCeiling = SendCeiling,
                sendDerivation = "One send per charged product request, plus one uncharged robots "
                    + "redirect hop per session: the query channel admits no product redirect and "
                    + "the robots route declares exactly two steps.",
                productRequestCount = production?.ProductRequestCount,
                delivered = production?.Delivered,
                observationCount = production?.Observations?.Count,
                typedAndDatedObservationCount = production?.Observations?.Count(
                    static observation => observation.ObservedTypeIris.Count > 0
                        && observation.RawDateLexical.Length > 0),
                excludedEventCount = production?.ExcludedEvents?.Count,
                completionEvidenceSha256 = production?.CompletionEvidenceRef?.Sha256,
                root,
            },
            new JsonSerializerOptions { WriteIndented = true });

        await store.CreateAsync(index, CustodyClass.NightlyFloor90d, CancellationToken.None);
        await File.WriteAllBytesAsync(Path.Combine(root, "terminal-index.json"), index);
    }

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
                    var end = text.IndexOfAny(['/', '\"', '\n', '\t', ' '], index + token.Length);
                    var host = end < 0
                        ? text[(index + token.Length)..]
                        : text[(index + token.Length)..end];
                    if (host.Length > 0
                        && !AdmittedHosts.Contains(host, StringComparer.OrdinalIgnoreCase)
                        && !host.StartsWith("www.w3.org", StringComparison.OrdinalIgnoreCase)
                        && !host.StartsWith("data.europa.eu", StringComparison.OrdinalIgnoreCase)
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

    /// <summary>
    /// The renderer for this run's discovery question.
    /// </summary>
    /// <remarks>
    /// Its renderer SOURCE is the frozen procedure-event plan's own file bytes, as the integrated
    /// live harnesses do, so the retained provenance names the reviewed source this question was
    /// written against rather than a fixture label.
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
