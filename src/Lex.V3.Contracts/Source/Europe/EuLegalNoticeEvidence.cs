using System.Security.Cryptography;
using System.Text.Json;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// One bounded GET of the reviewed EUR-Lex legal notice, frozen as the class-level evidence R8
/// requires before any content class or exception channel may carry a positive rights disposition.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is.</b> D1-01 Candidate 5 R8 (lines 763-784) names one exact request as policy
/// evidence: a bounded GET of
/// <c>https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en</c>, binding its
/// requested and effective URI, redirect edges, response bytes, digest, media type, language
/// selection, observation time, effective-or-observed date, and source-policy version. It says this
/// request "is allowed solely as legal-policy evidence even though EUR-Lex is forbidden as an
/// automated legal-body fallback" under Decision 23 (<c>DECISIONS.md</c> item 23: EUR-Lex sits
/// behind an AWS WAF challenge for non-browser clients, so the Union corpus goes Cellar-native via
/// <c>publications.europa.eu</c> instead; EUR-Lex itself is never a body source).
/// </para>
/// <para>
/// <b>Refreeze, 2026-09-03 (coordination/EVENTS.md event
/// lex-event-20260903T173221003Z-887bf79258394fe8a8791f77effa758e).</b> The prior version at
/// 93673f1e was returned NOT READY: it bound a digest of bytes that nothing held, because it carried
/// no custody receipt, no robots hop and no redirect policy of its own, while the session already
/// produces held routed evidence with exactly those facts proven. Decisions 75 and 78 hold that a
/// run retains what it depends on; this type stops re-deriving a parallel, unheld observation and
/// becomes a door over a proven route instead, the same shape
/// <see cref="RepresentationChainObservation.FromRoute"/> already established for item 9: the only
/// production path is <see cref="FromRoute"/>, and it takes a real <see cref="RoutedHttpEvidence"/>
/// together with the <see cref="HttpLogicalRequest"/> that produced it. A route that never actually
/// happened, or whose bytes were never actually retained, cannot reach this type; <c>RoutedHttpHop</c>
/// and <see cref="RoutedHttpEvidence.Create"/> (Decision 80's receipt gate) already proved both
/// before a <see cref="RoutedHttpEvidence"/> could exist at all.
/// </para>
/// <para>
/// <b>What was deleted.</b> The prior version's own parallel <c>EuLegalNoticeRedirectHop</c> model,
/// with its own ordinal, Location-chain and termination checks, is gone: <see cref="RoutedHttpEvidence.Create"/>
/// (via <c>RoutedHttpEvidenceDocument.CreateFromVerifiedHops</c>) already enforces hop ordering,
/// antecedent linkage, exact Location causality, and redirect-loop refusal for the route that
/// produces the evidence this type now takes as input, so re-checking any of that here would only be
/// restating a fact the route already proved. <see cref="FromRoute"/> reads the redirect chain's
/// start and end (the first hop's <c>RequestUri</c> and the terminal hop's own facts) and nothing in
/// between.
/// </para>
/// <para>
/// <b>The R8 field list, placed with typed absence.</b> R8 requires "byte count, SHA-256, media
/// type, language selection, observation time, effective or observed date, and source-policy
/// version." This type places each field as follows, per the refreeze objection:
/// <list type="bullet">
/// <item><description><see cref="LanguageSelection"/> is stated explicitly as the fixed constant
/// <c>"en"</c>, not parsed: <see cref="RequestedUri"/> is pinned to <c>locale=en</c>, so the
/// language this request selects is a structural fact of the pinned URI, not a caller's claim.
/// </description></item>
/// <item><description><see cref="ObservedDate"/> is read directly from the terminal hop's own
/// <c>Date</c> response header, present or absent exactly as the publisher sent it. The live capture
/// behind this type (<c>coordination/measurements/2026-09-03-eu-legal-notice-capture.md</c>)
/// observed a <c>Date</c> header and no <c>Last-Modified</c>; that measurement is why this type
/// reads R8's "observation time" and the "observed" half of "effective or observed date" from the
/// one date-shaped fact the publisher actually supplied, rather than from a caller-asserted capture
/// clock nothing checks against the route.</description></item>
/// <item><description><see cref="PolicyEffectiveDate"/> and <see cref="SourcePolicyVersion"/> are
/// present-or-typed-absent fields, following the same closed union
/// (<see cref="RoutedHttpAbsentHeader"/> / <see cref="RoutedHttpSingleHeader"/>) this codebase
/// already uses for a header the publisher may or may not send, so "not observed" is recorded rather
/// than silently defaulted. <see cref="FromRoute"/> has no page-content parser to read a policy's own
/// effective date or version out of the notice prose, so every instance it mints today carries both
/// as <see cref="RoutedHttpAbsentHeader"/>; this is honest for what one bounded GET without a body
/// parser can support, and the union gives the "effective or observed date" alternative and the
/// version a place to be filled in later without widening this type's closed shape. The R8 matrix
/// slice's carried condition (change detection needs an identity that survives per-request token
/// churn, plus maximum policy age and a revalidation rule) is explicitly not addressed here; it
/// stays open at the matrix layer this type does not own.</description></item>
/// </list>
/// </para>
/// <para>
/// <b>The new custody-proof fields.</b> <see cref="DurableWriteReceiptSha256"/> is taken directly
/// from the terminal hop, which Decision 80's receipt gate at <see cref="RoutedHttpEvidence.Create"/>
/// already proved names bytes actually held in custody; this is exactly the fact the prior version
/// could not carry, because it was never routed through that pipeline.
/// <see cref="RoutedEvidenceSha256"/> references the routed evidence this record was minted from, by
/// its own canonical digest, computed by <see cref="FromRoute"/> from the real
/// <see cref="RoutedHttpEvidence"/> object rather than accepted as a caller-supplied string, so the
/// reference cannot be forged independently of the object it names: see
/// <c>RoutedEvidenceSha256ChangesWithTheReferencedRouteRatherThanBeingACopiedLiteral</c> for the
/// driving test.
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
/// those, because one bounded GET, or even two, cannot establish them.
/// </para>
/// </remarks>
public sealed class EuLegalNoticeEvidence
{
    public const string SchemaId = "lex-eu-legal-notice-evidence/2";

