using System.Text;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The work locator shared by the temporal operations, pinned where the carried mutants of the
/// <c>as_of</c> review found it unheld: the Legilux host boundary (U4) and the canonical spelling
/// of the stable work coordinate (T12), for <c>as_of</c>, <c>timeline</c> and <c>article_history</c>.
/// </summary>
[TestClass]
public sealed class V3CorpusLocatorTests
{
    private static readonly (string RawTarget, Func<string, string> Body)[] Operations =
    [
        ("/api/v3/as_of", identifier => JsonSerializer.Serialize(new { operation_id = "as_of", parameters = new { identifier, date = "2024-01-01" } })),
        ("/api/v3/timeline", identifier => JsonSerializer.Serialize(new { operation_id = "timeline", parameters = new { identifier } })),
        ("/api/v3/article_history", identifier => JsonSerializer.Serialize(new { operation_id = "article_history", parameters = new { identifier, anchor = "art_1" } })),
        ("/api/v3/diff", identifier => JsonSerializer.Serialize(new { operation_id = "diff", parameters = new { identifier, date_from = "2024-01-01", date_to = "2025-01-01" } })),
        ("/api/v3/changes_in_period", identifier => JsonSerializer.Serialize(new { operation_id = "changes_in_period", parameters = new { identifier, date_from = "2024-01-01", date_to = "2025-01-01" } })),
        ("/api/v3/in_force_on", identifier => JsonSerializer.Serialize(new { operation_id = "in_force_on", parameters = new { identifier, date = "2024-01-01" } })),
        ("/api/v3/search", identifier => JsonSerializer.Serialize(new { operation_id = "search", parameters = new { identifier, query = "bail", language = "fra" } })),
        ("/api/v3/provenance", identifier => JsonSerializer.Serialize(new { operation_id = "provenance", parameters = new { identifier, date = "2024-01-01" } })),
        ("/api/v3/dossier", identifier => JsonSerializer.Serialize(new { operation_id = "dossier", parameters = new { identifier } })),
        ("/api/v3/citation", identifier => JsonSerializer.Serialize(new { operation_id = "citation", parameters = new { identifier, date = "2024-01-01" } })),
    ];

    /// <summary>
    /// Served routes that take no identifier, so this suite's identifier-shaped questions do not apply
    /// to them, each with the suite that drives it. The suite is named with <c>nameof</c>, so a route
    /// listed here whose suite does not exist does not compile.
    /// </summary>
    private static readonly (string RawTarget, string DrivenBy)[] RoutesDrivenByTheirOwnSuites =
    [
        ("/api/v3/coverage", nameof(V3CorpusCoverageMountTests)),
    ];

    [TestMethod]
    public void EveryServedRouteIsOneThisSuiteExercises()
    {
        // A binding added to the served list with no route in the list above and none driven by its own
        // suite is one no test drives, and the handler's dispatch has a default arm that runs resolve for
        // it; the first request for it would be a server error. So the lists together are the served set
        // (resolve is driven by the locator tests themselves, and is the default arm's live path).
        var ownSuites = RoutesDrivenByTheirOwnSuites.Select(static route => route.RawTarget).ToArray();
        var served = V3RestRouteBinding.Served.Select(static binding => binding.RawTarget).Order(StringComparer.Ordinal).ToArray();
        var exercised = Operations.Select(static operation => operation.RawTarget)
            .Concat(ownSuites).Append("/api/v3/resolve").Order(StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(exercised, served);
        Assert.AreEqual(ownSuites.Length, ownSuites.Distinct(StringComparer.Ordinal).Count());
        Assert.IsFalse(ownSuites.Intersect(Operations.Select(static operation => operation.RawTarget), StringComparer.Ordinal).Any(),
            "A route is either driven here or by its own suite, not both.");
    }

    [TestMethod]
    public async Task TheLegiluxHostIsRecognisedOnADotBoundaryOnly()
    {
        var europe = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = europe;
        using var europeMount = await V3CorpusMount.OpenAsync(europe.Directory, CancellationToken.None);
        Assert.IsNotNull(europeMount);

        // The publisher's host and its subdomains name Luxembourg law: on a mount without the
        // Luxembourg index that is a corpus not mounted, with Luxembourg context.
        foreach (var identifier in new[]
                 {
                     "https://legilux.public.lu/eli/etat/leg/loi/2004/07/30/n1/jo",
                     "https://data.legilux.public.lu/eli/etat/leg/loi/2004/07/30/n1/jo",
                     "https://LEGILUX.public.lu/x",
                     // Not an ELI path, so only the host decides: a subdomain of the publisher is the publisher.
                     "https://data.legilux.public.lu/some/other/path",
                     "https://sparql.legilux.public.lu/",
                 })
        {
            foreach (var (rawTarget, body) in Operations)
            {
                var envelope = await PostEnvelopeAsync(europeMount, rawTarget, body(identifier));
                Assert.AreEqual("no_corpus_mounted", envelope.Refusal!.Code, $"{identifier} {rawTarget}");
                Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher, $"{identifier} {rawTarget}");
            }
        }

        // A host that merely ends in the publisher's letters is not the publisher: no shape, so
        // identifier_unknown with the shapeless rule, never Luxembourg law whose corpus is missing.
        foreach (var identifier in new[]
                 {
                     "https://notlegilux.public.lu/eli/etat/leg/loi/2004/07/30/n1/jo",
                     "https://legilux.public.lu.example/eli/etat/leg/loi/2004/07/30/n1/jo",
                     "https://public.lu/legilux.public.lu",
                 })
        {
            foreach (var (rawTarget, body) in Operations)
            {
                var envelope = await PostEnvelopeAsync(europeMount, rawTarget, body(identifier));
                Assert.AreEqual("identifier_unknown", envelope.Refusal!.Code, $"{identifier} {rawTarget}");
                StringAssert.Contains(envelope.Refusal.HelpfulPayload.GetProperty("what_would_answer").GetString(),
                    "no publisher shape", $"{identifier} {rawTarget}");
            }
        }
    }

    [TestMethod]
    public async Task TheStableWorkCoordinateIsAcceptedInItsCanonicalSpellingOnly()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var (rawTarget, body) in Operations)
        {
            var canonical = await PostEnvelopeAsync(mount, rawTarget, body($"/lu-legilux/{fixture.WorkKey}"));
            Assert.AreNotEqual("identifier_unknown", canonical.Refusal?.Code, rawTarget);
        }

        // The same two segments, spelled otherwise: an empty segment, a trailing slash, a doubled
        // leading slash, or the origin with a trailing slash. The grammar is one spelling.
        foreach (var identifier in new[]
                 {
                     $"/lu-legilux//{fixture.WorkKey}",
                     $"/lu-legilux/{fixture.WorkKey}/",
                     $"//lu-legilux/{fixture.WorkKey}",
                     $"/lu-legilux/{fixture.WorkKey}//",
                     $"https://law.soufien.lu/lu-legilux/{fixture.WorkKey}/",
                 })
        {
            foreach (var (rawTarget, body) in Operations)
            {
                var envelope = await PostEnvelopeAsync(mount, rawTarget, body(identifier));
                Assert.AreEqual("identifier_unknown", envelope.Refusal!.Code, $"{identifier} {rawTarget}");
                Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher, $"{identifier} {rawTarget}");
            }
        }
    }

    private static async Task<V3Envelope> PostEnvelopeAsync(V3CorpusMount mount, string rawTarget, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "mounted-corpus-locator";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        return V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
    }
}
