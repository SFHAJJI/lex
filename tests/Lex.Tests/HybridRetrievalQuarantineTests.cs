using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Lex.Ask;
using Lex.Index;
using Lex.Mcp;

namespace Lex.Tests;

/// <summary>
/// The argument gate's quarantine of a retrieval mode the mounted corpus cannot serve.
///
/// <para>hybrid is not a planner mistake. The catalogue advertises it, the planner schema offers
/// it, and a model that reads "keyword or hybrid" picks hybrid for a concept question about as
/// often as one run in twelve. What it cannot do is know that semantic activation never passed on
/// this artifact set, so MCP answers every selected publisher with retrieval_mode_unavailable,
/// leaves the operation with no execution at all, and a question the keyword index answers well
/// comes back as not_available.</para>
///
/// <para>The refusal is MCP's honest answer and stays exactly where it is: a third party calling
/// the tool directly must never be served a different retrieval mode than the one it asked for
/// (Explicit_hybrid_search_is_typed_unavailable_instead_of_silently_running_keyword pins that, and
/// the deploy gate asserts it on every release). The assistant is the other boundary: it chooses
/// which operations to plan, so it is the one that must not plan an unservable one.</para>
/// </summary>
public sealed class HybridRetrievalQuarantineTests
{
    /// <summary>Two publishers mounted, neither able to serve a hybrid search. This is what both
    /// the local replica and the deployed candidate report on /readyz today, for different
    /// reasons: encoder_unavailable here, benchmark_identity_mismatch there.</summary>
    private static readonly CorpusVocabulary CannotServeHybrid =
        new(["eu-eurlex", "lu-legilux"], ["EU", "LU"], hybridRetrievalServable: false);

    /// <summary>The same corpus after activation passes.</summary>
    private static readonly CorpusVocabulary CanServeHybrid =
        new(["eu-eurlex", "lu-legilux"], ["EU", "LU"], hybridRetrievalServable: true);

    private const string ConceptQuestion = "responsibilities of a data protection officer";

    [Fact]
    public void A_planned_hybrid_search_is_repaired_to_keyword_when_nothing_mounted_can_serve_it()
    {
        var normalized = OperationArguments.Normalize("search", new JsonObject
        {
            ["query"] = ConceptQuestion,
            ["retrieval_mode"] = "hybrid",
        }, out var repairs, CannotServeHybrid);

        Assert.Equal("keyword", normalized["retrieval_mode"]!.GetValue<string>());
        Assert.Equal(["search.retrieval_mode quarantined"], repairs.ToArray());
    }

