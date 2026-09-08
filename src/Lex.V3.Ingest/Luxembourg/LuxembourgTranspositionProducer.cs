using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Why a delivered Luxembourg query result could not become a Legilux bridge side.</summary>
public enum LuxembourgTranspositionProductionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("query_execution_refused")]
    QueryExecutionRefused = 1,

    [JsonStringEnumMemberName("transposes_family_not_complete")]
    TransposesFamilyNotComplete = 2,

    [JsonStringEnumMemberName("transposes_family_evidence_not_singular")]
    TransposesFamilyEvidenceNotSingular = 3,

    [JsonStringEnumMemberName("relation_not_admitted")]
    RelationNotAdmitted = 4,
}

/// <summary>One Legilux publisher assertion, kept separate from every derived or NIM reading.</summary>
public sealed record LuxembourgTranspositionRelation(
    string EuWorkUri,
    string LegiluxMeasureUri,
    EuTranspositionSourceAcquisition Acquisition);

/// <summary>Completed Legilux acquisitions, or one typed refusal. Never both.</summary>
public sealed class LuxembourgTranspositionProductionResult
{
    private LuxembourgTranspositionProductionResult(
        IReadOnlyList<LuxembourgTranspositionRelation>? relations,
        SourceArtifactRef? completionEvidenceRef,
        LuxembourgTranspositionProductionRefusal refusal,
        string? detail)
    {
        Relations = relations;
        CompletionEvidenceRef = completionEvidenceRef;
        Refusal = refusal;
        Detail = detail;
    }

    public IReadOnlyList<LuxembourgTranspositionRelation>? Relations { get; }
    public SourceArtifactRef? CompletionEvidenceRef { get; }
    public LuxembourgTranspositionProductionRefusal Refusal { get; }
    public string? Detail { get; }
    public bool Delivered => Refusal == LuxembourgTranspositionProductionRefusal.None;

    internal static LuxembourgTranspositionProductionResult Success(
        IReadOnlyList<LuxembourgTranspositionRelation> relations,
        SourceArtifactRef completionEvidenceRef) =>
        new(Array.AsReadOnly(relations.ToArray()), completionEvidenceRef,
            LuxembourgTranspositionProductionRefusal.None, null);

    internal static LuxembourgTranspositionProductionResult Refused(
        LuxembourgTranspositionProductionRefusal refusal,
        string detail)
    {
        if (refusal == LuxembourgTranspositionProductionRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }
        return new(null, null, refusal, detail);
    }

    /// <summary>
    /// Legilux's completed answer for one exact EU work IRI. An empty completed relation set is
    /// a proven absence; a refused producer result is never readable as that absence.
    /// </summary>
    public IReadOnlyList<EuTranspositionSourceAcquisition> ForAssertedEuWork(string euWorkUri)
    {
        if (!Delivered || Relations is null || CompletionEvidenceRef is null)
        {
            throw new InvalidOperationException("A refused production result has no completed Legilux acquisition.");
        }
        var publisherUri = LuxembourgTranspositionProducer.RequirePublisherUri(euWorkUri, nameof(euWorkUri));
        var matches = Relations
            .Where(value => string.Equals(value.EuWorkUri, publisherUri, StringComparison.Ordinal))
            .Select(static value => value.Acquisition)
            .ToArray();
        return matches.Length > 0
            ? Array.AsReadOnly(matches)
            : Array.AsReadOnly(new[]
            {
                new EuTranspositionSourceAcquisition(
                    EuTranspositionAssertedBy.Legilux,
                    EuRelationAcquisitionState.Complete,
                    null,
                    CompletionEvidenceRef),
            });
    }
}

/// <summary>
/// Converts only already-verified, publisher-asserted JOLux <c>transposes</c> rows into the
/// Legilux column of the accepted two-source bridge. It performs no Cellar identity conversion,
/// normalised ELI join, or final bridge construction.
/// </summary>
public static class LuxembourgTranspositionProducer
{
    private const string TransposesPredicate =
        "http://data.legilux.public.lu/resource/ontology/jolux#transposes";

