using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// One redirect hop observed on the way to the captured legal-notice response.
/// </summary>
/// <remarks>
/// Modelled on <c>RoutedHttpHop</c>'s request/status/location shape, narrowed to exactly what a
/// single bounded GET of one page can observe: no network origin, no completion union, no custody
/// readback. Those exist in <c>RoutedHttpEvidence</c> because it is minted by the routed acquisition
/// pipeline against durable custody; this type is not, and carrying fields this capture cannot
/// independently verify would assert more than one observation supports.
/// </remarks>
public sealed class EuLegalNoticeRedirectHop
{
    public EuLegalNoticeRedirectHop(ulong ordinal, string requestUri, int status, string location)
    {
        Ordinal = ordinal;
        RequestUri = RoutedHttpValidation.RequireAbsoluteHttpsUri(requestUri, nameof(requestUri));
        if (status is < 300 or > 399)
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "A legal-notice redirect hop must carry an observed 3xx status.");
        }

        Status = status;
        Location = RoutedHttpValidation.RequireAbsoluteHttpsUri(location, nameof(location));
    }

    public ulong Ordinal { get; }

    public string RequestUri { get; }

    public int Status { get; }

    public string Location { get; }
}

/// <summary>
/// One bounded GET of the reviewed EUR-Lex legal notice, frozen as the class-level evidence R8
/// requires before any content class or exception channel may carry a positive rights disposition.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is.</b> D1-01 Candidate 5 R8 (lines 763-784) names one exact request as policy
/// evidence: a bounded GET of
/// <c>https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en</c>, binding its
/// requested and effective URI, every redirect edge, response bytes, digest, media type, and dates.
/// It says this request "is allowed solely as legal-policy evidence even though EUR-Lex is forbidden
/// as an automated legal-body fallback" under Decision 23 (<c>DECISIONS.md</c> item 23: EUR-Lex sits
/// behind an AWS WAF challenge for non-browser clients, so the Union corpus goes Cellar-native via
/// <c>publications.europa.eu</c> instead; EUR-Lex itself is never a body source). This type exists
/// to hold exactly that one observation and nothing else, so the boundary is structural rather than
/// documentary: <see cref="RequestedUri"/> is fixed to R8's exact string and the constructor refuses
/// any other value, so no path through this type can ever describe a different EUR-Lex resource, let
/// alone a law body. <see cref="MaximumNoticeBytes"/> then keeps even that one page far below the
/// size of any real corpus manifestation (see the remark on that constant), so the type cannot be
/// repurposed to hold one by widening the byte count alone. <see cref="EuRightsDisposition"/> and
/// <see cref="EuRightsExceptionDisposition"/> already carry a <see cref="SourceArtifactRef"/>
/// evidence pointer with no producer; <see cref="ToArtifactRef"/> is that producer, binding a
/// caller-assigned custody resource id to this capture's own canonical-byte digest.
/// </para>
/// <para>
/// <b>What one bounded GET proves and what it does not.</b> A single request establishes only what
/// was observed at one instant: this exact status, these exact bytes, this exact digest. It proves
/// nothing about whether a second request would return the same bytes. Two independent captures of
/// this exact URI taken eleven seconds apart on 2026-09-03 in fact returned different bytes and
/// different SHA-256 digests, one byte apart in length: the page embeds a per-session analytics
/// agent id and a per-request CSRF token in three hidden form fields, and both differ between
/// requests even though the surrounding legal prose did not visibly change between the two captures
/// compared. So <see cref="Sha256"/> is the digest of this exact captured observation and is
/// deliberately never described here as a stable content fingerprint, a change-detection key, or
/// proof that the notice is byte-stable across time or requests: this type does not claim any of
/// those, because one bounded GET, or even two, cannot establish them. Revalidation cadence,
/// staleness policy, and "did the notice change" all remain outside this type's evidence, exactly as
/// R8 requires (maximum policy age and a revalidation rule are separate, not-yet-built obligations of
/// the full R8 record, not something a single capture can supply).
/// </para>
/// <para>
/// <b>What this deliberately does not carry.</b> No custody durable-write receipt and no readback
/// digest: those exist on <c>RoutedHttpHop</c> because that hop was written into custody by the
/// routed acquisition pipeline, and the readback proves the write, not the fetch. This capture was
/// never routed through that pipeline, so asserting a custody-backed readback here would describe a
/// write that did not happen. No robots-policy hop and no run identity: those exist on
/// <c>RoutedHttpEvidence</c> because it is minted only by the private runtime acquisition adapter as
/// part of a numbered acquisition run; this type is minted directly from one measured observation and
/// carries no acquisition-run context, because it took none. A future acquisition-integrated capture
/// path may want those fields; adding them is new work bound to new evidence, not a widening of this
/// type's existing constructor.
/// </para>
/// </remarks>
public sealed class EuLegalNoticeEvidence
{
    public const string SchemaId = "lex-eu-legal-notice-evidence/1";

