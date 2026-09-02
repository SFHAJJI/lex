using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The Union acquisition profile: which channels, at what rate, against which ceiling.
///
/// The tests are exhaustive over the closed channel set rather than exemplary. On the previous
/// slice an exemplary test passed under three successive versions of the same defect, and only
/// walking every member closed it.
/// </summary>
[TestClass]
public sealed class EuAcquisitionProfileTests
{
    [TestMethod]
    public void EveryChannelCarriesExactlyOneDisposition()
    {
        // An unmentioned channel is indistinguishable from a refused one, so a profile that simply
        // omits one is refused rather than read as excluding it.
        foreach (var missing in EuScopeVocabulary.Channels)
        {
            var partial = FullChannelSet().Where(d => d.Channel != missing).ToArray();
            if (!partial.Any(d => d.MayGraduate()))
            {
                continue; // covered by the no-admitted-channel test instead
            }

            var thrown = Assert.ThrowsExactly<ArgumentException>(
                () => new EuAcquisitionProfile(partial, Pacing(), Ceiling()),
                $"a profile omitting {missing} was accepted");
            StringAssert.Contains(thrown.Message, missing.ToString());
        }
    }

    [TestMethod]
    public void AChannelWithTwoDispositionsHasNone()
    {
        // Doubled with the channel's own reviewed admission. The duplicate is what this test is
        // about, and a contradicting admission is now refused at construction, which would throw
        // outside the assertion below and prove nothing about the profile.
        var doubled = FullChannelSet()
            .Append(Disposition(
                EuChannel.CellarSparqlEndpoint,
                EuChannelDisposition.PolicyFor(EuChannel.CellarSparqlEndpoint)))
            .ToArray();

        Assert.ThrowsExactly<ArgumentException>(
            () => new EuAcquisitionProfile(doubled, Pacing(), Ceiling()));
    }

    [TestMethod]
    public void AdmittedChannelsAreExactlyThoseThatMayGraduate()
    {
        var profile = new EuAcquisitionProfile(FullChannelSet(), Pacing(), Ceiling());

        CollectionAssert.AreEquivalent(
            new[] { EuChannel.CellarSparqlEndpoint, EuChannel.PublicationsRestResource },
            profile.AdmittedChannels().ToArray());
        CollectionAssert.DoesNotContain(profile.AdmittedChannels().ToArray(), EuChannel.EurLexPortal);
    }

    [TestMethod]
    public void MutatingTheCallersListAfterConstructionCannotChangeTheProfile()
    {
        // IReadOnlyList is a view, not a guarantee: a caller can hand a List through it and mutate
        // it afterwards. The profile keeps its own snapshot.
        var live = new List<EuChannelDisposition>(FullChannelSet());
        var profile = new EuAcquisitionProfile(live, Pacing(), Ceiling());
        live.Clear();

        Assert.AreEqual(3, profile.Channels.Count);
        Assert.AreEqual(2, profile.AdmittedChannels().Count);
    }

    [TestMethod]
    public void PacingRecordsTheConfiguredIntervalAndWhyItWasChosen()
    {
        // The served host publishes no crawl delay, so 1500 ms is a judgement rather than
        // compliance. A profile carrying only the number could not tell those apart, and an
        // earlier measurement of mine confused exactly that by comparing our interval to a host
        // the architecture forbids us to fetch from.
        var pacing = new EuPacingPolicy(
            1500,
            EuPacingBasis.ChosenAbsentPublishedGuidance,
            Evidence("aa"));

        Assert.AreEqual(1500, pacing.MinimumIntervalMilliseconds);
        Assert.AreEqual(EuPacingBasis.ChosenAbsentPublishedGuidance, pacing.Basis);
        AssertTokens<EuPacingBasis>("chosen_absent_published_guidance", "published_crawl_delay");
    }

    [TestMethod]
    public void EveryPacingBasisNeedsItsEvidence()
    {
        // Both bases, not only the compliant one. "We checked and found no guidance" and "we did
        // not check" produce the same interval with different standing, and only the evidence
        // separates them.
        foreach (var basis in Enum.GetValues<EuPacingBasis>())
        {
            Assert.ThrowsExactly<ArgumentNullException>(
                () => new EuPacingPolicy(1500, basis, null!),
                $"{basis} was allowed to carry no evidence");
        }
    }

