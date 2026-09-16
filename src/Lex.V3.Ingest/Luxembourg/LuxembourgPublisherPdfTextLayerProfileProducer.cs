using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.Ingest.Luxembourg;

public enum LuxembourgPublisherPdfTextLayerDisposition
{
    [JsonStringEnumMemberName("not_applicable")]
    NotApplicable = 1,

    [JsonStringEnumMemberName("admitted")]
    Admitted = 2,

    [JsonStringEnumMemberName("typed_gap")]
    TypedGap = 3,
}

public enum LuxembourgPublisherPdfTextLayerGapReason
{
    [JsonStringEnumMemberName("upstream_layout_gap")]
    UpstreamLayoutGap = 1,

    [JsonStringEnumMemberName("invisible_text_layer")]
    InvisibleTextLayer = 2,

    [JsonStringEnumMemberName("text_artifact_too_large")]
    TextArtifactTooLarge = 3,
}

public sealed record LuxembourgPublisherPdfTextLayerPage(
    int PhysicalPageNumber,
    int ImageCount,
    IReadOnlyList<string> OrderedGlyphTextFragments);

public sealed record LuxembourgPublisherPdfTextLayerDocument(
    IReadOnlyList<LuxembourgPublisherPdfTextLayerPage> Pages);

/// <summary>
/// One publisher-PDF member's lower-confidence visible text-layer disposition. The exact layout
/// outcome remains the source of truth; this outcome never claims article or reading-order semantics.
/// </summary>
public sealed class LuxembourgPublisherPdfTextLayerOutcome
{
    internal LuxembourgPublisherPdfTextLayerOutcome(
        LuxembourgPdfLayoutEvidenceOutcome sourceLayoutEvidence,
        LuxembourgPublisherPdfTextLayerDisposition disposition,
        LuxembourgPublisherPdfTextLayerGapReason? gapReason,
        DurableBlobWriteReceipt? textArtifactReceipt,
        int pageCount,
        int glyphFragmentCount,
        int imageCount,
        int characterCount,
        string ruleProfileSha256)
    {
        SourceLayoutEvidence = sourceLayoutEvidence;
        Disposition = disposition;
        GapReason = gapReason;
        TextArtifactReceipt = textArtifactReceipt;
        PageCount = pageCount;
        GlyphFragmentCount = glyphFragmentCount;
        ImageCount = imageCount;
        CharacterCount = characterCount;
        RuleProfileSha256 = ruleProfileSha256;
        SemanticIdentitySha256 = IdentityOf(this);
    }

    public LuxembourgPdfLayoutEvidenceOutcome SourceLayoutEvidence { get; }

    public LuxembourgPublisherPdfTextLayerDisposition Disposition { get; }

    public LuxembourgPublisherPdfTextLayerGapReason? GapReason { get; }

    public DurableBlobWriteReceipt? TextArtifactReceipt { get; }

    public int PageCount { get; }

    public int GlyphFragmentCount { get; }

    public int ImageCount { get; }

    public int CharacterCount { get; }

    public string RuleProfileSha256 { get; }

    public string SemanticIdentitySha256 { get; }

