using System.Globalization;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest;

/// <summary>Why the proof-bound Stage 3 body evidence could not be composed.</summary>
public enum Stage3BodyCompositionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("luxembourg_body_set_outside_corpus")]
    LuxembourgBodySetOutsideCorpus = 1,

    [JsonStringEnumMemberName("luxembourg_body_set_object_mismatch")]
    LuxembourgBodySetObjectMismatch = 2,
}

/// <summary>
/// One proof-complete EU Formex classification beside the three exact corpus records that carry
/// its custody evidence. Member outcomes and gaps remain ordered and unmodified.
/// </summary>
public sealed class Stage3EuropeBodyComposition
{
    internal Stage3EuropeBodyComposition(EuBoundAnnexBodyClassification classification)
    {
        Classification = classification;
        FormexCustody = classification.Binding.FormexSource;
        XhtmlCustody = classification.Binding.XhtmlSource;
        PdfCustody = classification.Binding.PdfSource;
        Annexes = classification.Members;
    }

    public EuBoundAnnexBodyClassification Classification { get; }

    public CorpusRecord FormexCustody { get; }

    public CorpusRecord XhtmlCustody { get; }

    public CorpusRecord PdfCustody { get; }

    /// <summary>
    /// The classifier's exact ordered <c>(outcome?, gap)</c> values. This is an image-only
    /// determination, not a claim that the general admitted/rejected vocabulary was exercised.
    /// </summary>
    public IReadOnlyList<EuBoundAnnexBodyMemberClassification> Annexes { get; }
}

/// <summary>One Luxembourg act's corpus custody record and its exact Gazette body set.</summary>
public sealed class Stage3LuxembourgBodyComposition
{
    internal Stage3LuxembourgBodyComposition(
        CorpusRecord custody,
        LuxembourgGazetteBodySet gazetteBodies)
    {
        Custody = custody;
        GazetteBodies = gazetteBodies;
    }

    public CorpusRecord Custody { get; }

    public LuxembourgGazetteBodySet GazetteBodies { get; }
}

/// <summary>
/// Construction-only body composition over one accepted Stage 3 evidence envelope.
/// </summary>
/// <remarks>
/// This is not the larger signed <c>lex-corpus/6</c> artifact and has no serializer or schema id.
/// It preserves custody and admissibility as separate axes so a later accepted corpus contract can
/// consume proof-bearing inputs without flattening typed gaps or manufacturing EU admission.
/// </remarks>
public sealed class Stage3BodyComposition
{
    private Stage3BodyComposition(
        Stage3EvidenceEnvelope envelope,
        IReadOnlyList<Stage3EuropeBodyComposition> europe,
        IReadOnlyList<Stage3LuxembourgBodyComposition> luxembourg)
    {
        Envelope = envelope;
        Europe = Array.AsReadOnly(europe.ToArray());
        Luxembourg = Array.AsReadOnly(luxembourg.ToArray());
    }

    public Stage3EvidenceEnvelope Envelope { get; }

    public IReadOnlyList<Stage3EuropeBodyComposition> Europe { get; }

    public IReadOnlyList<Stage3LuxembourgBodyComposition> Luxembourg { get; }

    public static Stage3BodyComposition? TryCreate(
        Stage3EvidenceEnvelope envelope,
        out Stage3BodyCompositionRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        refusal = Stage3BodyCompositionRefusal.None;
        detail = null;

        var europe = envelope.FormexAnnexClassifications.Classifications
            .Select(static classification => new Stage3EuropeBodyComposition(classification))
            .ToArray();

        var recordsByOrdinal = envelope.Luxembourg.CorpusRecordSet!.Set.Records
            .GroupBy(static record => record.ObjectOrdinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        var luxembourg = new List<Stage3LuxembourgBodyComposition>();
        foreach (var pair in envelope.Luxembourg.GazetteBodySetsByOrdinal!.OrderBy(static pair => pair.Key))
        {
            if (!recordsByOrdinal.TryGetValue(pair.Key, out var matches) || matches.Length != 1)
            {
                refusal = Stage3BodyCompositionRefusal.LuxembourgBodySetOutsideCorpus;
                detail = pair.Key.ToString(CultureInfo.InvariantCulture);
                return null;
            }

            var record = matches[0];
            if (!string.Equals(
                    record.ObjectRef.PublisherUri,
                    pair.Value.PublisherActIri,
                    StringComparison.Ordinal))
            {
                refusal = Stage3BodyCompositionRefusal.LuxembourgBodySetObjectMismatch;
                detail = pair.Value.PublisherActIri;
                return null;
            }

            luxembourg.Add(new Stage3LuxembourgBodyComposition(record, pair.Value));
        }

        return new Stage3BodyComposition(envelope, europe, luxembourg);
    }
}
