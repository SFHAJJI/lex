using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>The two admitted rights-policy evidence sources. Closed.</summary>
public enum EuLegalNoticeSource
{
    [JsonStringEnumMemberName("eur_lex_legal_notice")]
    EurLexLegalNotice = 1,
    [JsonStringEnumMemberName("commission_reuse_decision_2011_833")]
    CommissionReuseDecision2011833 = 2,
}

/// <summary>
/// One retained rights-policy capture. Decision 95 uses Commission Decision 2011/833/EU on the
/// Publications Office route. Retained legacy notice routes can be reconstructed into schema /3;
/// serialized /2 receipts are not accepted by the /3 reader.
/// </summary>
/// <remarks>
/// The Decision receipt is the policy the EUR-Lex notice cites, not the notice itself. Decision 95
/// records the accepted limit for Parliament and Council documents. No new EUR-Lex request is made.
/// FromRoute requires the exact source URI, a complete 200 on that origin, the source's media type
/// and, for the Decision, English XHTML negotiation. RoutedHttpEvidence already proves redirect
/// causality, robots and custody receipts. This type binds the terminal request by digest.
/// The body digest identifies this capture, not a stable policy version. The publisher's Date and
/// the observation clock are distinct; policy effective date and version remain typed absent
/// because no parser reads them from the page. ParseAndVerify reopens the canonical record; it
/// does not independently reopen the route or repeat its custody checks.
/// </remarks>
public sealed class EuLegalNoticeEvidence
{
    public const string SchemaId = "lex-eu-legal-notice-evidence/3";

    /// <summary>
    /// The legacy request R8 named, kept for retained-route reconstruction and source-profile identity.
    /// A legal-notice evidence type that could target an arbitrary
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

    public const string ReuseDecisionUri = "https://publications.europa.eu/resource/celex/32011D0833";

    public EuLegalNoticeSource Source { get; }

