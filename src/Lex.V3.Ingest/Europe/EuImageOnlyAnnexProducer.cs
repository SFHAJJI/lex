using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using UglyToad.PdfPig;

namespace Lex.V3.Ingest.Europe;

/// <summary>Why retained PDF bytes did not produce an image-only annex disposition. Closed.</summary>
public enum EuImageOnlyAnnexProductionRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("profile_digest_mismatch")]
    ProfileDigestMismatch = 1,

    [JsonStringEnumMemberName("profile_invalid")]
    ProfileInvalid = 2,

    [JsonStringEnumMemberName("profile_does_not_name_annex")]
    ProfileDoesNotNameAnnex = 3,

    [JsonStringEnumMemberName("profile_does_not_name_transport")]
    ProfileDoesNotNameTransport = 4,

    [JsonStringEnumMemberName("retained_bytes_unavailable")]
    RetainedBytesUnavailable = 5,

    [JsonStringEnumMemberName("pdf_unreadable")]
    PdfUnreadable = 6,

    [JsonStringEnumMemberName("profile_page_outside_document")]
    ProfilePageOutsideDocument = 7,

    [JsonStringEnumMemberName("annex_contains_text")]
    AnnexContainsText = 8,

    [JsonStringEnumMemberName("annex_contains_no_image")]
    AnnexContainsNoImage = 9,
}

/// <summary>One evidence-bound image-only annex disposition, or one named refusal.</summary>
public sealed class EuImageOnlyAnnexProductionResult
{
    private EuImageOnlyAnnexProductionResult(
        EuAnnexBodyDisposition? disposition,
        EuImageOnlyAnnexProductionRefusal refusal,
        string? detail)
    {
        Disposition = disposition;
        Refusal = refusal;
        Detail = detail;
    }

    public EuAnnexBodyDisposition? Disposition { get; }

    public EuImageOnlyAnnexProductionRefusal Refusal { get; }

    public string? Detail { get; }

    public bool Produced => Refusal == EuImageOnlyAnnexProductionRefusal.None;

    internal static EuImageOnlyAnnexProductionResult Success(EuAnnexBodyDisposition disposition) =>
        new(disposition, EuImageOnlyAnnexProductionRefusal.None, null);

    internal static EuImageOnlyAnnexProductionResult Refused(
        EuImageOnlyAnnexProductionRefusal refusal,
        string detail) => new(null, refusal, detail);
}

/// <summary>
/// Reopens one official PDF receipt and produces #587's typed gap only for profile-selected pages
/// that contain images and no PDF text glyphs.
/// </summary>
/// <remarks>
/// The profile bytes bind the annex location and the one-based page selection. The producer does
/// not OCR, extract, return or reconstruct wording. A PDF address is not itself evidence that an
/// annex is image-only. This bounded producer accepts traditional cross-reference tables only,
/// after checking every live entry against its object header; cross-reference streams refuse.
/// </remarks>
public sealed class EuImageOnlyAnnexProducer
{
    private const string ProfileHeader = "lex-v3-eu-image-only-annex-profile/1";
    private const string Classification = "classification=pdfpig-0.1.11:no_glyphs+image";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ICustodyStore _custodyStore;

    public EuImageOnlyAnnexProducer(ICustodyStore custodyStore)
    {
        _custodyStore = custodyStore ?? throw new ArgumentNullException(nameof(custodyStore));
    }

    public async Task<EuImageOnlyAnnexProductionResult> RunAsync(
        SourceObjectRef sourceObject,
        EuStructuralLocation annexLocation,
        EuDocumentFetchAddress officialAddress,
        HttpLogicalRequest officialRequest,
        HttpLogicalRequest terminalRequest,
        RoutedHttpEvidence sourceEvidence,
        DurableBlobWriteReceipt retainedTransportBytes,
        ReadOnlyMemory<byte> profileBytes,
        SourceArtifactRef profileRef,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceObject);
        ArgumentNullException.ThrowIfNull(annexLocation);
        ArgumentNullException.ThrowIfNull(officialAddress);
        ArgumentNullException.ThrowIfNull(officialRequest);
        ArgumentNullException.ThrowIfNull(terminalRequest);
        ArgumentNullException.ThrowIfNull(sourceEvidence);
        ArgumentNullException.ThrowIfNull(retainedTransportBytes);
        ArgumentNullException.ThrowIfNull(profileRef);
        cancellationToken.ThrowIfCancellationRequested();

        var profileDigest = Convert.ToHexStringLower(SHA256.HashData(profileBytes.Span));
        if (!string.Equals(profileDigest, profileRef.Sha256, StringComparison.Ordinal))
        {
            return EuImageOnlyAnnexProductionResult.Refused(
                EuImageOnlyAnnexProductionRefusal.ProfileDigestMismatch,
                "the profile bytes do not carry the digest their reference names");
        }

