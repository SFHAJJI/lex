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

        var revalidation = RoutedHttpHop.Create(
            0, Uuid("reval"), null, Digest('9'), EffectiveUri, 304,
            Headers(), Time(0), Time(1),
            new Revalidation304HttpCompletion(), 0, EmptyDigest, Digest('c'), 0, EmptyDigest);
        var appended = MustAppend(chain, RepresentationChainObservation.FromHop(revalidation));

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
        var partial = RoutedHttpHop.Create(
            0, Uuid("partial"), null, Digest('9'), EffectiveUri, 200,
            Headers(contentLength: "10"), Time(0), Time(1),
            new IncompleteHttpCompletion(HttpAcquisitionReasonRegistry.Member(HttpPartialBodyReason.DeclaredLengthShortRead)),
            5, Digest('d'), Digest('c'), 5, Digest('d'));
        var observation = RepresentationChainObservation.FromHop(partial);

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
        var empty = RoutedHttpHop.Create(
            0, Uuid("empty"), null, Digest('9'), EffectiveUri, 200,
            Headers(contentLength: "0"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(0), 0, EmptyDigest, Digest('c'), 0, EmptyDigest);
        var observation = RepresentationChainObservation.FromHop(empty);

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
        var ranged = RoutedHttpHop.Create(
            0, Uuid("ranged"), null, Digest('9'), EffectiveUri, 206,
            Headers(contentLength: "3", contentRange: "bytes 0-2/9"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(3), 3, Digest('a'), Digest('c'), 3, Digest('a'));
        var appended = MustAppend(chain, RepresentationChainObservation.FromHop(ranged));
        Assert.AreEqual(RepresentationChainAppendDisposition.AppendedAsEvidenceOnly, appended.Disposition);
        Assert.IsNull(chain.CurrentTrustedBaseline);
    }

    [TestMethod]
    public void ARedirectResponseNeverQualifies()
    {
        var chain = OpenChain();
        var redirect = RoutedHttpHop.Create(
            0, Uuid("redirect"), null, Digest('9'), EffectiveUri, 301,
            Headers(contentLength: "0", location: "https://data.legilux.public.lu/other"),
            Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(0), 0, EmptyDigest, Digest('c'), 0, EmptyDigest);
        var appended = MustAppend(chain, RepresentationChainObservation.FromHop(redirect));
        Assert.AreEqual(RepresentationChainAppendDisposition.AppendedAsEvidenceOnly, appended.Disposition);
        Assert.IsNull(chain.CurrentTrustedBaseline);
    }

    [TestMethod]
    public void AByteBearingErrorBodyNeverCreatesAReplacementEvent()
    {
        var chain = OpenChain();
        var baseline = MustAppend(chain, CandidateObservation(Observation("baseline"), digest: Digest('a'), length: 3));

        var errorBody = RoutedHttpHop.Create(
            0, Uuid("error"), null, Digest('9'), EffectiveUri, 500,
            Headers(contentLength: "9"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(9), 9, Digest('e'), Digest('c'), 9, Digest('e'));
        var appended = MustAppend(chain, RepresentationChainObservation.FromHop(errorBody));

        Assert.AreEqual(RepresentationChainAppendDisposition.AppendedAsEvidenceOnly, appended.Disposition);
        Assert.AreSame(baseline, chain.CurrentTrustedBaseline);
        Assert.AreEqual(0, chain.ReplacementEvents.Count);
    }

    [TestMethod]
    public void AnOpenRepresentationRequestKeyNeverEstablishesATrustedBaseline()
    {
        var key = RepresentationChainKey.Create(RequestedUri, EffectiveUri, Digest64);
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
    public void AnObservationFromAnotherUriIsRefused()
    {
        var chain = OpenChain();
        var foreignHop = RoutedHttpHop.Create(
            0, Uuid("foreign"), null, Digest('9'), OtherUri, 200,
            Headers(contentLength: "3"), Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(3), 3, Digest('a'), Digest('c'), 3, Digest('a'));
        var observation = RepresentationChainObservation.FromHop(foreignHop);

        var appended = chain.TryAppend(observation, out var refusal);
        Assert.IsNull(appended);
        Assert.AreEqual(RepresentationChainAppendRefusal.EffectiveUriMismatch, refusal);
        Assert.AreEqual(0, chain.History.Count);
        Assert.IsNull(chain.CurrentTrustedBaseline);
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
    public void OpenAndFromHopRejectNullArguments()
    {
        var key = RepresentationChainKey.Create(RequestedUri, EffectiveUri, Digest64);
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            RepresentationChain.Open(null!, isClosedRepresentationRequestKey: true));
        Assert.ThrowsExactly<ArgumentNullException>(() => RepresentationChainObservation.FromHop(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            RepresentationChain.Open(key, isClosedRepresentationRequestKey: true).TryAppend(null!, out _));
    }

    private static RepresentationChain OpenChain()
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
        var hop = RoutedHttpHop.Create(
            0, observationId, null, Digest('9'), EffectiveUri, 200,
            Headers(contentLength: length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Time(0), Time(1),
            new DeclaredContentLengthHttpCompletion(length), length, digest, Digest('c'), length, digest);
        var observation = RepresentationChainObservation.FromHop(hop);
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
}
