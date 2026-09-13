using System.Security.Cryptography;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// Shared fixtures for #419 slice 6a: body joins over one as-published act (in one run or in two
/// independent ones), rights observations in every state the Gazette channel must type, and the
/// typed retention of a fetched body - address, requests, route evidence and receipt - in the
/// verified shape and in every broken one the hostile tests need. Same recipes as
/// <c>LuxembourgBodyJoinTests</c> and <c>EuAnnexBodyDispositionTests</c>, parameterized.
/// </summary>
internal static class LuxembourgGazetteBodyFixtures
{
    public const string ActLoi1 = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo";
    public const string ActRgd2 = "http://data.legilux.public.lu/eli/etat/leg/rgd/2026/02/02/a2/jo";
    public const string ActLoi3 = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/03/03/a3/jo";
    public const string ActAmin5 = "http://data.legilux.public.lu/eli/etat/leg/amin/2026/05/05/a5/jo";
    public const string ActNeverHeld = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/09/09/a9/jo";

    public const string LanguageFra = "http://publications.europa.eu/resource/authority/language/FRA";
    public const string LanguageDeu = "http://publications.europa.eu/resource/authority/language/DEU";
    public const string FormatXml = "http://data.legilux.public.lu/resource/authority/user-format/xml";
    public const string FormatPdfA = "http://data.legilux.public.lu/resource/authority/user-format/pdfa";
    public const string FormatPdf = "http://data.legilux.public.lu/resource/authority/user-format/pdf";
    public const string CcBy40 = "http://creativecommons.org/licenses/by/4.0/";
    public const string LicenceScl = "http://data.legilux.public.lu/resource/authority/license/licenceSCL";
    public const string EmptyDigest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    public const string GazetteMediaType = "application/pdf";

    public static SourceArtifactRef Run { get; } = Artifact("cbe6e64c-789a-4c73-854d-19e464728a50", '1');
    public static SourceArtifactRef SparqlEnumeration { get; } = Artifact("388b94c5-c812-494a-8414-11659c742d7f", '3');
    public static SourceArtifactRef InFileEnumeration { get; } = Artifact("43b2af70-9a13-4a0f-a202-f5bb00199239", '4');
    public static SourceArtifactRef SparqlEvidence { get; } = Artifact("5f4c1a2e-0b7d-4e7f-9c1a-2b3c4d5e6f70", '5');
    public static SourceArtifactRef InFileEvidence { get; } = Artifact("6a5b2c3d-1e8f-4a0b-8d2c-3e4f5a6b7c81", '6');
    public static SourceArtifactRef OtherSparqlEvidence { get; } = Artifact("8c7d4e5f-3a01-4c2d-af4e-5a6b7c8d9ea3", '8');
    public static SourceArtifactRef RouteRun { get; } = Artifact("9d8e7f60-4b12-4d3e-bf5a-6b7c8d9e0fa4", '9');

    public static string ManifestationOf(string act, string language, string format) =>
        act + "/" + language + "/" + format;

    public static string ItemOf(string act, string language, string format) =>
        "http://data.legilux.public.lu/filestore/" + act[(act.LastIndexOf("leg/", StringComparison.Ordinal) + 4)..]
            .Replace('/', '-') + "-" + language + "." + format;

    /// <summary>One structurally consistent listing of <paramref name="act"/>, unless told otherwise.</summary>
    public static LuxembourgWemiCandidate Candidate(
        string act,
        string language,
        string format,
        string? rootOverride = null,
        LuxembourgWemiCandidateDisposition disposition = LuxembourgWemiCandidateDisposition.StructurallyConsistent,
        IReadOnlyList<LuxembourgWemiBlockerCode>? blockers = null,
        SourceArtifactRef? observationRef = null)
    {
        var languageIri = language == "fr" ? LanguageFra : LanguageDeu;
        var formatIri = format switch { "pdfa" => FormatPdfA, "pdf" => FormatPdf, _ => FormatXml };
        return new LuxembourgWemiCandidate(
            rootOverride ?? act,
            act + "/" + language,
            ManifestationOf(act, language, format),
            ItemOf(act, language, format),
            languageIri,
            formatIri,
            observationRef ?? Run,
            disposition,
            blockers ?? []);
    }

