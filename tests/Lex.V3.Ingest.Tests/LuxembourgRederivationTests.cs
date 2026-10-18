using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Api;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// R1 for the profiles (slice S4 of the Luxembourg design, Release 1): over an acquisition R0 verified, inventory /4 and
/// content /3 run over R0's own population, the acquisition result is minted on R0's own run, and a
/// <c>lex-lu-rederivation/1</c> record is held; the first mount builds end to end from it, and every refusal the design
/// names for this release holds.
/// </summary>
/// <remarks>
/// LEGAL CARE. The Luxembourg body here is synthetic: every byte of <see cref="SyntheticConsolidation"/> is written in this
/// file, in the publisher's shape (an Akoma Ntoso consolidation with its identification block, one article and one article
/// carrying index terms). No publisher file is read by these tests beyond the retained PDF the shared acquisition fixture
/// already serves, and none of the withheld fixtures.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgRederivationTests
{
    // Pinned digests, each computed outside this code base: the frozen rules R0 verifies under (inventory /3, content
    // /1), the later rules R1 runs (inventory /4 as S2 pinned it, content /3 as S3b pinned it; content /2, S3a's, which
    // the rule set named until S3b, now refuses), and policy /1 (Python, over the canonical text below).
    private const string Inventory3RuleSha256 = "70e926111b25a4d364e96b679de0baaa090a869175a82d42e0052572804cf42e";
    private const string Content1RuleSha256 = "dd32d9a59ea33b8c9e9cd51b7b2657a6fa2b716ad00e35db075416ba4cd38a9b";
    private const string Inventory4RuleSha256 = "8a011d874aed08b161d90c900d54e9ab26dd9f925ed37323e544e44b2056c63f";
    private const string Content2RuleSha256 = "bf3fea5a5229d1a9f035d8d56816bd054abee30591370dfc6fe62f154dfaef2d";
    private const string Content3RuleSha256 = LuxembourgAknLegalContentProfile3ProducerTests.Content3RuleSha256;

    /// <summary>State digest /2's rule profile (index 9, S6), recomputed in Python from its text.</summary>
    private const string State2RuleSha256 = LuxembourgIndexCatalogueStatesTests.PinnedStateRuleProfileSha256;
    private const string Policy1Sha256 = "a477f4f2e6e0468bc9069da742f6f34f248ec831e01277a40552cb1e5bad2abb";

    private static readonly DateTimeOffset DerivedAt = new(2026, 10, 17, 6, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A consolidation in the publisher's Akoma Ntoso shape, every byte synthetic: its identification names the fetched
    /// manifestation with CC BY 4.0 (so both rights channels agree), the work, expression and complex work the frozen
    /// inventory requires, and an applicability date. <c>art_1</c> is plain wording; <c>art_2</c> carries index terms,
    /// which content /1 cannot read (a typed gap) and content /3 keeps as metadata, as /2 does.
    /// </summary>
    internal static byte[] SyntheticConsolidation()
    {
        const string root = LuxembourgFirstMountAcquisitionTests.ConsolidationIri;
        const string parent = LuxembourgFirstMountAcquisitionTests.ConsolidationParent;
        const string expression = LuxembourgFirstMountAcquisitionTests.ConsolidationExpression;
        const string manifestation = LuxembourgFirstMountAcquisitionTests.XmlManifestation;
        var licence = VerifiedLuxembourgSourceProfile.AdmittingLicence;
        return Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + "<akomaNtoso xmlns=\"http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13\" xmlns:scl=\"http://www.scl.lu\">\n"
            + "<act name=\"consolidation\">\n<meta>\n<identification source=\"#lex-synthetic\">\n"
            + $"<FRBRWork><FRBRthis value=\"{root}\"/></FRBRWork>\n"
            + $"<FRBRExpression><FRBRthis value=\"{expression}\"/></FRBRExpression>\n"
            + $"<FRBRManifestation><FRBRthis value=\"{manifestation}\"/></FRBRManifestation>\n"
            + $"<scl:JOLUXComplexWork><scl:jolux scl:name=\"uriThis\">{parent}</scl:jolux></scl:JOLUXComplexWork>\n"
            + $"<scl:JOLUXLegalResource><scl:jolux scl:name=\"uriThis\">{root}</scl:jolux>"
            + $"<scl:jolux scl:name=\"isMemberOf\">{parent}</scl:jolux>"
            + "<scl:jolux scl:name=\"dateApplicability\">2026-02-01</scl:jolux></scl:JOLUXLegalResource>\n"
            + $"<scl:JOLUXExpression><scl:jolux scl:name=\"uriThis\">{expression}</scl:jolux></scl:JOLUXExpression>\n"
            + $"<scl:JOLUXManifestation><scl:jolux scl:name=\"uriThis\">{manifestation}</scl:jolux>"
            + $"<scl:jolux scl:name=\"license\">{licence}</scl:jolux></scl:JOLUXManifestation>\n"
            + "</identification>\n</meta>\n<body>\n"
            + "<article id=\"art_1\"><num>Art. 1er.</num><content><p>Texte synthétique du premier article.</p></content></article>\n"
            + "<article id=\"art_2\"><scl:indexterms><scl:indexterm><scl:label>Terme synthétique</scl:label></scl:indexterm>"
            + "</scl:indexterms><num>Art. 2.</num><content><p>Un article dont les termes d'index sont des métadonnées.</p>"
            + "</content></article>\n"
            + "</body>\n</act>\n</akomaNtoso>\n");
    }

    internal static LuxembourgRederivationRequest Release1(DateTimeOffset? derivedAt = null, string? expected = null) =>
        new(LuxembourgRederivation.Release1RuleSet,
            LuxembourgRederivation.RulesOf(LuxembourgRederivation.Release1RuleSet),
            LuxembourgRightsAdmissionPolicies.Policy1Sha256,
            derivedAt ?? DerivedAt,
            expected);

    [TestMethod]
    public void TheRuleSetThePolicyAndTheFrozenRulesAreThePinnedDigests()
    {
        // R0's pins: the frozen rules stay what run 15's catalog verified.
        Assert.AreEqual(Inventory3RuleSha256, LuxembourgAknArticleInventoryProducer.RuleProfileSha256);
        Assert.AreEqual(Content1RuleSha256, LuxembourgAknLegalContentProfileProducer.RuleProfileSha256);

        CollectionAssert.AreEqual(new[] { LuxembourgRederivation.Release1RuleSet }, LuxembourgRederivation.ImplementedRuleSets.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                new LuxembourgRederivationRule("lex-v3-luxembourg-akn-article-inventory-profile/4", Inventory4RuleSha256),
                new LuxembourgRederivationRule("lex-v3-luxembourg-akn-legal-content-profile/3", Content3RuleSha256),
                new LuxembourgRederivationRule("lex-v3-luxembourg-expression-state/2", State2RuleSha256),
            },
            LuxembourgRederivation.RulesOf(LuxembourgRederivation.Release1RuleSet).ToArray());

        // Policy /1: today's admission, the two channels of one run agreeing on CC BY, by its pinned text and digest.
        var text = Encoding.UTF8.GetString(LuxembourgRightsAdmissionPolicies.CopyPolicy1Bytes());
        Assert.AreEqual(Policy1Sha256, LuxembourgRightsAdmissionPolicies.Policy1Sha256);
        Assert.AreEqual(Policy1Sha256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
        StringAssert.StartsWith(text, LuxembourgRightsAdmissionPolicies.Policy1Name + "\n");
        StringAssert.Contains(text, "\nadmits=" + ContractWire.NameOf(LuxembourgRightsChannelDisposition.AgreedSameRunCcBy) + "\n");
        Assert.AreEqual(Policy1Sha256, LuxembourgRederivation.AdmissionPolicyOf(LuxembourgRederivation.Release1RuleSet));
        CollectionAssert.AreEqual(new[] { Policy1Sha256 }, LuxembourgRightsAdmissionPolicies.RegisteredSha256s.ToArray());
        Assert.IsTrue(LuxembourgRightsAdmissionPolicies.IsRegistered(Policy1Sha256));
        Assert.IsFalse(LuxembourgRightsAdmissionPolicies.IsRegistered(new string('a', 64)));
        Assert.IsFalse(LuxembourgRightsAdmissionPolicies.IsRegistered(null));
    }

    /// <summary>
    /// The acceptance of S4: V3FirstMountBuild end to end over a re-derived population. R1 runs on R0's own run and
    /// population objects (the envelope's identity checks hold), keeps R0's references, admits the article content /1
    /// could not read, and the corpus, both indexes and the report say so; the API's own mount opens what was written.
    /// </summary>
    [TestMethod]
    public async Task AReDerivedPopulationBuildsTheFirstMountEndToEnd()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (europe, verified) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot(), SyntheticConsolidation());
        Assert.IsTrue(europe.Delivered, europe.Detail);
        Assert.IsTrue(verified.Delivered, verified.Detail);

        // R0 as acquired: inventory /3 inventories the body, content /1 reads art_1 and leaves art_2 a typed gap.
        var frozen = verified.AknLegalContent!.Outcomes;
        Assert.AreEqual(LuxembourgAknArticleInventoryDisposition.Inventoried, verified.AknInventory!.Outcomes.Single().Disposition);
        CollectionAssert.AreEqual(
            new[] { LuxembourgAknLegalContentDisposition.Admitted, LuxembourgAknLegalContentDisposition.UnsupportedContentShape },
            frozen.Select(static outcome => outcome.Disposition).ToArray());
        var rights = verified.Run!.HeldBodyDerivationPopulation!.Inputs.Single().RightsResolution!;
        Assert.AreEqual(LuxembourgRightsChannelDisposition.AgreedSameRunCcBy, rights.Disposition, "both channels agree on the synthetic body");

        // R1 inside a measurement of every network operation the process starts: it must start none.
        LuxembourgFirstMountAcquisitionResult rederived;
        using (var network = new V3NetworkActivityListener())
        {
            rederived = await LuxembourgRederivation.ApplyAsync(store, verified, Release1(), CancellationToken.None);
            Assert.AreEqual(0L, network.Total, "R1 starts no network operation: " + network.Describe());
        }

        // The acquisition result on R0's own run: the same objects and references, the later derivations.
        Assert.IsTrue(rederived.Delivered);
        Assert.AreSame(verified.Run, rederived.Run);
        Assert.AreSame(verified.Profile, rederived.Profile);
        Assert.AreEqual(verified.VocabularyEvidenceRef, rederived.VocabularyEvidenceRef);
        Assert.AreEqual(verified.VocabularyCheckpointRef, rederived.VocabularyCheckpointRef);
        Assert.AreEqual(verified.CheckpointRef, rederived.CheckpointRef);
        Assert.AreEqual(verified.PopulationScopeManifestRef, rederived.PopulationScopeManifestRef);
        Assert.AreEqual(verified.Resumption, rederived.Resumption);
        Assert.AreSame(verified.Run.HeldBodyDerivationPopulation, rederived.AknInventory!.SourcePopulation);
        Assert.AreSame(rederived.AknInventory, rederived.AknLegalContent!.SourceInventoryPopulation);
        Assert.AreEqual(Inventory4RuleSha256, rederived.AknInventory.RuleProfileSha256);
        Assert.AreEqual(Content3RuleSha256, rederived.AknLegalContent.RuleProfileSha256);
        Assert.AreEqual(DerivedAt, rederived.DerivedAt);
        var articles = rederived.AknLegalContent.Outcomes;
        CollectionAssert.AreEqual(
            new[] { LuxembourgAknLegalContentDisposition.Admitted, LuxembourgAknLegalContentDisposition.Admitted },
            articles.Select(static outcome => outcome.Disposition).ToArray());
        Assert.AreEqual("Terme synthétique", articles[1].Article!.IndexTerms!.Single().Label);
        Assert.AreEqual(0, articles[0].Article!.IndexTerms!.Count);

        // The record is held in custody under its own digest, and names R0's verified derivations and R1's outputs.
        var record = rederived.Rederivation!;
        CollectionAssert.AreEqual(record.CanonicalBytes.ToArray(),
            (await CustodyRestore.ReadByDigestCheckedAsync(store, record.Sha256, CancellationToken.None)).ToArray());
        Assert.AreEqual(verified.AknInventory.IdentitySha256, RecordValue(record, "verified_under", "inventory_sha256"));
        Assert.AreEqual(verified.AknLegalContent.IdentitySha256, RecordValue(record, "verified_under", "content_sha256"));
        Assert.AreEqual(rederived.AknInventory.IdentitySha256, record.InventoryIdentitySha256);
        Assert.AreEqual(rederived.AknLegalContent.IdentitySha256, record.ContentIdentitySha256);
        Assert.AreEqual(verified.CheckpointRef!.Sha256, record.AcquisitionCatalogSha256);

        // The first mount, end to end, over the re-derived population, and over R0's for comparison.
        var clock = new V3OfflineMount.BuildClock(new DateTimeOffset(2026, 10, 12, 18, 17, 5, TimeSpan.Zero));
        var build = await new V3FirstMountBuild(store, clock).RunAsync(europe, rederived, CancellationToken.None);
        Assert.IsTrue(build.Delivered, $"{build.Refusal}: {build.Detail}");
        var baseline = await new V3FirstMountBuild(store, clock).RunAsync(europe, verified, CancellationToken.None);
        Assert.IsTrue(baseline.Delivered, $"{baseline.Refusal}: {baseline.Detail}");

        var header = build.Corpus!.VerifiedSet.Set;
        Assert.AreEqual(record.Sha256, header.LuxembourgRederivation);
        Assert.AreEqual(Policy1Sha256, header.LuxembourgRightsAdmissionPolicy);
        Assert.AreSame(record, build.LuxembourgDerivation!.Record);
        Assert.AreEqual(DerivedAt, build.LuxembourgDerivation.DerivedAt);
        foreach (var identity in new[] { Inventory4RuleSha256, Content3RuleSha256, record.InventoryIdentitySha256, record.ContentIdentitySha256 })
        {
            CollectionAssert.Contains(header.ProfileIdentities.ToArray(), identity, "the profile identities name the population's profiles");
        }

        CollectionAssert.DoesNotContain(header.ProfileIdentities.ToArray(), Content1RuleSha256, "never the /1 constant over a /2 population");
        var baselineHeader = baseline.Corpus!.VerifiedSet.Set;
        Assert.IsNull(baselineHeader.LuxembourgRederivation);
        Assert.IsNull(baselineHeader.LuxembourgRightsAdmissionPolicy);
        Assert.IsNull(baseline.LuxembourgDerivation);
        CollectionAssert.Contains(baselineHeader.ProfileIdentities.ToArray(), Content1RuleSha256);
        CollectionAssert.DoesNotContain(baselineHeader.ProfileIdentities.ToArray(), Inventory3RuleSha256, "an inventory /3 is named by its identity alone, as before");
        var corpusText = Encoding.UTF8.GetString(build.Corpus.CanonicalBytes.Span);
        StringAssert.Contains(corpusText, $"\"luxembourg_rederivation\":\"{record.Sha256}\",\"luxembourg_rights_admission_policy\":\"{Policy1Sha256}\"");
        Assert.IsFalse(Encoding.UTF8.GetString(baseline.Corpus.CanonicalBytes.Span).Contains("luxembourg_rederivation", StringComparison.Ordinal),
            "a corpus from the acquisition's own derivations writes no re-derivation field");

        // Every EU member is byte-identical; the Luxembourg member is the one with a held body.
        var luMembers = header.Members.Where(static member => member.Publisher == PublisherId.LuLegilux).ToArray();
        CollectionAssert.AreEqual(
            baselineHeader.Members.Where(static member => member.Publisher != PublisherId.LuLegilux).Select(LexCorpus6Builder.WriteMember).Select(Convert.ToBase64String).ToArray(),
            header.Members.Where(static member => member.Publisher != PublisherId.LuLegilux).Select(LexCorpus6Builder.WriteMember).Select(Convert.ToBase64String).ToArray(),
            "every EU member stays byte-identical");
        Assert.AreEqual(2, luMembers.SelectMany(static member => member.Stage3Outcomes)
            .Count(static outcome => outcome.Disposition == LexCorpus6Stage3Disposition.AknAdmitted));
        Assert.AreEqual(1, baselineHeader.Members.Where(static member => member.Publisher == PublisherId.LuLegilux)
            .SelectMany(static member => member.Stage3Outcomes)
            .Count(static outcome => outcome.Disposition == LexCorpus6Stage3Disposition.AknAdmitted));

        var directory = Path.Combine(Path.GetTempPath(), "lex-v3-rederived-mount-" + Guid.NewGuid().ToString("N"));
        try
        {
            var write = await V3CorpusMountWriter.WriteAsync(build, directory, null, CancellationToken.None, clock.GetUtcNow());
            Assert.AreEqual(record.Reference, write.LuxembourgRederivation);
            var verification = await V3CorpusMountWriter.VerifyAsync(directory, CancellationToken.None);
            Assert.IsTrue(verification.Verified, verification.Detail);

            // The Luxembourg index holds both articles R1 admitted (R0 admitted one).
            using (var connection = LuxembourgIndexBuilder.Open(
                       Path.Combine(directory, V3FirstMountBuildResult.LuxembourgIndexFileName), Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT count(*) FROM articles";
                Assert.AreEqual(2L, command.ExecuteScalar());
                command.CommandText = "SELECT DISTINCT rule_profile_sha256 FROM articles";
                Assert.AreEqual(Content3RuleSha256, command.ExecuteScalar());
                command.CommandText = "SELECT built_at FROM observations";
                Assert.AreEqual("2026-10-12T18:17:05Z", command.ExecuteScalar(), "the build time stays the acquisition's");
            }

            // The build report's derivation block.
            using (var report = JsonDocument.Parse(await File.ReadAllBytesAsync(write.ReportPath)))
            {
                var derivation = report.RootElement.GetProperty("derivation");
                Assert.AreEqual("2026-10-17T06:00:00Z", derivation.GetProperty("derivedAt").GetString());
                Assert.AreEqual(LuxembourgRederivation.Release1RuleSet, derivation.GetProperty("ruleSet").GetString());
                Assert.AreEqual(Policy1Sha256, derivation.GetProperty("admissionPolicy").GetString());
                Assert.AreEqual(record.Sha256, derivation.GetProperty("record").GetProperty("Sha256").GetString());
                Assert.AreEqual("none_on_this_path", derivation.GetProperty("publisherSession").GetString(),
                    "the report states the path; a count belongs to the process that measured it");
                Assert.IsFalse(derivation.TryGetProperty("publisherRequests", out _), "no constant count stands in for a measurement");
                Assert.AreEqual(AcquisitionJournal.CurrentSource(), derivation.GetProperty("toolSource").GetString());
                Assert.AreEqual(3, derivation.GetProperty("rules").GetArrayLength());
                Assert.AreEqual(
                    $"Acquired by the Luxembourg acquisition whose catalog is {verified.CheckpointRef.Sha256}; re-derived from "
                    + $"that acquisition's custody under rule set lu-release-1 and admission policy {Policy1Sha256}, at the "
                    + "derivation time its inputs state (2026-10-17T06:00:00Z); the derivation path holds no publisher session, "
                    + "and the offline tool measures its process's network activity and refuses to finish with any.",
                    derivation.GetProperty("statement").GetString());
                Assert.AreEqual("2026-10-12T18:17:05+00:00", report.RootElement.GetProperty("derivationInputUtc").GetString(),
                    "the report's input clock stays the acquisition's");
            }

            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount, "the API's own mount opens a re-derived build");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The record names no clock and no tool commit: re-derived again, at another stated time, it is the same bytes and the
    /// same content-derived resource identity, and its shape is the one the design names.
    /// </summary>
    [TestMethod]
    public async Task TheRecordNamesNoClockAndIsReproducedByteForByte()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (_, verified) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot(), SyntheticConsolidation());
        Assert.IsTrue(verified.Delivered, verified.Detail);

        var first = (await LuxembourgRederivation.ApplyAsync(store, verified, Release1(), CancellationToken.None)).Rederivation!;
        var again = (await LuxembourgRederivation.ApplyAsync(store, verified, Release1(DerivedAt.AddDays(3)), CancellationToken.None)).Rederivation!;
        CollectionAssert.AreEqual(first.CanonicalBytes.ToArray(), again.CanonicalBytes.ToArray());
        Assert.AreEqual(first.Reference, again.Reference);
        Assert.AreEqual(ContentDerivedIdentity.DeriveUuidUrn(LuxembourgRederivationRecord.Schema, first.CanonicalBytes.Span), first.Reference.ResourceId);
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(first.CanonicalBytes.Span)), first.Sha256);

        var text = Encoding.UTF8.GetString(first.CanonicalBytes.Span);
        Assert.IsFalse(text.Contains("2026-10-17", StringComparison.Ordinal) || text.Contains("2026-10-20", StringComparison.Ordinal), "no clock");
        Assert.IsFalse(text.Contains(AcquisitionJournal.CurrentSource(), StringComparison.Ordinal), "no tool commit");
        using var document = JsonDocument.Parse(first.CanonicalBytes);
        var root = document.RootElement;
        CollectionAssert.AreEqual(
            new[] { "schema", "acquisition", "verified_under", "rules", "outputs", "counts", "publisher_session" },
            root.EnumerateObject().Select(static property => property.Name).ToArray());
        Assert.AreEqual(LuxembourgRederivationRecord.Schema, root.GetProperty("schema").GetString());
        Assert.AreEqual("none_on_this_path", root.GetProperty("publisher_session").GetString(),
            "the record states the path, never a count it did not measure");
        var acquisition = root.GetProperty("acquisition");
        Assert.AreEqual(verified.CheckpointRef!.Sha256, acquisition.GetProperty("catalog_sha256").GetString());
        Assert.AreEqual(verified.Run!.AcquisitionCheckpointRef!.Sha256, acquisition.GetProperty("query_checkpoint_sha256").GetString());
        Assert.AreEqual(verified.Run.CorpusRecordSetRef!.Sha256, acquisition.GetProperty("record_set_sha256").GetString());
        Assert.AreEqual(verified.Run.ObservedObjectIdentitySetRef!.Sha256, acquisition.GetProperty("observed_set_sha256").GetString());
        var input = verified.Run.HeldBodyDerivationPopulation!.Inputs.Single();
        Assert.AreEqual(input.CorpusRecord.RunIdentity.Sha256, acquisition.GetProperty("run_identity_sha256s").EnumerateArray().Single().GetString());
        Assert.AreEqual(input.CorpusRecord.ManifestRef.Sha256, acquisition.GetProperty("final_manifest_sha256s").EnumerateArray().Single().GetString());
        Assert.AreEqual(input.RightsResolution!.BoundRunIdentity.Sha256,
            acquisition.GetProperty("rights_bound_run_identity_sha256s").EnumerateArray().Single().GetString());
        Assert.AreEqual(JsonValueKind.Null, acquisition.GetProperty("scope_manifest_sha256").ValueKind, "an act range has no scope manifest");
        var verifiedUnder = root.GetProperty("verified_under");
        Assert.AreEqual(Inventory3RuleSha256, verifiedUnder.GetProperty("inventory_profile_sha256").GetString());
        Assert.AreEqual(Content1RuleSha256, verifiedUnder.GetProperty("content_profile_sha256").GetString());
        var rules = root.GetProperty("rules");
        Assert.AreEqual(LuxembourgRederivation.Release1RuleSet, rules.GetProperty("rule_set").GetString());
        CollectionAssert.AreEqual(new[] { Inventory4RuleSha256, Content3RuleSha256, State2RuleSha256 },
            rules.GetProperty("profiles").EnumerateArray().Select(static rule => rule.GetProperty("sha256").GetString()).ToArray());
        Assert.AreEqual(Policy1Sha256, rules.GetProperty("admission_policy_sha256").GetString());
        Assert.AreEqual("as_acquired", rules.GetProperty("rights_resolution").GetString());
        var counts = root.GetProperty("counts");
        Assert.AreEqual(1, counts.GetProperty("held_bodies").GetInt32());
        Assert.AreEqual("agreed_same_run_cc_by", counts.GetProperty("rights_dispositions")[0].GetProperty("disposition").GetString());
        Assert.AreEqual("inventoried", counts.GetProperty("inventory_dispositions")[0].GetProperty("disposition").GetString());
        Assert.AreEqual(0, counts.GetProperty("content_consistency_refusals").GetInt32());
        Assert.AreEqual(2, counts.GetProperty("admitted_articles").GetInt32());
        Console.WriteLine($"fixture record {first.Sha256} ({first.CanonicalBytes.Length} bytes): {text}");
    }

    /// <summary>A held body that no longer reads as its digest refuses: R1 never derives from bytes R0 did not verify.</summary>
    [TestMethod]
    public async Task AnAlteredBodyRefusesTheReDerivation()
    {
        var root = Path.Combine(Path.GetTempPath(), "lex-v3-rederive-tamper-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemCustodyStore(root);
            var (_, verified) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot(), SyntheticConsolidation());
            Assert.IsTrue(verified.Delivered, verified.Detail);
            var body = verified.Run!.HeldBodyDerivationPopulation!.Inputs.Single().Receipt.Reference.ContentSha256;
            var paths = Directory.EnumerateFiles(root, body, SearchOption.AllDirectories).ToArray();
            Assert.IsTrue(paths.Length > 0, "the body is held in this custody");
            foreach (var path in paths)
            {
                var bytes = await File.ReadAllBytesAsync(path);
                bytes[bytes.Length / 2] ^= 0x20;
                await File.WriteAllBytesAsync(path, bytes);
            }

            var refused = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
                LuxembourgRederivation.ApplyAsync(store, verified, Release1(), CancellationToken.None));
            StringAssert.Contains(refused.Message, body);
            StringAssert.Contains(refused.Message, "no longer reads as its digest");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A rule set or rule digest this build does not implement, or a policy the rule set does not name, refuses before any
    /// body is read: nothing is substituted.
    /// </summary>
    [TestMethod]
    public async Task AnUnimplementedRuleDigestOrPolicyRefusesBeforeAnyBodyIsRead()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (_, verified) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot(), SyntheticConsolidation());
        var rules = LuxembourgRederivation.RulesOf(LuxembourgRederivation.Release1RuleSet);
        var writesBefore = store.CreateCallCount;
        foreach (var (what, request, expected) in new (string, LuxembourgRederivationRequest, string)[]
                 {
                     ("an altered inventory digest", Release1() with { Rules = [rules[0] with { Sha256 = new string('e', 64) }, rules[1], rules[2]] },
                         "no rule digest is substituted"),
                     ("an altered content digest", Release1() with { Rules = [rules[0], rules[1] with { Sha256 = Inventory4RuleSha256 }, rules[2]] },
                         "no rule digest is substituted"),
                     ("the frozen content rule", Release1() with { Rules = [rules[0], new("lex-v3-luxembourg-akn-legal-content-profile/1", Content1RuleSha256), rules[2]] },
                         "no rule digest is substituted"),
                     ("a missing rule", Release1() with { Rules = [rules[0]] }, "no rule digest is substituted"),
                     // Inputs written before index 9 named inventory /4 and content /2 only: they refuse, and no build runs them
                     // under a state rule they did not name.
                     ("the rule set as it was before state digest /2", Release1() with { Rules = [rules[0], rules[1]] }, "no rule digest is substituted"),
                     ("an altered state digest rule", Release1() with { Rules = [rules[0], rules[1], rules[2] with { Sha256 = new string('d', 64) }] },
                         "no rule digest is substituted"),
                     // Inputs written before S3b named content /2: they refuse, and no build runs them under content /3.
                     ("the rule set as it was before content /3", Release1() with
                         { Rules = [rules[0], new("lex-v3-luxembourg-akn-legal-content-profile/2", Content2RuleSha256), rules[2]] },
                         "no rule digest is substituted"),
                     ("a rule renamed", Release1() with { Rules = [rules[0] with { Profile = "lex-v3-luxembourg-akn-article-inventory-profile/5" }, rules[1], rules[2]] },
                         "no rule digest is substituted"),
                     ("an unknown rule set", Release1() with { RuleSet = "lu-release-2" }, "no rule set is substituted"),
                     ("an unregistered policy", Release1() with { AdmissionPolicySha256 = new string('a', 64) }, "no policy is substituted"),
                 })
        {
            var refused = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(
                () => LuxembourgRederivation.ApplyAsync(store, verified, request, CancellationToken.None), what);
            StringAssert.Contains(refused.Message, expected, what);
        }

        Assert.AreEqual(writesBefore, store.CreateCallCount, "nothing was held");
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgRederivation.ApplyAsync(
            store, verified, Release1(DerivedAt.AddMilliseconds(500)), CancellationToken.None), "a derivation time is a whole second");
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgRederivation.ApplyAsync(
            store, verified, Release1(expected: "NOT-A-DIGEST"), CancellationToken.None), "a sealed record is a digest");
    }

    /// <summary>A record other than the one sealed inputs expect refuses, and nothing is minted or held for it.</summary>
    [TestMethod]
    public async Task ASealedRecordMismatchRefuses()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (_, verified) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot(), SyntheticConsolidation());
        var record = (await LuxembourgRederivation.ApplyAsync(store, verified, Release1(), CancellationToken.None)).Rederivation!;
        var sealedResult = await LuxembourgRederivation.ApplyAsync(store, verified, Release1(expected: record.Sha256), CancellationToken.None);
        Assert.AreEqual(record.Reference, sealedResult.Rederivation!.Reference, "the sealed record is reproduced");

        var other = new string('0', 64);
        var writesBefore = store.CreateCallCount;
        var refused = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            LuxembourgRederivation.ApplyAsync(store, verified, Release1(expected: other), CancellationToken.None));
        StringAssert.Contains(refused.Message, record.Sha256);
        StringAssert.Contains(refused.Message, other);
        Assert.AreEqual(writesBefore, store.CreateCallCount, "a refused record is not held");
    }

    /// <summary>R1 runs over R0's verified result only: never over a re-derived result, a refused one, or none.</summary>
    [TestMethod]
    public async Task TheReDerivationRunsOnlyOverTheVerifiedAcquisition()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (_, verified) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot(), SyntheticConsolidation());
        var rederived = await LuxembourgRederivation.ApplyAsync(store, verified, Release1(), CancellationToken.None);
        var again = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            LuxembourgRederivation.ApplyAsync(store, rederived, Release1(), CancellationToken.None));
        StringAssert.Contains(again.Message, "never over a re-derived one");
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => LuxembourgRederivation.ApplyAsync(store,
            LuxembourgFirstMountAcquisitionResult.Refused(LuxembourgFirstMountAcquisitionRefusal.RunRefused, "test"), Release1(), CancellationToken.None));
        Assert.ThrowsExactly<ArgumentException>(() => LuxembourgRederivation.RulesOf("lu-release-2"));
    }

    /// <summary>
    /// A signature check, and only that: the re-derivation's surface takes a custody store, an acquisition result and a
    /// request, and none of them carries a transport, a session or a wire budget. It reads types, not method bodies, so it
    /// cannot see code that builds its own client (the review of #973 added one and this stayed green). Zero traffic is
    /// measured instead: <see cref="AReDerivedPopulationBuildsTheFirstMountEndToEnd"/> and
    /// <see cref="InputsThreeRoundTripThroughDeriveAndSeal"/> run R1 and the derive inside a
    /// <see cref="V3NetworkActivityListener"/> and require zero network operations, and the tool's offline commands enter
    /// <see cref="V3OfflineProcess"/> and refuse to finish when their listener counts any
    /// (<see cref="V3FirstMountBuildTests.TheReDerivationStepsRoundTripThroughTheToolWithNoPublisherTraffic"/>).
    /// </summary>
    [TestMethod]
    public void TheReDerivationSurfaceTakesNoTransport()
    {
        var wire = new[]
        {
            typeof(System.Net.Http.HttpMessageHandler), typeof(System.Net.Http.HttpClient), typeof(WireRequestBudget),
        };
        var surface = typeof(LuxembourgRederivation)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .SelectMany(static method => method.GetParameters().Select(static parameter => parameter.ParameterType).Append(method.ReturnType))
            .Concat(typeof(LuxembourgRederivationRequest).GetProperties().Select(static property => property.PropertyType))
            .Concat(typeof(LuxembourgRederivationRecord).GetProperties().Select(static property => property.PropertyType))
            .ToArray();
        Assert.IsTrue(surface.Length > 10);
        foreach (var type in surface)
        {
            Assert.IsFalse(wire.Any(forbidden => forbidden.IsAssignableFrom(type) || type.IsGenericType &&
                type.GetGenericArguments().Any(argument => forbidden.IsAssignableFrom(argument))), type.FullName);
        }

        Assert.IsFalse(typeof(LuxembourgRederivation).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Any(field => wire.Any(forbidden => forbidden.IsAssignableFrom(field.FieldType))));
    }

    /// <summary>
    /// The listener is not vacuous, and sees what a proxy trap cannot: a client built as the code base's own session builds
    /// its handler (<c>UseProxy = false</c>), sending to a stand-in on the loopback interface, is counted as a socket connect
    /// and an HTTP request, and a name resolution is counted too. Nothing leaves this machine.
    /// </summary>
    [TestMethod]
    public async Task TheListenerCountsThePinnedTransportThatAProxyTrapCannotSee()
    {
        var standIn = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        standIn.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)standIn.LocalEndpoint).Port;
            var served = Task.Run(async () =>
            {
                using var client = await standIn.AcceptTcpClientAsync();
                var stream = client.GetStream();
                _ = await stream.ReadAsync(new byte[4096]);
                await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray());
            });
            using var network = new V3NetworkActivityListener();
            Assert.AreEqual(0L, network.Total, network.Describe());
            using (var handler = new System.Net.Http.SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false })
            using (var client = new System.Net.Http.HttpClient(handler))
            using (var response = await client.GetAsync($"http://127.0.0.1:{port}/"))
            {
                Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);
            }

            await served;
            Assert.IsGreaterThanOrEqualTo(1L, network.SocketConnects, network.Describe());
            Assert.IsGreaterThanOrEqualTo(1L, network.HttpRequests, network.Describe());
            _ = await System.Net.Dns.GetHostAddressesAsync("localhost");
            Assert.IsGreaterThanOrEqualTo(1L, network.NameResolutions, network.Describe());
            Assert.AreEqual(network.SocketConnects + network.NameResolutions + network.HttpRequests, network.Total);
        }
        finally
        {
            standIn.Stop();
        }
    }

    /// <summary>
    /// An offline process builds no publisher session: once <see cref="V3OfflineProcess"/> is entered, the session's one
    /// constructor refuses before its transport sends anything, on the test transport here (the production door is never
    /// called by a test). The switch is process-wide, so this class runs alone and the test leaves it as it found it.
    /// </summary>
    [TestMethod]
    public async Task AnOfflineProcessBuildsNoPublisherSession()
    {
        Assert.IsFalse(V3OfflineProcess.IsEntered, "no test leaves the guard entered");
        var handler = new SendCountingHandler();
        try
        {
            V3OfflineProcess.Enter();
            Assert.IsTrue(V3OfflineProcess.IsEntered);
            var refused = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => RoutedHttpAcquisitionSession.StartWithTestTransportAsync(
                EuAcquisitionTestFixture.SourceWitness(), new EuAcquisitionTestFixture.EuInMemoryCustodyStore(), handler,
                TimeProvider.System, WireRequestBudget.OfWireRequests(10), CancellationToken.None));
            StringAssert.Contains(refused.Message, V3OfflineProcess.SwitchName);
            Assert.AreEqual(0, handler.Sends, "refused before the transport sent anything");
        }
        finally
        {
            AppContext.SetSwitch(V3OfflineProcess.SwitchName, false);
            handler.Dispose();
        }

        Assert.IsFalse(V3OfflineProcess.IsEntered);
    }

    /// <summary>
    /// R1 over a resumed acquisition keeps R0's resumption, so the report states the window and both spends: the resumed
    /// run's (<c>WireSpent</c>) and the earlier runs' floor (<c>PreviousWireSpentAtLeast</c>), named apart and never added.
    /// The review of #973 dropped the resumption from R1's result and every test stayed green; this one does not.
    /// </summary>
    [TestMethod]
    public async Task AReDerivationOfAResumedAcquisitionKeepsItsResumptionAndStatesBothSpends()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var verified = await LuxembourgFirstMountAcquisitionTests.ResumedLegislativePopulationAsync(store);
        var resumption = verified.Resumption!;
        Assert.IsGreaterThan(0, resumption.PreviousWireSpentAtLeast, "the earlier run spent requests before it stopped");

        LuxembourgFirstMountAcquisitionResult rederived;
        using (var network = new V3NetworkActivityListener())
        {
            rederived = await LuxembourgRederivation.ApplyAsync(store, verified, Release1(), CancellationToken.None);
            Assert.AreEqual(0L, network.Total, network.Describe());
        }

        Assert.IsNotNull(rederived.Resumption, "R1 keeps R0's resumption");
        Assert.AreEqual(ContractJson.Serialize(resumption), ContractJson.Serialize(rederived.Resumption));
        Assert.AreEqual(verified.CheckpointRef, rederived.CheckpointRef);
        Assert.AreEqual(verified.PopulationScopeManifestRef, rederived.PopulationScopeManifestRef);

        var record = rederived.Rederivation!;
        var block = JsonSerializer.SerializeToNode(V3CorpusMountWriter.Derivation(
            new V3LuxembourgDerivation(record, DerivedAt), rederived.Resumption))!;
        static string Day(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.AreEqual(
            $"Acquired by the Luxembourg acquisition whose catalog is {verified.CheckpointRef!.Sha256}, resumed on "
            + $"{Day(resumption.ResumedAt)} from {resumption.PreviousRuns} earlier run(s) begun on {Day(resumption.PreviousStartedAt)} "
            + $"and catalogued on {Day(resumption.CatalogedAt)}; the resumed run had spent {resumption.WireSpent} publisher "
            + $"request(s) when it catalogued, after at least {resumption.PreviousWireSpentAtLeast} by the earlier run(s); "
            + $"re-derived from that acquisition's custody under rule set lu-release-1 and admission policy {Policy1Sha256}, at "
            + "the derivation time its inputs state (2026-10-17T06:00:00Z); the derivation path holds no publisher session, and "
            + "the offline tool measures its process's network activity and refuses to finish with any.",
            block["statement"]!.GetValue<string>());
    }

    /// <summary>A transport that counts what reaches it and answers nothing useful: the guard must refuse before it.</summary>
    private sealed class SendCountingHandler : System.Net.Http.HttpMessageHandler
    {
        private int _sends;

        internal int Sends => Volatile.Read(ref _sends);

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sends);
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound) { RequestMessage = request });
        }
    }

    /// <summary>
    /// The corpus header names the record and the policy together or neither, and only a registered policy; a corpus built
    /// from re-derived populations without its record cannot be built at all.
    /// </summary>
    [TestMethod]
    public async Task TheCorpusHeaderNamesTheRecordAndThePolicyTogetherAndOnlyARegisteredPolicy()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (europe, verified) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot(), SyntheticConsolidation());
        var rederived = await LuxembourgRederivation.ApplyAsync(store, verified, Release1(), CancellationToken.None);
        var build = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(DerivedAt)).RunAsync(europe, rederived, CancellationToken.None);
        Assert.IsTrue(build.Delivered, build.Detail);
        var canonical = build.Corpus!.CanonicalBytes.ToArray();
        Assert.AreEqual(build.Corpus.ArtifactRef, VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(canonical).ArtifactRef);

        foreach (var (what, mutate) in new (string, Action<JsonObject>)[]
                 {
                     ("the policy without the record", document => document.Remove("luxembourg_rederivation")),
                     ("the record without the policy", document => document.Remove("luxembourg_rights_admission_policy")),
                     ("an unregistered policy", document => document["luxembourg_rights_admission_policy"] = new string('a', 64)),
                     ("a record that is not a digest", document => document["luxembourg_rederivation"] = "record"),
                 })
        {
            var document = JsonNode.Parse(canonical)!.AsObject();
            mutate(document);
            Assert.ThrowsExactly<ArgumentException>(
                () => VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(Encoding.UTF8.GetBytes(document.ToJsonString() + "\n")), what);
        }

        // The members-at-a-time reader holds the header to the same rule.
        var unregistered = JsonNode.Parse(canonical)!.AsObject();
        unregistered["luxembourg_rights_admission_policy"] = new string('a', 64);
        Assert.ThrowsExactly<ArgumentException>(() => LexCorpus6MountView.ParseCanonicalAndVerify(
            Encoding.UTF8.GetBytes(unregistered.ToJsonString() + "\n")));
        Assert.AreEqual(build.Corpus.ArtifactRef, LexCorpus6MountView.ParseCanonicalAndVerify(canonical).ArtifactRef);

        // A re-derived population without its record never reaches a corpus.
        var withoutRecord = LuxembourgFirstMountAcquisitionResult.Success(
            rederived.Run!, rederived.Profile!, rederived.VocabularyEvidenceRef!, rederived.AknInventory!, rederived.AknLegalContent!);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(DerivedAt)).RunAsync(europe, withoutRecord, CancellationToken.None));
    }

    /// <summary>
    /// Inputs /3 in process: written over the acquisition's inputs /1, derived (R0 then R1) into the same files an
    /// in-process build over R1 writes, sealed with the record the derive names, derived again into the same files; and each
    /// way inputs /3 can be wrong refuses before anything is written.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InputsThreeRoundTripThroughDeriveAndSeal(bool populationScope)
    {
        var root = Path.Combine(Path.GetTempPath(), "lex-v3-inputs3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
            var (europe, verified) = await V3FirstMountBuildTests.AcquireAsync(store, V3FirstMountBuildTests.CheckoutRoot(), SyntheticConsolidation());
            var buildTime = new DateTimeOffset(2026, 10, 12, 18, 17, 5, TimeSpan.Zero);
            var seed = EuAxiomWiringHarness.Seed(null).Celex;
            SourceArtifactRef baseInputs;
            if (populationScope)
            {
                // The dry run's shape: inputs /2 over a population scope, whose catalog names its scope manifest.
                using var handler = new LuxembourgFirstMountAcquisitionTests.LuxembourgFamilyHandler(
                    LuxembourgFirstMountAcquisitionTests.PdfBytes(), xmlBody: SyntheticConsolidation());
                verified = await new LuxembourgFirstMountAcquisition(store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler)
                    .RunPopulationAsync(LuxembourgPopulationScope.Legislative,
                        await LuxembourgRendererSources.FromCheckoutAsync(store, V3FirstMountBuildTests.CheckoutRoot(), CancellationToken.None),
                        LuxembourgAcquisitionTestFixture.TestWireBudget(), CancellationToken.None);
                Assert.IsTrue(verified.Delivered, verified.Detail);
                Assert.IsNotNull(verified.PopulationScopeManifestRef);
                baseInputs = await V3OfflineMount.CapturePopulationAsync(store, europe, verified, [seed],
                    LuxembourgPopulationScope.Legislative, buildTime, null, CancellationToken.None);
            }
            else
            {
                baseInputs = await V3OfflineMount.CaptureAsync(store, europe, verified, [seed],
                    LuxembourgFirstMountAcquisitionTests.ActRange, buildTime, null, CancellationToken.None);
            }
            var inputs = await V3OfflineMount.CaptureRederivationAsync(store, baseInputs, LuxembourgRederivation.Release1RuleSet,
                Policy1Sha256, DerivedAt, CancellationToken.None);
            Assert.AreEqual(inputs, await V3OfflineMount.CaptureRederivationAsync(store, baseInputs, LuxembourgRederivation.Release1RuleSet,
                Policy1Sha256, DerivedAt, CancellationToken.None), "the same arguments give the same inputs");

            // The inputs' own shape.
            var inputsBytes = (await CustodyRestore.ReadByDigestCheckedAsync(store, inputs.Sha256, CancellationToken.None)).ToArray();
            using (var document = JsonDocument.Parse(inputsBytes))
            {
                CollectionAssert.AreEqual(
                    new[] { "schema", "base", "rule_set", "rules", "admission_policy", "derived_at", "expected_rederivation" },
                    document.RootElement.EnumerateObject().Select(static property => property.Name).ToArray());
                Assert.AreEqual("lex-v3-offline-mount-inputs/3", document.RootElement.GetProperty("schema").GetString());
                Assert.AreEqual(baseInputs.Sha256, document.RootElement.GetProperty("base").GetProperty("sha256").GetString());
                Assert.AreEqual(JsonValueKind.Null, document.RootElement.GetProperty("expected_rederivation").ValueKind);
            }

            var derived = Path.Combine(root, "derived");
            // The whole derive of inputs /3 (R0's reopen, R1, the build and the write) inside the network measurement.
            V3CorpusMountWrite write;
            using (var network = new V3NetworkActivityListener())
            {
                write = await V3OfflineMount.DeriveAsync(store, inputs, derived, CancellationToken.None);
                Assert.AreEqual(0L, network.Total, "a derive of inputs /3 starts no network operation: " + network.Describe());
            }

            Assert.IsNotNull(write.LuxembourgRederivation);

            // The same files as an in-process build over R1 at the acquisition's build time.
            var rederived = await LuxembourgRederivation.ApplyAsync(store, verified, Release1(), CancellationToken.None);
            Assert.AreEqual(rederived.Rederivation!.Reference, write.LuxembourgRederivation);
            Assert.AreEqual(verified.PopulationScopeManifestRef, rederived.PopulationScopeManifestRef);
            using (var recordDocument = JsonDocument.Parse(rederived.Rederivation.CanonicalBytes))
            {
                var scopeManifest = recordDocument.RootElement.GetProperty("acquisition").GetProperty("scope_manifest_sha256");
                Assert.AreEqual(verified.PopulationScopeManifestRef?.Sha256, scopeManifest.ValueKind == JsonValueKind.Null ? null : scopeManifest.GetString());
            }
            var expectedBuild = await new V3FirstMountBuild(store, new V3OfflineMount.BuildClock(buildTime)).RunAsync(europe, rederived, CancellationToken.None);
            var expected = Path.Combine(root, "expected");
            await V3CorpusMountWriter.WriteAsync(expectedBuild, expected, null, CancellationToken.None, buildTime);
            CollectionAssert.AreEqual(V3FirstMountBuildTests.MountDigests(expected), V3FirstMountBuildTests.MountDigests(derived));

            // Sealed with the record the derive named: the same files again.
            var sealedInputs = await V3OfflineMount.SealRederivationAsync(store, inputs, write.LuxembourgRederivation!.Sha256, CancellationToken.None);
            var sealedDerived = Path.Combine(root, "sealed");
            using (var network = new V3NetworkActivityListener())
            {
                await V3OfflineMount.DeriveAsync(store, sealedInputs, sealedDerived, CancellationToken.None);
                Assert.AreEqual(0L, network.Total, "a derive of sealed inputs /3 starts no network operation: " + network.Describe());
            }
            CollectionAssert.AreEqual(V3FirstMountBuildTests.MountDigests(derived), V3FirstMountBuildTests.MountDigests(sealedDerived));
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
                V3OfflineMount.SealRederivationAsync(store, sealedInputs, write.LuxembourgRederivation.Sha256, CancellationToken.None),
                "sealed inputs are never sealed again");
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
                V3OfflineMount.SealRederivationAsync(store, baseInputs, write.LuxembourgRederivation.Sha256, CancellationToken.None),
                "only inputs /3 are sealed");

            // Sealed with another record: the derive refuses and writes nothing.
            var wrongSeal = await V3OfflineMount.SealRederivationAsync(store, inputs, new string('0', 64), CancellationToken.None);
            var refusedDirectory = Path.Combine(root, "refused-seal");
            var refused = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
                V3OfflineMount.DeriveAsync(store, wrongSeal, refusedDirectory, CancellationToken.None));
            StringAssert.Contains(refused.Message, "not the 0000000000000000000000000000000000000000000000000000000000000000 its sealed inputs expect");
            Assert.IsFalse(Directory.Exists(refusedDirectory) && Directory.EnumerateFileSystemEntries(refusedDirectory).Any());

            // Inputs naming a rule digest this build does not implement refuse before anything is reopened.
            var text = Encoding.UTF8.GetString(inputsBytes);
            foreach (var (what, altered) in new (string, string)[]
                     {
                         ("an unimplemented content digest", text.Replace(Content3RuleSha256, new string('c', 64), StringComparison.Ordinal)),
                         ("the content /2 rule the set named before S3b", text
                             .Replace(Content3RuleSha256, Content2RuleSha256, StringComparison.Ordinal)
                             .Replace("lex-v3-luxembourg-akn-legal-content-profile/3", "lex-v3-luxembourg-akn-legal-content-profile/2", StringComparison.Ordinal)),
                         ("an unimplemented rule set", text.Replace("\"rule_set\":\"lu-release-1\"", "\"rule_set\":\"lu-release-9\"", StringComparison.Ordinal)),
                         ("an unregistered policy", text.Replace(Policy1Sha256, new string('a', 64), StringComparison.Ordinal)),
                         ("a derivation time that is not a whole second", text.Replace("2026-10-17T06:00:00+00:00", "2026-10-17T06:00:00.5+00:00", StringComparison.Ordinal)),
                         ("a derivation time before the acquisition's build time", text.Replace("2026-10-17T06:00:00+00:00", "2026-10-12T18:17:04+00:00", StringComparison.Ordinal)),
                         ("a base that is itself inputs /3", text.Replace(baseInputs.Sha256, inputs.Sha256, StringComparison.Ordinal)),
                     })
            {
                Assert.AreNotEqual(text, altered, what);
                var held = await store.CreateAsync(Encoding.UTF8.GetBytes(altered), CustodyClass.NightlyFloor90d, CancellationToken.None);
                var output = Path.Combine(root, "refused-" + Guid.NewGuid().ToString("N"));
                await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => V3OfflineMount.DeriveAsync(store,
                    new SourceArtifactRef(inputs.ResourceId, held.Reference.ContentSha256), output, CancellationToken.None), what);
                Assert.IsFalse(Directory.Exists(output), what);
            }

            // Capture refuses what a derive would.
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => V3OfflineMount.CaptureRederivationAsync(store, baseInputs,
                "lu-release-9", Policy1Sha256, DerivedAt, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => V3OfflineMount.CaptureRederivationAsync(store, baseInputs,
                LuxembourgRederivation.Release1RuleSet, new string('a', 64), DerivedAt, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => V3OfflineMount.CaptureRederivationAsync(store, baseInputs,
                LuxembourgRederivation.Release1RuleSet, Policy1Sha256, buildTime.AddSeconds(-1), CancellationToken.None));
            await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => V3OfflineMount.CaptureRederivationAsync(store, inputs,
                LuxembourgRederivation.Release1RuleSet, Policy1Sha256, DerivedAt, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string? RecordValue(LuxembourgRederivationRecord record, string part, string name)
    {
        using var document = JsonDocument.Parse(record.CanonicalBytes);
        return document.RootElement.GetProperty(part).GetProperty(name).GetString();
    }
}
