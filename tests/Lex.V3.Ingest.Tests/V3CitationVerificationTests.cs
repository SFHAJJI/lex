using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The launch contract's first promise to the reader: "verify resolves every citation the product emitted". Every
/// answer the live screens read (the operations the answer census samples, driven the same way on the same two fixture
/// mounts) is walked whole. Every citation is taken by its role (a <c>permalink</c> or <c>*_permalink</c> property, a
/// <c>resolve.identifier</c>) and must be a hash-pinned permalink, and any other permalink is taken wherever it sits; each
/// is asked of <c>verify</c> on the mount that emitted it. Each must answer <c>digest_matches</c> for the state its digest names,
/// and a permalink with an article fragment must name that article. A citation the product prints and cannot vouch for
/// is the failure this exists to catch.
/// </summary>
[TestClass]
public sealed class V3CitationVerificationTests
{
    private static readonly DateTimeOffset ObservedAt =
        DateTimeOffset.Parse("2026-09-17T00:00:00Z", CultureInfo.InvariantCulture);

    /// <summary>A hash-pinned permalink: the stable coordinate, <c>--</c> and the state digest, and an article id after <c>#</c>.</summary>
    private static readonly Regex Permalink = new(
        @"^/lu-legilux/[a-z0-9_-]+/\d{4}-\d{2}-\d{2}--(?<digest>[0-9a-f]{64})(#(?<anchor>[^#\s]+))?$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// A hash-pinned EU permalink (#850): the CELEX, the language, the wording date, <c>--</c> and the wording digest, and
    /// the provision after <c>#</c>, escaped as the platform escapes it.
    /// </summary>
    private static readonly Regex EuropePermalink = new(
        @"^/eu-eurlex/[^/#\s]+/[a-z]{3}/\d{4}-\d{2}-\d{2}--(?<digest>[0-9a-f]{64})(#(?<anchor>[^#\s]+))?$",
        RegexOptions.CultureInvariant);

    [TestMethod]
    public async Task EveryPermalinkTheServedAnswersEmitVerifiesOnTheMountThatEmittedIt()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        // The compare and radar screens' second fixture: a later state with one article reworded and one id renamed.
        var compared = await MountedFixture.CreateAsync();
        await using var cleanupCompared = compared;
        var own = compared.ArticlesOfOwnState().OrderBy(static article => article.PublisherId, StringComparer.Ordinal).ToArray();
        var changedArticle = own.Single(static article => article.PublisherId == "art_15");
        var laterDate = DateOnly.ParseExact(compared.ApplicabilityDate, "yyyy-MM-dd").AddDays(400).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var later = await compared.AddStateAsync(laterDate, "later");
        await compared.RewriteArticleTextAsync(later.ExpressionIri, changedArticle.PublisherId, changedArticle.Text + " amended");
        await compared.RenameArticleIdAsync(later.ExpressionIri, "art_16", "art_16-new");
        using var comparedMount = await V3CorpusMount.OpenAsync(compared.Directory, CancellationToken.None);
        Assert.IsNotNull(comparedMount);

        var parameters = new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" };
        var emitted = new List<(V3CorpusMount Mount, string Operation, string Permalink)>();
        async Task CollectAsync(V3CorpusMount from, string operation, object request)
        {
            var answer = await AnswerAsync(from, operation, request);
            foreach (var (path, value, _) in CitationsIn(answer))
            {
                Assert.IsTrue(Permalink.IsMatch(value), $"{operation} emitted {value} at {path} as a citation, and it is not a hash-pinned permalink");
                emitted.Add((from, operation, value));
            }
        }

        await CollectAsync(mount, "search", new { query = "assemblée générale", language = "fra" });
        await CollectAsync(mount, "dossier", new { parameters.identifier, parameters.language });
        await CollectAsync(mount, "as_of", parameters);
        await CollectAsync(mount, "evidence_bundle", parameters);
        await CollectAsync(mount, "provenance", parameters);
        await CollectAsync(mount, "status_on", parameters);
        await CollectAsync(mount, "article_history", new { parameters.identifier, anchor = "art_15", parameters.language });
        await CollectAsync(mount, "diff", new { parameters.identifier, date_from = parameters.date, date_to = parameters.date, parameters.language });
        await CollectAsync(mount, "changes_in_period", new { date_from = parameters.date, date_to = parameters.date });
        await CollectAsync(comparedMount, "diff", new { identifier = $"/lu-legilux/{compared.WorkKey}", date_from = compared.ApplicabilityDate, date_to = laterDate, language = "fra" });
        await CollectAsync(comparedMount, "changes_in_period", new { date_from = compared.ApplicabilityDate, date_to = laterDate });

        // Every screen that prints a citation contributed one, or the walk proves less than it says.
        CollectionAssert.IsSubsetOf(
            new[] { "search", "dossier", "as_of", "evidence_bundle", "article_history", "diff", "changes_in_period" },
            emitted.Select(static entry => entry.Operation).Distinct(StringComparer.Ordinal).ToArray(),
            "an operation a live screen reads emitted no permalink the walk could find");
        Assert.IsTrue(emitted.Any(static entry => entry.Permalink.Contains('#', StringComparison.Ordinal)), "no article permalink was emitted");

        foreach (var (from, operation, permalink) in emitted.Distinct())
        {
            var match = Permalink.Match(permalink);
            var verified = await EnvelopeAsync(from, "verify", new { identifier = permalink });
            Assert.AreEqual(V3Verdicts.Answer, verified.Verdict, $"{operation} emitted {permalink}, and verify refused it with {verified.Refusal?.Code}");
            var body = verified.Result!.Value;
            Assert.AreEqual("digest_matches", body.GetProperty("verdict").GetString(), $"{operation}: {permalink}");
            Assert.AreEqual(match.Groups["digest"].Value, body.GetProperty("state_sha256").GetString(), $"{operation}: {permalink} verified another state");
            if (match.Groups["anchor"].Success)
            {
                Assert.AreEqual(match.Groups["anchor"].Value, body.GetProperty("requested_anchor").GetString(), $"{operation}: {permalink}");
            }
        }
    }