    private static string IdentityOf(LuxembourgPublisherPdfTextLayerOutcome outcome) => Digest(string.Join(
        '\n',
        "lex-v3-luxembourg-publisher-pdf-text-layer-outcome/1",
        outcome.RuleProfileSha256,
        outcome.SourceLayoutEvidence.SemanticIdentitySha256,
        ((int)outcome.Disposition).ToString(CultureInfo.InvariantCulture),
        outcome.GapReason is { } gap ? ((int)gap).ToString(CultureInfo.InvariantCulture) : string.Empty,
        outcome.TextArtifactReceipt?.Reference.ContentSha256 ?? string.Empty,
        outcome.PageCount.ToString(CultureInfo.InvariantCulture),
        outcome.GlyphFragmentCount.ToString(CultureInfo.InvariantCulture),
        outcome.ImageCount.ToString(CultureInfo.InvariantCulture),
        outcome.CharacterCount.ToString(CultureInfo.InvariantCulture)));

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class LuxembourgPublisherPdfTextLayerPopulation
{
    private LuxembourgPublisherPdfTextLayerPopulation(
        LuxembourgPdfLayoutEvidencePopulation sourceLayoutEvidencePopulation,
        IReadOnlyList<LuxembourgPublisherPdfTextLayerOutcome> outcomes,
        string ruleProfileSha256)
    {
        SourceLayoutEvidencePopulation = sourceLayoutEvidencePopulation;
        Outcomes = Array.AsReadOnly(outcomes.ToArray());
        RuleProfileSha256 = ruleProfileSha256;
        IdentitySha256 = Digest(string.Join(
            '\n',
            new[]
            {
                "lex-v3-luxembourg-publisher-pdf-text-layer-population/1",
                ruleProfileSha256,
                sourceLayoutEvidencePopulation.IdentitySha256,
            }.Concat(Outcomes.Select(static outcome => outcome.SemanticIdentitySha256))));
    }

    public LuxembourgPdfLayoutEvidencePopulation SourceLayoutEvidencePopulation { get; }

    public IReadOnlyList<LuxembourgPublisherPdfTextLayerOutcome> Outcomes { get; }

    public string RuleProfileSha256 { get; }

    public string IdentitySha256 { get; }

    internal static LuxembourgPublisherPdfTextLayerPopulation Create(
        LuxembourgPdfLayoutEvidencePopulation source,
        IReadOnlyList<LuxembourgPublisherPdfTextLayerOutcome> outcomes,
        string ruleProfileSha256)
    {
        if (outcomes.Count != source.Outcomes.Count
            || outcomes.Where((outcome, index) =>
                !ReferenceEquals(outcome.SourceLayoutEvidence, source.Outcomes[index])).Any())
        {
            throw new InvalidOperationException(
                "Every layout-evidence member needs exactly one ordered text-layer outcome.");
        }

        return new(source, outcomes, ruleProfileSha256);
    }

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public enum LuxembourgPublisherPdfTextLayerProductionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("layout_evidence_unavailable")]
    LayoutEvidenceUnavailable = 1,

    [JsonStringEnumMemberName("text_artifact_custody_unavailable")]
    TextArtifactCustodyUnavailable = 2,
}

public sealed class LuxembourgPublisherPdfTextLayerProductionResult
{
    private LuxembourgPublisherPdfTextLayerProductionResult(
        LuxembourgPublisherPdfTextLayerPopulation? population,
        LuxembourgPublisherPdfTextLayerProductionRefusal refusal,
        string? detail)
    {
        Population = population;
        Refusal = refusal;
        Detail = detail;
    }

    public LuxembourgPublisherPdfTextLayerPopulation? Population { get; }

    public LuxembourgPublisherPdfTextLayerProductionRefusal Refusal { get; }

    public string? Detail { get; }

    public bool Produced => Refusal == LuxembourgPublisherPdfTextLayerProductionRefusal.None;

    internal static LuxembourgPublisherPdfTextLayerProductionResult Success(
        LuxembourgPublisherPdfTextLayerPopulation population) => new(
            population, LuxembourgPublisherPdfTextLayerProductionRefusal.None, null);

    internal static LuxembourgPublisherPdfTextLayerProductionResult Refused(
        LuxembourgPublisherPdfTextLayerProductionRefusal refusal,
        string detail) => new(null, refusal, detail);
}

public static class LuxembourgPublisherPdfTextLayerArtifactReader
{
    public static async Task<LuxembourgPublisherPdfTextLayerDocument> ReadAsync(
        LuxembourgPublisherPdfTextLayerOutcome outcome,
        ICustodyStore custodyStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(custodyStore);
        if (outcome.TextArtifactReceipt is null)
        {
            throw new InvalidOperationException("This outcome carries no text-layer artifact.");
        }

        var bytes = await CustodyRestore.ReadCheckedAsync(
            custodyStore, outcome.TextArtifactReceipt.Reference, cancellationToken)
            .ConfigureAwait(false);
        return LuxembourgPublisherPdfTextLayerArtifactCodec.Decode(
            bytes.Span,
            outcome.RuleProfileSha256,
            outcome.SourceLayoutEvidence.SemanticIdentitySha256,
            outcome.PageCount,
            outcome.GlyphFragmentCount,
            outcome.ImageCount,
            outcome.CharacterCount);
    }
}

