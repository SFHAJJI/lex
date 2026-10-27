using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Api;
using Lex.V3.Ingest.Releases;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The release path's data checks (the audit's gap 6): the release-over-release comparison (R106) over a fixture mount and a
/// changed copy of it, read by the production reader, and over synthetic inventories for every scope a typed reason may have;
/// the explanations file read strictly; R86(2)'s listing of admitted consolidation files without text, typed and explained or
/// failing; and the <c>release-checks</c> command over two fixture mounts, by its exit codes and files.
/// </summary>
[TestClass]
public sealed class V3ReleaseChecksTests
{
    private const string ProfileBefore = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string ProfileAfter = "2222222222222222222222222222222222222222222222222222222222222222";

    [TestMethod]
    public async Task AnUnchangedMountKeepsEverythingAndAChangedCopyListsEachChangeUntilItIsExplained()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddEuropeCollisionAsync();
        var serving = V3MountInventory.Read(fixture.Directory);
        Assert.AreEqual(fixture.CorpusSha256, serving.CorpusSha256);
        Assert.AreEqual("lex-v3-luxembourg-index/9", serving.LuxembourgIndexSchema);
        Assert.IsNotNull(serving.EuropeIndexSchema);
        Assert.IsTrue(serving.Versions.Values.Any(static version => version.Publisher == V3MountInventory.Luxembourg && version.HasText));
        Assert.IsTrue(serving.Versions.Values.Any(static version => version.Publisher == V3MountInventory.Europe && version.HasText));
        var luxembourgState = serving.Versions.Values.Single(version => version.IdentitySha256 == fixture.StateSha256);
        Assert.AreEqual(fixture.PermalinkOf(fixture.StateSha256), luxembourgState.Address, "a Luxembourg version's address is the permalink the API mints");

        // The same mount: everything kept, nothing listed.
        var same = V3ReleaseComparison.Compare(serving, V3MountInventory.Read(fixture.Directory), null);
        Assert.HasCount(0, same.Items);
        Assert.IsFalse(same.Refuses);
        Assert.AreEqual(serving.Articles.Values.Sum(static group => group.Articles.Count), same.Classes.Sum(static row => row.Articles.Kept));
        Assert.AreEqual(0L, same.Classes.Sum(static row => row.Articles.Added + row.Articles.Changed + row.Articles.Lost + row.Versions.Lost + row.Versions.Changed));

