using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// <c>dossier</c> for an EU work: the expressions the EU index holds, the Formex act date of each held
/// wording (never an applicability date), the article count and the members, with EU context and fixed
/// words for what is not held; every refusal typed with EU context.
/// </summary>
[TestClass]
public sealed class V3CorpusEuropeDossierMountTests
{
    private const string Celex = "32016R0679";

    private static string RawTarget => V3RestRouteBinding.Dossier.RawTarget;

    [TestMethod]
    public async Task AnEuWorksDossierListsItsHeldExpressionWithTheWordingDateArticleCountAndMembers()
    {
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        string? first = null;
        foreach (var identifier in new[]
                 {
                     Celex, fixture.PublisherWorkId, fixture.PublisherExpressionId, fixture.ArticleIdentitySha256,
                 })
        {
            var envelope = await EnvelopeAsync(mount, RawTarget, "dossier", new { identifier });
            Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, $"{identifier}: {envelope.Refusal?.Code}");
            Assert.AreEqual("work_record", envelope.Result!.ObjectType);
            Assert.AreEqual(PublisherId.EuEurLex, envelope.Context.Publisher);
            Assert.AreEqual("eu", envelope.Context.Jurisdiction);
            var body = envelope.Result.Value;
            Assert.AreEqual("eu-eurlex", body.GetProperty("publisher").GetString());
            Assert.AreEqual(fixture.PublisherWorkId, body.GetProperty("publisher_work_id").GetString());
            Assert.AreEqual(Celex, body.GetProperty("celex").GetString());
            Assert.AreEqual(fixture.IndexSha256, body.GetProperty("index_sha256").GetString());
            CollectionAssert.AreEqual(new[] { "eng" }, Strings(body.GetProperty("available_languages")));
            var expression = body.GetProperty("expressions").EnumerateArray().Single();
            Assert.AreEqual(fixture.PublisherExpressionId, expression.GetProperty("publisher_expression_id").GetString());
            CollectionAssert.AreEqual(new[] { "2016-04-27" }, Strings(expression.GetProperty("wording_dates")));
            Assert.AreEqual(99, expression.GetProperty("article_count").GetInt64(), "the GDPR's 99 articles.");
            var member = expression.GetProperty("members").EnumerateArray().Single();
            Assert.AreEqual(64, member.GetProperty("object_ref_sha256").GetString()!.Length);
            Assert.AreEqual("acquired", member.GetProperty("outcome").GetString());
            Assert.IsFalse(body.GetProperty("consolidations_held").GetBoolean());
            StringAssert.Contains(body.GetProperty("date_semantics").GetString(), "never merged with a Luxembourg applicability date");
            CollectionAssert.AreEqual(
                new[] { "titles", "later_wordings", "force_dates", "document_type", "corrigenda", "other_languages" },
                body.GetProperty("not_held").EnumerateArray().Select(static row => row.GetProperty("item").GetString()).ToArray());
            Assert.IsFalse(ContainsProperty(body, "applicability_date"), "an EU record never carries a Luxembourg applicability date.");

            var resolved = await EnvelopeAsync(mount, V3RestRouteBinding.Resolve.RawTarget, "resolve",
                new { identifier = expression.GetProperty("resolve").GetProperty("identifier").GetString() });
            Assert.IsNull(resolved.Refusal, resolved.Refusal?.Code);

            // The expression's one held wording, pinned as EU search pins it (the EU permalink grammar), and the
            // permalink verifies: a dossier the live screen shows cites nothing unpinned.
            var pinned = expression.GetProperty("pinned_wording");
            Assert.AreEqual("2016-04-27", pinned.GetProperty("wording_date").GetString());
            var permalink = pinned.GetProperty("permalink").GetString()!;
            Assert.AreEqual($"/eu-eurlex/{Celex}/eng/2016-04-27--{pinned.GetProperty("wording_sha256").GetString()}", permalink);
            StringAssert.StartsWith(body.GetProperty("digest_rule").GetString(), "the SHA-256, under the domain lex-v3-eu-wording/1");
            var verified = await EnvelopeAsync(mount, "/api/v3/verify", "verify", new { identifier = permalink });
            Assert.AreEqual("digest_matches", verified.Result?.Value.GetProperty("verdict").GetString(), verified.Refusal?.Code);
            var searched = await EnvelopeAsync(mount, "/api/v3/search", "search", new { query = "personal data", language = "eng", identifier = Celex, limit = 1 });
            Assert.AreEqual(searched.Result!.Value.GetProperty("pinned_wording").GetProperty("permalink").GetString(), permalink,
                "the dossier pins the very wording EU search pins");

            // Every identifier of the one work answers the same record, apart from what it echoes.
            var node = System.Text.Json.Nodes.JsonNode.Parse(body.GetRawText())!.AsObject();
            Assert.AreEqual(identifier, node["requested_identifier"]!.GetValue<string>());
            node.Remove("requested_identifier");
            var normalised = node.ToJsonString();
            first ??= normalised;
            Assert.AreEqual(first, normalised, identifier);
        }
    }

    [TestMethod]
    public async Task EuDossierRefusalsAreTypedWithEuContext()
    {
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var french = await EnvelopeAsync(mount, RawTarget, "dossier", new { identifier = Celex, language = "fra" });
        Assert.AreEqual("language_not_available", french.Refusal?.Code);
        CollectionAssert.AreEqual(new[] { "eng" }, Strings(french.Refusal!.HelpfulPayload.GetProperty("available_languages")));
        Assert.AreEqual(PublisherId.EuEurLex, french.Context.Publisher);

        var english = await EnvelopeAsync(mount, RawTarget, "dossier", new { identifier = Celex, language = "eng" });
        Assert.AreEqual("eng", english.Result!.Value.GetProperty("requested_language").GetString());
        Assert.AreEqual(1, english.Result.Value.GetProperty("expression_count").GetInt32());

        var unknown = await EnvelopeAsync(mount, RawTarget, "dossier", new { identifier = "32099R9999" });
        Assert.AreEqual("identifier_unknown", unknown.Refusal?.Code);
        Assert.AreEqual(PublisherId.EuEurLex, unknown.Context.Publisher);

        // A hand-edited index where one CELEX names two works (the builder ties each work root to one CELEX
        // seed, so only a fixture reaches this): the dossier names both and picks neither (review of #762).
        await fixture.AddSecondExpressionWithSamePublisherProvisionIdentifierAsync(celex: Celex);
        using var twoWorks = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(twoWorks);
        var byCelex = await EnvelopeAsync(twoWorks, RawTarget, "dossier", new { identifier = Celex });
        Assert.AreEqual("ambiguous_identifier", byCelex.Refusal?.Code, "one CELEX naming two works is never answered with the first.");
        Assert.AreEqual(PublisherId.EuEurLex, byCelex.Context.Publisher);
        CollectionAssert.AreEquivalent(
            new[] { fixture.PublisherWorkId, "http://publications.europa.eu/resource/celex/32026R1965" },
            Strings(byCelex.Refusal!.HelpfulPayload.GetProperty("candidates")));
        var byWork = await EnvelopeAsync(twoWorks, RawTarget, "dossier", new { identifier = fixture.PublisherWorkId });
        Assert.IsNull(byWork.Refusal, "the work IRI still names one work.");
    }

    [TestMethod]
    public async Task AnIdentifierBothIndexesHoldIsAmbiguousAndALuxembourgDossierIsUnchanged()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.BindPublisherWorkIdentifierAsync();
        var europeExpression = await fixture.AddEuropeCollisionAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var collision = await EnvelopeAsync(mount, RawTarget, "dossier", new { identifier = fixture.PublisherWid });
        Assert.AreEqual("ambiguous_identifier", collision.Refusal?.Code);
        CollectionAssert.Contains(Strings(collision.Refusal!.HelpfulPayload.GetProperty("candidates")), europeExpression);

        var luxembourg = await EnvelopeAsync(mount, RawTarget, "dossier", new { identifier = $"/lu-legilux/{fixture.WorkKey}" });
        Assert.AreEqual("lu-legilux", luxembourg.Result!.Value.GetProperty("publisher").GetString());
        Assert.IsTrue(luxembourg.Result.Value.GetProperty("states").GetArrayLength() > 0);
        Assert.IsFalse(ContainsProperty(luxembourg.Result.Value, "wording_date"), "a Luxembourg record never carries an EU wording date.");
    }

    private static string?[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(static value => value.GetString()).ToArray();

    private static bool ContainsProperty(JsonElement element, string name) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Any(property =>
            string.Equals(property.Name, name, StringComparison.Ordinal) || ContainsProperty(property.Value, name)),
        JsonValueKind.Array => element.EnumerateArray().Any(item => ContainsProperty(item, name)),
        _ => false,
    };
}
