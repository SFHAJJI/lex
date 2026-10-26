using System.Globalization;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

public sealed partial class V3MountedGatesTests
{
    /// <summary>
    /// The temporal oracle over <c>lex-v3-luxembourg-index/9</c> (the data design of 2026-10-14, §3.5), written apart from the
    /// reader and from the index's own chain: it reads each catalogue state's start and kind from its row, the publisher's
    /// end from the catalogue's facts (<c>work_facts</c>: one exact <c>dateEndApplicability</c>, of a consolidation or a code
    /// state, as index 9's state rule profile reads it), whether the index holds the state's text from the articles the state
    /// binds, and, for a state whose text is not held, whether its rights withheld it from the corpus file's member of its
    /// expression. It never reads <c>applicable_to</c>, <c>applicable_to_source</c>, <c>boundary</c>, <c>text_status</c> or the
    /// <c>intervals</c> table. It applies the specification's interval rules itself (§3.2's table; R21 for how a date is said):
    /// a state covers D when <c>from ≤ D &lt; to</c>, <c>to</c> the publisher's end, else the next dated state's start of the
    /// work's French chain (French rows and rows naming no expression), else none; a translation takes the interval of the
    /// French state it translates. What a request must select follows from the covering states: one with text, served; one
    /// without, <c>text_withheld</c> or <c>text_not_available</c> for <c>as_of</c>, and a row naming it for <c>in_force_on</c>;
    /// several held with one wording (the same rule profiles, the same articles by publisher id and tokens), served once (the
    /// act as published when exactly one is, else the state that begins last); several otherwise, <c>ambiguous_version</c>;
    /// none, <c>no_version_for_date</c> with its reason (before the history, a catalogue hole, after a closed end, the end day
    /// of a suspected inclusive end, where an invalid interval would apply, or no expression in the language asked).
    /// </summary>
    internal sealed class IntervalOracle : IDisposable
    {
        private const string French = "fra";
        private readonly SqliteConnection _connection;
        private readonly string _corpusPath;
        private readonly Dictionary<string, Row[]> _works = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _wordings = new(StringComparer.Ordinal);
        private Dictionary<(string Root, string Expression), string>? _memberOutcomes;

        /// <summary>One catalogue state as the oracle reads it, and the interval its own rules give it.</summary>
        internal sealed record Row(
            string Digest,
            DateOnly From,
            string Kind,
            string Language,
            string Expression,
            string LegalResource,
            string? TranslationOf,
            DateOnly? PublisherEnd,
            bool Held,
            string Profiles,
            string ArticlesJson)
        {
            public DateOnly? To { get; init; }

            public bool Invalid { get; init; }

            public bool InclusiveSuspected { get; init; }

            public bool Covers(DateOnly day) => !Invalid && From <= day && (To is null || day < To.Value);
        }

        /// <summary>What the rules select for one work, one language and one day.</summary>
        internal sealed record Selection(string Kind, Row? Row, string? Reason)
        {
            public const string Served = "served";
            public const string Textless = "textless";
            public const string Ambiguous = "ambiguous";
            public const string None = "none";
        }

        private IntervalOracle(SqliteConnection connection, string corpusPath, Dictionary<(string Root, string Expression), string>? memberOutcomes)
        {
            _connection = connection;
            _corpusPath = corpusPath;
            _memberOutcomes = memberOutcomes;
        }

        /// <summary>
        /// The oracle over a mount whose Luxembourg index is index 9, or null (an index of schema 8 or 6, or none). The members'
        /// outcomes come from the mount's corpus file, unless a fixture whose index no corpus describes gives its own.
        /// </summary>
        public static IntervalOracle? Open(string mountDirectory, IReadOnlyDictionary<(string Root, string Expression), string>? memberOutcomes = null)
        {
            var index = Path.Combine(mountDirectory, V3CorpusMount.IndexFileName);
            if (!File.Exists(index))
            {
                return null;
            }

            var connection = LuxembourgIndexBuilder.Open(index, SqliteOpenMode.ReadOnly);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('states') WHERE name = 'state_kind'";
                if ((long)command.ExecuteScalar()! == 0)
                {
                    connection.Dispose();
                    return null;
                }
            }

            return new IntervalOracle(connection, Path.Combine(mountDirectory, V3CorpusMount.CorpusFileName),
                memberOutcomes is null ? null : new Dictionary<(string, string), string>(memberOutcomes));
        }