    public static LuxembourgTranspositionProductionResult Produce(LuxembourgQueryExecutionResult execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        if (execution.Refusal is { } queryRefusal)
        {
            return LuxembourgTranspositionProductionResult.Refused(
                LuxembourgTranspositionProductionRefusal.QueryExecutionRefused,
                queryRefusal.Code.ToString() + (queryRefusal.Detail is null ? string.Empty : $": {queryRefusal.Detail}"));
        }

        var families = execution.RelationFamilyAcquisitions.Where(value =>
            string.Equals(value.PredicateIri, TransposesPredicate, StringComparison.Ordinal)).ToArray();
        if (families.Length != 1 ||
            families[0].State != LuxembourgRelationFamilyAcquisitionState.AcquiredComplete)
        {
            return LuxembourgTranspositionProductionResult.Refused(
                LuxembourgTranspositionProductionRefusal.TransposesFamilyNotComplete,
                "The publisher transposes family is missing, ambiguous, or did not complete.");
        }
        var family = families[0];
        if (family.CompletionEvidence is null)
        {
            return LuxembourgTranspositionProductionResult.Refused(
                LuxembourgTranspositionProductionRefusal.TransposesFamilyEvidenceNotSingular,
                "The publisher transposes family has multiple completion proofs, which the accepted bridge side cannot represent as one evidence reference.");
        }

        var evidenceRef = family.CompletionEvidence.AcquisitionRunRef;
        try
        {
            var relations = execution.ResolvedRelations
                .Where(value => string.Equals(value.PredicateIri, TransposesPredicate, StringComparison.Ordinal))
                .Select(value => Decode(value, evidenceRef))
                .ToArray();
            return LuxembourgTranspositionProductionResult.Success(relations, evidenceRef);
        }
        catch (ArgumentException exception)
        {
            return LuxembourgTranspositionProductionResult.Refused(
                LuxembourgTranspositionProductionRefusal.RelationNotAdmitted,
                exception.Message);
        }
    }

    private static LuxembourgTranspositionRelation Decode(
        LuxembourgResolvedRelation relation,
        SourceArtifactRef completionEvidenceRef)
    {
        if (relation.Authority != LuxembourgRelationAuthority.PublisherAsserted ||
            relation.Disposition != LuxembourgRelationDisposition.Accepted ||
            relation.Semantic != LuxembourgRelationSemantic.AssertedRelation)
        {
            throw new ArgumentException("A transposes row must remain an accepted publisher-asserted relation.", nameof(relation));
        }
        var euWorkUri = RequirePublisherUri(relation.ObjectIri, nameof(relation));
        var side = new EuTranspositionSide(
            EuTranspositionAssertedBy.Legilux,
            relation.SubjectIri,
            relation.ObservationRef,
            null,
            null);
        return new LuxembourgTranspositionRelation(
            euWorkUri,
            relation.SubjectIri,
            new EuTranspositionSourceAcquisition(
                EuTranspositionAssertedBy.Legilux,
                EuRelationAcquisitionState.Complete,
                side,
                completionEvidenceRef));
    }

    internal static string RequirePublisherUri(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 4_096 ||
            value.Any(static character => character is < '!' or > '~') ||
            HasInvalidPercentEscape(value) ||
            !(value.StartsWith("http://", StringComparison.Ordinal) ||
              value.StartsWith("https://", StringComparison.Ordinal)) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            string.IsNullOrEmpty(parsed.Host) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment))
        {
            throw new ArgumentException(
                "Publisher identities must be exact absolute HTTP(S) URIs without userinfo, query, or fragment.",
                parameterName);
        }
        return value;
    }

    private static bool HasInvalidPercentEscape(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '%' &&
                (index + 2 >= value.Length ||
                 !Uri.IsHexDigit(value[index + 1]) ||
                 !Uri.IsHexDigit(value[index + 2])))
            {
                return true;
            }
        }
        return false;
    }
}
