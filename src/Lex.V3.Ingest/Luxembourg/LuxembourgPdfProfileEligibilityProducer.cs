using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>The evidence-bound PDF family selected for one held Luxembourg body.</summary>
public enum LuxembourgPdfProfileEligibilityDisposition
{
    [JsonStringEnumMemberName("not_pdf")]
    NotPdf = 1,

    [JsonStringEnumMemberName("publisher_pdf_eligible")]
    PublisherPdfEligible = 2,

    [JsonStringEnumMemberName("gazette_pdf_eligible")]
    GazettePdfEligible = 3,

    [JsonStringEnumMemberName("typed_gap")]
    TypedGap = 4,
}

/// <summary>Why one PDF body could not enter either reviewed profile family.</summary>
public enum LuxembourgPdfProfileEligibilityGapReason
{
    [JsonStringEnumMemberName("gazette_receipt_mismatch")]
    GazetteReceiptMismatch = 1,
}

/// <summary>
/// One held Luxembourg body classified only far enough to select the publisher-PDF or Gazette-PDF
/// rule family. It does not choose a rule version, inspect layout, or emit text.
/// </summary>
public sealed class LuxembourgPdfProfileEligibilityOutcome
{
    internal LuxembourgPdfProfileEligibilityOutcome(
        LuxembourgHeldBodyDerivationInput input,
        LuxembourgPdfProfileEligibilityDisposition disposition,
        LuxembourgPdfProfileEligibilityGapReason? gapReason,
        LuxembourgGazetteBodyDisposition? gazetteEvidence,
        string ruleProfileSha256)
    {
        Input = input;
        Disposition = disposition;
        GapReason = gapReason;
        GazetteEvidence = gazetteEvidence;
        TransportReceipt = input.Receipt;
        PublisherExpressionIri = input.SelectedWemiCandidate.ExpressionIri;
        PublisherManifestationIri = input.SelectedWemiCandidate.ManifestationIri;
        PublisherItemIri = input.SelectedWemiCandidate.ItemIri;
        RuleProfileSha256 = ruleProfileSha256;
        SemanticIdentitySha256 = Digest(string.Join(
            '\n',
            "lex-v3-luxembourg-pdf-profile-eligibility-outcome/1",
            ruleProfileSha256,
            input.SelectedWemiCandidate.RootIri,
            PublisherExpressionIri,
            PublisherManifestationIri,
            PublisherItemIri,
            input.SelectedWemiCandidate.LanguageIri,
            input.SelectedWemiCandidate.FormatIri,
            ((int)disposition).ToString(System.Globalization.CultureInfo.InvariantCulture),
            gapReason is null
                ? string.Empty
                : ((int)gapReason.Value).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    public LuxembourgHeldBodyDerivationInput Input { get; }

    public string PublisherExpressionIri { get; }

    public string PublisherManifestationIri { get; }

    public string PublisherItemIri { get; }

    public LuxembourgPdfProfileEligibilityDisposition Disposition { get; }

    public LuxembourgPdfProfileEligibilityGapReason? GapReason { get; }

    /// <summary>The exact admitted Gazette disposition, present only for Gazette-PDF eligibility.</summary>
    public LuxembourgGazetteBodyDisposition? GazetteEvidence { get; }

    /// <summary>The exact retained receipt from the held-body population; provenance, not identity.</summary>
    public DurableBlobWriteReceipt TransportReceipt { get; }

    public string RuleProfileSha256 { get; }

    /// <summary>Stable WEMI and rule-family identity. Run and receipt metadata are excluded.</summary>
    public string SemanticIdentitySha256 { get; }

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>
/// Exactly one PDF-family eligibility outcome per held Luxembourg body, preserving the source
/// population's already-verified ordinal order.
/// </summary>
public sealed class LuxembourgPdfProfileEligibilityPopulation
{
    internal LuxembourgPdfProfileEligibilityPopulation(
        Stage3BodyComposition sourceComposition,
        IReadOnlyList<LuxembourgPdfProfileEligibilityOutcome> outcomes,
        string ruleProfileSha256)
    {
        SourceComposition = sourceComposition;
        Outcomes = Array.AsReadOnly(outcomes.ToArray());
        RuleProfileSha256 = ruleProfileSha256;
        IdentitySha256 = Digest(string.Join(
            '\n',
            new[]
            {
                "lex-v3-luxembourg-pdf-profile-eligibility-population/1",
                ruleProfileSha256,
            }.Concat(Outcomes.Select(static outcome => outcome.SemanticIdentitySha256))));
    }

    public Stage3BodyComposition SourceComposition { get; }

    public IReadOnlyList<LuxembourgPdfProfileEligibilityOutcome> Outcomes { get; }

    public string RuleProfileSha256 { get; }

    public string IdentitySha256 { get; }

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>
/// Selects only the evidence-backed Luxembourg PDF profile family from the proof-bound Stage 3
/// composition. No caller-supplied package or profile map is accepted.
/// </summary>
public static class LuxembourgPdfProfileEligibilityProducer
{
    public static string RuleProfileSha256 { get; } = Digest(RuleProfileText());

    public static LuxembourgPdfProfileEligibilityPopulation Produce(Stage3BodyComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        var gazetteBodies = composition.Luxembourg
            .SelectMany(static value => value.GazetteBodies.Bodies)
            .ToArray();
        var outcomes = composition.LuxembourgDerivationPopulation.Inputs
            .Select(input => Classify(input, gazetteBodies))
            .ToArray();
        return new LuxembourgPdfProfileEligibilityPopulation(
            composition, outcomes, RuleProfileSha256);
    }

    private static LuxembourgPdfProfileEligibilityOutcome Classify(
        LuxembourgHeldBodyDerivationInput input,
        IReadOnlyList<LuxembourgGazetteBodyDisposition> gazetteBodies)
    {
        if (input.Address.UserFormatToken is not
            (LuxembourgUserFormatToken.Pdf or LuxembourgUserFormatToken.PdfA))
        {
            return Outcome(input, LuxembourgPdfProfileEligibilityDisposition.NotPdf);
        }

        var selected = input.SelectedWemiCandidate;
        var gazette = gazetteBodies.SingleOrDefault(body =>
                body.RetainedTransportBytes is not null &&
                Same(body.Candidate.WemiCandidate.RootIri, selected.RootIri) &&
                Same(body.Candidate.WemiCandidate.ExpressionIri, selected.ExpressionIri) &&
                Same(body.Candidate.WemiCandidate.ManifestationIri, selected.ManifestationIri) &&
                Same(body.Candidate.WemiCandidate.ItemIri, selected.ItemIri) &&
                Same(body.Candidate.WemiCandidate.LanguageIri, selected.LanguageIri) &&
                Same(body.Candidate.WemiCandidate.FormatIri, selected.FormatIri));
        if (gazette is null)
        {
            return Outcome(input, LuxembourgPdfProfileEligibilityDisposition.PublisherPdfEligible);
        }

        if (gazette.RetainedTransportBytes != input.Receipt)
        {
            return Outcome(
                input,
                LuxembourgPdfProfileEligibilityDisposition.TypedGap,
                LuxembourgPdfProfileEligibilityGapReason.GazetteReceiptMismatch);
        }

        return Outcome(
            input,
            LuxembourgPdfProfileEligibilityDisposition.GazettePdfEligible,
            gazetteEvidence: gazette);
    }

    private static LuxembourgPdfProfileEligibilityOutcome Outcome(
        LuxembourgHeldBodyDerivationInput input,
        LuxembourgPdfProfileEligibilityDisposition disposition,
        LuxembourgPdfProfileEligibilityGapReason? gapReason = null,
        LuxembourgGazetteBodyDisposition? gazetteEvidence = null) =>
        new(input, disposition, gapReason, gazetteEvidence, RuleProfileSha256);

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    private static string RuleProfileText() =>
        "lex-v3-luxembourg-pdf-profile-eligibility-rule/1\n" +
        "pdf=selected-wemi-format-pdf-or-pdfa\n" +
        "gazette=exact-admitted-act-expression-manifestation-item-and-retained-receipt\n" +
        "exact-gazette-without-retained-bytes=publisher-pdf-family\n" +
        "no-admitted-match=publisher-pdf-family\n" +
        "layout=not-inspected\n";

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
