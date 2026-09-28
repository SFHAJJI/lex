using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// <c>verify</c> on a mounted corpus: a hash-pinned permalink is verified against the state digest
/// the index holds at its stable coordinate (the same reading <c>resolve</c> makes of a pinned
/// identifier), a digest the coordinate no longer carries is the <c>pinned_digest_mismatch</c>
/// refusal naming the current one, and a work identifier answers the current digests so a caller
/// can pin them.
/// </summary>
[TestClass]
public sealed class V3CorpusVerifyMountTests
{
    private static string RawTarget => V3RestRouteBinding.Verify.RawTarget;

    [TestMethod]
    public void TheVerifyRouteIsTheServedBinding()
    {
        Assert.AreEqual("/api/v3/verify", RawTarget);
        Assert.AreEqual("verify", V3RestRouteBinding.Verify.OperationId);
        Assert.IsTrue(V3RestRouteBinding.Served.Contains(V3RestRouteBinding.Verify));
    }

    [TestMethod]
    public async Task ThePinnedPermalinkOfTheHeldStateVerifiesByItsDigestAndNamesItsSources()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await VerifyAsync(mount, new { identifier = fixture.Permalink });

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual("verify", envelope.OperationId);
        Assert.AreEqual("verification", envelope.Result!.ObjectType);
        var body = envelope.Result.Value;
        Assert.AreEqual("digest_matches", body.GetProperty("verdict").GetString());
        Assert.AreEqual(fixture.StateSha256, body.GetProperty("requested_digest").GetString());
        Assert.AreEqual(fixture.StateSha256, body.GetProperty("state_sha256").GetString());
        Assert.AreEqual(fixture.WorkKey, body.GetProperty("work_key").GetString());
        Assert.AreEqual(fixture.ApplicabilityDate, body.GetProperty("applicability_date").GetString());
        Assert.AreEqual(fixture.StableCoordinate, body.GetProperty("stable_coordinate").GetString());
        Assert.AreEqual(fixture.Permalink, body.GetProperty("permalink").GetString());
        Assert.AreEqual(fixture.ExpressionIri, body.GetProperty("expression_iri").GetString());
        Assert.IsGreaterThan(0, body.GetProperty("articles").GetInt32());
        Assert.AreEqual(64, body.GetProperty("article_identities_sha256").GetString()!.Length);
        Assert.IsGreaterThan(0, body.GetProperty("rule_profile_sha256s").GetArrayLength());
        Assert.IsGreaterThan(0, body.GetProperty("sources").GetArrayLength(), "the state's sources are the ones provenance names.");
        var verifiedBy = body.GetProperty("verified_by");
        Assert.AreEqual(fixture.CorpusSha256, verifiedBy.GetProperty("corpus_sha256").GetString());
        Assert.AreEqual(fixture.IndexSha256, verifiedBy.GetProperty("index_sha256").GetString());
        Assert.AreEqual(V3OperationRegistry.Reviewed.Sha256, verifiedBy.GetProperty("registry_sha256").GetString());
        CollectionAssert.AreEqual(new[] { "fra" }, body.GetProperty("available_languages").EnumerateArray().Select(static v => v.GetString()).ToArray());
        Assert.AreEqual(3, body.GetProperty("not_held").GetArrayLength());
    }

    [TestMethod]
    public async Task ADigestTheCoordinateNoLongerCarriesIsThePinnedDigestMismatchNamingTheCurrentOne()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var stale = fixture.StableCoordinate + "--" + new string('0', 64);

        var envelope = await VerifyAsync(mount, new { identifier = stale });

        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("pinned_digest_mismatch", envelope.Refusal!.Code);
        var payload = envelope.Refusal.HelpfulPayload;
        Assert.AreEqual(new string('0', 64), payload.GetProperty("requested_digest").GetString());
        Assert.AreEqual(fixture.StateSha256, payload.GetProperty("current_digest").GetString());
        Assert.AreEqual(fixture.StableCoordinate, payload.GetProperty("stable_coordinate").GetString());
        Assert.AreEqual(fixture.Permalink, payload.GetProperty("current_hash_pinned_url").GetString());
        Assert.IsGreaterThan(0, payload.GetProperty("rule_profile_sha256s").GetArrayLength());
    }

    [TestMethod]
    public async Task APermalinkAtACoordinateThatHoldsNoStateIsUnknown()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await VerifyAsync(mount, new { identifier = $"/lu-legilux/{fixture.WorkKey}/1900-01-01--{fixture.StateSha256}" });

        Assert.AreEqual("identifier_unknown", envelope.Refusal!.Code);
        StringAssert.Contains(envelope.Refusal.HelpfulPayload.GetProperty("what_would_answer").GetString(), "hash-pinned permalink");
    }

    [TestMethod]
    public async Task AWorkIdentifierAnswersTheCurrentDigestsSoACallerCanPinThem()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var identifier in new[] { $"/lu-legilux/{fixture.WorkKey}", fixture.StableCoordinate[..fixture.StableCoordinate.LastIndexOf('/')] })
        {
            var envelope = await VerifyAsync(mount, new { identifier });
            Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, identifier);
            var body = envelope.Result!.Value;
            Assert.AreEqual("current_digests", body.GetProperty("verdict").GetString());
            Assert.AreEqual(JsonValueKind.Null, body.GetProperty("requested_digest").ValueKind);
            var states = body.GetProperty("states").EnumerateArray().ToArray();
            Assert.AreEqual(1, states.Length);
            Assert.AreEqual(fixture.Permalink, states[0].GetProperty("permalink").GetString());
            Assert.AreEqual(fixture.StateSha256, states[0].GetProperty("state_sha256").GetString());
        }
    }

    [TestMethod]
    public async Task ALanguageNotHeldIsRefusedOnBothForms()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var identifier in new[] { fixture.Permalink, $"/lu-legilux/{fixture.WorkKey}" })
        {
            var envelope = await VerifyAsync(mount, new { identifier, language = "eng" });
            Assert.AreEqual("language_not_available", envelope.Refusal!.Code, identifier);
            CollectionAssert.AreEqual(
                new[] { "fra" },
                envelope.Refusal.HelpfulPayload.GetProperty("available_languages").EnumerateArray().Select(static v => v.GetString()).ToArray());
        }
    }

    [TestMethod]
    public async Task ARequestWithoutAnIdentifierIsATransportFailureNotAnEnvelope()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var context = await PostAsync(mount, RawTarget, JsonSerializer.Serialize(new { operation_id = "verify", parameters = new { } }));

        Assert.AreNotEqual(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    private static async Task<V3Envelope> VerifyAsync(V3CorpusMount mount, object parameters)
    {
        var context = await PostAsync(mount, RawTarget, JsonSerializer.Serialize(new { operation_id = "verify", parameters }));
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    private static async Task<DefaultHttpContext> PostAsync(V3CorpusMount mount, string rawTarget, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-verify";
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
