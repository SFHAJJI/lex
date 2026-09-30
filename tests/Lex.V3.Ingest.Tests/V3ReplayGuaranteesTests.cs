using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The replay guarantees of <c>33-product-spec.md</c> (G1 to G5) that a single build can prove, run against the real
/// handler on a mounted corpus: G2 snapshot determinism (every served operation answers the same canonical bytes from
/// the same snapshot, from a byte copy of it and when asked again later, apart from the observation time and the
/// request's own reference) and G5 independent verifiability (a reader holding one evidence bundle and the publisher's
/// file finds each served article's text in that file, and recomputes the body digest, each text digest, the article
/// identities digest and the state digest from the derivations the platform publishes).
/// </summary>
/// <remarks>
/// G1 (a replaced publisher file mints a new version id and a <c>file_replaced</c> event), G3 (nothing hard-deleted
/// across builds) and G4 (as-observed answering) need predecessor chaining with observation times: one build holds no
/// observation time and its log is a genesis log, so they wait for a second build after the first mount, and their
/// operations (<c>as_observed</c>, <c>knowable_on</c>) are registered and not served. G2's detached signature comes from
/// the release pipeline. G5 does not recompute <c>wording_sha256</c> (its input is the stored token stream, which the
/// bundle does not serve), nor the article identities, the rule-profile digests or the body receipt, whose derivations
/// are not published. The mount is the test fixture, so this proves the path, not a corpus.
/// </remarks>
[TestClass]
public sealed class V3ReplayGuaranteesTests
{
    private static readonly DateTimeOffset Later = ObservedAt.AddDays(30);

