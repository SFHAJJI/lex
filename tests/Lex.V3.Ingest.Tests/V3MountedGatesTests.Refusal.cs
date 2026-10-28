using System.Globalization;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Evaluation;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The refusal set over a mount: each request is built from what the mount holds, so the one code the
/// registry says answers it follows from the mount's own data (a date before a held work's first state, a
/// digest that is not its state's, a title two of its works carry). A code this mount cannot produce (no
/// two states share a date, no member's licence withholds text) is named as not produced, with its reason,
/// and never faked. A mount with no Luxembourg state holds only what does not need one. An EU index gives EU
/// requests from its own data (a held work and a word of it, a CELEX it does not hold, a language the work is not
/// held in, a dated search, a date asked of <c>in_force_on</c>), so a mount that holds only the EU still measures its
/// refusals; a code an EU request produces is not listed as not produced.
/// </summary>
public sealed partial class V3MountedGatesTests
{
    /// <summary>A request a client can send, and the one verdict the mount's data says answers it.</summary>
    internal sealed record RefusalRequest(string Operation, object Parameters, string Gold);

    /// <summary>The refusal set a mount gives, and the codes it cannot produce, each with why.</summary>
    internal sealed record RefusalSet(IReadOnlyDictionary<string, RefusalRequest> Requests, IReadOnlyDictionary<string, string> NotProduced);

    private const string NoSuchWork = "/lu-legilux/no-such-work";

    /// <summary>
    /// The evidence bundle's own rule for a state's text (<c>V3CorpusMount</c>, rights at compose time): its sources are
    /// the members behind its article identities (<c>StateSources</c>), and the text is withheld when it has none, or
    /// when any source was not acquired or holds another rights disposition than the one admitting one. As SQL over a
    /// state <c>s</c>; the tokens are the contract's own.
    /// </summary>
    private static readonly string StateWithheld =
        "(NOT EXISTS (SELECT 1 FROM json_each(s.article_identities_json) j JOIN articles a ON a.article_identity_sha256 = j.value JOIN members m ON m.object_ref_sha256 = a.object_ref_sha256) " +
        "OR EXISTS (SELECT 1 FROM json_each(s.article_identities_json) j JOIN articles a ON a.article_identity_sha256 = j.value JOIN members m ON m.object_ref_sha256 = a.object_ref_sha256 " +
        $"WHERE m.outcome IS NOT '{ContractWire.NameOf(LexCorpus6OutcomeKind.Acquired)}' OR m.rights_disposition IS NOT '{ContractWire.NameOf(LuxembourgRightsChannelDisposition.AgreedSameRunCcBy)}'))";
    private static readonly string[] CandidateLanguages = ["eng", "deu", "fra", "ltz"];
    private static readonly string[] CandidateFormats = ["docx", "epub", "odt", "rtf"];

