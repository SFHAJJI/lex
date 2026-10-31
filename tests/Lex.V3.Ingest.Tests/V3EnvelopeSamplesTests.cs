using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The envelope census: what a REST client really receives, whole, beside the contract a reader needs to
/// verify it without holding its own copy of the registry. The web's envelope reader is written against
/// this file, as the coverage reader is written against the answer census.
/// </summary>
/// <remarks>
/// <para>
/// Each envelope is driven through the real handler and passes <see cref="V3EnvelopeJson.ParseAndVerify"/>
/// before it is recorded. The fixture mints the corpus and index digests per run, so they are replaced by
/// fixed digests of the same shape (every occurrence, text-wise), which keeps every envelope readable by a
/// strict reader: a placeholder that is not a digest would be one a reader has to be taught to accept.
/// </para>
/// <para>
/// The contract block is read from the reviewed registry, not written here: the envelope schema and
/// version, the registry schema and digest, the refusal schema, the verdicts, every operation's result
/// schema and object types, and every refusal code with its mandatory payload fields.
/// </para>
/// </remarks>
[TestClass]
public sealed class V3EnvelopeSamplesTests
{
    private const string RenderVariable = "V3_RENDER_ENVELOPE_SAMPLES";
    private const string SamplesSchema = "lex-v3-envelope-samples/1";
    private static readonly string FixedCorpusDigest = new('a', 64);
    private static readonly string FixedIndexDigest = new('b', 64);
    private static readonly string FixedObjectRefDigest = new('c', 64);
    private static readonly string FixedReceiptDigest = new('d', 64);

    // The commit the samples' server names as running (R23): the server names its own build's, which moves with every commit, so
    // the samples name a fixed one of the same shape, as they name fixed digests.
    private static readonly string FixedServingCommit = new('e', 40);

    // The commit a mount written by this build names as its index's (R23, "index built from"): the build's own, which moves
    // with every commit, so it is replaced by a fixed one of the same shape wherever a derived mount's answer names it.
    private static readonly string FixedBuilderCommit = new('f', 40);

    [TestMethod]
    public async Task TheEnvelopeSamplesAreWhatTheHandlerSendsBesideTheReviewedContract()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        // The build report a derive writes beside its mount states the text yield per class (R86(4)), which coverage serves.
        await V3TextYieldTests.WriteYieldReportAsync(fixture);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        // A long act read by page (the plan's item 3.7): the fixture act with 60 articles of synthetic text, two pages.
        var longAct = await MountedFixture.CreateAsync(await SyntheticLongAct.DocumentAsync(60, textBytes: 120, lean: true));
        await using var cleanupLongAct = longAct;
        using var longActMount = await V3CorpusMount.OpenAsync(longAct.Directory, CancellationToken.None);
        Assert.IsNotNull(longActMount);

        // An EU mount (the GDPR's one held English wording), for the search screen's EU answers and refusals.
        // The GDPR's three dated statements as run 16 holds them: the EU dossier says the act's dates by role (the plan's item 2.1),
        // and its titles and Official Journal reference over the EU index /7 (Release 3's item 1).
        var europe = await EuropeMountedFixture.CreateAsync(withForceDates: true, withTitles: true);
        await using var cleanupEurope = europe;
        using var europeMount = await V3CorpusMount.OpenAsync(europe.Directory, CancellationToken.None);
        Assert.IsNotNull(europeMount);

