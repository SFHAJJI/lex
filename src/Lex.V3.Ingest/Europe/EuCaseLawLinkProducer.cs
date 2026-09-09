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
        SourceArtifactRef? completionEvidenceRef,
        EuCaseLawLinkProductionRefusal refusal,
        string? detail)
    {
        Relations = relations;
        CompletionEvidenceRef = completionEvidenceRef;
        Refusal = refusal;
        Detail = detail;
    }

    public IReadOnlyList<EuCaseLawLinkRelation>? Relations { get; }
    public SourceArtifactRef? CompletionEvidenceRef { get; }
    public EuCaseLawLinkProductionRefusal Refusal { get; }
    public string? Detail { get; }
    public bool Delivered => Refusal == EuCaseLawLinkProductionRefusal.None;

    internal static EuCaseLawLinkProductionResult Success(
        IReadOnlyList<EuCaseLawLinkRelation> relations,
        SourceArtifactRef completionEvidenceRef) =>
        new(Array.AsReadOnly(relations.ToArray()), completionEvidenceRef,
            EuCaseLawLinkProductionRefusal.None, null);

    internal static EuCaseLawLinkProductionResult Refused(
        EuCaseLawLinkProductionRefusal refusal,
        string detail)
    {
        if (refusal == EuCaseLawLinkProductionRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        return new(null, null, refusal, detail);
    }

    /// <summary>
    /// The admitted links for one exact EU act. A refused run is never readable as an empty set.
    /// </summary>
    public IReadOnlyList<EuCaseLawLinkRelation> ForEuWork(string euWorkUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(euWorkUri);
        if (!Delivered || Relations is null || CompletionEvidenceRef is null)
        {
            throw new InvalidOperationException(
                "A refused case-law production has no admitted links.");
        }

        return Array.AsReadOnly(Relations
            .Where(value => string.Equals(value.EuWorkUri, euWorkUri, StringComparison.Ordinal))
            .ToArray());
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
            catch (ArgumentException exception)
            {
                return EuCaseLawLinkProductionResult.Refused(
                    EuCaseLawLinkProductionRefusal.RowNotAdmitted, exception.Message);
            }
        }

        return EuCaseLawLinkProductionResult.Success(relations, completionEvidenceRef);
    }

    private static EuCaseLawLinkRelation DecodeRow(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string euWorkUri,
        TargetBodyScope targetBodyScope,
        string observationId)
    {
        if (row.Terms.Count != profile.ProjectionVariables.Count || row.Terms.Count != 10)
        {
            throw new ArgumentException("A case-law row has ten exact terms.", nameof(row));
        }

        var caseWorkUri = RequireIri(Term(row, profile, "case_work"), "case_work");

        // The predicate is deliberately NOT re-checked against the pinned set here.
        // EuCaseLawLinkBinding.Create already refuses an unpinned predicate by name and owns that
        // vocabulary; restating the membership test would be a second copy of one rule, free to
        // drift from the copy that actually decides. An unpinned predicate therefore arrives as
        // Create's own ArgumentException and leaves this producer as RowNotAdmitted.
        var predicateUri = RequireIri(Term(row, profile, "case_predicate"), "case_predicate");

        var ecli = RequireEcliFromItsTermNotItsMarker(
            Term(row, profile, "ecli"), Term(row, profile, "ecli_kind"));

        var caseIdentity = new OfficialIdentitySet(
            PublisherId.EuEurLex, [new OfficialIdentifier(FactsIdentifierFamily.Ecli, ecli)]);
        var actIdentity = new OfficialIdentitySet(
            PublisherId.EuEurLex,
            [new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, euWorkUri)]);

        var binding = EuCaseLawLinkBinding.Create(
            caseIdentity, actIdentity, predicateUri, targetBodyScope, [], observationId);
        return new EuCaseLawLinkRelation(caseWorkUri, euWorkUri, predicateUri, binding);
    }

    /// <summary>
    /// Reads the ECLI from the term, and refuses when the plan's marker disagrees with it.
    /// </summary>
    /// <remarks>
    /// The marker cannot be the authority: it is this codebase's own <c>BIND</c> about a row. But a
    /// marker that contradicts its term is not noise either — it means the delivery is not what
    /// either side believes, so the row is refused rather than read past. A case with no ECLI is a
    /// real shape the plan asks for explicitly, and it is refused here for a different reason: E6's
    /// binding cannot prove the case side without one.
    /// </remarks>
    private static string RequireEcliFromItsTermNotItsMarker(
        RepeatedEnumerationRdfTerm ecli, RepeatedEnumerationRdfTerm marker)
    {
        var termIsBoundLiteral =
            ecli.Kind == RepeatedEnumerationRdfTermKind.Literal && !string.IsNullOrEmpty(ecli.Value);
        var markerSaysUnbound = string.Equals(
            marker.Value, EuCaseLawDiscoveryPlan.UnboundEcliKind, StringComparison.Ordinal);

        if (termIsBoundLiteral == markerSaysUnbound)
        {
            throw new ArgumentException(
                "The ecli term and the ecli_kind marker disagree about whether an ECLI was delivered.",
                nameof(ecli));
        }

        if (!termIsBoundLiteral)
        {
            throw new ArgumentException(
                "A case-law link needs the case's own ECLI to prove which side is the case.",
                nameof(ecli));
        }

        return ecli.Value!;
    }

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
