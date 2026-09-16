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

    [JsonStringEnumMemberName("evidence_artifact_too_large")]
    EvidenceArtifactTooLarge = 5,
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
    int TextOrientation,
    int RenderingMode);

/// <summary>One physical page, retained even when it contains no text glyphs.</summary>
public sealed record LuxembourgPdfPageEvidence(
    int PhysicalPageNumber,
    double Width,
    double Height,
    int RotationDegrees,
    int ImageCount);

/// <summary>One reopened layout artifact. Consumers restore one member at a time.</summary>
public sealed record LuxembourgPdfLayoutEvidenceDocument(
    IReadOnlyList<LuxembourgPdfPageEvidence> Pages,
    IReadOnlyList<LuxembourgPdfGlyphEvidence> Glyphs);

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
        DurableBlobWriteReceipt? layoutEvidenceReceipt,
        int pageCount,
        int glyphCount,
        string ruleProfileSha256)
    {
        SourceEligibility = sourceEligibility;
        Disposition = disposition;
        GapReason = gapReason;
        LayoutEvidenceReceipt = layoutEvidenceReceipt;
        PageCount = pageCount;
        GlyphCount = glyphCount;
        TransportReceipt = sourceEligibility.TransportReceipt;
        RuleProfileSha256 = ruleProfileSha256;
        SemanticIdentitySha256 = IdentityOf(this);
    }

    public LuxembourgPdfProfileEligibilityOutcome SourceEligibility { get; }

    public LuxembourgPdfLayoutEvidenceDisposition Disposition { get; }

    public LuxembourgPdfLayoutEvidenceGapReason? GapReason { get; }

    /// <summary>
    /// The compact canonical raw-evidence artifact. Its content address enters semantic identity;
    /// receipt policy and run metadata do not. Null means this outcome carries no parsed evidence.
    /// </summary>
    public DurableBlobWriteReceipt? LayoutEvidenceReceipt { get; }

    public int PageCount { get; }

    public int GlyphCount { get; }

    /// <summary>The exact retained-byte receipt, carried as provenance and excluded from identity.</summary>
    public DurableBlobWriteReceipt TransportReceipt { get; }

    public string RuleProfileSha256 { get; }

    public string SemanticIdentitySha256 { get; }

    private static string IdentityOf(LuxembourgPdfLayoutEvidenceOutcome outcome)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "lex-v3-luxembourg-pdf-layout-evidence-outcome/2");
        Append(hash, outcome.RuleProfileSha256);
        Append(hash, outcome.SourceEligibility.SemanticIdentitySha256);
        Append(hash, (int)outcome.Disposition);
        Append(hash, outcome.GapReason is { } gap ? (int)gap : 0);
        Append(hash, outcome.LayoutEvidenceReceipt?.Reference.ContentSha256 ?? string.Empty);
        Append(hash, outcome.PageCount);
        Append(hash, outcome.GlyphCount);

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

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
    private LuxembourgPdfLayoutEvidencePopulation(
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
                "lex-v3-luxembourg-pdf-layout-evidence-population/2",
                ruleProfileSha256,
                sourceEligibilityPopulation.IdentitySha256,
            }.Concat(Outcomes.Select(static outcome => outcome.SemanticIdentitySha256))));
    }

    public LuxembourgPdfProfileEligibilityPopulation SourceEligibilityPopulation { get; }

    public IReadOnlyList<LuxembourgPdfLayoutEvidenceOutcome> Outcomes { get; }

    public string RuleProfileSha256 { get; }

    public string IdentitySha256 { get; }

    internal static LuxembourgPdfLayoutEvidencePopulation Create(
        LuxembourgPdfProfileEligibilityPopulation sourceEligibilityPopulation,
        IReadOnlyList<LuxembourgPdfLayoutEvidenceOutcome> outcomes,
        string ruleProfileSha256) =>
        new(sourceEligibilityPopulation, outcomes, ruleProfileSha256);

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public enum LuxembourgPdfLayoutEvidenceProductionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("retained_bytes_unavailable")]
    RetainedBytesUnavailable = 1,

    [JsonStringEnumMemberName("layout_evidence_custody_unavailable")]
    LayoutEvidenceCustodyUnavailable = 2,
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

    internal static LuxembourgPdfLayoutEvidenceProductionResult Refused(
        LuxembourgPdfLayoutEvidenceProductionRefusal refusal,
        string detail) => new(null, refusal, detail);
}

