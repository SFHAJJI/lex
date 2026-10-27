using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest.Names;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// R77 (the audit of 2026-10-27, gap 1), driven through the real handler on a verified mount: an act the publisher's
/// catalogue holds and the index holds no version of is answered <c>text_not_available</c> with its record, read from the
/// catalogue's facts in the index (its identity and address, type, dates by role, status in the publisher's word, Mémorial
/// issue, historical identifier, official PDF, title, versions and why no text of each is held), never
/// <c>identifier_unknown</c>: by the dossier, by reading, by resolve and by search's exact reference. An identifier the
/// catalogue does not name is still <c>identifier_unknown</c>; an act R92's names record withholds is <c>text_withheld</c>,
/// with no title.
/// </summary>
[TestClass]
public sealed class V3CorpusCatalogueRecordMountTests
{
    /// <summary>A second act of the fixture's day, which the fixture's catalogue names and its index holds no version of.</summary>
    private const string Work = "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n5";

    private const string Act = Work + "/jo";

    internal const string Key = "loi-1991-08-10-n5";

    private const string Pdf = "http://data.legilux.public.lu/filestore/eli/etat/leg/memorial/1991/a58/fr/pdf/eli-etat-leg-memorial-1991-a58-fr-pdf.pdf";

    private const string Title = "Loi du 10 août 1991 portant approbation de la Convention de La Haye";

    private const string Jolux = "http://data.legilux.public.lu/resource/";

    /// <summary>The catalogue's facts of the act, as run 15's catalogue states an act it licenses licenceSCL (or as given).</summary>
    internal static async Task AddCatalogueActAsync(MountedFixture fixture, string licence = Jolux + "authority/license/licenceSCL", string title = Title)
    {
        await fixture.AddWorkFactAsync(Act, "rdf:type", "resource_type", "iri", Jolux + "ontology/jolux#Act");
        await fixture.AddWorkFactAsync(Act, "typeDocument", "resource_type", "iri", Jolux + "authority/resource-type/LOI");
        await fixture.AddWorkFactAsync(Act, "dateDocument", "descriptive_date", "literal", "1991-08-10");
        await fixture.AddWorkFactAsync(Act, "publicationDate", "descriptive_date", "literal", "1991-08-23");
        await fixture.AddWorkFactAsync(Act, "dateEntryInForce", "act_force", "literal", "1991-08-27");
        await fixture.AddWorkFactAsync(Act, "dateNoLongerInForce", "act_force", "literal", "2010-01-01");
        await fixture.AddWorkFactAsync(Act, "inForceStatus", "act_force", "iri", Jolux + "authority/application-status/no-longer-in-force");
        await fixture.AddWorkFactAsync(Act, "isPartOf", "wemi_structural", "iri", "http://data.legilux.public.lu/eli/etat/leg/memorial/1991/a58");
        await fixture.AddWorkFactAsync(Act, "historicalLegalId", "act_identity", "literal", "1991A10580");
        await fixture.AddWorkFactAsync(Act, "isRealizedBy", "wemi_structural", "iri", Act + "/fr");
        await fixture.AddWorkFactAsync(Act + "/fr", "language", "expression_language_or_title", "iri", "http://publications.europa.eu/resource/authority/language/FRA");
        await fixture.AddWorkFactAsync(Act + "/fr", "title", "expression_language_or_title", "literal", title);
        await fixture.AddWorkFactAsync(Act + "/fr", "isEmbodiedBy", "wemi_structural", "iri", Act + "/fr/pdf");
        await fixture.AddWorkFactAsync(Act + "/fr/pdf", "userFormat", "manifestation_format", "iri", Jolux + "authority/user-format/pdf");
        await fixture.AddWorkFactAsync(Act + "/fr/pdf", "license", "rights_and_provenance", "iri", licence);
        await fixture.AddWorkFactAsync(Act + "/fr/pdf", "isExemplifiedBy", "wemi_structural", "iri", Pdf);
    }

    private static async Task<(MountedFixture Fixture, V3CorpusMount Mount)> MountAsync(Func<MountedFixture, Task> prepare)
    {
        var fixture = await MountedFixture.CreateAsync();
        await prepare(fixture);
        var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        return (fixture, mount);
    }

    private static Task<V3Envelope> AskAsync(V3CorpusMount mount, string operation, object parameters) =>
        EnvelopeAsync(mount, "/api/v3/" + operation, operation, parameters);

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(static value => value.GetString()!).ToArray();

