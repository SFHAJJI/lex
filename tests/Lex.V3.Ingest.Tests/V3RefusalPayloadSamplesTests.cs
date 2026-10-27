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
/// The refusal census: every refusal code a served operation really produces, observed by driving the
/// real handler on fixture mounts, and the payload it really sends. The samples are written to
/// <c>schemas/v3-platform/refusal-payload-samples.json</c> so a reader of the platform's payloads can
/// be held against what the producer sends and not against a second declaration of what it should.
/// <c>produced</c> and <c>not_produced</c> partition the registry's twenty codes exactly; a code the
/// API constructs that no scenario here produces fails; a scenario that yields a different code from
/// the one it names fails. A regeneration writes the file and then fails, so it can never be mistaken
/// for a check.
/// </summary>
[TestClass]
public sealed class V3RefusalPayloadSamplesTests
{
    private const string RenderVariable = "V3_RENDER_REFUSAL_PAYLOAD_SAMPLES";
    private const string SamplesSchema = "lex-v3-refusal-payload-samples/1";

    private sealed record Observation(string Operation, string Scenario, string Code, JsonElement Payload);

    /// <summary>What the scenarios saw, and every scenario that did not yield the refusal it names: all of them, so one run says everything.</summary>
    private sealed class Census
    {
        public List<Observation> Observed { get; } = [];

        public List<string> Problems { get; } = [];
    }