        // A copy with one Luxembourg article's text changed, another removed from its state, and one EU article's text changed.
        var copy = Path.Combine(Path.GetTempPath(), $"lex-v3-release-candidate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(copy);
        try
        {
            foreach (var file in Directory.GetFiles(fixture.Directory))
            {
                File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
            }

            string changedKey, lostKey, europeKey;
            using (var connection = Writable(Path.Combine(copy, V3CorpusMount.IndexFileName)))
            {
                var identities = JsonSerializer.Deserialize<string[]>(Scalar(connection,
                    "SELECT article_identities_json FROM states WHERE state_sha256 = $state", ("$state", fixture.StateSha256)))!;
                Assert.IsGreaterThan(2, identities.Length);
                string KeyOf(string identity) => luxembourgState.Key + "|" +
                    Scalar(connection, "SELECT publisher_id FROM articles WHERE article_identity_sha256 = $id", ("$id", identity));
                changedKey = KeyOf(identities[0]);
                lostKey = KeyOf(identities[1]);
                Execute(connection, "UPDATE articles SET searchable_text = searchable_text || ' (changed)' WHERE article_identity_sha256 = $id", ("$id", identities[0]));
                Execute(connection, "DELETE FROM relations WHERE from_ref = $id", ("$id", identities[1]));
                Execute(connection, "DELETE FROM articles WHERE article_identity_sha256 = $id", ("$id", identities[1]));
                Execute(connection, "UPDATE states SET article_identities_json = $ids WHERE state_sha256 = $state",
                    ("$ids", JsonSerializer.Serialize(identities.Where((_, index) => index != 1).ToArray())), ("$state", fixture.StateSha256));
            }

            using (var connection = Writable(Path.Combine(copy, V3CorpusMount.EuropeIndexFileName)))
            {
                var row = Scalar(connection,
                    "SELECT article_identity_sha256 || '|' || publisher_work_id || '|' || language || '|' || wording_date || '|' || package_entry || '|' || publisher_identifier " +
                    "FROM articles ORDER BY article_identity_sha256 LIMIT 1");
                var parts = row.Split('|');
                europeKey = string.Join('|', V3MountInventory.Europe, parts[1], parts[2], parts[3], parts[4], parts[5]);
                Execute(connection, "UPDATE articles SET searchable_text = searchable_text || ' (changed)' WHERE article_identity_sha256 = $id", ("$id", parts[0]));
            }

            var candidate = V3MountInventory.Read(copy);
            var changed = V3ReleaseComparison.Compare(serving, candidate, null);
            CollectionAssert.AreEquivalent(
                new[] { (V3ReleaseComparison.ArticleTextChanged, changedKey), (V3ReleaseComparison.ArticleLost, lostKey), (V3ReleaseComparison.ArticleTextChanged, europeKey) },
                changed.Items.Select(static item => (item.Change, item.Key)).ToArray(),
                "the state's stored digest and the EU wording's article identities are unchanged, so no version's identity is");
            Assert.IsTrue(changed.Items.All(static item => !item.Explained), "the publisher bodies are the same, so the data explains none");
            Assert.IsTrue(changed.Refuses);
            var luxembourgRow = changed.Classes.Single(static counted => counted.Publisher == V3MountInventory.Luxembourg && counted.Articles.Lost > 0);
            Assert.AreEqual((1L, 1L), (luxembourgRow.Articles.Changed, luxembourgRow.Articles.Lost));

            // Named in the release's explanations, each is explained, and an entry that covers nothing is listed as unused.
            var explanations = Explanations(new JsonArray
            {
                Entry(V3ReleaseExplanations.DefectCorrected, V3MountInventory.Luxembourg, items: [changedKey]),
                Entry(V3ReleaseExplanations.WithdrawnByPublisher, V3MountInventory.Luxembourg, items: [lostKey]),
                Entry(V3ReleaseExplanations.DefectCorrected, V3MountInventory.Europe, items: ["eu-eurlex|nothing"]),
                Entry(V3ReleaseExplanations.DefectCorrected, V3MountInventory.Europe, items: [europeKey]),
            });
            var explained = V3ReleaseComparison.Compare(serving, candidate, explanations);
            Assert.IsFalse(explained.Refuses, string.Join("; ", explained.Items.Where(static item => !item.Explained).Select(static item => item.Key)));
            Assert.AreEqual(2, explained.UnusedExplanations.Single().Index);

            // The report: one form, the verdict and every item.
            var mount = new V3ComparedMount("serving", serving.CorpusSha256, serving.LuxembourgIndexSchema, serving.EuropeIndexSchema, []);
            var bytes = changed.Write(mount, mount with { Label = "candidate" }, null);
            CollectionAssert.AreEqual(bytes, changed.Write(mount, mount with { Label = "candidate" }, null));
            using var report = JsonDocument.Parse(bytes);
            Assert.AreEqual(V3ReleaseComparison.SchemaId, report.RootElement.GetProperty("schema").GetString());
            Assert.AreEqual("refuse", report.RootElement.GetProperty("verdict").GetString());
            Assert.AreEqual(changed.Items.Count, report.RootElement.GetProperty("items").GetArrayLength());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(copy, recursive: true);
        }
    }