        if (!TryReadProfile(
                profileBytes.Span,
                out var annexDigest,
                out var transportDigest,
                out var pages,
                out var profileFailure))
        {
            return EuImageOnlyAnnexProductionResult.Refused(
                EuImageOnlyAnnexProductionRefusal.ProfileInvalid,
                profileFailure!);
        }

        var offeredAnnexDigest = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(annexLocation.RawValue)));
        if (!string.Equals(annexDigest, offeredAnnexDigest, StringComparison.Ordinal))
        {
            return EuImageOnlyAnnexProductionResult.Refused(
                EuImageOnlyAnnexProductionRefusal.ProfileDoesNotNameAnnex,
                "the profile names a different structural annex location");
        }

        if (!string.Equals(
                transportDigest,
                retainedTransportBytes.Reference.ContentSha256,
                StringComparison.Ordinal))
        {
            return EuImageOnlyAnnexProductionResult.Refused(
                EuImageOnlyAnnexProductionRefusal.ProfileDoesNotNameTransport,
                "the profile names different retained PDF bytes");
        }

        ReadOnlyMemory<byte> pdfBytes;
        try
        {
            pdfBytes = await CustodyRestore.ReadCheckedAsync(
                    _custodyStore, retainedTransportBytes.Reference, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is CustodyRequiredException
            or CustodyIntegrityException or CustodyPolicyException)
        {
            return EuImageOnlyAnnexProductionResult.Refused(
                EuImageOnlyAnnexProductionRefusal.RetainedBytesUnavailable,
                exception.Message);
        }

        try
        {
            if (!HasPdfStructure(pdfBytes.Span))
            {
                return EuImageOnlyAnnexProductionResult.Refused(
                    EuImageOnlyAnnexProductionRefusal.PdfUnreadable,
                    "the retained bytes do not carry a complete supported PDF structure");
            }

            using var document = PdfDocument.Open(pdfBytes.ToArray());
            if (pages.Any(page => page > document.NumberOfPages))
            {
                return EuImageOnlyAnnexProductionResult.Refused(
                    EuImageOnlyAnnexProductionRefusal.ProfilePageOutsideDocument,
                    "the profile selects a page outside the retained PDF");
            }

            foreach (var pageNumber in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.GetPage(pageNumber);
                if (page.Letters.Count > 0)
                {
                    return EuImageOnlyAnnexProductionResult.Refused(
                        EuImageOnlyAnnexProductionRefusal.AnnexContainsText,
                        $"profile page {pageNumber} contains PDF text glyphs");
                }

                if (!page.GetImages().Any())
                {
                    return EuImageOnlyAnnexProductionResult.Refused(
                        EuImageOnlyAnnexProductionRefusal.AnnexContainsNoImage,
                        $"profile page {pageNumber} contains no PDF image");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return EuImageOnlyAnnexProductionResult.Refused(
                EuImageOnlyAnnexProductionRefusal.PdfUnreadable,
                exception.GetType().Name);
        }

        var disposition = EuAnnexBodyDisposition.Create(
            sourceObject,
            annexLocation,
            officialAddress,
            officialRequest,
            terminalRequest,
            sourceEvidence,
            retainedTransportBytes,
            profileBytes,
            profileRef,
            EuAnnexBodyDispositionOutcome.TextNotAvailable);
        return EuImageOnlyAnnexProductionResult.Success(disposition);
    }

    private static bool TryReadProfile(
        ReadOnlySpan<byte> bytes,
        out string? annexDigest,
        out string? transportDigest,
        out int[] pages,
        out string? failure)
    {
        annexDigest = null;
        transportDigest = null;
        pages = [];
        failure = null;

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            failure = "the profile is not UTF-8";
            return false;
        }

        var lines = text.Split('\n');
        if (lines.Length != 6 || lines[^1].Length != 0 ||
            !string.Equals(lines[0], ProfileHeader, StringComparison.Ordinal) ||
            !lines[1].StartsWith("annex_location_sha256=", StringComparison.Ordinal) ||
            !lines[2].StartsWith("transport_sha256=", StringComparison.Ordinal) ||
            !lines[3].StartsWith("pages=", StringComparison.Ordinal) ||
            !string.Equals(lines[4], Classification, StringComparison.Ordinal))
        {
            failure = "the profile does not have the exact image-only annex profile shape";
            return false;
        }

        annexDigest = lines[1]["annex_location_sha256=".Length..];
        if (!CustodyDigest.IsLowercaseSha256(annexDigest))
        {
            failure = "the profile annex location is not one lowercase SHA-256";
            return false;
        }

        transportDigest = lines[2]["transport_sha256=".Length..];
        if (!CustodyDigest.IsLowercaseSha256(transportDigest))
        {
            failure = "the profile transport is not one lowercase SHA-256";
            return false;
        }

        var pageTokens = lines[3]["pages=".Length..].Split(',');
        if (pageTokens.Length == 0 || pageTokens.Any(token => token.Length == 0 ||
            (token.Length > 1 && token[0] == '0') ||
            !int.TryParse(token, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var page) || page <= 0))
        {
            failure = "the profile pages are not positive canonical integers";
            return false;
        }

        pages = pageTokens.Select(token => int.Parse(
                token, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        if (!pages.SequenceEqual(pages.Order()) || pages.Distinct().Count() != pages.Length)
        {
            failure = "the profile pages are not unique and strictly increasing";
            return false;
        }

        return true;
    }

    private static bool HasPdfStructure(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.StartsWith("%PDF-"u8))
        {
            return false;
        }

        var end = bytes.Length;
        while (end > 0 && bytes[end - 1] is 0 or 9 or 10 or 12 or 13 or 32)
        {
            end--;
        }

        var document = bytes[..end];
        if (!document.EndsWith("%%EOF"u8))
        {
            return false;
        }

        var startXref = document.LastIndexOf("startxref"u8);
        if (startXref < 0)
        {
            return false;
        }

        var cursor = startXref + "startxref"u8.Length;
        SkipPdfWhitespace(document, ref cursor);
        var offsetStart = cursor;
        long offset = 0;
        while (cursor < document.Length && document[cursor] is >= (byte)'0' and <= (byte)'9')
        {
            offset = checked((offset * 10) + document[cursor] - (byte)'0');
            cursor++;
        }

        if (cursor == offsetStart || offset >= document.Length)
        {
            return false;
        }

        SkipPdfWhitespace(document, ref cursor);
        if (!document[cursor..].SequenceEqual("%%EOF"u8))
        {
            return false;
        }

        var crossReference = document[(int)offset..];
        if (crossReference.StartsWith("xref"u8))
        {
            return HasValidTraditionalCrossReference(document, (int)offset);
        }

        return false;
    }

    private static bool HasValidTraditionalCrossReference(
        ReadOnlySpan<byte> document,
        int crossReferenceOffset)
    {
        using var reader = new StringReader(Encoding.ASCII.GetString(document[crossReferenceOffset..]));
        if (!string.Equals(reader.ReadLine(), "xref", StringComparison.Ordinal))
        {
            return false;
        }

        var sawInUseEntry = false;
        while (reader.ReadLine() is { } header)
        {
            if (string.Equals(header, "trailer", StringComparison.Ordinal))
            {
                return sawInUseEntry;
            }

            var subsection = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (subsection.Length != 2 ||
                !int.TryParse(subsection[0], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var firstObject) ||
                !int.TryParse(subsection[1], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var count) ||
                firstObject < 0 || count <= 0)
            {
                return false;
            }

            for (var index = 0; index < count; index++)
            {
                var entry = reader.ReadLine()?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (entry is not { Length: 3 } || entry[2] is not ("n" or "f") ||
                    !long.TryParse(entry[0], System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var objectOffset) ||
                    !int.TryParse(entry[1], System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var generation) ||
                    objectOffset < 0 || generation < 0)
                {
                    return false;
                }

                var objectNumber = firstObject + index;
                if (objectNumber == 0)
                {
                    if (entry[2] != "f" || objectOffset != 0 || generation != 65535)
                    {
                        return false;
                    }

                }
                else if (entry[2] == "n")
                {
                    sawInUseEntry = true;
                    if (objectOffset >= document.Length ||
                        !ObjectHeaderMatches(
                            document[(int)objectOffset..], objectNumber, generation))
                    {
                        return false;
                    }
                }
            }
        }

        return false;
    }

    private static bool ObjectHeaderMatches(
        ReadOnlySpan<byte> bytes,
        int expectedObject,
        int expectedGeneration)
    {
        var cursor = 0;
        return TryReadNonNegativeInt(bytes, ref cursor, out var objectNumber) &&
            objectNumber == expectedObject &&
            SkipRequiredPdfWhitespace(bytes, ref cursor) &&
            TryReadNonNegativeInt(bytes, ref cursor, out var generation) &&
            generation == expectedGeneration &&
            SkipRequiredPdfWhitespace(bytes, ref cursor) &&
            bytes[cursor..].StartsWith("obj"u8);
    }

    private static bool TryReadNonNegativeInt(
        ReadOnlySpan<byte> bytes,
        ref int cursor,
        out int value)
    {
        var start = cursor;
        value = 0;
        while (cursor < bytes.Length && bytes[cursor] is >= (byte)'0' and <= (byte)'9')
        {
            value = checked((value * 10) + bytes[cursor] - (byte)'0');
            cursor++;
        }

        return cursor > start;
    }

    private static bool SkipRequiredPdfWhitespace(ReadOnlySpan<byte> bytes, ref int cursor)
    {
        var start = cursor;
        SkipPdfWhitespace(bytes, ref cursor);
        return cursor > start;
    }

    private static void SkipPdfWhitespace(ReadOnlySpan<byte> bytes, ref int cursor)
    {
        while (cursor < bytes.Length && bytes[cursor] is 0 or 9 or 10 or 12 or 13 or 32)
        {
            cursor++;
        }
    }
}
