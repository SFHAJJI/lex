using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;

namespace Lex.V3.Ingest;

/// <summary>Why an accepted Stage 3 envelope could not prove its two run lineages.</summary>
public enum Stage3EvidenceLineageRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("europe_corpus_record_set_mismatch")]
    EuropeCorpusRecordSetMismatch = 1,

    [JsonStringEnumMemberName("europe_manifest_mismatch")]
    EuropeManifestMismatch = 2,

    [JsonStringEnumMemberName("europe_run_identity_mismatch")]
    EuropeRunIdentityMismatch = 3,

    [JsonStringEnumMemberName("luxembourg_corpus_record_set_mismatch")]
    LuxembourgCorpusRecordSetMismatch = 4,

    [JsonStringEnumMemberName("luxembourg_manifest_mismatch")]
    LuxembourgManifestMismatch = 5,

    [JsonStringEnumMemberName("luxembourg_run_identity_mismatch")]
    LuxembourgRunIdentityMismatch = 6,

}

/// <summary>
/// A construction-only proof that each jurisdiction's accepted result is tied to its own retained
/// scope-manifest bytes, verified manifest identity and verified corpus-set bytes. Europe and
/// Luxembourg remain two separate runs; this type does not mint or imply one shared episode.
/// </summary>
public sealed class Stage3EvidenceLineage
{
    private Stage3EvidenceLineage(Stage3EvidenceEnvelope envelope)
    {
        Envelope = envelope;

        EuropeScopeManifestReceipt = envelope.Europe.ScopeManifestReceipt!;
        EuropeScopeManifestCanonicalSha256 = envelope.Europe.ScopeManifestCanonicalSha256!;
        EuropeCorpusRecordSetRef = envelope.Europe.CorpusRecordSetRef!;
        EuropeManifestRef = envelope.Europe.CorpusRecordSet!.Set.ManifestRef;
        EuropeRunIdentity = envelope.Europe.CorpusRecordSet.Set.RunIdentity;

        LuxembourgScopeManifestReceipt = envelope.Luxembourg.ScopeManifestReceipt!;
        LuxembourgScopeManifestCanonicalSha256 = envelope.Luxembourg.ScopeManifestCanonicalSha256!;
        LuxembourgCorpusRecordSetRef = envelope.Luxembourg.CorpusRecordSetRef!;
        LuxembourgManifestRef = envelope.Luxembourg.CorpusRecordSet!.Set.ManifestRef;
        LuxembourgRunIdentity = envelope.Luxembourg.CorpusRecordSet.Set.RunIdentity;
    }

    public Stage3EvidenceEnvelope Envelope { get; }

    public DurableBlobWriteReceipt EuropeScopeManifestReceipt { get; }

    public string EuropeScopeManifestCanonicalSha256 { get; }

    public SourceArtifactRef EuropeCorpusRecordSetRef { get; }

    public SourceArtifactRef EuropeManifestRef { get; }

    public SourceArtifactRef EuropeRunIdentity { get; }

    public DurableBlobWriteReceipt LuxembourgScopeManifestReceipt { get; }

    public string LuxembourgScopeManifestCanonicalSha256 { get; }

    public SourceArtifactRef LuxembourgCorpusRecordSetRef { get; }

    public SourceArtifactRef LuxembourgManifestRef { get; }

    public SourceArtifactRef LuxembourgRunIdentity { get; }

    public static Stage3EvidenceLineage? TryBind(
        Stage3EvidenceEnvelope envelope,
        out Stage3EvidenceLineageRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!TryBindRun(
                envelope.Europe.CorpusRecordSetRef!,
                envelope.Europe.CorpusRecordSet!,
                envelope.Europe.ScopeManifestCanonicalSha256!,
                envelope.Europe.ScopeManifestReceipt!,
                Stage3EvidenceLineageRefusal.EuropeCorpusRecordSetMismatch,
                Stage3EvidenceLineageRefusal.EuropeManifestMismatch,
                Stage3EvidenceLineageRefusal.EuropeRunIdentityMismatch,
                out refusal,
                out detail))
        {
            return null;
        }

        if (!TryBindRun(
                envelope.Luxembourg.CorpusRecordSetRef!,
                envelope.Luxembourg.CorpusRecordSet!,
                envelope.Luxembourg.ScopeManifestCanonicalSha256!,
                envelope.Luxembourg.ScopeManifestReceipt!,
                Stage3EvidenceLineageRefusal.LuxembourgCorpusRecordSetMismatch,
                Stage3EvidenceLineageRefusal.LuxembourgManifestMismatch,
                Stage3EvidenceLineageRefusal.LuxembourgRunIdentityMismatch,
                out refusal,
                out detail))
        {
            return null;
        }

        refusal = Stage3EvidenceLineageRefusal.None;
        detail = null;
        return new Stage3EvidenceLineage(envelope);
    }

    private static bool TryBindRun(
        SourceArtifactRef corpusRecordSetRef,
        VerifiedCorpusRecordSet corpusRecordSet,
        string scopeManifestCanonicalSha256,
        DurableBlobWriteReceipt scopeManifestReceipt,
        Stage3EvidenceLineageRefusal corpusMismatch,
        Stage3EvidenceLineageRefusal manifestMismatch,
        Stage3EvidenceLineageRefusal runIdentityMismatch,
        out Stage3EvidenceLineageRefusal refusal,
        out string? detail)
    {
        using var bytes = new MemoryStream();
        var canonicalSetSha256 = CorpusRecordSetCanonicalWriter.Write(bytes, corpusRecordSet.Set);
        if (!string.Equals(corpusRecordSetRef.Sha256, canonicalSetSha256, StringComparison.Ordinal))
        {
            refusal = corpusMismatch;
            detail = $"expected {canonicalSetSha256}; found {corpusRecordSetRef.Sha256}";
            return false;
        }

        if (!string.Equals(
                corpusRecordSet.Set.ManifestRef.Sha256,
                scopeManifestCanonicalSha256,
                StringComparison.Ordinal))
        {
            refusal = manifestMismatch;
            detail = $"expected {corpusRecordSet.Set.ManifestRef.Sha256}; found {scopeManifestCanonicalSha256}";
            return false;
        }

        var retainedManifestSha256 = scopeManifestReceipt.Reference.ContentSha256;
        if (!string.Equals(
                corpusRecordSet.Set.RunIdentity.Sha256,
                retainedManifestSha256,
                StringComparison.Ordinal))
        {
            refusal = runIdentityMismatch;
            detail = $"expected {corpusRecordSet.Set.RunIdentity.Sha256}; found {retainedManifestSha256}";
            return false;
        }

        refusal = Stage3EvidenceLineageRefusal.None;
        detail = null;
        return true;
    }
}