        // The EU time view on a mount whose 2024-01-01 consolidation is held in French only (review of #972): the English
        // original wording's permalink, whose next wording's text this site does not hold in English. Captured first, so the
        // derived mount is gone before the rest is asked.
        JsonObject laterNotHeld;
        JsonObject laterNotHeldPastLast;
        JsonObject laterNotHeldVerified;
        var (branchRoot, branchDirectory) = await V3CorpusEuropeTimeViewTests.ConsolidatedWorksMountAsync(
            new EuFirstMountAcquisitionTests.ConsolidatedWorkSpec(
                V3CorpusEuropeTimeViewBranchTests.WorkB, V3CorpusEuropeTimeViewTests.ConsolidationDate, V3CorpusEuropeTimeViewBranchTests.Designated,
                null, V3CorpusEuropeTimeViewBranchTests.FrenchPackage));
        try
        {
            using var branchMount = await V3CorpusMount.OpenAsync(branchDirectory, CancellationToken.None);
            Assert.IsNotNull(branchMount);
            var (originalDate, originalPermalink) = await V3CorpusEuropeTimeViewTests.OriginalPinAsync(branchMount, "eng");
            const string scenario = "an EU wording's permalink whose next wording's text is not held in its language: an answer, the wording it pins";
            var parameters = new { identifier = originalPermalink, date = originalDate };
            // The derived mount's corpus and index digests move from run to run (its custody mints its references), so they
            // are read from a first answer and replaced in the one recorded, as every other mount's are.
            var probe = (await CaptureAsync(FixedCorpusDigest, FixedIndexDigest, branchMount, "evidence_bundle", scenario, parameters))["envelope"]!;
            laterNotHeld = await CaptureAsync(
                probe["context"]!["snapshot"]!["snapshot_sha256"]!.GetValue<string>(),
                probe["result"]!["value"]!["index_sha256"]!.GetValue<string>(),
                branchMount, "evidence_bundle", scenario, parameters);
            // Its custody records each body's receipt per run too.
            WithFixed(laterNotHeld["envelope"]!, "body_receipt_sha256", FixedReceiptDigest);
            // A page past the wording's last (the review of #978, F2): refused with the wording's pages and its language.
            laterNotHeldPastLast = await CaptureAsync(
                probe["context"]!["snapshot"]!["snapshot_sha256"]!.GetValue<string>(),
                probe["result"]!["value"]!["index_sha256"]!.GetValue<string>(),
                branchMount, "evidence_bundle", "the same EU wording's page 100, past its last: a refusal naming its pages",
                new { identifier = originalPermalink, date = originalDate, page = 100 });
            // A citation's evidence form is verified by both its hashes (R65): verify at the first provision's permalink names
            // the wording's digest and the provision text's, which the web's generator checks against the bundle above.
            laterNotHeldVerified = await CaptureAsync(
                probe["context"]!["snapshot"]!["snapshot_sha256"]!.GetValue<string>(),
                probe["result"]!["value"]!["index_sha256"]!.GetValue<string>(),
                branchMount, "verify", "an EU provision's permalink: digest_matches, with the provision's text digest",
                new { identifier = originalPermalink + "/001" });
        }
        finally
        {
            Directory.Delete(branchRoot, recursive: true);
        }