    public static LuxembourgWemiTopologyResolution Topology(params LuxembourgWemiCandidate[] candidates) => new(
        candidates,
        [],
        [],
        candidates.Select(static c => c.ExpressionIri).Distinct(StringComparer.Ordinal)
            .OrderBy(static v => v, StringComparer.Ordinal).ToArray(),
        candidates.Select(static c => c.ManifestationIri).Distinct(StringComparer.Ordinal)
            .OrderBy(static v => v, StringComparer.Ordinal).ToArray());

    public static LuxembourgRightsChannelObservation Sparql(string manifestationIri, params string[] licences) =>
        new(manifestationIri, Run, SparqlEvidence, licences);

    public static LuxembourgRightsChannelObservation InFileRead(string manifestationIri, params string[] licences) =>
        new(manifestationIri, Run, InFileEvidence, licences);

    /// <summary>Both channels agree on the admitting licence for every listed manifestation.</summary>
    public static LuxembourgBodyJoinResolution JoinAgreedCcBy(string act, params LuxembourgWemiCandidate[] candidates) =>
        LuxembourgBodyJoin.Resolve(
            act,
            Run,
            Topology(candidates),
            new LuxembourgSparqlRightsChannelObservations(
                Run, SparqlEnumeration, candidates.Select(c => Sparql(c.ManifestationIri, CcBy40)).ToArray()),
            new LuxembourgInFileRightsChannelObservations(
                Run, InFileEnumeration, candidates.Select(c => InFileRead(c.ManifestationIri, CcBy40)).ToArray()));

    /// <summary>
    /// The same agreed CC BY fact, observed by an INDEPENDENT execution: every run-minted reference
    /// - the run identity, both enumeration refs, both evidence refs, the candidates' observation
    /// ref - is minted from <paramref name="seed"/>, so two seeds are two runs over one fact.
    /// </summary>
    public static LuxembourgBodyJoinResolution JoinAgreedCcByFromRun(string act, char seed, params (string language, string format)[] listings)
    {
        var run = ArtifactOf(seed, seed);
        var candidates = listings.Select(l => Candidate(act, l.language, l.format, observationRef: run)).ToArray();
        return LuxembourgBodyJoin.Resolve(
            act,
            run,
            Topology(candidates),
            new LuxembourgSparqlRightsChannelObservations(
                run, ArtifactOf(seed, 'a'),
                candidates.Select(c => new LuxembourgRightsChannelObservation(c.ManifestationIri, run, ArtifactOf(seed, 'b'), [CcBy40])).ToArray()),
            new LuxembourgInFileRightsChannelObservations(
                run, ArtifactOf(seed, 'c'),
                candidates.Select(c => new LuxembourgRightsChannelObservation(c.ManifestationIri, run, ArtifactOf(seed, 'd'), [CcBy40])).ToArray()));
    }

    /// <summary>Both channels say the publisher marked every listed manifestation not reusable.</summary>
    public static LuxembourgBodyJoinResolution JoinLicenceScl(string act, params LuxembourgWemiCandidate[] candidates) =>
        LuxembourgBodyJoin.Resolve(
            act,
            Run,
            Topology(candidates),
            new LuxembourgSparqlRightsChannelObservations(
                Run, SparqlEnumeration, candidates.Select(c => Sparql(c.ManifestationIri, LicenceScl)).ToArray()),
            new LuxembourgInFileRightsChannelObservations(
                Run, InFileEnumeration, candidates.Select(c => InFileRead(c.ManifestationIri, LicenceScl)).ToArray()));

    /// <summary>
    /// The realistic PDF case today: SPARQL says CC BY; the in-file channel ran, could not read the
    /// PDF, and rejected the reading (unsupported representation) - so it holds no row and names the
    /// manifestation among its rejected ones.
    /// </summary>
    public static LuxembourgBodyJoinResolution JoinSecondChannelCannotReadPdf(string act, params LuxembourgWemiCandidate[] candidates) =>
        LuxembourgBodyJoin.Resolve(
            act,
            Run,
            Topology(candidates),
            new LuxembourgSparqlRightsChannelObservations(
                Run, SparqlEnumeration, candidates.Select(c => Sparql(c.ManifestationIri, CcBy40)).ToArray()),
            new LuxembourgInFileRightsChannelObservations(
                Run,
                InFileEnumeration,
                [],
                acquisitionCompleted: true,
                rejectedManifestationIris: candidates.Select(static c => c.ManifestationIri).ToArray()));

