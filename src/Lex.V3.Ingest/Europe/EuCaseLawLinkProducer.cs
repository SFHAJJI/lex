using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

/// <summary>Why delivered case-law rows could not become E6 bindings.</summary>
public enum EuCaseLawLinkProductionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>The enumeration itself was refused, so there are no rows to read.</summary>
    [JsonStringEnumMemberName("enumeration_refused")]
    EnumerationRefused = 1,

    /// <summary>The run delivered but its whole enumeration was not proven.</summary>
    [JsonStringEnumMemberName("enumeration_proof_refused")]
    EnumerationProofRefused = 2,

    /// <summary>A delivered row could not be admitted as a case-law link.</summary>
    [JsonStringEnumMemberName("row_not_admitted")]
    RowNotAdmitted = 3,

    /// <summary>
    /// A row names an act whose body scope the caller did not supply.
    /// </summary>
    /// <remarks>
    /// Whether an act's own body is held is a fact about this corpus, not about the publisher's
    /// case-law rows, so it cannot be decoded from a row and must not be defaulted. Choosing a value
    /// here would assert a body-holding fact nobody supplied, which is a claim rather than a reading.
    /// </remarks>
    [JsonStringEnumMemberName("target_body_scope_not_supplied")]
    TargetBodyScopeNotSupplied = 4,

    /// <summary>
    /// The proven pages would not reopen into verified rows.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="EnumerationProofRefused"/>: the enumeration was proven and the
    /// failure is later, in re-deriving each page's rows from its retained bytes. Folding the two
    /// would report a proof failure for a delivery whose proof held.
    /// </remarks>
    [JsonStringEnumMemberName("verified_rows_refused")]
    VerifiedRowsRefused = 5,

    /// <summary>
    /// The caller supplied no body scope for an act this run actually asked about.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="TargetBodyScopeNotSupplied"/>, which is about an act a DELIVERED ROW
    /// named. This one is about the run's own question: a batch member with no scope cannot produce
    /// links even if the publisher answers for it, so the run would report a proven empty set for an
    /// act it really did ask about. Caught before the request rather than after the answer.
    /// </remarks>
    [JsonStringEnumMemberName("requested_act_body_scope_not_supplied")]
    RequestedActBodyScopeNotSupplied = 6,

    /// <summary>
    /// A delivered row reached neither an admitted relation nor a typed unrepresentable row.
    /// </summary>
    /// <remarks>
    /// A conservation failure rather than anything the publisher can cause. The decode loop has
    /// three exits per row - admitted, unrepresentable, or a refusal that returns - so a row can
    /// only go missing through an edit that adds a fourth. A row silently dropped between delivery
    /// and result is the false absence S2-A03 forbids, and it would be invisible in every count this
    /// result reports, which is why it is refused loudly instead.
    /// </remarks>
    [JsonStringEnumMemberName("delivered_row_not_accounted_for")]
    DeliveredRowNotAccountedFor = 7,
}

/// <summary>
/// One delivered row that names a real citation this contract cannot carry as a link, because
/// the publisher delivered nothing that proves which side is the case.
/// </summary>
/// <remarks>
/// <para>
/// This exists so that "kept and typed, never dropped" is true of the rows too, not only of the
/// ECLI state. The row is well formed and the citation is real; what is missing is an identity
/// <c>OfficialIdentifier.ProvesCase()</c> accepts, and inventing one is forbidden.
/// </para>
/// <para>
/// It is deliberately NOT a refusal of the whole production. A malformed row means the delivery
/// itself cannot be trusted, so that refuses everything; this row is trustworthy and simply
/// unrepresentable, and sinking the act's other links over it would lose facts the publisher did
/// deliver. Structural exclusions are first class here rather than absences.
/// </para>
/// </remarks>
public sealed record EuCaseLawUnrepresentableRow(
    string CaseWorkUri,
    string EuWorkUri,
    string PredicateUri);

/// <summary>One admitted case-law link and the coordinates it was read from.</summary>
public sealed record EuCaseLawLinkRelation(
    string CaseWorkUri,
    string EuWorkUri,
    string PredicateUri,
    EuCaseLawLinkBinding Binding);