    internal static RefusalSet RefusalCases(string mountDirectory, IReadOnlyList<Timeline> timelines, IReadOnlyDictionary<(string Root, string Expression), string>? memberOutcomes = null)
    {
        // Coverage answers about the Luxembourg index, and refuses no_corpus_mounted when the mount holds none (an EU index
        // alone): the handler's own rule (its Luxembourg reader is null).
        var hasLuxembourgIndex = File.Exists(Path.Combine(mountDirectory, V3CorpusMount.IndexFileName));
        var requests = new SortedDictionary<string, RefusalRequest>(StringComparer.Ordinal)
        {
            ["coverage"] = new("coverage", new { }, hasLuxembourgIndex ? "answer" : "no_corpus_mounted"),
            // What no data of the mount decides, so every mount gives it: an instrument the out-of-scope list names, refused before
            // the mount is read (the plan's item 6.10; J5), and an operation served from no data (8.6), refused with its typed reason.
            ["cssf-circular"] = new("dossier", new { identifier = "Circulaire CSSF 20/747" }, "out_of_corpus_scope"),
            ["served-from-no-data"] = new("concepts", new { }, "retrieval_mode_unavailable"),
        };
        var notProduced = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var hasEuropeIndex = File.Exists(Path.Combine(mountDirectory, V3CorpusMount.EuropeIndexFileName));
        if (!hasEuropeIndex)
        {
            requests["eu-work-no-eu-index"] = new("search", new { query = "x", language = "eng", identifier = "32016R0679" }, "no_corpus_mounted");
        }
        else
        {
            notProduced["no_corpus_mounted"] = "the mount holds an EU index, so an EU request is not refused for want of one";
        }

        // On index 9 the interval oracle says what the rules serve on a day (the data design's §3.5), so an anchor is a state
        // the rules serve alone on its own date, a date before the history is before the catalogue's first state (a state
        // without text may begin before the first held one), and two states of one date are ambiguous only when the oracle
        // says so (one wording is served once). On an index of schema 8 or 6 the timelines say it (the R1 gates, 2026-10-26).
        using var oracle = IntervalOracle.Open(mountDirectory, memberOutcomes);
        bool ServedAlone(string work, string language, DateOnly date, string state) =>
            oracle is null || oracle.AsOf(work, language, date) == state;

        // A dated state held alone on its date: the anchor of every request that names one state.
        var held = timelines
            .SelectMany(static timeline => timeline.Dates.Where(static date => date.States.Count == 1).Select(date => (Timeline: timeline, date.Date, State: date.States[0])))
            .FirstOrDefault(value => ServedAlone(value.Timeline.WorkKey, value.Timeline.Language, value.Date, value.State));
        if (held.Timeline is null)
        {
            foreach (var code in new[] { "identifier_unknown", "language_not_available", "no_version_for_date", "anchor_not_in_version", "pinned_digest_mismatch", "retrieval_mode_unavailable", "snapshot_unknown", "format_not_available", "ambiguous_version", "ambiguous_identifier", "profiles_differ", "text_withheld", "text_not_available" })
            {
                notProduced[code] = "the mount holds no Luxembourg state held alone on its date";
            }

            return Finished(mountDirectory, requests, notProduced);
        }

        var (timeline, date, state) = held;
        var work = $"/lu-legilux/{timeline.WorkKey}";
        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var language = timeline.Language;
        requests["a-held-state"] = new("as_of", new { identifier = work, date = day, language }, "answer");
        requests["the-held-permalink"] = new("verify", new { identifier = $"{work}/{day}--{state}/{language}", language }, "answer");
        requests["unknown-work"] = new("as_of", new { identifier = NoSuchWork, date = day }, "identifier_unknown");
        var first = oracle is null ? timeline.Dates[0].Date : oracle.Boundaries(timeline.WorkKey, language)[0];
        requests["before-history"] = new("as_of", new { identifier = work, date = first.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), language }, "no_version_for_date");
        requests["anchor-not-held"] = new("article_history", new { identifier = work, anchor = "art_no_such_anchor", language }, "anchor_not_in_version");
        requests["pinned-digest-not-held"] = new("verify", new { identifier = $"{work}/{day}--{new string('0', 64)}/{language}", language }, "pinned_digest_mismatch");
        requests["mode-not-held"] = new("search", new { query = "loi", language, mode = "bm25" }, "retrieval_mode_unavailable");
        requests["foreign-cursor"] = new("events", new { after = new string('0', 64) + ":1" }, "snapshot_unknown");

        var heldLanguages = timelines.Where(value => value.WorkKey == timeline.WorkKey).Select(static value => value.Language).ToHashSet(StringComparer.Ordinal);
        var absent = CandidateLanguages.FirstOrDefault(candidate => !heldLanguages.Contains(candidate));
        if (absent is not null)
        {
            requests["language-not-held"] = new("dossier", new { identifier = work, language = absent }, "language_not_available");
            requests["as-of-language-not-held"] = new("as_of", new { identifier = work, date = day, language = absent }, "language_not_available");
        }
        else
        {
            notProduced["language_not_available"] = $"{timeline.WorkKey} is held in every candidate language";
        }

