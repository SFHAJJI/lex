using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Europe;

/// <summary>Why two already-completed source columns could not form one transposition bridge.</summary>
public enum EuTranspositionBridgeProductionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("work_kind_not_for_eu_work")]
    WorkKindNotForEuWork = 1,

    [JsonStringEnumMemberName("legilux_not_delivered")]
    LegiluxNotDelivered = 2,

    [JsonStringEnumMemberName("nim_not_delivered")]
    NimNotDelivered = 3,

    [JsonStringEnumMemberName("source_columns_contradict_work_kind")]
    SourceColumnsContradictWorkKind = 4,
}

/// <summary>One two-source bridge, or a typed reason no bridge was produced.</summary>
public sealed class EuTranspositionBridgeProductionResult
{
    private readonly IReadOnlyList<byte[]> _normalisedEliJoinEvidenceBytes;

    private EuTranspositionBridgeProductionResult(
        EuTranspositionBridge? bridge,
        IReadOnlyList<byte[]>? normalisedEliJoinEvidenceBytes,
        EuTranspositionBridgeProductionRefusal refusal,
        string? detail)
    {
        Bridge = bridge;
        _normalisedEliJoinEvidenceBytes = Array.AsReadOnly(
            normalisedEliJoinEvidenceBytes?.Select(static bytes => bytes.ToArray()).ToArray() ?? []);
        Refusal = refusal;
        Detail = detail;
    }

    public EuTranspositionBridge? Bridge { get; }
    public EuTranspositionBridgeProductionRefusal Refusal { get; }
    public string? Detail { get; }
    public bool Delivered => Refusal == EuTranspositionBridgeProductionRefusal.None;

    /// <summary>The canonical evidence bytes for each derived join, in normalised-ELI order.</summary>
    public IReadOnlyList<byte[]> CopyNormalisedEliJoinEvidenceBytes() =>
        Array.AsReadOnly(_normalisedEliJoinEvidenceBytes.Select(static bytes => bytes.ToArray()).ToArray());

    internal static EuTranspositionBridgeProductionResult Success(
        EuTranspositionBridge bridge,
        IReadOnlyList<byte[]>? normalisedEliJoinEvidenceBytes = null) =>
        new(
            bridge ?? throw new ArgumentNullException(nameof(bridge)),
            normalisedEliJoinEvidenceBytes,
            EuTranspositionBridgeProductionRefusal.None,
            null);

    internal static EuTranspositionBridgeProductionResult Refused(
        EuTranspositionBridgeProductionRefusal refusal,
        string detail)
    {
        if (refusal == EuTranspositionBridgeProductionRefusal.None)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal));
        }

        return new(null, [], refusal, detail);
    }
}