/// <summary>
/// Derives only a publisher-PDF's visible content-stream text layer from governed physical evidence.
/// Gazette-PDF interpretation, OCR, reading order and legal structure are deliberately separate.
/// </summary>
public sealed class LuxembourgPublisherPdfTextLayerProfileProducer
{
    private const string RuleProfile =
        "lex-v3-luxembourg-publisher-pdf-text-layer-rule/1\n" +
        "source=exact-luxembourg-pdf-layout-evidence-population/2\n" +
        "family=publisher-pdf-eligible-only;gazette-pdf=not-applicable\n" +
        "pages=physical-order\n" +
        "text=visible-glyph-values-as-distinct-content-stream-ordered-fragments;no-concatenation;no-normalization;no-added-separators\n" +
        "page-images=exact-count;never-interpreted-as-text\n" +
        "nonpainting-rendering-modes=3,7=>typed-gap\n" +
        "semantics=lower-confidence-text-layer-only;no-reading-order-or-legal-structure\n" +
        "artifact=lex-v3-luxembourg-publisher-pdf-text-layer-binary/1;one-custody-object-per-member;max-268435456-bytes\n" +
        "ocr=none\n";

    private readonly ICustodyStore _custodyStore;
    private readonly long _maximumArtifactBytes;

    public LuxembourgPublisherPdfTextLayerProfileProducer(ICustodyStore custodyStore)
        : this(custodyStore, CustodyBounds.MaxObjectBytes)
    {
    }