        var twin = timelines.SelectMany(static value => value.Dates.Where(static date => date.States.Count > 1).Select(date => (Timeline: value, date.Date)))
            .FirstOrDefault(value => oracle is null || oracle.AsOf(value.Timeline.WorkKey, value.Timeline.Language, value.Date) == Ambiguous);
        if (twin.Timeline is not null)
        {
            requests["twin-states"] = new("as_of", new { identifier = $"/lu-legilux/{twin.Timeline.WorkKey}", date = twin.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), language = twin.Timeline.Language }, "ambiguous_version");
        }
        else
        {
            notProduced["ambiguous_version"] = "no two states of one work share a date in one language";
        }

        using var connection = LuxembourgIndexBuilder.Open(Path.Combine(mountDirectory, V3CorpusMount.IndexFileName), SqliteOpenMode.ReadOnly);
        // A format none of the work's manifestations carries, by the handler's rule (the last segment of the publisher's
        // userFormat IRI, over every expression of the work's states). Legilux publishes docx for most acts (22,908 docx
        // manifestations in the population), so a format assumed absent was answered on the real mount (2026-10-13).
        var heldFormats = Rows(connection,
                "SELECT f.object_value FROM states s " +
                $"JOIN work_facts e ON e.subject_iri = s.expression_iri AND e.predicate = '{ContractWire.NameOf(LuxembourgAssertionPredicate.IsEmbodiedBy)}' " +
                $"JOIN work_facts f ON f.subject_iri = e.object_value AND f.predicate = '{ContractWire.NameOf(LuxembourgAssertionPredicate.UserFormat)}' " +
                $"WHERE s.work_key = '{timeline.WorkKey.Replace("'", "''", StringComparison.Ordinal)}'")
            .Select(static row => row[0].TrimEnd('/'))
            .Select(static format => format[(format.LastIndexOf('/') + 1)..])
            .ToHashSet(StringComparer.Ordinal);
        var unheldFormat = CandidateFormats.FirstOrDefault(candidate => !heldFormats.Contains(candidate));
        if (unheldFormat is not null)
        {
            requests["format-not-held"] = new("manifestation", new { identifier = work, format = unheldFormat }, "format_not_available");
        }
        else
        {
            notProduced["format_not_available"] = $"{timeline.WorkKey}'s manifestations carry every candidate format";
        }

        var title = AmbiguousTitle(Rows(connection, "SELECT work_identifier, normalized_title FROM work_titles").Select(static row => (row[0], row[1])).ToArray());
        if (title is not null)
        {
            requests["title-two-works-carry"] = new("resolve", new { identifier = title }, "ambiguous_identifier");
        }
        else
        {
            notProduced["ambiguous_identifier"] = "no title, nor any beginning of one, is carried by two works";
        }

        // Two states of one work in one language, each alone on its date, whose rule profiles differ.
        // Held states only (on index 9 a state without text binds no article), each the one the rules serve on its date.
        var profiles = Rows(connection, "SELECT work_key, language, applicability_date, rule_profiles_json, state_sha256 FROM states WHERE article_identities_json <> '[]' ORDER BY work_key, language, applicability_date")
            .GroupBy(static row => (row[0], row[1], row[2]))
            .Where(static group => group.Count() == 1)
            .Select(static group => group.Single())
            .Where(row => ServedAlone(row[0], row[1], Day(row[2]), row[4]))
            .GroupBy(static row => (row[0], row[1]))
            .SelectMany(static group => group.Zip(group.Skip(1), static (from, to) => (From: from, To: to)))
            .FirstOrDefault(static pair => pair.From[3] != pair.To[3]);
        if (profiles.From is not null)
        {
            requests["profiles-differ"] = new("diff", new { identifier = $"/lu-legilux/{profiles.From[0]}", date_from = profiles.From[2], date_to = profiles.To[2], language = profiles.From[1] }, "profiles_differ");
        }
        else
        {
            notProduced["profiles_differ"] = "no two states of one work differ in rule profile";
        }

        // A state held alone on its date whose text the bundle withholds by its own rule (review of #839: the first
        // version guessed a "non_admitting" prefix and missed a disposition such as `conflict`); and one whose text it
        // does not withhold but whose every article holds none, since rights are decided before any text is read.
        var withheld = StateWhere(connection, StateWithheld, ServedAlone);
        if (withheld is { } blocked)
        {
            requests["rights-not-agreed"] = new("evidence_bundle", new { identifier = $"/lu-legilux/{blocked.Work}", date = blocked.Date, language = blocked.Language }, "text_withheld");
        }
        else
        {
            notProduced["text_withheld"] = "every state's sources were acquired under the one admitting rights disposition";
        }

        var empty = StateWhere(connection, $"NOT {StateWithheld} AND NOT EXISTS (SELECT 1 FROM articles a WHERE a.expression_iri = s.expression_iri AND a.searchable_text <> '')", ServedAlone);
        if (empty is { } textless)
        {
            requests["no-text-held"] = new("evidence_bundle", new { identifier = $"/lu-legilux/{textless.Work}", date = textless.Date, language = textless.Language }, "text_not_available");
        }
        else
        {
            notProduced["text_not_available"] = "every state whose text is admitted holds text";
        }

        // On index 9 a date a state without text covers is refused by as_of with the state's own reason (the specification's
        // R83 and R77, and the data design's §3.5): text_withheld when its member's rights withhold it, else text_not_available. Each is
        // asked on the first such state the oracle says as_of refuses so on its own date. A not-produced reason says what the
        // catalogue holds, since index 9 holds states without text (the R1 gates, 2026-10-27).
        if (oracle is not null)
        {
            foreach (var (code, id) in new[] { ("text_withheld", "state-text-withheld"), ("text_not_available", "state-text-not-available") })
            {
                if (TextlessStateRefused(connection, oracle, code) is { } refused)
                {
                    requests[id] = new("as_of", new { identifier = $"/lu-legilux/{refused.Work}", date = refused.Date, language = refused.Language }, code);
                }
            }

            // An ambiguity the sample does not hold may be in the catalogue: the first day, in work order, on which the rules say
            // two French states cover a work's date and are not one wording (a pair of one date, or one state nested in another).
            if (!requests.ContainsKey("twin-states") && AmbiguousInCatalogue(connection, oracle) is { } ambiguous)
            {
                requests["twin-states"] = new("as_of", new { identifier = $"/lu-legilux/{ambiguous.Work}", date = ambiguous.Date, language = "fra" }, "ambiguous_version");
            }

            ReasonOnIndexNine(notProduced, "text_withheld", "no state without text that as_of refuses on its own date is withheld by its rights, and every held state's sources were acquired under the one admitting rights disposition");
            ReasonOnIndexNine(notProduced, "text_not_available", "no state without text that as_of refuses on its own date lacks it for a reason other than its rights, and every held state whose text is admitted holds text");
            ReasonOnIndexNine(notProduced, "ambiguous_version", "no day a French state of a served work begins or ends on is one the rules call ambiguous (states of one wording are served once)");
            ReasonOnIndexNine(notProduced, "profiles_differ", "no two held states of one work, each served alone on its own date, differ in rule profile");
        }

        return Finished(mountDirectory, requests, notProduced);
    }

    private static void ReasonOnIndexNine(IDictionary<string, string> notProduced, string code, string reason)
    {
        if (notProduced.ContainsKey(code))
        {
            notProduced[code] = reason;
        }
    }

    /// <summary>
    /// The first day, in work key order then date order, of a French chain's starts and ends on which the interval oracle says
    /// <c>as_of</c> in French is <c>ambiguous_version</c>, or none.
    /// </summary>
    private static (string Work, string Date)? AmbiguousInCatalogue(SqliteConnection connection, IntervalOracle oracle) =>
        Rows(connection, "SELECT DISTINCT work_key FROM states ORDER BY work_key")
            .SelectMany(row => oracle.Boundaries(row[0], "fra").Select(day => (Work: row[0], Day: day)))
            .Where(value => oracle.AsOf(value.Work, "fra", value.Day) == Ambiguous)
            .Select(static value => ((string Work, string Date)?)(value.Work, value.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .FirstOrDefault();

    /// <summary>
    /// The first state without text, in the catalogue's order, on whose own date the interval oracle says <c>as_of</c> refuses
    /// with <paramref name="code"/> (no held state of its language covers that date beside it).
    /// </summary>
    private static (string Work, string Date, string Language)? TextlessStateRefused(SqliteConnection connection, IntervalOracle oracle, string code) =>
        Rows(connection, "SELECT work_key, applicability_date, language FROM states WHERE article_identities_json = '[]' AND language <> '' ORDER BY work_key, applicability_date, language, state_sha256")
            .Where(row => oracle.AsOf(row[0], row[2], Day(row[1])) == "refusal:" + code)
            .Select(static row => ((string Work, string Date, string Language)?)(row[0], row[1], row[2]))
            .FirstOrDefault();

    /// <summary>
    /// The set with the EU requests the mount's EU index gives, and without a not-produced entry for a code some request
    /// produces.
    /// </summary>
    private static RefusalSet Finished(string mountDirectory, SortedDictionary<string, RefusalRequest> requests, SortedDictionary<string, string> notProduced)
    {
        EuropeRefusals(mountDirectory, requests);
        foreach (var gold in requests.Values.Select(static request => request.Gold))
        {
            notProduced.Remove(gold);
        }

        return new RefusalSet(requests, notProduced);
    }

    /// <summary>
    /// EU requests from the mount's EU index, for its first work in its first language that holds a usable word (six
    /// letters or more) in any of its articles, scanned in order (review of #846: the first version read only the first
    /// article, and a short one left the EU unmeasured), among the wordings EU search answers by CELEX
    /// (<see cref="EuropeSearchedArticles"/>): a word the work holds, searched in it (answered); the same in a CELEX the index does not hold (<c>identifier_unknown</c>); in a language the work is not
    /// held in (<c>language_not_available</c>); and with a date, which EU search does not serve (<c>retrieval_mode_unavailable</c>).
    /// The work asked of <c>in_force_on</c> at a date (<c>retrieval_mode_unavailable</c>: the EU capability manifest states it refused,
    /// since no build indexes the force dates and a wording date is never one).
    /// And, when the work's expression in that language holds one wording date, the EU permalink grammar's (#850; R64's shape): a
    /// provision of the wording pinned by its digest, recomputed here by the stated rule, so <c>verify</c> answering it also
    /// checks the API's digest (answered); the wording pinned by a digest it does not have (<c>pinned_digest_mismatch</c>);
    /// and a provision the wording does not hold (<c>anchor_not_in_version</c>).
    /// </summary>
    private static void EuropeRefusals(string mountDirectory, IDictionary<string, RefusalRequest> requests)
    {
        var path = Path.Combine(mountDirectory, V3CorpusMount.EuropeIndexFileName);
        if (!File.Exists(path))
        {
            return;
        }

        using var connection = EuropeIndexBuilder.Open(path, SqliteOpenMode.ReadOnly);
        var usable = Rows(connection, $"SELECT publisher_work_celex, language, searchable_text FROM articles WHERE {EuropeSearchedArticles(connection)} ORDER BY publisher_work_celex, language, article_identity_sha256")
            .Select(static row => (Celex: row[0], Language: row[1], Word: UsableWord(row[2])))
            .FirstOrDefault(static row => row.Word is not null);
        if (usable.Word is null)
        {
            return;
        }

        var (celex, language, word) = usable;

        requests["eu-search-held"] = new("search", new { query = word, language, identifier = celex }, "answer");
        const string NotHeld = "32099R9999";
        if (Scalar(connection, $"SELECT publisher_work_celex FROM articles WHERE publisher_work_celex = '{NotHeld}' LIMIT 1") is null)
        {
            // An EU act outside the population an EU index states (its census of seed acts) is out of the corpus's scope (R80);
            // an index that states no population (no states table, or none in it) still answers it unknown.
            var statesPopulation = Scalar(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'states'") is not null &&
                                   Scalar(connection, "SELECT seed_celex FROM states LIMIT 1") is not null;
            requests["eu-unknown-celex"] = new("search", new { query = word, language, identifier = NotHeld }, statesPopulation ? "out_of_corpus_scope" : "identifier_unknown");
        }

        var held = Rows(connection, $"SELECT DISTINCT language FROM articles WHERE publisher_work_celex = '{celex.Replace("'", "''", StringComparison.Ordinal)}'").Select(static row => row[0]).ToHashSet(StringComparer.Ordinal);
        var absent = CandidateLanguages.FirstOrDefault(candidate => !held.Contains(candidate));
        if (absent is not null)
        {
            requests["eu-language-not-held"] = new("search", new { query = word, language = absent, identifier = celex }, "language_not_available");
        }

        // A dated EU search reads the wording the date selects, as as_of does, where the EU index holds the act's census: a date
        // before every EU act is as_of's no_version_for_date; without a census no wording can be selected, and a date is refused.
        var quotedCelex = celex.Replace("'", "''", StringComparison.Ordinal);
        requests["eu-dated-search"] = HasStatesTable(connection) && Scalar(connection, $"SELECT seed_celex FROM states WHERE seed_celex = '{quotedCelex}' LIMIT 1") is not null
            ? new("search", new { query = word, language, identifier = celex, date = "1900-01-01" }, "no_version_for_date")
            : new("search", new { query = word, language, identifier = celex, date = "2020-01-01" }, "retrieval_mode_unavailable");
        requests["eu-in-force-on"] = new("in_force_on", new { identifier = celex, date = "2020-01-01", language }, "retrieval_mode_unavailable");

        var quotedLanguage = language.Replace("'", "''", StringComparison.Ordinal);
        var wordings = Rows(connection, $"SELECT DISTINCT publisher_work_id, publisher_expression_id, wording_date FROM articles WHERE publisher_work_celex = '{quotedCelex}' AND language = '{quotedLanguage}'");
        if (wordings.Count != 1)
        {
            return;
        }

        var (workId, expressionId, wordingDate) = (wordings[0][0], wordings[0][1], wordings[0][2]);
        var articles = Rows(connection, $"SELECT publisher_identifier, article_identity_sha256 FROM articles WHERE publisher_expression_id = '{expressionId.Replace("'", "''", StringComparison.Ordinal)}' ORDER BY publisher_identifier, article_identity_sha256");
        var digest = V3EuropePermalinkTests.WordingSha256ByTheStatedRule(celex, workId, expressionId, language, wordingDate, articles.Select(static row => row[1]));
        var wording = $"/eu-eurlex/{celex}/{wordingDate}--{digest}/{language}";
        requests["eu-pinned-provision"] = new("verify", new { identifier = $"{wording}/{Uri.EscapeDataString(articles[0][0])}" }, "answer");
        requests["eu-pinned-digest-not-held"] = new("verify", new { identifier = $"/eu-eurlex/{celex}/{wordingDate}--{new string('0', 64)}/{language}" }, "pinned_digest_mismatch");
        requests["eu-provision-not-held"] = new("verify", new { identifier = $"{wording}/lex-no-such-provision" }, "anchor_not_in_version");
    }

    /// <summary>
    /// A title query the resolver answers with two works or more, by its own rule (an exact normalized title
    /// first, else the titles it begins): a normalized title two works carry, or the longest beginning two works'
    /// titles share, which sorted titles place side by side. Null when the mount has none.
    /// </summary>
    internal static string? AmbiguousTitle(IReadOnlyList<(string Work, string Normalized)> titles)
    {
        var sorted = titles.OrderBy(static title => title.Normalized, StringComparer.Ordinal).ToArray();
        var candidates = sorted.Select(static title => title.Normalized)
            .Concat(sorted.Zip(sorted.Skip(1)).Where(static pair => pair.First.Work != pair.Second.Work).Select(static pair => SharedBeginning(pair.First.Normalized, pair.Second.Normalized)))
            .Select(static candidate => LuxembourgIndexBuilder.NormalizeTitle(candidate))
            .Where(static candidate => candidate.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        return candidates.FirstOrDefault(candidate =>
        {
            var exact = titles.Where(title => title.Normalized == candidate).ToArray();
            var selected = exact.Length > 0 ? exact : titles.Where(title => title.Normalized.StartsWith(candidate, StringComparison.Ordinal)).ToArray();
            return selected.Select(static title => title.Work).Distinct(StringComparer.Ordinal).Count() >= 2;
        });
    }

    private static string SharedBeginning(string a, string b)
    {
        var length = 0;
        while (length < a.Length && length < b.Length && a[length] == b[length])
        {
            length++;
        }

        return a[..length].TrimEnd();
    }

    /// <summary>A word of six letters or more in an article's searchable text, or null.</summary>
    private static string? UsableWord(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(static token => token.Length >= 6 && token.All(char.IsLetter));

    /// <summary>A state alone on its date, with articles, that meets a condition on its articles (as <c>s</c>).</summary>
    private static (string Work, string Date, string Language)? StateWhere(
        SqliteConnection connection, string condition, Func<string, string, DateOnly, string, bool> servedAlone)
    {
        // The first such state the rules serve alone on its own date (on index 9 another state may cover it too).
        var row = Rows(connection,
            "SELECT s.work_key, s.applicability_date, s.language, s.state_sha256 FROM states s " +
            "WHERE EXISTS (SELECT 1 FROM articles a WHERE a.expression_iri = s.expression_iri) AND " + condition + " " +
            "AND (SELECT COUNT(*) FROM states t WHERE t.work_key = s.work_key AND t.language = s.language AND t.applicability_date = s.applicability_date) = 1 " +
            "ORDER BY s.work_key, s.applicability_date, s.language LIMIT 64")
            .FirstOrDefault(candidate => servedAlone(candidate[0], candidate[2], Day(candidate[1]), candidate[3]));
        return row is null ? null : (row[0], row[1], row[2]);
    }

    /// <summary>
    /// The EU articles <c>search</c> and the original-wording <c>verify</c> answer by their work's CELEX, as SQL over <c>articles</c>:
    /// the EU index reader's own condition (<c>ResolveExact</c>). The article carries a CELEX: since schema 5 a consolidated version
    /// the publisher gives none holds NULL there, and reading it threw before any card was written. And on an index with a states
    /// table it is an original legal text: a consolidated version is answered by the time view alone (the EU temporal cases), and
    /// EU search refuses its CELEX <c>identifier_unknown</c>, so a set derived from it would fail on a correct mount.
    /// </summary>
    private static string EuropeSearchedArticles(SqliteConnection connection) =>
        "publisher_work_celex IS NOT NULL" + (HasStatesTable(connection)
            ? " AND EXISTS (SELECT 1 FROM members m WHERE m.object_ref_sha256 = articles.object_ref_sha256 AND m.content_class = " +
              $"'{ContractWire.NameOf(Lex.V3.Contracts.Source.Europe.EuContentClass.OriginalLegalText)}')"
            : "");

    private static string? Scalar(SqliteConnection connection, string sql) => Rows(connection, sql).FirstOrDefault()?[0];

    private static List<string[]> Rows(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string[]>();
        while (reader.Read())
        {
            rows.Add(Enumerable.Range(0, reader.FieldCount).Select(at => reader.GetString(at)).ToArray());
        }

        return rows;
    }

    /// <summary>The refusal gate over a mount: every request answered with its one code, and the verdict control.</summary>
    internal static (EvaluationCardSet Set, RefusalSet Cases) RunRefusalGate(
        V3CorpusMount mount, string mountDirectory, IReadOnlyList<Timeline> timelines, IReadOnlyDictionary<(string Root, string Expression), string>? memberOutcomes = null)
    {
        var derived = RefusalCases(mountDirectory, timelines, memberOutcomes);
        var cases = derived.Requests.Select(static pair => new VerdictCase(pair.Key, pair.Value.Gold)).ToArray();
        VerdictArm arm = caseId =>
        {
            var request = derived.Requests[caseId];
            var envelope = EnvelopeAsync(mount, "/api/v3/" + request.Operation, request.Operation, request.Parameters).GetAwaiter().GetResult();
            return envelope.Refusal?.Code ?? "answer";
        };
        var report = VerdictEvaluation.Evaluate(cases, arm, floor: Math.Max(1, cases.Length));
        var control = ShuffledControls.VerdictShuffle(cases, arm, (set, run) => VerdictEvaluation.Evaluate(set, run, floor: Math.Max(1, set.Count)), Seed);
        var notProduced = derived.NotProduced.Count == 0 ? "every produced code" : "not produced here: " + string.Join("; ", derived.NotProduced.Select(static pair => $"{pair.Key} ({pair.Value})"));
        return (EvaluationCard.Verdict("refusal", $"the served operations, cases derived from the mount; {notProduced}", cases, report, control), derived);
    }

    [TestMethod]
    public async Task TheRefusalSetDerivedFromTheMountIsAnsweredCodeForCodeOnTheFixture()
    {
        // The fixture with what a refusal needs to be producible: two titled works, a later state with another
        // rule profile and two states on one date.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.AddTwoWorkTitlesAsync();
        var first = Day(fixture.ApplicabilityDate);
        var later = await fixture.AddStateAsync(first.AddDays(400).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "later");
        await fixture.AddRuleProfileToStateAsync(later.ExpressionIri, new string('a', 64));
        await fixture.AddStateAsync(first.AddDays(800).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "twin-a");
        await fixture.AddStateAsync(first.AddDays(800).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "twin-b");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var (set, derived) = RunRefusalGate(mount, fixture.Directory, Timelines(fixture.Directory));
        Assert.AreEqual(GateVerdict.Pass, set.Gates.Single().Verdict, "every derived request is refused with its own code, or answered where it must be");
        Assert.AreEqual(ControlVerdict.CaughtTheShuffle, set.Control.Verdict, set.Control.Reason);
        CollectionAssert.AreEquivalent(
            new[] { "text_withheld", "text_not_available" }, derived.NotProduced.Keys.ToArray(),
            "one mount cannot hold a withheld licence and text beside them; every other produced code is derived: " + string.Join("; ", derived.NotProduced.Select(static pair => pair.Key + " (" + pair.Value + ")")));
        Assert.AreEqual(18, set.CaseCount, "the fixture machine gates' 22 less the two codes another mount holds, the regulation's twin, which is the fixture's own, and the limit's case, which needs a handler of its own");
    }

    [TestMethod]
    public async Task TheFormatCaseNamesAFormatNoneOfTheWorksManifestationsCarries()
    {
        // Legilux publishes docx for most acts (22,908 docx manifestations in the population): the case that assumed docx
        // absent was answered on the real mount (2026-10-13). With a docx manifestation held, the case asks for the next
        // candidate, which the handler refuses.
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var docx = fixture.ExpressionIri + "/docx";
        await fixture.AddWorkFactAsync(fixture.ExpressionIri, ContractWire.NameOf(LuxembourgAssertionPredicate.IsEmbodiedBy),
            ContractWire.NameOf(LuxembourgAssertionFactKind.WemiStructural), "iri", docx);
        await fixture.AddWorkFactAsync(docx, ContractWire.NameOf(LuxembourgAssertionPredicate.UserFormat),
            ContractWire.NameOf(LuxembourgAssertionFactKind.ManifestationFormat), "iri", "http://data.legilux.public.lu/resource/authority/user-format/docx");
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var (set, derived) = RunRefusalGate(mount, fixture.Directory, Timelines(fixture.Directory));
        Assert.AreEqual("epub", System.Text.Json.JsonSerializer.SerializeToElement(derived.Requests["format-not-held"].Parameters).GetProperty("format").GetString());
        Assert.AreEqual(GateVerdict.Pass, set.Gates.Single().Verdict, "the handler refuses the format the case names");
    }

    [TestMethod]
    [DataRow("non_admitting_licence_scl")]
    [DataRow("conflict")]
    public async Task ARightsDispositionThatWithholdsTextIsDerivedAsTextWithheld(string disposition)
    {
        // Any disposition but the admitting one withholds the text (review of #839: `conflict` was missed).
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        await fixture.SetMemberRightsDispositionAsync(disposition);
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var (set, derived) = RunRefusalGate(mount, fixture.Directory, Timelines(fixture.Directory));
        Assert.IsFalse(derived.NotProduced.ContainsKey("text_withheld"), string.Join("; ", derived.NotProduced.Select(static pair => pair.Key + " (" + pair.Value + ")")));
        Assert.AreEqual("text_withheld", derived.Requests["rights-not-agreed"].Gold);
        Assert.AreEqual(GateVerdict.Pass, set.Gates.Single().Verdict, "the handler withholds it, as derived");
    }

    [TestMethod]
    public async Task AnEuIndexGivesItsOwnRefusalRequests()
    {
        // The GDPR mounted alone in its EU index: no Luxembourg state, yet the refusal set measures the EU's answers and
        // six EU refusals (three of search, in_force_on, which the EU capability manifest states refused, and two of verify
        // over the EU permalink grammar), and none of their codes is listed as not produced.
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var (set, derived) = RunRefusalGate(mount, fixture.Directory, Timelines(fixture.Directory));
        CollectionAssert.IsSubsetOf(
            new[] { "eu-search-held", "eu-unknown-celex", "eu-language-not-held", "eu-dated-search", "eu-in-force-on", "eu-pinned-provision", "eu-pinned-digest-not-held", "eu-provision-not-held" },
            derived.Requests.Keys.ToArray());
        // An unheld CELEX is out of the population the fixture's EU index states (R80), so out_of_corpus_scope is the EU's here.
        foreach (var code in new[] { "out_of_corpus_scope", "language_not_available", "retrieval_mode_unavailable", "pinned_digest_mismatch", "anchor_not_in_version" })
        {
            Assert.IsFalse(derived.NotProduced.ContainsKey(code), $"{code} is produced by an EU request");
        }

        Assert.AreEqual(GateVerdict.Pass, set.Gates.Single().Verdict, "every EU request is answered with its own code");
        Assert.AreEqual(ControlVerdict.CaughtTheShuffle, set.Control.Verdict, set.Control.Reason);
    }

    [TestMethod]
    public async Task AShortFirstEuArticleStillGivesTheEuRequests()
    {
        // The review of #846's reproduction: the EU index's first article holds no usable word, the others do; the EU
        // requests are still derived, from the first article that holds one.
        var fixture = await EuropeMountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var index = Path.Combine(fixture.Directory, V3CorpusMount.EuropeIndexFileName);
        using (var connection = EuropeIndexBuilder.Open(index, SqliteOpenMode.ReadWrite))
        {
            using var shorten = connection.CreateCommand();
            shorten.CommandText = "UPDATE articles SET searchable_text = 'a' WHERE article_identity_sha256 = (SELECT article_identity_sha256 FROM articles ORDER BY publisher_work_celex, language, article_identity_sha256 LIMIT 1)";
            Assert.AreEqual(1, shorten.ExecuteNonQuery());
        }

        var derived = RefusalCases(fixture.Directory, Timelines(fixture.Directory));
        CollectionAssert.IsSubsetOf(new[] { "eu-search-held", "eu-unknown-celex", "eu-language-not-held", "eu-dated-search" }, derived.Requests.Keys.ToArray(),
            string.Join(", ", derived.Requests.Keys));
    }
}
