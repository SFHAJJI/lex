using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Ingest.Europe;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The EU text yield measured over EU run 16's custody: for every consolidated version (state) the census holds and each of
/// English and French, why it has text in the served EU index or why not. Never a CI test.
/// </summary>
/// <remarks>
/// <para>
/// OPT-IN AND READ-ONLY, like the force-dates probe. It runs only with <c>LEX_EU_TEXT_YIELD_PROBE=1</c>;
/// <c>LEX_EU_TEXT_YIELD_PROBE_CUSTODY</c> names run 16's custody root (its brotli store, read in place through
/// <see cref="EuForceDatesRun16Probe.HeldOnlyCustody"/>, which never writes); <c>LEX_EU_TEXT_YIELD_PROBE_INDEX</c> names a
/// served EU index (derive-r1's), opened read-only, whose states and articles are the served yield;
/// <c>LEX_EU_TEXT_YIELD_PROBE_OUT</c> names the directory the summary and the per-state rows are written to. Nothing goes over
/// the network.
/// </para>
/// <para>
/// WHAT IT RUNS. The acquisition reopened exactly as the derive reopens it (<see cref="EuFirstMountAcquisition.ReopenAsync"/>:
/// the query run, the Formex population and the rights route from the run's own checkpoints), then the main-body producer the
/// build runs (<see cref="EuFormexMainBodyLegalContentProducer"/>) over the reopened Formex population. Each state's English and
/// French expressions are read from the population's own dispositions (the manifestation types its enumeration proved, the
/// package outcome, the main-body disposition and its detail) and from the run's corpus records (the held body, its served
/// format, or the typed reason it is not held). The served index says which (work, language) pairs carry articles.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class EuTextYieldRun16Probe
{
    private const string EnableVariable = "LEX_EU_TEXT_YIELD_PROBE";
    private const string CustodyVariable = "LEX_EU_TEXT_YIELD_PROBE_CUSTODY";
    private const string IndexVariable = "LEX_EU_TEXT_YIELD_PROBE_INDEX";
    private const string OutVariable = "LEX_EU_TEXT_YIELD_PROBE_OUT";
    private const string English = "http://publications.europa.eu/resource/authority/language/ENG";
    private const string French = "http://publications.europa.eu/resource/authority/language/FRA";

    [TestMethod]
    public async Task TheEuTextYieldOverRun16IsMeasured()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive("Set LEX_EU_TEXT_YIELD_PROBE=1, LEX_EU_TEXT_YIELD_PROBE_CUSTODY, LEX_EU_TEXT_YIELD_PROBE_INDEX and LEX_EU_TEXT_YIELD_PROBE_OUT for the read-only run-16 measurement.");
        }

        var root = Environment.GetEnvironmentVariable(CustodyVariable);
        var indexPath = Environment.GetEnvironmentVariable(IndexVariable);
        var output = Environment.GetEnvironmentVariable(OutVariable);
        Assert.IsTrue(Directory.Exists(root), CustodyVariable + " names run 16's custody.");
        Assert.IsTrue(File.Exists(indexPath), IndexVariable + " names a served EU index.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(output), OutVariable + " names the output directory.");
        Directory.CreateDirectory(output!);
        var clock = Stopwatch.StartNew();
        var phases = new JsonObject();
        void Phase(string name) => phases[name] = new JsonObject
        {
            ["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 1),
            ["working_set_mib"] = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024),
            ["managed_mib"] = GC.GetTotalMemory(false) / (1024 * 1024),
        };

        // ---- The served yield: the index's states and the (work, language) pairs that carry articles. ----
        var states = new List<(string Seed, string Root, string Work, string? Celex, int DateStatus, string? ConsolidationDate)>();
        var served = new Dictionary<(string Work, string Language), int>();
        var servedDigests = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = indexPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT seed_celex,root_work_iri,publisher_work_iri,publisher_work_celex,date_status,publisher_consolidation_date FROM states";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    states.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT publisher_work_id,language,count(*) FROM articles GROUP BY publisher_work_id,language";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    served[(reader.GetString(0), reader.GetString(1))] = reader.GetInt32(2);
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT article_identity_sha256,text_sha256 FROM article_digests";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    servedDigests[reader.GetString(0)] = reader.GetString(1);
            }
        }

        SqliteConnection.ClearAllPools();
        Phase("index_read");

        // ---- The acquisition, reopened as the derive reopens it, and the build's main-body producer over it. ----
        var store = new EuForceDatesRun16Probe.HeldOnlyCustody(FileSystemCustodyStore.WithBrotliCompression(root!));
        var pointer = JsonNode.Parse(File.ReadAllText(Directory.GetFiles(root!, "eu-acquisition-*.json").Single()))!;
        var catalogRef = new SourceArtifactRef(pointer["resource_id"]!.GetValue<string>(), pointer["sha256"]!.GetValue<string>());
        var catalog = JsonNode.Parse(Encoding.UTF8.GetString((await store.ReadByDigestAsync(catalogRef.Sha256, CancellationToken.None)).Span))!;
        var seeds = catalog["seeds"]!.AsArray().Select(static seed => seed!.GetValue<string>()).ToArray();
        var europe = await EuFirstMountAcquisition.ReopenAsync(store, catalogRef, seeds, CancellationToken.None);
        Assert.IsTrue(europe.Delivered, "The EU acquisition reopens.");
        Phase("acquisition_reopened");
        var run = europe.Run!;
        var formex = europe.Formex!.Reconciliation!;
        var mainBody = await new EuFormexMainBodyLegalContentProducer(store).RunAsync(formex, CancellationToken.None);
        Phase("main_body_produced");

        var enumerations = formex.PopulationsByFamilyKey.Values
            .SelectMany(static population => population.Eligibility.Enumerations)
            .GroupBy(static enumeration => enumeration.ExpressionIdentity)
            .ToDictionary(static group => group.Key, static group => group.First());
        var outcomesByWork = mainBody.Outcomes
            .GroupBy(static outcome => outcome.Source.ExpressionIdentity.PublisherWorkId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var records = run.CorpusRecordSet!.Set.Records;
        var recordsByUri = records.GroupBy(static record => record.ObjectRef.PublisherUri, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var ladders = run.DocumentLadderResultsByOrdinal ?? new Dictionary<int, EuDocumentLadderResult>();
        var classes = run.HeldBodyContentClasses ?? new Dictionary<SourceObjectRef, Lex.V3.Contracts.Source.Europe.EuContentClassObservation>();

        JsonObject RecordOf(CorpusRecord record) => new()
        {
            ["canonical_key"] = record.ObjectRef.CanonicalKey,
            ["publisher_uri"] = record.ObjectRef.PublisherUri,
            ["ordinal"] = record.ObjectOrdinal,
            ["body"] = record.Body.Kind.ToString(),
            ["body_disposition"] = record.BodyDisposition.ToString(),
            ["not_held_reason"] = record.Body.NotHeldReason?.ToString(),
            ["pending"] = record.Body.PendingAcquisitionReason is { } pending ? $"{pending.Kind}:{pending.Refusal}" : null,
            ["served_format"] = ladders.TryGetValue(record.ObjectOrdinal, out var ladder) ? ladder.Served?.ToString() : null,
            ["attempted_formats"] = ladders.TryGetValue(record.ObjectOrdinal, out var tried)
                ? new JsonArray([.. tried.Attempted.Select(static value => (JsonNode?)value.ToString())]) : null,
            ["content_class"] = classes.TryGetValue(record.ObjectRef, out var contentClass) ? contentClass.ContentClass.ToString() : null,
        };

        var rows = new JsonArray();
        var stateClasses = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var languageClasses = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var derivedClasses = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var derivedStates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in states.OrderBy(static value => value.Seed, StringComparer.Ordinal).ThenBy(static value => value.Work, StringComparer.Ordinal))
        {
            var outcomes = outcomesByWork.TryGetValue(state.Work, out var found) ? found : [];
            var languages = new JsonObject();
            var perLanguage = new List<string>();
            var derivedLanguages = new List<string>();
            foreach (var (language, token) in new[] { (English, "eng"), (French, "fra") })
            {
                var expressions = outcomes.Where(outcome => outcome.Source.Expression.OfficialLanguage == language).ToArray();
                var articleCount = served.TryGetValue((state.Work, token), out var count) ? count : 0;
                var uris = new[] { state.Work }.Concat(expressions.Select(static outcome => outcome.Source.ExpressionIdentity.PublisherExpressionId))
                    .Distinct(StringComparer.Ordinal).ToArray();
                var held = uris.SelectMany(uri => recordsByUri.TryGetValue(uri, out var matched) ? matched : [])
                    .Where(record => language == English ? record.ObjectRef.PublisherUri == state.Work || expressions.Any(e => e.Source.ExpressionIdentity.PublisherExpressionId == record.ObjectRef.PublisherUri)
                        : expressions.Any(e => e.Source.ExpressionIdentity.PublisherExpressionId == record.ObjectRef.PublisherUri))
                    .Distinct().ToArray();
                var expressionRows = new JsonArray();
                foreach (var outcome in expressions)
                {
                    var identity = outcome.Source.ExpressionIdentity;
                    var types = enumerations.TryGetValue(identity, out var enumeration) && enumeration.ManifestationTypes is { } listed
                        ? listed.Select(static value => value.PublisherType).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                        : null;
                    expressionRows.Add(new JsonObject
                    {
                        ["expression"] = identity.PublisherExpressionId,
                        ["manifestation_types"] = types is null ? null : new JsonArray([.. types.Select(static value => (JsonNode?)value)]),
                        ["package"] = outcome.Source.Kind.ToString(),
                        ["package_not_acquired_reason"] = outcome.Source.NotAcquiredReason == EuFormexPackageNotAcquiredReason.None ? null : outcome.Source.NotAcquiredReason.ToString(),
                        ["package_refusal"] = outcome.Source.PackageRefusal.ToString() == "None" ? null : outcome.Source.PackageRefusal.ToString(),
                        ["package_detail"] = Trim(outcome.Source.Detail),
                        ["main_body"] = outcome.Disposition.ToString(),
                        ["main_body_detail"] = Trim(outcome.Detail),
                        ["main_body_articles"] = outcome.Articles.Count,
                    });
                }

                var languageClass = Classify(articleCount, expressions, enumerations, held, ladders);
                perLanguage.Add(languageClass);
                // What this build derives for the language: the articles of a main body that carries articles and binds to a
                // held corpus body by the corpus builder's own rule (the index serves exactly these).
                var derivedArticles = expressions
                    .Where(outcome => EuFormexMainBodyLegalContentProducer.CarriesArticles(outcome.Disposition) &&
                        held.Any(record => record.Body.Kind == CorpusBodyRecordKind.Held &&
                            LexCorpus6Builder.FormexMainBodyBelongsTo(outcome.Source.Expression, record.ObjectRef)))
                    .Sum(static outcome => outcome.Articles.Count);
                if (derivedArticles > 0) derivedLanguages.Add(token);
                Add(derivedClasses, $"{token} {(derivedArticles > 0 ? "derived" : articleCount > 0 ? "served_not_derived" : languageClass)}",
                    $"{state.Seed} {state.Celex ?? "-"} {state.Work}");
                Add(languageClasses, $"{token} {languageClass}", $"{state.Seed} {state.Celex ?? "-"} {state.Work}");
                languages[token] = new JsonObject
                {
                    ["class"] = languageClass,
                    ["served_articles"] = articleCount,
                    ["derived_articles"] = derivedArticles,
                    ["expressions"] = expressionRows,
                    ["records"] = new JsonArray([.. held.Select(record => (JsonNode?)RecordOf(record))]),
                };
            }

            var stateClass = perLanguage.Any(static value => value == "served") ? "served" : string.Join(" | ", perLanguage);
            Add(stateClasses, stateClass, $"{state.Seed} {state.Celex ?? "-"} {state.Work}");
            if (derivedLanguages.Count > 0) derivedStates.Add(state.Work);
            rows.Add(new JsonObject
            {
                ["seed"] = state.Seed,
                ["work"] = state.Work,
                ["celex"] = state.Celex,
                ["date_status"] = state.DateStatus,
                ["consolidation_date"] = state.ConsolidationDate,
                ["class"] = stateClass,
                ["expression_languages"] = new JsonArray([.. outcomes.Select(static outcome => Language(outcome.Source.Expression.OfficialLanguage))
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(static value => (JsonNode?)value)]),
                ["languages"] = languages,
            });
        }

        var census = states.Select(static state => state.Work).ToHashSet(StringComparer.Ordinal);
        // Every article this build derives, by identity and text digest, against the served index's: a served article this
        // build no longer derives, or derives with another text, would be a regression.
        var derived = mainBody.Outcomes.Where(static outcome => EuFormexMainBodyLegalContentProducer.CarriesArticles(outcome.Disposition))
            .SelectMany(static outcome => outcome.Articles)
            .GroupBy(static article => article.IdentitySha256, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().TextSha256, StringComparer.Ordinal);
        var servedKept = servedDigests.Count(pair => derived.TryGetValue(pair.Key, out var text) && text == pair.Value);
        var servedChanged = servedDigests.Count(pair => derived.TryGetValue(pair.Key, out var text) && text != pair.Value);
        var summary = new JsonObject
        {
            ["schema"] = "lex-v3-eu-text-yield-run16-probe/1",
            ["catalog_sha256"] = catalogRef.Sha256,
            ["index"] = indexPath,
            ["states"] = states.Count,
            ["states_served"] = stateClasses.TryGetValue("served", out var servedStates) ? servedStates.Count : 0,
            ["state_classes"] = Classes(stateClasses),
            ["language_classes"] = Classes(languageClasses),
            ["main_body_profile"] = EuFormexMainBodyLegalContentProducer.Profile,
            ["states_derived_by_this_build"] = derivedStates.Count,
            ["derived_language_classes"] = Classes(derivedClasses),
            ["served_articles"] = servedDigests.Count,
            ["served_articles_derived_unchanged"] = servedKept,
            ["served_articles_derived_with_other_text"] = servedChanged,
            ["served_articles_not_derived"] = servedDigests.Count - servedKept - servedChanged,
            ["derived_articles_not_served"] = derived.Keys.Count(key => !servedDigests.ContainsKey(key)),
            ["derived_articles"] = mainBody.Outcomes.Where(static outcome => EuFormexMainBodyLegalContentProducer.CarriesArticles(outcome.Disposition))
                .Sum(static outcome => outcome.Articles.Count),
            ["formex_outcomes"] = mainBody.Outcomes.Count,
            ["formex_outcomes_outside_census"] = mainBody.Outcomes.Count(outcome => !census.Contains(outcome.Source.ExpressionIdentity.PublisherWorkId)),
            ["formex_outcomes_by_language_package_and_main_body"] = Counted(mainBody.Outcomes.Select(static outcome =>
                $"{Language(outcome.Source.Expression.OfficialLanguage)} {outcome.Source.Kind}{(outcome.Source.NotAcquiredReason == EuFormexPackageNotAcquiredReason.None ? "" : ":" + outcome.Source.NotAcquiredReason)} {outcome.Disposition}")),
            ["main_body_details"] = Counted(mainBody.Outcomes.Where(static outcome => outcome.Source.Kind == EuFormexPackageOutcomeKind.Acquired &&
                outcome.Disposition != EuFormexMainBodyLegalContentDisposition.Admitted).Select(static outcome => $"{outcome.Disposition}: {Trim(outcome.Detail)}")),
            ["not_acquired_details"] = Counted(mainBody.Outcomes.Where(static outcome => outcome.Source.Kind == EuFormexPackageOutcomeKind.NotAcquired)
                .Select(static outcome => $"{outcome.Source.NotAcquiredReason}: {Trim(outcome.Source.Detail, 140)}")),
            ["records"] = records.Count,
            ["records_by_body"] = Counted(records.Select(record => $"{(record.ObjectRef.CanonicalKey.Split(':')[0])} {record.Body.Kind} {record.Body.NotHeldReason?.ToString() ?? "-"} {record.Body.PendingAcquisitionReason?.Refusal?.ToString() ?? "-"} served={(ladders.TryGetValue(record.ObjectOrdinal, out var ladder) ? ladder.Served?.ToString() ?? "none" : "-")}")),
            ["phases"] = phases,
            ["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 1),
            ["peak_working_set_mib"] = Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024),
        };
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(output!, "eu-text-yield-summary.json"), summary.ToJsonString(options));
        await File.WriteAllTextAsync(Path.Combine(output!, "eu-text-yield-states.json"), rows.ToJsonString(options));
        Console.WriteLine(summary.ToJsonString(options));
        Assert.HasCount(states.Count, rows);
    }

    /// <summary>
    /// One language's class: served, or the first reason the derivation gives. The order is the pipeline's: is there an
    /// expression in that language, did its Formex package reach the main-body parser and what did the parser say, and what
    /// does the run hold of its body.
    /// </summary>
    private static string Classify(
        int articleCount,
        IReadOnlyList<EuFormexMainBodyLegalContentOutcome> expressions,
        IReadOnlyDictionary<Lex.V3.Contracts.Derivation.LanguageScopedExpressionIdentity, EuFormexManifestationEnumerationResult> enumerations,
        IReadOnlyList<CorpusRecord> held,
        IReadOnlyDictionary<int, EuDocumentLadderResult> ladders)
    {
        if (articleCount > 0) return "served";
        if (expressions.Count == 0) return "no_expression_in_language";
        var heldBody = held.FirstOrDefault(static record => record.Body.Kind == CorpusBodyRecordKind.Held);
        var body = heldBody is null
            ? held.Count == 0 ? "no_record" : $"body_{held[0].Body.Kind}_{held[0].Body.NotHeldReason?.ToString() ?? held[0].Body.PendingAcquisitionReason?.Refusal?.ToString() ?? "-"}"
            : $"body_held_{(ladders.TryGetValue(heldBody.ObjectOrdinal, out var ladder) ? ladder.Served?.ToString() ?? "none" : "-")}";
        var outcome = expressions.Count == 1 ? expressions[0] : expressions.OrderBy(static value => value.Disposition).First();
        var types = enumerations.TryGetValue(outcome.Source.ExpressionIdentity, out var enumeration) && enumeration.ManifestationTypes is { } listed
            ? string.Join("+", listed.Select(static value => value.PublisherType).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            : "unenumerated";
        var formex = outcome.Source.Kind switch
        {
            EuFormexPackageOutcomeKind.Acquired => outcome.Disposition == EuFormexMainBodyLegalContentDisposition.Admitted
                ? "formex_admitted_not_indexed" : $"formex_acquired_{outcome.Disposition}",
            EuFormexPackageOutcomeKind.NotAcquired => $"formex_not_acquired_{outcome.Source.NotAcquiredReason}",
            EuFormexPackageOutcomeKind.PackageRejected => $"formex_package_rejected_{outcome.Source.PackageRefusal}",
            _ => $"formex_{outcome.Source.Kind}",
        };
        return $"{formex} [{types}] {body}" + (expressions.Count > 1 ? $" (expressions={expressions.Count})" : "");
    }

    private static string Language(string iri) => iri switch { English => "eng", French => "fra", _ => iri[(iri.LastIndexOf('/') + 1)..].ToLowerInvariant() };

    private static string? Trim(string? value, int length = 200) => value is null ? null : value.Length <= length ? value : value[..length];

    private static void Add(Dictionary<string, List<string>> map, string key, string example)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(example);
    }

    private static JsonObject Classes(Dictionary<string, List<string>> map) =>
        new(map.OrderByDescending(static pair => pair.Value.Count).ThenBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => KeyValuePair.Create(pair.Key, (JsonNode?)new JsonObject
            {
                ["count"] = pair.Value.Count,
                ["examples"] = new JsonArray([.. pair.Value.Take(4).Select(static value => (JsonNode?)value)]),
            })));

    private static JsonObject Counted(IEnumerable<string> values) =>
        new(values.GroupBy(static value => value, StringComparer.Ordinal)
            .OrderByDescending(static group => group.Count()).ThenBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => KeyValuePair.Create(group.Key, (JsonNode?)group.Count())));
}