    [TestMethod]
    public async Task EveryRefusalTheServedOperationsProduceHasARealSampleAndTheTwentyAreOnePartition()
    {
        var census = new Census();
        await ObserveLuxembourgAsync(census);
        await ObserveOneStateAsync(census);
        await ObserveRightsAsync(census);
        await ObserveNamesAsync(census);
        await ObserveUnmountedAsync(census);
        await ObserveReferenceListsAsync(census);
        await ObserveEuropeReferencesAsync(census);
        await ObserveOutsideNamedVersionAsync(census);
        await ObserveScopeAsync(census);
        await ObserveRateLimitedAsync(census);
        Assert.IsEmpty(census.Problems, "Scenarios that did not yield the refusal they name:\n" + string.Join("\n", census.Problems));
        var observed = census.Observed;

        // Each payload is held, independently of the copy the API embeds, to its code's published schema (the plan's item 6.10;
        // 36-ideal-evaluation.md §4: "Payload completeness: JSON-schema per refusal type; 100 percent").
        var schemaFailures = observed
            .Where(static observation => !PublishedPayloadSchema(observation.Code).Evaluate(observation.Payload).IsValid)
            .Select(static observation => $"{observation.Operation} / {observation.Scenario}: {observation.Code}")
            .ToArray();
        Assert.IsEmpty(schemaFailures, "Payloads their code's published schema refuses:\n" + string.Join("\n", schemaFailures));

        var registryCodes = V3OperationRegistry.Reviewed.RefusalCodes.Order(StringComparer.Ordinal).ToArray();
        Assert.AreEqual(20, registryCodes.Length);
        var producedCodes = observed.Select(static o => o.Code).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var notProduced = registryCodes.Except(producedCodes, StringComparer.Ordinal).ToArray();

        // The partition: every code is in exactly one list, and no list holds a code the registry does not.
        Assert.IsEmpty(producedCodes.Except(registryCodes, StringComparer.Ordinal).ToArray(), "A refusal code outside the registry was produced.");
        Assert.AreEqual(20, producedCodes.Length + notProduced.Length);
        Assert.IsEmpty(producedCodes.Intersect(notProduced, StringComparer.Ordinal).ToArray());
        Assert.IsGreaterThan(0, producedCodes.Length, "A census with nothing produced is not a census.");

        // The API's own source is the second witness: a code it constructs that no scenario here
        // produces has no sample, and that fails until a scenario exists.
        var constructed = CodesTheApiConstructs();
        CollectionAssert.AreEqual(
            constructed,
            producedCodes,
            "The codes the API constructs and the codes the scenarios produced are not the same set: add a scenario for the code with no sample, or explain the one no scenario can reach.");

        // Line endings are LF whatever the platform writes and whatever a working copy holds: the repository
        // checks files out with LF, and a Windows runner's newline must not make this comparison say so.
        var document = BuildDocument(observed, notProduced).Replace("\r\n", "\n", StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(document + "\n");
        var path = Path.Combine(RepositoryRoot(), "schemas", "v3-platform", "refusal-payload-samples.json");
        if (Environment.GetEnvironmentVariable(RenderVariable) == "1")
        {
            await File.WriteAllBytesAsync(path, bytes);
            Assert.Fail($"{RenderVariable} rendered {path} and did not verify it: run again without the variable, which is the only run that checks anything.");
        }

        Assert.IsTrue(File.Exists(path), "The samples file is missing: render it with " + RenderVariable + "=1 and commit it.");
        var held = Encoding.UTF8.GetBytes((await File.ReadAllTextAsync(path)).Replace("\r\n", "\n", StringComparison.Ordinal));
        CollectionAssert.AreEqual(bytes, held, "The samples file is not what the producers send now.");
    }

    [TestMethod]
    public async Task ARefusalScenarioThatYieldsAnotherCodeFailsNamingTheOperationAndTheScenario()
    {
        // The census is only as good as the scenarios it drives; a scenario that quietly produced a
        // different code would put a sample under the wrong name. This one asks for a refusal that the
        // fixture does not produce and shows the failure names both.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var census = new Census();

        await DriveAsync(census, mount, "as_of", "a date the fixture holds",
            new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" }, "no_version_for_date");

        var problem = census.Problems.Single();
        StringAssert.Contains(problem, "as_of");
        StringAssert.Contains(problem, "a date the fixture holds");
        StringAssert.Contains(problem, "no_version_for_date");
        Assert.IsEmpty(census.Observed);
    }

    private static async Task ObserveOneStateAsync(Census observed)
    {
        // The fixture as it is, one work with one state: a pinned coordinate whose digest is not the held
        // one is a mismatch here, and on a work with several states it is ambiguous, which is another refusal.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var wrongDigest = fixture.StateSha256[..^1] + (fixture.StateSha256[^1] == '0' ? '1' : '0');
        await DriveAsync(observed, mount, "resolve", "a pinned state whose digest is not the held one",
            new { identifier = fixture.PermalinkOf(wrongDigest) }, "pinned_digest_mismatch");
    }

    private static async Task ObserveLuxembourgAsync(Census observed)
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        // German is a second language of the work; two works carry a title that prefixes the query (a
        // plural title); one later French state has another rule-profile set; two French states share
        // one later date.
        await fixture.AddSecondLanguageStateAsync(fixture.ApplicabilityDate);
        await fixture.AddTwoWorkTitlesAsync();
        var laterDate = Shift(fixture.ApplicabilityDate, 400);
        var later = await fixture.AddStateAsync(laterDate, "later");
        await fixture.AddRuleProfileToStateAsync(later.ExpressionIri, new string('a', 64));
        var twinDate = Shift(fixture.ApplicabilityDate, 800);
        await fixture.AddStateAsync(twinDate, "twin-a");
        await fixture.AddStateAsync(twinDate, "twin-b");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var work = $"/lu-legilux/{fixture.WorkKey}";
        const string unknown = "/lu-legilux/no-such-work";
        const string european = "32016R0679";

        // An event cursor names its log; one from another log is not read against this one.
        await DriveAsync(observed, mount, "events", "a cursor from another log", new { after = new string('0', 64) + ":1" }, "snapshot_unknown");
        await DriveAsync(observed, mount, "answer_drift", "a cursor from another log", new { after = new string('0', 64) + ":1" }, "snapshot_unknown");
        const string beforeHistory = "1900-01-01";
        var date = fixture.ApplicabilityDate;

        // resolve
        await DriveAsync(observed, mount, "resolve", "an identifier no work has", new { identifier = unknown }, "identifier_unknown");
        await DriveAsync(observed, mount, "resolve", "a title two works carry", new { identifier = "Reglement sur l'epreuve" }, "ambiguous_identifier");

        // as_of
        await DriveAsync(observed, mount, "as_of", "two states on the date", new { identifier = work, date = twinDate, language = "fra" }, "ambiguous_version");
        await DriveAsync(observed, mount, "as_of", "a date before the history", new { identifier = work, date = beforeHistory, language = "fra" }, "no_version_for_date");
        await DriveAsync(observed, mount, "as_of", "a language not held", new { identifier = work, date, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "as_of", "an identifier no work has", new { identifier = unknown, date, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "as_of", "a European identifier on a Luxembourg-only mount", new { identifier = european, date, language = "fra" }, "retrieval_mode_unavailable");

        // timeline
        await DriveAsync(observed, mount, "timeline", "a language not held", new { identifier = work, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "timeline", "an identifier no work has", new { identifier = unknown, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "timeline", "a European identifier on a Luxembourg-only mount", new { identifier = european, language = "fra" }, "retrieval_mode_unavailable");

        // article_history
        await DriveAsync(observed, mount, "article_history", "an anchor the version does not hold", new { identifier = work, anchor = "art_no_such_anchor", language = "fra" }, "anchor_not_in_version");
        await DriveAsync(observed, mount, "article_history", "a language not held", new { identifier = work, anchor = "art_1", language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "article_history", "an identifier no work has", new { identifier = unknown, anchor = "art_1", language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "article_history", "a European identifier on a Luxembourg-only mount", new { identifier = european, anchor = "art_1", language = "fra" }, "retrieval_mode_unavailable");

        // diff
        await DriveAsync(observed, mount, "diff", "two states with different rule profiles", new { identifier = work, date_from = date, date_to = laterDate, language = "fra" }, "profiles_differ");
        await DriveAsync(observed, mount, "diff", "two states on the start date", new { identifier = work, date_from = twinDate, date_to = Shift(twinDate, 10), language = "fra" }, "ambiguous_version");
        await DriveAsync(observed, mount, "diff", "a start before the history", new { identifier = work, date_from = beforeHistory, date_to = date, language = "fra" }, "no_version_for_date");
        await DriveAsync(observed, mount, "diff", "a language not held", new { identifier = work, date_from = date, date_to = laterDate, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "diff", "an identifier no work has", new { identifier = unknown, date_from = date, date_to = laterDate, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "diff", "a European identifier on a Luxembourg-only mount", new { identifier = european, date_from = date, date_to = laterDate, language = "fra" }, "retrieval_mode_unavailable");

        // changes_in_period
        await DriveAsync(observed, mount, "changes_in_period", "a language not held", new { date_from = date, date_to = laterDate, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "changes_in_period", "an identifier no work has", new { identifier = unknown, date_from = date, date_to = laterDate, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "changes_in_period", "a European identifier on a Luxembourg-only mount", new { identifier = european, date_from = date, date_to = laterDate, language = "fra" }, "retrieval_mode_unavailable");

        // in_force_on
        await DriveAsync(observed, mount, "in_force_on", "two states on the date of a named work", new { identifier = work, date = twinDate, language = "fra" }, "ambiguous_version");
        await DriveAsync(observed, mount, "in_force_on", "a named work with no state that early", new { identifier = work, date = beforeHistory, language = "fra" }, "no_version_for_date");
        await DriveAsync(observed, mount, "in_force_on", "a language not held", new { date, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "in_force_on", "an identifier no work has", new { identifier = unknown, date, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "in_force_on", "a European identifier on a Luxembourg-only mount", new { identifier = european, date, language = "fra" }, "retrieval_mode_unavailable");

        // search
        await DriveAsync(observed, mount, "search", "two states on the date of a named work", new { query = "loyer", language = "fra", identifier = work, date = twinDate }, "ambiguous_version");
        await DriveAsync(observed, mount, "search", "a named work with no state that early", new { query = "loyer", language = "fra", identifier = work, date = beforeHistory }, "no_version_for_date");
        await DriveAsync(observed, mount, "search", "a language not held", new { query = "loyer", language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "search", "an identifier no work has", new { query = "loyer", language = "fra", identifier = unknown }, "identifier_unknown");
        await DriveAsync(observed, mount, "search", "a mode the index cannot serve", new { query = "loyer", language = "fra", mode = "bm25" }, "retrieval_mode_unavailable");
        // search's exact references (the plan's item 4.2): every refusal of the phase says what was recognised.
        await DriveAsync(observed, mount, "search", "an exact reference to an act this index does not hold: refused with no list", new { query = "/lu-legilux/no-such-work", language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "search", "a citation of an article the version the date selects does not hold", new { query = "article 99 de la loi du 10 août 1991, 15 mars 2024", language = "fra" }, "anchor_not_in_version");
        await DriveAsync(observed, mount, "search", "a citation at a date before the act's held history", new { query = "article 5 de la loi du 10 août 1991 au 1er janvier 1990", language = "fra" }, "no_version_for_date");
        await DriveAsync(observed, mount, "search", "a citation at a date two states of the act share", new { query = $"article 5 de la loi du 10 août 1991, {twinDate}", language = "fra" }, "ambiguous_version");
        await DriveAsync(observed, mount, "search", "a citation of an act in a language it is not held in", new { query = "article 5 de la loi du 10 août 1991, 15 mars 2024", language = "eng" }, "language_not_available");
        // search can answer one EU work, so on a mount with no EU index the missing corpus is the reason.
        await DriveAsync(observed, mount, "search", "a European identifier on a Luxembourg-only mount", new { query = "loyer", language = "fra", identifier = european }, "no_corpus_mounted");

        // provenance
        await DriveAsync(observed, mount, "provenance", "two states on the date", new { identifier = work, date = twinDate, language = "fra" }, "ambiguous_version");
        await DriveAsync(observed, mount, "provenance", "a date before the history", new { identifier = work, date = beforeHistory, language = "fra" }, "no_version_for_date");
        await DriveAsync(observed, mount, "provenance", "a language not held", new { identifier = work, date, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "provenance", "an identifier no work has", new { identifier = unknown, date, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "provenance", "a European identifier on a Luxembourg-only mount", new { identifier = european, date, language = "fra" }, "retrieval_mode_unavailable");

        // dossier
        await DriveAsync(observed, mount, "dossier", "a language not held", new { identifier = work, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "dossier", "an identifier no work has", new { identifier = unknown, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "dossier", "a European identifier on a Luxembourg-only mount", new { identifier = european, language = "fra" }, "no_corpus_mounted");

        // citation
        await DriveAsync(observed, mount, "citation", "two states on the date", new { identifier = work, date = twinDate, language = "fra" }, "ambiguous_version");
        await DriveAsync(observed, mount, "citation", "a date before the history", new { identifier = work, date = beforeHistory, language = "fra" }, "no_version_for_date");
        await DriveAsync(observed, mount, "citation", "a language not held", new { identifier = work, date, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "citation", "an identifier no work has", new { identifier = unknown, date, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "citation", "an anchor the version does not hold", new { identifier = work, date, anchor = "art_no_such_anchor", language = "fra" }, "anchor_not_in_version");
        await DriveAsync(observed, mount, "citation", "a European identifier on a Luxembourg-only mount", new { identifier = european, date, language = "fra" }, "retrieval_mode_unavailable");

        // cited_by
        await DriveAsync(observed, mount, "cited_by", "an identifier no work has", new { identifier = unknown }, "identifier_unknown");
        await DriveAsync(observed, mount, "cited_by", "a European identifier on a Luxembourg-only mount", new { identifier = european }, "retrieval_mode_unavailable");

        // verify
        await DriveAsync(observed, mount, "verify", "an identifier no work has", new { identifier = unknown }, "identifier_unknown");
        await DriveAsync(observed, mount, "verify", "a European identifier on a Luxembourg-only mount", new { identifier = european }, "retrieval_mode_unavailable");
        await DriveAsync(observed, mount, "verify", "a pinned permalink whose digest the coordinate no longer carries",
            new { identifier = fixture.PermalinkOf(new string('0', 64)), language = "fra" }, "pinned_digest_mismatch");
        await DriveAsync(observed, mount, "verify", "a pinned permalink at a coordinate two states of its language share, with a digest neither carries",
            new { identifier = $"/lu-legilux/{fixture.WorkKey}/{twinDate}--{new string('0', 64)}/fra" }, "ambiguous_identifier");
        await DriveAsync(observed, mount, "verify", "a language not held", new { identifier = $"/lu-legilux/{fixture.WorkKey}", language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "verify", "an article permalink naming an article the pinned state does not hold",
            new { identifier = $"{fixture.Permalink}/art_no_such_anchor", language = "fra" }, "anchor_not_in_version");

        // relations
        await DriveAsync(observed, mount, "relations", "an identifier no work has", new { identifier = unknown }, "identifier_unknown");
        await DriveAsync(observed, mount, "relations", "a European identifier on a Luxembourg-only mount", new { identifier = european }, "retrieval_mode_unavailable");
        await DriveAsync(observed, mount, "relations", "a date before the first state", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = "1900-01-01" }, "no_version_for_date");

        // evidence_bundle
        await DriveAsync(observed, mount, "evidence_bundle", "two states on the date", new { identifier = work, date = twinDate, language = "fra" }, "ambiguous_version");
        await DriveAsync(observed, mount, "evidence_bundle", "a date before the history", new { identifier = work, date = beforeHistory, language = "fra" }, "no_version_for_date");
        await DriveAsync(observed, mount, "evidence_bundle", "a language not held", new { identifier = work, date, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "evidence_bundle", "an identifier no work has", new { identifier = unknown, date, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "evidence_bundle", "an article the state does not hold", new { identifier = work, date, language = "fra", article = "art_15-9" }, "anchor_not_in_version");
        await DriveAsync(observed, mount, "evidence_bundle", "a page past the state's last", new { identifier = work, date, language = "fra", page = 2 }, "anchor_not_in_version");
        await DriveAsync(observed, mount, "evidence_bundle", "a European identifier on a Luxembourg-only mount", new { identifier = european, date, language = "fra" }, "retrieval_mode_unavailable");
        // At a hash-pinned permalink (R64), the reading page's own request: never another state in the pinned one's place.
        await DriveAsync(observed, mount, "evidence_bundle", "a pinned permalink whose digest the coordinate no longer carries",
            new { identifier = fixture.PermalinkOf(new string('0', 64)), date, language = "fra" }, "pinned_digest_mismatch");
        await DriveAsync(observed, mount, "evidence_bundle", "a pinned permalink at a coordinate two states of its language share, with a digest neither carries",
            new { identifier = $"/lu-legilux/{fixture.WorkKey}/{twinDate}--{new string('0', 64)}/fra", date = twinDate }, "ambiguous_identifier");
        await DriveAsync(observed, mount, "evidence_bundle", "a pinned permalink with another language asked beside it",
            new { identifier = fixture.Permalink, date, language = "deu" }, "language_not_available");
        await DriveAsync(observed, mount, "as_of", "a pinned permalink whose digest the coordinate no longer carries",
            new { identifier = fixture.PermalinkOf(new string('0', 64)), date, language = "fra" }, "pinned_digest_mismatch");

        // classification
        await DriveAsync(observed, mount, "classification", "a language not held", new { identifier = work, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "classification", "an identifier no work has", new { identifier = unknown, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "classification", "a European identifier on a Luxembourg-only mount", new { identifier = european, language = "fra" }, "retrieval_mode_unavailable");

        // manifestation
        await DriveAsync(observed, mount, "manifestation", "a format no manifestation of the work carries", new { identifier = work, language = "fra", format = "docx" }, "format_not_available");
        await DriveAsync(observed, mount, "manifestation", "a language not held", new { identifier = work, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "manifestation", "an identifier no work has", new { identifier = unknown, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "manifestation", "a European identifier on a Luxembourg-only mount", new { identifier = european, language = "fra" }, "retrieval_mode_unavailable");

        // status_on
        await DriveAsync(observed, mount, "status_on", "two states on the date", new { identifier = work, date = twinDate, language = "fra" }, "ambiguous_version");
        await DriveAsync(observed, mount, "status_on", "a date before the history", new { identifier = work, date = beforeHistory, language = "fra" }, "no_version_for_date");
        await DriveAsync(observed, mount, "status_on", "a language not held", new { identifier = work, date, language = "eng" }, "language_not_available");
        await DriveAsync(observed, mount, "status_on", "an identifier no work has", new { identifier = unknown, date, language = "fra" }, "identifier_unknown");
        await DriveAsync(observed, mount, "status_on", "a European identifier on a Luxembourg-only mount", new { identifier = european, date, language = "fra" }, "retrieval_mode_unavailable");

        // browse
        await DriveAsync(observed, mount, "browse", "a language not held", new { language = "eng" }, "language_not_available");

        // coverage
        await DriveAsync(observed, mount, "coverage", "a language not held", new { language = "eng" }, "language_not_available");
    }

    private static async Task ObserveOutsideNamedVersionAsync(Census observed)
    {
        // An exact reference whose ELI names one version (the act as published), at a date a later consolidation of the act
        // covers: index 9's rules serve the consolidation, which the ELI does not name, so no_version_for_date says
        // outside_named_version and names both (the review of #987: the web's card is held to this payload).
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddStateAsync("2025-01-01", "fr/xml", legalResourceLeaf: "consolide/20250101");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        await DriveAsync(observed, mount, "search", "an ELI naming the act as published, at a date a later consolidation covers",
            new { query = "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo, au 1er juin 2025", language = "fra" }, "no_version_for_date");
    }

    private static async Task ObserveRightsAsync(Census observed)
    {
        // The rights rule at compose time: a member the index records under any rights disposition but the
        // admitting one withholds the whole bundle; a state whose articles hold no text has none to serve.
        var withheld = await MountedFixture.CreateAsync();
        await using var cleanupWithheld = withheld;
        await withheld.SetMemberRightsDispositionAsync("non_admitting_licence_scl");
        using (var mount = await V3CorpusMount.OpenAsync(withheld.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            await DriveAsync(observed, mount, "evidence_bundle", "a member whose rights channels did not agree CC BY",
                new { identifier = $"/lu-legilux/{withheld.WorkKey}", date = withheld.ApplicabilityDate, language = "fra" }, "text_withheld");
            await DriveAsync(observed, mount, "evidence_bundle", "a pinned permalink to a state whose member's rights channels did not agree CC BY",
                new { identifier = withheld.Permalink, date = withheld.ApplicabilityDate, language = "fra" }, "text_withheld");
        }

        var empty = await MountedFixture.CreateAsync();
        await using var cleanupEmpty = empty;
        foreach (var (_, publisherId, _) in empty.ArticlesOfOwnState())
        {
            await empty.SetArticleTokensAsync(empty.ExpressionIri, publisherId, string.Empty, "[]");
        }

        using (var mount = await V3CorpusMount.OpenAsync(empty.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            await DriveAsync(observed, mount, "evidence_bundle", "a state whose articles hold no text",
                new { identifier = $"/lu-legilux/{empty.WorkKey}", date = empty.ApplicabilityDate, language = "fra" }, "text_not_available");
        }

        // R77: an act the publisher's catalogue holds, licensed licenceSCL, and the index holds no version of: its record.
        var catalogued = await MountedFixture.CreateAsync();
        await using var cleanupCatalogued = catalogued;
        await V3CorpusCatalogueRecordMountTests.AddCatalogueActAsync(catalogued);
        using (var mount = await V3CorpusMount.OpenAsync(catalogued.Directory, CancellationToken.None))
        {
            Assert.IsNotNull(mount);
            await DriveAsync(observed, mount, "dossier", "an act the publisher's catalogue holds and the index holds no version of (R77)",
                new { identifier = "/lu-legilux/" + V3CorpusCatalogueRecordMountTests.Key }, "text_not_available");
        }
    }

    private static async Task ObserveNamesAsync(Census observed)
    {
        // R92: an act whose served text names a person (synthetic, V3CorpusNamesMountTests), measured, with the names record
        // beside its mount: its bundle is text_withheld for the names it holds, with its identity and hashes.
        var fixture = await MountedFixture.CreateAsync(await V3CorpusNamesMountTests.ActNamingAPersonAsync());
        await using var cleanup = fixture;
        Lex.V3.Ingest.Names.NamesMeasurement.WriteFiles(Lex.V3.Ingest.Names.NamesMeasurement.Measure(fixture.Directory), fixture.Directory);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        await DriveAsync(observed, mount, "evidence_bundle", "an act the names record withholds (R92)",
            new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" }, "text_withheld");
    }

    private static async Task ObserveUnmountedAsync(Census observed)
    {
        // A mount that holds the European index and no Luxembourg one: every Luxembourg question is
        // no_corpus_mounted, said as a refusal and not as an error.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName));
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.CapabilityManifestFileName));
        await fixture.AddEuropeCollisionAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        const string work = "/lu-legilux/any-work";

        // resolve never says no_corpus_mounted: a title asked of a mount with no Luxembourg titles is a mode it cannot serve.
        await DriveAsync(observed, mount, "resolve", "a title asked of a mount without a Luxembourg index", new { identifier = "Reglement sur l'epreuve" }, "retrieval_mode_unavailable");
        await DriveAsync(observed, mount, "as_of", "no Luxembourg index", new { identifier = work, date = "2024-01-01" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "timeline", "no Luxembourg index", new { identifier = work }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "article_history", "no Luxembourg index", new { identifier = work, anchor = "art_1" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "diff", "no Luxembourg index", new { identifier = work, date_from = "2024-01-01", date_to = "2025-01-01" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "changes_in_period", "no Luxembourg index", new { date_from = "2024-01-01", date_to = "2025-01-01" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "in_force_on", "no Luxembourg index", new { date = "2024-01-01" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "search", "no Luxembourg index", new { query = "loyer", language = "fra" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "coverage", "no Luxembourg index", new { }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "provenance", "no Luxembourg index", new { identifier = work, date = "2024-01-01" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "dossier", "no Luxembourg index", new { identifier = work }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "citation", "no Luxembourg index", new { identifier = work, date = "2024-01-01" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "cited_by", "no Luxembourg index", new { identifier = work }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "verify", "no Luxembourg index", new { identifier = work }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "relations", "no Luxembourg index", new { identifier = work }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "evidence_bundle", "no Luxembourg index", new { identifier = work, date = "2024-01-01" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "classification", "no Luxembourg index", new { identifier = work }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "manifestation", "no Luxembourg index", new { identifier = work }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "status_on", "no Luxembourg index", new { identifier = work, date = "2024-01-01" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "browse", "no Luxembourg index", new { }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "ask", "no Luxembourg index", new { question = "Can I be fired while on sick leave?" }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "events", "no Luxembourg index", new { }, "no_corpus_mounted");
        await DriveAsync(observed, mount, "answer_drift", "no Luxembourg index", new { }, "no_corpus_mounted");
    }

    private static async Task ObserveReferenceListsAsync(Census observed)
    {
        // search's exact references (the review of #974, F2): a Mémorial issue the catalogue says publishes two acts, the
        // fixture's and one the index holds no version of, names both, each marked held or not, and opens neither.
        const string issue = "http://data.legilux.public.lu/eli/etat/leg/memorial/1991/a58";
        const string other = "http://data.legilux.public.lu/eli/etat/leg/rgd/1991/08/09/n1";
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddWorkFactAsync("http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo", "isPartOf", "wemi_structural", "iri", issue);
        await fixture.AddWorkFactAsync(other + "/jo", "isPartOf", "wemi_structural", "iri", issue);
        await fixture.AddWorkFactAsync(other + "/jo/fr", "title", "expression_language_or_title", "literal", "Règlement grand-ducal du 9 août 1991 relatif aux examens");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        await DriveAsync(observed, mount, "search", "a Mémorial issue that publishes two acts: each listed, held or not, none chosen",
            new { query = "Mém. A n° 58 de 1991", language = "fra" }, "ambiguous_identifier");
    }

    private static async Task ObserveEuropeReferencesAsync(Census observed)
    {
        // search's exact references to EU acts (the review of #974, F5): every refusal of the phase says what was recognised, and
        // the reader renders each. An act the EU index holds outside its census has one wording, which answers no date.
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using (fixture)
        {
            await fixture.AddSecondExpressionWithSamePublisherProvisionIdentifierAsync();
            using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            await DriveAsync(observed, mount, "search", "a citation at a date of an EU act this index holds outside its census",
                new { query = "32026R0002, 15 March 2026", language = "eng" }, "retrieval_mode_unavailable");
        }

        // The wording the date selects, held in no served language.
        var (root, directory) = await V3CorpusEuropeTimeViewTests.ConsolidatedWorksMountAsync(
            new EuFirstMountAcquisitionTests.ConsolidatedWorkSpec(
                V3CorpusEuropeTimeViewBranchTests.WorkB, V3CorpusEuropeTimeViewTests.ConsolidationDate, V3CorpusEuropeTimeViewBranchTests.Designated,
                null, V3CorpusEuropeTimeViewBranchTests.FrenchPackage));
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            await DriveAsync(observed, mount, "search", "a citation at a date whose EU wording is held in no served language",
                new { query = "Regulation (EU) 2016/679, 1 March 2025", language = "eng" }, "text_not_available");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A clock that does not move, so a client's minute of tokens stays spent while the census asks.</summary>
    private sealed class StoppedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ObservedAt;
    }

    private static async Task ObserveRateLimitedAsync(Census observed)
    {
        // The per-client limit (R109): one client's minute of API requests spent, every served operation then answers
        // rate_limited before the mount is asked, with 429 and Retry-After over REST. One handler and one limit for the whole
        // scenario, from one address outside the host.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount, null,
            new V3ClientLimiter(new StoppedClock(), static () => new byte[32]));
        for (var index = 0; index < V3ClientLimiter.ApiPerMinute; index++)
        {
            var spent = await PostAsync(handler, "/api/v3/coverage", JsonSerializer.Serialize(new { operation_id = "coverage", parameters = new { } }));
            Assert.AreEqual(StatusCodes.Status200OK, spent.Response.StatusCode, $"request {index + 1} of the client's minute");
        }

        var work = $"/lu-legilux/{fixture.WorkKey}";
        var date = fixture.ApplicabilityDate;
        foreach (var (operation, parameters) in new (string, object)[]
        {
            ("resolve", new { identifier = work }),
            ("as_of", new { identifier = work, date }),
            ("as_observed", new { identifier = work, date }),
            ("timeline", new { identifier = work }),
            ("article_history", new { identifier = work, anchor = "art_1" }),
            ("diff", new { identifier = work, date_from = date, date_to = date }),
            ("changes_in_period", new { date_from = date, date_to = date }),
            ("in_force_on", new { date }),
            ("search", new { query = "loyer", language = "fra" }),
            ("coverage", new { }),
            ("provenance", new { identifier = work, date }),
            ("dossier", new { identifier = work }),
            ("citation", new { identifier = work, date }),
            ("cited_by", new { identifier = work }),
            ("verify", new { identifier = work }),
            ("relations", new { identifier = work }),
            ("evidence_bundle", new { identifier = work, date }),
            ("classification", new { identifier = work }),
            ("manifestation", new { identifier = work }),
            ("status_on", new { identifier = work, date }),
            ("browse", new { }),
            ("ask", new { question = "Can I be fired while on sick leave?" }),
            ("events", new { }),
            ("answer_drift", new { }),
            // The three served from no data are limited alike: a request over the limit never reaches their refusal.
            ("concepts", new { identifier = work }),
            ("knowable_on", new { identifier = work, date }),
            ("transposition", new { identifier = work }),
        })
        {
            var label = $"{operation} / the client's minute of requests spent";
            var context = await PostAsync(handler, "/api/v3/" + operation, JsonSerializer.Serialize(new { operation_id = operation, parameters }));
            if (context.Response.StatusCode != StatusCodes.Status429TooManyRequests ||
                !string.Equals(context.Response.Headers.RetryAfter.ToString(), "1", StringComparison.Ordinal))
            {
                observed.Problems.Add($"{label}: expected 429 with Retry-After 1 and got HTTP {context.Response.StatusCode}, Retry-After '{context.Response.Headers.RetryAfter}': {Encoding.UTF8.GetString(ResponseBytes(context))}");
                continue;
            }

            var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
            if (envelope.Refusal is null || !string.Equals(envelope.Refusal.Code, "rate_limited", StringComparison.Ordinal))
            {
                observed.Problems.Add($"{label}: expected the refusal rate_limited and got {envelope.Refusal?.Code ?? envelope.Verdict}");
                continue;
            }

            observed.Observed.Add(new Observation(operation, "the client's minute of requests spent", "rate_limited", envelope.Refusal.HelpfulPayload.Clone()));
        }

        CollectionAssert.AreEquivalent(
            V3RestRouteBinding.Routed.Select(static binding => binding.OperationId).ToArray(),
            observed.Observed.Where(static o => o.Code == "rate_limited").Select(static o => o.Operation).ToArray(),
            "every routed operation is limited alike");
    }

    /// <summary>
    /// The two refusals given before the mount is read: the three operations served from no data, each refusing
    /// <c>retrieval_mode_unavailable</c> (the plan's item 8.6), and the instruments the out-of-scope list names, refused
    /// <c>out_of_corpus_scope</c> with their official place and who to ask (the plan's item 6.10; J5's circular).
    /// </summary>
    private static async Task ObserveScopeAsync(Census observed)
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var work = $"/lu-legilux/{fixture.WorkKey}";
        await DriveAsync(observed, mount, "concepts", "an operation served from no data, asked of a held work", new { identifier = work }, "retrieval_mode_unavailable");
        await DriveAsync(observed, mount, "knowable_on", "an operation served from no data, asked of a held work on its date", new { identifier = work, date = fixture.ApplicabilityDate }, "retrieval_mode_unavailable");
        await DriveAsync(observed, mount, "transposition", "an operation served from no data, asked of a held work", new { identifier = work }, "retrieval_mode_unavailable");
        await DriveAsync(observed, mount, "dossier", "a CSSF circular asked for by its name (J5)", new { identifier = "Circulaire CSSF 20/747" }, "out_of_corpus_scope");
        await DriveAsync(observed, mount, "resolve", "a decision of the Court of Justice by its ECLI", new { identifier = "ECLI:EU:C:2014:317" }, "out_of_corpus_scope");
        await DriveAsync(observed, mount, "as_of", "a CNPD decision on a date", new { identifier = "Délibération CNPD n° 12/2023", date = fixture.ApplicabilityDate }, "out_of_corpus_scope");
        await DriveAsync(observed, mount, "timeline", "a RESA publication", new { identifier = "RESA_2020_123.456" }, "out_of_corpus_scope");
    }

    /// <summary>A refusal code's payload schema as published (<c>schemas/v3-platform/{code}-payload.schema.json</c>), read from the file.</summary>
    private static Json.Schema.JsonSchema PublishedPayloadSchema(string code) =>
        Json.Schema.JsonSchema.FromText(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "schemas", "v3-platform", $"{code.Replace('_', '-')}-payload.schema.json")),
            new Json.Schema.BuildOptions { Dialect = Json.Schema.Dialect.Draft202012, SchemaRegistry = new Json.Schema.SchemaRegistry() });

    /// <summary>Drives one routed operation through the real handler and records the refusal, which must be the one named.</summary>
    private static async Task DriveAsync(
        Census census, V3CorpusMount mount, string operation, string scenario, object parameters, string expectedCode)
    {
        var body = JsonSerializer.Serialize(new { operation_id = operation, parameters });
        var context = await PostAsync(mount, "/api/v3/" + operation, body);
        var label = $"{operation} / {scenario}";
        if (context.Response.StatusCode != StatusCodes.Status200OK)
        {
            census.Problems.Add($"{label}: expected the refusal {expectedCode} and got HTTP {context.Response.StatusCode}: {Encoding.UTF8.GetString(ResponseBytes(context))}");
            return;
        }

        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        if (envelope.Refusal is null || !string.Equals(envelope.Refusal.Code, expectedCode, StringComparison.Ordinal))
        {
            census.Problems.Add($"{label}: expected the refusal {expectedCode} and got {envelope.Refusal?.Code ?? envelope.Verdict}");
            return;
        }

        census.Observed.Add(new Observation(operation, scenario, expectedCode, envelope.Refusal.HelpfulPayload.Clone()));
    }

    /// <summary>The refusal codes the API's source constructs, read from the source: a code built by a variable is not seen here.</summary>
    private static string[] CodesTheApiConstructs()
    {
        var api = Path.Combine(RepositoryRoot(), "src", "Lex.V3.Api");
        var pattern = new Regex(@"V3PlatformOperationRefusal\(\s*[A-Za-z_.]+,\s*""([a-z_]+)""", RegexOptions.CultureInvariant);
        // Recursive, with build output left out: a handler added in a subfolder must not be missed silently.
        var separators = new[] { Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar };
        return Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(file => !separators.Any(separator => file.Contains(separator, StringComparison.Ordinal)))
            .SelectMany(file => pattern.Matches(File.ReadAllText(file)).Select(static m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static string BuildDocument(List<Observation> observed, string[] notProduced)
    {
        var produced = new JsonArray();
        foreach (var group in observed.GroupBy(static o => o.Code, StringComparer.Ordinal).OrderBy(static g => g.Key, StringComparer.Ordinal))
        {
            var members = group.OrderBy(static o => o.Operation, StringComparer.Ordinal).ThenBy(static o => o.Scenario, StringComparer.Ordinal).ToArray();
            var keySets = members.Select(static o => o.Payload.EnumerateObject().Select(static p => p.Name).ToArray()).ToArray();
            var union = keySets.SelectMany(static k => k).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var everywhere = union.Where(key => keySets.All(keys => keys.Contains(key, StringComparer.Ordinal))).ToArray();
            // The richest payload seen is the sample: a reader held against it is held against every field any producer sends.
            var richest = members.OrderByDescending(static o => o.Payload.EnumerateObject().Count()).ThenBy(static o => o.Operation, StringComparer.Ordinal).First();
            // "A reader held against the samples is held against every field any producer sends" holds only while
            // every key is in a sample. When no one producer's payload holds every key another sends (search's exact
            // references add what was recognised where diff adds its bound), the richest payload that carries a key
            // still uncovered joins the samples (more_payloads), until every key is in one; a key in no sample fails.
            var more = new List<Observation>();
            var covered = richest.Payload.EnumerateObject().Select(static p => p.Name).ToHashSet(StringComparer.Ordinal);
            while (union.FirstOrDefault(key => !covered.Contains(key)) is { } uncovered)
            {
                var next = members
                    .Where(o => o.Payload.TryGetProperty(uncovered, out _))
                    .OrderByDescending(static o => o.Payload.EnumerateObject().Count())
                    .ThenBy(static o => o.Operation, StringComparer.Ordinal)
                    .ThenBy(static o => o.Scenario, StringComparer.Ordinal)
                    .First();
                more.Add(next);
                covered.UnionWith(next.Payload.EnumerateObject().Select(static p => p.Name));
            }

            var missing = union.Where(key => !covered.Contains(key)).ToArray();
            Assert.IsEmpty(missing, $"{group.Key}: {string.Join(", ", missing)} is in no sample; the census needs a sample per key set for this code.");
            var row = new JsonObject
            {
                ["code"] = group.Key,
                ["optional_payload_keys"] = new JsonArray(union.Except(everywhere, StringComparer.Ordinal).Select(static k => (JsonNode)k).ToArray()),
                ["payload"] = JsonNode.Parse(richest.Payload.GetRawText()),
                ["produced_by"] = new JsonArray(members.Select(static o => o.Operation).Distinct(StringComparer.Ordinal).Select(static o => (JsonNode)o).ToArray()),
            };
            if (more.Count > 0)
            {
                row["more_payloads"] = new JsonArray(more.Select(static o => JsonNode.Parse(o.Payload.GetRawText())).ToArray());
            }

            produced.Add(row);
        }

        var root = new JsonObject
        {
            ["not_produced"] = new JsonArray(notProduced.Select(static c => (JsonNode)c).ToArray()),
            ["produced"] = produced,
            ["publisher"] = "lu-legilux",
            ["schema"] = SamplesSchema,
        };
        return SortOrdinal(root).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonNode SortOrdinal(JsonNode node) => node switch
    {
        JsonObject value => new JsonObject(value
            .OrderBy(static member => member.Key, StringComparer.Ordinal)
            .Select(static member => KeyValuePair.Create(member.Key, member.Value is null ? null : SortOrdinal(member.Value)))),
        JsonArray value => new JsonArray(value.Select(static item => item is null ? null : SortOrdinal(item)).ToArray()),
        _ => node.DeepClone(),
    };

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static Task<DefaultHttpContext> PostAsync(V3CorpusMount mount, string rawTarget, string body) =>
        PostAsync(new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount), rawTarget, body);

    /// <summary>One POST through <paramref name="handler"/>, from an address outside the host, which the per-client limit counts.</summary>
    private static async Task<DefaultHttpContext> PostAsync(V3ApiHandler handler, string rawTarget, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "refusal-payload-samples";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.77");
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
        context.Response.Body = new MemoryStream();
        await handler.HandleAsync(context, CancellationToken.None);
        return context;
    }
}