        // An EU act dated before 1 July 2013 (the owner's decision of 2026-10-18 on PR #975's question, O4): the same derived mount
        // with the original's Formex package dated 2013-06-26, a fixture's date (the day CRD IV and the CRR bear), so its bundle
        // says the dated authenticity sentence. The page that holds its last article, so the census carries one short page.
        JsonObject datedBefore;
        JsonObject europeSearchAll;
        JsonObject europeSearchOn;
        var (datedRoot, datedDirectory) = await V3CorpusEuropeTimeViewTests.OriginalDatedMountAsync("20130626",
            new EuFirstMountAcquisitionTests.ConsolidatedWorkSpec(
                V3CorpusEuropeTimeViewBranchTests.WorkB, V3CorpusEuropeTimeViewTests.ConsolidationDate, V3CorpusEuropeTimeViewBranchTests.Designated,
                V3CorpusEuropeTimeViewBranchTests.EnglishPackage, V3CorpusEuropeTimeViewBranchTests.FrenchPackage));
        try
        {
            using var datedMount = await V3CorpusMount.OpenAsync(datedDirectory, CancellationToken.None);
            Assert.IsNotNull(datedMount);
            const string scenario = "an EU act dated before 1 July 2013 (a fixture's date, 2013-06-26, in the GDPR's package), the page of its last article: an answer with the dated authenticity sentence";
            var parameters = new { identifier = V3CorpusEuropeTimeViewTests.GdprSeed, date = "2013-06-26", language = "eng", article = "099" };
            var probe = (await CaptureAsync(FixedCorpusDigest, FixedIndexDigest, datedMount, "evidence_bundle", scenario, parameters))["envelope"]!;
            datedBefore = await CaptureAsync(
                probe["context"]!["snapshot"]!["snapshot_sha256"]!.GetValue<string>(),
                probe["result"]!["value"]!["index_sha256"]!.GetValue<string>(),
                datedMount, "evidence_bundle", scenario, parameters);
            WithFixed(datedBefore["envelope"]!, "body_receipt_sha256", FixedReceiptDigest);

            // EU search over the act's time view (the audit of 2026-10-27, gap 5), on the same mount: with no date, both wordings
            // read and each unchanged article served once, under the consolidation, the original named beside it; with a date,
            // the one wording that date selects, pinned.
            europeSearchAll = await CaptureAsync(
                probe["context"]!["snapshot"]!["snapshot_sha256"]!.GetValue<string>(),
                probe["result"]!["value"]!["index_sha256"]!.GetValue<string>(),
                datedMount, "search", "an EU act's original wording and its consolidated version, no date, the strict lane alone: each article's text once, under the latest wording that holds it, each hit dated by its wording's role",
                new { query = "joint controllers", language = "eng", identifier = V3CorpusEuropeTimeViewTests.GdprSeed, mode = "strict" });
            europeSearchOn = await CaptureAsync(
                probe["context"]!["snapshot"]!["snapshot_sha256"]!.GetValue<string>(),
                probe["result"]!["value"]!["index_sha256"]!.GetValue<string>(),
                datedMount, "search", "the same EU act at a date before its consolidation, the strict lane alone: the one wording the date selects, pinned",
                new { query = "Joint Controllers", language = "eng", identifier = V3CorpusEuropeTimeViewTests.GdprSeed, mode = "strict", date = "2020-01-01" });
        }
        finally
        {
            Directory.Delete(datedRoot, recursive: true);
        }

        // An act the publisher's catalogue holds and the index holds no version of (R77; the audit of 2026-10-27, gap 1): the
        // fixture's day with a second act its catalogue names, licensed licenceSCL, which every page says from its record.
        var catalogued = await MountedFixture.CreateAsync();
        await using var cleanupCatalogued = catalogued;
        await V3CorpusCatalogueRecordMountTests.AddCatalogueActAsync(catalogued);
        using var cataloguedMount = await V3CorpusMount.OpenAsync(catalogued.Directory, CancellationToken.None);
        Assert.IsNotNull(cataloguedMount);
        const string catalogueAddress = "/lu-legilux/" + V3CorpusCatalogueRecordMountTests.Key;