/// <summary>Admitted case-law links, or one typed refusal. Never both.</summary>
public sealed class EuCaseLawLinkProductionResult
{
    private EuCaseLawLinkProductionResult(
        IReadOnlyList<EuCaseLawLinkRelation>? relations,
        IReadOnlyList<EuCaseLawUnrepresentableRow>? unrepresentableRows,
        IReadOnlySet<string>? actsAskedAbout,
        SourceArtifactRef? completionEvidenceRef,
        EuCaseLawLinkProductionRefusal refusal,
        string? detail)
    {
        Relations = relations;
        UnrepresentableRows = unrepresentableRows;
        ActsAskedAbout = actsAskedAbout;
        CompletionEvidenceRef = completionEvidenceRef;
        Refusal = refusal;
        Detail = detail;
    }

    public IReadOnlyList<EuCaseLawLinkRelation>? Relations { get; }

    /// <summary>
    /// Delivered citations no link could be built for, kept rather than dropped. Null on a
    /// refused run, for the same reason <see cref="Relations"/> is.
    /// </summary>
    public IReadOnlyList<EuCaseLawUnrepresentableRow>? UnrepresentableRows { get; }

    /// <summary>
    /// The acts this production actually asked about. Null on a refused run.
    /// </summary>
    /// <remarks>
    /// Kept so that "no judgment cites this act" can only be said about an act that was asked about.
    /// Without it, filtering an act nobody enumerated returns an empty list that reads as a proven
    /// absence, which is the same false emptiness the refused-run guard already prevents by a
    /// different route.
    /// </remarks>
    public IReadOnlySet<string>? ActsAskedAbout { get; }
    public SourceArtifactRef? CompletionEvidenceRef { get; }
    public EuCaseLawLinkProductionRefusal Refusal { get; }
    public string? Detail { get; }
    public bool Delivered => Refusal == EuCaseLawLinkProductionRefusal.None;

    /// <summary>
    /// How many product requests the run this result came from actually sent.
    /// </summary>
    /// <remarks>
    /// Carried on a refusal as well as a success, because a refused run still spent the publisher's
    /// budget and a receipt reporting nothing for it would understate the traffic caused.
    /// </remarks>
    public int ProductRequestCount { get; private init; }

    internal static EuCaseLawLinkProductionResult Success(
        IReadOnlyList<EuCaseLawLinkRelation> relations,
        IReadOnlyList<EuCaseLawUnrepresentableRow> unrepresentableRows,
        IReadOnlySet<string> actsAskedAbout,
        SourceArtifactRef completionEvidenceRef,
        int productRequestCount = 0) =>
        new(Array.AsReadOnly(relations.ToArray()),
            Array.AsReadOnly(unrepresentableRows.ToArray()), actsAskedAbout, completionEvidenceRef,
            EuCaseLawLinkProductionRefusal.None, null) { ProductRequestCount = productRequestCount };