    private static string SourceToken(EuLegalNoticeSource source) => source switch
    {
        EuLegalNoticeSource.EurLexLegalNotice => "eur_lex_legal_notice",
        EuLegalNoticeSource.CommissionReuseDecision2011833 => "commission_reuse_decision_2011_833",
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    private static string SourceUri(EuLegalNoticeSource source) => source switch
    {
        EuLegalNoticeSource.EurLexLegalNotice => RequestedUri,
        EuLegalNoticeSource.CommissionReuseDecision2011833 => ReuseDecisionUri,
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    private static string SourceLanguage(EuLegalNoticeSource source) =>
        source == EuLegalNoticeSource.EurLexLegalNotice ? LanguageSelection : "eng";

    private readonly byte[] _canonicalBytes;
    private readonly string _canonicalSha256;

    private EuLegalNoticeEvidence(
        EuLegalNoticeSource source,
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
        Source = source;
        _ = SourceToken(source);
        RoutedEvidenceSha256 = RoutedHttpValidation.RequireSha256(
            routedEvidenceSha256,
            nameof(routedEvidenceSha256));
        EffectiveUri = RoutedHttpValidation.RequireAbsoluteHttpsUri(effectiveUri, nameof(effectiveUri));
        // The four null guards below are defensive, not reachable through either caller of this
        // private constructor: FromRoute always supplies a real RoutedHttpSingleHeader (matched by
        // pattern from the terminal hop) and real RoutedHttpHeaderField union instances (never a null
        // reference, only the closed Absent/Single/Multiple cases), and ParseAndVerify's
        // ParseHeaderField and the mediaTypeElement branch likewise always return a non-null instance
        // or throw first. Left as documented guards rather than assumptions.
        MediaType = mediaType ?? throw new ArgumentNullException(nameof(mediaType));
        var origin = RoutedHttpNetworkOrigin.FromUri(EffectiveUri);
        var pinnedOrigin = RoutedHttpNetworkOrigin.FromUri(SourceUri(source));
        var expectedMediaType = source == EuLegalNoticeSource.EurLexLegalNotice ? "text/html" : "application/xhtml+xml";
        if (origin.Host != pinnedOrigin.Host || origin.EffectivePort != pinnedOrigin.EffectivePort ||
            mediaType.Value.Split(';')[0].Trim() != expectedMediaType)
        {
            throw new ArgumentException("Rights evidence must retain its source's origin and media type.");
        }
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
    /// the route's own first hop, never against a caller's claim, and the terminal hop's own origin
    /// is checked against that same pinned URI's origin: a route that starts at the pinned URI but
    /// is redirected to a different host or port is refused rather than silently becoming
    /// <see cref="EffectiveUri"/>.
    /// </summary>
    public static EuLegalNoticeEvidence FromRoute(RoutedHttpEvidence evidence, HttpLogicalRequest request)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(request);
        // Defensive, not reachable through any current producer: RoutedHttpEvidence.Create already
        // refuses to mint an evidence object with fewer than one hop ("HTTP /4 evidence must retain
        // one to six route hops"), so no real RoutedHttpEvidence this method can be called with ever
        // has an empty Hops list. Left in place as a documented guard rather than an assumption.
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

        var source = evidence.Hops[0].RequestUri switch
        {
            RequestedUri => EuLegalNoticeSource.EurLexLegalNotice,
            ReuseDecisionUri => EuLegalNoticeSource.CommissionReuseDecision2011833,
            _ => throw new ArgumentException("Legal-notice evidence must route from an exact admitted rights URI.", nameof(evidence)),
        };
        if (source == EuLegalNoticeSource.CommissionReuseDecision2011833 &&
            (!HasHeader("accept", "application/xhtml+xml") || !HasHeader("accept-language", "eng")))
        {
            throw new ArgumentException("The reuse Decision requires exactly accept=application/xhtml+xml and accept-language=eng.", nameof(request));
        }

        bool HasHeader(string name, string value) =>
            request.Headers.Count(header => header.Name == name) == 1 &&
            request.Headers.Any(header => header.Name == name && header.Value == value);

        // The first-hop pin above only proves where the route started. A route that redirects away
        // to any other host or port would still pass it, and that other host's bytes would become
        // EffectiveUri and this type's captured evidence: R8 binds an official legal-notice URI at
        // both ends, so the session's admitted-URI set (whatever a caller chose to route through)
        // must not be the only guard between this type and an off-pin redirect target. Refuse unless
        // the terminal hop terminates on the pinned URI's own host and port. RoutedHttpHop.Create
        // already derives NetworkOrigin from each hop's own RequestUri, so this compares two
        // already-validated origins, never a caller's separate claim.
        //
        // An origin is scheme, host and port. RoutedHttpNetworkOrigin.EffectivePort carries the
        // third, and a probe during review proved a redirect from the pinned URI to
        // eur-lex.europa.eu on port 8443 was accepted as notice evidence before this line compared
        // it; the port is now part of the refusal (see
        // FromRouteRefusesATwoHopRouteThatRedirectsToTheSameHostOnADifferentPort). Scheme itself is
        // deliberately not part of the boolean check below: RoutedHttpNetworkOrigin.Scheme is a
        // hardcoded "https" on every instance (RoutedHttpValidation.RequireAbsoluteHttpsUri already
        // refuses any URI not spelled "https://" before RoutedHttpNetworkOrigin.FromUri can build one
        // from it, and both terminalHop.NetworkOrigin and pinnedOrigin are built that way), so
        // comparing the two Scheme values can never be false and would be exactly the
        // compares-a-constant-to-itself shape this project refuses to keep. It still appears in the
        // message below purely as a readable origin string, not as a condition.
        var pinnedOrigin = RoutedHttpNetworkOrigin.FromUri(SourceUri(source));
        if (!string.Equals(terminalHop.NetworkOrigin.Host, pinnedOrigin.Host, StringComparison.Ordinal) ||
            terminalHop.NetworkOrigin.EffectivePort != pinnedOrigin.EffectivePort)
        {
            throw new ArgumentException(
                $"Legal-notice evidence must terminate on the pinned R8 origin " +
                $"({pinnedOrigin.Scheme}://{pinnedOrigin.Host}:{pinnedOrigin.EffectivePort}); the " +
                $"terminal hop's origin is {terminalHop.NetworkOrigin.Scheme}://" +
                $"{terminalHop.NetworkOrigin.Host}:{terminalHop.NetworkOrigin.EffectivePort}.",
                nameof(evidence));
        }

        // The session seals a route whose body it could not read to the end (a short read against
        // the declared length, a body deadline, a mid-body transport failure) as an incomplete
        // route carrying the bytes it did retain. Those bytes are held and their digest is real,
        // but they are not the notice: R8's byte count and SHA-256 are of the captured page, not
        // of however much of it arrived. A 200 status and a text/html media type both survive a
        // truncation, so neither check below can see it. Refuse unless the route is complete,
        // exactly as EuDocumentFetchOutcome.Classify does for the Cellar route.
        if (evidence.Outcome is not CompleteHttpRouteOutcome)
        {
            throw new ArgumentException(
                "Legal-notice evidence requires a complete route; a route the session sealed as " +
                "incomplete or unobserved retains at most part of the page and is not the notice.",
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
                source == EuLegalNoticeSource.EurLexLegalNotice ? "text/html" : "application/xhtml+xml",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Legal-notice evidence must observe exactly one {(source == EuLegalNoticeSource.EurLexLegalNotice ? "text/html" : "application/xhtml+xml")} media type on the terminal hop.",
                nameof(evidence));
        }

        var routedEvidenceSha256 = Convert.ToHexString(
            SHA256.HashData(evidence.CopyCanonicalBytes())).ToLowerInvariant();

        return new EuLegalNoticeEvidence(
            source,
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
                    "schema", "source", "requested_uri", "effective_uri", "language_selection", "media_type",
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

            var source = root.GetProperty("source").GetString() switch
            {
                "eur_lex_legal_notice" => EuLegalNoticeSource.EurLexLegalNotice,
                "commission_reuse_decision_2011_833" => EuLegalNoticeSource.CommissionReuseDecision2011833,
                _ => throw new ArgumentException("Unknown rights evidence source.", nameof(canonicalBytes)),
            };

            if (!string.Equals(
                    root.GetProperty("requested_uri").GetString(),
                    SourceUri(source),
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Legal-notice evidence must name the exact R8 requested URI.",
                    nameof(canonicalBytes));
            }

            if (!string.Equals(
                    root.GetProperty("language_selection").GetString(),
                    SourceLanguage(source),
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
                source,
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
        writer.Raw(",\"source\":");
        writer.String(SourceToken(value.Source));
        writer.Raw(",\"requested_uri\":");
        writer.String(SourceUri(value.Source));
        writer.Raw(",\"effective_uri\":");
        writer.String(value.EffectiveUri);
        writer.Raw(",\"language_selection\":");
        writer.String(SourceLanguage(value.Source));
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
                // Defensive, not reachable while RoutedHttpHeaderField stays a closed union of
                // exactly Absent/Single/Multiple: the compiler already refuses any other case in the
                // switch above. Left as a documented guard against a future subtype being added here
                // without a matching write branch, not an assumption that one exists today.
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