    /// <summary>
    /// The exact request R8 names. <see cref="FromRoute"/> refuses any route whose first hop was not
    /// requested at this exact URI: a legal-notice evidence type that could target an arbitrary
    /// EUR-Lex URI could just as easily be pointed at a law-body page, which is exactly the
    /// corpus-source use Decision 23 forbids.
    /// </summary>
    public const string RequestedUri =
        "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en";

    /// <summary>R8's language selection, stated explicitly. See the type remarks.</summary>
    public const string LanguageSelection = "en";

    /// <summary>
    /// The retained-byte ceiling for a captured notice page. The two live captures behind this type
    /// were 135,428 and 135,427 bytes; 4 MiB is generously above that observed size while remaining
    /// far below the size of any real corpus manifestation this project handles (Formex packages and
    /// consolidated acts run to tens of megabytes; <c>CustodyBounds.MaxObjectBytes</c>, the routed
    /// acquisition pipeline's own retained-entity ceiling, is 256 MiB). A law body large enough to
    /// matter cannot fit this type even if every other check were bypassed, which is the second half
    /// of the structural boundary alongside the fixed <see cref="RequestedUri"/>.
    /// </summary>
    public const int MaximumNoticeBytes = 4 * 1024 * 1024;

    private readonly byte[] _canonicalBytes;
    private readonly string _canonicalSha256;