    [TestMethod]
    public void EachTypedReasonExplainsOnlyWhatBothMountsShowItCovers()
    {
        var serving = Inventory(
            Version("a", "2020-01-01", "s1", profiles: [ProfileBefore], bodies: ["b1"]),
            Version("b", "2020-01-01", "s2", profiles: [ProfileBefore], bodies: ["b2"]),
            Version("c", "2020-01-01", "s3", bodies: ["b3"]),
            Version("d", "2020-01-01", "s4", bodies: ["b4"]),
            Version("e", "2020-01-01", "s5", bodies: ["b5"]),
            Version("f", "2020-01-01", "s6", status: V3MountInventory.NoText, bodies: [], articles: 0));
        var candidate = Inventory(
            Version("a", "2020-01-01", "t1", profiles: [ProfileAfter], bodies: ["b1"]),
            Version("b", "2020-01-01", "t2", profiles: [ProfileAfter], bodies: ["b2x"]),
            Version("c", "2020-01-01", "s3", status: "withheld_rights", reason: "conflict", bodies: [], articles: 0),
            Version("d", "2020-01-01", "s4", status: "withheld_rights", reason: "typed_quarantine_in_file_reading_rejected; content consistency", bodies: [], articles: 0),
            Version("f", "2020-01-01", "t6", bodies: ["b6"]),
            Version("g", "2020-01-01", "s7", bodies: ["b7"]));
        var nothing = V3ReleaseComparison.Compare(serving, candidate, null);
        var byKey = nothing.Items.ToDictionary(static item => item.Key[..(item.Key.IndexOf("|2020", StringComparison.Ordinal))]);
        Assert.AreEqual(V3ReleaseComparison.VersionIdentityChanged, byKey["lu-legilux|a"].Change);
        Assert.IsFalse(byKey["lu-legilux|a"].Explained, "a rule change is a typed reason the release gives, never one assumed");
        Assert.AreEqual(V3ReleaseComparison.PublisherBodyChanged, byKey["lu-legilux|b"].Reason, "the publisher's own file changed");
        Assert.AreEqual("data", byKey["lu-legilux|b"].ReasonSource);
        Assert.AreEqual(V3ReleaseComparison.VersionTextLost, byKey["lu-legilux|c"].Change);
        Assert.AreEqual(V3ReleaseComparison.VersionTextLost, byKey["lu-legilux|d"].Change);
        Assert.AreEqual(V3ReleaseComparison.VersionLost, byKey["lu-legilux|e"].Change);
        Assert.IsFalse(byKey.ContainsKey("lu-legilux|f"), "text a version gains never refuses");
        Assert.IsFalse(byKey.ContainsKey("lu-legilux|g"), "a version added never refuses");
        Assert.AreEqual(4L, nothing.Unexplained);
        var row = nothing.Classes.Single();
        Assert.AreEqual(new V3ReleaseCounts(0, 1, 5, 1), row.Versions);
        Assert.AreEqual(new V3ReleaseCounts(5, 1, 0, 1), row.Works);

        var explanations = Explanations(new JsonArray
        {
            // The wrong way round covers nothing; the right profiles cover a.
            Entry(V3ReleaseExplanations.DerivationRuleChanged, V3MountInventory.Luxembourg, from: ProfileAfter, to: ProfileBefore),
            Entry(V3ReleaseExplanations.DerivationRuleChanged, V3MountInventory.Luxembourg, from: ProfileBefore, to: ProfileAfter),
            // Only d is withheld under this disposition; c is a conflict.
            Entry(V3ReleaseExplanations.RightsWithdrawn, V3MountInventory.Luxembourg, dispositions: ["typed_quarantine_in_file_reading_rejected"]),
            // An item named for the other publisher covers nothing.
            Entry(V3ReleaseExplanations.WithdrawnByPublisher, V3MountInventory.Europe, items: [byKey["lu-legilux|e"].Key]),
        });
        var explained = V3ReleaseComparison.Compare(serving, candidate, explanations);
        var reasons = explained.Items.ToDictionary(static item => item.Key[..(item.Key.IndexOf("|2020", StringComparison.Ordinal))], static item => (item.Reason, item.Entry));
        Assert.AreEqual((V3ReleaseExplanations.DerivationRuleChanged, (int?)1), reasons["lu-legilux|a"]);
        Assert.AreEqual((V3ReleaseExplanations.RightsWithdrawn, (int?)2), reasons["lu-legilux|d"]);
        Assert.AreEqual(((string?)null, (int?)null), reasons["lu-legilux|c"]);
        Assert.AreEqual(((string?)null, (int?)null), reasons["lu-legilux|e"]);
        CollectionAssert.AreEqual(new[] { 0, 3 }, explained.UnusedExplanations.Select(static entry => entry.Index).ToArray());
        Assert.AreEqual(2L, explained.Unexplained);
    }

    [TestMethod]
    public void ATextlessVersionReadUnderAnotherCoordinateIsNoLossButOneGoneIs()
    {
        // An EU census version listed without text (by its Cellar work) whose wording the candidate now reads under its language
        // and date: the same publisher identity, so nothing served is lost. A textless version gone with its identity is a loss.
        var listed = Version("a", "2020-01-01", "s1", status: V3MountInventory.NoText, bodies: [], articles: 0);
        var read = listed with { Key = listed.Key.Replace("|fra", "|eng", StringComparison.Ordinal), TextStatus = V3MountInventory.Held, BodySha256s = ["b1"], Articles = 3 };
        var gone = Version("b", "2020-01-01", "s2", status: V3MountInventory.NoText, bodies: [], articles: 0);
        var compared = V3ReleaseComparison.Compare(Inventory(listed, gone), Inventory(read), null);
        Assert.AreEqual(V3ReleaseComparison.VersionLost, compared.Items.Single().Change);
        StringAssert.Contains(compared.Items.Single().Key, "|b|");
        Assert.AreEqual(new V3ReleaseCounts(0, 1, 1, 1), compared.Classes.Single().Versions);
    }