    internal static EuCaseLawLinkProductionResult Refused(
        EuCaseLawLinkProductionRefusal refusal,
        string detail,
        int productRequestCount = 0)
    {
        if (refusal == EuCaseLawLinkProductionRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        return new(null, null, null, null, refusal, detail)
        {
            ProductRequestCount = productRequestCount,
        };
    }

    /// <summary>
    /// The admitted links for one exact EU act. A refused run is never readable as an empty set.
    /// </summary>
    public IReadOnlyList<EuCaseLawLinkRelation> ForEuWork(string euWorkUri)
    {
        RequireAskedAbout(euWorkUri);
        return Array.AsReadOnly(Relations!
            .Where(value => string.Equals(value.EuWorkUri, euWorkUri, StringComparison.Ordinal))
            .ToArray());
    }

    /// <summary>
    /// The delivered citations for one exact EU act that could not become links. A caller that
    /// reads <see cref="ForEuWork"/> and ignores this is reading a partial answer as a whole one,
    /// which is why the two are separate methods rather than one filtered list.
    /// </summary>
    public IReadOnlyList<EuCaseLawUnrepresentableRow> UnrepresentableForEuWork(string euWorkUri)
    {
        RequireAskedAbout(euWorkUri);
        return Array.AsReadOnly(UnrepresentableRows!
            .Where(value => string.Equals(value.EuWorkUri, euWorkUri, StringComparison.Ordinal))
            .ToArray());
    }

    /// <summary>
    /// Refuses to answer for a run that was refused, or for an act this run never asked about.
    /// </summary>
    /// <remarks>
    /// Both are the same mistake wearing different clothes. Returning an empty list in either case
    /// would let a caller read "no judgment cites this act" out of a run that either failed or never
    /// looked, and those are different answers to a user's question from a proven absence.
    /// </remarks>
    private void RequireAskedAbout(string euWorkUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(euWorkUri);
        if (!Delivered || Relations is null || UnrepresentableRows is null ||
            ActsAskedAbout is null || CompletionEvidenceRef is null)
        {
            throw new InvalidOperationException(
                "A refused case-law production has no admitted links.");
        }

        if (!ActsAskedAbout.Contains(euWorkUri))
        {
            throw new ArgumentOutOfRangeException(
                nameof(euWorkUri),
                $"This production never asked about {euWorkUri}, so it cannot say whether case law "
                    + "cites it.");
        }
    }
}

/// <summary>
/// Turns delivered case-law rows into E6 bindings, or refuses with a typed reason.
/// </summary>
/// <remarks>
/// <para>
/// THE ECLI IS READ FROM ITS TERM, NEVER FROM ITS MARKER. The plan binds <c>?ecli_kind</c> as a
/// convenience, but that marker is a value this codebase computes ABOUT a row, not the publisher's
/// word for what the row is. This seat has already shipped the opposite once, on the E1 axiom
/// decoder, and had it found in review: a row whose marker disagreed with its term was read by the
/// marker. Here the term decides, and a marker that disagrees with it refuses the row rather than
/// being quietly overruled — a disagreement means one of the two is wrong and neither is safe to
/// prefer silently.
/// </para>
/// <para>
/// THE ACT'S BODY SCOPE IS SUPPLIED, NOT INFERRED. <see cref="EuCaseLawLinkBinding.Create"/> needs
/// a <see cref="TargetBodyScope"/> for the act, and no case-law row carries one: whether this corpus
/// holds that act's body is a fact about the corpus. A row naming an act the caller did not supply a
/// scope for is refused. Defaulting would assert a body-holding fact nobody stated, and the caller
/// is the only party that knows it.
/// </para>
/// <para>
/// REFUSED WHOLE, NEVER FILTERED. One unadmitted row refuses the production. Admitting the rest
/// would hand back a link set that looks complete and is not, and the dropped row would leave
/// nothing behind to notice.
/// </para>
/// </remarks>
public sealed class EuCaseLawLinkProducer
{
    private readonly EuRepeatedEnumerationExecutor _executor;
    private readonly RepeatedEnumerationDeliveryReopenGlue _reopenGlue;

    public EuCaseLawLinkProducer(ICustodyStore custodyStore, TimeProvider timeProvider)
        : this(custodyStore, timeProvider, null)
    {
    }

    internal EuCaseLawLinkProducer(
        ICustodyStore custodyStore,
        TimeProvider timeProvider,
        System.Net.Http.HttpMessageHandler? testHandlerOverride)
    {
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _executor = new EuRepeatedEnumerationExecutor(custodyStore, timeProvider, testHandlerOverride);
        _reopenGlue = new RepeatedEnumerationDeliveryReopenGlue(custodyStore);
    }

    /// <summary>
    /// Runs the family and reads its proven rows. The only public way to obtain links.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ACTS THIS RUN ASKED ABOUT ARE ITS OWN BATCH, in the plan's canonical form, not the keys
    /// of the caller's scope map. Those are different sets and the difference is a false absence: a
    /// caller supplying scopes for A and B while the batch asks only about A would have
    /// <c>ForEuWork(B)</c> answer an evidenced empty set for an act nobody enumerated.
    /// </para>
    /// <para>
    /// A scope is still required for every requested act, and refused by name before the request
    /// rather than after the answer, because an act with no scope cannot produce links even when the
    /// publisher answers for it.
    /// </para>
    /// </remarks>
    public async Task<EuCaseLawLinkProductionResult> RunAsync(
        EuCaseLawRunRequest request,
        IReadOnlyDictionary<string, TargetBodyScope> actBodyScopes,
        BoundMachineRequest sourceWitness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actBodyScopes);
        ArgumentNullException.ThrowIfNull(sourceWitness);

        var scopes = Exactly(actBodyScopes);
        var asked = EuCaseLawDiscoveryPlan.RequestedPartitionMembers(request.BatchWorks);
        foreach (var act in asked)
        {
            if (!scopes.ContainsKey(act))
            {
                return EuCaseLawLinkProductionResult.Refused(
                    EuCaseLawLinkProductionRefusal.RequestedActBodyScopeNotSupplied,
                    $"This run asks about {act} and no body scope was supplied for it.");
            }
        }