/// <summary>Restores one exact canonical member artifact, keeping population memory bounded.</summary>
public static class LuxembourgPdfLayoutEvidenceArtifactReader
{
    public static async Task<LuxembourgPdfLayoutEvidenceDocument> ReadAsync(
        LuxembourgPdfLayoutEvidenceOutcome outcome,
        ICustodyStore custodyStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(custodyStore);
        if (outcome.LayoutEvidenceReceipt is null)
        {
            throw new InvalidOperationException("This outcome carries no layout-evidence artifact.");
        }

        var bytes = await CustodyRestore.ReadCheckedAsync(
            custodyStore, outcome.LayoutEvidenceReceipt.Reference, cancellationToken)
            .ConfigureAwait(false);
        return LuxembourgPdfLayoutEvidenceArtifactCodec.Decode(
            bytes.Span,
            outcome.RuleProfileSha256,
            outcome.SourceEligibility.SemanticIdentitySha256,
            outcome.PageCount,
            outcome.GlyphCount);
    }
}

/// <summary>
/// Reopens only the receipts of one proof-complete PDF eligibility population and retains physical
/// page/content-stream glyph evidence. It accepts no caller-authored map, receipt, bytes or text.
/// </summary>
public sealed class LuxembourgPdfLayoutEvidenceProducer
{
    private const string RuleProfile =
        "lex-v3-luxembourg-pdf-layout-evidence-rule/2\n" +
        "parser=pdfpig/0.1.11\n" +
        "pages=physical-order\n" +
        "page-evidence=dimensions+rotation+image-count\n" +
        "glyphs=content-stream-order+text+rectangle+font-size+font-name+orientation+rendering-mode\n" +
        "artifact=lex-v3-luxembourg-pdf-layout-evidence-binary/1;one-custody-object-per-member;max-268435456-bytes\n" +
        "semantics=none\n" +
        "ocr=not-classified\n";

    private readonly ICustodyStore _custodyStore;
    private readonly long _maximumArtifactBytes;

    public LuxembourgPdfLayoutEvidenceProducer(ICustodyStore custodyStore)
        : this(custodyStore, CustodyBounds.MaxObjectBytes)
    {
    }