    [TestMethod]
    public void APacingIntervalMustBeBoundedAndPositive()
    {
        foreach (var interval in new[] { 0, -1, EuPacingPolicy.MaximumIntervalMilliseconds + 1 })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => new EuPacingPolicy(
                    interval, EuPacingBasis.ChosenAbsentPublishedGuidance, Evidence("aa")));
        }

        // The boundary itself is legal, so the guard is a bound rather than an exclusion.
        _ = new EuPacingPolicy(
            EuPacingPolicy.MaximumIntervalMilliseconds,
            EuPacingBasis.ChosenAbsentPublishedGuidance,
            Evidence("aa"));
    }

    [TestMethod]
    public void TheCeilingBindsAThresholdAndADetectorAndAssessesNothing()
    {
        var ceiling = new EuDeliveryCeilingBinding(1_000_000, Detector());
        Assert.AreEqual(1_000_000, ceiling.MaxDeliverableRows);
        Assert.AreEqual("virtuoso_delivery_ceiling", ceiling.DetectorRef.MemberKey);

        // The assessment belongs to the shared primitive. If this type ever grows a method that
        // decides whether a page is truncated, Luxembourg and the Union have two answers about one
        // endpoint property.
        Assert.IsNull(
            typeof(EuDeliveryCeilingBinding).GetMethod("IsAtRisk"),
            "the ceiling binding grew an assessment of its own");
        Assert.IsNull(typeof(EuDeliveryCeilingBinding).GetMethod("Assess"));
    }

    [TestMethod]
    public void ACeilingMustBeAPositiveRowCount()
    {
        foreach (var rows in new long[] { 0, -1 })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => new EuDeliveryCeilingBinding(rows, Detector()));
        }

        Assert.ThrowsExactly<ArgumentNullException>(
            () => new EuDeliveryCeilingBinding(1_000_000, null!));
    }

    [TestMethod]
    public void TheProfileRoundTripsAndRefusesAnUnknownChannel()
    {
        var profile = new EuAcquisitionProfile(FullChannelSet(), Pacing(), Ceiling());
        var json = ContractJson.Serialize(profile);

        StringAssert.Contains(json, "cellar_sparql_endpoint");
        StringAssert.Contains(json, "chosen_absent_published_guidance");
        // A computed convenience must not become a wire field somebody could set.
        Assert.IsFalse(json.Contains("admitted_channels", StringComparison.Ordinal));

        var restored = ContractJson.Deserialize<EuAcquisitionProfile>(json);
        Assert.AreEqual(profile.Channels.Count, restored.Channels.Count);
        Assert.AreEqual(profile.Pacing, restored.Pacing);
        Assert.AreEqual(profile.Ceiling, restored.Ceiling);

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<EuAcquisitionProfile>(
                json.Replace("cellar_sparql_endpoint", "cellar_sparql_mirror", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void TheProfileInvariantsHoldOnTheWireToo()
    {
        // A document could otherwise carry a shape the constructor refuses.
        var profile = new EuAcquisitionProfile(FullChannelSet(), Pacing(), Ceiling());
        var json = ContractJson.Serialize(profile);

        var everythingExcluded = json.Replace("\"admitted\"", "\"excluded\"", StringComparison.Ordinal);
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<EuAcquisitionProfile>(everythingExcluded),
            "a wire document admitted no channel and was accepted");
    }

    internal static EuAcquisitionProfile ProbeProfile() =>
        new(FullChannelSet(), Pacing(), Ceiling());

    private static EuChannelDisposition[] FullChannelSet() =>
    [
        Disposition(EuChannel.CellarSparqlEndpoint, EuChannelAdmission.Admitted),
        Disposition(EuChannel.PublicationsRestResource, EuChannelAdmission.Admitted),
        Disposition(EuChannel.EurLexPortal, EuChannelAdmission.Excluded),
    ];

    private static EuChannelDisposition Disposition(EuChannel channel, EuChannelAdmission admission) =>
        new(channel, admission, "reason_code", "eu_channel_admission_1", Evidence("bb"));

    private static EuPacingPolicy Pacing() =>
        new(1500, EuPacingBasis.ChosenAbsentPublishedGuidance, Evidence("aa"));

    private static EuDeliveryCeilingBinding Ceiling() => new(1_000_000, Detector());

    private static SourceRegistryMemberRef Detector() =>
        new(Evidence("cc"), "virtuoso_delivery_ceiling");

    private static SourceArtifactRef Evidence(string seed) =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000" + seed, new string(seed[0], 64));

    private static void AssertTokens<TEnum>(params string[] expected)
        where TEnum : struct, Enum
    {
        var members = Enum.GetValues<TEnum>();
        Assert.AreEqual(expected.Length, members.Length, $"{typeof(TEnum).Name} member count");
        for (var index = 0; index < members.Length; index++)
        {
            Assert.AreEqual("\"" + expected[index] + "\"", ContractJson.Serialize(members[index]));
        }
    }
}