    [TestMethod]
    public void ArticlesAreComparedByCoordinateAndTheirTextDigestsAsAMultiset()
    {
        var version = Version("a", "2020-01-01", "s1", profiles: [ProfileBefore], bodies: ["b1"], articles: 3);
        var after = version with { IdentitySha256 = "t1", RuleProfiles = [ProfileAfter], Address = "/lu-legilux/a/2020-01-01--t1/fra" };
        var serving = Inventory([version], Article(version, "1", ("x", "b1", ProfileBefore)), Article(version, "2", ("y", "b1", ProfileBefore), ("z", "b1", ProfileBefore)), Article(version, "3", ("w", "b1", ProfileBefore)));
        var candidate = Inventory([after], Article(after, "1", ("x", "b1", ProfileAfter)), Article(after, "2", ("z", "b1", ProfileAfter), ("v", "b1", ProfileAfter)), Article(after, "4", ("u", "b1", ProfileAfter)));
        var compared = V3ReleaseComparison.Compare(serving, candidate, null);
        var row = compared.Classes.Single();
        Assert.AreEqual(new V3ReleaseCounts(2, 1, 1, 1), row.Articles, "x and z kept, y changed to v, w lost, u added");
        CollectionAssert.AreEquivalent(
            new[] { V3ReleaseComparison.ArticleTextChanged, V3ReleaseComparison.ArticleLost, V3ReleaseComparison.VersionIdentityChanged },
            compared.Items.Select(static item => item.Change).ToArray());
        var textChanged = compared.Items.Single(static item => item.Change == V3ReleaseComparison.ArticleTextChanged);
        Assert.AreEqual("y", textChanged.Serving["text_sha256"]);
        Assert.AreEqual("v", textChanged.Candidate["text_sha256"]);

        // A rule change explains the changed text and the version's identity, never the article lost.
        var explained = V3ReleaseComparison.Compare(serving, candidate, Explanations(new JsonArray
        {
            Entry(V3ReleaseExplanations.DerivationRuleChanged, V3MountInventory.Luxembourg, from: ProfileBefore, to: ProfileAfter),
        }));
        Assert.AreEqual(V3ReleaseComparison.ArticleLost, explained.Items.Single(static item => !item.Explained).Change);

        // A drop beyond ten per cent is named.
        var emptied = Inventory([after]);
        StringAssert.Contains(string.Join(";", V3ReleaseComparison.Compare(serving, emptied, null).Drops), "articles served 4 to 0");
    }

    [TestMethod]
    public void ARightsEntryExplainsOnlyAWholeDispositionAndAnArticleLostFromAFileStillReadIsNeverTheDatas()
    {
        // The review of #1002, F2: "conflict" explains a version withheld as a conflict, with or without the reason's detail, and
        // never one whose reason merely starts with the same letters.
        var serving = Inventory(
            Version("a", "2020-01-01", "s1", bodies: ["b1"]),
            Version("b", "2020-01-01", "s2", bodies: ["b2"]),
            Version("c", "2020-01-01", "s3", bodies: ["b3"]));
        var candidate = Inventory(
            Version("a", "2020-01-01", "s1", status: "withheld_rights", reason: "conflict", bodies: [], articles: 0),
            Version("b", "2020-01-01", "s2", status: "withheld_rights", reason: "conflict; content consistency", bodies: [], articles: 0),
            Version("c", "2020-01-01", "s3", status: "withheld_rights", reason: "conflicting_detail", bodies: [], articles: 0));
        var explained = V3ReleaseComparison.Compare(serving, candidate, Explanations(new JsonArray
        {
            Entry(V3ReleaseExplanations.RightsWithdrawn, V3MountInventory.Luxembourg, dispositions: ["conflict"]),
        }));
        var byWork = explained.Items.ToDictionary(static item => item.Key[..item.Key.IndexOf("|2020", StringComparison.Ordinal)], static item => item.Explained);
        Assert.IsTrue(byWork["lu-legilux|a"]);
        Assert.IsTrue(byWork["lu-legilux|b"]);
        Assert.IsFalse(byWork["lu-legilux|c"], "a reason that only starts with the disposition's letters is another reason");

        // F1: the version now also reads b2, but y is gone from b1, which it still reads: the data does not explain that loss.
        var version = Version("d", "2020-01-01", "s4", bodies: ["b1"], articles: 2);
        var widened = version with { BodySha256s = ["b1", "b2"] };
        var lost = V3ReleaseComparison.Compare(
                Inventory([version], Article(version, "1", ("x", "b1", ProfileBefore)), Article(version, "2", ("y", "b1", ProfileBefore))),
                Inventory([widened], Article(widened, "1", ("x", "b1", ProfileBefore)), Article(widened, "3", ("w", "b2", ProfileBefore))),
                null)
            .Items.Single(static item => item.Change == V3ReleaseComparison.ArticleLost);
        Assert.IsFalse(lost.Explained, "an article lost from a file the candidate still reads is never the publisher's change");

        // An article lost with the file it was read from is the data's.
        var moved = version with { BodySha256s = ["b2"] };
        var gone = V3ReleaseComparison.Compare(
                Inventory([version], Article(version, "1", ("x", "b1", ProfileBefore)), Article(version, "2", ("y", "b1", ProfileBefore))),
                Inventory([moved], Article(moved, "1", ("x", "b2", ProfileBefore))),
                null)
            .Items.Single(static item => item.Change == V3ReleaseComparison.ArticleLost);
        Assert.AreEqual((V3ReleaseComparison.PublisherBodyChanged, "data"), (gone.Reason, gone.ReasonSource));
    }