    /// <summary>
    /// The exact request R8 names. Fixed rather than accepted as an argument: a legal-notice
    /// evidence type that could target an arbitrary EUR-Lex URI could just as easily be pointed at a
    /// law-body page, which is exactly the corpus-source use Decision 23 forbids. Pinning the string
    /// here, and refusing any other value in the constructor, keeps that boundary structural instead
    /// of relying on every future caller remembering to pass the right literal.
    /// </summary>
    public const string RequestedUri =
        "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en";

    /// <summary>
    /// The redirect ceiling this type admits. Five hops mirrors the caution behind
    /// <c>RoutedHttpEvidence</c>'s six-hop bound (one fewer here because this type has no terminal
    /// non-redirect hop slot to spend on the initial request); nothing about R8 needs a redirect
    /// chain this long, and the observed capture used zero, but a smaller cap invented purely for
    /// this page would be a guess this type has no evidence for either.
    /// </summary>
    public const int MaximumRedirectHops = 5;

    /// <summary>
    /// The retained-byte ceiling for a captured notice page. The two live captures behind this type
    /// were 135,428 and 135,427 bytes; 4 MiB is generously above that observed size while remaining
    /// far below the size of any real corpus manifestation this project handles (Formex packages and
    /// consolidated acts run to tens of megabytes; <c>CustodyBounds.MaxObjectBytes</c>, the routed
    /// acquisition pipeline's own retained-entity ceiling for actual law bodies, is 256 MiB). A law
    /// body large enough to matter cannot fit this type even if every other check were bypassed,
    /// which is the second half of the structural boundary alongside the fixed
    /// <see cref="RequestedUri"/>.
    /// </summary>
    public const int MaximumNoticeBytes = 4 * 1024 * 1024;

    private readonly EuLegalNoticeRedirectHop[] _redirects;
    private readonly byte[] _canonicalBytes;
    private readonly string _canonicalSha256;

    public EuLegalNoticeEvidence(
        string requestedUri,
        string effectiveUri,
        IReadOnlyList<EuLegalNoticeRedirectHop> redirects,
        int finalStatus,
        RoutedHttpSingleHeader mediaType,
        RoutedHttpHeaderField publisherDate,
        RoutedHttpHeaderField publisherLastModified,
        ulong byteLength,
        string sha256,
        string capturedAt)
    {
        if (!string.Equals(requestedUri, RequestedUri, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Legal-notice evidence must request the exact R8 URI; {requestedUri} is not " +
                $"{RequestedUri}.",
                nameof(requestedUri));
        }

        effectiveUri = RoutedHttpValidation.RequireAbsoluteHttpsUri(effectiveUri, nameof(effectiveUri));

        ArgumentNullException.ThrowIfNull(redirects);
        var redirectSnapshot = redirects.ToArray();
        if (redirectSnapshot.Length > MaximumRedirectHops ||
            redirectSnapshot.Any(static hop => hop is null))
        {
            throw new ArgumentException(
                $"A legal-notice capture retains at most {MaximumRedirectHops} redirect hops.",
                nameof(redirects));
        }

        var expectedFrom = RequestedUri;
        for (var index = 0; index < redirectSnapshot.Length; index++)
        {
            var hop = redirectSnapshot[index];
            if (hop.Ordinal != (ulong)index)
            {
                throw new ArgumentException(
                    "Redirect hops must be ordered from zero without a gap.",
                    nameof(redirects));
            }

            if (!string.Equals(hop.RequestUri, expectedFrom, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Every redirect hop must be requested at the exact URI its predecessor named.",
                    nameof(redirects));
            }

            expectedFrom = hop.Location;
        }

        if (!string.Equals(expectedFrom, effectiveUri, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The redirect chain does not terminate at the declared effective URI.",
                nameof(effectiveUri));
        }

        if (finalStatus != 200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(finalStatus),
                finalStatus,
                "Only a complete 200 response over the effective URI is captured notice evidence; " +
                "this type does not model a blocked, redirected-without-following, or failed " +
                "attempt.");
        }

        FinalStatus = finalStatus;
        MediaType = mediaType ?? throw new ArgumentNullException(nameof(mediaType));
        PublisherDate = publisherDate ?? throw new ArgumentNullException(nameof(publisherDate));
        PublisherLastModified =
            publisherLastModified ?? throw new ArgumentNullException(nameof(publisherLastModified));

