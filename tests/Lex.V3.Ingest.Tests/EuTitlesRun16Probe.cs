using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Release 3's item 1 measured: the EU index /7's titles and Official Journal references over EU run 16's custody, without a
/// derive. Never a CI test.
/// </summary>
/// <remarks>
/// <para>
/// OPT-IN AND READ-ONLY, like the force-dates and text-yield probes. It runs only with <c>LEX_EU_TITLES_PROBE=1</c>;
/// <c>LEX_EU_TITLES_PROBE_CUSTODY</c> names run 16's custody root (read in place through
/// <see cref="EuForceDatesRun16Probe.HeldOnlyCustody"/>, which never writes) and <c>LEX_EU_TITLES_PROBE_OUT</c> the summary
/// file written. Nothing goes over the network.
/// </para>
/// <para>
/// WHAT IT RUNS. The acquisition reopened exactly as the derive reopens it (<see cref="EuFirstMountAcquisition.ReopenAsync"/>),
/// which replays the query run from its checkpoint and holds the replay to the result digest the checkpoint stores, so a title
/// that entered that digest would refuse here; then the main-body producer the build runs, and the build's own projections of
/// the states, the titles and the references (<see cref="EuropeIndexBuilder.ProjectStates"/>,
/// <see cref="EuropeIndexBuilder.ProjectTitles"/>, <see cref="EuropeIndexBuilder.ProjectOfficialJournalReferences"/>), reopened
/// by the reader's own check (<see cref="EuropeIndexBuilder.ValidateTitleEvidence"/>). A main body is counted as the build
/// counts it when its disposition carries articles; the build also binds it to a corpus member, which every such body of run 16
/// has (the text-yield probe).
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class EuTitlesRun16Probe
{
    private const string EnableVariable = "LEX_EU_TITLES_PROBE";
    private const string CustodyVariable = "LEX_EU_TITLES_PROBE_CUSTODY";
    private const string OutVariable = "LEX_EU_TITLES_PROBE_OUT";

    [TestMethod]
    public async Task TheTitlesAndOfficialJournalReferencesOverRun16AreMeasured()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive("Set LEX_EU_TITLES_PROBE=1, LEX_EU_TITLES_PROBE_CUSTODY and LEX_EU_TITLES_PROBE_OUT for the read-only run-16 measurement.");
        }

        var root = Environment.GetEnvironmentVariable(CustodyVariable);
        var output = Environment.GetEnvironmentVariable(OutVariable);
        Assert.IsTrue(Directory.Exists(root), CustodyVariable + " names run 16's custody.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(output), OutVariable + " names the summary.");
        var clock = Stopwatch.StartNew();

        var store = new EuForceDatesRun16Probe.HeldOnlyCustody(FileSystemCustodyStore.WithBrotliCompression(root!));
        var pointer = JsonNode.Parse(File.ReadAllText(Directory.GetFiles(root!, "eu-acquisition-*.json").Single()))!;
        var catalogRef = new SourceArtifactRef(pointer["resource_id"]!.GetValue<string>(), pointer["sha256"]!.GetValue<string>());
        var catalog = JsonNode.Parse(Encoding.UTF8.GetString((await store.ReadByDigestAsync(catalogRef.Sha256, CancellationToken.None)).Span))!;
        var seeds = catalog["seeds"]!.AsArray().Select(static seed => seed!.GetValue<string>()).ToArray();
        var europe = await EuFirstMountAcquisition.ReopenAsync(store, catalogRef, seeds, CancellationToken.None);
        Assert.IsTrue(europe.Delivered, "The EU acquisition reopens, its query run held to the result digest its checkpoint stores.");
        var reopened = clock.Elapsed.TotalSeconds;
        var run = europe.Run!;
        var mainBody = await new EuFormexMainBodyLegalContentProducer(store).RunAsync(europe.Formex!.Reconciliation!, CancellationToken.None);

        // ---- The build's projections. ----
        var states = EuropeIndexBuilder.ProjectStates(run.ObservedWorkFacts);
        var carrying = mainBody.Outcomes.Where(static outcome => EuFormexMainBodyLegalContentProducer.CarriesArticles(outcome.Disposition) && outcome.Articles.Count > 0).ToArray();
        static string Language(string value) => value is "EN" or "ENG" ? "eng" : value is "FR" or "FRA" ? "fra" : throw new InvalidDataException(value);
        var publications = carrying.Where(static outcome => outcome.Publication is not null)
            .Select(outcome => new EuropeIndexBuilder.IndexedPublication(outcome.Source.ExpressionIdentity.PublisherWorkId,
                outcome.Source.ExpressionIdentity.PublisherExpressionId, Language(outcome.Articles[0].Language),
                (outcome.Source.AcquiredInventory ?? outcome.Source.RetainedInventory)!.SourceReceipt.Reference.ContentSha256, outcome.Publication!))
            .ToArray();
        var titles = EuropeIndexBuilder.ProjectTitles(run.ObservedExpressionTitles, states, publications);
        var references = EuropeIndexBuilder.ProjectOfficialJournalReferences(publications);
        var withArticles = carrying.Select(static outcome => outcome.Source.ExpressionIdentity.PublisherExpressionId).ToHashSet(StringComparer.Ordinal);
        EuropeIndexBuilder.ValidateTitleEvidence(titles, references, states, withArticles.Contains);

        // ---- Per work and per seed. ----
        var seedOf = states.GroupBy(static state => state.PublisherWorkIri, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Select(static state => state.SeedCelex).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var rootOf = states.Where(static state => state.DateStatus == EuropeIndexStateDateStatus.OriginalWording)
            .ToDictionary(static state => state.SeedCelex, static state => state.PublisherWorkIri, StringComparer.Ordinal);
        var cellar = titles.Where(static row => row.Source == EuropeTitleSources.CellarExpressionTitle).ToArray();
        bool WorkTitled(string work, string language) => cellar.Any(row => row.PublisherWorkIri == work && row.Language == language);
        var consolidated = states.Where(static state => state.DateStatus != EuropeIndexStateDateStatus.OriginalWording)
            .Select(static state => state.PublisherWorkIri).Distinct(StringComparer.Ordinal).ToArray();
        string ClassOf(IEnumerable<EuropeIndexOfficialJournalReference> rows)
        {
            var list = rows.ToArray();
            return list.Length == 0 ? "none" : list.Any(static row => row.NumberForm == EuOfficialJournalNumberForms.SeriesNumber) ? "series_number" : "act_number";
        }

        var perSeed = rootOf.Keys.Order(StringComparer.Ordinal).Select(seed =>
        {
            var original = references.Where(row => row.PublisherWorkIri == rootOf[seed]).ToArray();
            var ofSeed = references.Where(row => seedOf.TryGetValue(row.PublisherWorkIri, out var held) && held.Contains(seed)).ToArray();
            var cls = original.Length > 0 ? ClassOf(original) : ClassOf(ofSeed);
            return (Seed: seed, Class: cls, FromOriginal: original.Length > 0);
        }).ToArray();

        static JsonObject Counted(IEnumerable<string> values) =>
            new(values.GroupBy(static value => value, StringComparer.Ordinal).OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(static group => KeyValuePair.Create(group.Key, (JsonNode?)group.Count())));
        static JsonArray Strings(IEnumerable<string> values) => new([.. values.Select(static value => (JsonNode?)value)]);
        var summary = new JsonObject
        {
            ["schema"] = "lex-v3-eu-titles-run16-probe/1",
            ["catalog_sha256"] = catalogRef.Sha256,
            ["query_checkpoint_sha256"] = catalog["query"]!["sha256"]!.GetValue<string>(),
            ["query_checkpoint_reopened"] = true,
            ["census_works"] = states.Select(static state => state.PublisherWorkIri).Distinct(StringComparer.Ordinal).Count(),
            ["seeds"] = rootOf.Count,
            ["consolidated_works"] = consolidated.Length,
            ["observed_titles"] = run.ObservedExpressionTitles.Count,
            ["observed_titles_by_predicate"] = Counted(run.ObservedExpressionTitles.Select(static title => title.PredicateIri[(title.PredicateIri.IndexOf('#') + 1)..])),
            ["observed_titles_by_language"] = Counted(run.ObservedExpressionTitles.Select(static title => title.LanguageIri[(title.LanguageIri.LastIndexOf('/') + 1)..])),
            ["observed_titles_with_language_tag"] = run.ObservedExpressionTitles.Count(static title => title.RawLanguageTag is not null),
            ["title_rows_by_source"] = Counted(titles.Select(static row => row.Source)),
            ["title_rows_by_source_and_language"] = Counted(titles.Select(static row => $"{row.Source} {row.Language}")),
            ["expressions_with_publishers_title"] = cellar.Select(static row => row.PublisherExpressionIri).Distinct(StringComparer.Ordinal).Count(),
            ["census_works_with_english_title"] = seedOf.Keys.Count(work => WorkTitled(work, "eng")),
            ["census_works_with_french_title"] = seedOf.Keys.Count(work => WorkTitled(work, "fra")),
            ["seeds_with_english_and_french_titles"] = rootOf.Values.Count(work => WorkTitled(work, "eng") && WorkTitled(work, "fra")),
            ["consolidated_works_with_english_title"] = consolidated.Count(work => WorkTitled(work, "eng")),
            ["consolidated_works_with_french_title"] = consolidated.Count(work => WorkTitled(work, "fra")),
            ["main_bodies_with_articles"] = carrying.Length,
            ["main_bodies_with_a_publication"] = publications.Length,
            ["main_bodies_with_a_formex_title"] = publications.Count(static value => value.Publication.TitleText is not null),
            ["main_bodies_with_a_reference"] = references.Length,
            ["references_by_kind_and_form"] = Counted(references.Select(static row => $"{row.Kind} {row.NumberForm}")),
            ["references_without_publication_date"] = references.Count(static row => row.PublicationDate is null),
            ["references_without_first_page"] = references.Count(static row => row.FirstPage is null),
            ["references_by_series"] = Counted(references.Select(static row => row.Series)),
            ["seeds_by_reference_class"] = Counted(perSeed.Select(static seed => seed.Class)),
            ["seeds_whose_reference_is_a_consolidations"] = Strings(perSeed.Where(static seed => seed.Class != "none" && !seed.FromOriginal).Select(static seed => seed.Seed)),
            ["seeds_with_no_reference"] = Strings(perSeed.Where(static seed => seed.Class == "none").Select(static seed => seed.Seed)),
            ["seeds_by_act_number"] = Strings(perSeed.Where(static seed => seed.Class == "act_number").Select(static seed => seed.Seed)),
            ["reopen_seconds"] = Math.Round(reopened, 1),
            ["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 1),
            ["peak_working_set_mib"] = Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024),
        };
        var text = summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(output!, text);
        Console.WriteLine(text);
        Assert.IsGreaterThan(0, titles.Length);
    }
}