    [TestMethod]
    public void TheExplanationsFileIsReadStrictly()
    {
        var good = Explanations(new JsonArray
        {
            Entry(V3ReleaseExplanations.DerivationRuleChanged, V3MountInventory.Luxembourg, from: ProfileBefore, to: ProfileAfter),
            Entry(V3ReleaseExplanations.RightsWithdrawn, V3MountInventory.Luxembourg, dispositions: ["conflict"]),
            Entry(V3ReleaseExplanations.DefectCorrected, V3MountInventory.Europe, items: ["eu-eurlex|x"]),
        }, new JsonArray { Textless(V3MountInventory.Europe, new string('a', 64), "formex_main_body_package_rejected") });
        Assert.AreEqual("v3-20261026T180319Z-8958ec5fa63f", good.ServingRelease);
        Assert.HasCount(3, good.Changes);
        Assert.HasCount(1, good.Textless);
        Assert.AreEqual(64, good.Sha256.Length);

        var broken = new (string What, Action<JsonObject> Break)[]
        {
            ("another schema", static root => root["schema"] = "lex-v3-release-explanations/2"),
            ("an unknown member", static root => root["note"] = "x"),
            ("no serving release", static root => root.Remove("serving_release")),
            ("an unknown reason", static root => root["changes"]![0]!["reason"] = "publisher_body_changed"),
            ("an unknown publisher", static root => root["changes"]![2]!["publisher"] = "de-bgbl"),
            ("no explanation", static root => root["changes"]![2]!.AsObject().Remove("explanation")),
            ("no reference", static root => root["changes"]![2]!["reference"] = " "),
            ("two scopes", static root => root["changes"]![2]!["dispositions"] = new JsonArray("conflict")),
            ("no scope", static root => root["changes"]![2]!.AsObject().Remove("items")),
            ("profiles on the EU", static root => root["changes"]![0]!["publisher"] = V3MountInventory.Europe),
            ("one profile", static root => root["changes"]![0]!.AsObject().Remove("to_rule_profile_sha256")),
            ("the same profile twice", static root => root["changes"]![0]!["to_rule_profile_sha256"] = ProfileBefore),
            ("a profile that is no digest", static root => root["changes"]![0]!["from_rule_profile_sha256"] = "ABC"),
            ("dispositions for another reason", static root => root["changes"]![1]!["reason"] = V3ReleaseExplanations.DefectCorrected),
            ("an item twice", static root => root["changes"]![2]!["items"] = new JsonArray("eu-eurlex|x", "eu-eurlex|x")),
            // The review of #1002, F2: a disposition is a whole rights token, never a fragment of one.
            ("a disposition that is a fragment", static root => root["changes"]![1]!["dispositions"] = new JsonArray("t")),
            ("a disposition no rights rule names", static root => root["changes"]![1]!["dispositions"] = new JsonArray("conflicting")),
            ("a textless entry with no object", static root => root["textless"]![0]!.AsObject().Remove("object_ref_sha256")),
            ("a textless entry with no outcome", static root => root["textless"]![0]!["outcome"] = ""),
        };
        foreach (var (what, breakIt) in broken)
        {
            var root = JsonNode.Parse(Encoding.UTF8.GetString(ExplanationsBytes(new JsonArray
            {
                Entry(V3ReleaseExplanations.DerivationRuleChanged, V3MountInventory.Luxembourg, from: ProfileBefore, to: ProfileAfter),
                Entry(V3ReleaseExplanations.RightsWithdrawn, V3MountInventory.Luxembourg, dispositions: ["conflict"]),
                Entry(V3ReleaseExplanations.DefectCorrected, V3MountInventory.Europe, items: ["eu-eurlex|x"]),
            }, new JsonArray { Textless(V3MountInventory.Europe, new string('a', 64), "formex_main_body_package_rejected") })))!.AsObject();
            breakIt(root);
            Assert.ThrowsExactly<InvalidDataException>(() => V3ReleaseExplanations.Read(Encoding.UTF8.GetBytes(root.ToJsonString())), what);
        }

        Assert.ThrowsExactly<InvalidDataException>(() => V3ReleaseExplanations.Read("not json"u8.ToArray()));
    }