    internal LuxembourgPublisherPdfTextLayerProfileProducer(
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

    public async Task<LuxembourgPublisherPdfTextLayerProductionResult> RunAsync(
        LuxembourgPdfLayoutEvidencePopulation sourceLayoutEvidencePopulation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceLayoutEvidencePopulation);
        cancellationToken.ThrowIfCancellationRequested();

        var outcomes = new List<LuxembourgPublisherPdfTextLayerOutcome>(
            sourceLayoutEvidencePopulation.Outcomes.Count);
        foreach (var source in sourceLayoutEvidencePopulation.Outcomes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source.Disposition == LuxembourgPdfLayoutEvidenceDisposition.NotApplicable
                || source.SourceEligibility.Disposition ==
                    LuxembourgPdfProfileEligibilityDisposition.GazettePdfEligible)
            {
                outcomes.Add(Outcome(source,
                    LuxembourgPublisherPdfTextLayerDisposition.NotApplicable,
                    null, null, 0, 0, 0, 0));
                continue;
            }

            if (source.Disposition == LuxembourgPdfLayoutEvidenceDisposition.TypedGap)
            {
                outcomes.Add(Outcome(source,
                    LuxembourgPublisherPdfTextLayerDisposition.TypedGap,
                    LuxembourgPublisherPdfTextLayerGapReason.UpstreamLayoutGap,
                    null, 0, 0, 0, 0));
                continue;
            }

            LuxembourgPdfLayoutEvidenceDocument layout;
            try
            {
                layout = await LuxembourgPdfLayoutEvidenceArtifactReader.ReadAsync(
                    source, _custodyStore, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is CustodyRequiredException
                or CustodyIntegrityException or CustodyPolicyException or InvalidDataException)
            {
                return LuxembourgPublisherPdfTextLayerProductionResult.Refused(
                    LuxembourgPublisherPdfTextLayerProductionRefusal.LayoutEvidenceUnavailable,
                    source.SourceEligibility.PublisherItemIri + ": " + exception.Message);
            }

            if (layout.Glyphs.Any(static glyph => glyph.RenderingMode is 3 or 7))
            {
                outcomes.Add(Outcome(source,
                    LuxembourgPublisherPdfTextLayerDisposition.TypedGap,
                    LuxembourgPublisherPdfTextLayerGapReason.InvisibleTextLayer,
                    null, 0, 0, 0, 0));
                continue;
            }

            TextArtifact artifact;
            try
            {
                artifact = Encode(layout, source.SemanticIdentitySha256, _maximumArtifactBytes);
            }
            catch (TextLayerArtifactTooLargeException)
            {
                outcomes.Add(Outcome(source,
                    LuxembourgPublisherPdfTextLayerDisposition.TypedGap,
                    LuxembourgPublisherPdfTextLayerGapReason.TextArtifactTooLarge,
                    null, 0, 0, 0, 0));
                continue;
            }
            DurableBlobWriteReceipt receipt;
            try
            {
                var held = await CustodyHold.TryHoldAsync(
                    _custodyStore, artifact.Bytes, cancellationToken).ConfigureAwait(false);
                receipt = held.Receipt
                    ?? throw new CustodyRequiredException(
                        held.Failure ?? "The publisher-PDF text-layer artifact was not held.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is CustodyRequiredException
                or CustodyIntegrityException or CustodyPolicyException)
            {
                return LuxembourgPublisherPdfTextLayerProductionResult.Refused(
                    LuxembourgPublisherPdfTextLayerProductionRefusal.TextArtifactCustodyUnavailable,
                    source.SourceEligibility.PublisherItemIri + ": " + exception.Message);
            }

            outcomes.Add(Outcome(source,
                LuxembourgPublisherPdfTextLayerDisposition.Admitted,
                null,
                receipt,
                artifact.PageCount,
                artifact.GlyphFragmentCount,
                artifact.ImageCount,
                artifact.CharacterCount));
        }

        return LuxembourgPublisherPdfTextLayerProductionResult.Success(
            LuxembourgPublisherPdfTextLayerPopulation.Create(
                sourceLayoutEvidencePopulation, outcomes, RuleProfileSha256));
    }

    private static TextArtifact Encode(
        LuxembourgPdfLayoutEvidenceDocument layout,
        string sourceSemanticIdentitySha256,
        long maximumBytes)
    {
        using var writer = new LuxembourgPublisherPdfTextLayerArtifactCodec.Writer(
            RuleProfileSha256, sourceSemanticIdentitySha256, layout.Pages.Count, maximumBytes);
        var characters = 0;
        var fragments = 0;
        var images = 0;
        foreach (var page in layout.Pages)
        {
            var glyphs = layout.Glyphs
                .Where(glyph => glyph.PhysicalPageNumber == page.PhysicalPageNumber)
                .OrderBy(static glyph => glyph.GlyphOrdinal)
                .ToArray();
            foreach (var glyph in glyphs)
            {
                characters = checked(characters + glyph.Text.Length);
            }

            fragments = checked(fragments + glyphs.Length);
            images = checked(images + page.ImageCount);
            writer.BeginPage(page.PhysicalPageNumber, page.ImageCount, glyphs.Length);
            foreach (var glyph in glyphs)
            {
                writer.WriteTextFragment(glyph.Text);
            }
        }

        return new(writer.ToMemory(), layout.Pages.Count, fragments, images, characters);
    }

    private static LuxembourgPublisherPdfTextLayerOutcome Outcome(
        LuxembourgPdfLayoutEvidenceOutcome source,
        LuxembourgPublisherPdfTextLayerDisposition disposition,
        LuxembourgPublisherPdfTextLayerGapReason? gap,
        DurableBlobWriteReceipt? receipt,
        int pageCount,
        int glyphFragmentCount,
        int imageCount,
        int characterCount) => new(
            source, disposition, gap, receipt, pageCount, glyphFragmentCount, imageCount,
            characterCount, RuleProfileSha256);

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record TextArtifact(
        ReadOnlyMemory<byte> Bytes,
        int PageCount,
        int GlyphFragmentCount,
        int ImageCount,
        int CharacterCount);
}

internal sealed class TextLayerArtifactTooLargeException : InvalidOperationException;

internal static class LuxembourgPublisherPdfTextLayerArtifactCodec
{
    private static ReadOnlySpan<byte> Magic => "LXPDTL1\n"u8;
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

        internal void BeginPage(int physicalPageNumber, int imageCount, int glyphFragmentCount)
        {
            WriteInt32(physicalPageNumber);
            WriteInt32(imageCount);
            WriteInt32(glyphFragmentCount);
        }