        var run = await _executor.RunCaseLawLinksAsync(
            request, sourceWitness, cancellationToken).ConfigureAwait(false);
        if (run.Receipt is not { } receipt)
        {
            return EuCaseLawLinkProductionResult.Refused(
                EuCaseLawLinkProductionRefusal.EnumerationRefused,
                run.Refusal?.Code.ToString() ?? "enumeration returned neither a receipt nor a refusal",
                run.ProductRequestCount);
        }

        var proof = receipt.TryProveFamilyEnumeration(receipt.Delivery.PartitionKey, out var proofRefusal);
        if (proof is null)
        {
            return EuCaseLawLinkProductionResult.Refused(
                EuCaseLawLinkProductionRefusal.EnumerationProofRefused,
                proofRefusal.ToString(),
                run.ProductRequestCount);
        }

        var pages = new List<RepeatedEnumerationResolvedEvidence>(receipt.Delivery.PagesA.Pages.Count);
        foreach (var page in receipt.Delivery.PagesA.Pages.OrderBy(static value => value.Ordinal))
        {
            pages.Add(await _reopenGlue.ReopenPageEvidenceAsync(page.Evidence, cancellationToken)
                .ConfigureAwait(false));
        }

        var profile = request.Plan.CreateDeliveryProfile();
        var rows = VerifiedRepeatedEnumerationRows.TryOpen(
            proof,
            receipt.Delivery,
            profile,
            receipt.Delivery.InterpretationProfileRef,
            receipt.Delivery.CountA.HttpEvidenceRef,
            pages,
            out var rowRefusal);
        if (rows is null)
        {
            return EuCaseLawLinkProductionResult.Refused(
                EuCaseLawLinkProductionRefusal.VerifiedRowsRefused,
                rowRefusal.ToString(),
                run.ProductRequestCount);
        }

        return DecodeRows(
            rows, profile, scopes, proof.AcquisitionRunRef, asked, run.ProductRequestCount);
    }

    /// <summary>
    /// Decodes one delivered page set into bindings.
    /// </summary>
    /// <param name="actBodyScopes">
    /// Each act's own body scope, keyed by its Cellar work URI. A row naming an act absent from this
    /// map is refused rather than defaulted.
    /// </param>
    /// <summary>
    /// The supplied scopes, re-keyed under the exact comparer this identity boundary requires.
    /// </summary>
    /// <remarks>
    /// A CELLAR WORK URI IS AN EXACT COORDINATE and case is part of it. The map arrives from the
    /// caller, so its comparer is the caller's choice, and an OrdinalIgnoreCase one made a scope
    /// supplied for <c>/resource/CELLAR/…</c> answer for the distinct canonical
    /// <c>/resource/cellar/…</c>: the run sent traffic and bound the requested act to evidence
    /// supplied for another coordinate, and the pre-request refusal meant to prevent exactly that
    /// never fired. Codex found this at head <c>a27760b5</c>.
    ///
    /// Copied rather than merely wrapped, because a wrapper still answers through the comparer it
    /// was given. Both the preflight and the decode read through this copy.
    /// </remarks>
    private static Dictionary<string, TargetBodyScope> Exactly(
        IReadOnlyDictionary<string, TargetBodyScope> supplied)
    {
        var exact = new Dictionary<string, TargetBodyScope>(StringComparer.Ordinal);
        foreach (var pair in supplied)
        {
            exact[pair.Key] = pair.Value;
        }

        return exact;
    }