        if (byteLength == 0 || byteLength > MaximumNoticeBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteLength),
                byteLength,
                $"A captured notice must retain between 1 and {MaximumNoticeBytes} bytes.");
        }

        ByteLength = byteLength;
        Sha256 = RoutedHttpValidation.RequireSha256(sha256, nameof(sha256));
        CapturedAt = RoutedHttpValidation.RequireTimestamp(capturedAt, nameof(capturedAt));

        EffectiveUri = effectiveUri;
        _redirects = redirectSnapshot;
        _canonicalBytes = WriteCanonicalBytes(this);
        _canonicalSha256 = Convert.ToHexString(SHA256.HashData(_canonicalBytes)).ToLowerInvariant();
    }

    public string Schema => SchemaId;

    public string EffectiveUri { get; }

    public IReadOnlyList<EuLegalNoticeRedirectHop> Redirects => Array.AsReadOnly(_redirects);

    public int FinalStatus { get; }

    public RoutedHttpSingleHeader MediaType { get; }

    /// <summary>The response's <c>Date</c> header. Absent is a legitimate observed state.</summary>
    public RoutedHttpHeaderField PublisherDate { get; }

    /// <summary>
    /// The response's <c>Last-Modified</c> header. The live capture behind this type observed this
    /// as absent; the field stays a typed union rather than a nullable string so that "the publisher
    /// did not send one" is recorded rather than silently missing, matching how every other header on
    /// <c>RoutedHttpResponseHeaders</c> in this codebase represents absence.
    /// </summary>
    public RoutedHttpHeaderField PublisherLastModified { get; }

    public ulong ByteLength { get; }

    /// <summary>
    /// The SHA-256 of the captured response bytes for this one observation. Not a content
    /// fingerprint and not a change-detection key: see the type-level remarks on why one bounded GET
    /// cannot support either reading.
    /// </summary>
    public string Sha256 { get; }

    public string CapturedAt { get; }

    /// <summary>The SHA-256 of this evidence object's own canonical bytes, for <see cref="ToArtifactRef"/>.</summary>
    public string CanonicalSha256 => _canonicalSha256;

    /// <summary>
    /// The reference <see cref="EuRightsDisposition.EvidenceRef"/> and
    /// <see cref="EuRightsExceptionDisposition.EvidenceRef"/> already declare a slot for. The
    /// resource id is assigned by whatever custody write actually stores this evidence's canonical
    /// bytes; this method only binds that externally-assigned id to the digest of the bytes it was
    /// asked to store, so a caller cannot mint a reference to bytes other than this exact capture.
    /// </summary>
    public SourceArtifactRef ToArtifactRef(string resourceId) => new(resourceId, _canonicalSha256);

    public byte[] CopyCanonicalBytes() => _canonicalBytes.ToArray();

    public static EuLegalNoticeEvidence ParseAndVerify(ReadOnlySpan<byte> canonicalBytes)
    {
        try
        {
            var json = RoutedHttpValidation.DecodeStrictUtf8(canonicalBytes, nameof(canonicalBytes));
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
            var root = document.RootElement;
            RoutedHttpValidation.RequireExactPropertyNames(
                root,
                [
                    "schema", "requested_uri", "effective_uri", "redirects", "final_status",
                    "media_type", "publisher_date", "publisher_last_modified", "byte_length",
                    "sha256", "captured_at",
                ],
                nameof(canonicalBytes));
            if (!string.Equals(root.GetProperty("schema").GetString(), SchemaId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Legal-notice evidence has the wrong schema.",
                    nameof(canonicalBytes));
            }

            var redirectsElement = root.GetProperty("redirects");
            if (redirectsElement.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException(
                    "Legal-notice redirect hops must be an array.",
                    nameof(canonicalBytes));
            }

            var redirects = redirectsElement.EnumerateArray().Select(element =>
            {
                RoutedHttpValidation.RequireExactPropertyNames(
                    element,
                    ["ordinal", "request_uri", "status", "location"],
                    nameof(canonicalBytes));
                return new EuLegalNoticeRedirectHop(
                    element.GetProperty("ordinal").GetUInt64(),
                    element.GetProperty("request_uri").GetString()!,
                    element.GetProperty("status").GetInt32(),
                    element.GetProperty("location").GetString()!);
            }).ToArray();

            var mediaTypeElement = root.GetProperty("media_type");
            RoutedHttpValidation.RequireExactPropertyNames(
                mediaTypeElement,
                ["kind", "value"],
                nameof(canonicalBytes));
            if (!string.Equals(mediaTypeElement.GetProperty("kind").GetString(), "single", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Legal-notice media type must be one observed single header value.",
                    nameof(canonicalBytes));
            }

            var rebuilt = new EuLegalNoticeEvidence(
                root.GetProperty("requested_uri").GetString()!,
                root.GetProperty("effective_uri").GetString()!,
                redirects,
                root.GetProperty("final_status").GetInt32(),
                new RoutedHttpSingleHeader(mediaTypeElement.GetProperty("value").GetString()!),
                ParseHeaderField(root.GetProperty("publisher_date"), nameof(canonicalBytes)),
                ParseHeaderField(root.GetProperty("publisher_last_modified"), nameof(canonicalBytes)),
                root.GetProperty("byte_length").GetUInt64(),
                root.GetProperty("sha256").GetString()!,
                root.GetProperty("captured_at").GetString()!);
            if (!canonicalBytes.SequenceEqual(rebuilt._canonicalBytes))
            {
                throw new ArgumentException(
                    "Legal-notice evidence is not its exact canonical typed representation.",
                    nameof(canonicalBytes));
            }

            return rebuilt;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException)
        {
            throw new ArgumentException(
                "Legal-notice evidence is not one valid closed canonical object.",
                nameof(canonicalBytes),
                exception);
        }
    }

    private static byte[] WriteCanonicalBytes(EuLegalNoticeEvidence value)
    {
        var writer = new RoutedHttpTextWriter();
        writer.Raw("{\"schema\":");
        writer.String(SchemaId);
        writer.Raw(",\"requested_uri\":");
        writer.String(RequestedUri);
        writer.Raw(",\"effective_uri\":");
        writer.String(value.EffectiveUri);
        writer.Raw(",\"redirects\":[");
        for (var index = 0; index < value._redirects.Length; index++)
        {
            if (index > 0)
            {
                writer.Raw(",");
            }

            var hop = value._redirects[index];
            writer.Raw("{\"ordinal\":");
            writer.UInt64(hop.Ordinal);
            writer.Raw(",\"request_uri\":");
            writer.String(hop.RequestUri);
            writer.Raw(",\"status\":");
            writer.UInt64((ulong)hop.Status);
            writer.Raw(",\"location\":");
            writer.String(hop.Location);
            writer.Raw("}");
        }

        writer.Raw("],\"final_status\":");
        writer.UInt64((ulong)value.FinalStatus);
        writer.Raw(",\"media_type\":{\"kind\":\"single\",\"value\":");
        writer.String(value.MediaType.Value);
        writer.Raw("},\"publisher_date\":");
        WriteHeaderField(writer, value.PublisherDate);
        writer.Raw(",\"publisher_last_modified\":");
        WriteHeaderField(writer, value.PublisherLastModified);
        writer.Raw(",\"byte_length\":");
        writer.UInt64(value.ByteLength);
        writer.Raw(",\"sha256\":");
        writer.String(value.Sha256);
        writer.Raw(",\"captured_at\":");
        writer.String(value.CapturedAt);
        writer.Raw("}\n");
        return writer.ToUtf8();
    }

    private static void WriteHeaderField(RoutedHttpTextWriter writer, RoutedHttpHeaderField value)
    {
        switch (value)
        {
            case RoutedHttpAbsentHeader:
                writer.Raw("{\"kind\":\"absent\"}");
                return;
            case RoutedHttpSingleHeader single:
                writer.Raw("{\"kind\":\"single\",\"value\":");
                writer.String(single.Value);
                writer.Raw("}");
                return;
            case RoutedHttpMultipleHeader multiple:
                writer.Raw("{\"kind\":\"multiple\",\"values\":[");
                for (var index = 0; index < multiple.Values.Count; index++)
                {
                    if (index > 0)
                    {
                        writer.Raw(",");
                    }

                    writer.String(multiple.Values[index]);
                }

                writer.Raw("]}");
                return;
            default:
                throw new ArgumentException("The HTTP header field union is not closed.", nameof(value));
        }
    }

    private static RoutedHttpHeaderField ParseHeaderField(JsonElement element, string parameterName)
    {
        var kind = element.GetProperty("kind").GetString();
        switch (kind)
        {
            case "absent":
                RoutedHttpValidation.RequireExactPropertyNames(element, ["kind"], parameterName);
                return new RoutedHttpAbsentHeader();
            case "single":
                RoutedHttpValidation.RequireExactPropertyNames(element, ["kind", "value"], parameterName);
                return new RoutedHttpSingleHeader(element.GetProperty("value").GetString()!);
            case "multiple":
                RoutedHttpValidation.RequireExactPropertyNames(element, ["kind", "values"], parameterName);
                var values = element.GetProperty("values");
                if (values.ValueKind != JsonValueKind.Array)
                {
                    throw new ArgumentException("Multiple HTTP values must be an array.", parameterName);
                }

                return new RoutedHttpMultipleHeader(
                    values.EnumerateArray().Select(static value => value.GetString()!).ToArray());
            default:
                throw new ArgumentException("The HTTP header-field kind is not closed.", parameterName);
        }
    }
}
