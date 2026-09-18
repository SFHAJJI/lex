using System.Text;
using System.Security.Cryptography;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Platform;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class V3CorpusResolveMountTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 9, 18, 7, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task MountedExactIndexServesResolveSuccessAndBindsSnapshot()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var context = Request(fixture.ExpressionIri);
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual(fixture.CorpusSha256, envelope.Context.Snapshot.SnapshotSha256);
        Assert.AreEqual(ObservedAt, envelope.Context.Freshness.ObservedAt);
        Assert.AreEqual("stale", envelope.Context.Freshness.UpstreamHealth);
        Assert.AreEqual(fixture.ExpressionIri, envelope.Result!.Value.GetProperty("expression_iri").GetString());
        Assert.AreEqual(fixture.IndexSha256, envelope.Result.Value.GetProperty("index_sha256").GetString());
        Assert.IsGreaterThan(0, envelope.Result.Value.GetProperty("article_identities").GetArrayLength());
    }

    [TestMethod]
    public async Task UnknownIdentifierReturnsReviewedDomainRefusalFromTheMountedCorpus()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var context = Request("eli/unknown");
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("identifier_unknown", envelope.Refusal!.Code);
        Assert.AreEqual("eli/unknown", envelope.Refusal.HelpfulPayload.GetProperty("requested_identifier").GetString());
        Assert.AreEqual(fixture.CorpusSha256, envelope.Context.Snapshot.SnapshotSha256);
    }

    [TestMethod]
    public async Task StaleHashPinnedPermalinkReturnsTypedMismatchWithoutSubstitution()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var requested = fixture.StateSha256[..^1] + (fixture.StateSha256[^1] == '0' ? '1' : '0');
        var identifier = $"/lu-legilux/{fixture.WorkKey}/{fixture.ApplicabilityDate}--{requested}";
        var context = Request(identifier);
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.IsNull(envelope.Result);
        Assert.AreEqual("pinned_digest_mismatch", envelope.Refusal!.Code);
        var payload = envelope.Refusal.HelpfulPayload;
        Assert.AreEqual(requested, payload.GetProperty("requested_digest").GetString());
        Assert.AreEqual(fixture.StateSha256, payload.GetProperty("current_digest").GetString());
        Assert.AreEqual(fixture.StableCoordinate, payload.GetProperty("stable_coordinate").GetString());
        Assert.AreEqual(fixture.Permalink, payload.GetProperty("current_hash_pinned_url").GetString());
        Assert.IsFalse(payload.TryGetProperty("reason", out _));
        Assert.IsGreaterThan(0, payload.GetProperty("rule_profile_sha256s").GetArrayLength());
        Assert.IsFalse(
            System.Text.Json.JsonSerializer.Serialize(envelope).Contains("searchable_text", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ExactHashPinnedPermalinkResolvesOnlyItsCurrentExpressionState()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var context = Request("https://law.soufien.lu" + fixture.Permalink);
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual(fixture.StateSha256, envelope.Result!.Value.GetProperty("state_sha256").GetString());
        Assert.AreEqual(fixture.ExpressionIri, envelope.Result.Value.GetProperty("expression_iri").GetString());
        Assert.AreEqual(fixture.Permalink, envelope.Result.Value.GetProperty("permalink").GetString());
    }

    [TestMethod]
    public async Task ExactHashSelectsItsStateWhenWorkAndDateHaveTwoLanguages()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var alternate = await fixture.AddSecondLanguageStateAtSameDateAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await ResolveAsync(mount, fixture.Permalink);

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual(fixture.StateSha256, envelope.Result!.Value.GetProperty("state_sha256").GetString());
        Assert.AreNotEqual(alternate.StateSha256, envelope.Result.Value.GetProperty("state_sha256").GetString());
    }

    [TestMethod]
    public async Task UnknownHashAcrossSeveralCurrentStatesReturnsAmbiguousCandidates()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var alternate = await fixture.AddSecondLanguageStateAtSameDateAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var unknown = new string('0', 64);
        if (unknown == fixture.StateSha256 || unknown == alternate.StateSha256) unknown = new string('1', 64);

        var envelope = await ResolveAsync(mount, fixture.StableCoordinate + "--" + unknown);

        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("ambiguous_identifier", envelope.Refusal!.Code);
        CollectionAssert.AreEquivalent(
            new[] { fixture.Permalink, fixture.StableCoordinate + "--" + alternate.StateSha256 },
            envelope.Refusal.HelpfulPayload.GetProperty("candidates").EnumerateArray()
                .Select(static value => value.GetString()).ToArray());
    }

    [TestMethod]
    public async Task WellFormedPinWithoutAStateReturnsDomainRefusal()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}/2000-01-01--{new string('a', 64)}";

        var envelope = await ResolveAsync(mount, identifier);

        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("identifier_unknown", envelope.Refusal!.Code);
        Assert.AreEqual(identifier,
            envelope.Refusal.HelpfulPayload.GetProperty("requested_identifier").GetString());
    }

    [TestMethod]
    [DataRow("http")]
    [DataRow("userinfo")]
    [DataRow("port")]
    [DataRow("query")]
    [DataRow("fragment")]
    [DataRow("host")]
    [DataRow("segment-count")]
    [DataRow("publisher")]
    [DataRow("uppercase-digest")]
    [DataRow("invalid-date")]
    public async Task PinnedPermalinkGrammarRejectsEachNonCanonicalForm(string mutation)
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = mutation switch
        {
            "http" => "http://law.soufien.lu" + fixture.Permalink,
            "userinfo" => "https://user@law.soufien.lu" + fixture.Permalink,
            "port" => "https://law.soufien.lu:444" + fixture.Permalink,
            "query" => "https://law.soufien.lu" + fixture.Permalink + "?x=1",
            "fragment" => "https://law.soufien.lu" + fixture.Permalink + "#x",
            "host" => "https://evillaw.soufien.lu" + fixture.Permalink,
            "segment-count" => fixture.Permalink + "/extra",
            "publisher" => fixture.Permalink.Replace("/lu-legilux/", "/other/", StringComparison.Ordinal),
            "uppercase-digest" => fixture.StableCoordinate + "--" + fixture.StateSha256.ToUpperInvariant(),
            "invalid-date" => fixture.StableCoordinate.Replace(fixture.ApplicabilityDate, "2024-02-30", StringComparison.Ordinal) + "--" + fixture.StateSha256,
            _ => throw new AssertFailedException(mutation),
        };

        var envelope = await ResolveAsync(mount, identifier);

        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.IsNotNull(envelope.Refusal);
        Assert.IsFalse(envelope.Refusal.HelpfulPayload.TryGetProperty("current_digest", out _));
    }

    [TestMethod]
    public async Task ForeignOriginCannotPresentAPathAsACanonicalPinnedPermalink()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var context = Request("https://example.invalid" + fixture.Permalink);
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("identifier_unknown", envelope.Refusal!.Code);
        Assert.IsFalse(envelope.Refusal.HelpfulPayload.TryGetProperty("current_digest", out _));
        Assert.IsFalse(envelope.Refusal.HelpfulPayload.TryGetProperty("current_hash_pinned_url", out _));
    }

    [TestMethod]
    public async Task NormalizedPublisherTitleUsesR1WithoutSelectingAnExpression()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddWorkTitleAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var context = Request(fixture.WorkTitle.ToUpperInvariant() + "...");
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual("r1_work_discovery", envelope.Result!.Value.GetProperty("retrieval_lane").GetString());
        Assert.AreEqual("exact_normalized_title", envelope.Result.Value.GetProperty("match_reason").GetString());
        Assert.AreEqual(fixture.PublisherWid, envelope.Result.Value.GetProperty("work_identifier").GetString());
        CollectionAssert.Contains(
            envelope.Result.Value.GetProperty("expressions")
                .EnumerateArray().Select(static value => value.GetString()).ToArray(),
            fixture.ExpressionIri);
        Assert.IsFalse(envelope.Result.Value.TryGetProperty("expression_iri", out _),
            "R1 resolves a work and must not silently select one expression.");
    }

    [TestMethod]
    public async Task MissingTitleCapabilityRefusesR1WithoutGuessing()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var context = Request("ordinary unknown words");
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("retrieval_mode_unavailable", envelope.Refusal!.Code);
        Assert.AreEqual(
            "r1_work_discovery",
            envelope.Refusal.HelpfulPayload.GetProperty("requested_mode").GetString());
        CollectionAssert.AreEqual(
            new[] { "r0_exact_coordinate" },
            envelope.Refusal.HelpfulPayload.GetProperty("available_modes")
                .EnumerateArray().Select(static value => value.GetString()).ToArray());
    }

    [TestMethod]
    public async Task PunctuationOnlyQueryDoesNotMisreportPresentTitleCapabilityAsUnavailable()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddWorkTitleAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var context = Request("!!!");
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("identifier_unknown", envelope.Refusal!.Code);
        Assert.AreEqual("!!!", envelope.Refusal.HelpfulPayload.GetProperty("requested_identifier").GetString());
    }

    [TestMethod]
    public async Task PrefixMatchingMultipleWorksReturnsDeterministicAmbiguity()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var secondWork = await fixture.AddTwoWorkTitlesAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var context = Request("Reglement sur l'epreuve");
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("ambiguous_identifier", envelope.Refusal!.Code);
        Assert.AreEqual("prefix", envelope.Refusal.HelpfulPayload.GetProperty("match_reason").GetString());
        CollectionAssert.AreEqual(
            new[] { fixture.PublisherWid, secondWork }.Order(StringComparer.Ordinal).ToArray(),
            envelope.Refusal.HelpfulPayload.GetProperty("candidates")
                .EnumerateArray().Select(static value => value.GetString()).ToArray());
    }

    [TestMethod]
    public async Task AmbiguousCandidatesAreOrderedNewestFirst()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var secondWork = await fixture.AddTwoWorkTitlesAsync(equalDates: false);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var context = Request("Reglement sur l'epreuve");
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("ambiguous_identifier", envelope.Refusal!.Code);
        CollectionAssert.AreEqual(
            new[] { fixture.PublisherWid, secondWork },
            envelope.Refusal.HelpfulPayload.GetProperty("candidates")
                .EnumerateArray().Select(static value => value.GetString()).ToArray());
    }

    [TestMethod]
    public async Task OutOfOrderTitleWordsReachAllTokensContainedTier()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddWorkTitleAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var context = Request("terminale reglement");
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict);
        Assert.AreEqual("r1_work_discovery", envelope.Result!.Value.GetProperty("retrieval_lane").GetString());
        Assert.AreEqual("all_tokens_contained", envelope.Result.Value.GetProperty("match_reason").GetString());
        Assert.AreEqual(fixture.PublisherWid, envelope.Result.Value.GetProperty("work_identifier").GetString());
    }

    [TestMethod]
    [DataRow("{\"operation_id\":\"resolve\",\"parameters\":{}}")]
    [DataRow("{\"operation_id\":\"resolve\",\"parameters\":{\"identifier\":7}}")]
    [DataRow("{\"operation_id\":\"resolve\",\"parameters\":{\"identifier\":\" \\t\"}}")]
    public async Task UnusableIdentifierRemainsABelowEnvelopeTransportFailure(string body)
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var context = RequestBody(body);
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.AreEqual("application/problem+json", context.Response.ContentType);
        using var problem = System.Text.Json.JsonDocument.Parse(ResponseBytes(context));
        Assert.AreEqual("request_schema_invalid", problem.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(problem.RootElement.TryGetProperty("verdict", out _));
        Assert.IsFalse(problem.RootElement.TryGetProperty("refusal", out _));
        Assert.IsFalse(
            System.Text.Json.JsonSerializer.Serialize(problem.RootElement)
                .Contains("requested_identifier", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task MissingOrChangedMountInputsFailClosedBeforeAReaderIsReturned()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.CapabilityManifestFileName));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None));

        await fixture.RestoreCapabilityManifestAsync();
        var corpusPath = Path.Combine(fixture.Directory, V3CorpusMount.CorpusFileName);
        var corpusBytes = await File.ReadAllBytesAsync(corpusPath);
        corpusBytes[^2] ^= 0x01;
        await File.WriteAllBytesAsync(corpusPath, corpusBytes);
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None));

        await fixture.RestoreCorpusAsync();
        var indexPath = Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName);
        var bytes = await File.ReadAllBytesAsync(indexPath);
        bytes[^1] ^= 0xff;
        await File.WriteAllBytesAsync(indexPath, bytes);
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None));
    }

    [TestMethod]
    public async Task ValidCorpusFromAnotherBuildIsRejectedByTheIndexCorpusBinding()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var original = VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(fixture.CorpusBytes);
        var replacementProfiles = original.Set.ProfileIdentities.ToArray();
        replacementProfiles[0] = replacementProfiles[0] == new string('f', 64)
            ? new string('e', 64)
            : new string('f', 64);
        Array.Sort(replacementProfiles, StringComparer.Ordinal);
        var foreignBytes = LexCorpus6Builder.Write(original.Set with
        {
            ProfileIdentities = replacementProfiles,
        });
        var foreign = VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(foreignBytes);
        Assert.AreNotEqual(original.ArtifactRef, foreign.ArtifactRef);
        await File.WriteAllBytesAsync(
            Path.Combine(fixture.Directory, V3CorpusMount.CorpusFileName),
            foreignBytes);

        var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None));

        StringAssert.Contains(exception.Message, "does not bind the mounted corpus/6 artifact");
    }

    [TestMethod]
    public async Task WorkIdentifierMatchingMultipleExpressionsReturnsAllAmbiguousCandidates()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var alternateExpression = await fixture.AddAlternateExpressionForSameWorkAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        Assert.IsNotNull(fixture.PublisherWid);
        var context = Request(fixture.PublisherWid);
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable,
            new V3PlatformHost(),
            static () => ObservedAt,
            mount);

        await handler.HandleAsync(context, CancellationToken.None);

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("ambiguous_identifier", envelope.Refusal!.Code);
        Assert.AreEqual(
            fixture.PublisherWid,
            envelope.Refusal.HelpfulPayload.GetProperty("requested_identifier").GetString());
        var candidates = envelope.Refusal.HelpfulPayload.GetProperty("candidates")
            .EnumerateArray().Select(static value => value.GetString()).ToArray();
        CollectionAssert.AreEqual(
            new[] { fixture.ExpressionIri, alternateExpression }.Order(StringComparer.Ordinal).ToArray(),
            candidates);
    }

    [TestMethod]
    public async Task ExactIdentifierPresentInBothPublisherIndexesRefusesCrossPublisherSelection()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.BindPublisherWorkIdentifierAsync();
        var europeExpression = await fixture.AddEuropeCollisionAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await ResolveAsync(mount, fixture.PublisherWid);

        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict);
        Assert.AreEqual("ambiguous_identifier", envelope.Refusal!.Code);
        CollectionAssert.AreEqual(
            new[] { fixture.ExpressionIri, europeExpression }
                .Order(StringComparer.Ordinal).ToArray(),
            envelope.Refusal.HelpfulPayload.GetProperty("candidates")
                .EnumerateArray().Select(static value => value.GetString()).ToArray());
    }

    [TestMethod]
    public async Task MountedEuropeIndexServesExactWorkExpressionAndProvisionCoordinates()
    {
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var identifier in new[]
                 {
                     fixture.PublisherWorkId,
                     fixture.PublisherExpressionId,
                     fixture.PublisherProvisionIdentifier,
                 })
        {
            var envelope = await ResolveAsync(mount, identifier);
            Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, identifier);
            Assert.AreEqual(PublisherId.EuEurLex, envelope.Context.Publisher, identifier);
            Assert.AreEqual(TimelineSemantics.OfficialConsolidationState,
                envelope.Context.TimelineSemantics, identifier);
            Assert.AreEqual(fixture.CorpusSha256, envelope.Context.Snapshot.SnapshotSha256, identifier);
            Assert.AreEqual("eu-eurlex", envelope.Result!.Value.GetProperty("publisher").GetString());
            Assert.AreEqual(fixture.PublisherWorkId,
                envelope.Result.Value.GetProperty("publisher_work_id").GetString());
            Assert.AreEqual(fixture.PublisherExpressionId,
                envelope.Result.Value.GetProperty("expression_iri").GetString());
            Assert.AreEqual(fixture.IndexSha256,
                envelope.Result.Value.GetProperty("index_sha256").GetString());
        }
    }

    [TestMethod]
    public async Task PartialEuropeMountFailsClosed()
    {
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        File.Delete(Path.Combine(
            fixture.Directory, V3CorpusMount.EuropeCapabilityManifestFileName));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None));
    }

    [TestMethod]
    public async Task AbsentMountPreservesNoCorpusMountedRefusal()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-no-mount-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNull(mount);
            var context = Request("eli/example");
            var handler = new V3ApiHandler(
                SyntheticApiState.Unavailable,
                new V3PlatformHost(),
                static () => ObservedAt,
                mount);

            await handler.HandleAsync(context, CancellationToken.None);

            var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
            Assert.AreEqual("no_corpus_mounted", envelope.Refusal!.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static DefaultHttpContext Request(string identifier)
    {
        return RequestBody(
            "{\"operation_id\":\"resolve\",\"parameters\":{\"identifier\":" +
            System.Text.Json.JsonSerializer.Serialize(identifier) + "}}");
    }

    private static DefaultHttpContext RequestBody(string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-request";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = V3ResolveRestRoute.RawTarget;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static byte[] ResponseBytes(DefaultHttpContext context) =>
        ((MemoryStream)context.Response.Body).ToArray();

    private static async Task<V3Envelope> ResolveAsync(V3CorpusMount mount, string identifier)
    {
        var context = Request(identifier);
        var handler = new V3ApiHandler(
            SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }

    private sealed class MountedFixture : IAsyncDisposable
    {
        private readonly byte[] _capabilityManifestBytes;
        private readonly byte[] _corpusBytes;
        private readonly Stage3DerivationProfileEnvelope _envelope;

        private MountedFixture(
            string directory,
            string expressionIri,
            string publisherWid,
            string workTitle,
            string workKey,
            string applicabilityDate,
            string stateSha256,
            string corpusSha256,
            string indexSha256,
            byte[] capabilityManifestBytes,
            byte[] corpusBytes,
            Stage3DerivationProfileEnvelope envelope)
        {
            Directory = directory;
            ExpressionIri = expressionIri;
            PublisherWid = publisherWid;
            WorkTitle = workTitle;
            WorkKey = workKey;
            ApplicabilityDate = applicabilityDate;
            StateSha256 = stateSha256;
            CorpusSha256 = corpusSha256;
            IndexSha256 = indexSha256;
            _capabilityManifestBytes = capabilityManifestBytes;
            _corpusBytes = corpusBytes;
            _envelope = envelope;
        }

        public string Directory { get; }
        public string ExpressionIri { get; }
        public string PublisherWid { get; }
        public string WorkTitle { get; }
        public string WorkKey { get; }
        public string ApplicabilityDate { get; }
        public string StateSha256 { get; }
        public string StableCoordinate => $"/lu-legilux/{WorkKey}/{ApplicabilityDate}";
        public string Permalink => StableCoordinate + "--" + StateSha256;
        public string CorpusSha256 { get; }
        public string IndexSha256 { get; }
        public byte[] CorpusBytes => _corpusBytes.ToArray();

        public static async Task<MountedFixture> CreateAsync()
        {
            const string manifestation =
                "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml";
            const string item =
                "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/1991/08/10/n3/jo/fr/xml/eli-etat-leg-loi-1991-08-10-n3-jo-fr-xml.xml";
            var xml = await File.ReadAllBytesAsync(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "LuAknLegalContent",
                "loi-1991-08-10-n3--2024-02-01--fr.bin"));
            ICustodyStore store = new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
            var luxembourg = await LuxembourgGazetteAcquisitionTests
                .CompleteXmlForStage3BodyCompositionAsync(xml, store, manifestation, item);
            var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(
                luxembourgOverride: luxembourg,
                luxembourgStore: store);
            var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
            Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
            var index = LuxembourgIndexBuilder.TryBuild(envelope, out var indexRefusal, out var indexDetail);
            Assert.IsNotNull(index, $"{indexRefusal}: {indexDetail}");
            var article = envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation
                .Outcomes.First(static value => value.Article is not null).Article!;
            var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-corpus-mount-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(
                Path.Combine(directory, V3CorpusMount.IndexFileName),
                index.IndexBytes.ToArray());
            LuxembourgIndexBuilder.StateRow state;
            using (var connection = LuxembourgIndexBuilder.Open(
                       Path.Combine(directory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly))
            {
                state = ReadStates(connection).Single();
            }
            var capabilityBytes = index.CapabilityManifestBytes.ToArray();
            await File.WriteAllBytesAsync(
                Path.Combine(directory, V3CorpusMount.CapabilityManifestFileName),
                capabilityBytes);
            var corpusBytes = corpus.CanonicalBytes.ToArray();
            await File.WriteAllBytesAsync(
                Path.Combine(directory, V3CorpusMount.CorpusFileName),
                corpusBytes);
            return new MountedFixture(
                directory,
                article.PublisherExpressionIri,
                "fixture-work-identifier",
                "Règlement sur l'épreuve terminale",
                state.WorkKey,
                state.ApplicabilityDate,
                state.StateSha256,
                corpus.ArtifactRef.Sha256,
                index.IndexRef.Sha256,
                capabilityBytes,
                corpusBytes,
                envelope);
        }

        public async Task<string> AddEuropeCollisionAsync()
        {
            var built = EuropeIndexBuilder.TryBuild(
                _envelope, out var refusal, out var detail);
            Assert.IsNotNull(built, $"{refusal}: {detail}");
            var indexPath = Path.Combine(Directory, V3CorpusMount.EuropeIndexFileName);
            await File.WriteAllBytesAsync(indexPath, built.IndexBytes.ToArray());
            var expression = "http://publications.europa.eu/resource/cellar/cross-publisher-expression";
            EuropeIndexBuilder.MemberRow[] members;
            EuropeIndexBuilder.CorrigendumLineRow[] lines;
            EuropeIndexBuilder.CorrigendumGapRow[] gaps;
            EuropeIndexBuilder.ArticleRow[] articles;
            using (var connection = EuropeIndexBuilder.Open(indexPath, SqliteOpenMode.ReadWrite))
            {
                members = ReadEuropeMembers(connection);
                lines = ReadEuropeLines(connection);
                gaps = ReadEuropeGaps(connection);
                var member = members[0];
                var inserted = new EuropeIndexBuilder.ArticleRow(
                    new string('c', 64), member.ObjectRefSha256, PublisherWid, expression,
                    "collision.xml", "collision-provision", "Collision", "2024-02-01",
                    "eng", "collision", "[]");
                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO articles VALUES($identity,$object,$work,$expression,$entry,$identifier,$heading,$date,$language,$text,$tokens)";
                insert.Parameters.AddWithValue("$identity", inserted.ArticleIdentitySha256);
                insert.Parameters.AddWithValue("$object", inserted.ObjectRefSha256);
                insert.Parameters.AddWithValue("$work", inserted.PublisherWorkId);
                insert.Parameters.AddWithValue("$expression", inserted.PublisherExpressionId);
                insert.Parameters.AddWithValue("$entry", inserted.PackageEntry);
                insert.Parameters.AddWithValue("$identifier", inserted.PublisherIdentifier);
                insert.Parameters.AddWithValue("$heading", inserted.Heading);
                insert.Parameters.AddWithValue("$date", inserted.WordingDate);
                insert.Parameters.AddWithValue("$language", inserted.Language);
                insert.Parameters.AddWithValue("$text", inserted.SearchableText);
                insert.Parameters.AddWithValue("$tokens", inserted.TokensJson);
                Assert.AreEqual(1, insert.ExecuteNonQuery());
                articles = ReadEuropeArticles(connection);
                using var stamp = connection.CreateCommand();
                stamp.CommandText = "UPDATE stamp SET logical_rows_sha256=$logical WHERE stamp_id=1";
                stamp.Parameters.AddWithValue(
                    "$logical", EuropeIndexBuilder.HashLogicalRows(members, lines, gaps, articles));
                Assert.AreEqual(1, stamp.ExecuteNonQuery());
            }

            var indexBytes = await File.ReadAllBytesAsync(indexPath);
            var digest = Convert.ToHexStringLower(SHA256.HashData(indexBytes));
            var manifest = EuropeIndexBuilder.MeasureCapabilities(digest, articles);
            using var stream = new MemoryStream();
            _ = V3IndexCapabilityManifestArtifact.Write(stream, manifest);
            await File.WriteAllBytesAsync(
                Path.Combine(Directory, V3CorpusMount.EuropeCapabilityManifestFileName),
                stream.ToArray());
            return expression;
        }

        public async Task<string> AddAlternateExpressionForSameWorkAsync()
        {
            var indexPath = Path.Combine(Directory, V3CorpusMount.IndexFileName);
            LuxembourgIndexBuilder.ArticleRow source;
            LuxembourgIndexBuilder.MemberRow[] members;
            LuxembourgIndexBuilder.ArticleRow[] articles;
            LuxembourgIndexBuilder.StateRow[] states;
            LuxembourgIndexBuilder.WorkTitleRow[] titles;
            var alternateExpression = ExpressionIri + "/alternate-expression";
            using (var connection = LuxembourgIndexBuilder.Open(indexPath, SqliteOpenMode.ReadWrite))
            {
                source = ReadArticles(connection).First(value =>
                    string.Equals(value.ExpressionIri, ExpressionIri, StringComparison.Ordinal));
                using (var bindWork = connection.CreateCommand())
                {
                    bindWork.CommandText =
                        "UPDATE articles SET publisher_wid=$wid WHERE expression_iri=$expression";
                    bindWork.Parameters.AddWithValue("$wid", PublisherWid);
                    bindWork.Parameters.AddWithValue("$expression", ExpressionIri);
                    Assert.IsGreaterThan(0, bindWork.ExecuteNonQuery());
                }
                using (var insert = connection.CreateCommand())
                {
                    insert.CommandText = """
                        INSERT INTO articles VALUES(
                          $identity,$object,$expression,$publisher,$wid,$date,$language,$profile,$text,$tokens)
                        """;
                    insert.Parameters.AddWithValue("$identity", new string('f', 64));
                    insert.Parameters.AddWithValue("$object", source.ObjectRefSha256);
                    insert.Parameters.AddWithValue("$expression", alternateExpression);
                    insert.Parameters.AddWithValue("$publisher", source.PublisherId);
                    insert.Parameters.AddWithValue("$wid", PublisherWid);
                    insert.Parameters.AddWithValue("$date", (object?)source.ApplicabilityDate ?? DBNull.Value);
                    insert.Parameters.AddWithValue("$language", source.Language);
                    insert.Parameters.AddWithValue("$profile", source.RuleProfileSha256);
                    insert.Parameters.AddWithValue("$text", source.SearchableText);
                    insert.Parameters.AddWithValue("$tokens", source.TokensJson);
                    Assert.AreEqual(1, insert.ExecuteNonQuery());
                }

                members = ReadMembers(connection);
                articles = ReadArticles(connection);
                states = ReadStates(connection);
                titles = ReadWorkTitles(connection);
                using var stamp = connection.CreateCommand();
                stamp.CommandText = "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1";
                stamp.Parameters.AddWithValue(
                    "$digest", LuxembourgIndexBuilder.HashLogicalRows(members, articles, states, titles));
                Assert.AreEqual(1, stamp.ExecuteNonQuery());
            }

            var indexBytes = await File.ReadAllBytesAsync(indexPath);
            var indexDigest = Convert.ToHexStringLower(SHA256.HashData(indexBytes));
            var manifest = LuxembourgIndexBuilder.MeasureCapabilities(indexDigest, articles, titles);
            using var stream = new MemoryStream();
            _ = V3IndexCapabilityManifestArtifact.Write(stream, manifest);
            await File.WriteAllBytesAsync(
                Path.Combine(Directory, V3CorpusMount.CapabilityManifestFileName),
                stream.ToArray());
            return alternateExpression;
        }

        public async Task BindPublisherWorkIdentifierAsync()
        {
            var indexPath = Path.Combine(Directory, V3CorpusMount.IndexFileName);
            LuxembourgIndexBuilder.MemberRow[] members;
            LuxembourgIndexBuilder.ArticleRow[] articles;
            LuxembourgIndexBuilder.StateRow[] states;
            LuxembourgIndexBuilder.WorkTitleRow[] titles;
            using (var connection = LuxembourgIndexBuilder.Open(indexPath, SqliteOpenMode.ReadWrite))
            {
                using var bindWork = connection.CreateCommand();
                bindWork.CommandText =
                    "UPDATE articles SET publisher_wid=$wid WHERE expression_iri=$expression";
                bindWork.Parameters.AddWithValue("$wid", PublisherWid);
                bindWork.Parameters.AddWithValue("$expression", ExpressionIri);
                Assert.IsGreaterThan(0, bindWork.ExecuteNonQuery());
                members = ReadMembers(connection);
                articles = ReadArticles(connection);
                states = ReadStates(connection);
                titles = ReadWorkTitles(connection);
                using var stamp = connection.CreateCommand();
                stamp.CommandText =
                    "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1";
                stamp.Parameters.AddWithValue(
                    "$digest", LuxembourgIndexBuilder.HashLogicalRows(
                        members, articles, states, titles));
                Assert.AreEqual(1, stamp.ExecuteNonQuery());
            }

            var indexBytes = await File.ReadAllBytesAsync(indexPath);
            var indexDigest = Convert.ToHexStringLower(SHA256.HashData(indexBytes));
            var manifest = LuxembourgIndexBuilder.MeasureCapabilities(indexDigest, articles, titles);
            using var stream = new MemoryStream();
            _ = V3IndexCapabilityManifestArtifact.Write(stream, manifest);
            await File.WriteAllBytesAsync(
                Path.Combine(Directory, V3CorpusMount.CapabilityManifestFileName),
                stream.ToArray());
        }

        public async Task<LuxembourgIndexBuilder.StateRow> AddSecondLanguageStateAtSameDateAsync()
        {
            var indexPath = Path.Combine(Directory, V3CorpusMount.IndexFileName);
            LuxembourgIndexBuilder.MemberRow[] members;
            LuxembourgIndexBuilder.ArticleRow[] articles;
            LuxembourgIndexBuilder.StateRow[] states;
            LuxembourgIndexBuilder.WorkTitleRow[] titles;
            LuxembourgIndexBuilder.StateRow alternate;
            using (var connection = LuxembourgIndexBuilder.Open(indexPath, SqliteOpenMode.ReadWrite))
            {
                var sourceState = ReadStates(connection).Single();
                var sourceArticles = ReadArticles(connection)
                    .Where(article => sourceState.ArticleIdentitiesJson.Contains(
                        article.ArticleIdentitySha256, StringComparison.Ordinal)).ToArray();
                var expression = sourceState.PublisherLegalResourceIri + "/de";
                var identities = new List<string>();
                foreach (var source in sourceArticles)
                {
                    var identity = Convert.ToHexStringLower(SHA256.HashData(
                        Encoding.UTF8.GetBytes("de:" + source.ArticleIdentitySha256)));
                    identities.Add(identity);
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO articles VALUES($identity,$object,$expression,$publisher,$wid,$date,'deu',$profile,$text,$tokens)";
                    insert.Parameters.AddWithValue("$identity", identity);
                    insert.Parameters.AddWithValue("$object", source.ObjectRefSha256);
                    insert.Parameters.AddWithValue("$expression", expression);
                    insert.Parameters.AddWithValue("$publisher", source.PublisherId);
                    insert.Parameters.AddWithValue("$wid", (object?)source.PublisherWid ?? DBNull.Value);
                    insert.Parameters.AddWithValue("$date", (object?)source.ApplicabilityDate ?? DBNull.Value);
                    insert.Parameters.AddWithValue("$profile", source.RuleProfileSha256);
                    insert.Parameters.AddWithValue("$text", source.SearchableText);
                    insert.Parameters.AddWithValue("$tokens", source.TokensJson);
                    Assert.AreEqual(1, insert.ExecuteNonQuery());
                }
                identities.Sort(StringComparer.Ordinal);
                var profiles = System.Text.Json.JsonSerializer.Deserialize<string[]>(sourceState.RuleProfilesJson)!;
                var digest = LuxembourgIndexBuilder.StateSha256(
                    sourceState.WorkKey, sourceState.ApplicabilityDate, expression,
                    sourceState.PublisherWorkIri, sourceState.PublisherLegalResourceIri, "deu",
                    profiles, identities);
                alternate = new LuxembourgIndexBuilder.StateRow(
                    sourceState.WorkKey, sourceState.ApplicabilityDate, digest, expression,
                    sourceState.PublisherWorkIri, sourceState.PublisherLegalResourceIri, "deu",
                    sourceState.RuleProfilesJson, System.Text.Json.JsonSerializer.Serialize(identities));
                using (var insertState = connection.CreateCommand())
                {
                    insertState.CommandText = "INSERT INTO states VALUES($work,$date,$digest,$expression,$workIri,$resource,'deu',$profiles,$identities)";
                    insertState.Parameters.AddWithValue("$work", alternate.WorkKey);
                    insertState.Parameters.AddWithValue("$date", alternate.ApplicabilityDate);
                    insertState.Parameters.AddWithValue("$digest", alternate.StateSha256);
                    insertState.Parameters.AddWithValue("$expression", alternate.ExpressionIri);
                    insertState.Parameters.AddWithValue("$workIri", alternate.PublisherWorkIri);
                    insertState.Parameters.AddWithValue("$resource", alternate.PublisherLegalResourceIri);
                    insertState.Parameters.AddWithValue("$profiles", alternate.RuleProfilesJson);
                    insertState.Parameters.AddWithValue("$identities", alternate.ArticleIdentitiesJson);
                    Assert.AreEqual(1, insertState.ExecuteNonQuery());
                }
                members = ReadMembers(connection);
                articles = ReadArticles(connection);
                states = ReadStates(connection);
                titles = ReadWorkTitles(connection);
                using var stamp = connection.CreateCommand();
                stamp.CommandText = "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1";
                stamp.Parameters.AddWithValue(
                    "$digest", LuxembourgIndexBuilder.HashLogicalRows(members, articles, states, titles));
                Assert.AreEqual(1, stamp.ExecuteNonQuery());
            }

            var indexBytes = await File.ReadAllBytesAsync(indexPath);
            var indexDigest = Convert.ToHexStringLower(SHA256.HashData(indexBytes));
            var manifest = LuxembourgIndexBuilder.MeasureCapabilities(indexDigest, articles, titles);
            using var stream = new MemoryStream();
            _ = V3IndexCapabilityManifestArtifact.Write(stream, manifest);
            await File.WriteAllBytesAsync(
                Path.Combine(Directory, V3CorpusMount.CapabilityManifestFileName), stream.ToArray());
            return alternate;
        }

        public async Task AddWorkTitleAsync()
        {
            var indexPath = Path.Combine(Directory, V3CorpusMount.IndexFileName);
            LuxembourgIndexBuilder.MemberRow[] members;
            LuxembourgIndexBuilder.ArticleRow[] articles;
            LuxembourgIndexBuilder.StateRow[] states;
            LuxembourgIndexBuilder.WorkTitleRow[] titles;
            using (var connection = LuxembourgIndexBuilder.Open(indexPath, SqliteOpenMode.ReadWrite))
            {
                using (var bindWork = connection.CreateCommand())
                {
                    bindWork.CommandText =
                        "UPDATE articles SET publisher_wid=$wid WHERE expression_iri=$expression";
                    bindWork.Parameters.AddWithValue("$wid", PublisherWid);
                    bindWork.Parameters.AddWithValue("$expression", ExpressionIri);
                    Assert.IsGreaterThan(0, bindWork.ExecuteNonQuery());
                }
                using (var insert = connection.CreateCommand())
                {
                    insert.CommandText = "INSERT INTO work_titles VALUES($wid,$expression,$language,$title,$normalized,$date,'title',$evidence)";
                    insert.Parameters.AddWithValue("$wid", PublisherWid);
                    insert.Parameters.AddWithValue("$expression", ExpressionIri);
                    insert.Parameters.AddWithValue("$language", "fra");
                    insert.Parameters.AddWithValue("$title", WorkTitle);
                    insert.Parameters.AddWithValue("$normalized", LuxembourgIndexBuilder.NormalizeTitle(WorkTitle));
                    insert.Parameters.AddWithValue("$date", "2024-02-01");
                    insert.Parameters.AddWithValue("$evidence", new string('d', 64));
                    Assert.AreEqual(1, insert.ExecuteNonQuery());
                }

                members = ReadMembers(connection);
                articles = ReadArticles(connection);
                states = ReadStates(connection);
                titles = ReadWorkTitles(connection);
                using var stamp = connection.CreateCommand();
                stamp.CommandText = "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1";
                stamp.Parameters.AddWithValue(
                    "$digest", LuxembourgIndexBuilder.HashLogicalRows(members, articles, states, titles));
                Assert.AreEqual(1, stamp.ExecuteNonQuery());
            }

            var indexBytes = await File.ReadAllBytesAsync(indexPath);
            var indexDigest = Convert.ToHexStringLower(SHA256.HashData(indexBytes));
            var manifest = LuxembourgIndexBuilder.MeasureCapabilities(indexDigest, articles, titles);
            using var stream = new MemoryStream();
            _ = V3IndexCapabilityManifestArtifact.Write(stream, manifest);
            await File.WriteAllBytesAsync(
                Path.Combine(Directory, V3CorpusMount.CapabilityManifestFileName),
                stream.ToArray());
        }

        public async Task<string> AddTwoWorkTitlesAsync(bool equalDates = true)
        {
            const string secondWork = "fixture-work-identifier-two";
            var indexPath = Path.Combine(Directory, V3CorpusMount.IndexFileName);
            LuxembourgIndexBuilder.MemberRow[] members;
            LuxembourgIndexBuilder.ArticleRow[] articles;
            LuxembourgIndexBuilder.StateRow[] states;
            LuxembourgIndexBuilder.WorkTitleRow[] titles;
            using (var connection = LuxembourgIndexBuilder.Open(indexPath, SqliteOpenMode.ReadWrite))
            {
                var source = ReadArticles(connection).First(value =>
                    string.Equals(value.ExpressionIri, ExpressionIri, StringComparison.Ordinal));
                using (var bindWork = connection.CreateCommand())
                {
                    bindWork.CommandText =
                        "UPDATE articles SET publisher_wid=$wid WHERE expression_iri=$expression";
                    bindWork.Parameters.AddWithValue("$wid", PublisherWid);
                    bindWork.Parameters.AddWithValue("$expression", ExpressionIri);
                    Assert.IsGreaterThan(0, bindWork.ExecuteNonQuery());
                }
                using (var insertArticle = connection.CreateCommand())
                {
                    insertArticle.CommandText = "INSERT INTO articles VALUES($identity,$object,$expression,$publisher,$wid,$date,$language,$profile,$text,$tokens)";
                    insertArticle.Parameters.AddWithValue("$identity", new string('e', 64));
                    insertArticle.Parameters.AddWithValue("$object", source.ObjectRefSha256);
                    insertArticle.Parameters.AddWithValue("$expression", ExpressionIri + "/second-work");
                    insertArticle.Parameters.AddWithValue("$publisher", source.PublisherId);
                    insertArticle.Parameters.AddWithValue("$wid", secondWork);
                    insertArticle.Parameters.AddWithValue("$date", (object?)source.ApplicabilityDate ?? DBNull.Value);
                    insertArticle.Parameters.AddWithValue("$language", source.Language);
                    insertArticle.Parameters.AddWithValue("$profile", source.RuleProfileSha256);
                    insertArticle.Parameters.AddWithValue("$text", source.SearchableText);
                    insertArticle.Parameters.AddWithValue("$tokens", source.TokensJson);
                    Assert.AreEqual(1, insertArticle.ExecuteNonQuery());
                }
                foreach (var row in new[]
                {
                    (PublisherWid, ExpressionIri, WorkTitle + " zeta", "2024-02-01"),
                    (secondWork, ExpressionIri + "/second-work", WorkTitle + " alpha", equalDates ? "2024-02-01" : "2024-01-01"),
                })
                {
                    using var insertTitle = connection.CreateCommand();
                    insertTitle.CommandText = "INSERT INTO work_titles VALUES($wid,$expression,'fra',$title,$normalized,$date,'title',$evidence)";
                    insertTitle.Parameters.AddWithValue("$wid", row.Item1);
                    insertTitle.Parameters.AddWithValue("$expression", row.Item2);
                    insertTitle.Parameters.AddWithValue("$title", row.Item3);
                    insertTitle.Parameters.AddWithValue("$normalized", LuxembourgIndexBuilder.NormalizeTitle(row.Item3));
                    insertTitle.Parameters.AddWithValue("$date", row.Item4);
                    insertTitle.Parameters.AddWithValue(
                        "$evidence", row.Item1 == PublisherWid ? new string('d', 64) : new string('c', 64));
                    Assert.AreEqual(1, insertTitle.ExecuteNonQuery());
                }

                members = ReadMembers(connection);
                articles = ReadArticles(connection);
                states = ReadStates(connection);
                titles = ReadWorkTitles(connection);
                using var stamp = connection.CreateCommand();
                stamp.CommandText = "UPDATE stamp SET logical_rows_sha256=$digest WHERE stamp_id=1";
                stamp.Parameters.AddWithValue(
                    "$digest", LuxembourgIndexBuilder.HashLogicalRows(members, articles, states, titles));
                Assert.AreEqual(1, stamp.ExecuteNonQuery());
            }

            var indexBytes = await File.ReadAllBytesAsync(indexPath);
            var indexDigest = Convert.ToHexStringLower(SHA256.HashData(indexBytes));
            var manifest = LuxembourgIndexBuilder.MeasureCapabilities(indexDigest, articles, titles);
            using var stream = new MemoryStream();
            _ = V3IndexCapabilityManifestArtifact.Write(stream, manifest);
            await File.WriteAllBytesAsync(
                Path.Combine(Directory, V3CorpusMount.CapabilityManifestFileName),
                stream.ToArray());
            return secondWork;
        }

        private static LuxembourgIndexBuilder.MemberRow[] ReadMembers(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT object_ref_sha256,source_ordinal,outcome,rights_disposition,stage3_outcomes_json,gaps_json FROM members ORDER BY object_ref_sha256";
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexBuilder.MemberRow>();
            while (reader.Read()) values.Add(new(
                reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5)));
            return values.ToArray();
        }

        private static LuxembourgIndexBuilder.ArticleRow[] ReadArticles(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT article_identity_sha256,object_ref_sha256,expression_iri,publisher_id,publisher_wid,applicability_date,language,rule_profile_sha256,searchable_text,tokens_json FROM articles ORDER BY article_identity_sha256";
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexBuilder.ArticleRow>();
            while (reader.Read()) values.Add(new(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9)));
            return values.ToArray();
        }

        private static LuxembourgIndexBuilder.StateRow[] ReadStates(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT work_key,applicability_date,state_sha256,expression_iri,publisher_work_iri,publisher_legal_resource_iri,language,rule_profiles_json,article_identities_json FROM states ORDER BY work_key,applicability_date,expression_iri,language";
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexBuilder.StateRow>();
            while (reader.Read()) values.Add(new(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8)));
            return values.ToArray();
        }

        private static LuxembourgIndexBuilder.WorkTitleRow[] ReadWorkTitles(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT work_identifier,expression_iri,language,title,normalized_title,document_date,title_kind,evidence_sha256 FROM work_titles ORDER BY work_identifier,expression_iri,language,title,title_kind,evidence_sha256";
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexBuilder.WorkTitleRow>();
            while (reader.Read()) values.Add(new(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6),
                reader.GetString(7)));
            return values.ToArray();
        }

        private static EuropeIndexBuilder.MemberRow[] ReadEuropeMembers(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT object_ref_sha256,source_ordinal,outcome,content_class,stage3_outcomes_json,gaps_json FROM members ORDER BY object_ref_sha256";
            using var reader = command.ExecuteReader();
            var values = new List<EuropeIndexBuilder.MemberRow>();
            while (reader.Read()) values.Add(new(
                reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5)));
            return values.ToArray();
        }

        private static EuropeIndexBuilder.CorrigendumLineRow[] ReadEuropeLines(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT line_identity_sha256,family_key,corrected_work_root,corrigendum_work_root,publisher_expression_id,language_iri,reach,date_state,publisher_date_raw_lexical,publisher_date_datatype_iri,expression_content_sha256 FROM corrigendum_lines ORDER BY line_identity_sha256";
            using var reader = command.ExecuteReader();
            var values = new List<EuropeIndexBuilder.CorrigendumLineRow>();
            while (reader.Read()) values.Add(new(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetString(10)));
            return values.ToArray();
        }

        private static EuropeIndexBuilder.CorrigendumGapRow[] ReadEuropeGaps(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT gap_identity_sha256,family_key,work_root,reason FROM corrigendum_gaps ORDER BY gap_identity_sha256";
            using var reader = command.ExecuteReader();
            var values = new List<EuropeIndexBuilder.CorrigendumGapRow>();
            while (reader.Read()) values.Add(new(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            return values.ToArray();
        }

        private static EuropeIndexBuilder.ArticleRow[] ReadEuropeArticles(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT article_identity_sha256,object_ref_sha256,publisher_work_id,publisher_expression_id,package_entry,publisher_identifier,heading,wording_date,language,searchable_text,tokens_json FROM articles ORDER BY article_identity_sha256";
            using var reader = command.ExecuteReader();
            var values = new List<EuropeIndexBuilder.ArticleRow>();
            while (reader.Read()) values.Add(new(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), reader.GetString(9), reader.GetString(10)));
            return values.ToArray();
        }

        public Task RestoreCapabilityManifestAsync() => File.WriteAllBytesAsync(
            Path.Combine(Directory, V3CorpusMount.CapabilityManifestFileName),
            _capabilityManifestBytes);

        public Task RestoreCorpusAsync() => File.WriteAllBytesAsync(
            Path.Combine(Directory, V3CorpusMount.CorpusFileName),
            _corpusBytes);

        public ValueTask DisposeAsync()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class EuropeMountedFixture : IAsyncDisposable
    {
        private EuropeMountedFixture(
            string directory,
            string publisherWorkId,
            string publisherExpressionId,
            string publisherProvisionIdentifier,
            string corpusSha256,
            string indexSha256)
        {
            Directory = directory;
            PublisherWorkId = publisherWorkId;
            PublisherExpressionId = publisherExpressionId;
            PublisherProvisionIdentifier = publisherProvisionIdentifier;
            CorpusSha256 = corpusSha256;
            IndexSha256 = indexSha256;
        }

        public string Directory { get; }
        public string PublisherWorkId { get; }
        public string PublisherExpressionId { get; }
        public string PublisherProvisionIdentifier { get; }
        public string CorpusSha256 { get; }
        public string IndexSha256 { get; }

        public static async Task<EuropeMountedFixture> CreateAsync()
        {
            var envelope = await EuropeIndexBuilderTests.RetainedGdprEnvelopeAsync();
            var corpus = LexCorpus6Builder.TryBuild(
                envelope, out var corpusRefusal, out var corpusDetail);
            Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
            var index = EuropeIndexBuilder.TryBuild(
                envelope, out var indexRefusal, out var indexDetail);
            Assert.IsNotNull(index, $"{indexRefusal}: {indexDetail}");
            var admitted = envelope.BodyComposition.Envelope.FormexMainBodyLegalContent!.Outcomes
                .Single(static outcome =>
                    outcome.Disposition == EuFormexMainBodyLegalContentDisposition.Admitted);
            var directory = Path.Combine(
                Path.GetTempPath(), $"lex-v3-europe-mount-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(
                Path.Combine(directory, V3CorpusMount.EuropeIndexFileName),
                index.IndexBytes.ToArray());
            await File.WriteAllBytesAsync(
                Path.Combine(directory, V3CorpusMount.EuropeCapabilityManifestFileName),
                index.CapabilityManifestBytes.ToArray());
            await File.WriteAllBytesAsync(
                Path.Combine(directory, V3CorpusMount.CorpusFileName),
                corpus.CanonicalBytes.ToArray());
            return new EuropeMountedFixture(
                directory,
                admitted.Source.ExpressionIdentity.PublisherWorkId,
                admitted.Source.ExpressionIdentity.PublisherExpressionId,
                admitted.Articles[0].PublisherIdentifier,
                corpus.ArtifactRef.Sha256,
                index.IndexRef.Sha256);
        }

        public ValueTask DisposeAsync()
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
