using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// <c>provenance</c> driven through the real handler on a verified mount: how a served state is tied to
/// the digests the mount verified. It takes <c>as_of</c>'s request, selects by <c>as_of</c>'s rule and
/// refuses as <c>as_of</c> refuses, which is held byte for byte; the chain is held against a ground truth
/// read from the rows by code that shares nothing with the reader; the answer's property set is pinned at
/// every depth; and what is not held is a fixed list, with nothing signed.
/// </summary>
[TestClass]
public sealed class V3CorpusProvenanceMountTests
{
    private static string RawTarget => V3RestRouteBinding.Provenance.RawTarget;

    private sealed record Ground(
        (string ObjectRef, string Outcome, string? Rights, string GapsJson)[] Members,
        (string StateSha256, string Expression, string Language, string IdentitiesJson, string ProfilesJson,
            string WorkKey, string Date, string WorkIri, string ResourceIri)[] States,
        (string Identity, string ObjectRef)[] Articles);

    private static Ground ReadGround(MountedFixture fixture)
    {
        using var connection = LuxembourgIndexBuilder.Open(
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);
        var members = new List<(string, string, string?, string)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT object_ref_sha256,outcome,rights_disposition,gaps_json FROM members";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                members.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3)));
            }
        }

        var states = new List<(string, string, string, string, string, string, string, string, string)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT state_sha256,expression_iri,language,article_identities_json,rule_profiles_json," +
                "work_key,applicability_date,publisher_work_iri,publisher_legal_resource_iri FROM states";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                states.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8)));
            }
        }

        var articles = new List<(string, string)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT article_identity_sha256,object_ref_sha256 FROM articles";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                articles.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        return new Ground(members.ToArray(), states.ToArray(), articles.ToArray());
    }

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The digest derivation as the answer states it, written out here from that sentence alone.</summary>
    private static string RecomputeStateDigest(
        string workKey, string date, string expression, string workIri, string resourceIri, string language,
        IEnumerable<string> profiles, IEnumerable<string> identities)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Append(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var length = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        Append("lex-v3-luxembourg-expression-state/1");
        foreach (var value in new[] { "lu-legilux", workKey, date, expression, workIri, resourceIri, language })
        {
            Append(value);
        }

        foreach (var profile in profiles.Order(StringComparer.Ordinal)) Append(profile);
        foreach (var identity in identities.Order(StringComparer.Ordinal)) Append(identity);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>The list digest as the derivation sentence states it, written out here from that sentence alone.</summary>
    private static string IdentitiesDigest(IEnumerable<string> identities)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var identity in identities.Order(StringComparer.Ordinal))
        {
            var bytes = Encoding.UTF8.GetBytes(identity);
            var length = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    [TestMethod]
    public void TheProvenanceRouteIsTheServedBinding()
    {
        Assert.AreEqual("/api/v3/provenance", RawTarget);
        Assert.AreEqual("provenance", V3RestRouteBinding.Provenance.OperationId);
        Assert.IsTrue(V3RestRouteBinding.Served.Contains(V3RestRouteBinding.Provenance));
    }

    [TestMethod]
    public async Task TheChainIsWhatTheRowsSayAndTheSameStateAsOfServes()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.SetMemberGapsAsync("[\"a_recorded_gap\",\"b_second_gap\"]");
        await fixture.SetMemberRightsDispositionAsync("a_recorded_rights_disposition");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var ground = ReadGround(fixture);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";

        var envelope = await ProvenanceAsync(mount, identifier, fixture.ApplicabilityDate, "fra");

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual("provenance", envelope.OperationId);
        Assert.AreEqual("provenance_chain", envelope.Result!.ObjectType);
        var body = envelope.Result.Value;
        var state = body.GetProperty("states").EnumerateArray().Single();
        var row = ground.States.Single(s => s.Expression == fixture.ExpressionIri);
        Assert.AreEqual(row.StateSha256, state.GetProperty("state_sha256").GetString());
        Assert.AreEqual(row.Language, state.GetProperty("language").GetString());
        Assert.AreEqual(fixture.StateSha256, state.GetProperty("state_sha256").GetString());
        Assert.AreEqual(fixture.Permalink, state.GetProperty("permalink").GetString());
        Assert.AreEqual(fixture.StableCoordinate, state.GetProperty("stable_coordinate").GetString());
        Assert.AreEqual(fixture.ExpressionIri, state.GetProperty("expression_iri").GetString());
        CollectionAssert.AreEqual(
            JsonSerializer.Deserialize<string[]>(row.ProfilesJson)!,
            state.GetProperty("rule_profile_sha256s").EnumerateArray().Select(static p => p.GetString()).ToArray());
        var identities = JsonSerializer.Deserialize<string[]>(row.IdentitiesJson)!;
        Assert.AreEqual(identities.Length, state.GetProperty("articles").GetInt32());
        Assert.IsGreaterThan(1, identities.Length, "The fixture's state must hold several articles for a source count to mean something.");

        // The derivation the answer states is the derivation: recomputed here from the rows using only that
        // sentence, it gives the digest the answer names, so a reader can check the chain outside this code.
        Assert.AreEqual(
            state.GetProperty("state_sha256").GetString(),
            RecomputeStateDigest(row.WorkKey, row.Date, row.Expression, row.WorkIri, row.ResourceIri, row.Language,
                JsonSerializer.Deserialize<string[]>(row.ProfilesJson)!, identities));

        // The sources are the corpus members that hold the state's articles, each once, in digest order,
        // with what the corpus recorded for it. Several articles of one document are one source.
        var expectedRefs = identities
            .Select(identity => ground.Articles.Single(a => a.Identity == identity).ObjectRef)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var sources = state.GetProperty("sources").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(expectedRefs, sources.Select(static s => s.GetProperty("object_ref_sha256").GetString()).ToArray());
        // The body facts are the corpus manifest's, read here from the corpus artifact itself and not through the mount.
        var corpus = VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(
            File.ReadAllBytes(Path.Combine(fixture.Directory, V3CorpusMount.CorpusFileName)));
        foreach (var source in sources)
        {
            var reference = source.GetProperty("object_ref_sha256").GetString();
            var held = corpus.Set.Members.Single(m => m.ObjectRefSha256 == reference);
            Assert.AreEqual(held.BodySha256, source.GetProperty("body_sha256").ValueKind == JsonValueKind.Null ? null : source.GetProperty("body_sha256").GetString());
            Assert.AreEqual(held.BodyByteLength, source.GetProperty("body_byte_length").ValueKind == JsonValueKind.Null ? null : source.GetProperty("body_byte_length").GetInt64());
            Assert.AreEqual(held.BodyReceiptSha256, source.GetProperty("body_receipt_sha256").ValueKind == JsonValueKind.Null ? null : source.GetProperty("body_receipt_sha256").GetString());
            // The body digest is not the object reference: naming one as the other is the overclaim this answer exists to avoid.
            Assert.AreNotEqual(reference, source.GetProperty("body_sha256").GetString());
        }

        foreach (var source in sources)
        {
            var member = ground.Members.Single(m => m.ObjectRef == source.GetProperty("object_ref_sha256").GetString());
            Assert.AreEqual(member.Outcome, source.GetProperty("outcome").GetString());
            Assert.AreEqual(member.Rights, source.GetProperty("rights_disposition").ValueKind == JsonValueKind.Null ? null : source.GetProperty("rights_disposition").GetString());
            CollectionAssert.AreEqual(
                JsonSerializer.Deserialize<string[]>(member.GapsJson)!,
                source.GetProperty("gaps").EnumerateArray().Select(static g => g.GetString()).ToArray());
        }

        CollectionAssert.AreEqual(new[] { "a_recorded_gap", "b_second_gap" }, sources.Single().GetProperty("gaps").EnumerateArray().Select(static g => g.GetString()).ToArray());
        Assert.AreEqual("a_recorded_rights_disposition", sources.Single().GetProperty("rights_disposition").GetString());

        var verified = body.GetProperty("verified_by");
        Assert.AreEqual(fixture.CorpusSha256, verified.GetProperty("corpus_sha256").GetString());
        // The gaps were set after the fixture was built, which re-stamped the index, so its digest is the file's now.
        var indexDigest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName))));
        Assert.AreNotEqual(fixture.IndexSha256, indexDigest, "The index was re-stamped by the gap change, so the fixture's original digest is stale.");
        Assert.AreEqual(indexDigest, verified.GetProperty("index_sha256").GetString());
        Assert.AreEqual(V3OperationRegistry.Reviewed.Sha256, verified.GetProperty("registry_sha256").GetString());

        // The same request to as_of names the same state: a caller moves from one to the other.
        var asOf = (await AsOfAsync(mount, identifier, fixture.ApplicabilityDate, "fra")).Result!.Value.GetProperty("states").EnumerateArray().Single();
        // And a caller who holds nothing but as_of's list and this answer can finish the recomputation the
        // derivation names: the list's digest is the one stated here, and the state digest follows from it.
        var callersList = asOf.GetProperty("article_identities").EnumerateArray().Select(static i => i.GetString()!).ToArray();
        Assert.AreEqual(state.GetProperty("article_identities_sha256").GetString(), IdentitiesDigest(callersList));
        Assert.AreEqual(
            state.GetProperty("state_sha256").GetString(),
            RecomputeStateDigest(row.WorkKey, state.GetProperty("applicability_date").GetString()!,
                state.GetProperty("expression_iri").GetString()!, state.GetProperty("publisher_work_iri").GetString()!,
                state.GetProperty("publisher_legal_resource_iri").GetString()!, state.GetProperty("language").GetString()!,
                state.GetProperty("rule_profile_sha256s").EnumerateArray().Select(static p => p.GetString()!).ToArray(), callersList));
        Assert.AreEqual(asOf.GetProperty("state_sha256").GetString(), state.GetProperty("state_sha256").GetString());
        Assert.AreEqual(asOf.GetProperty("permalink").GetString(), state.GetProperty("permalink").GetString());
        Assert.AreEqual(asOf.GetProperty("stable_coordinate").GetString(), state.GetProperty("stable_coordinate").GetString());
    }

    [TestMethod]
    public async Task TheLanguageAndTheDateSelectAndRefuseExactlyAsAsOfDoes()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddSecondLanguageStateAsync(fixture.ApplicabilityDate);
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        await fixture.AddStateAsync(laterDate, "later");
        var twinDate = Shift(fixture.ApplicabilityDate, 800);
        await fixture.AddStateAsync(twinDate, "twin-a");
        await fixture.AddStateAsync(twinDate, "twin-b");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var work = $"/lu-legilux/{fixture.WorkKey}";

        // No language asked: every served language's state on the date, as as_of serves them.
        var both = (await ProvenanceAsync(mount, work, fixture.ApplicabilityDate, null)).Result!.Value;
        var asOfBoth = (await AsOfAsync(mount, work, fixture.ApplicabilityDate, null)).Result!.Value;
        CollectionAssert.AreEqual(
            asOfBoth.GetProperty("states").EnumerateArray().Select(static s => s.GetProperty("state_sha256").GetString()).ToArray(),
            both.GetProperty("states").EnumerateArray().Select(static s => s.GetProperty("state_sha256").GetString()).ToArray());
        Assert.AreEqual(2, both.GetProperty("states").GetArrayLength());
        var french = (await ProvenanceAsync(mount, work, fixture.ApplicabilityDate, "fra")).Result!.Value;
        Assert.AreEqual(1, french.GetProperty("states").GetArrayLength());
        Assert.AreEqual("fra", french.GetProperty("states")[0].GetProperty("language").GetString());

        // The date selects the state in force on it, not the one dated on it.
        var between = (await ProvenanceAsync(mount, work, Shift(fixture.ApplicabilityDate, 100), "fra")).Result!.Value;
        Assert.AreEqual(fixture.StateSha256, between.GetProperty("states")[0].GetProperty("state_sha256").GetString());

        // Every refusal is as_of's, byte for byte, through the shared builders.
        foreach (var (label, identifier, date, language) in new (string, string, string, string?)[]
                 {
                     ("two states on the date", work, twinDate, "fra"),
                     ("a date before the history", work, "1900-01-01", "fra"),
                     ("a language not held", work, fixture.ApplicabilityDate, "eng"),
                     ("an identifier no work has", "/lu-legilux/no-such-work", fixture.ApplicabilityDate, "fra"),
                     ("a European identifier", "32016R0679", fixture.ApplicabilityDate, "fra"),
                 })
        {
            var provenance = await ProvenanceAsync(mount, identifier, date, language);
            var asOf = await AsOfAsync(mount, identifier, date, language);
            Assert.AreEqual(V3Verdicts.Refuse, provenance.Verdict, label);
            Assert.AreEqual(asOf.Refusal!.Code, provenance.Refusal!.Code, label);
            // The one thing that may differ is the mode a European identifier is refused for: it names the operation.
            if (provenance.Refusal.Code != "retrieval_mode_unavailable")
            {
                Assert.AreEqual(asOf.Refusal.HelpfulPayload.GetRawText(), provenance.Refusal.HelpfulPayload.GetRawText(), label);
            }
            else
            {
                Assert.AreEqual("r6_provenance", provenance.Refusal.HelpfulPayload.GetProperty("requested_mode").GetString(), label);
            }
        }

        // A date only a later state answers, and nothing on it in another language: still one refusal each way.
        Assert.AreEqual("ambiguous_version", (await ProvenanceAsync(mount, work, twinDate, "fra")).Refusal!.Code);
    }

    private static void CollectPaths(JsonElement element, string prefix, SortedSet<string> paths)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var path = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
                    paths.Add(path);
                    CollectPaths(property.Value, path, paths);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectPaths(item, prefix + "[]", paths);
                }

                break;
        }
    }

    [TestMethod]
    public async Task TheAnswerHasExactlyTheseProperties_SoAFieldOfAnyNameFailsUntilItIsDeclared()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddSecondLanguageStateAsync(fixture.ApplicabilityDate);
        await fixture.SetMemberGapsAsync("[\"a_recorded_gap\"]");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var body = (await ProvenanceAsync(mount, $"/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate, null)).Result!.Value;

        var paths = new SortedSet<string>(StringComparer.Ordinal);
        CollectPaths(body, string.Empty, paths);
        // The answer's promises are that it holds no first-sighting event and no signature and signs
        // nothing: a property of any name, at any depth, fails here until it is declared.
        var declared = new[]
        {
            "available_languages", "derivation",
            "not_held", "not_held[].item", "not_held[].reason",
            "publisher", "requested_date", "requested_identifier", "requested_language", "scope", "sources_note",
            "states", "states[].applicability_date", "states[].articles", "states[].expression_iri", "states[].language",
            "states[].article_identities_sha256", "states[].permalink", "states[].publisher_legal_resource_iri", "states[].publisher_work_iri",
            "states[].rule_profile_sha256s", "states[].sources", "states[].sources[].body_byte_length", "states[].sources[].body_receipt_sha256",
            "states[].sources[].body_sha256", "states[].sources[].gaps", "states[].sources[].object_ref_sha256", "states[].sources[].outcome",
            "states[].sources[].rights_disposition", "states[].stable_coordinate",
            "states[].state_sha256",
            "verified_by", "verified_by.corpus_sha256", "verified_by.index_sha256", "verified_by.registry_sha256",
            "work_key",
        }.Order(StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(declared, paths.ToArray(), "The answer carries a property that is not declared, or lost one that is.");
        Assert.AreEqual(2, body.GetProperty("states").GetArrayLength());
        Assert.IsTrue(body.GetProperty("states").EnumerateArray().All(static s => s.GetProperty("sources").GetArrayLength() > 0));
        Assert.IsTrue(body.GetProperty("states").EnumerateArray().Any(static s => s.GetProperty("sources")[0].GetProperty("gaps").GetArrayLength() > 0));
    }

    [TestMethod]
    public async Task TheAnswerSaysWhatIsNotHeldAndThatNothingIsSigned()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var body = (await ProvenanceAsync(mount, $"/lu-legilux/{fixture.WorkKey}", fixture.ApplicabilityDate, "fra")).Result!.Value;

        Assert.AreEqual(
            "the chain from the publisher's identifiers to the digests this mount verified; it holds no first-sighting event and no signature, so none is claimed",
            body.GetProperty("scope").GetString());
        Assert.AreEqual(
            "state_sha256 is a SHA-256 over the domain tag lex-v3-luxembourg-expression-state/1 and then, each as UTF-8 preceded by its " +
            "length as four bytes big-endian, the publisher, the work key, the applicability date, the expression, the publisher work IRI, " +
            "the publisher legal-resource IRI, the language, each rule-profile digest in sorted order and each article identity in sorted " +
            "order; the article identities are not carried here (they are article_identities in as_of's answer to the same request, and " +
            "article_identities_sha256 is the SHA-256 of them in sorted order, each preceded by its length in the same way, so a caller can check " +
            "the list it holds); this mount's own reader recomputes the state digest when the index is opened and refuses an index in which it " +
            "does not match its row",
            body.GetProperty("derivation").GetString());
        Assert.AreEqual(
            "object_ref_sha256 identifies the source object in the corpus; body_sha256 is the digest of the publisher bytes the corpus retained " +
            "for it, body_byte_length their length and body_receipt_sha256 the digest of the corpus receipt for that body, each null where the " +
            "corpus holds none",
            body.GetProperty("sources_note").GetString());
        var notHeld = body.GetProperty("not_held").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(
            new[] { "first_sighting_event", "signature_stamp", "publisher_revision_history" },
            notHeld.Select(static row => row.GetProperty("item").GetString()).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "no observation time or first-sighting event is held, so nothing here says when the publisher's bytes were first seen",
                "no signature or stamp is held or made: this states which digests this mount verified and signs nothing",
                "no record of corrections or withdrawals of the publisher's document is held",
            },
            notHeld.Select(static row => row.GetProperty("reason").GetString()).ToArray());
    }

    [TestMethod]
    public async Task AMountWithoutTheLuxembourgIndexRefusesAsNoCorpusMounted()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName));
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.CapabilityManifestFileName));
        await fixture.AddEuropeCollisionAsync();
        using var europeOnly = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(europeOnly);

        var unmounted = await ProvenanceAsync(europeOnly, "/lu-legilux/any-work", "2024-01-01", null);

        Assert.AreEqual("no_corpus_mounted", unmounted.Refusal!.Code);
    }

    [TestMethod]
    [DataRow("{\"operation_id\":\"provenance\",\"parameters\":{}}")]
    [DataRow("{\"operation_id\":\"provenance\",\"parameters\":{\"identifier\":\"/lu-legilux/x\"}}")]
    [DataRow("{\"operation_id\":\"provenance\",\"parameters\":{\"date\":\"2024-01-01\"}}")]
    [DataRow("{\"operation_id\":\"provenance\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"date\":\"2024-02-30\"}}")]
    [DataRow("{\"operation_id\":\"provenance\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"date\":\"2024-01-01\",\"language\":\" \"}}")]
    // Undeclared: rejected, never ignored (a limit, a signature or a sort are other questions).
    [DataRow("{\"operation_id\":\"provenance\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"date\":\"2024-01-01\",\"signed\":true}}")]
    [DataRow("{\"operation_id\":\"provenance\",\"parameters\":{\"identifier\":\"/lu-legilux/x\",\"date\":\"2024-01-01\",\"limit\":5}}")]
    public async Task UnusableProvenanceRequestsAreBelowEnvelopeSchemaRejections(string body)
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var context = await PostAsync(mount, RawTarget, body);

        Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.AreEqual("application/problem+json", context.Response.ContentType);
        using var problem = JsonDocument.Parse(ResponseBytes(context));
        Assert.AreEqual("request_schema_invalid", problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task<V3Envelope> ProvenanceAsync(V3CorpusMount mount, string identifier, string date, string? language) =>
        await PostOkAsync(mount, RawTarget, "provenance", identifier, date, language);

    private static async Task<V3Envelope> AsOfAsync(V3CorpusMount mount, string identifier, string date, string? language) =>
        await PostOkAsync(mount, "/api/v3/as_of", "as_of", identifier, date, language);

    private static async Task<V3Envelope> PostOkAsync(
        V3CorpusMount mount, string rawTarget, string operation, string identifier, string date, string? language)
    {
        var parameters = new Dictionary<string, object> { ["identifier"] = identifier, ["date"] = date };
        if (language is not null)
        {
            parameters["language"] = language;
        }

        var context = await PostAsync(mount, rawTarget, JsonSerializer.Serialize(new { operation_id = operation, parameters }));
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    private static async Task<DefaultHttpContext> PostAsync(V3CorpusMount mount, string rawTarget, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-provenance";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        return context;
    }
}