    [TestMethod]
    public void EveryAdmittedConsolidationFileWithoutTextIsListedTypedOrFailing()
    {
        const string Eli = "http://data.legilux.public.lu/eli/etat/leg/";
        var misfiledState = Version("code-civil", "2019-04-28", "m1", status: "not_available_misfiled", reason: "content consistency: shared_file", bodies: [], articles: 0)
            with { Identity = Eli + "code/civil/20190428", Family = "code", Kind = "code" };
        var files = new[]
        {
            HeldFile(V3MountInventory.Luxembourg, "loi", "consolidation", Eli + "loi/2020/01/01/a1/consolide/20210101", "xml", 12, "akn_admitted"),
            HeldFile(V3MountInventory.Luxembourg, "loi", "consolidation", Eli + "loi/2020/01/01/a1/consolide/20220101", "xml", 0, "akn_unsupported_content_shape", "pdf_not_applicable"),
            HeldFile(V3MountInventory.Luxembourg, "loi", "consolidation", Eli + "loi/2020/01/01/a1/consolide/20230101", "xml", 0, "akn_marker_only_evidence", "pdf_not_applicable"),
            HeldFile(V3MountInventory.Luxembourg, "loi", "consolidation", Eli + "loi/2020/01/01/a1/consolide/20240101", "pdf", 0, "pdf_upstream_text_layer_gap"),
            HeldFile(V3MountInventory.Luxembourg, "loi", "consolidation", Eli + "loi/2020/01/01/a1/consolide/20250101", "pdf", 0, "pdf_not_applicable"),
            HeldFile(V3MountInventory.Luxembourg, "code", "code", Eli + "code/civil/20190428", "xml", 0, "akn_upstream_not_inventoried", "pdf_not_applicable"),
            HeldFile(V3MountInventory.Luxembourg, "loi", "as_published", Eli + "loi/2020/01/01/a1/jo", "xml", 0, "akn_unsupported_content_shape"),
            HeldFile(V3MountInventory.Luxembourg, "rgd", "consolidation", Eli + "rgd/2020/01/01/a1/consolide/20210101", "xml", 0, "akn_admitted") with { Outcome = "rights_withheld" },
            HeldFile(V3MountInventory.Europe, "eu", "consolidation", null, null, 0, "formex_main_body_not_eligible"),
            HeldFile(V3MountInventory.Europe, "eu", "consolidation", null, null, 0, "formex_main_body_package_rejected"),
            HeldFile(V3MountInventory.Europe, "eu", "consolidation", null, null, 0, "formex_main_body_missing"),
            HeldFile(V3MountInventory.Europe, "eu", "consolidation", null, null, 0, "formex_main_body_package_not_acquired"),
            HeldFile(V3MountInventory.Europe, "eu", "consolidation", null, null, 3, "formex_main_body_admitted"),
            HeldFile(V3MountInventory.Europe, "eu", "original_legal_text", null, null, 0, "formex_main_body_missing"),
        };
        var inventory = new V3MountInventory(new string('c', 64), "lex-v3-luxembourg-index/9", "lex-v3-europe-index/6",
            new Dictionary<string, V3ServedVersion> { [misfiledState.Key] = misfiledState }, new Dictionary<string, V3ServedArticles>(), files);

        var measured = V3ConsolidationText.Measure(inventory, null);
        var classified = measured.Listed.Select(static item => (item.Identity ?? item.Outcome, item.Classification)).ToArray();
        CollectionAssert.AreEquivalent(new[]
        {
            (Eli + "loi/2020/01/01/a1/consolide/20220101", V3ConsolidationText.Failure),
            (Eli + "loi/2020/01/01/a1/consolide/20230101", V3ConsolidationText.TypedTextlessMarkerOnly),
            (Eli + "loi/2020/01/01/a1/consolide/20240101", V3ConsolidationText.PdfTextLayerProfile),
            (Eli + "loi/2020/01/01/a1/consolide/20250101", V3ConsolidationText.Failure),
            (Eli + "code/civil/20190428", V3ConsolidationText.TypedTextlessMisfiled),
            ("formex_main_body_not_eligible", V3ConsolidationText.TypedTextlessNoXmlMainBody),
            ("formex_main_body_package_rejected", V3ConsolidationText.Failure),
            ("formex_main_body_missing", V3ConsolidationText.Failure),
            ("formex_main_body_package_not_acquired", V3ConsolidationText.TypedTextlessNoPackageHeld),
        }, classified, "as-published files, original wordings, withheld files and files with text are not listed");
        Assert.AreEqual("akn_unsupported_content_shape+pdf_not_applicable", measured.Listed.Single(static item => item.Identity?.EndsWith("20220101", StringComparison.Ordinal) == true).Outcome);
        Assert.AreEqual(4L, measured.Unexplained);
        Assert.IsTrue(measured.Refuses);
        var lu = measured.Counts.Single(static count => count.Publisher == V3MountInventory.Luxembourg);
        Assert.AreEqual((6L, 1L, 5L, 2L), (lu.Admitted, lu.WithText, lu.Listed, lu.Unexplained));

        // A textless entry explains its file only with exactly the outcome the data carries.
        var rejected = files[9];
        var missing = files[10];
        var explained = V3ConsolidationText.Measure(inventory, Explanations(new JsonArray(), new JsonArray
        {
            Textless(V3MountInventory.Europe, rejected.ObjectRefSha256, "formex_main_body_package_rejected"),
            Textless(V3MountInventory.Europe, missing.ObjectRefSha256, "formex_main_body_package_rejected"),
        }));
        Assert.AreEqual(0, explained.Listed.Single(item => item.ObjectRefSha256 == rejected.ObjectRefSha256).Entry);
        Assert.IsNull(explained.Listed.Single(item => item.ObjectRefSha256 == missing.ObjectRefSha256).Entry);
        Assert.AreEqual(1, explained.UnusedExplanations.Single().Index);
        Assert.AreEqual(3L, explained.Unexplained);

        using var report = JsonDocument.Parse(explained.Write(new V3ComparedMount("candidate", inventory.CorpusSha256, null, null, []), null));
        Assert.AreEqual(V3ConsolidationText.SchemaId, report.RootElement.GetProperty("schema").GetString());
        Assert.AreEqual(9, report.RootElement.GetProperty("listed").GetArrayLength());
        Assert.AreEqual(3, report.RootElement.GetProperty("listed").EnumerateArray().Count(static item => item.GetProperty("blocking").GetBoolean()));
    }

