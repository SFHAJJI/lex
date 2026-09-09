using System.Text.Json.Serialization;
using Lex.V3.Contracts;
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

    internal static EuCaseLawLinkProductionResult Success(
        IReadOnlyList<EuCaseLawLinkRelation> relations,
        IReadOnlyList<EuCaseLawUnrepresentableRow> unrepresentableRows,
        IReadOnlySet<string> actsAskedAbout,
        SourceArtifactRef completionEvidenceRef) =>
        new(Array.AsReadOnly(relations.ToArray()),
            Array.AsReadOnly(unrepresentableRows.ToArray()), actsAskedAbout, completionEvidenceRef,
            EuCaseLawLinkProductionRefusal.None, null);

    internal static EuCaseLawLinkProductionResult Refused(
        EuCaseLawLinkProductionRefusal refusal,
        string detail)
    {
        if (refusal == EuCaseLawLinkProductionRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        return new(null, null, null, null, refusal, detail);
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
public static class EuCaseLawLinkProducer
{
    /// <summary>
    /// Decodes one delivered page set into bindings.
    /// </summary>
    /// <param name="actBodyScopes">
    /// Each act's own body scope, keyed by its Cellar work URI. A row naming an act absent from this
    /// map is refused rather than defaulted.
    /// </param>
    public static EuCaseLawLinkProductionResult DecodeRows(
        IReadOnlyList<RepeatedEnumerationRow> rows,
        RepeatedEnumerationInterpretationProfile profile,
        IReadOnlyDictionary<string, TargetBodyScope> actBodyScopes,
        SourceArtifactRef completionEvidenceRef)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(actBodyScopes);
        ArgumentNullException.ThrowIfNull(completionEvidenceRef);

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

            if (!actBodyScopes.TryGetValue(euWorkUri, out var scope))
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

        return EuCaseLawLinkProductionResult.Success(
            relations, unrepresentable, actBodyScopes.Keys.ToHashSet(StringComparer.Ordinal),
            completionEvidenceRef);
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

        // The predicate is deliberately NOT re-checked against the pinned set here.
        // EuCaseLawLinkBinding.Create already refuses an unpinned predicate by name and owns that
        // vocabulary; restating the membership test would be a second copy of one rule, free to
        // drift from the copy that actually decides. An unpinned predicate therefore arrives as
        // Create's own ArgumentException and leaves this producer as RowNotAdmitted.
        var predicateUri = RequireIri(Term(row, profile, "case_predicate"), "case_predicate");

        var caseIdentifier = RequireCaseIdentityFromItsTermsNotItsMarkers(
            Term(row, profile, "ecli"),
            Term(row, profile, "ecli_kind"),
            Term(row, profile, "case_celex"),
            Term(row, profile, "case_celex_kind"));

        var caseIdentity = new OfficialIdentitySet(PublisherId.EuEurLex, [caseIdentifier]);
        var actIdentity = new OfficialIdentitySet(
            PublisherId.EuEurLex,
            [new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, euWorkUri)]);

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