    /// <summary>The record every answer about the act carries, held to the catalogue's facts.</summary>
    private static JsonElement Record(V3Envelope envelope, string asked)
    {
        Assert.AreEqual(V3Verdicts.Refuse, envelope.Verdict, asked);
        Assert.AreEqual("text_not_available", envelope.Refusal!.Code, $"{asked}: {envelope.Refusal.HelpfulPayload.GetRawText()}");
        Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher, asked);
        var payload = envelope.Refusal.HelpfulPayload;
        Assert.AreEqual("catalogue_only", payload.GetProperty("record_kind").GetString(), asked);
        Assert.AreEqual(Act, payload.GetProperty("official_identity").GetString(), $"{asked}: the act as published");
        Assert.AreEqual(Pdf, payload.GetProperty("official_source").GetString(), $"{asked}: the publisher's PDF of its French text");
        Assert.AreEqual("none", payload.GetProperty("retained_transport_evidence").GetString(), asked);
        Assert.AreEqual(Work, payload.GetProperty("work_iri").GetString(), asked);
        Assert.AreEqual(Key, payload.GetProperty("work_key").GetString(), asked);
        Assert.AreEqual("/lu-legilux/" + Key, payload.GetProperty("record_address").GetString(), asked);
        Assert.AreEqual(Act, payload.GetProperty("act_iri").GetString(), asked);
        // R21: each date the publisher's, under its own role.
        CollectionAssert.AreEqual(new[] { "1991-08-10" }, Strings(payload.GetProperty("date_document")), asked);
        CollectionAssert.AreEqual(new[] { "1991-08-23" }, Strings(payload.GetProperty("publication_date")), asked);
        CollectionAssert.AreEqual(new[] { "1991-08-27" }, Strings(payload.GetProperty("entry_into_force")), asked);
        CollectionAssert.AreEqual(new[] { "2010-01-01" }, Strings(payload.GetProperty("no_longer_in_force")), asked);
        CollectionAssert.AreEqual(new[] { Jolux + "authority/application-status/no-longer-in-force" }, Strings(payload.GetProperty("in_force_status")), asked);
        CollectionAssert.AreEqual(new[] { Jolux + "authority/resource-type/LOI" }, Strings(payload.GetProperty("document_type")), asked);
        CollectionAssert.AreEqual(new[] { "http://data.legilux.public.lu/eli/etat/leg/memorial/1991/a58" }, Strings(payload.GetProperty("memorial_issue")), asked);
        CollectionAssert.AreEqual(new[] { "1991A10580" }, Strings(payload.GetProperty("historical_legal_id")), asked);
        CollectionAssert.AreEqual(new[] { Pdf }, Strings(payload.GetProperty("official_pdf")), asked);
        CollectionAssert.AreEqual(new[] { Act }, Strings(payload.GetProperty("versions")), asked);
        CollectionAssert.AreEqual(new[] { "as_published" }, Strings(payload.GetProperty("version_kinds")), asked);
        Assert.AreEqual(payload.GetProperty("reason").GetString(), Strings(payload.GetProperty("version_reasons")).Single(), asked);
        Assert.IsGreaterThan(0, payload.GetProperty("record_evidence_sha256s").GetArrayLength(), asked);
        Assert.IsFalse(payload.GetProperty("asserts_absence_of_law").GetBoolean(), asked);
        StringAssert.StartsWith(payload.GetProperty("rule").GetString(), "R77: ", asked);
        StringAssert.Contains(payload.GetProperty("reason_rule").GetString(), "never why the acquisition did not fetch it", asked);
        // Every member a scalar or a list of scalars, as the refusal card reads every payload.
        foreach (var member in payload.EnumerateObject())
        {
            var values = member.Value.ValueKind == JsonValueKind.Array ? member.Value.EnumerateArray().ToArray() : [member.Value];
            Assert.IsTrue(values.All(static value => value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)), $"{asked}: {member.Name}");
        }

        return payload;
    }

    [TestMethod]
    public async Task AnActTheCatalogueHoldsAndTheIndexHoldsNoVersionOfIsAnsweredWithItsRecordNeverIdentifierUnknown()
    {
        var (fixture, mount) = await MountAsync(static fixture => AddCatalogueActAsync(fixture));
        await using var cleanup = fixture;
        using var mounted = mount;

        // The dossier by this site's address of the act and by the publisher's IRIs, reading, and resolve.
        foreach (var (operation, parameters) in new (string, object)[]
                 {
                     ("dossier", new { identifier = "/lu-legilux/" + Key }),
                     ("dossier", new { identifier = Work }),
                     ("dossier", new { identifier = Act }),
                     ("evidence_bundle", new { identifier = "/lu-legilux/" + Key, date = "2026-09-18", language = "fra" }),
                     ("timeline", new { identifier = "/lu-legilux/" + Key }),
                     ("resolve", new { identifier = Act }),
                 })
        {
            var payload = Record(await AskAsync(mount, operation, parameters), $"{operation} {JsonSerializer.Serialize(parameters)}");
            Assert.AreEqual("catalogue_licence_scl", payload.GetProperty("reason").GetString(), operation);
            Assert.AreEqual(Title, payload.GetProperty("title").GetString(), operation);
            Assert.AreEqual("fra", payload.GetProperty("title_language").GetString(), operation);
            CollectionAssert.AreEqual(new[] { "new_official_observation" }, Strings(payload.GetProperty("what_would_answer")), operation);
        }

        // Search's exact reference: the act's title words after its type and date name it among the day's acts.
        var search = await AskAsync(mount, "search", new { query = "loi du 10 août 1991 portant approbation de la Convention de La Haye", language = "fra" });
        var recognised = Record(search, "the reference");
        CollectionAssert.AreEqual(new[] { Work }, Strings(recognised.GetProperty("recognised_catalogue_works")));
        Assert.AreEqual(JsonValueKind.Null, recognised.GetProperty("recognised_held_act").ValueKind, "an act this site holds no version of");
        Record(await AskAsync(mount, "search", new { query = "/lu-legilux/" + Key, language = "fra" }), "this site's address of the act");

        // An identifier the catalogue names nothing under is still unknown.
        foreach (var unknown in new[] { "/lu-legilux/loi-1991-08-10-n9", "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n9", "http://data.legilux.public.lu/eli/etat/leg/memorial/1991/a58" })
        {
            Assert.AreEqual("identifier_unknown", (await AskAsync(mount, "dossier", new { identifier = unknown })).Refusal?.Code, unknown);
        }

        // The act the index holds is answered as before.
        Assert.IsNull((await AskAsync(mount, "dossier", new { identifier = $"/lu-legilux/{fixture.WorkKey}" })).Refusal);
    }

    [TestMethod]
    public async Task AnActLicensedCcByAndNotAcquiredSaysSoAndThatAnExpansionWouldAnswer()
    {
        var (fixture, mount) = await MountAsync(static fixture => AddCatalogueActAsync(fixture, "http://creativecommons.org/licenses/by/4.0/"));
        await using var cleanup = fixture;
        using var mounted = mount;
        var payload = Record(await AskAsync(mount, "dossier", new { identifier = "/lu-legilux/" + Key }), "cc by");
        Assert.AreEqual("catalogue_cc_by_not_acquired", payload.GetProperty("reason").GetString());
        CollectionAssert.AreEqual(new[] { "expanded_official_scope" }, Strings(payload.GetProperty("what_would_answer")));
    }

    [TestMethod]
    public async Task AnActTheNamesRecordWithholdsIsTextWithheldWithNoTitleAndNoRecord()
    {
        // R92 (#1001): a catalogue act whose title grants naturalisation to a named person (invented) is withheld by its class.
        const string naming = "Loi du 10 août 1991 conférant la naturalisation à Monsieur Jean Exemple";
        var (fixture, mount) = await MountAsync(static fixture => AddCatalogueActAsync(fixture, title: naming));
        await using var cleanup = fixture;
        mount.Dispose();
        var outcome = NamesMeasurement.Measure(fixture.Directory);
        var withheld = outcome.Withheld.Single(work => string.Equals(work.WorkIri, Work, StringComparison.Ordinal));
        Assert.IsNull(withheld.WorkKey, "a catalogue act has no version held, so no work key in the names record");
        NamesMeasurement.WriteFiles(outcome, fixture.Directory);
        using var guarded = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(guarded);

        foreach (var (operation, parameters) in new (string, object)[]
                 {
                     ("dossier", new { identifier = "/lu-legilux/" + Key }),
                     ("dossier", new { identifier = Work }),
                     ("evidence_bundle", new { identifier = "/lu-legilux/" + Key, date = "2026-09-18", language = "fra" }),
                     ("resolve", new { identifier = Act }),
                     ("search", new { query = "/lu-legilux/" + Key, language = "fra" }),
                 })
        {
            var envelope = await AskAsync(guarded, operation, parameters);
            Assert.AreEqual("text_withheld", envelope.Refusal?.Code, operation);
            var payload = envelope.Refusal!.HelpfulPayload;
            Assert.AreEqual(PrivateIndividuals.Reason, payload.GetProperty("reason").GetString(), operation);
            Assert.AreEqual(Work, payload.GetProperty("official_link").GetString(), operation);
            Assert.IsFalse(payload.TryGetProperty("record_kind", out _), $"{operation}: no record of a withheld act");
            Assert.IsFalse(payload.TryGetProperty("title", out _), $"{operation}: no title of a withheld act");
            var text = payload.GetRawText();
            Assert.IsFalse(text.Contains("Exemple", StringComparison.Ordinal) || text.Contains("conférant", StringComparison.Ordinal), $"{operation}: {text}");
        }

        // The day's acts listed: the withheld act's title is never shown.
        var listed = await AskAsync(guarded, "search", new { query = "loi du 10 août 1991 relative aux chiens", language = "fra" });
        Assert.AreEqual("ambiguous_identifier", listed.Refusal?.Code);
        Assert.IsFalse(listed.Refusal!.HelpfulPayload.GetRawText().Contains("Exemple", StringComparison.Ordinal));
    }
}
