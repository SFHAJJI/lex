using System.Text.Json;
using Lex.V3.Contracts.Platform.Assistant;
using Lex.V3.Contracts.Platform.Assistant.Bricks;

namespace Lex.V3.Tests.Assistant;

/// <summary>
/// The bricks' test corpus: the law of 27 July 1997 on the insurance contract, in the French version applicable from
/// 2025-11-25, as the Lex index of mount d4-20261113 holds it (the publisher's text, CC BY 4.0, unchanged): every article
/// with its served text, that text's digest and its stored tokens. The tables are built from it exactly as the mount builds
/// them, and an executor serves its articles in the shape <c>evidence_bundle</c> answers.
/// </summary>
internal static class BricksFixture
{
    internal const string Work = "loi-1997-07-27-n10";
    internal const string Address = "/lu-legilux/" + Work;
    internal const string Today = "2026-11-18";

    private static readonly Lazy<(V4ActTables Tables, Dictionary<string, (string Text, string Sha256)> Articles, string State, string Date)> Loaded = new(Load);

    internal static V4ActTables Tables => Loaded.Value.Tables;

    internal static string StateSha256 => Loaded.Value.State;

    private static (V4ActTables, Dictionary<string, (string, string)>, string, string) Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Bricks", Work + ".fra.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var sources = root.GetProperty("articles").EnumerateArray().Select(static article => new V4ArticleSource(
            article.GetProperty("publisher_id").GetString()!,
            article.GetProperty("text").GetString()!,
            article.GetProperty("text_sha256").GetString()!,
            article.GetProperty("tokens").GetRawText())).ToList();
        var state = root.GetProperty("state_sha256").GetString()!;
        var date = root.GetProperty("applicability_date").GetString()!;
        return (
            V4ActTables.Build(root.GetProperty("work").GetString()!, state, date, "fra", sources),
            sources.ToDictionary(static source => source.PublisherId, static source => (source.Text, source.TextSha256), StringComparer.Ordinal),
            state,
            date);
    }

    /// <summary>The corpus the bricks read: the fixture's act on any date; every other act is not held.</summary>
    internal sealed class Corpus : IV4Corpus
    {
        public List<string> Asked { get; } = [];

        public HashSet<string> EuActsHeld { get; } = new(StringComparer.Ordinal);

        public V4ActAtDate Act(string work, string date)
        {
            Asked.Add(work + "@" + date);
            return work == Work
                ? new V4ActAtDate(Tables, new V4ActFacts(work, "held", Tables.Date, null, 11, Tables.Summary.Articles, Tables.Summary.Lines))
                : new V4ActAtDate(null, new V4ActFacts(work, "text_not_available", null, null, 0, 0, 0));
        }

        public bool HoldsEuAct(string celex) => EuActsHeld.Contains(celex);
    }

    /// <summary>A model that answers what a test scripts for each step, and records what it was sent.</summary>
    internal sealed class Model(Func<V4ModelCall, string> reply) : IV4Model
    {
        public List<V4ModelCall> Calls { get; } = [];

        public string ModelId => "scripted-model";

        public V4ModelReply Complete(V4ModelCall call, CancellationToken cancellationToken)
        {
            Calls.Add(call);
            return new V4ModelReply(reply(call), 1500, 1000, 100);
        }
    }

    /// <summary>
    /// The steps of a plan as the mount runs them: <c>evidence_bundle</c> of one article of the fixture's act, a search, and
    /// the record of an act for the other operations.
    /// </summary>
    internal sealed class Executor
    {
        public List<(string Operation, string Parameters)> Calls { get; } = [];

        /// <summary>The article a search opens (its words are a citation), or null when a search finds nothing.</summary>
        public string? SearchOpens { get; init; }

        /// <summary>Text to serve in place of an article's own (an article the index would hold with another wording).</summary>
        public Dictionary<string, string> Replaced { get; } = new(StringComparer.Ordinal);

        public V3StepOutcome Run(string operationId, string parametersJson)
        {
            Calls.Add((operationId, parametersJson));
            using var parameters = JsonDocument.Parse(parametersJson);
            if (operationId == "search")
            {
                // No hit; or, as the exact-reference phase does when the words are a citation, one article of the fixture's act.
                var found = SearchOpens is null
                    ? JsonSerializer.SerializeToElement(new { publisher = "lu-legilux", hits = Array.Empty<object>() })
                    : JsonSerializer.SerializeToElement(new
                    {
                        publisher = "lu-legilux",
                        hits = Array.Empty<object>(),
                        reference = new { phase = "exact_reference", outcome = "opens", opens = new { publisher = "lu-legilux", work = Work, article_id = SearchOpens } },
                    });
                return new V3StepOutcome(operationId, parametersJson, "search_results", "answer", found, null, null);
            }

            if (operationId != "evidence_bundle")
            {
                var record = JsonSerializer.SerializeToElement(new { requested_identifier = parameters.RootElement.TryGetProperty("identifier", out var named) ? named.GetString() : null, publisher = "lu-legilux" });
                return new V3StepOutcome(operationId, parametersJson, "work_record", "answer", record, null, null);
            }

            var article = parameters.RootElement.GetProperty("article").GetString()!;
            var (text, sha) = Loaded.Value.Articles[article];
            if (Replaced.TryGetValue(article, out var other))
            {
                text = other;
                sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(other)));
            }

            var permalink = Address + "/" + Loaded.Value.Date + "/fra/" + Loaded.Value.State;
            var value = JsonSerializer.SerializeToElement(new
            {
                publisher = "lu-legilux",
                work_key = Work,
                requested_identifier = parameters.RootElement.GetProperty("identifier").GetString(),
                states = new[]
                {
                    new
                    {
                        language = "fra",
                        applicability_date = Loaded.Value.Date,
                        state_sha256 = Loaded.Value.State,
                        date_role = "applicability",
                        state_kind = "consolidation",
                        permalink,
                        articles = new[]
                        {
                            new
                            {
                                publisher_id = article,
                                text,
                                text_sha256 = sha,
                                official_source = "http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10",
                                article_permalink = permalink + "#" + article,
                            },
                        },
                    },
                },
                citation_facts = new { journal_officiel_iri = "http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/jo" },
            });
            return new V3StepOutcome(operationId, parametersJson, "evidence_bundle", "answer", value, null, null);
        }
    }

    internal static readonly V3AssistantEnvironment Environment = new(Today, "corpus-0123456789abcdef", new string('b', 64), ["fra"]);

    /// <summary>An answer of the pipeline over the fixture, with the bricks as its planner.</summary>
    internal static V3AskOutcome Ask(string question, IV4Model? model = null, Executor? executor = null, Corpus? corpus = null) =>
        V3AssistantPipeline.Answer(
            new V3AssistantRequest(question),
            new V4BricksPlanner(corpus ?? new Corpus(), model),
            (executor ?? new Executor()).Run,
            Environment,
            CancellationToken.None);

    /// <summary>The line of the fixture's act with this identifier.</summary>
    internal static V4Line Line(string id) => Tables.Line(id) ?? throw new InvalidOperationException("The fixture holds no line " + id + ".");
}