    /// <summary>
    /// Agreed licences on both channels, with the SPARQL channel's evidence ref and the licence
    /// lists chosen by the caller (each list ordinal-sorted and unique, as the observation requires).
    /// </summary>
    public static LuxembourgBodyJoinResolution JoinAgreedCcByWith(
        string act,
        SourceArtifactRef sparqlEvidence,
        IReadOnlyList<string> sparqlLicences,
        IReadOnlyList<string> inFileLicences,
        params LuxembourgWemiCandidate[] candidates) =>
        LuxembourgBodyJoin.Resolve(
            act,
            Run,
            Topology(candidates),
            new LuxembourgSparqlRightsChannelObservations(
                Run, SparqlEnumeration,
                candidates.Select(c => new LuxembourgRightsChannelObservation(c.ManifestationIri, Run, sparqlEvidence, sparqlLicences)).ToArray()),
            new LuxembourgInFileRightsChannelObservations(
                Run, InFileEnumeration,
                candidates.Select(c => new LuxembourgRightsChannelObservation(c.ManifestationIri, Run, InFileEvidence, inFileLicences)).ToArray()));

    // ---- the typed retention: address, requests, route, receipt ----

    /// <summary>The address for one listing: its item as the store file URI, its format, unstated legal value, its act's ELI page path.</summary>
    public static LuxembourgDocumentFetchAddress AddressOf(
        LuxembourgBodyCandidateResolution listing,
        string? itemOverride = null,
        LuxembourgUserFormatToken? formatOverride = null,
        string? actOverride = null) =>
        LuxembourgDocumentFetchAddress.Create(
            LuxembourgFileUri.RequireValid(itemOverride ?? listing.WemiCandidate.ItemIri),
            formatOverride ?? LuxembourgAuthorityIri.TryParseUserFormat(listing.WemiCandidate.FormatIri)!.Value,
            LuxembourgLegalValue.Unstated,
            new Uri(actOverride ?? listing.WemiCandidate.RootIri, UriKind.Absolute).AbsolutePath);

    /// <summary>A GET with the adapter's one header and no negotiation, unless headers are given.</summary>
    public static HttpLogicalRequest Request(string uri, IReadOnlyList<HttpLogicalRequestHeader>? headers = null) =>
        HttpLogicalRequest.Create(
            uri,
            HttpRequestMethod.Get,
            headers ?? [new HttpLogicalRequestHeader("user-agent", "lex-v3-tests")],
            new HttpLogicalRequestBody(0, EmptyDigest),
            Digest('1'),
            Digest('2'));

