using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Platform.Assistant.Bricks;

namespace Lex.V3.Api;

/// <summary>
/// The corpus as the bricks read it (V4, bricks A and B): an act's lines, definitions and references on a date, built from the
/// typed pieces the index keeps. Nothing is downloaded and no model is asked.
/// </summary>
/// <remarks>
/// <para>
/// <b>The version and the rights are the evidence step's.</b> The act is first asked of <c>evidence_bundle</c> itself, through
/// the same dispatch a plan's step runs: the state it answers for the date is the state the tables are built from, and a date
/// it refuses (no version, an ambiguous one, a text withheld by its rights, a text not held) gives no tables, with its
/// refusal's code as the text status. So a line the bricks cite, or hand to a model, is a line the answer may quote: the text
/// of an act whose rights withhold it is never read here.
/// </para>
/// <para>
/// <b>Kept in memory, by version.</b> The tables of a version are built once (a few milliseconds for a law of 150 articles)
/// and kept under the version's digest; which version a date reads is asked again for each date.
/// </para>
/// </remarks>
internal sealed partial class V3CorpusMount
{
    /// <summary>The corpus the bricks read, over this mount, with the clock a step is run on.</summary>
    internal IV4Corpus BricksCorpus(Func<DateTimeOffset> clock) => new BricksCorpusOverMount(this, clock ?? throw new ArgumentNullException(nameof(clock)));

    private sealed class BricksCorpusOverMount(V3CorpusMount mount, Func<DateTimeOffset> clock) : IV4Corpus
    {
        private const string French = "fra";

        private readonly ConcurrentDictionary<string, V4ActTables> _tables = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<(string Work, string Date), V4ActAtDate> _byDate = new();

        public V4ActAtDate Act(string work, string date)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(work);
            ArgumentException.ThrowIfNullOrWhiteSpace(date);
            if (_byDate.Count > 256)
            {
                // A day's dates are few; a client that asks many is not kept in memory for it.
                _byDate.Clear();
            }

            return _byDate.GetOrAdd((work, date), key => Read(key.Work, key.Date));
        }

        private V4ActAtDate Read(string work, string date)
        {
            if (mount._reader is null)
            {
                return new V4ActAtDate(null, new V4ActFacts(work, "no_corpus_mounted", null, null, 0, 0, 0));
            }

            var parameters = JsonSerializer.Serialize(new { identifier = "/lu-legilux/" + work, date, language = French });
            var outcome = mount.RunAssistantStep("evidence_bundle", parameters, clock());
            var versions = mount._reader.ResolveWorkStates(work).Count(state => string.Equals(state.Language, French, StringComparison.Ordinal));
            if (outcome.Refused || outcome.Result is not { } result ||
                !result.TryGetProperty("states", out var states) || states.ValueKind != JsonValueKind.Array || states.GetArrayLength() != 1)
            {
                return new V4ActAtDate(null, new V4ActFacts(work, outcome.RefusalCode ?? "text_not_available", null, null, versions, 0, 0));
            }

            var state = states[0];
            var digest = state.GetProperty("state_sha256").GetString()!;
            var from = state.GetProperty("applicability_date").GetString()!;
            var to = state.TryGetProperty("next_applicability_date", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
            var tables = _tables.GetOrAdd(digest, _ => Build(work, digest, from));
            return new V4ActAtDate(tables, new V4ActFacts(work, "held", from, to, versions, tables.Summary.Articles, tables.Summary.Lines));
        }

        private V4ActTables Build(string work, string stateSha256, string date)
        {
            // The order evidence_bundle serves the articles in: the publisher document's, else the reading's.
            var resolved = mount._reader!.ResolveStateArticles(stateSha256);
            var articles = resolved.Count > 0 && resolved.All(static article => article.DocumentPosition is not null)
                ? resolved.ToArray()
                : InLuxembourgReadingOrder(resolved, static article => article.PublisherId, static article => article.ArticleIdentitySha256);
            var sources = articles
                .Select(static article => new V4ArticleSource(
                    article.PublisherId,
                    article.Text,
                    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(article.Text))),
                    article.TokensJson))
                .ToList();
            return V4ActTables.Build("/lu-legilux/" + work, stateSha256, date, French, sources);
        }

        public bool HoldsEuAct(string celex)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(celex);
            if (mount._europeReader is null)
            {
                return false;
            }

            try
            {
                return !mount.RunAssistantStep("dossier", JsonSerializer.Serialize(new { identifier = celex }), clock()).Refused;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return false;
            }
        }
    }
}
