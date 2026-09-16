using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using UglyToad.PdfPig;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>The structural PDF evidence retained for one eligibility member.</summary>
public enum LuxembourgPdfLayoutEvidenceDisposition
{
    [JsonStringEnumMemberName("not_applicable")]
    NotApplicable = 1,

    [JsonStringEnumMemberName("admitted")]
    Admitted = 2,

    [JsonStringEnumMemberName("typed_gap")]
    TypedGap = 3,
}

/// <summary>Why one member has no admitted PDF layout evidence.</summary>
public enum LuxembourgPdfLayoutEvidenceGapReason
{
    [JsonStringEnumMemberName("upstream_eligibility_gap")]
    UpstreamEligibilityGap = 1,

    [JsonStringEnumMemberName("pdf_unreadable")]
    PdfUnreadable = 2,

    [JsonStringEnumMemberName("no_text_glyphs")]
    NoTextGlyphs = 3,

    [JsonStringEnumMemberName("invalid_glyph_geometry")]
    InvalidGlyphGeometry = 4,
}

/// <summary>One glyph in PDF content-stream order, with its physical-page geometry.</summary>
public sealed record LuxembourgPdfGlyphEvidence(
    int PhysicalPageNumber,
    int GlyphOrdinal,
    string Text,
    double Left,
    double Bottom,
    double Right,
    double Top,
    double FontSize,
    string FontName,
    int TextOrientation);

/// <summary>
/// One eligibility member's exact structural evidence. Glyphs are evidence only: this type makes
/// no claim about reading order, OCR, legal wording, columns, markers, footnotes or citations.
/// </summary>
public sealed class LuxembourgPdfLayoutEvidenceOutcome
{
    internal LuxembourgPdfLayoutEvidenceOutcome(
        LuxembourgPdfProfileEligibilityOutcome sourceEligibility,
        LuxembourgPdfLayoutEvidenceDisposition disposition,
        LuxembourgPdfLayoutEvidenceGapReason? gapReason,
        IReadOnlyList<LuxembourgPdfGlyphEvidence> glyphs,
        string ruleProfileSha256)
    {
        SourceEligibility = sourceEligibility;
        Disposition = disposition;
        GapReason = gapReason;
        Glyphs = Array.AsReadOnly(glyphs.ToArray());
        TransportReceipt = sourceEligibility.TransportReceipt;
        RuleProfileSha256 = ruleProfileSha256;
        SemanticIdentitySha256 = IdentityOf(this);
    }

    public LuxembourgPdfProfileEligibilityOutcome SourceEligibility { get; }

    public LuxembourgPdfLayoutEvidenceDisposition Disposition { get; }

    public LuxembourgPdfLayoutEvidenceGapReason? GapReason { get; }

    public IReadOnlyList<LuxembourgPdfGlyphEvidence> Glyphs { get; }

    /// <summary>The exact retained-byte receipt, carried as provenance and excluded from identity.</summary>
    public DurableBlobWriteReceipt TransportReceipt { get; }

    public string RuleProfileSha256 { get; }

    public string SemanticIdentitySha256 { get; }