        internal void WriteTextFragment(string text)
        {
            var byteCount = StrictUtf8.GetByteCount(text);
            EnsureFits(checked(sizeof(int) + byteCount));

            Span<byte> length = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(length, byteCount);
            _stream.Write(length);

            var encoder = StrictUtf8.GetEncoder();
            var remaining = text.AsSpan();
            Span<byte> buffer = stackalloc byte[4096];
            while (!remaining.IsEmpty)
            {
                encoder.Convert(
                    remaining,
                    buffer,
                    flush: true,
                    out var charactersUsed,
                    out var bytesUsed,
                    out _);
                _stream.Write(buffer[..bytesUsed]);
                remaining = remaining[charactersUsed..];
            }
        }

        internal ReadOnlyMemory<byte> ToMemory()
        {
            if (!_stream.TryGetBuffer(out var buffer))
            {
                throw new InvalidOperationException("The canonical text-layer buffer is unavailable.");
            }

            return new ReadOnlyMemory<byte>(buffer.Array!, buffer.Offset, checked((int)_stream.Length));
        }

        public void Dispose() => _stream.Dispose();

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

        private void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            EnsureFits(bytes.Length);
            _stream.Write(bytes);
        }

        private void EnsureFits(int byteCount)
        {
            if (byteCount < 0 || _stream.Length > _maximumBytes - byteCount)
            {
                throw new TextLayerArtifactTooLargeException();
            }
        }
    }

    internal static LuxembourgPublisherPdfTextLayerDocument Decode(
        ReadOnlySpan<byte> bytes,
        string expectedRuleProfileSha256,
        string expectedSourceSemanticIdentitySha256,
        int expectedPages,
        int expectedGlyphFragments,
        int expectedImages,
        int expectedCharacters)
    {
        var reader = new Reader(bytes);
        reader.RequireMagic(Magic);
        if (!string.Equals(reader.ReadString(), expectedRuleProfileSha256, StringComparison.Ordinal)
            || !string.Equals(
                reader.ReadString(), expectedSourceSemanticIdentitySha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The text-layer artifact is bound to a different rule or source member.");
        }

        var pageCount = reader.ReadCount("page count");
        if (pageCount != expectedPages)
        {
            throw new InvalidDataException("The artifact page count does not match its outcome.");
        }

        var pages = new List<LuxembourgPublisherPdfTextLayerPage>(pageCount);
        var glyphFragments = 0;
        var images = 0;
        var characters = 0;
        for (var index = 0; index < pageCount; index++)
        {
            var physicalPageNumber = reader.ReadInt32();
            if (physicalPageNumber != index + 1)
            {
                throw new InvalidDataException("The artifact physical pages are not canonical.");
            }

            var imageCount = reader.ReadCount("page image count");
            var fragmentCount = reader.ReadCount("page glyph-fragment count");
            var fragments = new string[fragmentCount];
            for (var fragmentIndex = 0; fragmentIndex < fragmentCount; fragmentIndex++)
            {
                var fragment = reader.ReadString();
                characters = checked(characters + fragment.Length);
                fragments[fragmentIndex] = fragment;
            }

            glyphFragments = checked(glyphFragments + fragmentCount);
            images = checked(images + imageCount);
            pages.Add(new(
                physicalPageNumber,
                imageCount,
                Array.AsReadOnly(fragments)));
        }

        if (glyphFragments != expectedGlyphFragments
            || images != expectedImages
            || characters != expectedCharacters
            || !reader.AtEnd)
        {
            throw new InvalidDataException("The text-layer artifact is not canonical for its outcome.");
        }

        return new(Array.AsReadOnly(pages.ToArray()));
    }

    private ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private ReadOnlySpan<byte> _remaining = bytes;

        internal bool AtEnd => _remaining.IsEmpty;

        internal void RequireMagic(ReadOnlySpan<byte> expected)
        {
            if (_remaining.Length < expected.Length || !_remaining[..expected.Length].SequenceEqual(expected))
            {
                throw new InvalidDataException("The text-layer artifact has the wrong schema.");
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

        internal string ReadString()
        {
            var length = ReadCount("string length");
            Require(length);
            var value = StrictUtf8.GetString(_remaining[..length]);
            _remaining = _remaining[length..];
            return value;
        }

        private void Require(int count)
        {
            if (count < 0 || _remaining.Length < count)
            {
                throw new InvalidDataException("The text-layer artifact is truncated.");
            }
        }
    }
}
