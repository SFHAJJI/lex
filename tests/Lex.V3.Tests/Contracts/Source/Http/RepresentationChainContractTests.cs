using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Http;

[TestClass]
public sealed class RepresentationChainContractTests
{
    private const string RequestedUri = "https://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/a1/jo";
    private const string EffectiveUri = "https://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/a1/jo/fr";
    private const string OtherUri = "https://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/a1/jo/en";
    private static readonly string Digest64 = new string('0', 63) + "1";

    [TestMethod]
    public void AQualifyingSequenceABAEmitsExactlyTwoReplacementEventsInOrder()
    {
        var chain = OpenChain();

        var a1 = MustAppend(chain, CandidateObservation(Observation("a1"), digest: Digest('a'), length: 3));
        Assert.AreEqual(RepresentationChainAppendDisposition.BaselineEstablished, a1.Disposition);
        Assert.AreSame(a1, chain.CurrentTrustedBaseline);
        Assert.AreEqual(0, chain.ReplacementEvents.Count);

        var b = MustAppend(chain, CandidateObservation(Observation("b"), digest: Digest('b'), length: 5));
        Assert.AreEqual(RepresentationChainAppendDisposition.ReplacementRecorded, b.Disposition);
        Assert.AreSame(b, chain.CurrentTrustedBaseline);
        Assert.AreEqual(1, chain.ReplacementEvents.Count);
        Assert.AreSame(a1, chain.ReplacementEvents[0].Predecessor);
        Assert.AreSame(b, chain.ReplacementEvents[0].Replacement);

        var a2 = MustAppend(chain, CandidateObservation(Observation("a2"), digest: Digest('a'), length: 3));
        Assert.AreEqual(RepresentationChainAppendDisposition.ReplacementRecorded, a2.Disposition);
        Assert.AreSame(a2, chain.CurrentTrustedBaseline);
        Assert.AreEqual(2, chain.ReplacementEvents.Count);
        Assert.AreSame(b, chain.ReplacementEvents[1].Predecessor);
        Assert.AreSame(a2, chain.ReplacementEvents[1].Replacement);

        CollectionAssert.AreEqual(
            new[] { a1.ObservationId, b.ObservationId, a2.ObservationId },
            chain.History.Select(static entry => entry.ObservationId).ToArray());
    }

    [TestMethod]
    public void ASameLengthDifferentDigestCandidateIsStillARecordedReplacement()
    {
        // Byte count alone is not the comparison: two representations of equal length but
        // different content must still be a `file_replaced` event, never a silent
        // baseline_confirmed_unchanged. This is the half of the (byte_count, sha256) pair that a
        // length-only comparison would miss.
        var chain = OpenChain();
        var baseline = MustAppend(chain, CandidateObservation(Observation("same-length-a"), digest: Digest('a'), length: 3));
        var tampered = MustAppend(chain, CandidateObservation(Observation("same-length-b"), digest: Digest('b'), length: 3));

        Assert.AreEqual(RepresentationChainAppendDisposition.ReplacementRecorded, tampered.Disposition);
        Assert.AreSame(tampered, chain.CurrentTrustedBaseline);
        Assert.AreEqual(1, chain.ReplacementEvents.Count);
        Assert.AreSame(baseline, chain.ReplacementEvents[0].Predecessor);
        Assert.AreSame(tampered, chain.ReplacementEvents[0].Replacement);
    }

    [TestMethod]
    public void AnEqualCandidateConfirmsTheBaselineWithoutARepetitionEvent()
    {
        var chain = OpenChain();
        var first = MustAppend(chain, CandidateObservation(Observation("first"), digest: Digest('a'), length: 3));
        var second = MustAppend(chain, CandidateObservation(Observation("second"), digest: Digest('a'), length: 3));

        Assert.AreEqual(RepresentationChainAppendDisposition.BaselineConfirmedUnchanged, second.Disposition);
        Assert.AreSame(first, chain.CurrentTrustedBaseline);
        Assert.AreEqual(0, chain.ReplacementEvents.Count);
        Assert.AreEqual(2, chain.History.Count);
    }