    private static string IdentityOf(LuxembourgPdfLayoutEvidenceOutcome outcome)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "lex-v3-luxembourg-pdf-layout-evidence-outcome/1");
        Append(hash, outcome.RuleProfileSha256);
        Append(hash, outcome.SourceEligibility.SemanticIdentitySha256);
        Append(hash, (int)outcome.Disposition);
        Append(hash, outcome.GapReason is { } gap ? (int)gap : 0);
        foreach (var glyph in outcome.Glyphs)
        {
            Append(hash, glyph.PhysicalPageNumber);
            Append(hash, glyph.GlyphOrdinal);
            Append(hash, glyph.Text);
            Append(hash, Canonical(glyph.Left));
            Append(hash, Canonical(glyph.Bottom));
            Append(hash, Canonical(glyph.Right));
            Append(hash, Canonical(glyph.Top));
            Append(hash, Canonical(glyph.FontSize));
            Append(hash, glyph.FontName);
            Append(hash, glyph.TextOrientation);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string Canonical(double value) =>
        (value == 0d ? 0d : value).ToString("R", CultureInfo.InvariantCulture);

    private static void Append(IncrementalHash hash, int value) =>
        Append(hash, value.ToString(CultureInfo.InvariantCulture));

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

/// <summary>Exactly one layout-evidence outcome per member of one proof-complete eligibility population.</summary>
public sealed class LuxembourgPdfLayoutEvidencePopulation
{
    internal LuxembourgPdfLayoutEvidencePopulation(
        LuxembourgPdfProfileEligibilityPopulation sourceEligibilityPopulation,
        IReadOnlyList<LuxembourgPdfLayoutEvidenceOutcome> outcomes,
        string ruleProfileSha256)
    {
        SourceEligibilityPopulation = sourceEligibilityPopulation;
        Outcomes = Array.AsReadOnly(outcomes.ToArray());
        RuleProfileSha256 = ruleProfileSha256;
        IdentitySha256 = Digest(string.Join(
            '\n',
            new[]
            {
                "lex-v3-luxembourg-pdf-layout-evidence-population/1",
                ruleProfileSha256,
                sourceEligibilityPopulation.IdentitySha256,
            }.Concat(Outcomes.Select(static outcome => outcome.SemanticIdentitySha256))));
    }

    public LuxembourgPdfProfileEligibilityPopulation SourceEligibilityPopulation { get; }

    public IReadOnlyList<LuxembourgPdfLayoutEvidenceOutcome> Outcomes { get; }

    public string RuleProfileSha256 { get; }

    public string IdentitySha256 { get; }

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public enum LuxembourgPdfLayoutEvidenceProductionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("retained_bytes_unavailable")]
    RetainedBytesUnavailable = 1,
}

public sealed class LuxembourgPdfLayoutEvidenceProductionResult
{
    private LuxembourgPdfLayoutEvidenceProductionResult(
        LuxembourgPdfLayoutEvidencePopulation? population,
        LuxembourgPdfLayoutEvidenceProductionRefusal refusal,
        string? detail)
    {
        Population = population;
        Refusal = refusal;
        Detail = detail;
    }

    public LuxembourgPdfLayoutEvidencePopulation? Population { get; }

    public LuxembourgPdfLayoutEvidenceProductionRefusal Refusal { get; }

    /// <summary>Run-specific failure detail; never part of semantic identity.</summary>
    public string? Detail { get; }

    public bool Produced => Refusal == LuxembourgPdfLayoutEvidenceProductionRefusal.None;

    internal static LuxembourgPdfLayoutEvidenceProductionResult Success(
        LuxembourgPdfLayoutEvidencePopulation population) => new(
            population, LuxembourgPdfLayoutEvidenceProductionRefusal.None, null);

    internal static LuxembourgPdfLayoutEvidenceProductionResult Refused(string detail) => new(
        null, LuxembourgPdfLayoutEvidenceProductionRefusal.RetainedBytesUnavailable, detail);
}

/// <summary>
/// Reopens only the receipts of one proof-complete PDF eligibility population and retains physical
/// page/content-stream glyph evidence. It accepts no caller-authored map, receipt, bytes or text.
/// </summary>
public sealed class LuxembourgPdfLayoutEvidenceProducer
{
    private const string RuleProfile =
        "lex-v3-luxembourg-pdf-layout-evidence-rule/1\n" +
        "parser=pdfpig/0.1.11\n" +
        "pages=physical-order\n" +
        "glyphs=content-stream-order+text+rectangle+font-size+font-name+orientation\n" +
        "semantics=none\n" +
        "ocr=not-classified\n";

    private readonly ICustodyStore _custodyStore;