/// <summary>
/// Assembles exactly one already-produced Legilux column and one already-produced NIM column for
/// one EU work. When both rows name the same admitted Legilux ELI, it derives a disclosed join and
/// retains canonical evidence bytes binding both source references without changing either column.
/// </summary>
public static class EuTranspositionBridgeProducer
{
    public static EuTranspositionBridgeProductionResult Produce(
        string euWorkUri,
        EuWorkKindAssertion workKindAssertion,
        LuxembourgTranspositionProductionResult legilux,
        EuNationalImplementingMeasureProductionResult nim)
    {
        ArgumentNullException.ThrowIfNull(workKindAssertion);
        ArgumentNullException.ThrowIfNull(legilux);
        ArgumentNullException.ThrowIfNull(nim);
        try
        {
            _ = new OfficialIdentitySet(
                PublisherId.EuEurLex,
                [new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, euWorkUri)]);
        }
        catch (ArgumentException exception)
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.WorkKindNotForEuWork, exception.Message);
        }

        if (workKindAssertion.Work.Publisher != PublisherId.EuEurLex ||
            !string.Equals(
                workKindAssertion.Work.Value(FactsIdentifierFamily.CellarWorkUri),
                euWorkUri,
                StringComparison.Ordinal))
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.WorkKindNotForEuWork,
                "The work-kind assertion does not name this exact EU Cellar work.");
        }

        if (nim.Relations is not null && nim.Relations.Any(relation =>
                string.Equals(relation.EuWorkUri, euWorkUri, StringComparison.Ordinal) &&
                relation.Acquisition.Sides.Count > 0 &&
                relation.WorkKindAssertion.Kind != workKindAssertion.Kind))
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.SourceColumnsContradictWorkKind,
                "The NIM relation work kind contradicts the bridge work-kind assertion.");
        }

        EuTranspositionSourceAcquisition legiluxColumn;
        try
        {
            legiluxColumn = legilux.ForAssertedEuWork(euWorkUri);
        }
        catch (InvalidOperationException exception)
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.LegiluxNotDelivered, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.SourceColumnsContradictWorkKind, exception.Message);
        }

        EuTranspositionSourceAcquisition nimColumn;
        try
        {
            nimColumn = nim.ForEuWork(euWorkUri);
        }
        catch (InvalidOperationException exception)
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.NimNotDelivered, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.SourceColumnsContradictWorkKind, exception.Message);
        }

        try
        {
            var joins = BuildNormalisedEliJoins(euWorkUri, legilux, nim);
            return EuTranspositionBridgeProductionResult.Success(new EuTranspositionBridge(
                euWorkUri,
                workKindAssertion.Kind,
                EuTranspositionBridge.TransposabilityFor(workKindAssertion.Kind),
                legiluxColumn,
                nimColumn,
                joins.Select(static join => join.Join).ToArray()),
                joins.Select(static join => join.EvidenceBytes).ToArray());
        }
        catch (ArgumentException exception)
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.SourceColumnsContradictWorkKind, exception.Message);
        }
    }

    private static IReadOnlyList<NormalisedEliJoinBuild> BuildNormalisedEliJoins(
        string euWorkUri,
        LuxembourgTranspositionProductionResult legilux,
        EuNationalImplementingMeasureProductionResult nim)
    {
        var legiluxRelations = legilux.Relations!
            .Where(value => string.Equals(value.EuWorkUri, euWorkUri, StringComparison.Ordinal))
            .ToArray();
        var nimRelations = nim.Relations!
            .Where(value => string.Equals(value.EuWorkUri, euWorkUri, StringComparison.Ordinal))
            .ToArray();
        if (legiluxRelations.Any(static value => value.Acquisition.Sides.Count > 1) ||
            nimRelations.Any(static value => value.Acquisition.Sides.Count > 1))
        {
            throw new ArgumentException("One source relation cannot carry more than one publisher assertion.");
        }

        var legiluxByEli = legiluxRelations
            .Where(static value => value.Acquisition.Sides.Count == 1)
            .Select(value => (Relation: value, Eli: NormaliseLegiluxEli(value.LegiluxMeasureUri)))
            .Where(static value => value.Eli is not null)
            .ToLookup(static value => value.Eli!, StringComparer.Ordinal);
        var nimByEli = nimRelations
            .Where(static value => value.Acquisition.Sides.Count == 1 && value.LegiluxEli is not null)
            .Select(value => (Relation: value, Eli: NormaliseLegiluxEli(value.LegiluxEli!)))
            .Where(static value => value.Eli is not null)
            .ToLookup(static value => value.Eli!, StringComparer.Ordinal);

        var builds = new List<NormalisedEliJoinBuild>();
        foreach (var normalisedEli in legiluxByEli.Select(static group => group.Key)
                     .Intersect(nimByEli.Select(static group => group.Key), StringComparer.Ordinal)
                     .OrderBy(static value => value, StringComparer.Ordinal))
        {
            var legiluxMatches = legiluxByEli[normalisedEli].ToArray();
            var nimMatches = nimByEli[normalisedEli]
                .OrderBy(static value => value.Relation.NimWorkUri, StringComparer.Ordinal)
                .ThenBy(static value => value.Relation.NimCelex, StringComparer.Ordinal)
                .ThenBy(static value => value.Relation.ImplementsPredicateIri, StringComparer.Ordinal)
                .ThenBy(static value => value.Relation.Acquisition.Sides[0].EvidenceRef.ResourceId, StringComparer.Ordinal)
                .ThenBy(static value => value.Relation.Acquisition.Sides[0].EvidenceRef.Sha256, StringComparer.Ordinal)
                .ToArray();
            if (legiluxMatches.Length != 1 || nimMatches.Length == 0 ||
                legiluxMatches[0].Relation.Acquisition.Sides.Count != 1 ||
                nimMatches.Any(static value => value.Relation.Acquisition.Sides.Count != 1))
            {
                throw new ArgumentException(
                    $"The normalised ELI {normalisedEli} is ambiguous within a publisher column.");
            }

            var legiluxRelation = legiluxMatches[0].Relation;
            if (legiluxRelation.EuWorkIdentityEvidenceRef is null)
            {
                throw new ArgumentException(
                    "A Legilux local target cannot become a Cellar work without retained identity evidence.");
            }
            var evidenceBytes = nimMatches.Length == 1
                ? WriteJoinEvidence(
                    euWorkUri,
                    normalisedEli,
                    legiluxRelation.LegiluxMeasureUri,
                    legiluxRelation.Acquisition.Sides[0].EvidenceRef,
                    legiluxRelation.EuWorkIdentityEvidenceRef,
                    nimMatches[0].Relation.LegiluxEli!,
                    nimMatches[0].Relation.NimWorkUri,
                    nimMatches[0].Relation.NimCelex,
                    nimMatches[0].Relation.ImplementsPredicateIri,
                    nimMatches[0].Relation.Acquisition.Sides[0].EvidenceRef)
                : WriteAggregateJoinEvidence(
                    euWorkUri,
                    normalisedEli,
                    legiluxRelation,
                    nimMatches.Select(static value => value.Relation).ToArray());
            var digest = Convert.ToHexStringLower(SHA256.HashData(evidenceBytes));
            var evidenceIdentityFamily = nimMatches.Length == 1
                ? "lex-v3/eu-transposition-normalised-eli-join/1"
                : "lex-v3/eu-transposition-normalised-eli-join/2";
            var evidenceRef = new SourceArtifactRef(
                ContentDerivedIdentity.DeriveUuidUrn(
                    evidenceIdentityFamily,
                    evidenceBytes),
                digest);
            builds.Add(new NormalisedEliJoinBuild(
                new EuNormalisedEliJoin(normalisedEli, evidenceRef), evidenceBytes));
        }
        return Array.AsReadOnly(builds.ToArray());
    }

    private static string? NormaliseLegiluxEli(string value)
    {
        const string httpPrefix = "http://data.legilux.public.lu/eli/";
        const string httpsPrefix = "https://data.legilux.public.lu/eli/";
        var suffix = value.StartsWith(httpsPrefix, StringComparison.Ordinal)
            ? value[httpsPrefix.Length..]
            : value.StartsWith(httpPrefix, StringComparison.Ordinal)
                ? value[httpPrefix.Length..]
                : null;
        if (string.IsNullOrEmpty(suffix) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        return httpsPrefix + suffix;
    }

    private static byte[] WriteJoinEvidence(
        string euWorkUri,
        string normalisedEli,
        string legiluxEli,
        SourceArtifactRef legiluxEvidenceRef,
        SourceArtifactRef legiluxIdentityEvidenceRef,
        string nimEli,
        string nimWorkUri,
        string nimCelex,
        string implementsPredicateIri,
        SourceArtifactRef nimEvidenceRef)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "eu_transposition_normalised_eli_join_evidence/1");
            writer.WriteString("eu_work_uri", euWorkUri);
            writer.WriteString("normalised_eli", normalisedEli);
            writer.WriteString("legilux_eli", legiluxEli);
            writer.WriteString("legilux_evidence_resource_id", legiluxEvidenceRef.ResourceId);
            writer.WriteString("legilux_evidence_sha256", legiluxEvidenceRef.Sha256);
            writer.WriteString("legilux_identity_evidence_resource_id", legiluxIdentityEvidenceRef.ResourceId);
            writer.WriteString("legilux_identity_evidence_sha256", legiluxIdentityEvidenceRef.Sha256);
            writer.WriteString("nim_eli", nimEli);
            writer.WriteString("nim_work_uri", nimWorkUri);
            writer.WriteString("nim_celex", nimCelex);
            writer.WriteString("implements_predicate_iri", implementsPredicateIri);
            writer.WriteString("nim_evidence_resource_id", nimEvidenceRef.ResourceId);
            writer.WriteString("nim_evidence_sha256", nimEvidenceRef.Sha256);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static byte[] WriteAggregateJoinEvidence(
        string euWorkUri,
        string normalisedEli,
        LuxembourgTranspositionRelation legiluxRelation,
        IReadOnlyList<EuNationalImplementingMeasureRelation> nimRelations)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "eu_transposition_normalised_eli_join_evidence/2");
            writer.WriteString("eu_work_uri", euWorkUri);
            writer.WriteString("normalised_eli", normalisedEli);
            writer.WriteString("legilux_eli", legiluxRelation.LegiluxMeasureUri);
            writer.WriteString(
                "legilux_evidence_resource_id",
                legiluxRelation.Acquisition.Sides[0].EvidenceRef.ResourceId);
            writer.WriteString(
                "legilux_evidence_sha256",
                legiluxRelation.Acquisition.Sides[0].EvidenceRef.Sha256);
            writer.WriteString(
                "legilux_identity_evidence_resource_id",
                legiluxRelation.EuWorkIdentityEvidenceRef!.ResourceId);
            writer.WriteString(
                "legilux_identity_evidence_sha256",
                legiluxRelation.EuWorkIdentityEvidenceRef.Sha256);
            writer.WriteStartArray("nim_assertions");
            foreach (var relation in nimRelations)
            {
                writer.WriteStartObject();
                writer.WriteString("nim_eli", relation.LegiluxEli);
                writer.WriteString("nim_work_uri", relation.NimWorkUri);
                writer.WriteString("nim_celex", relation.NimCelex);
                writer.WriteString("implements_predicate_iri", relation.ImplementsPredicateIri);
                writer.WriteString(
                    "nim_evidence_resource_id",
                    relation.Acquisition.Sides[0].EvidenceRef.ResourceId);
                writer.WriteString(
                    "nim_evidence_sha256",
                    relation.Acquisition.Sides[0].EvidenceRef.Sha256);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private sealed record NormalisedEliJoinBuild(EuNormalisedEliJoin Join, byte[] EvidenceBytes);
}