    /// <remarks>
    /// INTERNAL, and deliberately. A public decoder taking a caller's rows and a caller's evidence
    /// reference can mint links from rows nobody proved, citing custody nobody established - and a
    /// case-law link whose evidence a caller invented is not the publisher-asserted metadata S2-A01
    /// and S2-A07 permit here. Callers come through <see cref="RunAsync"/>; tests reach this by
    /// <c>InternalsVisibleTo</c>, which is a test seam and not a second public door.
    /// </remarks>
    /// <remarks>
    /// THIS PARAGRAPH USED TO CITE "Candidate 5 R5.3" for a rule against an unnamed intermediate.
    /// R5.3 is one sentence at <c>05-user-journeys.md:345</c> requiring every refusal envelope to
    /// carry <c>what_would_answer</c>, and nothing in Candidate 5's R5.1-R5.5 forbids any such
    /// intermediate. The citation was mine and it was invented; the door stays internal for the
    /// reason above, which is the one the reviewed ruling placed this slice on.
    /// </remarks>
    /// <param name="actsAskedAbout">
    /// The acts the RUN asked about, canonical. Null falls back to the scope map's keys, which is
    /// what a decode-only caller can offer and is why the run supplies its own.
    /// </param>
    internal static EuCaseLawLinkProductionResult DecodeRows(
        IReadOnlyList<RepeatedEnumerationRow> rows,
        RepeatedEnumerationInterpretationProfile profile,
        IReadOnlyDictionary<string, TargetBodyScope> actBodyScopes,
        SourceArtifactRef completionEvidenceRef,
        IReadOnlyList<string>? actsAskedAbout = null,
        int productRequestCount = 0)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(actBodyScopes);
        ArgumentNullException.ThrowIfNull(completionEvidenceRef);

        // Read under the exact comparer here as well, so the guard holds for a direct internal
        // caller and not only for the one RunAsync makes.
        var exactScopes = Exactly(actBodyScopes);

        var relations = new List<EuCaseLawLinkRelation>(rows.Count);
        var unrepresentable = new List<EuCaseLawUnrepresentableRow>();
        foreach (var row in rows)
        {
            string euWorkUri;
            try
            {
                euWorkUri = RequireIri(Term(row, profile, "eu_work"), "eu_work");
            }
            catch (ArgumentException exception)
            {
                return EuCaseLawLinkProductionResult.Refused(
                    EuCaseLawLinkProductionRefusal.RowNotAdmitted, exception.Message);
            }

            if (!exactScopes.TryGetValue(euWorkUri, out var scope))
            {
                return EuCaseLawLinkProductionResult.Refused(
                    EuCaseLawLinkProductionRefusal.TargetBodyScopeNotSupplied,
                    $"No body scope was supplied for {euWorkUri}, and one cannot be read from a case-law row.");
            }

            try
            {
                // The run's own retained reference is the custody coordinate these terms were read
                // from, which is what E6's binding means by a source observation id.
                relations.Add(DecodeRow(
                    row, profile, euWorkUri, scope, completionEvidenceRef.ResourceId));
            }
            catch (CaseSideNotProvableException)
            {
                // Well formed, and a real citation, but nothing delivered proves which side is the
                // case. Kept as a typed exclusion instead of refusing the act's other links.
                unrepresentable.Add(new EuCaseLawUnrepresentableRow(
                    RequireIri(Term(row, profile, "case_work"), "case_work"),
                    euWorkUri,
                    RequireIri(Term(row, profile, "case_predicate"), "case_predicate")));
            }
            catch (ArgumentException exception)
            {
                return EuCaseLawLinkProductionResult.Refused(
                    EuCaseLawLinkProductionRefusal.RowNotAdmitted, exception.Message);
            }
        }

        // EVERY DELIVERED ROW IS ACCOUNTED FOR EXACTLY ONCE. The loop above has three exits per
        // row - an admitted relation, a typed unrepresentable row, or a refusal that returns - so a
        // delivered row can only go missing through a future edit that adds a fourth. This is that
        // edit's alarm, and it is a conservation check rather than a guard against the publisher:
        // a row silently dropped between delivery and result is the false absence S2-A03 forbids,
        // and it would be invisible in every count this result reports.
        if (relations.Count + unrepresentable.Count != rows.Count)
        {
            return EuCaseLawLinkProductionResult.Refused(
                EuCaseLawLinkProductionRefusal.DeliveredRowNotAccountedFor,
                $"The delivery carried {rows.Count} rows and this result accounts for "
                    + $"{relations.Count + unrepresentable.Count} of them.");
        }