    // A repair nobody counts is silent drift, which is the whole reason this gate returns its
    // repairs instead of logging them. The frozen operation is where AskService reads them for the
    // planner_argument_repaired diagnostic and for the plan trace, so that is where this is pinned.
    [Fact]
    public void The_quarantine_travels_on_the_frozen_operation_where_every_repair_is_counted()
    {
        var plan = OperationPlan.FromPlannerOutput("req-1", "en", new JsonArray(new JsonObject
        {
            ["tool"] = "search",
            ["arguments"] = new JsonObject
            {
                ["query"] = ConceptQuestion,
                ["retrieval_mode"] = "hybrid",
            },
        }), vocabulary: CannotServeHybrid);

        var operation = Assert.Single(plan.Operations);
        Assert.Equal("keyword", operation.Arguments.GetProperty("retrieval_mode").GetString());
        Assert.Equal(["search.retrieval_mode quarantined"], operation.Repairs.ToArray());
        // Argument names and verbs only. These lines are logged.
        Assert.All(operation.Repairs, repair =>
        {
            Assert.DoesNotContain("hybrid", repair, StringComparison.Ordinal);
            Assert.DoesNotContain("protection", repair, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_planned_keyword_search_is_frozen_exactly_as_the_planner_wrote_it()
    {
        var normalized = OperationArguments.Normalize("search", new JsonObject
        {
            ["query"] = ConceptQuestion,
            ["retrieval_mode"] = "keyword",
        }, out var repairs, CannotServeHybrid);

        Assert.Equal("keyword", normalized["retrieval_mode"]!.GetValue<string>());
        Assert.Empty(repairs);
    }

    // The negative half: the quarantine is one value of one argument, not a new licence to rewrite
    // tuning arguments. Everything here is a value the corpus can serve, including the retrieval
    // mode nobody supplied, and none of it may be repaired.
    [Fact]
    public void No_argument_other_than_the_unservable_retrieval_mode_is_quarantined()
    {
        var search = OperationArguments.Normalize("search", new JsonObject
        {
            ["query"] = ConceptQuestion,
            ["fuzzy"] = "off",
            ["time_scope"] = "all_versions",
            ["limit"] = 5,
        }, out var searchRepairs, CannotServeHybrid);

        Assert.Empty(searchRepairs);
        Assert.Equal("off", search["fuzzy"]!.GetValue<string>());
        Assert.Equal("all_versions", search["time_scope"]!.GetValue<string>());
        // The absent mode is completed by the default, which is not a repair and never was.
        Assert.Equal("keyword", search["retrieval_mode"]!.GetValue<string>());

        var asOf = OperationArguments.Normalize("as_of", new JsonObject
        {
            ["work_query"] = "GDPR",
            ["date"] = "2021-01-01",
            ["mode"] = "full",
        }, out var asOfRepairs, CannotServeHybrid);

        Assert.Empty(asOfRepairs);
        Assert.Equal("full", asOf["mode"]!.GetValue<string>());
    }

    // Two different facts about the same argument, and the counts have to stay separable: a rising
    // dropped rate means the model is inventing values and the prompt or the schema is at fault; a
    // rising quarantined rate means the model is asking correctly for a mode this corpus cannot
    // serve, which is an activation signal and nobody's mistake.
    [Fact]
    public void An_invented_retrieval_mode_is_still_reported_as_dropped_rather_than_quarantined()
    {
        var normalized = OperationArguments.Normalize("search", new JsonObject
        {
            ["query"] = ConceptQuestion,
            ["retrieval_mode"] = "semantic",
        }, out var repairs, CannotServeHybrid);

        Assert.Equal("keyword", normalized["retrieval_mode"]!.GetValue<string>());
        Assert.Equal(["search.retrieval_mode dropped"], repairs.ToArray());
    }

    // The quarantine is a fact about this corpus, not a rule about hybrid. If it were the second,
    // activating semantic retrieval would leave the assistant permanently unable to plan the mode
    // it just paid to build, and nothing would say so.
    [Fact]
    public void The_same_planned_hybrid_survives_untouched_once_the_corpus_can_serve_it()
    {
        var normalized = OperationArguments.Normalize("search", new JsonObject
        {
            ["query"] = ConceptQuestion,
            ["retrieval_mode"] = "hybrid",
        }, out var repairs, CanServeHybrid);

        Assert.Equal("hybrid", normalized["retrieval_mode"]!.GetValue<string>());
        Assert.Empty(repairs);
    }

    // Where the answer comes from. No stamp, manifest or coverage row states semantic readiness,
    // because it is decided per publisher when this process opens its indexes: vectors on disk are
    // unservable without an encoder, and an encoder is unservable without vectors. The reader
    // behind MCP is the only component that knows, so the assistant asks it the question itself and
    // reads the typed refusal that comes back.
    [Fact]
    public void The_ask_layer_reads_servability_from_the_server_it_actually_holds()
    {
        using var fixture = new SearchFixture(semantic: false);
        using var activated = new SearchFixture(semantic: true);

        Assert.False(AskService.HybridRetrievalServable(fixture.Core));
        Assert.True(AskService.HybridRetrievalServable(activated.Core));
        // A build that mounted nothing serves no retrieval mode at all, and answers the probe with
        // its readiness object rather than a publisher array.
        Assert.False(AskService.HybridRetrievalServable(
            new McpCore(new Dictionary<string, LexIndexReader>())));
    }

    // The two halves joined: the probe decides, the gate acts on it, and an assistant built over an
    // unactivated corpus cannot freeze a hybrid search into a plan.
    [Fact]
    public void An_assistant_over_an_unactivated_corpus_plans_keyword_and_says_it_did()
    {
        using var fixture = new SearchFixture(semantic: false);
        var vocabulary = new CorpusVocabulary(
            fixture.Core.MountedPublishers, fixture.Core.MountedJurisdictions,
            AskService.HybridRetrievalServable(fixture.Core));

        var plan = OperationPlan.FromPlannerOutput("req-1", "en", new JsonArray(new JsonObject
        {
            ["tool"] = "search",
            ["arguments"] = new JsonObject
            {
                ["query"] = ConceptQuestion,
                ["retrieval_mode"] = "hybrid",
            },
        }), vocabulary: vocabulary);

        var operation = Assert.Single(plan.Operations);
        Assert.Equal("keyword", operation.Arguments.GetProperty("retrieval_mode").GetString());
        Assert.Equal(["search.retrieval_mode quarantined"], operation.Repairs.ToArray());
    }

    /// <summary>One mounted publisher over a temporary index, optionally with semantic vectors and
    /// an encoder, which is exactly what separates a reader that can serve hybrid from one that
    /// cannot.</summary>
    private sealed class SearchFixture : IDisposable
    {
        private readonly string _db = Path.Combine(
            Path.GetTempPath(), $"lex-hybrid-probe-{Guid.NewGuid():N}.db");
        private readonly string _vectors;
        private readonly ProbeEncoder _encoder = new();
        private readonly LexIndexReader _reader;

        public SearchFixture(bool semantic)
        {
            _vectors = Path.ChangeExtension(_db, ".vectors");
            var doc = new DocRow(
                "eu-eurlex:probe:2020-01-01", "eu-eurlex", "probe", "urn:probe", "REG", "en",
                "2020-01-01", null, "publisher", "2026-08-01T00:00:00Z", Withdrawn: false,
                TextAvailable: true, TextPublic: true, RecordSha: "abc", BodySha: null,
                SourceUri: "https://example.org", Title: "Probe", TitleShort: "Probe",
                Body: null, PublicationDate: "2020-01-01", StatusNote: null);
            const string text =
                "the data protection officer reports to the highest management level";
            var provision = new ProvisionRow(
                Rid: $"{doc.Key}|{doc.Language}|{doc.ValidFrom}", Seq: 0, Anchor: "art_1",
                ProvisionId: $"{doc.Key}#art_1", PType: "article", Num: "1", Heading: null,
                Path: null, ArticleValidFrom: null, WorkTitle: doc.Title, TextMd: text,
                TextSha: Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
            IndexBuilder.Build(_db, new Dictionary<string, string>
            {
                ["collection"] = "eu-eurlex",
                ["jurisdiction"] = "EU",
            }, [doc], [provision], [], [], null,
                semantic: semantic
                    ? new SemanticBuildOptions(_encoder, _vectors, "model-sha", "tokenizer-sha")
                    : null);
            _reader = semantic
                ? LexIndexReader.Open(_db, _encoder, _vectors)
                : LexIndexReader.Open(_db);
            Core = new McpCore(new Dictionary<string, LexIndexReader>
            {
                ["eu-eurlex"] = _reader,
            });
        }

        public McpCore Core { get; }

        public void Dispose()
        {
            _reader.Dispose();
            _encoder.Dispose();
            // The pooled SQLite handle outlives the reader, so a temporary file that will not
            // delete is normal here and is not a test result.
            foreach (var path in new[] { _db, _vectors })
                try { File.Delete(path); } catch (IOException) { }
        }
    }

    /// <summary>The smallest encoder that makes a reader hybrid-capable. What it embeds does not
    /// matter here: the probe reads envelope statuses, never hits.</summary>
    private sealed class ProbeEncoder : ITextEncoder
    {
        public string ModelId => "test/probe";
        public string ModelRevision => "test-revision";
        public int Dimensions => 4;

        public int CountTokens(string text) =>
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length + 2;

        public int PrefixLengthForTokens(string text, int maxTokens) => text.Length;

        public int SuffixStartForTokens(string text, int maxTokens) => 0;

        public float[] Encode(string text, EmbeddingInputKind kind)
        {
            var vector = new float[Dimensions];
            foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                vector[Math.Abs(StringComparer.Ordinal.GetHashCode(token) % Dimensions)] += 1;
            var norm = MathF.Sqrt(vector.Sum(value => value * value));
            if (norm == 0) return [1, 0, 0, 0];
            for (var index = 0; index < vector.Length; index++) vector[index] /= norm;
            return vector;
        }

        public void Dispose() { }
    }
}
