using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest;
using Microsoft.AspNetCore.Http;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// <c>manifestation</c> on a mounted corpus: the publisher's manifestations of the work's expressions
/// with their formats and items, read verbatim from the fact table and held against the rows; the one
/// this corpus retained is marked with the corpus manifest's body digest; a requested format filters,
/// and a format no manifestation carries is refused naming the ones held.
/// </summary>
[TestClass]
public sealed class V3CorpusManifestationMountTests
{
    private static string RawTarget => V3RestRouteBinding.Manifestation.RawTarget;

    private static string Token(string iri)
    {
        var trimmed = iri.TrimEnd('/');
        return trimmed[(trimmed.LastIndexOf('/') + 1)..];
    }

    [TestMethod]
    public void TheManifestationRouteIsTheServedBinding()
    {
        Assert.AreEqual("/api/v3/manifestation", RawTarget);
        Assert.AreEqual("manifestation", V3RestRouteBinding.Manifestation.OperationId);
        Assert.IsTrue(V3RestRouteBinding.Served.Contains(V3RestRouteBinding.Manifestation));
    }

    [TestMethod]
    public async Task TheManifestationsAreThePublishersWithTheRetainedOneMarkedAndItsBodyDigestTheCorpusManifests()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";
        var ground = ReadFacts(fixture);
        var corpus = VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(
            File.ReadAllBytes(Path.Combine(fixture.Directory, V3CorpusMount.CorpusFileName)));
        var member = corpus.Set.Members.Single(candidate => candidate.LuxembourgRights is not null);
        var selected = member.LuxembourgRights!.SelectedWemi;