    [TestMethod]
    public void A304DoesNotDisturbTheTrustedBaseline()
    {
        var chain = OpenChain();
        var baseline = MustAppend(chain, CandidateObservation(Observation("baseline"), digest: Digest('a'), length: 3));

        var request = LogicalRequestFor(EffectiveUri);
        var revalidation = RoutedHttpHop.Create(
            0, Uuid("reval"), null, RequestDigest(request), EffectiveUri, 304,
            Headers(), Time(0), Time(1),
            new Revalidation304HttpCompletion(), 0, EmptyDigest, WriteReceiptDigest(EmptyDigest, 0), 0, EmptyDigest);
        var appended = MustAppend(
            chain, RepresentationChainObservation.FromRoute(EvidenceFor(revalidation), request));

        Assert.AreEqual(RepresentationChainAppendDisposition.AppendedAsEvidenceOnly, appended.Disposition);
        Assert.AreSame(baseline, chain.CurrentTrustedBaseline);
        Assert.AreEqual(0, chain.ReplacementEvents.Count);
        Assert.AreEqual(2, chain.History.Count);
    }

    [TestMethod]
    public void APartialTransferAtStatus200NeverQualifiesEvenThoughItIsDerivableStatus()
    {
        // This is the trap R3.4 explicitly names: a 200 with an incomplete transfer is still
        // classified `derivable_status` by HttpStatusClassifier, because that classifier only
        // looks at status and Content-Range. Qualification must also check the completion shape,
        // not status alone, or a short read would be able to mint or move a trusted baseline.
        var chain = OpenChain();
        var request = LogicalRequestFor(EffectiveUri);
        var partial = RoutedHttpHop.Create(
            0, Uuid("partial"), null, RequestDigest(request), EffectiveUri, 200,
            Headers(contentLength: "10"), Time(0), Time(1),
            new IncompleteHttpCompletion(HttpAcquisitionReasonRegistry.Member(HttpPartialBodyReason.DeclaredLengthShortRead)),
            5, Digest('d'), WriteReceiptDigest(Digest('d'), 5), 5, Digest('d'));
        var evidence = EvidenceFor(
            [partial], new IncompleteHttpRouteOutcome(HttpRouteIncompleteReason.HopIncomplete));
        var observation = RepresentationChainObservation.FromRoute(evidence, request);

        Assert.IsFalse(observation.QualifiesAsTrustedBaselineCandidate());
        var appended = MustAppend(chain, observation);
        Assert.AreEqual(RepresentationChainAppendDisposition.AppendedAsEvidenceOnly, appended.Disposition);
        Assert.IsNull(chain.CurrentTrustedBaseline);
    }