        return EuCaseLawLinkProductionResult.Success(
            relations,
            unrepresentable,
            (actsAskedAbout ?? exactScopes.Keys.ToArray()).ToHashSet(StringComparer.Ordinal),
            completionEvidenceRef,
            productRequestCount);
    }

    private static EuCaseLawLinkRelation DecodeRow(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string euWorkUri,
        TargetBodyScope targetBodyScope,
        string observationId)
    {
        if (row.Terms.Count != profile.ProjectionVariables.Count || row.Terms.Count != 13)
        {
            throw new ArgumentException("A case-law row has thirteen exact terms.", nameof(row));
        }

        var caseWorkUri = RequireIri(Term(row, profile, "case_work"), "case_work");

        var predicateUri = RequireIri(Term(row, profile, "case_predicate"), "case_predicate");

        // EVERYTHING THAT DOES NOT DEPEND ON THE CASE IDENTITY IS ASKED BEFORE THE CASE IDENTITY IS
        // CLASSIFIED. This ordering is load-bearing and it was not, until review caught it.
        //
        // Classifying the identity can now END this row's decoding: a citation whose identifier
        // belongs to another scheme throws CaseSideNotProvableException, which the caller records
        // as a typed exclusion with Refusal=None. Anything asked AFTER that point is therefore
        // never asked at all for such a row. A delivery carrying both a foreign identifier and a
        // predicate this family never requested was accepted as an ordinary excluded citation, and
        // the unasked predicate - which means the response is not the answer to the question asked
        // - went unreported.
        //
        // So the rule is: the typed exclusion may only ever mean "the case side is not provable".
        // It must never also mean "and we stopped looking before we found the rest".
        //
        // The predicate is asked of EuCaseLawPredicateVocabulary itself rather than restated here.
        // Create still refuses an unpinned predicate on its own, and that is deliberate: this is
        // the earlier of two asks of ONE authority, not a second copy of the rule.
        if (!EuCaseLawPredicateVocabulary.IsPinned(predicateUri))
        {
            throw new ArgumentException(
                $"\"{predicateUri}\" is not one of the pinned EU case-law predicates, so this "
                    + "delivery is not the answer to the question this family asked.",
                nameof(row));
        }

        // Hoisted above the identity for the same reason: the act's identity is the publisher's
        // claim about the act, and it is knowable without knowing which side is the case.
        var actIdentity = new OfficialIdentitySet(
            PublisherId.EuEurLex,
            [new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, euWorkUri)]);

        var caseIdentifier = RequireCaseIdentityFromItsTermsNotItsMarkers(
            Term(row, profile, "ecli"),
            Term(row, profile, "ecli_kind"),
            Term(row, profile, "case_celex"),
            Term(row, profile, "case_celex_kind"));

        var caseIdentity = new OfficialIdentitySet(PublisherId.EuEurLex, [caseIdentifier]);

        var binding = EuCaseLawLinkBinding.Create(
            caseIdentity, actIdentity, predicateUri, targetBodyScope, [], observationId);
        return new EuCaseLawLinkRelation(caseWorkUri, euWorkUri, predicateUri, binding);
    }

    /// <summary>
    /// Reads the case's identity from its terms, using the markers only to detect disagreement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A marker cannot be the authority: it is this codebase's own <c>BIND</c> about a row. But a
    /// marker that contradicts its term is not noise either — it means the delivery is not what
    /// either side believes, so the row is refused rather than read past.
    /// </para>
    /// <para>
    /// Either identity proves the case, because <c>OfficialIdentifier.ProvesCase()</c> accepts a
    /// well-formed ECLI or a sector-6 CELEX. The plan asks for the CELEX only on the branch where
    /// the ECLI is absent, so exactly one of the two is answered per row and the markers say which:
    /// an ECLI-bearing row carries <see cref="EuCaseLawDiscoveryPlan.CelexNotAskedKind"/>, and a row
    /// without one carries a real CELEX answer. A row claiming both, or neither, is a delivery this
    /// plan cannot have produced.
    /// </para>
    /// <para>
    /// Where the publisher honestly has neither, this throws
    /// <see cref="CaseSideNotProvableException"/> rather than a plain refusal, and the caller keeps
    /// the row as a typed exclusion. Earlier this producer refused the whole production for that
    /// shape, which contradicted the accepted "never dropped" and took the act's other links with
    /// it.
    /// </para>
    /// </remarks>
    private static OfficialIdentifier RequireCaseIdentityFromItsTermsNotItsMarkers(
        RepeatedEnumerationRdfTerm ecli,
        RepeatedEnumerationRdfTerm ecliMarker,
        RepeatedEnumerationRdfTerm celex,
        RepeatedEnumerationRdfTerm celexMarker)
    {
        // THE IDENTITY CONTRACT'S FIRST GATE, ASKED BEFORE ANY OTHER QUESTION ABOUT THESE TERMS.
        // It has to come first because the questions below cannot tell a corrupt term from an
        // absent one: IsBoundLiteral reports an EMPTY literal as "not bound", which sends a
        // delivered-but-empty identifier down the same path as a term the publisher never sent.
        // Those are different claims. "The publisher answered with nothing in the field" means the
        // response is untrustworthy; "the publisher sent no term at all" is an honest absence this
        // family records as a typed exclusion. Collapsing them is the false absence S2-A03 forbids.
        //
        // Asked of BOTH terms, because the hole is symmetric - an empty ecli literal reached the
        // same typed exclusion by the same route.
        RequireDeliveredLiteralIsAnIdentity(ecli, nameof(ecli));
        RequireDeliveredLiteralIsAnIdentity(celex, nameof(celex));

        var ecliIsBoundLiteral = IsBoundLiteral(ecli);
        if (!string.Equals(ecliMarker.Value, MarkerFor(ecli), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The ecli term and the ecli_kind marker disagree about what was delivered.",
                nameof(ecli));
        }

        var celexWasNotAsked = string.Equals(
            celexMarker.Value, EuCaseLawDiscoveryPlan.CelexNotAskedKind, StringComparison.Ordinal);
        if (ecliIsBoundLiteral != celexWasNotAsked)
        {
            throw new ArgumentException(
                "The plan asks for the CELEX only where the ECLI is absent, so a row cannot answer "
                    + "both questions or neither.",
                nameof(celexMarker));
        }

        if (ecliIsBoundLiteral)
        {
            if (celex.Kind != RepeatedEnumerationRdfTermKind.Unbound)
            {
                throw new ArgumentException(
                    "A row whose CELEX was never asked for cannot carry one.", nameof(celex));
            }

            return new OfficialIdentifier(FactsIdentifierFamily.Ecli, ecli.Value!);
        }

        var celexIsBoundLiteral = IsBoundLiteral(celex);
        if (!string.Equals(celexMarker.Value, MarkerFor(celex), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The case_celex term and its marker disagree about what was delivered.",
                nameof(celex));
        }

        if (!celexIsBoundLiteral)
        {
            throw new CaseSideNotProvableException(
                "The publisher delivered no ECLI and no CELEX literal for this case.");
        }

        // A LITERAL NO CELEX GRAMMAR ADMITS IS A CITATION WE CANNOT REPRESENT, NOT A BROKEN
        // DELIVERY. Measured, not supposed: the retained E6 run under #415 carried 81 such values
        // across 338 rows - OJ C-series references like C/2024/01610 and C2023/099/01, and the EFTA
        // case number E2014C0273. Letting the identifier constructor throw made every one of them
        // sink the whole production, so 2,052 links the publisher did deliver were lost to a
        // citation whose identifier belongs to a scheme this family does not read.
        //
        // The check is asked BEFORE construction rather than caught after it, so it can only ever
        // reclassify the grammar decision. Catching the constructor's ArgumentException would also
        // swallow a genuinely malformed row, and those must keep refusing the delivery. By this
        // point the identity gate above has already rejected everything that is not an identity at
        // all, so a null profile here means exactly one thing: a real identifier from a scheme this
        // family does not read.
        if (OfficialIdentifier.ProfileOf(celex.Value!) is null)
        {
            throw new CaseSideNotProvableException(
                "The delivered case_celex literal is not a CELEX identifier in any sector: "
                    + celex.Value);
        }

        var identifier = new OfficialIdentifier(FactsIdentifierFamily.Celex, celex.Value!);

        // ProvesCase is asked rather than restated: sector 6 is case law and this producer does not
        // own that rule. A CELEX outside it is a real identity for something that is not a case, so
        // the row is unrepresentable rather than malformed.
        if (!identifier.ProvesCase())
        {
            throw new CaseSideNotProvableException(
                "The delivered CELEX does not prove the subject is case law.");
        }

        return identifier;
    }

    /// <summary>
    /// A literal delivered in an identifier position must be an opaque identity, or the response
    /// cannot be trusted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the identity contract's FIRST gate, the one <see cref="OfficialIdentifier"/> asks
    /// before it consults any family grammar. It is restated here because
    /// <c>FactsValidation.IsOpaqueIdentity</c> is internal to the contracts assembly, and it is
    /// restated in FULL: an earlier version of this producer mirrored only the length and printable
    /// rules, which let an empty literal and a space-padded one through to the typed-exclusion path
    /// and reported a corrupt identity term as an ordinary citation from another scheme.
    /// </para>
    /// <para>
    /// Any divergence fails safe. A value this admits and the constructor still rejects throws from
    /// the constructor, and a throw from there is a whole-delivery refusal.
    /// </para>
    /// </remarks>
    private static void RequireDeliveredLiteralIsAnIdentity(
        RepeatedEnumerationRdfTerm term, string parameterName)
    {
        // Only a delivered LITERAL makes this claim. An unbound term asserts nothing, and an IRI or
        // blank node in this position is caught by the marker comparison that follows.
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal)
        {
            return;
        }

        if (!IsOpaqueIdentityValue(term.Value))
        {
            throw new ArgumentException(
                "An identifier position carries something that is not an identifier at all, so the "
                    + "delivery cannot be trusted.",
                parameterName);
        }
    }

    /// <summary>
    /// One to two hundred printable ASCII characters, non-blank, with no surrounding whitespace.
    /// </summary>
    /// <remarks>
    /// Kept deliberately in the same order and shape as the contract's own rule so the two can be
    /// read against each other. Surrounding whitespace is rejected because two spellings that
    /// differ only in it are one value to a reader and two keys everywhere else, which is the shape
    /// that lets a duplicate hide.
    /// </remarks>
    private static bool IsOpaqueIdentityValue(string? value)
    {
        if (value is null || value.Length is 0 or > 200)
        {
            return false;
        }

        if (value.Trim().Length != value.Length || value.Trim().Length == 0)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is < ' ' or > '~')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the publisher delivered a literal with something in it.
    /// </summary>
    /// <remarks>
    /// This reports an EMPTY literal as unbound, which is why
    /// <see cref="RequireDeliveredLiteralIsAnIdentity"/> runs first: by the time anything asks this
    /// question, a delivered-but-empty identifier term has already refused the delivery, so a false
    /// answer here means the term genuinely was not sent.
    /// </remarks>
    private static bool IsBoundLiteral(RepeatedEnumerationRdfTerm term) =>
        term.Kind == RepeatedEnumerationRdfTermKind.Literal && !string.IsNullOrEmpty(term.Value);

    /// <summary>
    /// The marker the plan's own <c>BIND</c> must have produced for a term of this kind.
    /// </summary>
    /// <remarks>
    /// The markers have a four-valued space — <c>iri</c>, <c>literal</c>,
    /// <c>unsupported_blank_node</c> and <c>unbound</c> — and comparing against the whole of it is
    /// the point. An earlier version asked only "does the marker say unbound", collapsing four
    /// values to one boolean, so a literal term carrying an <c>iri</c> marker agreed with itself and
    /// passed. Half the marker's value space could not contradict anything, which made the
    /// disagreement check weaker than its own name.
    /// </remarks>
    private static string MarkerFor(RepeatedEnumerationRdfTerm term) => term.Kind switch
    {
        RepeatedEnumerationRdfTermKind.Iri => "iri",
        RepeatedEnumerationRdfTermKind.Literal => "literal",
        RepeatedEnumerationRdfTermKind.BlankNode => "unsupported_blank_node",
        RepeatedEnumerationRdfTermKind.Unbound => EuCaseLawDiscoveryPlan.UnboundEcliKind,
        _ => throw new ArgumentOutOfRangeException(nameof(term)),
    };

    /// <summary>
    /// A row whose citation is real but whose case side no delivered identity proves. Separate from
    /// every other refusal because it does not mean the delivery is untrustworthy.
    /// </summary>
    private sealed class CaseSideNotProvableException(string message) : ArgumentException(message);

    private static string RequireIri(RepeatedEnumerationRdfTerm term, string name)
    {
        if (term.Kind != RepeatedEnumerationRdfTermKind.Iri || string.IsNullOrEmpty(term.Value) ||
            term.Datatype is not null || term.Language is not null)
        {
            throw new ArgumentException($"{name} must be a publisher IRI.", name);
        }

        return term.Value;
    }

    private static RepeatedEnumerationRdfTerm Term(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string name)
    {
        for (var ordinal = 0; ordinal < profile.ProjectionVariables.Count; ordinal++)
        {
            if (string.Equals(profile.ProjectionVariables[ordinal], name, StringComparison.Ordinal))
            {
                return row.Terms[ordinal];
            }
        }

        throw new ArgumentException($"The delivery profile does not project {name}.", nameof(profile));
    }
}