        var envelopes = new JsonArray
        {
            await CaptureAsync(fixture, mount, "coverage", "the whole mount: an answer", new { }),
            await CaptureAsync(fixture, mount, "ask", "any question: the contained assistant's card under the point verdict", new { question = "Can I be fired while on sick leave?" }),
            // The request the Developers page shows the served release's own answer to (the plan's item 8.4: live examples from the
            // release's own data): the first held work, one row.
            await CaptureAsync(fixture, mount, "browse", "the first held work, one row: the answer the Developers page is rendered from", new { limit = 1 }),
            // An operation served from no data refuses with its typed reason inside the envelope (the plan's item 8.6).
            await CaptureAsync(fixture, mount, "concepts", "an operation served from no data: a refusal naming the mode asked, the modes served and the data that would serve it", new { identifier = $"/lu-legilux/{fixture.WorkKey}" }),
            // An instrument the out-of-scope list names (the plan's item 6.10; J5).
            await CaptureAsync(fixture, mount, "dossier", "a CSSF circular asked for by its name (J5): a refusal naming its official place and who to ask", new { identifier = "Circulaire CSSF 20/747" }),
            await CaptureAsync(fixture, null, "coverage", "no corpus mounted: a refusal", new { }),
            await CaptureAsync(fixture, mount, "search", "a language the mount holds no text in: a refusal with a payload", new { query = "loyer", language = "deu" }),
            await CaptureAsync(fixture, mount, "search", "a phrase the held text carries: an answer with hits in both lanes", new { query = "assemblée générale", language = "fra" }),
            await CaptureAsync(fixture, null, "search", "no corpus mounted: a refusal", new { query = "assemblée générale", language = "fra" }),
            // As the site asks every search (the plan's items 4.3 and 4.5): the strict lane alone, with hits and without.
            await CaptureAsync(fixture, mount, "search", "the phrase as the site asks it, the strict lane alone: an answer with its passages by act, each with its extract", new { query = "assemblée générale", language = "fra", mode = "strict" }),
            await CaptureAsync(fixture, mount, "search", "a word no held article carries, the strict lane alone: the answer the no-hit card is drawn from", new { query = "zéphyr", language = "fra", mode = "strict" }),
            // Capitals and accents never decide a match (the plan's item 4.4): the phrase in capitals and without its accents finds
            // the passages the phrase finds, each extract marking the text's own spelling.
            await CaptureAsync(fixture, mount, "search", "the phrase in capitals and without its accents, the strict lane alone: the passages the phrase finds, each marked as the text spells it", new { query = "ASSEMBLEE GENERALE", language = "fra", mode = "strict" }),
            await CaptureAsync(europe.CorpusSha256, europe.IndexSha256, europeMount, "search", "one EU work by its CELEX, a phrase its held wording carries: an answer whose hits carry the pinned wording's provision permalink", new { query = "joint controllers", language = "eng", identifier = "32016R0679" }),
            await CaptureAsync(europe.CorpusSha256, europe.IndexSha256, europeMount, "search", "one EU work in a language it is not held in: a refusal with a payload", new { query = "joint controllers", language = "fra", identifier = "32016R0679" }),
            await CaptureAsync(europe.CorpusSha256, europe.IndexSha256, europeMount, "search", "the EU work as the site asks it, the strict lane alone: an answer whose hits are the phrase's", new { query = "joint controllers", language = "eng", identifier = "32016R0679", mode = "strict" }),
            await CaptureAsync(europe.CorpusSha256, europe.IndexSha256, europeMount, "search", "a word the EU work's wording does not carry, the strict lane alone: the EU answer the no-hit card is drawn from", new { query = "zephyr", language = "eng", identifier = "32016R0679", mode = "strict" }),
            await CaptureAsync(europe.CorpusSha256, europe.IndexSha256, europeMount, "search", "an EU identifier the EU index does not hold: a refusal with a payload", new { query = "joint controllers", language = "eng", identifier = "32099R9999" }),
            await CaptureAsync(fixture, mount, "dossier", "the work, in the language it is held in: an answer", new { identifier = $"/lu-legilux/{fixture.WorkKey}", language = "fra" }),
            await CaptureAsync(fixture, mount, "dossier", "a work the index does not hold: a refusal with a payload", new { identifier = "/lu-legilux/no-such-work" }),
            await CaptureAsync(catalogued, cataloguedMount, "dossier", "an act the publisher's catalogue holds and the index holds no version of (R77): a refusal with its record", new { identifier = catalogueAddress }),
            await CaptureAsync(catalogued, cataloguedMount, "evidence_bundle", "the same act read at its address in French: a refusal with its record", new { identifier = catalogueAddress, date = catalogued.ApplicabilityDate, language = "fra" }),
            await CaptureAsync(catalogued, cataloguedMount, "search", "a reference naming the same act by the address this site gives it: a refusal with its record and what was recognised", new { query = catalogueAddress, language = "fra" }),
            await CaptureAsync(fixture, mount, "dossier", "a language the work is not held in: a refusal with a payload", new { identifier = $"/lu-legilux/{fixture.WorkKey}", language = "deu" }),
            await CaptureAsync(fixture, mount, "dossier", "an EU identifier on a mount without the EU index: a refusal with a payload", new { identifier = "32016R0679" }),
            await CaptureAsync(europe.CorpusSha256, europe.IndexSha256, europeMount, "dossier", "one EU work by its CELEX: an answer whose expression carries its pinned wording", new { identifier = "32016R0679" }),
            await CaptureAsync(fixture, null, "dossier", "no corpus mounted: a refusal", new { identifier = $"/lu-legilux/{fixture.WorkKey}" }),
            await CaptureAsync(fixture, mount, "evidence_bundle", "the work on its state's date, in the language it is held in: an answer", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate, language = "fra" }),
            await CaptureAsync(fixture, mount, "evidence_bundle", "a date before the work's history: a refusal with a payload", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = "1990-01-01" }),
            await CaptureAsync(fixture, mount, "evidence_bundle", "a work the index does not hold: a refusal with a payload", new { identifier = "/lu-legilux/no-such-work", date = fixture.ApplicabilityDate }),
            await CaptureAsync(fixture, mount, "evidence_bundle", "an EU identifier on a mount without the EU index: a refusal with a payload", new { identifier = "32016R0679", date = fixture.ApplicabilityDate }),
            await CaptureAsync(fixture, null, "evidence_bundle", "no corpus mounted: a refusal", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date = fixture.ApplicabilityDate }),
            // The reading at a permalink asks by the permalink itself (R64): the state it pins, or why not.
            await CaptureAsync(fixture, mount, "evidence_bundle", "a state's permalink at its own date: an answer, the state it pins", new { identifier = fixture.Permalink, date = fixture.ApplicabilityDate, language = "fra" }),
            await CaptureAsync(fixture, mount, "evidence_bundle", "a state's permalink whose digest is not the one held at its date: a refusal with the current link", new { identifier = fixture.PermalinkOf(new string('0', 64)), date = fixture.ApplicabilityDate, language = "fra" }),
            // Reading by article and by page (the plan's item 3.7): the page that holds an article, or a refusal naming the ids
            // nearest it; and a long act of synthetic text, read in two pages, the first with the state's contents, and a page
            // past its last refused with its pages (the review of #978, F2).
            await CaptureAsync(fixture, mount, "evidence_bundle", "a state's permalink and an article it does not hold: a refusal with the ids nearest it", new { identifier = fixture.Permalink, date = fixture.ApplicabilityDate, article = "art_15-9" }),
            await CaptureAsync(longAct, longActMount, "evidence_bundle", "a long act's state at its permalink, with its contents: an answer, its first page of two", new { identifier = longAct.Permalink, date = longAct.ApplicabilityDate, contents = true }),
            await CaptureAsync(longAct, longActMount, "evidence_bundle", "the same long act's second page: an answer, its last ten articles", new { identifier = longAct.Permalink, date = longAct.ApplicabilityDate, page = 2 }),
            await CaptureAsync(longAct, longActMount, "evidence_bundle", "the same long act's third page, past its last: a refusal naming its two pages", new { identifier = longAct.Permalink, date = longAct.ApplicabilityDate, page = 3 }),
            laterNotHeld,
            laterNotHeldPastLast,
            datedBefore,
            europeSearchAll,
            europeSearchOn,
            // A citation's evidence form is verified by both its hashes (R65): the version's, in its permalink, and the text's.
            await CaptureAsync(fixture, mount, "verify", "an article's permalink: digest_matches, with the article's text digest", new { identifier = fixture.Permalink + "/art_15" }),
            laterNotHeldVerified,
            await CaptureAsync(fixture, mount, "article_history", "one article of the work, in the language it is held in: an answer", new { identifier = $"/lu-legilux/{fixture.WorkKey}", anchor = "art_15", language = "fra" }),
            await CaptureAsync(fixture, mount, "article_history", "an article id no held state carries: a refusal with a payload", new { identifier = $"/lu-legilux/{fixture.WorkKey}", anchor = "art_44" }),
            await CaptureAsync(fixture, mount, "article_history", "a work the index does not hold: a refusal with a payload", new { identifier = "/lu-legilux/no-such-work", anchor = "art_15" }),
            await CaptureAsync(fixture, null, "article_history", "no corpus mounted: a refusal", new { identifier = $"/lu-legilux/{fixture.WorkKey}", anchor = "art_15" }),
            await CaptureAsync(fixture, mount, "diff", "the state on one date against the state on the same date: an answer", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date_from = fixture.ApplicabilityDate, date_to = fixture.ApplicabilityDate, language = "fra" }),
            await CaptureAsync(fixture, mount, "diff", "a from date before the work's history: a refusal with a payload", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date_from = "1990-01-01", date_to = fixture.ApplicabilityDate }),
            await CaptureAsync(fixture, mount, "diff", "a work the index does not hold: a refusal with a payload", new { identifier = "/lu-legilux/no-such-work", date_from = fixture.ApplicabilityDate, date_to = fixture.ApplicabilityDate }),
            await CaptureAsync(fixture, null, "diff", "no corpus mounted: a refusal", new { identifier = $"/lu-legilux/{fixture.WorkKey}", date_from = fixture.ApplicabilityDate, date_to = fixture.ApplicabilityDate }),
            await CaptureAsync(fixture, mount, "changes_in_period", "a window holding the work's first held state: an answer", new { date_from = fixture.ApplicabilityDate, date_to = fixture.ApplicabilityDate }),
            await CaptureAsync(fixture, mount, "changes_in_period", "a window before anything held: an answer with no row", new { date_from = "1990-01-01", date_to = "1990-12-31" }),
            await CaptureAsync(fixture, null, "changes_in_period", "no corpus mounted: a refusal", new { date_from = fixture.ApplicabilityDate, date_to = fixture.ApplicabilityDate }),
        };

        var document = new JsonObject
        {
            ["schema"] = SamplesSchema,
            ["note"] = "Whole REST envelopes the real handler sent, each verified by V3EnvelopeJson.ParseAndVerify before it was recorded, " +
                "and the reviewed registry's contract a reader needs to verify one. The fixture's per-run corpus and index digests are replaced " +
                $"by {FixedCorpusDigest[..8]}... and {FixedIndexDigest[..8]}... (same shape, every occurrence), and every object_ref_sha256 " +
                $"(the corpus's per-run reference to the object holding a source's bytes) by {FixedObjectRefDigest[..8]}...; in the envelopes from the two " +
                $"mounts derived from custody (the EU time view whose next wording's text is not held in English, and verify at its first provision; the EU act dated " +
                $"before 1 July 2013), every body_receipt_sha256 (that " +
                $"custody's per-run receipt) by {FixedReceiptDigest[..8]}...; nothing else is edited. " +
                "THE PRODUCER IS REAL AND THE MOUNT IS A FIXTURE.",
            ["contract"] = Contract(),
            ["envelopes"] = envelopes,
        };
        var text = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        var bytes = Encoding.UTF8.GetBytes(text);
        var path = Path.Combine(RepositoryRoot(), "schemas", "v3-platform", "envelope-samples.json");
        if (Environment.GetEnvironmentVariable(RenderVariable) == "1")
        {
            await File.WriteAllBytesAsync(path, bytes);
            Assert.Fail($"{RenderVariable} rendered {path} and did not verify it: run again without the variable, which is the only run that checks anything.");
        }

        Assert.IsTrue(File.Exists(path), "The envelope samples file is missing: render it with " + RenderVariable + "=1 and commit it.");
        var held = Encoding.UTF8.GetBytes((await File.ReadAllTextAsync(path)).Replace("\r\n", "\n", StringComparison.Ordinal));
        CollectionAssert.AreEqual(bytes, held, "The envelope samples file is not what the handler sends now.");
    }

    private static JsonObject Contract()
    {
        var registry = V3OperationRegistry.Reviewed;
        return new JsonObject
        {
            ["envelope_schema"] = V3EnvelopeBuilder.Schema,
            ["version"] = V3OperationRegistry.Version,
            ["registry_schema"] = V3OperationRegistry.Schema,
            ["registry_sha256"] = registry.Sha256,
            ["refusal_schema"] = V3OperationRegistry.RefusalSchema,
            ["verdicts"] = new JsonArray(V3Verdicts.All.Select(static verdict => (JsonNode)verdict).ToArray()),
            ["operations"] = new JsonArray(registry.Operations.Select(static operation => (JsonNode)new JsonObject
            {
                ["operation_id"] = operation.OperationId,
                ["result_schema"] = operation.ResultSchema,
                ["result_object_types"] = new JsonArray(operation.ResultObjectTypes.Select(static type => (JsonNode)type).ToArray()),
            }).ToArray()),
            ["refusals"] = new JsonArray(registry.RefusalCodes.Select(code => (JsonNode)new JsonObject
            {
                ["code"] = code,
                ["mandatory_payload_fields"] = new JsonArray(registry.MandatoryPayloadFields(code).Select(static field => (JsonNode)field).ToArray()),
                ["payload_schema"] = registry.RefusalPayloadSchema(code),
                ["payload_schema_sha256"] = registry.RefusalPayloadSchemaSha256(code),
            }).ToArray()),
            // The checked-in list of what Lex never holds (the plan's item 6.10), whose digest every out_of_corpus_scope names.
            ["out_of_scope"] = new JsonObject
            {
                ["schema"] = V3OutOfScope.Schema,
                ["sha256"] = V3OutOfScope.Sha256,
                ["classes"] = JsonNode.Parse(V3OutOfScope.CanonicalUtf8())!["classes"]!.DeepClone(),
            },
        };
    }

    private static Task<JsonObject> CaptureAsync(
        MountedFixture fixture, V3CorpusMount? mount, string operation, string scenario, object parameters) =>
        CaptureAsync(fixture.CorpusSha256, fixture.IndexSha256, mount, operation, scenario, parameters);

    private static async Task<JsonObject> CaptureAsync(
        string corpusSha256, string indexSha256, V3CorpusMount? mount, string operation, string scenario, object parameters)
    {
        var body = JsonSerializer.Serialize(new { operation_id = operation, parameters });
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "envelope-samples";
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = "/api/v3/" + operation;
        context.Response.Body = new MemoryStream();
        var handler = new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(FixedServingCommit), static () => ObservedAt, mount);
        await handler.HandleAsync(context, CancellationToken.None);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, $"{operation} / {scenario}");
        var sent = ((MemoryStream)context.Response.Body).ToArray();
        _ = V3EnvelopeJson.ParseAndVerify(sent, V3OperationRegistry.Reviewed);

        var text = Encoding.UTF8.GetString(sent)
            .Replace(corpusSha256, FixedCorpusDigest, StringComparison.Ordinal)
            .Replace(indexSha256, FixedIndexDigest, StringComparison.Ordinal)
            .Replace("corpus-" + corpusSha256[..16], "corpus-" + FixedCorpusDigest[..16], StringComparison.Ordinal)
            .Replace($"\"{AcquisitionJournal.CurrentSource()}\"", $"\"{FixedBuilderCommit}\"", StringComparison.Ordinal);
        return new JsonObject
        {
            ["operation"] = operation,
            ["scenario"] = scenario,
            ["http_status"] = context.Response.StatusCode,
            ["content_type"] = context.Response.ContentType,
            ["envelope"] = WithFixedObjectRefs(JsonNode.Parse(text)!),
        };
    }

    /// <summary>
    /// Replaces every <c>object_ref_sha256</c> value with the fixed digest: the corpus mints its object
    /// references per run, so the evidence bundle's sources would otherwise differ between two runs of
    /// the same fixture. A value that is not a digest is left as it is, so the verification still sees it.
    /// </summary>
    private static JsonNode WithFixedObjectRefs(JsonNode node) => WithFixed(node, "object_ref_sha256", FixedObjectRefDigest);

    /// <summary>Replaces every <paramref name="field"/> value that is a digest with <paramref name="fixedDigest"/>.</summary>
    private static JsonNode WithFixed(JsonNode node, string field, string fixedDigest)
    {
        switch (node)
        {
            case JsonObject value:
                foreach (var key in value.Select(static pair => pair.Key).ToArray())
                {
                    if (key == field && value[key] is JsonValue digest &&
                        digest.TryGetValue<string>(out var text) && text.Length == 64 && text.All(Uri.IsHexDigit))
                    {
                        value[key] = fixedDigest;
                    }
                    else if (value[key] is { } child)
                    {
                        WithFixed(child, field, fixedDigest);
                    }
                }

                break;
            case JsonArray items:
                foreach (var child in items)
                {
                    if (child is not null) WithFixed(child, field, fixedDigest);
                }

                break;
        }

        return node;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