    [TestMethod]
    public void AZeroOctetCompleteTwoHundredNeverQualifies()
    {
        // Also derivable_status, also a complete framed transfer (Content-Length: 0), and R3.4
        // still names this a non-qualifying zero-octet outcome.
        var chain = OpenChain();
        var request = LogicalRequestFor(EffectiveUri);
        var empty = RoutedHttpHop.Create(
            0, Uuid("empty"), null, RequestDigest(request), EffectiveUri, 200,
            Headers(contentLength: "0"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(0), 0, EmptyDigest, WriteReceiptDigest(EmptyDigest, 0), 0, EmptyDigest);
        var observation = RepresentationChainObservation.FromRoute(EvidenceFor(empty), request);

        Assert.IsTrue(observation.IsCompleteBodyTransfer);
        Assert.IsFalse(observation.QualifiesAsTrustedBaselineCandidate());
        var appended = MustAppend(chain, observation);
        Assert.AreEqual(RepresentationChainAppendDisposition.AppendedAsEvidenceOnly, appended.Disposition);
        Assert.IsNull(chain.CurrentTrustedBaseline);
    }

    [TestMethod]
    public void ARangedResponseNeverQualifies()
    {
        var chain = OpenChain();
        var request = LogicalRequestFor(EffectiveUri);
        var ranged = RoutedHttpHop.Create(
            0, Uuid("ranged"), null, RequestDigest(request), EffectiveUri, 206,
            Headers(contentLength: "3", contentRange: "bytes 0-2/9"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(3), 3, Digest('a'), WriteReceiptDigest(Digest('a'), 3), 3, Digest('a'));
        var appended = MustAppend(
            chain, RepresentationChainObservation.FromRoute(EvidenceFor(ranged), request));
        Assert.AreEqual(RepresentationChainAppendDisposition.AppendedAsEvidenceOnly, appended.Disposition);
        Assert.IsNull(chain.CurrentTrustedBaseline);
    }

    [TestMethod]
    public void ARedirectResponseNeverQualifies()
    {
        var chain = OpenChain();
        var request = LogicalRequestFor(EffectiveUri);
        var redirect = RoutedHttpHop.Create(
            0, Uuid("redirect"), null, RequestDigest(request), EffectiveUri, 301,
            Headers(contentLength: "0", location: "https://data.legilux.public.lu/other"),
            Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(0), 0, EmptyDigest, WriteReceiptDigest(EmptyDigest, 0), 0, EmptyDigest);
        var evidence = EvidenceFor(
            [redirect],
            new RedirectTargetUnobservedHttpRouteOutcome(RequestDigest(request), Time(0)));
        var appended = MustAppend(
            chain, RepresentationChainObservation.FromRoute(evidence, request));
        Assert.AreEqual(RepresentationChainAppendDisposition.AppendedAsEvidenceOnly, appended.Disposition);
        Assert.IsNull(chain.CurrentTrustedBaseline);
    }

    [TestMethod]
    public void AByteBearingErrorBodyNeverCreatesAReplacementEvent()
    {
        var chain = OpenChain();
        var baseline = MustAppend(chain, CandidateObservation(Observation("baseline"), digest: Digest('a'), length: 3));

        var request = LogicalRequestFor(EffectiveUri);
        var errorBody = RoutedHttpHop.Create(
            0, Uuid("error"), null, RequestDigest(request), EffectiveUri, 500,
            Headers(contentLength: "9"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(9), 9, Digest('e'), WriteReceiptDigest(Digest('e'), 9), 9, Digest('e'));
        var appended = MustAppend(
            chain, RepresentationChainObservation.FromRoute(EvidenceFor(errorBody), request));

        Assert.AreEqual(RepresentationChainAppendDisposition.AppendedAsEvidenceOnly, appended.Disposition);
        Assert.AreSame(baseline, chain.CurrentTrustedBaseline);
        Assert.AreEqual(0, chain.ReplacementEvents.Count);
    }

    [TestMethod]
    public void AnOpenRepresentationRequestKeyNeverEstablishesATrustedBaseline()
    {
        // Same URI both roles, matching the single-hop no-redirect fixtures CandidateObservation
        // builds throughout this file; the requested-versus-effective distinction is exercised
        // separately by the two redirect isolation tests.
        var key = RepresentationChainKey.Create(EffectiveUri, EffectiveUri, Digest64);
        var chain = RepresentationChain.Open(key, isClosedRepresentationRequestKey: false);

        var first = MustAppend(chain, CandidateObservation(Observation("first"), digest: Digest('a'), length: 3));
        var second = MustAppend(chain, CandidateObservation(Observation("second"), digest: Digest('a'), length: 3));

        Assert.AreEqual(RepresentationChainAppendDisposition.AppendedAsEvidenceOnly, first.Disposition);
        Assert.AreEqual(RepresentationChainAppendDisposition.AppendedAsEvidenceOnly, second.Disposition);
        Assert.IsNull(chain.CurrentTrustedBaseline);
        Assert.AreEqual(0, chain.ReplacementEvents.Count);
        Assert.AreEqual(2, chain.History.Count);
    }

    [TestMethod]
    public void AReusedObservationIdIsRefusedAndDoesNotDisturbTheChain()
    {
        var chain = OpenChain();
        var observation = CandidateObservation(Observation("dup"), digest: Digest('a'), length: 3);
        var first = MustAppend(chain, observation);

        var second = chain.TryAppend(observation, out var refusal);
        Assert.IsNull(second);
        Assert.AreEqual(RepresentationChainAppendRefusal.ObservationIdReused, refusal);
        Assert.AreEqual(1, chain.History.Count);
        Assert.AreSame(first, chain.CurrentTrustedBaseline);
    }

    [TestMethod]
    public void ARouteThatStartedAtTheKeysUriButEndedElsewhereIsRefusedOnTheEffectiveUri()
    {
        // Two hops, so RequestedUri and EffectiveUri genuinely differ: the route started exactly
        // where this chain's key says, and ended somewhere else. Isolates EffectiveUriMismatch
        // from RequestedUriMismatch; a single-hop foreign URI cannot do this, because a
        // single-hop route's RequestedUri and EffectiveUri are the same value by construction, so
        // it would always trip both checks at once and never prove which one actually fired.
        var chain = OpenRedirectChain();
        var (evidence, request) = RedirectRoute(RequestedUri, OtherUri);

        var observation = RepresentationChainObservation.FromRoute(evidence, request);
        var appended = chain.TryAppend(observation, out var refusal);

        Assert.IsNull(appended);
        Assert.AreEqual(RepresentationChainAppendRefusal.EffectiveUriMismatch, refusal);
        Assert.AreEqual(0, chain.History.Count);
        Assert.IsNull(chain.CurrentTrustedBaseline);
    }

    [TestMethod]
    public void ARouteThatEndedAtTheKeysUriButStartedElsewhereIsRefusedOnTheRequestedUri()
    {
        // The paired isolation: the route ended exactly where this chain's key says, but started
        // somewhere else. Before this fix RepresentationChainKey.RequestedUri was the caller's
        // own claim, never checked against anything, so a route with the right ending and the
        // wrong beginning passed silently.
        var chain = OpenRedirectChain();
        var (evidence, request) = RedirectRoute(OtherUri, EffectiveUri);

        var observation = RepresentationChainObservation.FromRoute(evidence, request);
        var appended = chain.TryAppend(observation, out var refusal);

        Assert.IsNull(appended);
        Assert.AreEqual(RepresentationChainAppendRefusal.RequestedUriMismatch, refusal);
        Assert.AreEqual(0, chain.History.Count);
        Assert.IsNull(chain.CurrentTrustedBaseline);
    }

    [TestMethod]
    public void APostEvidenceDocumentIsRefusedAtMintRatherThanEnteringAGetOnlyChain()
    {
        // R3.4 defines no POST chain. Before this fix, an observation minted from a bare
        // RoutedHttpHop carried no method at all, so nothing stopped a POST route's evidence from
        // being minted into a chain declared GET-only; the method was the caller's claim via
        // RepresentationChainKey, never the hop's own proof. FromRoute closes this at the door:
        // refused before a chain is ever involved.
        var request = HttpLogicalRequest.Create(
            EffectiveUri,
            HttpRequestMethod.Post,
            [
                new HttpLogicalRequestHeader("user-agent", "Lex/0.1 (+https://github.com/SFHAJJI/lex)"),
                new HttpLogicalRequestHeader("content-type", "application/sparql-query"),
            ],
            new HttpLogicalRequestBody(3, Digest('a')),
            Digest('1'),
            Digest('2'));
        var hop = RoutedHttpHop.Create(
            0, Uuid("post"), null, RequestDigest(request), EffectiveUri, 200,
            Headers(contentLength: "3"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(3), 3, Digest('a'), WriteReceiptDigest(Digest('a'), 3), 3, Digest('a'));

        Assert.ThrowsExactly<ArgumentException>(
            () => RepresentationChainObservation.FromRoute(EvidenceFor(hop), request));
    }

    [TestMethod]
    public void ARequestThatIsNotTheOneTheTerminalHopActuallySentIsRefusedAtMint()
    {
        // The digest check, isolated from the method check: a GET request object that simply
        // does not correspond to the hop it is paired with. Equal method alone would have let a
        // caller pair any GET request with any hop's evidence; only the digest ties the two
        // together, and this is the test that would fail if FromRoute compared methods instead
        // of hashing CopyCanonicalBytes.
        var request = LogicalRequestFor(EffectiveUri);
        var otherRequest = LogicalRequestFor(OtherUri);
        var hop = RoutedHttpHop.Create(
            0, Uuid("mismatched"), null, RequestDigest(request), EffectiveUri, 200,
            Headers(contentLength: "3"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(3), 3, Digest('a'), WriteReceiptDigest(Digest('a'), 3), 3, Digest('a'));

        Assert.ThrowsExactly<ArgumentException>(
            () => RepresentationChainObservation.FromRoute(EvidenceFor(hop), otherRequest));
    }

    [TestMethod]
    public void KeyCreationRejectsMalformedUrisAndDigests()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            RepresentationChainKey.Create("http://insecure.example/x", EffectiveUri, Digest64));
        Assert.ThrowsExactly<ArgumentException>(() =>
            RepresentationChainKey.Create(RequestedUri, EffectiveUri, "not-a-digest"));
    }

    [TestMethod]
    public void TwoKeysWithTheSameFourMembersAreEqualAndAnyDifferingMemberBreaksEquality()
    {
        var key1 = RepresentationChainKey.Create(RequestedUri, EffectiveUri, Digest64);
        var key2 = RepresentationChainKey.Create(RequestedUri, EffectiveUri, Digest64);
        var differentDigest = RepresentationChainKey.Create(RequestedUri, EffectiveUri, Digest('a'));
        var differentEffective = RepresentationChainKey.Create(RequestedUri, OtherUri, Digest64);

        Assert.AreEqual(key1, key2);
        Assert.AreEqual(key1.GetHashCode(), key2.GetHashCode());
        Assert.AreNotEqual(key1, differentDigest);
        Assert.AreNotEqual(key1, differentEffective);
        Assert.IsTrue(key1.CanonicalProjection().Contains("method=GET", StringComparison.Ordinal));
    }

    [TestMethod]
    public void OpenAndFromRouteRejectNullArguments()
    {
        var key = RepresentationChainKey.Create(RequestedUri, EffectiveUri, Digest64);
        var request = LogicalRequestFor(EffectiveUri);
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            RepresentationChain.Open(null!, isClosedRepresentationRequestKey: true));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            RepresentationChainObservation.FromRoute(null!, request));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            RepresentationChainObservation.FromRoute(EvidenceFor(SingleHop(request)), null!));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            RepresentationChain.Open(key, isClosedRepresentationRequestKey: true).TryAppend(null!, out _));
    }

    private static RepresentationChain OpenChain()
    {
        // Same URI both roles; see AnOpenRepresentationRequestKeyNeverEstablishesATrustedBaseline
        // for why. RequestedUri and EffectiveUri as two distinct constants exist for the redirect
        // isolation tests, which open their own chain against RequestedUri directly.
        var key = RepresentationChainKey.Create(EffectiveUri, EffectiveUri, Digest64);
        return RepresentationChain.Open(key, isClosedRepresentationRequestKey: true);
    }

    /// <summary>The (RequestedUri, EffectiveUri) pair, for the two redirect isolation tests.</summary>
    private static RepresentationChain OpenRedirectChain()
    {
        var key = RepresentationChainKey.Create(RequestedUri, EffectiveUri, Digest64);
        return RepresentationChain.Open(key, isClosedRepresentationRequestKey: true);
    }

    private static RepresentationChain.AppendedObservation MustAppend(
        RepresentationChain chain,
        RepresentationChainObservation observation)
    {
        var appended = chain.TryAppend(observation, out var refusal);
        Assert.AreEqual(RepresentationChainAppendRefusal.None, refusal);
        Assert.IsNotNull(appended);
        return appended!;
    }

    private static RepresentationChainObservation CandidateObservation(
        string observationId,
        string digest,
        ulong length)
    {
        var request = LogicalRequestFor(EffectiveUri);
        var hop = RoutedHttpHop.Create(
            0, observationId, null, RequestDigest(request), EffectiveUri, 200,
            Headers(contentLength: length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(length), length, digest, WriteReceiptDigest(digest, length), length, digest);
        var observation = RepresentationChainObservation.FromRoute(EvidenceFor(hop), request);
        Assert.IsTrue(observation.QualifiesAsTrustedBaselineCandidate());
        return observation;
    }

    private static RoutedHttpResponseHeaders Headers(
        string? contentLength = null,
        string? contentRange = null,
        string? location = null)
    {
        RoutedHttpHeaderField Field(string? value) => value is null
            ? new RoutedHttpAbsentHeader()
            : new RoutedHttpSingleHeader(value);
        var absent = new RoutedHttpAbsentHeader();
        return new RoutedHttpResponseHeaders(
            absent,
            Field(contentLength),
            absent,
            absent,
            Field(contentRange),
            absent,
            absent,
            Field(location),
            absent,
            absent,
            absent,
            absent,
            absent);
    }

    private static string Digest(char value) => new(value, 64);

    private static readonly string EmptyDigest =
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([])).ToLowerInvariant();

    private static string Time(int secondsOffset) =>
        $"2026-09-03T10:00:{secondsOffset:D2}.0000000Z";

    private static string Observation(string tag) => Uuid(tag);

    private static string Uuid(string tag)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(tag));
        var guid = new Guid(hash[..16]);
        return "urn:uuid:" + guid.ToString("D");
    }

    private static HttpLogicalRequest LogicalRequestFor(string uri) =>
        HttpLogicalRequest.Create(
            uri,
            HttpRequestMethod.Get,
            [new HttpLogicalRequestHeader("user-agent", "Lex/0.1 (+https://github.com/SFHAJJI/lex)")],
            new HttpLogicalRequestBody(0, EmptyDigest),
            Digest('1'),
            Digest('2'));

    private static string RequestDigest(HttpLogicalRequest request) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(request.CopyCanonicalBytes())).ToLowerInvariant();

    private static RoutedHttpHop SingleHop(HttpLogicalRequest request) =>
        RoutedHttpHop.Create(
            0, Uuid("single"), null, RequestDigest(request), request.Uri, 200,
            Headers(contentLength: "3"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(3), 3, Digest('a'), WriteReceiptDigest(Digest('a'), 3), 3, Digest('a'));

    private static RoutedHttpEvidence EvidenceFor(
        RoutedHttpHop[] hops, RoutedHttpRouteOutcome? outcome = null) =>
        RoutedHttpEvidence.Create(
            new SourceArtifactRef(Uuid("run-identity"), Digest('1')),
            1,
            0,
            hops,
            outcome ?? new CompleteHttpRouteOutcome(),
            ReceiptsFor(hops));

    /// <summary>
    /// A genuine, internally consistent <see cref="DurableBlobWriteReceipt"/> for exactly the given
    /// content digest and length, so a hop built from it satisfies Decision 80's receipt check at
    /// <see cref="RoutedHttpEvidence.Create"/>. Mirrors the helper of the same name in
    /// RoutedHttpEvidenceContractTests.cs; kept local rather than shared so each contract test file
    /// depends only on the Contracts types it already imports.
    /// </summary>
    private static string WriteReceiptDigest(string contentSha256, ulong length)
    {
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef,
            contentSha256,
            checked((long)length),
            CustodyClass.NightlyFloor90d);
        var policy = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            CustodyVerificationProfile.FileSystemUnenforced1,
            policyKey: null,
            CustodyProtection.NotEnforced,
            new DateTimeOffset(2026, 9, 2, 19, 0, 0, TimeSpan.Zero),
            protectedUntil: null);
        var receipt = new DurableBlobWriteReceipt(CustodySchemaIds.DurableBlobWriteReceipt, reference, policy);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(ContractJson.Serialize(receipt))))
            .ToLowerInvariant();
    }

    private static Dictionary<string, DurableBlobWriteReceipt> ReceiptsFor(IEnumerable<RoutedHttpHop> hops)
    {
        var receipts = new Dictionary<string, DurableBlobWriteReceipt>(StringComparer.Ordinal);
        foreach (var hop in hops)
        {
            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef,
                hop.Sha256,
                checked((long)hop.Length),
                CustodyClass.NightlyFloor90d);
            var policy = new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.FileSystemUnenforced1,
                policyKey: null,
                CustodyProtection.NotEnforced,
                new DateTimeOffset(2026, 9, 2, 19, 0, 0, TimeSpan.Zero),
                protectedUntil: null);
            receipts[hop.ObservationId] =
                new DurableBlobWriteReceipt(CustodySchemaIds.DurableBlobWriteReceipt, reference, policy);
        }

        return receipts;
    }

    private static RoutedHttpEvidence EvidenceFor(params RoutedHttpHop[] hops) =>
        EvidenceFor(hops, outcome: null);

    /// <summary>
    /// A genuine two-hop route: <paramref name="firstUri"/> answers 301 to
    /// <paramref name="terminalUri"/>, which then answers 200. Both hops share one logical
    /// request digest, since neither the redirect nor the terminal changes what was asked for.
    /// </summary>
    private static (RoutedHttpEvidence Evidence, HttpLogicalRequest Request) RedirectRoute(
        string firstUri, string terminalUri)
    {
        var request = LogicalRequestFor(terminalUri);
        var digest = RequestDigest(request);
        var firstHopId = Uuid("redirect-first-" + firstUri + terminalUri);
        var first = RoutedHttpHop.Create(
            0, firstHopId, null, digest, firstUri, 301,
            Headers(contentLength: "0", location: terminalUri), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(0), 0, EmptyDigest, WriteReceiptDigest(EmptyDigest, 0), 0, EmptyDigest);
        var terminal = RoutedHttpHop.Create(
            1, Uuid("redirect-terminal-" + firstUri + terminalUri), firstHopId, digest, terminalUri, 200,
            Headers(contentLength: "3"), Time(2), Time(3),
            new DeclaredContentLengthHttpCompletion(3), 3, Digest('a'), WriteReceiptDigest(Digest('a'), 3), 3, Digest('a'));
        return (EvidenceFor(first, terminal), request);
    }
}