        var envelope = await EnvelopeAsync(mount, RawTarget, "manifestation", new { identifier, language = "fra" });

        Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, envelope.Refusal?.Code);
        Assert.AreEqual("manifestation", envelope.Result!.ObjectType);
        var body = envelope.Result.Value;
        var expression = body.GetProperty("expressions").EnumerateArray().Single();
        Assert.AreEqual(fixture.ExpressionIri, expression.GetProperty("expression_iri").GetString());
        CollectionAssert.AreEqual(new[] { "fra" }, expression.GetProperty("languages").EnumerateArray().Select(static value => value.GetString()).ToArray());

        var expectedManifestations = ground
            .Where(row => row.Subject == fixture.ExpressionIri && row.Predicate == "isEmbodiedBy")
            .Select(static row => row.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.IsNotEmpty(expectedManifestations, "the real act's expression is embodied by at least one manifestation in the envelope.");
        var manifestations = expression.GetProperty("manifestations").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(expectedManifestations, manifestations.Select(static row => row.GetProperty("manifestation_iri").GetString()).ToArray());
        foreach (var manifestation in manifestations)
        {
            var iri = manifestation.GetProperty("manifestation_iri").GetString()!;
            CollectionAssert.AreEqual(
                ground.Where(row => row.Subject == iri && row.Predicate == "userFormat").Select(static row => row.Value).ToArray(),
                manifestation.GetProperty("formats").EnumerateArray().Select(static format => format.GetProperty("format_iri").GetString()).ToArray(),
                iri + ": the formats are the publisher's userFormat facts, in the table's order.");
            foreach (var format in manifestation.GetProperty("formats").EnumerateArray())
            {
                Assert.AreEqual(Token(format.GetProperty("format_iri").GetString()!), format.GetProperty("format").GetString());
                Assert.AreEqual(64, format.GetProperty("evidence_sha256").GetString()!.Length);
            }

            CollectionAssert.AreEqual(
                ground.Where(row => row.Subject == iri && row.Predicate == "isExemplifiedBy").Select(static row => row.Value).ToArray(),
                manifestation.GetProperty("items").EnumerateArray().Select(static item => item.GetProperty("item_iri").GetString()).ToArray(),
                iri + ": the items are the publisher's isExemplifiedBy facts.");

            var retained = string.Equals(iri, selected.ManifestationIri, StringComparison.Ordinal);
            Assert.AreEqual(retained, manifestation.GetProperty("retained").GetBoolean(), iri);
            CollectionAssert.AreEqual(
                retained ? new[] { member.BodySha256 } : Array.Empty<string?>(),
                manifestation.GetProperty("retained_body_sha256s").EnumerateArray().Select(static value => value.GetString()).ToArray(),
                iri + ": only the retained manifestation carries the corpus manifest's body digest.");
        }

        Assert.IsTrue(manifestations.Any(static row => row.GetProperty("retained").GetBoolean()), "the retained manifestation is one the publisher asserted.");
        var retainedRow = body.GetProperty("retained").EnumerateArray().Single();
        Assert.AreEqual(selected.ExpressionIri, retainedRow.GetProperty("expression_iri").GetString());
        Assert.AreEqual(selected.ManifestationIri, retainedRow.GetProperty("manifestation_iri").GetString());
        Assert.AreEqual(selected.FormatIri, retainedRow.GetProperty("format_iri").GetString());
        Assert.AreEqual(selected.ItemIri, retainedRow.GetProperty("item_iri").GetString());
        Assert.AreEqual(member.BodySha256, retainedRow.GetProperty("body_sha256").GetString());
        Assert.AreEqual(member.ObjectRefSha256, retainedRow.GetProperty("object_ref_sha256").GetString());

        CollectionAssert.AreEqual(
            ground.Where(row => expectedManifestations.Contains(row.Subject, StringComparer.Ordinal) && row.Predicate == "userFormat")
                .Select(static row => Token(row.Value)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            body.GetProperty("available_formats").EnumerateArray().Select(static value => value.GetString()).ToArray());
        CollectionAssert.Contains(
            body.GetProperty("available_formats").EnumerateArray().Select(static value => value.GetString()).ToArray(),
            Token(selected.FormatIri),
            "the retained format is among the formats the publisher asserted.");
        CollectionAssert.AreEqual(
            new[] { "manifestation_bytes", "authenticity", "format_semantics" },
            body.GetProperty("not_held").EnumerateArray().Select(static row => row.GetProperty("item").GetString()).ToArray());
    }

    [TestMethod]
    public async Task ARequestedFormatFiltersToItAndAFormatNoManifestationCarriesIsRefusedNamingTheOnesHeld()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var identifier = $"/lu-legilux/{fixture.WorkKey}";
        var corpus = VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(
            File.ReadAllBytes(Path.Combine(fixture.Directory, V3CorpusMount.CorpusFileName)));
        var format = Token(corpus.Set.Members.Single(candidate => candidate.LuxembourgRights is not null).LuxembourgRights!.SelectedWemi.FormatIri);

        var filtered = await EnvelopeAsync(mount, RawTarget, "manifestation", new { identifier, language = "fra", format });
        Assert.AreEqual(V3Verdicts.Answer, filtered.Verdict, filtered.Refusal?.Code);
        Assert.AreEqual(format, filtered.Result!.Value.GetProperty("requested_format").GetString());
        var manifestations = filtered.Result.Value.GetProperty("expressions").EnumerateArray().Single().GetProperty("manifestations").EnumerateArray().ToArray();
        Assert.IsNotEmpty(manifestations);
        Assert.IsTrue(manifestations.All(row => row.GetProperty("formats").EnumerateArray().Any(entry => entry.GetProperty("format").GetString() == format)),
            "every manifestation served carries the requested format.");

        var refused = await EnvelopeAsync(mount, RawTarget, "manifestation", new { identifier, language = "fra", format = "no-such-format" });
        Assert.AreEqual("format_not_available", refused.Refusal?.Code);
        var payload = refused.Refusal!.HelpfulPayload;
        Assert.AreEqual("no-such-format", payload.GetProperty("requested_format").GetString());
        CollectionAssert.AreEqual(
            filtered.Result.Value.GetProperty("available_formats").EnumerateArray().Select(static value => value.GetString()).ToArray(),
            payload.GetProperty("available_formats").EnumerateArray().Select(static value => value.GetString()).ToArray());
        CollectionAssert.Contains(payload.GetProperty("available_formats").EnumerateArray().Select(static value => value.GetString()).ToArray(), format);
    }

    [TestMethod]
    public async Task ItRefusesAsDossierRefuses()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var (parameters, expected) in new (object, string)[]
                 {
                     (new { identifier = "/lu-legilux/no-such-work", language = "fra" }, "identifier_unknown"),
                     (new { identifier = $"/lu-legilux/{fixture.WorkKey}", language = "eng" }, "language_not_available"),
                 })
        {
            var manifestation = await EnvelopeAsync(mount, RawTarget, "manifestation", parameters);
            var dossier = await EnvelopeAsync(mount, "/api/v3/dossier", "dossier", parameters);
            Assert.AreEqual(expected, manifestation.Refusal?.Code, JsonSerializer.Serialize(parameters));
            Assert.AreEqual(
                JsonSerializer.Serialize(dossier.Refusal!.HelpfulPayload),
                JsonSerializer.Serialize(manifestation.Refusal!.HelpfulPayload),
                expected + ": manifestation refuses with dossier's payload.");
        }
    }

    [TestMethod]
    public async Task ARequestWithoutAnIdentifierIsARequestSchemaFailure()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var context = await PostAsync(mount, RawTarget, JsonSerializer.Serialize(new { operation_id = "manifestation", parameters = new { format = "xml" } }));

        Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        using var problem = JsonDocument.Parse(ResponseBytes(context));
        Assert.AreEqual("request_schema_invalid", problem.RootElement.GetProperty("code").GetString());
    }
}
