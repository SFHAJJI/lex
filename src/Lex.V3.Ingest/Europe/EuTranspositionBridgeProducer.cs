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

    [JsonStringEnumMemberName("legilux_not_singular")]
    LegiluxNotSingular = 4,

    [JsonStringEnumMemberName("nim_not_singular")]
    NimNotSingular = 5,

    [JsonStringEnumMemberName("source_columns_contradict_work_kind")]
    SourceColumnsContradictWorkKind = 6,
}

/// <summary>One two-source bridge, or a typed reason no bridge was produced.</summary>
public sealed class EuTranspositionBridgeProductionResult
{
    private readonly byte[]? _normalisedEliJoinEvidenceBytes;

    private EuTranspositionBridgeProductionResult(
        EuTranspositionBridge? bridge,
        byte[]? normalisedEliJoinEvidenceBytes,
        EuTranspositionBridgeProductionRefusal refusal,
        string? detail)
    {
        Bridge = bridge;
        _normalisedEliJoinEvidenceBytes = normalisedEliJoinEvidenceBytes?.ToArray();
        Refusal = refusal;
        Detail = detail;
    }

    public EuTranspositionBridge? Bridge { get; }
    public EuTranspositionBridgeProductionRefusal Refusal { get; }
    public string? Detail { get; }
    public bool Delivered => Refusal == EuTranspositionBridgeProductionRefusal.None;

    /// <summary>The canonical derived-join evidence bytes, or null when no join was established.</summary>
    public byte[]? CopyNormalisedEliJoinEvidenceBytes() =>
        _normalisedEliJoinEvidenceBytes?.ToArray();

    internal static EuTranspositionBridgeProductionResult Success(
        EuTranspositionBridge bridge,
        byte[]? normalisedEliJoinEvidenceBytes = null) =>
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

        return new(null, null, refusal, detail);
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

        IReadOnlyList<EuTranspositionSourceAcquisition> legiluxColumns;
        try
        {
            legiluxColumns = legilux.ForAssertedEuWork(euWorkUri);
        }
        catch (InvalidOperationException exception)
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.LegiluxNotDelivered, exception.Message);
        }

        if (legiluxColumns.Count != 1)
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.LegiluxNotSingular,
                "The accepted bridge has one Legilux column and cannot choose or merge multiple assertions.");
        }

        IReadOnlyList<EuTranspositionSourceAcquisition> nimColumns;
        try
        {
            nimColumns = nim.ForEuWork(euWorkUri);
        }
        catch (InvalidOperationException exception)
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.NimNotDelivered, exception.Message);
        }

        if (nimColumns.Count != 1)
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.NimNotSingular,
                "The accepted bridge has one NIM column and cannot choose or merge multiple assertions.");
        }

        try
        {
            var join = BuildNormalisedEliJoin(euWorkUri, legilux, nim);
            return EuTranspositionBridgeProductionResult.Success(new EuTranspositionBridge(
                euWorkUri,
                workKindAssertion.Kind,
                EuTranspositionBridge.TransposabilityFor(workKindAssertion.Kind),
                legiluxColumns[0],
                nimColumns[0],
                join?.Join),
                join?.EvidenceBytes);
        }
        catch (ArgumentException exception)
        {
            return EuTranspositionBridgeProductionResult.Refused(
                EuTranspositionBridgeProductionRefusal.SourceColumnsContradictWorkKind, exception.Message);
        }
    }

    private static NormalisedEliJoinBuild? BuildNormalisedEliJoin(
        string euWorkUri,
        LuxembourgTranspositionProductionResult legilux,
        EuNationalImplementingMeasureProductionResult nim)
    {
        var legiluxRelation = legilux.Relations!
            .SingleOrDefault(value => string.Equals(value.EuWorkUri, euWorkUri, StringComparison.Ordinal));
        var nimRelation = nim.Relations!
            .SingleOrDefault(value => string.Equals(value.EuWorkUri, euWorkUri, StringComparison.Ordinal));
        if (legiluxRelation?.Acquisition.Side is null ||
            nimRelation?.Acquisition.Side is null ||
            nimRelation.LegiluxEli is null)
        {
            return null;
        }

        var legiluxEli = NormaliseLegiluxEli(legiluxRelation.LegiluxMeasureUri);
        var nimEli = NormaliseLegiluxEli(nimRelation.LegiluxEli);
        if (legiluxEli is null || !string.Equals(legiluxEli, nimEli, StringComparison.Ordinal))
        {
            return null;
        }

        var evidenceBytes = WriteJoinEvidence(
            euWorkUri,
            legiluxEli,
            legiluxRelation.LegiluxMeasureUri,
            legiluxRelation.Acquisition.Side!.EvidenceRef,
            nimRelation.LegiluxEli,
            nimRelation.NimWorkUri,
            nimRelation.NimCelex,
            nimRelation.ImplementsPredicateIri,
            nimRelation.Acquisition.Side!.EvidenceRef);
        var digest = Convert.ToHexStringLower(SHA256.HashData(evidenceBytes));
        var evidenceRef = new SourceArtifactRef(
            ContentDerivedIdentity.DeriveUuidUrn(
                "lex-v3/eu-transposition-normalised-eli-join/1",
                evidenceBytes),
            digest);
        return new NormalisedEliJoinBuild(
            new EuNormalisedEliJoin(legiluxEli, evidenceRef),
            evidenceBytes);
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

    private sealed record NormalisedEliJoinBuild(EuNormalisedEliJoin Join, byte[] EvidenceBytes);
}