    /// <summary>A custody receipt for bytes whose digest is <paramref name="digestCharacter"/> x 64.</summary>
    public static DurableBlobWriteReceipt Receipt(char digestCharacter, long length = 3)
    {
        var digest = length == 0 ? EmptyDigest : new string(digestCharacter, 64);
        var blob = new DurableBlobRef(CustodySchemaIds.DurableBlobRef, digest, length, CustodyClass.NightlyFloor90d);
        var time = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        return new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            blob,
            new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                blob,
                CustodyVerificationProfile.ImmutableObject1,
                new Guid("0f8b2c4d-6e7a-4b9c-8d1e-2f3a4b5c6d7e"),
                CustodyProtection.LockedTime,
                time,
                time.AddDays(91)));
    }

    public static RoutedHttpResponseHeaders ResponseHeaders(string mediaType = GazetteMediaType, ulong contentLength = 3)
    {
        var absent = new RoutedHttpAbsentHeader();
        return new RoutedHttpResponseHeaders(
            new RoutedHttpSingleHeader(mediaType),
            new RoutedHttpSingleHeader(contentLength.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            absent, absent, absent, absent, absent, absent, absent, absent, absent, absent, absent);
    }

    public static RoutedHttpResponseHeaders RedirectHeaders(string location)
    {
        var absent = new RoutedHttpAbsentHeader();
        return new RoutedHttpResponseHeaders(
            absent,
            new RoutedHttpSingleHeader("0"),
            absent, absent, absent, absent, absent,
            new RoutedHttpSingleHeader(location),
            absent, absent, absent, absent, absent);
    }

    /// <summary>
    /// A single-hop 200 route: the request to <paramref name="requestUri"/>, the media type and
    /// length given, the transported bytes' digest and the receipt the hop names - each overridable
    /// so a hostile test can break exactly one relation.
    /// </summary>
    public static RoutedHttpEvidence Route(
        HttpLogicalRequest request,
        string requestUri,
        DurableBlobWriteReceipt namedReceipt,
        string mediaType = GazetteMediaType,
        ulong length = 3,
        string? transportedSha256 = null,
        SourceArtifactRef? runIdentity = null)
    {
        var sha = transportedSha256 ?? namedReceipt.Reference.ContentSha256;
        var hop = RoutedHttpHop.Create(
            0,
            "urn:uuid:aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            null,
            Sha256(request.CopyCanonicalBytes()),
            requestUri,
            200,
            ResponseHeaders(mediaType, length),
            "2026-09-13T12:00:00.0000000Z",
            "2026-09-13T12:00:01.0000000Z",
            new DeclaredContentLengthHttpCompletion(length),
            length,
            sha,
            DurableBlobWriteReceiptDigest.Of(namedReceipt),
            length,
            sha);
        return RoutedHttpEvidence.Create(
            runIdentity ?? RouteRun,
            1,
            0,
            [hop],
            new CompleteHttpRouteOutcome(),
            new Dictionary<string, DurableBlobWriteReceipt> { [hop.ObservationId] = namedReceipt });
    }

    /// <summary>A 301 at the address followed by a 200 at <paramref name="terminalUri"/>, the terminal hop naming the receipt.</summary>
    public static RoutedHttpEvidence RedirectRoute(
        HttpLogicalRequest officialRequest,
        string officialUri,
        HttpLogicalRequest terminalRequest,
        string terminalUri,
        DurableBlobWriteReceipt receipt)
    {
        var firstReceipt = Receipt('0', 0);
        const string firstHopId = "urn:uuid:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
        var first = RoutedHttpHop.Create(
            0, firstHopId, null,
            Sha256(officialRequest.CopyCanonicalBytes()),
            officialUri, 301, RedirectHeaders(terminalUri),
            "2026-09-13T12:00:00.0000000Z", "2026-09-13T12:00:01.0000000Z",
            new DeclaredContentLengthHttpCompletion(0), 0, EmptyDigest,
            DurableBlobWriteReceiptDigest.Of(firstReceipt), 0, EmptyDigest);
        var terminal = RoutedHttpHop.Create(
            1, "urn:uuid:cccccccc-cccc-4ccc-8ccc-cccccccccccc", firstHopId,
            Sha256(terminalRequest.CopyCanonicalBytes()),
            terminalUri, 200, ResponseHeaders(),
            "2026-09-13T12:00:02.0000000Z", "2026-09-13T12:00:03.0000000Z",
            new DeclaredContentLengthHttpCompletion(3), 3, receipt.Reference.ContentSha256,
            DurableBlobWriteReceiptDigest.Of(receipt), 3, receipt.Reference.ContentSha256);
        return RoutedHttpEvidence.Create(
            RouteRun, 1, 0, [first, terminal], new CompleteHttpRouteOutcome(),
            new Dictionary<string, DurableBlobWriteReceipt>
            {
                [first.ObservationId] = firstReceipt,
                [terminal.ObservationId] = receipt,
            });
    }

    /// <summary>The verified shape: this listing's address, its non-negotiating fetch, a complete pdf transfer, the receipt the hop names.</summary>
    public static LuxembourgGazetteBodyRetention Retention(
        LuxembourgBodyCandidateResolution listing, char byteFill = 'a', SourceArtifactRef? routeRun = null)
    {
        var address = AddressOf(listing);
        var request = Request(address.FetchUri.AbsoluteUri);
        var receipt = Receipt(byteFill);
        return new LuxembourgGazetteBodyRetention(
            address, request, request, Route(request, address.FetchUri.AbsoluteUri, receipt, runIdentity: routeRun), receipt);
    }

    public static string Digest(char value) => new(value, 64);

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static SourceArtifactRef Artifact(string id, char digestCharacter) =>
        new("urn:uuid:" + id, new string(digestCharacter, 64));

    /// <summary>An artifact ref whose resource id and digest are both minted from the fills, for independent-run fixtures.</summary>
    public static SourceArtifactRef ArtifactOf(char resourceFill, char digestFill) => new(
        $"urn:uuid:{new string(resourceFill, 8)}-{new string(resourceFill, 4)}-4{new string(resourceFill, 3)}-8{new string(resourceFill, 3)}-{new string(resourceFill, 12)}",
        Digest(digestFill));
}