    [TestMethod]
    public async Task EveryServedOperationAnswersTheSameBytesFromTheSameSnapshotFromACopyOfItAndLater()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var laterDate = DateOnly.ParseExact(fixture.ApplicabilityDate, "yyyy-MM-dd").AddDays(400).ToString("yyyy-MM-dd");
        await fixture.AddStateAsync(laterDate, "later");
        var copy = Path.Combine(Path.GetTempPath(), $"lex-v3-replay-copy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(copy);
        try
        {
            foreach (var name in new[] { V3CorpusMount.IndexFileName, V3CorpusMount.CapabilityManifestFileName, V3CorpusMount.CorpusFileName })
            {
                File.Copy(Path.Combine(fixture.Directory, name), Path.Combine(copy, name));
            }

            using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
            using var copied = await V3CorpusMount.OpenAsync(copy, CancellationToken.None);
            Assert.IsNotNull(mount);
            Assert.IsNotNull(copied);

            var work = $"/lu-legilux/{fixture.WorkKey}";
            var date = fixture.ApplicabilityDate;
            var requests = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["resolve"] = new { identifier = fixture.Permalink },
                ["as_of"] = new { identifier = work, date, language = "fra" },
                ["timeline"] = new { identifier = work, language = "fra" },
                ["article_history"] = new { identifier = work, anchor = "art_2", language = "fra" },
                ["diff"] = new { identifier = work, date_from = date, date_to = laterDate, language = "fra" },
                ["changes_in_period"] = new { identifier = work, date_from = date, date_to = laterDate },
                ["in_force_on"] = new { identifier = work, date, language = "fra" },
                ["search"] = new { query = "Assemblée", language = "fra" },
                ["coverage"] = new { },
                ["provenance"] = new { identifier = work, date, language = "fra" },
                ["dossier"] = new { identifier = work, language = "fra" },
                ["citation"] = new { identifier = work, date },
                ["cited_by"] = new { identifier = work },
                ["verify"] = new { identifier = fixture.Permalink + "#art_2", language = "fra" },
                ["relations"] = new { identifier = work },
                ["evidence_bundle"] = new { identifier = work, date, language = "fra" },
                ["classification"] = new { identifier = work, language = "fra" },
                ["manifestation"] = new { identifier = work },
                ["status_on"] = new { identifier = work, date, language = "fra" },
                ["browse"] = new { },
                ["ask"] = new { question = $"Quel est le texte de {work} au {date} ?" },
                ["events"] = new { },
                ["answer_drift"] = new { },
            };
            CollectionAssert.AreEquivalent(
                V3RestRouteBinding.Served.Select(static binding => binding.OperationId).ToArray(),
                requests.Keys.ToArray(),
                "every served operation has a replay case, so a new operation fails here until it has one.");

            foreach (var (operation, parameters) in requests)
            {
                // The same request (one trace identity) asked twice, and of a byte copy of the mount: the same bytes.
                var first = await AnswerAsync(mount, operation, parameters, ObservedAt, "replay-first");
                CollectionAssert.AreEqual(first, await AnswerAsync(mount, operation, parameters, ObservedAt, "replay-first"), $"{operation}: asked again");
                CollectionAssert.AreEqual(first, await AnswerAsync(copied, operation, parameters, ObservedAt, "replay-first"), $"{operation}: from a byte copy of the mount");

                // Asked later, by another request: only the observation time and the request's own reference move; the
                // snapshot, the result and any refusal are the same bytes.
                var later = await AnswerAsync(mount, operation, parameters, Later, "replay-later");
                var earlier = WithoutRequestOwnFields(first);
                var then = WithoutRequestOwnFields(later);
                Assert.AreNotEqual(earlier.ObservedAt, then.ObservedAt, $"{operation}: the observation time is the request's own");
                Assert.AreNotEqual(earlier.RequestRef, then.RequestRef, $"{operation}: the request reference is the request's own");
                Assert.AreEqual(earlier.Remainder, then.Remainder, $"{operation}: asked later by another request");
            }
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [TestMethod]
    public async Task OneEvidenceBundleAndThePublisherFileAreEnoughToCheckItsTextAndRecomputeItsDerivedDigests()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var envelope = V3EnvelopeJson.ParseAndVerify(
            await AnswerAsync(mount, "evidence_bundle", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" }, ObservedAt, "replay-bundle"),
            V3OperationRegistry.Reviewed);
        Assert.IsNull(envelope.Refusal, envelope.Refusal?.Code);
        var bundle = envelope.Result!.Value;
        var state = bundle.GetProperty("states").EnumerateArray().Single();
        var articles = state.GetProperty("articles").EnumerateArray().ToArray();
        Assert.IsNotEmpty(articles);

        // The publisher's file, as the publisher serves it, hashes to the body digest the bundle names for its source.
        var publisherFile = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuAknLegalContent", "loi-1991-08-10-n3--2024-02-01--fr.bin"));
        var source = state.GetProperty("sources").EnumerateArray().Single();
        Assert.AreEqual(Sha256(publisherFile), source.GetProperty("body_sha256").GetString(), "the source's body digest is the publisher file's");
        Assert.AreEqual(publisherFile.Length, source.GetProperty("body_byte_length").GetInt32());
        CollectionAssert.AreEqual(
            new[] { source.GetProperty("body_sha256").GetString() },
            state.GetProperty("body_sha256s").EnumerateArray().Select(static value => value.GetString()).ToArray());

        // Each served article's text is in the publisher's file: the article with that id, its non-blank text nodes
        // in document order, outside the publisher's own scl: annotations and the authorial notes (which travel as
        // notes). So the text a quote carries is the publisher's, not only self-consistent with its digest.
        XNamespace scl = "http://www.scl.lu";
        var publisherArticles = XDocument.Load(new MemoryStream(publisherFile)).Descendants()
            .Where(static element => element.Name.LocalName == "article" && element.Attribute("id") is not null)
            .ToDictionary(static element => element.Attribute("id")!.Value, StringComparer.Ordinal);
        foreach (var article in articles)
        {
            var id = article.GetProperty("publisher_id").GetString()!;
            Assert.IsTrue(publisherArticles.TryGetValue(id, out var inFile), $"{id}: the publisher's file holds that article");
            var publisherText = string.Concat(inFile.DescendantNodes().OfType<XText>()
                .Where(node => node.Ancestors().TakeWhile(ancestor => ancestor != inFile)
                    .All(ancestor => ancestor.Name.Namespace != scl && ancestor.Name.LocalName != "authorialNote"))
                .Select(static node => node.Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value)));
            Assert.AreEqual(publisherText, article.GetProperty("text").GetString(), $"{id}: the served text is the publisher's");

            var text = Encoding.UTF8.GetBytes(article.GetProperty("text").GetString()!);
            Assert.AreEqual(Sha256(text), article.GetProperty("text_sha256").GetString(), $"{id}: the text digest is the served text's");
            Assert.AreEqual(text.Length, article.GetProperty("text_byte_length").GetInt32(), id);
            Assert.AreEqual(source.GetProperty("body_sha256").GetString(), article.GetProperty("body_sha256").GetString(), $"{id}: read from the named source");
            Assert.AreEqual(state.GetProperty("permalink").GetString() + "#" + id, article.GetProperty("article_permalink").GetString(), id);
        }

        // The article identities digest and the state digest, recomputed from the derivations the platform publishes
        // (provenance's `derivation`): length-prefixed UTF-8 fields in a fixed order after a domain tag.
        var identities = articles.Select(static article => article.GetProperty("article_identity_sha256").GetString()!)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.AreEqual(LengthPrefixedSha256(identities), state.GetProperty("article_identities_sha256").GetString(),
            "article_identities_sha256 is the digest of the identities in sorted order, each preceded by its length");

        string[] fields =
        [
            "lex-v3-luxembourg-expression-state/1",
            bundle.GetProperty("publisher").GetString()!,
            bundle.GetProperty("work_key").GetString()!,
            state.GetProperty("applicability_date").GetString()!,
            state.GetProperty("expression_iri").GetString()!,
            state.GetProperty("publisher_work_iri").GetString()!,
            state.GetProperty("publisher_legal_resource_iri").GetString()!,
            state.GetProperty("language").GetString()!,
            .. state.GetProperty("rule_profile_sha256s").EnumerateArray().Select(static value => value.GetString()!).Order(StringComparer.Ordinal),
            .. identities,
        ];
        var stateSha256 = state.GetProperty("state_sha256").GetString();
        Assert.AreEqual(LengthPrefixedSha256(fields), stateSha256,
            "the state digest recomputes from the values provenance's derivation names, in its order, the domain tag first");
        StringAssert.EndsWith(state.GetProperty("permalink").GetString(), "--" + stateSha256, "the permalink pins that digest");

        // The derivation the reader followed is the one the platform states: the domain tag is length-prefixed like
        // every value after it (an earlier wording put it outside that clause, and a reader who followed it got another
        // digest).
        var provenance = V3EnvelopeJson.ParseAndVerify(
            await AnswerAsync(mount, "provenance", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" }, ObservedAt, "replay-provenance"),
            V3OperationRegistry.Reviewed).Result!.Value.GetProperty("derivation").GetString();
        StringAssert.StartsWith(provenance,
            "state_sha256 is a SHA-256 over these values, each as UTF-8 preceded by its length as four bytes big-endian: the domain tag " +
            "lex-v3-luxembourg-expression-state/1, the publisher, the work key, the applicability date, the expression, the publisher work IRI, " +
            "the publisher legal-resource IRI, the language, each rule-profile digest in sorted order and each article identity in sorted order;");
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>SHA-256 over each value as UTF-8 preceded by its length as four bytes big-endian, as the derivation reads.</summary>
    private static string LengthPrefixedSha256(IEnumerable<string> fields)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (var field in fields)
        {
            var bytes = Encoding.UTF8.GetBytes(field);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>The whole response bytes of one REST request with its own trace identity, answered at <paramref name="observedAt"/>.</summary>
    private static async Task<byte[]> AnswerAsync(
        V3CorpusMount mount, string operation, object parameters, DateTimeOffset observedAt, string traceIdentifier)
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { operation_id = operation, parameters }));
        var context = new DefaultHttpContext { TraceIdentifier = traceIdentifier };
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/api/v3/" + operation;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), () => observedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        var bytes = ((MemoryStream)context.Response.Body).ToArray();
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, $"{operation}: {Encoding.UTF8.GetString(bytes)}");
        V3EnvelopeJson.ParseAndVerify(bytes, V3OperationRegistry.Reviewed);
        return bytes;
    }

    /// <summary>The envelope without the two fields that belong to the request rather than the answer.</summary>
    private static (string Remainder, string? ObservedAt, string? RequestRef) WithoutRequestOwnFields(byte[] envelope)
    {
        var node = JsonNode.Parse(envelope)!.AsObject();
        var freshness = node["context"]!["freshness"]!.AsObject();
        var observedAt = (string?)freshness["observed_at"];
        freshness.Remove("observed_at");
        var requestRef = (string?)node["request_ref"];
        node.Remove("request_ref");
        return (node.ToJsonString(), observedAt, requestRef);
    }
}