    [TestMethod]
    public async Task EveryPermalinkTheEuAnswersEmitVerifiesAndEveryCoordinateTheyPrintResolves()
    {
        // The EU half of the promise, over the answers the EU screens read (search in one work, and the dossier), on the
        // GDPR fixture. An EU answer's citations by role are its permalinks, each pinning the one wording held; its
        // resolve.identifier is the coordinate EU resolve answers (a provision or an expression), not a pinned citation, so
        // it is asked of resolve and must answer. Any other string that is exactly an EU permalink is a citation too.
        var europe = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = europe;
        using var mount = await V3CorpusMount.OpenAsync(europe.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var permalinks = new List<(string Operation, string Permalink)>();
        var coordinates = new List<(string Operation, string Coordinate)>();
        async Task CollectAsync(string operation, object request)
        {
            var answer = await AnswerAsync(mount, operation, request);
            foreach (var (path, value, _) in CitationsIn(answer, "$", EuropePermalink))
            {
                if (path.EndsWith(".resolve.identifier", StringComparison.Ordinal))
                {
                    coordinates.Add((operation, value));
                    continue;
                }

                Assert.IsTrue(EuropePermalink.IsMatch(value), $"{operation} emitted {value} at {path} as a citation, and it is not a hash-pinned EU permalink");
                permalinks.Add((operation, value));
            }
        }

        await CollectAsync("search", new { query = "joint controllers", language = "eng", identifier = "32016R0679" });
        await CollectAsync("dossier", new { identifier = "32016R0679" });
        // The EU reading screen's answer: the bundle of the original wording, on its wording date as the dossier pins it.
        var wordingDate = (await EnvelopeAsync(mount, "dossier", new { identifier = "32016R0679" })).Result!.Value
            .GetProperty("expressions")[0].GetProperty("pinned_wording").GetProperty("wording_date").GetString();
        await CollectAsync("evidence_bundle", new { identifier = "32016R0679", date = wordingDate });

        // Every EU answer contributed, with a provision permalink among them, or the walk proves less than it says.
        CollectionAssert.AreEquivalent(new[] { "search", "dossier", "evidence_bundle" }, permalinks.Select(static entry => entry.Operation).Distinct(StringComparer.Ordinal).ToArray());
        Assert.IsTrue(permalinks.Any(static entry => entry.Permalink.Contains('#', StringComparison.Ordinal)), "no EU provision permalink was emitted");
        CollectionAssert.AreEquivalent(new[] { "search", "dossier" }, coordinates.Select(static entry => entry.Operation).Distinct(StringComparer.Ordinal).ToArray());

        foreach (var (operation, permalink) in permalinks.Distinct())
        {
            var match = EuropePermalink.Match(permalink);
            var verified = await EnvelopeAsync(mount, "verify", new { identifier = permalink });
            Assert.AreEqual(V3Verdicts.Answer, verified.Verdict, $"{operation} emitted {permalink}, and verify refused it with {verified.Refusal?.Code}");
            var body = verified.Result!.Value;
            Assert.AreEqual("digest_matches", body.GetProperty("verdict").GetString(), $"{operation}: {permalink}");
            Assert.AreEqual(match.Groups["digest"].Value, body.GetProperty("wording_sha256").GetString(), $"{operation}: {permalink} verified another wording");
            if (match.Groups["anchor"].Success)
            {
                Assert.AreEqual(Uri.UnescapeDataString(match.Groups["anchor"].Value), body.GetProperty("requested_anchor").GetString(), $"{operation}: {permalink}");
            }
        }

        foreach (var (operation, coordinate) in coordinates.Distinct())
        {
            var resolved = await EnvelopeAsync(mount, "resolve", new { identifier = coordinate });
            Assert.AreEqual(V3Verdicts.Answer, resolved.Verdict, $"{operation} printed {coordinate} to resolve, and resolve refused it with {resolved.Refusal?.Code}");
        }
    }

    [TestMethod]
    public void TheWalkTakesEveryCitationByItsRoleAndAnyOtherPermalinkWhereverItSits()
    {
        var digest = new string('a', 64);
        var node = JsonNode.Parse($$"""
            {
              "permalink": "/lu-legilux/loi-x/2024-02-01--{{digest}}",
              "rows": [{ "nested": { "article_permalink": "/lu-legilux/loi-x/2024-02-01--{{digest}}#art_1" } }],
              "broken": { "article_permalink": "/lu-legilux/loi-x/2024-02-01--{{digest}}?broken#art_2" },
              "hits": [{ "resolve": { "identifier": "/lu-legilux/loi-x/2024-02-01" } }],
              "baseline": { "permalink": null },
              "candidates": ["/lu-legilux/loi-x/2024-02-01--{{digest}}", "/lu-legilux/loi-x/2024-02-01"],
              "note": "a sentence mentioning /lu-legilux/loi-x/2024-02-01--{{digest}} is prose, not a citation",
              "short": "/lu-legilux/loi-x/2024-02-01--abc"
            }
            """)!;
        var found = CitationsIn(node).ToArray();
        CollectionAssert.AreEquivalent(
            new[]
            {
                ($"$.permalink", $"/lu-legilux/loi-x/2024-02-01--{digest}", true),
                ($"$.rows[0].nested.article_permalink", $"/lu-legilux/loi-x/2024-02-01--{digest}#art_1", true),
                ($"$.broken.article_permalink", $"/lu-legilux/loi-x/2024-02-01--{digest}?broken#art_2", true),
                ($"$.hits[0].resolve.identifier", "/lu-legilux/loi-x/2024-02-01", true),
                ($"$.candidates[0]", $"/lu-legilux/loi-x/2024-02-01--{digest}", false),
            },
            found,
            "a citation is taken by its role whatever it holds, so a malformed one is a finding rather than a skip (review of #796)");
        Assert.IsFalse(Permalink.IsMatch(found.Single(static entry => entry.Path == "$.broken.article_permalink").Value));
    }

    /// <summary>
    /// Every citation the answer carries, with where it sits. By role: every value of a property named <c>permalink</c>
    /// or ending <c>_permalink</c>, and every <c>resolve.identifier</c> (the state a search hit or a radar row sends a
    /// reader to), whatever it holds, so a malformed citation is caught rather than skipped (review of #796). And any
    /// other string that is exactly a hash-pinned permalink, such as a candidate list.
    /// </summary>
    private static IEnumerable<(string Path, string Value, bool ByRole)> CitationsIn(JsonNode? node, string path = "$", Regex? pinned = null)
    {
        pinned ??= Permalink;
        switch (node)
        {
            case JsonObject value:
                foreach (var (key, child) in value)
                {
                    var at = $"{path}.{key}";
                    var byRole = key == "permalink" || key.EndsWith("_permalink", StringComparison.Ordinal)
                        || (key == "identifier" && path.EndsWith(".resolve", StringComparison.Ordinal));
                    if (byRole)
                    {
                        if (child is not null)
                        {
                            yield return (at, child.GetValueKind() == JsonValueKind.String ? child.GetValue<string>() : child.ToJsonString(), true);
                        }

                        continue;
                    }

                    foreach (var found in CitationsIn(child, at, pinned))
                    {
                        yield return found;
                    }
                }

                break;
            case JsonArray value:
                for (var index = 0; index < value.Count; index++)
                {
                    foreach (var found in CitationsIn(value[index], $"{path}[{index}]", pinned))
                    {
                        yield return found;
                    }
                }

                break;
            case JsonValue value when value.TryGetValue<string>(out var text) && pinned.IsMatch(text):
                yield return (path, text, false);
                break;
        }
    }

    private static async Task<JsonNode> AnswerAsync(V3CorpusMount mount, string operation, object parameters)
    {
        var envelope = await EnvelopeAsync(mount, operation, parameters);
        Assert.IsNull(envelope.Refusal, $"{operation} refused with {envelope.Refusal?.Code}");
        return JsonNode.Parse(envelope.Result!.Value.GetRawText())!;
    }

    private static async Task<V3Envelope> EnvelopeAsync(V3CorpusMount mount, string operation, object parameters)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { operation_id = operation, parameters }));
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "citation-verification";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/api/v3/" + operation;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, operation);
        return V3EnvelopeJson.ParseAndVerify(((MemoryStream)context.Response.Body).ToArray(), V3OperationRegistry.Reviewed);
    }
}