    private EuLegalNoticeEvidence(
        string routedEvidenceSha256,
        string effectiveUri,
        RoutedHttpSingleHeader mediaType,
        RoutedHttpHeaderField observedDate,
        RoutedHttpHeaderField policyEffectiveDate,
        RoutedHttpHeaderField sourcePolicyVersion,
        ulong byteLength,
        string sha256,
        string durableWriteReceiptSha256,
        string capturedAt)
    {
        RoutedEvidenceSha256 = RoutedHttpValidation.RequireSha256(
            routedEvidenceSha256,
            nameof(routedEvidenceSha256));
        EffectiveUri = RoutedHttpValidation.RequireAbsoluteHttpsUri(effectiveUri, nameof(effectiveUri));
        MediaType = mediaType ?? throw new ArgumentNullException(nameof(mediaType));
        ObservedDate = observedDate ?? throw new ArgumentNullException(nameof(observedDate));
        PolicyEffectiveDate =
            policyEffectiveDate ?? throw new ArgumentNullException(nameof(policyEffectiveDate));
        SourcePolicyVersion =
            sourcePolicyVersion ?? throw new ArgumentNullException(nameof(sourcePolicyVersion));

        if (byteLength == 0 || byteLength > MaximumNoticeBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteLength),
                byteLength,
                $"A captured notice must retain between 1 and {MaximumNoticeBytes} bytes.");
        }

        ByteLength = byteLength;
        Sha256 = RoutedHttpValidation.RequireSha256(sha256, nameof(sha256));
        DurableWriteReceiptSha256 = RoutedHttpValidation.RequireSha256(
            durableWriteReceiptSha256,
            nameof(durableWriteReceiptSha256));
        CapturedAt = RoutedHttpValidation.RequireTimestamp(capturedAt, nameof(capturedAt));

        _canonicalBytes = WriteCanonicalBytes(this);
        _canonicalSha256 = Convert.ToHexString(SHA256.HashData(_canonicalBytes)).ToLowerInvariant();
    }

    public string Schema => SchemaId;

    /// <summary>
    /// The canonical SHA-256 of the exact <see cref="RoutedHttpEvidence"/> this record was minted
    /// from, computed by <see cref="FromRoute"/> from the object itself rather than accepted as a
    /// caller-supplied string. This is the reference the refreeze objection asked for: the R8 record
    /// names the routed evidence it depends on by its own canonical digest, rather than restating
    /// fields a reader has to trust were transcribed correctly.
    /// </summary>
    public string RoutedEvidenceSha256 { get; }

    /// <summary>The route's own terminal <c>request_uri</c>. Equal to <see cref="RequestedUri"/> when no redirect occurred.</summary>
    public string EffectiveUri { get; }

    /// <summary>
    /// The terminal hop's <c>Content-Type</c>, required to be exactly one observed value whose media
    /// type is <c>text/html</c>; <see cref="FromRoute"/> refuses a route that does not observe this.
    /// </summary>
    public RoutedHttpSingleHeader MediaType { get; }

    /// <summary>
    /// The terminal hop's <c>Date</c> header: R8's "observation time" and the "observed" half of
    /// "effective or observed date", read from the one date-shaped fact the publisher actually sent.
    /// See the type remarks for why this is not the same as a caller-asserted capture clock.
    /// </summary>
    public RoutedHttpHeaderField ObservedDate { get; }

    /// <summary>
    /// R8's "effective" half of "effective or observed date": present when a future page-content
    /// parser supplies one, typed absent otherwise. Every instance <see cref="FromRoute"/> mints
    /// today carries this absent; see the type remarks.
    /// </summary>
    public RoutedHttpHeaderField PolicyEffectiveDate { get; }

    /// <summary>
    /// R8's source-policy version: present when a future page-content parser supplies one, typed
    /// absent otherwise. Every instance <see cref="FromRoute"/> mints today carries this absent; see
    /// the type remarks.
    /// </summary>
    public RoutedHttpHeaderField SourcePolicyVersion { get; }

    public ulong ByteLength { get; }

    /// <summary>
    /// The SHA-256 of the captured response bytes for this one observation, taken from the terminal
    /// hop. Not a content fingerprint and not a change-detection key: see the type-level remarks on
    /// why one bounded GET cannot support either reading.
    /// </summary>
    public string Sha256 { get; }

    /// <summary>
    /// The terminal hop's own custody write-receipt digest: proof, not restatement, that the exact
    /// bytes behind <see cref="Sha256"/> are actually held. This is the field the prior, pre-refreeze
    /// version of this type could not carry, because it was never routed through the pipeline that
    /// produces one.
    /// </summary>
    public string DurableWriteReceiptSha256 { get; }

    /// <summary>The terminal hop's own <c>terminal_observed_at</c>: the proven capture clock.</summary>
    public string CapturedAt { get; }

    /// <summary>The SHA-256 of this evidence object's own canonical bytes, for <see cref="ToArtifactRef"/>.</summary>
    public string CanonicalSha256 => _canonicalSha256;

    /// <summary>
    /// The only production door. <paramref name="evidence"/> is a real routed evidence document,
    /// already proven by <see cref="RoutedHttpEvidence.Create"/>'s receipt gate to name bytes
    /// actually held in custody; <paramref name="request"/> is the logical request that route was
    /// actually sent under, and it must be the exact request the terminal hop names, not merely one
    /// that happens to share a URI with it. The pinned <see cref="RequestedUri"/> is checked against
    /// the route's own first hop, never against a caller's claim.
    /// </summary>
    public static EuLegalNoticeEvidence FromRoute(RoutedHttpEvidence evidence, HttpLogicalRequest request)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(request);
        if (evidence.Hops.Count == 0)
        {
            throw new ArgumentException(
                "A route with no hops observed nothing to mint.", nameof(evidence));
        }

        var terminalHop = evidence.Hops[^1];

        // Same digest tie as RepresentationChainObservation.FromRoute: not method equality alone,
        // the exact bytes the terminal hop committed to sending.
        var requestDigest = Convert.ToHexString(
            SHA256.HashData(request.CopyCanonicalBytes())).ToLowerInvariant();
        if (!string.Equals(requestDigest, terminalHop.LogicalRequestSha256, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The logical request is not the one the terminal hop actually sent.",
                nameof(request));
        }

        if (request.Method != HttpRequestMethod.Get)
        {
            throw new ArgumentException(
                "Legal-notice evidence can only be minted from a GET; R8 names one bounded GET.",
                nameof(request));
        }

        if (!string.Equals(evidence.Hops[0].RequestUri, RequestedUri, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Legal-notice evidence must route from the exact R8 URI; " +
                $"{evidence.Hops[0].RequestUri} is not {RequestedUri}.",
                nameof(evidence));
        }

        if (terminalHop.Status != 200)
        {
            throw new ArgumentException(
                "Only a complete 200 response over the effective URI is captured notice evidence; " +
                "this type does not model a blocked, redirected-without-following, or failed attempt.",
                nameof(evidence));
        }

        if (terminalHop.Headers.ContentType is not RoutedHttpSingleHeader mediaType ||
            !string.Equals(
                mediaType.Value.Split(';')[0].Trim(),
                "text/html",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Legal-notice evidence must observe exactly one text/html media type on the terminal hop.",
                nameof(evidence));
        }

        var routedEvidenceSha256 = Convert.ToHexString(
            SHA256.HashData(evidence.CopyCanonicalBytes())).ToLowerInvariant();

        return new EuLegalNoticeEvidence(
            routedEvidenceSha256,
            terminalHop.RequestUri,
            mediaType,
            terminalHop.Headers.Date,
            new RoutedHttpAbsentHeader(),
            new RoutedHttpAbsentHeader(),
            terminalHop.Length,
            terminalHop.Sha256,
            terminalHop.DurableWriteReceiptSha256,
            terminalHop.TerminalObservedAt);
    }

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
                    "schema", "requested_uri", "effective_uri", "language_selection", "media_type",
                    "observed_date", "policy_effective_date", "source_policy_version", "byte_length",
                    "sha256", "durable_write_receipt_sha256", "routed_evidence_sha256", "captured_at",
                ],
                nameof(canonicalBytes));
            if (!string.Equals(root.GetProperty("schema").GetString(), SchemaId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Legal-notice evidence has the wrong schema.",
                    nameof(canonicalBytes));
            }

            if (!string.Equals(
                    root.GetProperty("requested_uri").GetString(),
                    RequestedUri,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Legal-notice evidence must name the exact R8 requested URI.",
                    nameof(canonicalBytes));
            }

            if (!string.Equals(
                    root.GetProperty("language_selection").GetString(),
                    LanguageSelection,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Legal-notice evidence must name the exact R8 language selection.",
                    nameof(canonicalBytes));
            }

            var mediaTypeElement = root.GetProperty("media_type");
            RoutedHttpValidation.RequireExactPropertyNames(
                mediaTypeElement,
                ["kind", "value"],
                nameof(canonicalBytes));
            if (!string.Equals(
                    mediaTypeElement.GetProperty("kind").GetString(),
                    "single",
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Legal-notice media type must be one observed single header value.",
                    nameof(canonicalBytes));
            }

            var rebuilt = new EuLegalNoticeEvidence(
                root.GetProperty("routed_evidence_sha256").GetString()!,
                root.GetProperty("effective_uri").GetString()!,
                new RoutedHttpSingleHeader(mediaTypeElement.GetProperty("value").GetString()!),
                ParseHeaderField(root.GetProperty("observed_date"), nameof(canonicalBytes)),
                ParseHeaderField(root.GetProperty("policy_effective_date"), nameof(canonicalBytes)),
                ParseHeaderField(root.GetProperty("source_policy_version"), nameof(canonicalBytes)),
                root.GetProperty("byte_length").GetUInt64(),
                root.GetProperty("sha256").GetString()!,
                root.GetProperty("durable_write_receipt_sha256").GetString()!,
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
        writer.Raw(",\"language_selection\":");
        writer.String(LanguageSelection);
        writer.Raw(",\"media_type\":{\"kind\":\"single\",\"value\":");
        writer.String(value.MediaType.Value);
        writer.Raw("},\"observed_date\":");
        WriteHeaderField(writer, value.ObservedDate);
        writer.Raw(",\"policy_effective_date\":");
        WriteHeaderField(writer, value.PolicyEffectiveDate);
        writer.Raw(",\"source_policy_version\":");
        WriteHeaderField(writer, value.SourcePolicyVersion);
        writer.Raw(",\"byte_length\":");
        writer.UInt64(value.ByteLength);
        writer.Raw(",\"sha256\":");
        writer.String(value.Sha256);
        writer.Raw(",\"durable_write_receipt_sha256\":");
        writer.String(value.DurableWriteReceiptSha256);
        writer.Raw(",\"routed_evidence_sha256\":");
        writer.String(value.RoutedEvidenceSha256);
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