    public LuxembourgPdfLayoutEvidenceProducer(ICustodyStore custodyStore) =>
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));

    public static string RuleProfileSha256 { get; } = Digest(RuleProfile);

    public async Task<LuxembourgPdfLayoutEvidenceProductionResult> RunAsync(
        LuxembourgPdfProfileEligibilityPopulation sourceEligibilityPopulation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceEligibilityPopulation);
        cancellationToken.ThrowIfCancellationRequested();

        var outcomes = new List<LuxembourgPdfLayoutEvidenceOutcome>(
            sourceEligibilityPopulation.Outcomes.Count);
        foreach (var source in sourceEligibilityPopulation.Outcomes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source.Disposition == LuxembourgPdfProfileEligibilityDisposition.NotPdf)
            {
                outcomes.Add(Outcome(
                    source, LuxembourgPdfLayoutEvidenceDisposition.NotApplicable, null, []));
                continue;
            }

            if (source.Disposition == LuxembourgPdfProfileEligibilityDisposition.TypedGap)
            {
                outcomes.Add(Outcome(
                    source,
                    LuxembourgPdfLayoutEvidenceDisposition.TypedGap,
                    LuxembourgPdfLayoutEvidenceGapReason.UpstreamEligibilityGap,
                    []));
                continue;
            }

            ReadOnlyMemory<byte> bytes;
            try
            {
                bytes = await CustodyRestore.ReadCheckedAsync(
                    _custodyStore, source.TransportReceipt.Reference, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is CustodyRequiredException
                or CustodyIntegrityException or CustodyPolicyException)
            {
                return LuxembourgPdfLayoutEvidenceProductionResult.Refused(
                    source.PublisherItemIri + ": " + exception.Message);
            }

            IReadOnlyList<LuxembourgPdfGlyphEvidence> glyphs;
            try
            {
                glyphs = ReadGlyphs(bytes.ToArray(), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                outcomes.Add(Outcome(
                    source,
                    LuxembourgPdfLayoutEvidenceDisposition.TypedGap,
                    LuxembourgPdfLayoutEvidenceGapReason.PdfUnreadable,
                    []));
                continue;
            }

            if (glyphs.Any(static glyph => !Valid(glyph)))
            {
                outcomes.Add(Outcome(
                    source,
                    LuxembourgPdfLayoutEvidenceDisposition.TypedGap,
                    LuxembourgPdfLayoutEvidenceGapReason.InvalidGlyphGeometry,
                    []));
            }
            else if (glyphs.Count == 0)
            {
                outcomes.Add(Outcome(
                    source,
                    LuxembourgPdfLayoutEvidenceDisposition.TypedGap,
                    LuxembourgPdfLayoutEvidenceGapReason.NoTextGlyphs,
                    []));
            }
            else
            {
                outcomes.Add(Outcome(
                    source, LuxembourgPdfLayoutEvidenceDisposition.Admitted, null, glyphs));
            }
        }

        return LuxembourgPdfLayoutEvidenceProductionResult.Success(
            new LuxembourgPdfLayoutEvidencePopulation(
                sourceEligibilityPopulation, outcomes, RuleProfileSha256));
    }

    private static IReadOnlyList<LuxembourgPdfGlyphEvidence> ReadGlyphs(
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(bytes);
        var result = new List<LuxembourgPdfGlyphEvidence>();
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var letter in page.Letters)
            {
                result.Add(new LuxembourgPdfGlyphEvidence(
                    page.Number,
                    result.Count,
                    letter.Value,
                    letter.GlyphRectangle.Left,
                    letter.GlyphRectangle.Bottom,
                    letter.GlyphRectangle.Right,
                    letter.GlyphRectangle.Top,
                    letter.FontSize,
                    letter.FontName ?? string.Empty,
                    (int)letter.TextOrientation));
            }
        }

        return result;
    }

    private static bool Valid(LuxembourgPdfGlyphEvidence glyph) =>
        glyph.PhysicalPageNumber > 0
        && glyph.GlyphOrdinal >= 0
        && glyph.Text.Length > 0
        && Finite(glyph.Left)
        && Finite(glyph.Bottom)
        && Finite(glyph.Right)
        && Finite(glyph.Top)
        && Finite(glyph.FontSize);

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static LuxembourgPdfLayoutEvidenceOutcome Outcome(
        LuxembourgPdfProfileEligibilityOutcome source,
        LuxembourgPdfLayoutEvidenceDisposition disposition,
        LuxembourgPdfLayoutEvidenceGapReason? gap,
        IReadOnlyList<LuxembourgPdfGlyphEvidence> glyphs) =>
        new(source, disposition, gap, glyphs, RuleProfileSha256);

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