    [TestMethod]
    public async Task TheCommandWritesBothReportsAndExitsByTheirVerdicts()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var root = Path.Combine(Path.GetTempPath(), $"lex-v3-release-checks-{Guid.NewGuid():N}");
        var candidate = Path.Combine(root, "candidate");
        Directory.CreateDirectory(candidate);
        try
        {
            foreach (var file in Directory.GetFiles(fixture.Directory))
            {
                File.Copy(file, Path.Combine(candidate, Path.GetFileName(file)));
            }

            var report = Path.Combine(root, "mount-report.json");
            await File.WriteAllTextAsync(report, MountReport(fixture.Directory));
            string changedState;
            using (var connection = Writable(Path.Combine(candidate, V3CorpusMount.IndexFileName)))
            {
                changedState = Scalar(connection, "SELECT state_sha256 FROM states WHERE text_status = 'held' LIMIT 1");
                Execute(connection, "UPDATE states SET state_sha256 = $new WHERE state_sha256 = $old", ("$new", new string('9', 64)), ("$old", changedState));
            }

            SqliteConnection.ClearAllPools();
            var (exit, stdout, _) = await RunAsync("release-checks", "--mount", candidate, "--out", Path.Combine(root, "out1"),
                "--serving-mount", fixture.Directory, "--serving-report", report);
            Assert.AreEqual(4, exit, stdout);
            StringAssert.Contains(stdout, "release-comparison.json verdict=refuse listed=1 unexplained=1");
            using (var written = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "out1", V3ReleaseComparison.FileName))))
            {
                var item = written.RootElement.GetProperty("items")[0];
                Assert.AreEqual(V3ReleaseComparison.VersionIdentityChanged, item.GetProperty("change").GetString());
                Assert.AreEqual(changedState, item.GetProperty("serving").GetProperty("identity_sha256").GetString());
                Assert.AreEqual(3, written.RootElement.GetProperty("serving").GetProperty("files_verified").GetArrayLength(),
                    "the serving mount's three files, held to its report");
            }

            Assert.IsTrue(File.Exists(Path.Combine(root, "out1", V3ConsolidationText.FileName)));

            // Explained in the release's explanations, the same candidate passes.
            var explanations = Path.Combine(root, "explanations.json");
            using (var written = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "out1", V3ReleaseComparison.FileName))))
            {
                var key = written.RootElement.GetProperty("items")[0].GetProperty("key").GetString()!;
                await File.WriteAllBytesAsync(explanations, ExplanationsBytes(new JsonArray
                {
                    Entry(V3ReleaseExplanations.DefectCorrected, V3MountInventory.Luxembourg, items: [key]),
                }));
            }

            (exit, stdout, _) = await RunAsync("release-checks", "--mount", candidate, "--out", Path.Combine(root, "out2"),
                "--serving-mount", fixture.Directory, "--serving-report", report, "--explanations", explanations);
            Assert.AreEqual(0, exit, stdout);
            StringAssert.Contains(stdout, "release-comparison.json verdict=pass listed=1 unexplained=0");

            // A serving mount that is not the one its release's report records is refused before anything is compared.
            await File.WriteAllTextAsync(report, MountReport(candidate));
            string stderr;
            (exit, _, stderr) = await RunAsync("release-checks", "--mount", candidate, "--out", Path.Combine(root, "out3"),
                "--serving-mount", fixture.Directory, "--serving-report", report);
            Assert.AreEqual(3, exit, stderr);
            StringAssert.Contains(stderr, "the serving mount is not the one the serving release's mount report records");
            Assert.IsFalse(File.Exists(Path.Combine(root, "out3", V3ReleaseComparison.FileName)));

            // Usage: the serving mount and its report go together, and the output is never inside a mount.
            (exit, _, _) = await RunAsync("release-checks", "--mount", candidate, "--out", Path.Combine(root, "out4"), "--serving-mount", fixture.Directory);
            Assert.AreEqual(2, exit);
            (exit, _, _) = await RunAsync("release-checks", "--mount", candidate, "--out", Path.Combine(candidate, "out"));
            Assert.AreEqual(2, exit);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    // ----- helpers -----

    private static V3ServedVersion Version(
        string work, string date, string sha, string status = V3MountInventory.Held, string? reason = null,
        string[]? profiles = null, string[]? bodies = null, long articles = 1) =>
        new(V3MountInventory.Luxembourg, $"lu-legilux|{work}|{date}|http://data.legilux.public.lu/eli/etat/leg/loi/{work}/jo/fr|fra", work,
            $"http://data.legilux.public.lu/eli/etat/leg/loi/{work}/jo", "loi", "as_published", $"/lu-legilux/{work}/{date}--{sha}/fra",
            sha, status, reason, profiles ?? [], bodies ?? ["b"], articles);

    private static KeyValuePair<string, V3ServedArticles> Article(V3ServedVersion version, string id, params (string Text, string Body, string Profile)[] texts) =>
        KeyValuePair.Create(version.Key + "|" + id, new V3ServedArticles(version.Key, id,
            texts.Select(static text => new V3ServedArticle(text.Text, text.Body, text.Profile)).ToArray()));

    private static V3MountInventory Inventory(params V3ServedVersion[] versions) => Inventory(versions, []);

    private static V3MountInventory Inventory(V3ServedVersion[] versions, params KeyValuePair<string, V3ServedArticles>[] articles) =>
        new(new string('c', 64), "lex-v3-luxembourg-index/9", null,
            versions.ToDictionary(static version => version.Key, StringComparer.Ordinal),
            articles.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal), []);

    private static int _files;

    private static V3HeldFile HeldFile(string publisher, string family, string kind, string? identity, string? format, long served, params string[] dispositions)
    {
        var objectRef = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("file" + _files++)));
        return new V3HeldFile(publisher, objectRef, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("body" + objectRef))), "acquired",
            family, kind, identity, format, dispositions.Order(StringComparer.Ordinal).ToArray(), [], served);
    }

    private static JsonObject Entry(string reason, string publisher, string[]? items = null, string? from = null, string? to = null, string[]? dispositions = null)
    {
        var entry = new JsonObject
        {
            ["reason"] = reason,
            ["publisher"] = publisher,
            ["explanation"] = "a reason a person reads",
            ["reference"] = "#1002",
        };
        if (items is not null) entry["items"] = new JsonArray(items.Select(static item => (JsonNode?)item).ToArray());
        if (from is not null) entry["from_rule_profile_sha256"] = from;
        if (to is not null) entry["to_rule_profile_sha256"] = to;
        if (dispositions is not null) entry["dispositions"] = new JsonArray(dispositions.Select(static item => (JsonNode?)item).ToArray());
        return entry;
    }

    private static JsonObject Textless(string publisher, string objectRef, string outcome) => new()
    {
        ["publisher"] = publisher,
        ["object_ref_sha256"] = objectRef,
        ["outcome"] = outcome,
        ["explanation"] = "the package's inventory finds no document unit",
        ["reference"] = "STATUS-DATA.md",
    };

    private static byte[] ExplanationsBytes(JsonArray changes, JsonArray? textless = null) => Encoding.UTF8.GetBytes(new JsonObject
    {
        ["schema"] = V3ReleaseExplanations.SchemaId,
        ["serving_release"] = "v3-20261026T180319Z-8958ec5fa63f",
        ["changes"] = changes,
        ["textless"] = textless ?? [],
    }.ToJsonString());

    private static V3ReleaseExplanations Explanations(JsonArray changes, JsonArray? textless = null) =>
        V3ReleaseExplanations.Read(ExplanationsBytes(changes, textless));

    private static string MountReport(string directory) => new JsonObject
    {
        ["files"] = new JsonArray(Directory.GetFiles(directory).Order(StringComparer.Ordinal).Select(static path => (JsonNode?)new JsonObject
        {
            ["Name"] = Path.GetFileName(path),
            ["ByteLength"] = new FileInfo(path).Length,
            ["Sha256"] = Convert.ToHexStringLower(SHA256.HashData(System.IO.File.ReadAllBytes(path))),
        }).ToArray()),
    }.ToJsonString();

    private static SqliteConnection Writable(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static string Scalar(SqliteConnection connection, string sql, params (string Name, string Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return (string)command.ExecuteScalar()!;
    }

    private static void Execute(SqliteConnection connection, string sql, params (string Name, string Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> RunAsync(params string[] arguments)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        var tool = Path.Combine(directory!.FullName, "src", "Lex.V3.Tool", "bin", configuration, "net10.0", "Lex.V3.Tool.dll");
        Assert.IsTrue(System.IO.File.Exists(tool), tool);
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(tool);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await stdout, await stderr);
    }
}
