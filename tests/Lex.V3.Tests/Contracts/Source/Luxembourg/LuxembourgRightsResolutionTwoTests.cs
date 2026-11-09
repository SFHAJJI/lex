using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// Resolution /2 (LU-DATA-DESIGN v2 §1.1, "Resolution", rules 1 to 15) and the admission policy registry (§1.2): one rule per
/// test, over readings /2 stated directly. The /1 tests (<see cref="LuxembourgRightsChannelsTests"/>) stay as /1's pins.
/// </summary>
[TestClass]
public sealed class LuxembourgRightsResolutionTwoTests
{
    /// <summary>Resolution /2's and policy A's texts, hashed outside this code base (Python, over the canonical texts).</summary>
    /// <remarks>Re-pinned in the review of #999: rule 12 quarantines a file naming any identity the catalogue does not know.</remarks>
    internal const string PinnedResolutionTwoSha256 = "05ed311fa4e124c04e5bd644423f6797dbbdf2a2141075d11879cd4dc2f9e083";
    internal const string PinnedPolicyASha256 = "b9687998dcde76930137a913f54b753d3f496a13da1c7d827d3f1573dcf3dc41";
    private const string Policy1Sha256 = "a477f4f2e6e0468bc9069da742f6f34f248ec831e01277a40552cb1e5bad2abb";

    /// <summary>
    /// <c>lex-lu-rights-admission-policy/A+L1+L1b+L2(a)</c> with the owner's words of 2026-10-27 on its ruling line: the entry
    /// Release 3's rule set names, recomputed in Python from its text.
    /// </summary>
    internal const string PinnedReleaseThreePolicySha256 = "2b049e7650b3c5a1250ad60ac263ffb50d11398945fe5e92ac6049634bfc4b43";

    /// <summary>The same entry's digest while its ruling line said the owner's words were not yet given (#999 to #1008).</summary>
    private const string EntryBeforeTheYesSha256 = "e52372f9417545f37380abd742e551f1d2a9035a5b146fda42ea17c3b1a645dd";

    private const string Xml = "http://data.legilux.public.lu/eli/etat/leg/loi/2021/01/22/a45/consolide/20211217/fr/xml";
    private const string Pdf = "http://data.legilux.public.lu/eli/etat/leg/loi/2021/01/22/a45/consolide/20211217/fr/pdf";
    private const string ActXml = "http://data.legilux.public.lu/eli/etat/leg/loi/2021/01/22/a45/jo/fr/xml";
    private const string CcBy = "http://creativecommons.org/licenses/by/4.0/";
    private const string Scl = "http://data.legilux.public.lu/resource/authority/license/licenceSCL";
    private const string Unruled = "https://example.invalid/licence/future";

