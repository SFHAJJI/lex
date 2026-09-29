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

        // The fixture's provision identifier is carried by a second act too, so the provision alone names two works.
        var second = await fixture.AddSecondExpressionWithSamePublisherProvisionIdentifierAsync();
        using var twoWorks = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(twoWorks);
        var byCelex = await EnvelopeAsync(twoWorks, RawTarget, "dossier", new { identifier = Celex });
        Assert.IsNull(byCelex.Refusal, "the CELEX still names one work.");
        Assert.AreEqual(1, byCelex.Result!.Value.GetProperty("expression_count").GetInt32(), "the second act is another work, not listed here.");
        Assert.IsNotNull(second);
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