    internal LuxembourgPdfLayoutEvidenceProducer(
        ICustodyStore custodyStore,
        long maximumArtifactBytes)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
        if (maximumArtifactBytes <= 0 || maximumArtifactBytes > CustodyBounds.MaxObjectBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumArtifactBytes));
        }

        _maximumArtifactBytes = maximumArtifactBytes;
    }

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
                    source, LuxembourgPdfLayoutEvidenceDisposition.NotApplicable, null, null, 0, 0));
                continue;
            }

            if (source.Disposition == LuxembourgPdfProfileEligibilityDisposition.TypedGap)
            {
                outcomes.Add(Outcome(
                    source,
                    LuxembourgPdfLayoutEvidenceDisposition.TypedGap,
                    LuxembourgPdfLayoutEvidenceGapReason.UpstreamEligibilityGap,
                    null,
                    0,
                    0));
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
                    LuxembourgPdfLayoutEvidenceProductionRefusal.RetainedBytesUnavailable,
                    source.PublisherItemIri + ": " + exception.Message);
            }

            PdfLayoutEvidenceArtifact layout;
            try
            {
                layout = ReadLayout(
                    bytes.ToArray(),
                    source.SemanticIdentitySha256,
                    _maximumArtifactBytes,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (LayoutEvidenceArtifactTooLargeException)
            {
                outcomes.Add(Outcome(
                    source,
                    LuxembourgPdfLayoutEvidenceDisposition.TypedGap,
                    LuxembourgPdfLayoutEvidenceGapReason.EvidenceArtifactTooLarge,
                    null,
                    0,
                    0));
                continue;
            }
            catch (Exception)
            {
                outcomes.Add(Outcome(
                    source,
                    LuxembourgPdfLayoutEvidenceDisposition.TypedGap,
                    LuxembourgPdfLayoutEvidenceGapReason.PdfUnreadable,
                    null,
                    0,
                    0));
                continue;
            }

            if (!layout.Valid)
            {
                outcomes.Add(Outcome(
                    source,
                    LuxembourgPdfLayoutEvidenceDisposition.TypedGap,
                    LuxembourgPdfLayoutEvidenceGapReason.InvalidGlyphGeometry,
                    null,
                    0,
                    0));
            }
            else
            {
                DurableBlobWriteReceipt evidenceReceipt;
                try
                {
                    evidenceReceipt = await HoldArtifactAsync(
                        layout.Bytes,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is CustodyRequiredException
                    or CustodyIntegrityException or CustodyPolicyException)
                {
                    return LuxembourgPdfLayoutEvidenceProductionResult.Refused(
                        LuxembourgPdfLayoutEvidenceProductionRefusal.LayoutEvidenceCustodyUnavailable,
                        source.PublisherItemIri + ": " + exception.Message);
                }

                outcomes.Add(Outcome(
                    source,
                    layout.GlyphCount == 0
                        ? LuxembourgPdfLayoutEvidenceDisposition.TypedGap
                        : LuxembourgPdfLayoutEvidenceDisposition.Admitted,
                    layout.GlyphCount == 0
                        ? LuxembourgPdfLayoutEvidenceGapReason.NoTextGlyphs
                        : null,
                    evidenceReceipt,
                    layout.PageCount,
                    layout.GlyphCount));
            }
        }

        return LuxembourgPdfLayoutEvidenceProductionResult.Success(
            LuxembourgPdfLayoutEvidencePopulation.Create(
                sourceEligibilityPopulation, outcomes, RuleProfileSha256));
    }

    private async Task<DurableBlobWriteReceipt> HoldArtifactAsync(
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        var (receipt, failure) = await CustodyHold
            .TryHoldAsync(_custodyStore, bytes, cancellationToken)
            .ConfigureAwait(false);
        if (receipt is null)
        {
            throw new CustodyRequiredException(failure ?? "The layout-evidence artifact was not held.");
        }

        return receipt;
    }

    private static PdfLayoutEvidenceArtifact ReadLayout(
        byte[] bytes,
        string sourceSemanticIdentitySha256,
        long maximumArtifactBytes,
        CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(bytes);
        using var writer = new LuxembourgPdfLayoutEvidenceArtifactCodec.Writer(
            RuleProfileSha256,
            sourceSemanticIdentitySha256,
            document.NumberOfPages,
            maximumArtifactBytes);
        var glyphOrdinal = 0;
        var valid = true;
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var letters = page.Letters;
            var pageEvidence = new LuxembourgPdfPageEvidence(
                page.Number,
                page.Width,
                page.Height,
                page.Rotation.Value,
                page.GetImages().Count());
            valid &= Valid(pageEvidence);
            writer.WritePage(pageEvidence, letters.Count);
            foreach (var letter in letters)
            {
                var glyph = new LuxembourgPdfGlyphEvidence(
                    page.Number,
                    glyphOrdinal++,
                    letter.Value,
                    letter.GlyphRectangle.Left,
                    letter.GlyphRectangle.Bottom,
                    letter.GlyphRectangle.Right,
                    letter.GlyphRectangle.Top,
                    letter.FontSize,
                    letter.FontName ?? string.Empty,
                    (int)letter.TextOrientation,
                    (int)letter.RenderingMode);
                valid &= Valid(glyph);
                writer.WriteGlyph(glyph);
            }
        }

        return new PdfLayoutEvidenceArtifact(
            writer.ToMemory(), document.NumberOfPages, glyphOrdinal, valid);
    }

    private static bool Valid(LuxembourgPdfPageEvidence page) =>
        page.PhysicalPageNumber > 0
        && Finite(page.Width)
        && page.Width > 0
        && Finite(page.Height)
        && page.Height > 0
        && page.ImageCount >= 0;

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
        DurableBlobWriteReceipt? layoutEvidenceReceipt,
        int pageCount,
        int glyphCount) =>
        new(source, disposition, gap, layoutEvidenceReceipt, pageCount, glyphCount, RuleProfileSha256);

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record PdfLayoutEvidenceArtifact(
        ReadOnlyMemory<byte> Bytes,
        int PageCount,
        int GlyphCount,
        bool Valid);
}