    [TestMethod]
    public void TheResolutionAndThePoliciesAreTheirPinnedTexts()
    {
        var resolution = LuxembourgRightsChannels.CopyResolutionTwoProfileBytes();
        StringAssert.StartsWith(Encoding.UTF8.GetString(resolution), "lex-lu-rights-resolution/2\n");
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(resolution)), LuxembourgRightsChannels.ResolutionTwoProfileSha256);
        Assert.AreEqual(PinnedResolutionTwoSha256, LuxembourgRightsChannels.ResolutionTwoProfileSha256);

        var policyA = LuxembourgRightsAdmissionPolicies.CopyPolicyABytes();
        StringAssert.StartsWith(Encoding.UTF8.GetString(policyA), "lex-lu-rights-admission-policy/A\nadmits=agreed_cc_by_rederived,agreed_same_run_cc_by\n");
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(policyA)), LuxembourgRightsAdmissionPolicies.PolicyASha256);
        Assert.AreEqual(PinnedPolicyASha256, LuxembourgRightsAdmissionPolicies.PolicyASha256);
        Assert.AreEqual(Policy1Sha256, LuxembourgRightsAdmissionPolicies.Policy1Sha256, "policy /1 unchanged");
        CollectionAssert.IsSubsetOf(new[] { Policy1Sha256, PinnedPolicyASha256 },
            LuxembourgRightsAdmissionPolicies.RegisteredSha256s.ToArray());
    }

    [TestMethod]
    public void PolicyAAdmitsTheTwoAgreementsAndPolicyOneOnlyTheSameRunOne()
    {
        CollectionAssert.AreEqual(new[] { LuxembourgRightsChannelDisposition.AgreedSameRunCcBy },
            LuxembourgRightsAdmissionPolicies.AdmittingDispositions(Policy1Sha256).ToArray());
        CollectionAssert.AreEqual(new[] { LuxembourgRightsChannelDisposition.AgreedSameRunCcBy },
            LuxembourgRightsAdmissionPolicies.AdmittingDispositions(null).ToArray(), "a corpus naming no policy reads as /1");
        CollectionAssert.AreEqual(
            new[] { LuxembourgRightsChannelDisposition.AgreedSameRunCcBy, LuxembourgRightsChannelDisposition.AgreedCcByRederived },
            LuxembourgRightsAdmissionPolicies.AdmittingDispositions(PinnedPolicyASha256).ToArray());
        foreach (var disposition in Enum.GetValues<LuxembourgRightsChannelDisposition>())
        {
            var admittedByA = disposition is LuxembourgRightsChannelDisposition.AgreedSameRunCcBy
                or LuxembourgRightsChannelDisposition.AgreedCcByRederived;
            Assert.AreEqual(admittedByA, LuxembourgRightsAdmissionPolicies.Admits(PinnedPolicyASha256, disposition), disposition.ToString());
        }

        Assert.ThrowsExactly<ArgumentException>(() => LuxembourgRightsAdmissionPolicies.Admits(new string('a', 64),
            LuxembourgRightsChannelDisposition.AgreedSameRunCcBy), "an unregistered policy names nothing");
    }

    [TestMethod]
    public void APolicyNamingADispositionThatCanNeverAdmitCannotBeBuilt()
    {
        foreach (var never in new[]
                 {
                     LuxembourgRightsChannelDisposition.Conflict, LuxembourgRightsChannelDisposition.Multiple,
                     LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl, LuxembourgRightsChannelDisposition.MissingValue,
                     LuxembourgRightsChannelDisposition.Stale, LuxembourgRightsChannelDisposition.TypedQuarantineInFileIdentityUnknown,
                     LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected,
                     LuxembourgRightsChannelDisposition.ChannelEnumerationUnproven,
                 })
        {
            var text = $"lex-lu-rights-admission-policy/X\nadmits={ContractWire.NameOf(never)}\nsingle-channel-admission=none\n"
                + "single-channel-evidence-window=none\nruling=none\n";
            Assert.ThrowsExactly<ArgumentException>(() => LuxembourgRightsAdmissionPolicies.Compose(text, never), never.ToString());
            Assert.IsFalse(LuxembourgRightsAdmissionPolicies.IsAdmissible(never));
        }

        // A single-channel disposition needs the owner's ruling and its evidence window in the entry's text.
        var silent = LuxembourgRightsChannelDisposition.SparqlCcByInFileSilent;
        Assert.ThrowsExactly<ArgumentException>(() => LuxembourgRightsAdmissionPolicies.Compose(
            "lex-lu-rights-admission-policy/X\nadmits=sparql_cc_by_in_file_silent\nsingle-channel-admission=none\n"
            + "single-channel-evidence-window=none\nruling=none\n", silent));
        Assert.ThrowsExactly<ArgumentException>(() => LuxembourgRightsAdmissionPolicies.Compose(
            "lex-lu-rights-admission-policy/X\nadmits=agreed_same_run_cc_by\nsingle-channel-admission=none\n"
            + "single-channel-evidence-window=none\nruling=none\n", LuxembourgRightsChannelDisposition.AgreedCcByRederived),
            "the text names exactly its admitting set");
    }

    /// <summary>
    /// The owner's rulings L1, L1b and L2 (LU-DATA-DESIGN v2 §5) are registered entries over policy A, each with option (a)
    /// or (b): none is on in policy A, a single-channel disposition admits only with its licence page, and under (b) only a
    /// page observed live. L3 (PDFs) has no entry, and no entry admits a conflict.
    /// </summary>
    [TestMethod]
    public void TheOwnersRulingsAreRegisteredSwitchesOverPolicyA()
    {
        Assert.HasCount(16, LuxembourgRightsAdmissionPolicies.RegisteredSha256s, "/1, A, and seven combinations of three rulings, (a) and (b)");
        Assert.AreEqual(PinnedPolicyASha256, LuxembourgRightsAdmissionPolicies.PolicyFor(false, false, false, false));
        Assert.AreEqual(PinnedPolicyASha256, LuxembourgRightsAdmissionPolicies.PolicyFor(false, false, false, true), "no ruling, no window");
        var l2a = LuxembourgRightsAdmissionPolicies.PolicyFor(false, false, true, false);
        var l2b = LuxembourgRightsAdmissionPolicies.PolicyFor(false, false, true, true);
        Assert.IsTrue(LuxembourgRightsAdmissionPolicies.IsRegistered(l2a));
        Assert.IsTrue(LuxembourgRightsAdmissionPolicies.IsRegistered(l2b));
        var text = Encoding.UTF8.GetString(LuxembourgRightsAdmissionPolicies.CopyPolicyBytes(l2a));
        StringAssert.StartsWith(text, "lex-lu-rights-admission-policy/A+L2(a)\nadmits=agreed_cc_by_rederived,agreed_same_run_cc_by,sparql_cc_by_in_file_names_other_identity\n");
        StringAssert.Contains(text, "the-owner's-words-not-yet-given");
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), l2a);
        Assert.IsTrue(LuxembourgRightsAdmissionPolicies.LivePagesOnly(l2b));
        Assert.IsFalse(LuxembourgRightsAdmissionPolicies.LivePagesOnly(l2a));

        var other = LuxembourgRightsChannelDisposition.SparqlCcByInFileNamesOtherIdentity;
        Assert.IsFalse(LuxembourgRightsAdmissionPolicies.Admits(l2a, other, null), "no single-channel admission without its licence page");
        Assert.IsTrue(LuxembourgRightsAdmissionPolicies.Admits(l2a, other, false), "(a): a replayed page counts");
        Assert.IsFalse(LuxembourgRightsAdmissionPolicies.Admits(l2b, other, false), "(b): only a live page");
        Assert.IsTrue(LuxembourgRightsAdmissionPolicies.Admits(l2b, other, true));
        Assert.IsFalse(LuxembourgRightsAdmissionPolicies.Admits(l2a, LuxembourgRightsChannelDisposition.SparqlCcByInFileSilent, true), "L1 is its own switch");
        Assert.IsFalse(LuxembourgRightsAdmissionPolicies.Admits(PinnedPolicyASha256, other, true), "policy A admits no single channel");
        Assert.IsTrue(LuxembourgRightsAdmissionPolicies.Admits(l2b, LuxembourgRightsChannelDisposition.AgreedCcByRederived, null));
        foreach (var policy in LuxembourgRightsAdmissionPolicies.RegisteredSha256s)
        {
            Assert.IsFalse(LuxembourgRightsAdmissionPolicies.Admits(policy, LuxembourgRightsChannelDisposition.SparqlCcByInFileNotApplicable, true), "L3 has no entry");
            Assert.IsFalse(LuxembourgRightsAdmissionPolicies.Admits(policy, LuxembourgRightsChannelDisposition.Conflict, true), "L4: a conflict never admits");
        }

        var all = LuxembourgRightsAdmissionPolicies.PolicyFor(true, true, true, false);
        CollectionAssert.AreEqual(
            new[]
            {
                LuxembourgRightsChannelDisposition.AgreedSameRunCcBy, LuxembourgRightsChannelDisposition.AgreedCcByRederived,
                LuxembourgRightsChannelDisposition.SparqlCcByInFileNamesOtherIdentity, LuxembourgRightsChannelDisposition.SparqlCcByInFileSilent,
                LuxembourgRightsChannelDisposition.SparqlCcByInFileOwnBlockEmpty,
            },
            LuxembourgRightsAdmissionPolicies.AdmittingDispositions(all).ToArray());
        StringAssert.StartsWith(Encoding.UTF8.GetString(LuxembourgRightsAdmissionPolicies.CopyPolicyBytes(all)), "lex-lu-rights-admission-policy/A+L1+L1b+L2(a)\n");
    }

    /// <summary>
    /// The owner's standing yes of 2026-10-27 to the driver's recommendation (L1, L1b and L2, each with option (a)) stands,
    /// dated, on the ruling line of exactly that entry, once per ruling, and moved its digest (recomputed in Python from the
    /// text); every other entry keeps its text, and its ruling line still says the owner's words are not given. The entry's
    /// digest before the yes is no longer registered.
    /// </summary>
    [TestMethod]
    public void TheOwnersStandingYesStandsOnTheEntryOfTheRecommendationAndNoOther()
    {
        const string Words = "the-owner's-words-2026-10-27:the-owner's-standing-yes-of-2026-10-27-to-the-driver's-recommendation," +
            "L1-L1b-and-L2-each-with-option-(a),so-the-release-rule-set-names-this-entry";
        var all = LuxembourgRightsAdmissionPolicies.PolicyFor(l1: true, l1b: true, l2: true, livePagesOnly: false);
        Assert.AreEqual(PinnedReleaseThreePolicySha256, all);
        var text = Encoding.UTF8.GetString(LuxembourgRightsAdmissionPolicies.CopyPolicyBytes(all));
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), all);
        var ruling = text.Split('\n')[4];
        StringAssert.StartsWith(ruling, "ruling=L1(a):");
        CollectionAssert.AreEqual(new[] { "L1(a)", "L1b(a)", "L2(a)" }, ruling["ruling=".Length..].Split('|').Select(static part => part[..part.IndexOf(':')]).ToArray());
        foreach (var part in ruling.Split('|'))
        {
            StringAssert.EndsWith(part, ";asked-of-the-owner-2026-10-27-(LU-DATA-DESIGN-2026-10-14-v2-§5),recommended-(a);" + Words);
        }

        Assert.IsFalse(text.Contains("not-yet-given", StringComparison.Ordinal));
        StringAssert.Contains(text, "\nsingle-channel-evidence-window=(a)every-licence-page-of-the-acquisition,a-resumed-run's-replayed-pages-included\n");
        Assert.IsFalse(LuxembourgRightsAdmissionPolicies.IsRegistered(EntryBeforeTheYesSha256), "the entry's text before the yes names no policy");

        foreach (var policy in LuxembourgRightsAdmissionPolicies.RegisteredSha256s.Where(policy => policy != all))
        {
            var other = Encoding.UTF8.GetString(LuxembourgRightsAdmissionPolicies.CopyPolicyBytes(policy));
            Assert.IsFalse(other.Contains("the-owner's-words-2026-10-27", StringComparison.Ordinal), other);
            Assert.AreEqual(LuxembourgRightsAdmissionPolicies.AdmitsSingleChannel(policy), other.Contains("the-owner's-words-not-yet-given", StringComparison.Ordinal), other);
        }
    }

    [TestMethod]
    public void ASingleChannelResolutionBindsOnlyItsOwnLicencePage()
    {
        var silent = Resolve(Reading(LuxembourgInFileRightsReadingTwoStatus.Silent, [], []));
        var observed = new DateTimeOffset(2026, 10, 11, 13, 0, 0, TimeSpan.Zero);
        var page = new LuxembourgSparqlLicencePage(Xml, PageRef, [CcBy], observed, observedLive: true);
        var bound = silent.WithLicencePage(page);
        Assert.AreEqual(page, bound.LicencePage());
        Assert.AreEqual("lu_rights_publisher_catalogue_only_file_silent", LuxembourgRightsChannelResolution.DisclosureCodeOf(bound.Disposition));
        Assert.IsTrue(LuxembourgRightsAdmissionPolicies.Admits(LuxembourgRightsAdmissionPolicies.PolicyFor(true, false, false, true), bound));
        Assert.IsFalse(LuxembourgRightsAdmissionPolicies.Admits(PinnedPolicyASha256, bound));
        Assert.IsFalse(LuxembourgRightsAdmissionPolicies.Admits(LuxembourgRightsAdmissionPolicies.PolicyFor(true, false, false, false), silent),
            "unbound, no page: never admitted");

        Assert.ThrowsExactly<ArgumentException>(() => silent.WithLicencePage(new LuxembourgSparqlLicencePage(Pdf, PageRef, [CcBy], observed, true)),
            "a page of another manifestation");
        Assert.ThrowsExactly<ArgumentException>(() => silent.WithLicencePage(new LuxembourgSparqlLicencePage(Xml, PageRef, [Scl], observed, true)),
            "other licences than the SPARQL channel's");
        Assert.ThrowsExactly<ArgumentException>(() => bound.WithLicencePage(page), "once");
        var agreed = Resolve(Reading(LuxembourgInFileRightsReadingTwoStatus.Bound, [CcBy], [CcBy], (Xml, [CcBy])));
        Assert.ThrowsExactly<ArgumentException>(() => agreed.WithLicencePage(page), "a two-channel agreement cites no page");
        Assert.IsNull(LuxembourgRightsChannelResolution.DisclosureCodeOf(agreed.Disposition));
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgSparqlLicencePage(Xml, PageRef, [CcBy], observed.ToOffset(TimeSpan.FromHours(2)), true));
    }

    // ---- Resolution /2, rule by rule. ----

    [TestMethod]
    public void TwinBlocksBothCcByAgreeRederived()
    {
        var result = Resolve(Reading(LuxembourgInFileRightsReadingTwoStatus.Bound, [CcBy], [CcBy], (Xml, [CcBy]), (Pdf, [CcBy])),
            catalogue: [(Xml, CcBy), (Pdf, CcBy)]);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.AgreedCcByRederived, result.Disposition);
        Assert.AreEqual("rights_agreed_dual_channel_cc_by_4_0_rederived", result.ReasonCode);
        Assert.IsFalse(result.ChannelsAgreeOnAdmittingLicence, "resolution /1's agreement is a different disposition");
        Assert.AreEqual(LuxembourgRightsChannels.ResolutionTwoProfileSha256, result.ResolutionRuleSha256());
        Assert.AreEqual(InFileIndex, result.InFileObservations.EnumerationRef);
        Assert.AreEqual(Body, result.InFileObservation!.EvidenceRef);
        CollectionAssert.AreEqual(new[] { CcBy }, result.InFileObservation.LicenceIris.ToArray());
        Assert.AreEqual(Run, result.InFileObservations.RunIdentity);
    }

    [TestMethod]
    public void AnOwnBlockOfCcByBesideASclSiblingIsAConflict() => Assert.AreEqual(
        LuxembourgRightsChannelDisposition.Conflict,
        Resolve(Reading(LuxembourgInFileRightsReadingTwoStatus.Bound, [CcBy], [CcBy, Scl], (Xml, [CcBy]), (Pdf, [Scl]))).Disposition);

    [TestMethod]
    public void ABlockNamingAnIdentityTheCatalogueMarksSclOrDoubleLicensedIsAConflict()
    {
        var twin = Reading(LuxembourgInFileRightsReadingTwoStatus.Bound, [CcBy], [CcBy], (Xml, [CcBy]), (Pdf, [CcBy]));
        Assert.AreEqual(LuxembourgRightsChannelDisposition.Conflict, Resolve(twin, catalogue: [(Pdf, Scl)]).Disposition);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.Conflict, Resolve(twin, catalogue: [(Pdf, CcBy), (Pdf, Scl)]).Disposition);
        var unbound = Reading(LuxembourgInFileRightsReadingTwoStatus.Unbound, [], [CcBy], (ActXml, [CcBy]));
        Assert.AreEqual(LuxembourgRightsChannelDisposition.Conflict, Resolve(unbound, catalogue: [(ActXml, Scl)]).Disposition);
    }

    [TestMethod]
    public void AnUnboundFileNamingOnlyUnknownIdentitiesIsQuarantinedAndOneNamingAKnownCcByIdentityIsSingleChannel()
    {
        var unbound = Reading(LuxembourgInFileRightsReadingTwoStatus.Unbound, [], [CcBy], (ActXml, [CcBy]));
        Assert.AreEqual(LuxembourgRightsChannelDisposition.TypedQuarantineInFileIdentityUnknown, Resolve(unbound).Disposition);
        var known = Resolve(unbound, catalogue: [(ActXml, CcBy)]);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.SparqlCcByInFileNamesOtherIdentity, known.Disposition);
        Assert.IsNull(known.InFileObservation, "a block naming another identity never supplies the held file's licence");
        Assert.IsFalse(LuxembourgRightsAdmissionPolicies.Admits(PinnedPolicyASha256, known.Disposition));
    }

    /// <summary>
    /// Rule 12 (the review of #999): a file naming one identity the catalogue licenses CC BY and one it does not know is a
    /// quarantine, never <c>names_other_identity</c>, so L2 can never admit it; and so is an unbound file naming no identity.
    /// </summary>
    [TestMethod]
    public void AnUnboundFileNamingAnyUnknownIdentityOrNoneIsQuarantined()
    {
        const string Unknown = "http://data.legilux.public.lu/eli/etat/leg/loi/1999/01/01/a1/jo/fr/xml";
        var mixed = Resolve(Reading(LuxembourgInFileRightsReadingTwoStatus.Unbound, [], [CcBy], (ActXml, [CcBy]), (Unknown, [CcBy])),
            catalogue: [(ActXml, CcBy)]);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.TypedQuarantineInFileIdentityUnknown, mixed.Disposition);
        Assert.IsFalse(LuxembourgRightsAdmissionPolicies.Admits(
            LuxembourgRightsAdmissionPolicies.PolicyFor(false, false, true, false), mixed.Disposition, true), "L2 (a) does not admit it");
        var nameless = Resolve(Reading(LuxembourgInFileRightsReadingTwoStatus.Unbound, [], [CcBy]));
        Assert.AreEqual(LuxembourgRightsChannelDisposition.TypedQuarantineInFileIdentityUnknown, nameless.Disposition,
            "a licence in a block naming nothing names no identity to check");
        var bothKnown = Resolve(Reading(LuxembourgInFileRightsReadingTwoStatus.Unbound, [], [CcBy], (ActXml, [CcBy]), (Unknown, [CcBy])),
            catalogue: [(ActXml, CcBy), (Unknown, CcBy)]);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.SparqlCcByInFileNamesOtherIdentity, bothKnown.Disposition,
            "every identity it names is CC BY in the catalogue");
    }

    [TestMethod]
    public void TheSingleChannelReadingsEachHaveTheirOwnDisposition()
    {
        Assert.AreEqual(LuxembourgRightsChannelDisposition.SparqlCcByInFileSilent,
            Resolve(Reading(LuxembourgInFileRightsReadingTwoStatus.Silent, [], [])).Disposition);
        var empty = Resolve(Reading(LuxembourgInFileRightsReadingTwoStatus.OwnBlockEmpty, [], [CcBy], (Xml, []), (Pdf, [CcBy])));
        Assert.AreEqual(LuxembourgRightsChannelDisposition.SparqlCcByInFileOwnBlockEmpty, empty.Disposition);
        Assert.AreEqual(0, empty.InFileObservation!.LicenceIris.Count, "the own block was read and states nothing");
        Assert.AreEqual(LuxembourgRightsChannelDisposition.SparqlCcByInFileNotApplicable,
            Resolve(Reading(LuxembourgInFileRightsReadingTwoStatus.NotApplicable, [], [])).Disposition);
    }

    [TestMethod]
    public void TheValueRulesKeepTheirOrder()
    {
        var bound = Reading(LuxembourgInFileRightsReadingTwoStatus.Bound, [CcBy], [CcBy], (Xml, [CcBy]));
        var silent = Reading(LuxembourgInFileRightsReadingTwoStatus.Silent, [], []);
        var rejected = Reading(LuxembourgInFileRightsReadingTwoStatus.AmbiguousBinding, [], []);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected,
            Resolve(rejected, sparql: [CcBy, Scl]).Disposition, "an in-file rejection outranks SPARQL multiple, as in /1");
        Assert.AreEqual(LuxembourgRightsChannelDisposition.Multiple, Resolve(silent, sparql: [CcBy, Scl]).Disposition,
            "SPARQL multiple outranks a silent file");
        Assert.AreEqual(LuxembourgRightsChannelDisposition.MissingValue, Resolve(bound, sparql: []).Disposition);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.TypedQuarantineUnrepresentableLicenceShape,
            Resolve(bound, unrepresentable: 1).Disposition, "an unrepresentable shape dominates");
        Assert.AreEqual(LuxembourgRightsChannelDisposition.Conflict, Resolve(bound, sparql: [Scl]).Disposition,
            "a CC BY own block against a licenceSCL catalogue is a conflict");
        Assert.AreEqual(LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl,
            Resolve(Reading(LuxembourgInFileRightsReadingTwoStatus.Bound, [Scl], [Scl], (Xml, [Scl])), sparql: [Scl]).Disposition);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.TypedQuarantineUnruledLicence,
            Resolve(Reading(LuxembourgInFileRightsReadingTwoStatus.Bound, [Unruled], [Unruled], (Xml, [Unruled])), sparql: [Unruled]).Disposition);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.ChannelEnumerationUnproven, Resolve(bound, sparqlRow: false).Disposition);
    }

    [TestMethod]
    public void IndependenceAndTheBoundRunIdentityAreStillRequired()
    {
        var bound = Reading(LuxembourgInFileRightsReadingTwoStatus.Bound, [CcBy], [CcBy], (Xml, [CcBy]));
        Assert.AreEqual(LuxembourgRightsChannelDisposition.Stale, Resolve(bound, inFileRun: StaleRun).Disposition,
            "an in-file channel carrying another run identity than the rights' bound one is stale");
        Assert.AreEqual(LuxembourgRightsChannelDisposition.EvidenceNotIndependent, Resolve(bound, inFileIndex: SparqlEnumeration).Disposition);
        var sharedBody = new LuxembourgInFileRightsReadingTwo(Xml, SparqlRow, LuxembourgInFileRightsReadingTwoStatus.Bound, [CcBy], [CcBy],
            [new LuxembourgInFileNamedIdentity(Xml, [CcBy])], []);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.EvidenceNotIndependent, Resolve(sharedBody).Disposition);
    }

    [TestMethod]
    public void AReadingIsConsistentWithItsStatus()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Reading(LuxembourgInFileRightsReadingTwoStatus.Bound, [], [CcBy]));
        Assert.ThrowsExactly<ArgumentException>(() => Reading(LuxembourgInFileRightsReadingTwoStatus.Silent, [], [CcBy]));
        Assert.ThrowsExactly<ArgumentException>(() => Reading(LuxembourgInFileRightsReadingTwoStatus.Bound, [CcBy], []),
            "an own-block licence the file does not declare");
        Assert.ThrowsExactly<ArgumentException>(() => Reading(LuxembourgInFileRightsReadingTwoStatus.MalformedXml, [], [CcBy]));
        Assert.ThrowsExactly<ArgumentException>(() => Reading(LuxembourgInFileRightsReadingTwoStatus.Bound, [" " + CcBy], [" " + CcBy]));
    }

    // ---- Plumbing. ----

    private static LuxembourgInFileRightsReadingTwo Reading(
        LuxembourgInFileRightsReadingTwoStatus status, string[] own, string[] declared, params (string Identity, string[] Licences)[] named) =>
        new(Xml, Body, status, own, declared, named.Select(static block => new LuxembourgInFileNamedIdentity(block.Identity, block.Licences)).ToArray(), [Xml]);

    private static LuxembourgRightsChannelResolution Resolve(
        LuxembourgInFileRightsReadingTwo reading,
        string[]? sparql = null,
        (string Manifestation, string Licence)[]? catalogue = null,
        int unrepresentable = 0,
        bool sparqlRow = true,
        SourceArtifactRef? inFileRun = null,
        SourceArtifactRef? inFileIndex = null)
    {
        var licences = sparql ?? [CcBy];
        var rows = sparqlRow
            ? new[] { new LuxembourgRightsChannelObservation(Xml, Run, SparqlRow, licences.Order(StringComparer.Ordinal).ToArray(), unrepresentable) }
            : [];
        var known = (catalogue ?? []).Select(static row => (row.Manifestation, (string?)row.Licence))
            .Concat(licences.Select(static licence => (Xml, (string?)licence)));
        return LuxembourgRightsChannels.ResolveTwo(
            Xml, Run, new LuxembourgSparqlRightsChannelObservations(Run, SparqlEnumeration, rows),
            inFileRun ?? Run, inFileIndex ?? InFileIndex, reading, LuxembourgRightsCatalogue.From(known));
    }

    private static SourceArtifactRef Run { get; } = Artifact("07b972ed-2a90-4f0a-af09-19ba7c86bc26", '1');

    private static SourceArtifactRef StaleRun { get; } = Artifact("bd96f7f4-24a8-44d1-b9cc-4ca1716918d0", '2');

    private static SourceArtifactRef SparqlRow { get; } = Artifact("4a204e37-4b19-4f73-b3e2-101f4a12a861", '3');

    private static SourceArtifactRef Body { get; } = Artifact("b458bce1-a4df-40a9-981c-037961aacb4f", '4');

    private static SourceArtifactRef SparqlEnumeration { get; } = Artifact("0bf0868f-4bf6-4759-8897-4106168d34f4", '6');

    private static SourceArtifactRef InFileIndex { get; } = Artifact("6ae809e6-b8aa-4038-a71a-70b228f71004", '7');

    private static SourceArtifactRef PageRef { get; } = Artifact("e095c0de-3aa2-457e-ad93-ec4b8f66f39f", '8');

    private static SourceArtifactRef Artifact(string id, char digestCharacter) => new("urn:uuid:" + id, new string(digestCharacter, 64));
}