        public void Dispose() => _connection.Dispose();

        /// <summary>Every catalogue state of one work, with the interval the oracle's own rules give each.</summary>
        public IReadOnlyList<Row> RowsOf(string work)
        {
            if (_works.TryGetValue(work, out var known))
            {
                return known;
            }

            var rows = new List<Row>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT state_sha256, applicability_date, state_kind, language, expression_iri, publisher_legal_resource_iri, translation_of, " +
                    "rule_profiles_json, article_identities_json FROM states WHERE work_key = $work";
                command.Parameters.AddWithValue("$work", work);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var kind = reader.GetString(2);
                    var resource = reader.GetString(5);
                    var articles = reader.GetString(8);
                    rows.Add(new Row(
                        reader.GetString(0), Day(reader.GetString(1)), kind, reader.GetString(3), reader.GetString(4), resource,
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        kind == "as_published" ? null : PublisherEndOf(resource),
                        HoldsText(articles), reader.GetString(7), articles));
                }
            }

            var completed = Complete(rows);
            _works[work] = completed;
            return completed;
        }

        /// <summary>The catalogue's one exact <c>dateEndApplicability</c> of a legal resource, or none (none stated, or several).</summary>
        private DateOnly? PublisherEndOf(string resource)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT object_value FROM work_facts WHERE subject_iri = $subject AND predicate = 'dateEndApplicability'";
            command.Parameters.AddWithValue("$subject", resource);
            using var reader = command.ExecuteReader();
            var values = new List<string>();
            while (reader.Read())
            {
                values.Add(reader.GetString(0));
            }

            return values.Count == 1 && DateOnly.TryParseExact(values[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
                ? end
                : null;
        }

        /// <summary>Whether the index holds a state's text: it binds articles, and the index holds the first of them.</summary>
        private bool HoldsText(string articlesJson)
        {
            var identities = JsonSerializer.Deserialize<string[]>(articlesJson) ?? [];
            if (identities.Length == 0)
            {
                return false;
            }

            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM articles WHERE article_identity_sha256 = $identity";
            command.Parameters.AddWithValue("$identity", identities[0]);
            return (long)command.ExecuteScalar()! == 1;
        }

        /// <summary>
        /// The interval of each row by the oracle's rules: over the French chain in date order, the publisher's end when stated,
        /// else the next later start (derived); an end on or before the start covers nothing; an end the day before the next
        /// start is a suspected inclusive end. A translation takes the interval of the French state of its legal resource whose
        /// expression it translates, when that state is a row; else its own end, with none derived.
        /// </summary>
        private static Row[] Complete(IReadOnlyList<Row> rows)
        {
            var chain = rows.Where(static row => IsFrench(row.Language)).ToArray();
            var starts = chain.Select(static row => row.From).Distinct().Order().ToArray();
            var completed = new Dictionary<string, Row>(StringComparer.Ordinal);
            foreach (var row in chain)
            {
                var next = starts.Where(start => start > row.From).Cast<DateOnly?>().FirstOrDefault();
                var end = row.PublisherEnd;
                completed[row.Digest] = row with
                {
                    To = end ?? next,
                    Invalid = end is { } stated && stated <= row.From,
                    InclusiveSuspected = end is { } closing && closing > row.From && next is { } following && closing == following.AddDays(-1),
                };
            }

            return rows.Select(row =>
            {
                if (completed.TryGetValue(row.Digest, out var french))
                {
                    return french;
                }

                var translated = row.TranslationOf is null
                    ? null
                    : chain.FirstOrDefault(candidate => candidate.Language == French && candidate.LegalResource == row.LegalResource && candidate.Expression == row.TranslationOf);
                if (translated is not null)
                {
                    var source = completed[translated.Digest];
                    return row with { To = source.To, Invalid = source.Invalid, InclusiveSuspected = source.InclusiveSuspected };
                }

                return row with { To = row.PublisherEnd, Invalid = row.PublisherEnd is { } end && end <= row.From };
            }).ToArray();
        }

        private static bool IsFrench(string language) => language.Length == 0 || language == French;

        /// <summary>The rows of one language's chain: French with the rows naming no expression, else the language's own.</summary>
        private IReadOnlyList<Row> Chain(string work, string language) =>
            RowsOf(work).Where(row => language == French ? IsFrench(row.Language) : row.Language == language).OrderBy(static row => row.From).ToArray();

        /// <summary>The languages a work holds text in: what a request with no language selects over.</summary>
        public IReadOnlyList<string> HeldLanguages(string work) =>
            RowsOf(work).Where(static row => row.Held).Select(static row => row.Language).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        /// <summary>The days a chain's rules change on: each start, and each end, with none outside the chain.</summary>
        public IReadOnlyList<DateOnly> Boundaries(string work, string? language)
        {
            var rows = language is null
                ? HeldLanguages(work).SelectMany(one => Chain(work, one))
                : Chain(work, language);
            return rows.SelectMany(static row => row.To is { } to ? new[] { row.From, to } : [row.From]).Distinct().Order().ToArray();
        }

        /// <summary>What the interval rules select for a work in one language on one day.</summary>
        public Selection Select(string work, string language, DateOnly day)
        {
            var chain = Chain(work, language);
            var covering = chain.Where(row => row.Covers(day)).ToArray();
            if (covering.Length == 1)
            {
                return new Selection(covering[0].Held ? Selection.Served : Selection.Textless, covering[0], null);
            }

            if (covering.Length > 1)
            {
                return OneWording(covering) is { } designated
                    ? new Selection(Selection.Served, designated, null)
                    : new Selection(Selection.Ambiguous, null, null);
            }

            // No state covers the day. Another language takes its intervals from the French chain, so where the French chain
            // has a state the reason is the French one's, or that the language holds no expression of the French state.
            if (language != French && Chain(work, French).Count > 0)
            {
                var french = Select(work, French, day);
                return french.Kind == Selection.None ? french : new Selection(Selection.None, null, "no_expression_in_language");
            }

            if (chain.Count == 0 || day < chain[0].From)
            {
                return new Selection(Selection.None, null, "before_history");
            }

            if (chain.Any(row => row.InclusiveSuspected && row.To == day))
            {
                return new Selection(Selection.None, null, "publisher_end_convention_unclear");
            }

            foreach (var invalid in chain.Where(row => row.Invalid && row.From <= day))
            {
                var following = chain.Select(static row => row.From).Where(start => start > invalid.From).Cast<DateOnly?>().FirstOrDefault();
                if (following is null || day < following.Value)
                {
                    return new Selection(Selection.None, null, "publisher_interval_invalid");
                }
            }

            return chain.Any(row => row.From > day)
                ? new Selection(Selection.None, null, "catalogue_hole")
                : new Selection(Selection.None, null, "after_closed_end");
        }

        /// <summary>
        /// Several covering states served once: every one holds its text, under the same rule profiles, with the same articles
        /// by publisher id and tokens; then the act as published when exactly one is, else the one that begins last; else none.
        /// </summary>
        private Row? OneWording(IReadOnlyList<Row> covering)
        {
            if (covering.Any(static row => !row.Held) ||
                covering.Select(static row => row.Profiles).Distinct(StringComparer.Ordinal).Count() != 1 ||
                covering.Select(row => Wording(row.ArticlesJson)).Distinct(StringComparer.Ordinal).Count() != 1)
            {
                return null;
            }

            var published = covering.Where(static row => row.Kind == "as_published").ToArray();
            if (published.Length == 1)
            {
                return published[0];
            }

            var latest = covering.Max(static row => row.From);
            var last = covering.Where(row => row.From == latest).ToArray();
            return last.Length == 1 ? last[0] : null;
        }

        /// <summary>A state's articles as a wording: each article's publisher id and tokens, in ordinal order.</summary>
        private string? Wording(string articlesJson)
        {
            if (_wordings.TryGetValue(articlesJson, out var known))
            {
                return known;
            }

            var lines = new List<string>();
            foreach (var identity in JsonSerializer.Deserialize<string[]>(articlesJson) ?? [])
            {
                using var command = _connection.CreateCommand();
                command.CommandText = "SELECT publisher_id, tokens_json FROM articles WHERE article_identity_sha256 = $identity";
                command.Parameters.AddWithValue("$identity", identity);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    lines.Add(reader.GetString(0) + "\t" + reader.GetString(1));
                }
            }

            var wording = string.Join('\n', lines.Order(StringComparer.Ordinal));
            _wordings[articlesJson] = wording;
            return wording;
        }

        /// <summary>
        /// The refusal of a state whose text is not held: <c>text_withheld</c> when the corpus file's member of its expression was
        /// withheld for its rights, else <c>text_not_available</c>.
        /// </summary>
        private string TextlessCode(Row row)
        {
            _memberOutcomes ??= MemberOutcomes(_corpusPath);
            return row.Expression.Length > 0 &&
                   _memberOutcomes.TryGetValue((row.LegalResource, row.Expression), out var outcome) && outcome == "rights_withheld"
                ? "text_withheld"
                : "text_not_available";
        }

        /// <summary>Each Luxembourg member's outcome by its selected root and expression, read from the corpus file.</summary>
        private static Dictionary<(string Root, string Expression), string> MemberOutcomes(string corpusPath)
        {
            using var stream = File.OpenRead(corpusPath);
            using var document = JsonDocument.Parse(stream);
            var outcomes = new Dictionary<(string, string), string>();
            foreach (var member in document.RootElement.GetProperty("members").EnumerateArray())
            {
                if (member.TryGetProperty("luxembourg_rights", out var rights) && rights.ValueKind == JsonValueKind.Object)
                {
                    var wemi = rights.GetProperty("selected_wemi");
                    var key = (wemi.GetProperty("root_iri").GetString()!, wemi.GetProperty("expression_iri").GetString()!);
                    var outcome = member.GetProperty("outcome").GetString()!;
                    // A withheld member of an expression says so, whatever another member of it says.
                    if (!outcomes.TryGetValue(key, out var seen) || outcome == "rights_withheld")
                    {
                        outcomes[key] = seen == "rights_withheld" ? seen : outcome;
                    }
                }
            }

            return outcomes;
        }

        private static string NoVersion(string reason) => $"refusal:no_version_for_date:{reason}";

        /// <summary>
        /// <c>as_of</c> for a work in a language, or with none: the state served in each language the work holds text in; an
        /// ambiguity in any language; when none is served, the refusal of the French chain (else of the first language held).
        /// </summary>
        public string AsOf(string work, string? language, DateOnly day)
        {
            if (language is not null)
            {
                return AsOfKey(Select(work, language, day));
            }

            var languages = HeldLanguages(work);
            var each = languages.Select(one => Select(work, one, day)).ToArray();
            if (each.Any(static selection => selection.Kind == Selection.Ambiguous))
            {
                return "refusal:ambiguous_version";
            }

            var served = each.Where(static selection => selection.Kind == Selection.Served).Select(static selection => selection.Row!.Digest).Order(StringComparer.Ordinal).ToArray();
            return served.Length > 0 ? string.Join('+', served) : AsOfKey(Select(work, RefusingLanguage(languages), day));
        }

        /// <summary>
        /// <c>in_force_on</c> for a named work, in a language or with none: a row per language that a state covers, naming the
        /// state served or the state whose text is not held; an ambiguity in any language refuses; no row at all is
        /// <c>as_of</c>'s refusal.
        /// </summary>
        public string InForceOn(string work, string? language, DateOnly day)
        {
            var languages = language is null ? HeldLanguages(work) : [language];
            var each = languages.Select(one => Select(work, one, day)).ToArray();
            if (each.Any(static selection => selection.Kind == Selection.Ambiguous))
            {
                return "refusal:ambiguous_version";
            }

            var rows = each.Where(static selection => selection.Kind is Selection.Served or Selection.Textless)
                .Select(static selection => selection.Row!.Digest).Order(StringComparer.Ordinal).ToArray();
            return rows.Length > 0 ? string.Join('+', rows) : AsOfKey(Select(work, language ?? RefusingLanguage(languages), day));
        }

        private static string RefusingLanguage(IReadOnlyList<string> languages) =>
            languages.Contains(French, StringComparer.Ordinal) ? French : languages.Order(StringComparer.Ordinal).First();

        private string AsOfKey(Selection selection) => selection.Kind switch
        {
            Selection.Served => selection.Row!.Digest,
            Selection.Textless => "refusal:" + TextlessCode(selection.Row!),
            Selection.Ambiguous => "refusal:ambiguous_version",
            _ => NoVersion(selection.Reason!),
        };

        private static DateOnly Day(string date) => DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