internal static class LuxembourgPdfLayoutEvidenceArtifactCodec
{
    private static ReadOnlySpan<byte> Magic => "LXPDFL1\n"u8;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal sealed class Writer : IDisposable
    {
        private readonly MemoryStream _stream = new();
        private readonly long _maximumBytes;

        internal Writer(
            string ruleProfileSha256,
            string sourceSemanticIdentitySha256,
            int pageCount,
            long maximumBytes)
        {
            _maximumBytes = maximumBytes;
            WriteBytes(Magic);
            WriteString(ruleProfileSha256);
            WriteString(sourceSemanticIdentitySha256);
            WriteInt32(pageCount);
        }

        internal void WritePage(LuxembourgPdfPageEvidence page, int glyphCount)
        {
            WriteInt32(page.PhysicalPageNumber);
            WriteDouble(page.Width);
            WriteDouble(page.Height);
            WriteInt32(page.RotationDegrees);
            WriteInt32(page.ImageCount);
            WriteInt32(glyphCount);
        }

        internal void WriteGlyph(LuxembourgPdfGlyphEvidence glyph)
        {
            WriteInt32(glyph.GlyphOrdinal);
            WriteString(glyph.Text);
            WriteDouble(glyph.Left);
            WriteDouble(glyph.Bottom);
            WriteDouble(glyph.Right);
            WriteDouble(glyph.Top);
            WriteDouble(glyph.FontSize);
            WriteString(glyph.FontName);
            WriteInt32(glyph.TextOrientation);
            WriteInt32(glyph.RenderingMode);
        }

        internal ReadOnlyMemory<byte> ToMemory()
        {
            if (!_stream.TryGetBuffer(out var buffer))
            {
                throw new InvalidOperationException("The canonical artifact buffer is unavailable.");
            }

            return new ReadOnlyMemory<byte>(buffer.Array!, buffer.Offset, checked((int)_stream.Length));
        }

        public void Dispose() => _stream.Dispose();

        private void WriteDouble(double value) => WriteInt64(BitConverter.DoubleToInt64Bits(value == 0d ? 0d : value));

        private void WriteString(string value)
        {
            var bytes = StrictUtf8.GetBytes(value);
            WriteInt32(bytes.Length);
            WriteBytes(bytes);
        }

        private void WriteInt32(int value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            WriteBytes(bytes);
        }

        private void WriteInt64(long value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64BigEndian(bytes, value);
            WriteBytes(bytes);
        }

        private void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            if (_stream.Length + bytes.Length > _maximumBytes)
            {
                throw new LayoutEvidenceArtifactTooLargeException();
            }

            _stream.Write(bytes);
        }
    }

    internal static LuxembourgPdfLayoutEvidenceDocument Decode(
        ReadOnlySpan<byte> bytes,
        string expectedRuleProfileSha256,
        string expectedSourceSemanticIdentitySha256,
        int expectedPages,
        int expectedGlyphs)
    {
        var reader = new Reader(bytes);
        reader.RequireMagic(Magic);
        if (!string.Equals(reader.ReadString(), expectedRuleProfileSha256, StringComparison.Ordinal)
            || !string.Equals(
                reader.ReadString(),
                expectedSourceSemanticIdentitySha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The layout-evidence artifact is bound to a different rule or source member.");
        }

        var pageCount = reader.ReadCount("page count");
        if (pageCount != expectedPages)
        {
            throw new InvalidDataException("The artifact page count does not match its outcome.");
        }

        var pages = new List<LuxembourgPdfPageEvidence>(pageCount);
        var glyphs = new List<LuxembourgPdfGlyphEvidence>(expectedGlyphs);
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var physicalPageNumber = reader.ReadInt32();
            var page = new LuxembourgPdfPageEvidence(
                physicalPageNumber,
                reader.ReadDouble(),
                reader.ReadDouble(),
                reader.ReadInt32(),
                reader.ReadCount("image count"));
            pages.Add(page);
            var pageGlyphCount = reader.ReadCount("page glyph count");
            for (var glyphIndex = 0; glyphIndex < pageGlyphCount; glyphIndex++)
            {
                glyphs.Add(new LuxembourgPdfGlyphEvidence(
                    physicalPageNumber,
                    reader.ReadInt32(),
                    reader.ReadString(),
                    reader.ReadDouble(),
                    reader.ReadDouble(),
                    reader.ReadDouble(),
                    reader.ReadDouble(),
                    reader.ReadDouble(),
                    reader.ReadString(),
                    reader.ReadInt32(),
                    reader.ReadInt32()));
            }
        }

        if (glyphs.Count != expectedGlyphs || !reader.AtEnd)
        {
            throw new InvalidDataException("The layout-evidence artifact is not canonical for its outcome.");
        }

        return new LuxembourgPdfLayoutEvidenceDocument(
            Array.AsReadOnly(pages.ToArray()),
            Array.AsReadOnly(glyphs.ToArray()));
    }

    private ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private ReadOnlySpan<byte> _remaining = bytes;

        internal bool AtEnd => _remaining.IsEmpty;

        internal void RequireMagic(ReadOnlySpan<byte> expected)
        {
            if (_remaining.Length < expected.Length || !_remaining[..expected.Length].SequenceEqual(expected))
            {
                throw new InvalidDataException("The layout-evidence artifact has the wrong schema.");
            }

            _remaining = _remaining[expected.Length..];
        }

        internal int ReadCount(string name)
        {
            var value = ReadInt32();
            if (value < 0)
            {
                throw new InvalidDataException($"The artifact {name} is negative.");
            }

            return value;
        }

        internal int ReadInt32()
        {
            Require(sizeof(int));
            var value = BinaryPrimitives.ReadInt32BigEndian(_remaining);
            _remaining = _remaining[sizeof(int)..];
            return value;
        }

        internal double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());

        internal string ReadString()
        {
            var length = ReadCount("string length");
            Require(length);
            var value = StrictUtf8.GetString(_remaining[..length]);
            _remaining = _remaining[length..];
            return value;
        }

        private long ReadInt64()
        {
            Require(sizeof(long));
            var value = BinaryPrimitives.ReadInt64BigEndian(_remaining);
            _remaining = _remaining[sizeof(long)..];
            return value;
        }

        private readonly void Require(int length)
        {
            if (length < 0 || _remaining.Length < length)
            {
                throw new InvalidDataException("The layout-evidence artifact is truncated.");
            }
        }
    }
}

internal sealed class LayoutEvidenceArtifactTooLargeException : Exception
{
}
