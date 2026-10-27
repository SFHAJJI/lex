using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Platform;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Release 2's rights through the derive's own path (LU-DATA-DESIGN v2 §1.1, §1.2, §2.2 "R1 with rights"; slice S4r's
/// acceptance): a consolidation acquired by the fixture's acquisition, its rights resolved by the acquisition (reading /1,
/// resolution /1), re-derived under <c>lu-release-2</c> (reading /2, resolution /2, policy A), built into the first mount,
/// written, verified and opened by the API. One case per shape of the file's <c>scl:JOLUXManifestation</c> blocks.
/// </summary>
/// <remarks>
/// LEGAL CARE. Every body is synthetic: <see cref="LuxembourgRederivationTests.SyntheticConsolidation"/>'s identification and
/// articles, its manifestation blocks rewritten here in the publisher's shape. No publisher file is read.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgRightsReleaseTwoTests
{
    private const string Xml = LuxembourgFirstMountAcquisitionTests.XmlManifestation;
    private const string Pdf = LuxembourgFirstMountAcquisitionTests.ConsolidationExpression + "/pdf";
    private const string ActXml = LuxembourgFirstMountAcquisitionTests.ConsolidationParent + "/jo/fr/xml";
    private static readonly string CcBy = VerifiedLuxembourgSourceProfile.AdmittingLicence;
    private static readonly string Scl = VerifiedLuxembourgSourceProfile.NonAdmittingLicenceScl;
    private static readonly DateTimeOffset DerivedAt = new(2026, 10, 27, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BuildTime = new(2026, 10, 12, 18, 17, 5, TimeSpan.Zero);

    internal static LuxembourgRederivationRequest Release2(string? expected = null) =>
        new(LuxembourgRederivation.Release2RuleSet,
            LuxembourgRederivation.RulesOf(LuxembourgRederivation.Release2RuleSet),
            LuxembourgRightsAdmissionPolicies.PolicyASha256,
            DerivedAt,
            expected);

    /// <summary>
    /// The finding's shape (run 15's <c>loi/2021/01/22/a45/consolide/20211217</c>): one FRBRthis of the XML manifestation and
    /// two blocks, the XML file's and its PDF twin's, both CC BY. Reading /1 refuses the file for its second block, so the
    /// acquisition withholds its text; reading /2 binds the XML file's own block, both channels say CC BY, and policy A admits
    /// it: the text is in the corpus, the index and the evidence bundle, which names <c>agreed_cc_by_rederived</c>. Built
    /// from the acquisition's own derivations (policy /1), the same body stays withheld.
    /// </summary>
    [TestMethod]
    public async Task TwoAgreeingBlocksAreWithheldUnderReadingOneAndServedUnderPolicyA()
    {
        var outcome = await DeriveAsync(Body(Block(Xml, CcBy), Block(Pdf, CcBy)), openMount: true);

        Assert.AreEqual(LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected, outcome.Acquired);
        Assert.AreEqual(LexCorpus6OutcomeKind.RightsWithheld, outcome.BaselineOutcome, "policy /1: withheld");
        Assert.AreEqual(0L, outcome.BaselineArticles, "policy /1: no text of it is indexed");
        Assert.AreEqual(NoRow, outcome.BaselineTextStatus, "index 9 records the states of works with a served state only");
        Assert.AreEqual(LuxembourgRightsChannelDisposition.AgreedCcByRederived, outcome.Rederived.Disposition);
        Assert.AreEqual(LuxembourgRightsChannels.ResolutionV2ProfileSha256, outcome.Rederived.ResolutionRuleSha256);
        var reading = outcome.Rederived.InFileReadingV2!;
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.Bound, reading.Status);
        CollectionAssert.AreEqual(new[] { CcBy }, reading.OwnBlockLicenceIris.ToArray());
        CollectionAssert.AreEqual(new[] { Xml, Pdf }, reading.NamedIdentities.Select(static named => named.Identity).ToArray(),
            "the PDF twin is a named identity, never the held body's licence");

        Assert.AreEqual(LexCorpus6OutcomeKind.Acquired, outcome.Member.Outcome, "policy A admits it");
        Assert.AreEqual(LuxembourgRightsChannelDisposition.AgreedCcByRederived, outcome.Member.LuxembourgRights!.Disposition);
        Assert.AreEqual(LuxembourgRightsChannels.ResolutionV2ProfileSha256, outcome.Member.LuxembourgRights.ResolutionRule);
        Assert.AreEqual(
            LexCorpus6LuxembourgRights.ComputeBindingSha256(outcome.Member.RunIdentity, outcome.Member.LuxembourgRights.BoundRunIdentity,
                outcome.Member.LuxembourgRights.SelectedWemi.IdentitySha256, LuxembourgRightsChannels.ResolutionV2ProfileSha256,
                LuxembourgInFileRightsReaderV2Tests.PinnedReadingV2ProfileSha256, LuxembourgRightsAdmissionPolicies.PolicyASha256,
                outcome.Record.Sha256),
            outcome.Member.LuxembourgRights.BindingSha256, "binding /2: the rules, the policy and the record");

        // Binding /2 covers the corpus's record: a header naming another record no longer binds the member.
        var otherRecord = outcome.CorpusText.Replace(
            $"\"luxembourg_rederivation\":\"{outcome.Record.Sha256}\"", $"\"luxembourg_rederivation\":\"{new string('e', 64)}\"",
            StringComparison.Ordinal);
        Assert.AreNotEqual(outcome.CorpusText, otherRecord);
        StringAssert.Contains(
            Assert.ThrowsExactly<ArgumentException>(() => VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(Encoding.UTF8.GetBytes(otherRecord)))
                .ToString(), "binding /2");
        Assert.ThrowsExactly<ArgumentException>(() => LexCorpus6MountView.ParseCanonicalAndVerify(Encoding.UTF8.GetBytes(otherRecord)));
        CollectionAssert.AreEqual(Array.Empty<string>(), outcome.Member.Gaps.ToArray());
        Assert.AreEqual(LuxembourgRightsAdmissionPolicies.PolicyASha256, outcome.Policy);
        StringAssert.Contains(outcome.CorpusText, $"\"resolution_rule\":\"{LuxembourgRightsChannels.ResolutionV2ProfileSha256}\"");
        Assert.AreEqual("held", outcome.TextStatus);
        Assert.AreEqual(2L, outcome.Articles, "both articles' text is indexed");

        // The API's mount serves the text, under the corpus's policy, and says the rule it applied.
        var bundle = outcome.Bundle!.Value;
        Assert.AreEqual("agreed_cc_by_rederived", bundle.GetProperty("rights_disposition").GetString());
        Assert.AreEqual(V3CorpusMount.EvidenceBundleRightsRuleNineA, bundle.GetProperty("rights_rule").GetString());
        var state = bundle.GetProperty("states").EnumerateArray().Single();
        Assert.AreEqual("agreed_cc_by_rederived", state.GetProperty("sources").EnumerateArray().Single().GetProperty("rights_disposition").GetString());
        Assert.AreEqual(2, state.GetProperty("articles").GetArrayLength());
        StringAssert.Contains(state.GetProperty("articles")[0].GetProperty("text").GetString(), "Texte synthétique du premier article.");

        // The record names Release 2's five rules, policy A and the rights re-resolution.
        using var record = JsonDocument.Parse(outcome.Record.CanonicalBytes);
        var rules = record.RootElement.GetProperty("rules");
        Assert.AreEqual("lu-release-2", rules.GetProperty("rule_set").GetString());
        CollectionAssert.AreEqual(
            new[]
            {
                "lex-lu-in-file-rights-reading/2", "lex-lu-rights-resolution/2", "lex-v3-luxembourg-akn-article-inventory-profile/4",
                "lex-v3-luxembourg-akn-legal-content-profile/3", "lex-v3-luxembourg-expression-state/2",
            },
            rules.GetProperty("profiles").EnumerateArray().Select(static rule => rule.GetProperty("profile").GetString()).ToArray());
        Assert.AreEqual(LuxembourgRightsAdmissionPolicies.PolicyASha256, rules.GetProperty("admission_policy_sha256").GetString());
        Assert.AreEqual("re_resolved_reading_2_keeping_agreed_admissions", rules.GetProperty("rights_resolution").GetString());
        var rights = record.RootElement.GetProperty("rights");
        Assert.AreEqual(LuxembourgRightsChannels.ResolutionV2ProfileSha256, rights.GetProperty("resolution_profile_sha256").GetString());
        Assert.AreEqual("typed_quarantine_in_file_reading_rejected",
            rights.GetProperty("dispositions_as_acquired")[0].GetProperty("disposition").GetString());
        Assert.AreEqual("agreed_cc_by_rederived",
            record.RootElement.GetProperty("counts").GetProperty("rights_dispositions")[0].GetProperty("disposition").GetString());
        // The evidence index /2 is held in custody and names the reading.
        var index = await CustodyRestore.ReadByDigestCheckedAsync(
            outcome.Store, rights.GetProperty("in_file_evidence_sha256").GetString()!, CancellationToken.None);
        using var evidence = JsonDocument.Parse(index);
        Assert.AreEqual("lex-lu-in-file-rights-evidence/2", evidence.RootElement.GetProperty("schema").GetString());
        Assert.AreEqual("bound", evidence.RootElement.GetProperty("readings")[0].GetProperty("status").GetString());
    }

    /// <summary>
    /// The held manifestation's block alone, CC BY: the acquisition already admits it (agreed_same_run_cc_by). Reading /2
    /// agrees, so R1 keeps the acquisition's own resolution and its member's rights block and binding /1, byte for byte.
    /// </summary>
    [TestMethod]
    public async Task TheHeldManifestationsBlockAloneKeepsTheAcquisitionsAdmission()
    {
        var outcome = await DeriveAsync(Body(Block(Xml, CcBy)));

        Assert.AreEqual(LuxembourgRightsChannelDisposition.AgreedSameRunCcBy, outcome.Acquired);
        Assert.AreEqual(LexCorpus6OutcomeKind.Acquired, outcome.BaselineOutcome);
        Assert.AreSame(outcome.AcquiredResolution, outcome.Rederived, "the acquisition's resolution, kept");
        Assert.IsNull(outcome.Rederived.ResolutionRuleSha256);
        Assert.AreEqual(LexCorpus6OutcomeKind.Acquired, outcome.Member.Outcome);
        Assert.IsNull(outcome.Member.LuxembourgRights!.ResolutionRule, "a kept admission writes no rule");
        Assert.AreEqual(
            LexCorpus6LuxembourgRights.ComputeBindingSha256(outcome.Member.RunIdentity, outcome.Member.LuxembourgRights.BoundRunIdentity,
                outcome.Member.LuxembourgRights.SelectedWemi.IdentitySha256),
            outcome.Member.LuxembourgRights.BindingSha256, "binding /1");
        Assert.AreEqual("held", outcome.TextStatus);
        using var record = JsonDocument.Parse(outcome.Record.CanonicalBytes);
        Assert.AreEqual(1, record.RootElement.GetProperty("rights").GetProperty("acquired_admissions_kept").GetInt32());
        Assert.AreEqual("agreed_cc_by_rederived",
            record.RootElement.GetProperty("rights").GetProperty("dispositions_under_resolution2")[0].GetProperty("disposition").GetString(),
            "resolution /2 agrees with the admission it keeps");
    }

    /// <summary>
    /// The blocks disagree: the XML file's own block says CC BY and its PDF twin's says licenceSCL. Rule 8: a declared value
    /// that is not the SPARQL value is a conflict, whichever block states it. Withheld, with its typed reason.
    /// </summary>
    [TestMethod]
    public async Task DisagreeingBlocksAreAConflictAndStayWithheld()
    {
        var outcome = await DeriveAsync(Body(Block(Xml, CcBy), Block(Pdf, Scl)));
        AssertWithheld(outcome, LuxembourgRightsChannelDisposition.Conflict, "rights_conflict");
        CollectionAssert.AreEqual(new[] { CcBy, Scl }.Order(StringComparer.Ordinal).ToArray(),
            outcome.Rederived.InFileReadingV2!.DeclaredLicenceIris.ToArray());
    }

    /// <summary>
    /// Both blocks say CC BY, but the publisher's catalogue licenses the PDF twin licenceSCL: the file claims CC BY for an
    /// identity the catalogue marks otherwise (the design's 152). A conflict, withheld.
    /// </summary>
    [TestMethod]
    public async Task ABlockNamingAnIdentityTheCatalogueLicensesOtherwiseIsAConflict()
    {
        var outcome = await DeriveAsync(Body(Block(Xml, CcBy), Block(Pdf, CcBy)), catalogue: [(Pdf, Scl)]);
        AssertWithheld(outcome, LuxembourgRightsChannelDisposition.Conflict, "rights_conflict");
    }

    /// <summary>
    /// No block names the fetched file: the file's only block names the base act's <c>/jo</c> manifestation, which the
    /// catalogue does not know. Nothing the file says can be checked: a typed quarantine. With the catalogue licensing the act
    /// CC BY, the file names another identity: a single-channel disposition, which policy A does not admit.
    /// </summary>
    [TestMethod]
    public async Task NoBlockNamingTheHeldFileStaysWithheld()
    {
        var unknown = await DeriveAsync(Body(Block(ActXml, CcBy)));
        Assert.AreEqual(LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected, unknown.Acquired, "reading /1 refuses it");
        AssertWithheld(unknown, LuxembourgRightsChannelDisposition.TypedQuarantineInFileIdentityUnknown,
            "rights_typed_quarantine_in_file_identity_unknown");
        Assert.AreEqual(LuxembourgInFileRightsReadingV2Status.Unbound, unknown.Rederived.InFileReadingV2!.Status);

        var known = await DeriveAsync(Body(Block(ActXml, CcBy)), catalogue: [(ActXml, CcBy)]);
        AssertWithheld(known, LuxembourgRightsChannelDisposition.SparqlCcByInFileNamesOtherIdentity,
            "rights_sparql_cc_by_in_file_names_other_identity");
    }

    /// <summary>
    /// The held file's own block states licenceSCL while the catalogue says CC BY: a conflict under both readings, withheld
    /// under every policy. (The acquisition's reading /1 reads it too, and resolution /1 says conflict.)
    /// </summary>
    [TestMethod]
    public async Task ANonCcByLicenceInTheOwnBlockStaysWithheld()
    {
        var outcome = await DeriveAsync(Body(Block(Xml, Scl)));
        Assert.AreEqual(LuxembourgRightsChannelDisposition.Conflict, outcome.Acquired);
        AssertWithheld(outcome, LuxembourgRightsChannelDisposition.Conflict, "rights_conflict");
    }

    /// <summary>
    /// The held file's own block states no licence, beside a CC BY twin: single channel (own_block_empty), whatever the sibling
    /// says; policy A does not admit it.
    /// </summary>
    [TestMethod]
    public async Task AnOwnBlockWithoutALicenceStaysWithheldBesideACcBySibling()
    {
        var outcome = await DeriveAsync(Body(Block(Xml, null), Block(Pdf, CcBy)));
        AssertWithheld(outcome, LuxembourgRightsChannelDisposition.SparqlCcByInFileOwnBlockEmpty, "rights_sparql_cc_by_in_file_own_block_empty");
    }

    /// <summary>
    /// A member the acquisition admitted that reading /2 does not agree with refuses the whole re-derivation, naming it: its
    /// own block says CC BY (reading /1 reads only that), and an annex's block elsewhere in the file says licenceSCL.
    /// </summary>
    [TestMethod]
    public async Task AnAcquisitionAdmissionReadingTwoDoesNotAgreeWithRefusesTheReDerivation()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var annex = "<attachments><attachment><doc name=\"annexe\"><meta><identification source=\"#lex-synthetic\">"
            + $"<scl:JOLUXManifestation><scl:jolux scl:name=\"uriThis\">{LuxembourgFirstMountAcquisitionTests.ConsolidationExpression}/annexe/xml</scl:jolux>"
            + $"<scl:jolux scl:name=\"license\">{Scl}</scl:jolux></scl:JOLUXManifestation></identification></meta>"
            + "<mainBody><p>Annexe synthétique.</p></mainBody></doc></attachment></attachments>\n";
        var body = Encoding.UTF8.GetString(Body(Block(Xml, CcBy))).Replace("</body>\n", "</body>\n" + annex, StringComparison.Ordinal);
        var (_, verified) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot(), Encoding.UTF8.GetBytes(body));
        Assert.IsTrue(verified.Delivered, verified.Detail);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.AgreedSameRunCcBy,
            verified.Run!.HeldBodyDerivationPopulation!.Inputs.Single().RightsResolution!.Disposition, "reading /1 reads the own block only");

        var refused = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(
            () => LuxembourgRederivation.ApplyAsync(store, verified, Release2(), CancellationToken.None));
        StringAssert.Contains(refused.Message, "was admitted by the acquisition (agreed_same_run_cc_by) and reading /2 does not agree");
        StringAssert.Contains(refused.Message, LuxembourgFirstMountAcquisitionTests.ConsolidationIri);
        StringAssert.Contains(refused.Message, "agreed_same_run_cc_by -> conflict");
    }

    /// <summary>
    /// The owner's ruling L1b, switched on for this test only (it is OFF in the release rule set): a file whose own block names
    /// the fetched file but states no licence is served on the publisher's SPARQL declaration alone, bound to the retained
    /// licence page holding its row and disclosed with that page's observation date, in the corpus and in the bundle. Under
    /// policy A the same body stays withheld (the test above). A corpus whose policy would not admit it refuses to read.
    /// </summary>
    [TestMethod]
    public async Task UnderTheOwnersRulingL1bAnOwnBlockWithoutALicenceIsServedWithItsDisclosure()
    {
        var policy = LuxembourgRightsAdmissionPolicies.PolicyFor(l1: false, l1b: true, l2: false, livePagesOnly: false);
        Assert.AreNotEqual(LuxembourgRederivation.AdmissionPolicyOf(LuxembourgRederivation.Release2RuleSet), policy, "the switch is off in the release");
        var outcome = await DeriveAsync(Body(Block(Xml, null), Block(Pdf, CcBy)), openMount: true, policy: policy);

        Assert.AreEqual(LuxembourgRightsChannelDisposition.SparqlCcByInFileOwnBlockEmpty, outcome.Rederived.Disposition);
        var page = outcome.Rederived.LicencePage!;
        Assert.AreEqual(Xml, page.ManifestationIri);
        CollectionAssert.AreEqual(new[] { CcBy }, page.LicenceIris.ToArray());
        Assert.IsTrue(page.ObservedLive, "an acquisition that did not resume observed every page live");
        Assert.IsGreaterThan(0, (await CustodyRestore.ReadByDigestCheckedAsync(outcome.Store, page.PageRef.Sha256, CancellationToken.None)).Length,
            "the licence page is a retained custody object");

        Assert.AreEqual(LexCorpus6OutcomeKind.Acquired, outcome.Member.Outcome);
        var disclosure = outcome.Member.LuxembourgRights!.RightsDisclosure!;
        Assert.AreEqual("lu_rights_publisher_catalogue_only_own_block_empty", disclosure.Code);
        Assert.AreEqual(page.PageRef, disclosure.LicencePage);
        Assert.IsTrue(disclosure.ObservedLive);
        CollectionAssert.Contains(outcome.Member.LuxembourgRights.EvidenceRefs.ToArray(), page.PageRef, "the admission cites its page");
        CollectionAssert.AreEqual(Array.Empty<string>(), outcome.Member.Gaps.ToArray(), "a disclosure is not a gap");
        Assert.AreEqual(policy, outcome.Policy);
        Assert.AreEqual("held", outcome.TextStatus);

        var bundle = outcome.Bundle!.Value;
        Assert.AreEqual("sparql_cc_by_in_file_own_block_empty", bundle.GetProperty("rights_disposition").GetString());
        StringAssert.EndsWith(bundle.GetProperty("rights_rule").GetString(), V3CorpusMount.EvidenceBundleSingleChannelRule);
        var source = bundle.GetProperty("states").EnumerateArray().Single().GetProperty("sources").EnumerateArray().Single();
        var said = source.GetProperty("rights_disclosure");
        Assert.AreEqual("lu_rights_publisher_catalogue_only_own_block_empty", said.GetProperty("code").GetString());
        Assert.AreEqual(page.PageRef.Sha256, said.GetProperty("licence_page_sha256").GetString());
        Assert.AreEqual(page.ObservedAt.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            said.GetProperty("licence_page_observed_on").GetString());
        Assert.IsTrue(said.GetProperty("licence_page_observed_live").GetBoolean());

        using (var record = JsonDocument.Parse(outcome.Record.CanonicalBytes))
        {
            var rights = record.RootElement.GetProperty("rights");
            Assert.AreEqual(1, rights.GetProperty("single_channel_admitted").GetInt32());
            var index = await CustodyRestore.ReadByDigestCheckedAsync(
                outcome.Store, rights.GetProperty("licence_pages_sha256").GetString()!, CancellationToken.None);
            using var pages = JsonDocument.Parse(index);
            Assert.AreEqual("lex-lu-sparql-licence-pages/1", pages.RootElement.GetProperty("schema").GetString());
            Assert.AreEqual(policy, record.RootElement.GetProperty("rules").GetProperty("admission_policy_sha256").GetString());
        }

        // The corpus holds every admission to its own policy: under policy A, or under option (b) with a page not observed
        // live, the disclosed member contradicts it and the corpus does not read. (The option windows themselves are
        // LuxembourgRightsResolutionV2Tests'.)
        var optionB = LuxembourgRightsAdmissionPolicies.PolicyFor(l1: false, l1b: true, l2: false, livePagesOnly: true);
        foreach (var (what, altered) in new[]
                 {
                     ("policy A", outcome.CorpusText.Replace(policy, LuxembourgRightsAdmissionPolicies.PolicyASha256, StringComparison.Ordinal)),
                     ("option (b), a replayed page", outcome.CorpusText.Replace(policy, optionB, StringComparison.Ordinal)
                         .Replace("\"observed_live\":true", "\"observed_live\":false", StringComparison.Ordinal)),
                 })
        {
            Assert.AreNotEqual(outcome.CorpusText, altered, what);
            Assert.ThrowsExactly<ArgumentException>(() => VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(Encoding.UTF8.GetBytes(altered)), what);
            Assert.ThrowsExactly<ArgumentException>(() => LexCorpus6MountView.ParseCanonicalAndVerify(Encoding.UTF8.GetBytes(altered)), what);
        }

        // Binding /2 covers the policy (the review of #999): another registered policy that would admit the same member, named
        // in the header in place of the one it was derived under, no longer binds it either.
        var swapped = outcome.CorpusText.Replace(policy, optionB, StringComparison.Ordinal);
        StringAssert.Contains(
            Assert.ThrowsExactly<ArgumentException>(() => LexCorpus6MountView.ParseCanonicalAndVerify(Encoding.UTF8.GetBytes(swapped))).ToString(),
            "binding /2");
        Assert.IsNotNull(LexCorpus6MountView.ParseCanonicalAndVerify(Encoding.UTF8.GetBytes(outcome.CorpusText)));
    }

    /// <summary>
    /// The owner's ruling L1: a file that states no licence anywhere is served on the SPARQL declaration alone with its
    /// disclosure; under policy A it is withheld with its typed reason. Only the release's own policy derives through the
    /// offline path: R1 refuses a ruling's policy there, and the rulings' door refuses anything but a registered policy over A.
    /// </summary>
    [TestMethod]
    public async Task UnderTheOwnersRulingL1ASilentFileIsServedAndUnderPolicyAItIsNot()
    {
        var silent = Body();
        var withheld = await DeriveAsync(silent);
        AssertWithheld(withheld, LuxembourgRightsChannelDisposition.SparqlCcByInFileSilent, "rights_sparql_cc_by_in_file_silent");
        Assert.IsNull(withheld.Rederived.LicencePage, "policy A reads no licence page");
        Assert.IsNull(withheld.Member.LuxembourgRights!.RightsDisclosure);

        var policy = LuxembourgRightsAdmissionPolicies.PolicyFor(l1: true, l1b: false, l2: false, livePagesOnly: true);
        var served = await DeriveAsync(silent, policy: policy);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.SparqlCcByInFileSilent, served.Rederived.Disposition);
        Assert.AreEqual(LexCorpus6OutcomeKind.Acquired, served.Member.Outcome, "option (b): the page was observed live");
        Assert.AreEqual("lu_rights_publisher_catalogue_only_file_silent", served.Member.LuxembourgRights!.RightsDisclosure!.Code);
        Assert.AreEqual(2L, served.Articles);

        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (_, verified) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot(), silent);
        var refused = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgRederivation.ApplyAsync(
            store, verified, Release2() with { AdmissionPolicySha256 = policy }, CancellationToken.None));
        StringAssert.Contains(refused.Message, "no policy is substituted");
        foreach (var other in new[] { LuxembourgRightsAdmissionPolicies.Policy1Sha256, new string('a', 64) })
        {
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgRederivation.ApplyUnderOwnersRulingAsync(
                store, verified, Release2() with { AdmissionPolicySha256 = other }, CancellationToken.None), other);
        }
    }

    /// <summary>
    /// R1 refuses, naming the member, when resolution /2 is not terminal evidence: the acquisition's SPARQL channel holds no
    /// row for the held file (<c>channel_enumeration_unproven</c>). Nothing is minted.
    /// </summary>
    [TestMethod]
    public async Task ANonTerminalResolutionRefusesTheReDerivation()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (_, verified) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot(), Body(Block(Xml, CcBy), Block(Pdf, CcBy)));
        var altered = await WithAcquiredRightsAsync(store, verified, static rights => LuxembourgRightsChannels.Resolve(
            rights.SelectedManifestationIri, rights.BoundRunIdentity,
            new LuxembourgSparqlRightsChannelObservations(rights.SparqlObservations.RunIdentity, rights.SparqlObservations.EnumerationRef, []),
            rights.InFileObservations));
        var refused = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(
            () => LuxembourgRederivation.ApplyAsync(store, altered, Release2(), CancellationToken.None));
        StringAssert.Contains(refused.Message, "resolves to a non-terminal disposition under resolution /2");
        StringAssert.Contains(refused.Message, LuxembourgFirstMountAcquisitionTests.ConsolidationIri);
        StringAssert.Contains(refused.Message, "-> channel_enumeration_unproven");
    }

    /// <summary>
    /// R1 refuses a held body that resolves to <c>non_admitting_licence_scl</c> under resolution /2 (a silent file whose
    /// SPARQL channel says licenceSCL): holding would have differed, so the acquisition's held set no longer stands.
    /// </summary>
    [TestMethod]
    public async Task ANonAdmittingLicenceSclRefusesTheReDerivation()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (_, verified) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot(), Body());
        var altered = await WithAcquiredRightsAsync(store, verified, static rights => LuxembourgRightsChannels.Resolve(
            rights.SelectedManifestationIri, rights.BoundRunIdentity,
            new LuxembourgSparqlRightsChannelObservations(
                rights.SparqlObservations.RunIdentity, rights.SparqlObservations.EnumerationRef,
                rights.SparqlObservations.Observations.Select(row => new LuxembourgRightsChannelObservation(
                    row.ManifestationIri, row.RunIdentity, row.EvidenceRef, [Scl])).ToArray()),
            rights.InFileObservations));
        var refused = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(
            () => LuxembourgRederivation.ApplyAsync(store, altered, Release2(), CancellationToken.None));
        StringAssert.Contains(refused.Message, "resolves to non_admitting_licence_scl under resolution /2, so its holding would have differed");
        StringAssert.Contains(refused.Message, LuxembourgFirstMountAcquisitionTests.ConsolidationIri);
    }

    /// <summary>
    /// Under one of the owner's rulings, R1 refuses a single-channel admission for which no retained licence page holds the
    /// manifestation's licence row: here the SPARQL channel's rights evidence index lists no page at all.
    /// </summary>
    [TestMethod]
    public async Task ASingleChannelAdmissionWithoutItsLicencePageRefusesTheReDerivation()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (_, verified) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot(), Body());
        var empty = Encoding.UTF8.GetBytes("{\"schema\":\"lex-v3-test-rights-evidence-index-without-pages\",\"deliveries\":[],\"observedObjectCount\":0,\"observationBatches\":[]}");
        var held = await store.CreateAsync(empty, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var noPages = new SourceArtifactRef("urn:uuid:7e57a6e5-0000-4000-8000-000000000001", held.Reference.ContentSha256);
        var altered = await WithAcquiredRightsAsync(store, verified, rights => LuxembourgRightsChannels.Resolve(
            rights.SelectedManifestationIri, rights.BoundRunIdentity,
            new LuxembourgSparqlRightsChannelObservations(rights.SparqlObservations.RunIdentity, noPages, rights.SparqlObservations.Observations),
            rights.InFileObservations));
        var policy = LuxembourgRightsAdmissionPolicies.PolicyFor(l1: true, l1b: false, l2: false, livePagesOnly: false);
        var refused = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgRederivation.ApplyUnderOwnersRulingAsync(
            store, altered, Release2() with { AdmissionPolicySha256 = policy }, CancellationToken.None));
        StringAssert.Contains(refused.Message, "(sparql_cc_by_in_file_silent) finds no retained licence page holding exactly its SPARQL licence");
        StringAssert.Contains(refused.Message, LuxembourgFirstMountAcquisitionTests.ConsolidationIri);

        // Under policy A the same body reads no page and is withheld: the refusal is the ruling's alone.
        var underA = await LuxembourgRederivation.ApplyAsync(store, altered, Release2(), CancellationToken.None);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.SparqlCcByInFileSilent,
            underA.Run!.HeldBodyDerivationPopulation!.Inputs.Single().RightsResolution!.Disposition);
    }

    /// <summary>
    /// The acquisition with each held member's acquired rights resolution replaced by <paramref name="change"/>'s, the frozen
    /// inventory /3 and content /1 run again over that population (R1 requires the acquisition's derivations to be of its own
    /// population), and its catalog and checkpoints as they were.
    /// </summary>
    private static async Task<LuxembourgFirstMountAcquisitionResult> WithAcquiredRightsAsync(
        ICustodyStore store,
        LuxembourgFirstMountAcquisitionResult verified,
        Func<LuxembourgRightsChannelResolution, LuxembourgRightsChannelResolution> change)
    {
        var run = verified.Run!;
        var population = run.HeldBodyDerivationPopulation!;
        var altered = run.WithHeldBodyDerivationPopulation(population.WithRederivedRights(
            population.Inputs.ToDictionary(static input => input.ObjectOrdinal, input => change(input.RightsResolution!))));
        var inventory = await new LuxembourgAknArticleInventoryProducer(store)
            .RunAsync(altered.HeldBodyDerivationPopulation!, CancellationToken.None);
        var content = await new LuxembourgAknLegalContentProfileProducer(store).RunAsync(inventory, CancellationToken.None);
        var result = LuxembourgFirstMountAcquisitionResult.Success(
            altered, verified.Profile!, verified.VocabularyEvidenceRef!, inventory, content);
        if (verified.VocabularyCheckpointRef is { } vocabulary)
        {
            result = result.WithVocabularyCheckpoint(vocabulary);
        }

        if (verified.CheckpointRef is { } checkpoint)
        {
            result = result.WithCheckpoint(checkpoint, verified.Resumption);
        }

        return verified.PopulationScopeManifestRef is { } scope ? result.WithPopulationScopeManifest(scope) : result;
    }

    /// <summary>The population's door re-binds exactly its members: a map that drops a member or moves its manifestation refuses.</summary>
    [TestMethod]
    public async Task TheRebindingDoorRefusesAChangedMemberSet()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (_, verified) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot(), Body(Block(Xml, CcBy)));
        var population = verified.Run!.HeldBodyDerivationPopulation!;
        var input = population.Inputs.Single();
        Assert.ThrowsExactly<CustodyIntegrityException>(() => population.WithRederivedRights(
            new Dictionary<int, LuxembourgRightsChannelResolution>()), "a dropped member");
        Assert.ThrowsExactly<CustodyIntegrityException>(() => population.WithRederivedRights(
            new Dictionary<int, LuxembourgRightsChannelResolution>
            {
                [input.ObjectOrdinal] = input.RightsResolution!,
                [input.ObjectOrdinal + 1000] = input.RightsResolution!,
            }), "an added member");
        var moved = LuxembourgRightsChannels.ResolveV2(
            Pdf, input.RightsResolution!.BoundRunIdentity, input.RightsResolution.SparqlObservations, input.RightsResolution.BoundRunIdentity,
            input.RightsResolution.InFileObservations.EnumerationRef,
            new LuxembourgInFileRightsReadingV2(Pdf, new SourceArtifactRef("urn:uuid:00000000-0000-4000-8000-000000000001", new string('1', 64)),
                LuxembourgInFileRightsReadingV2Status.Silent, [], [], [], []),
            LuxembourgRightsCatalogue.Empty);
        Assert.ThrowsExactly<CustodyIntegrityException>(() => population.WithRederivedRights(
            new Dictionary<int, LuxembourgRightsChannelResolution> { [input.ObjectOrdinal] = moved }), "a moved member");
        var same = population.WithRederivedRights(
            new Dictionary<int, LuxembourgRightsChannelResolution> { [input.ObjectOrdinal] = input.RightsResolution! });
        Assert.AreSame(population.CorpusRecordSet, same.CorpusRecordSet);
        Assert.AreSame(input.CorpusRecord, same.Inputs.Single().CorpusRecord);
        Assert.AreSame(input.Receipt, same.Inputs.Single().Receipt);
    }

    /// <summary>
    /// D1's path for Release 2: <c>rederive-inputs</c> under <c>lu-release-2</c> names policy A and the five rule digests;
    /// a derive of those inputs (R0, then R1 with rights, the build and the write) starts no network operation and writes
    /// the same files as an in-process build over R1; policy /1 under <c>lu-release-2</c>, or policy A under
    /// <c>lu-release-1</c>, refuses.
    /// </summary>
    [TestMethod]
    public async Task ReleaseTwoInputsDeriveThroughTheOfflinePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "lex-v3-release2-inputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
            var (europe, verified) = await V3FirstMountBuildTests.AcquireAsync(
                store, V3FirstMountBuildTests.CheckoutRoot(), Body(Block(Xml, CcBy), Block(Pdf, CcBy)));
            var baseInputs = await V3OfflineMount.CaptureAsync(store, europe, verified, [EuAxiomWiringHarness.Seed(null).Celex],
                LuxembourgFirstMountAcquisitionTests.ActRange, BuildTime, null, CancellationToken.None);
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => V3OfflineMount.CaptureRederivationAsync(store, baseInputs,
                LuxembourgRederivation.Release2RuleSet, LuxembourgRightsAdmissionPolicies.Policy1Sha256, DerivedAt, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => V3OfflineMount.CaptureRederivationAsync(store, baseInputs,
                LuxembourgRederivation.Release1RuleSet, LuxembourgRightsAdmissionPolicies.PolicyASha256, DerivedAt, CancellationToken.None));
            var inputs = await V3OfflineMount.CaptureRederivationAsync(store, baseInputs, LuxembourgRederivation.Release2RuleSet,
                LuxembourgRightsAdmissionPolicies.PolicyASha256, DerivedAt, CancellationToken.None);
            using (var document = JsonDocument.Parse(await CustodyRestore.ReadByDigestCheckedAsync(store, inputs.Sha256, CancellationToken.None)))
            {
                Assert.AreEqual("lu-release-2", document.RootElement.GetProperty("rule_set").GetString());
                Assert.AreEqual(LuxembourgRightsAdmissionPolicies.PolicyASha256, document.RootElement.GetProperty("admission_policy").GetString());
                CollectionAssert.AreEqual(
                    LuxembourgRederivation.RulesOf(LuxembourgRederivation.Release2RuleSet).Select(static rule => rule.Sha256).ToArray(),
                    document.RootElement.GetProperty("rules").EnumerateArray().Select(static rule => rule.GetProperty("sha256").GetString()).ToArray());
            }

            var derived = Path.Combine(root, "derived");
            V3CorpusMountWrite write;
            using (var network = new V3NetworkActivityListener())
            {
                write = await V3OfflineMount.DeriveAsync(store, inputs, derived, CancellationToken.None);
                Assert.AreEqual(0L, network.Total, "a derive of Release 2's inputs starts no network operation: " + network.Describe());
            }

            var rederived = await LuxembourgRederivation.ApplyAsync(store, verified, Release2(), CancellationToken.None);
            Assert.AreEqual(rederived.Rederivation!.Reference, write.LuxembourgRederivation, "the derive's record is R1's");
            using var expectedBuild = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(BuildTime)).RunAsync(europe, rederived, CancellationToken.None);
            var expected = Path.Combine(root, "expected");
            await V3CorpusMountWriter.WriteAsync(expectedBuild, expected, null, CancellationToken.None, BuildTime);
            CollectionAssert.AreEqual(V3FirstMountBuildTests.MountDigests(expected), V3FirstMountBuildTests.MountDigests(derived));
            Assert.AreEqual(2L, ArticleCount(derived), "both articles' text is indexed by the offline derive");

            var sealedInputs = await V3OfflineMount.SealRederivationAsync(store, inputs, write.LuxembourgRederivation!.Sha256, CancellationToken.None);
            var sealedDerived = Path.Combine(root, "sealed");
            await V3OfflineMount.DeriveAsync(store, sealedInputs, sealedDerived, CancellationToken.None);
            CollectionAssert.AreEqual(V3FirstMountBuildTests.MountDigests(derived), V3FirstMountBuildTests.MountDigests(sealedDerived));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // ---- Plumbing. ----

    private sealed record Outcome(
        EuAcquisitionTestFixture.EuInMemoryCustodyStore Store,
        LuxembourgRightsChannelDisposition Acquired,
        LuxembourgRightsChannelResolution AcquiredResolution,
        LexCorpus6OutcomeKind BaselineOutcome,
        string BaselineTextStatus,
        long BaselineArticles,
        LuxembourgRightsChannelResolution Rederived,
        LexCorpus6Member Member,
        string? Policy,
        string CorpusText,
        string TextStatus,
        long Articles,
        LuxembourgRederivationRecord Record,
        JsonElement? Bundle);

    private static void AssertWithheld(Outcome outcome, LuxembourgRightsChannelDisposition disposition, string reason)
    {
        Assert.AreEqual(disposition, outcome.Rederived.Disposition);
        Assert.AreEqual(LexCorpus6OutcomeKind.RightsWithheld, outcome.Member.Outcome, "policy A withholds it");
        Assert.AreEqual(disposition, outcome.Member.LuxembourgRights!.Disposition);
        CollectionAssert.AreEqual(new[] { reason }, outcome.Member.Gaps.ToArray(), "the typed reason");
        Assert.AreEqual(0L, outcome.Articles, "no text of it is indexed");
        Assert.AreEqual(NoRow, outcome.TextStatus, "its work has no served state, so index 9 records none of it");
        Assert.AreEqual(LexCorpus6OutcomeKind.RightsWithheld, outcome.BaselineOutcome, "withheld under policy /1 too");
        Assert.AreEqual(0L, outcome.BaselineArticles);
    }

    /// <summary>
    /// The fixture's acquisition of <paramref name="body"/> (with <paramref name="catalogue"/>'s licence assertions added to
    /// its typed assertions), built from its own derivations (policy /1) and re-derived under Release 2, both written and
    /// verified; the Release 2 mount opened by the API when <paramref name="openMount"/>, and its evidence bundle asked.
    /// </summary>
    private static async Task<Outcome> DeriveAsync(
        byte[] body, (string Manifestation, string Licence)[]? catalogue = null, bool openMount = false, string? policy = null)
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (europe, verified) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot(), body);
        Assert.IsTrue(europe.Delivered, europe.Detail);
        Assert.IsTrue(verified.Delivered, verified.Detail);
        verified = WithCatalogueFacts(verified, catalogue ?? []);

        var acquired = verified.Run!.HeldBodyDerivationPopulation!.Inputs.Single().RightsResolution!;
        LuxembourgFirstMountAcquisitionResult rederived;
        using (var network = new V3NetworkActivityListener())
        {
            rederived = policy is null
                ? await LuxembourgRederivation.ApplyAsync(store, verified, Release2(), CancellationToken.None)
                : await LuxembourgRederivation.ApplyUnderOwnersRulingAsync(
                    store, verified, Release2() with { AdmissionPolicySha256 = policy }, CancellationToken.None);
            Assert.AreEqual(0L, network.Total, "R1 with rights starts no network operation: " + network.Describe());
        }

        var clock = new V3OfflineMount.BuildClock(BuildTime);
        var root = Path.Combine(Path.GetTempPath(), "lex-v3-release2-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var baseline = await new V3FirstMountBuild(store, clock).RunAsync(europe, verified, CancellationToken.None);
            Assert.IsTrue(baseline.Delivered, $"{baseline.Refusal}: {baseline.Detail}");
            var baselineDirectory = Path.Combine(root, "baseline");
            await V3CorpusMountWriter.WriteAsync(baseline, baselineDirectory, null, CancellationToken.None, BuildTime);
            var baselineMember = HeldMember(baseline);

            using var build = await new V3FirstMountBuild(store, clock).RunAsync(europe, rederived, CancellationToken.None);
            Assert.IsTrue(build.Delivered, $"{build.Refusal}: {build.Detail}");
            var directory = Path.Combine(root, "release2");
            await V3CorpusMountWriter.WriteAsync(build, directory, null, CancellationToken.None, BuildTime);
            var verification = await V3CorpusMountWriter.VerifyAsync(directory, CancellationToken.None);
            Assert.IsTrue(verification.Verified, verification.Detail);

            JsonElement? bundle = null;
            if (openMount)
            {
                using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
                Assert.IsNotNull(mount, "the API's own mount opens a Release 2 build");
                var (workKey, date) = HeldState(directory);
                var envelope = await BundleAsync(mount, new { identifier = $"/lu-legilux/{workKey}", date, language = "fra" });
                Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, envelope.Refusal?.Code);
                bundle = envelope.Result!.Value.Clone();
            }

            return new Outcome(
                store,
                acquired.Disposition,
                acquired,
                baselineMember.Outcome,
                TextStatusOf(baselineDirectory),
                ArticleCount(baselineDirectory),
                rederived.Run!.HeldBodyDerivationPopulation!.Inputs.Single().RightsResolution!,
                HeldMember(build),
                build.Corpus!.VerifiedSet.Set.LuxembourgRightsAdmissionPolicy,
                Encoding.UTF8.GetString(build.Corpus.CanonicalBytes.Span),
                TextStatusOf(directory),
                ArticleCount(directory),
                rederived.Rederivation!,
                bundle);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static LexCorpus6Member HeldMember(V3FirstMountBuildResult build) => build.Corpus!.VerifiedSet.Set.Members
        .Single(static member => member.Publisher == PublisherId.LuLegilux && member.BodySha256 is not null);

    /// <summary>What a text status reads when the index holds no state of the work (index 9: a work with no served state).</summary>
    private const string NoRow = "no state row";

    private static string TextStatusOf(string directory) =>
        Scalar(directory, $"SELECT text_status FROM states WHERE expression_iri = '{LuxembourgFirstMountAcquisitionTests.ConsolidationExpression}'")
            as string ?? NoRow;

    private static long ArticleCount(string directory) => (long)Scalar(directory, "SELECT count(*) FROM articles")!;

    private static (string WorkKey, string Date) HeldState(string directory)
    {
        using var connection = LuxembourgIndexBuilder.Open(
            Path.Combine(directory, V3FirstMountBuildResult.LuxembourgIndexFileName), SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT work_key, applicability_date FROM states WHERE text_status = 'held'";
        using var reader = command.ExecuteReader();
        Assert.IsTrue(reader.Read(), "a held state");
        return (reader.GetString(0), reader.GetString(1));
    }

    private static object? Scalar(string directory, string sql)
    {
        using var connection = LuxembourgIndexBuilder.Open(
            Path.Combine(directory, V3FirstMountBuildResult.LuxembourgIndexFileName), SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    /// <summary>
    /// The synthetic consolidation with its identification's one <c>scl:JOLUXManifestation</c> replaced by
    /// <paramref name="blocks"/>, in order. The FRBR identity stays the XML manifestation's.
    /// </summary>
    internal static byte[] Body(params string[] blocks)
    {
        var synthetic = Encoding.UTF8.GetString(LuxembourgRederivationTests.SyntheticConsolidation());
        var own = Block(Xml, CcBy);
        Assert.AreEqual(1, CountOf(synthetic, own), "the synthetic body's own block");
        return Encoding.UTF8.GetBytes(synthetic.Replace(own, string.Concat(blocks), StringComparison.Ordinal));
    }

    /// <summary>One <c>scl:JOLUXManifestation</c> block naming <paramref name="uriThis"/>, with <paramref name="licence"/> when given.</summary>
    internal static string Block(string uriThis, string? licence) =>
        $"<scl:JOLUXManifestation><scl:jolux scl:name=\"uriThis\">{uriThis}</scl:jolux>"
        + (licence is null ? "" : $"<scl:jolux scl:name=\"license\">{licence}</scl:jolux>")
        + "</scl:JOLUXManifestation>\n";

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// The acquisition with the catalogue's <c>dateApplicability</c> of the consolidation (2026-02-01, its file's own date), so
    /// the index records its state, and <paramref name="licences"/> among its typed <c>jolux:license</c> assertions, as the
    /// catalogue would state them (from the observation that read the run's other assertions); the same run objects otherwise.
    /// </summary>
    private static LuxembourgFirstMountAcquisitionResult WithCatalogueFacts(
        LuxembourgFirstMountAcquisitionResult acquired, (string Manifestation, string Licence)[] licences)
    {
        var run = acquired.Run!;
        var evidence = run.TypedAssertions[0].FactDisposition.EvidenceRef;
        const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
        var extra = licences.Select(licence => new LuxembourgTypedAssertion(
            new LuxembourgObservedAssertion(
                licence.Manifestation, "http://data.legilux.public.lu/resource/ontology/jolux#license",
                LuxembourgAssertionObjectKind.Iri, licence.Licence, "", "", evidence),
            new LuxembourgAssertionFactDisposition(
                LuxembourgAssertionPredicate.License, LuxembourgAssertionFactKind.RightsAndProvenance, evidence),
            null,
            null)).Append(new LuxembourgTypedAssertion(
                new LuxembourgObservedAssertion(
                    LuxembourgFirstMountAcquisitionTests.ConsolidationIri,
                    "http://data.legilux.public.lu/resource/ontology/jolux#dateApplicability",
                    LuxembourgAssertionObjectKind.Literal, "2026-02-01", XsdDate, "", evidence),
                new LuxembourgAssertionFactDisposition(
                    LuxembourgAssertionPredicate.DateApplicability, LuxembourgAssertionFactKind.ConsolidationApplicability, evidence),
                null,
                new LuxembourgConsolidationApplicabilityDateFact(
                    LuxembourgConsolidationApplicabilityDatePredicate.DateApplicability, "2026-02-01", XsdDate, evidence))).ToArray();
        var redelivered = LuxembourgQueryExecutionResult.Delivered(
            run.Topology, run.FamilyOutcomes, run.RelationFamilyAcquisitions, run.ResolvedRelations, run.LocalInboundRelations,
            [.. run.TypedAssertions, .. extra],
            run.ResourceObservationSubjects, run.ResourceObservationExclusions, run.ScopeManifestReceipt!,
            run.ScopeManifestCanonicalSha256!, run.DocumentAcquisitionOutcomesByOrdinal!, run.CorpusRecordSetRef!,
            run.CorpusRecordSetReceipt!, run.CorpusRecordSet!, run.ObservedObjectIdentitySetRef!,
            run.ObservedObjectIdentitySetReceipt!, run.ObservedObjectIdentitySet!, run.HeldBodyDerivationPopulation!,
            run.GazetteBodySetsByOrdinal!, run.GazetteListingFetchRefusalsByOrdinal!,
            run.GazetteListingsWithContradictoryLegalValueByOrdinal!, run.PopulationLedger!);
        if (run.DocumentCheckpointRef is { } documents)
        {
            redelivered = redelivered.WithDocumentCheckpoint(documents);
        }

        if (run.GazetteCheckpointRef is { } gazette)
        {
            redelivered = redelivered.WithGazetteCheckpoint(gazette);
        }

        if (run.AcquisitionCheckpointRef is { } query)
        {
            redelivered = redelivered.WithAcquisitionCheckpoint(query);
        }

        var result = LuxembourgFirstMountAcquisitionResult.Success(
            redelivered, acquired.Profile!, acquired.VocabularyEvidenceRef!, acquired.AknInventory!, acquired.AknLegalContent!);
        if (acquired.VocabularyCheckpointRef is { } vocabulary)
        {
            result = result.WithVocabularyCheckpoint(vocabulary);
        }

        if (acquired.CheckpointRef is { } checkpoint)
        {
            result = result.WithCheckpoint(checkpoint, acquired.Resumption);
        }

        return acquired.PopulationScopeManifestRef is { } scope ? result.WithPopulationScopeManifest(scope) : result;
    }

    private static async Task<V3Envelope> BundleAsync(V3CorpusMount mount, object parameters)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { operation_id = "evidence_bundle", parameters }));
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "release-2-rights-evidence-bundle";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = V3RestRouteBinding.EvidenceBundle.RawTarget;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => V3CorpusResolveMountTests.ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode,
            Encoding.UTF8.GetString(V3CorpusResolveMountTests.ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(V3CorpusResolveMountTests.ResponseBytes(context), V3OperationRegistry.Reviewed);
    }
}
