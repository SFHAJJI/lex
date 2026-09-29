using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// <c>ask</c> on a mounted corpus while the assistant is contained (Decisions 51 and 91): every question
/// answers the typed presentation result <c>assistant_v3_unavailable</c> on one fixed handoff card under
/// the <c>point</c> verdict, naming the deterministic operations that do answer; never a refusal code,
/// never an answer from held law, and nothing in the card depends on the question.
/// </summary>
[TestClass]
public sealed class V3CorpusAskMountTests
{
    private static string RawTarget => V3RestRouteBinding.Ask.RawTarget;

    [TestMethod]
    public void TheAskRouteIsTheServedBinding()
    {
        Assert.AreEqual("/api/v3/ask", RawTarget);
        Assert.AreEqual("ask", V3RestRouteBinding.Ask.OperationId);
        Assert.IsTrue(V3RestRouteBinding.Served.Contains(V3RestRouteBinding.Ask));
    }

    [TestMethod]
    public async Task EveryQuestionAnswersTheOneContainedCardUnderThePointVerdict()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        string[] questions =
        [
            "Can I be fired while on sick leave?",
            "Quel est le loyer maximal d'un bail d'habitation ?",
            $"/lu-legilux/{fixture.WorkKey}",
            "What did the GDPR say between 2018 and 2024?",
        ];
        var bodies = new List<string>();
        foreach (var question in questions)
        {
            var envelope = await EnvelopeAsync(mount, RawTarget, "ask", new { question });
            Assert.IsNull(envelope.Refusal, $"{question}: {envelope.Refusal?.Code}");
            Assert.AreEqual(V3Verdicts.Point, envelope.Verdict, question);
            Assert.AreEqual("success", envelope.Context.Status);
            Assert.AreEqual(PublisherId.LuLegilux, envelope.Context.Publisher);
            Assert.AreEqual("handoff_card", envelope.Result!.ObjectType);
            var card = envelope.Result.Value;
            Assert.AreEqual("assistant_v3_unavailable", card.GetProperty("presentation_result").GetString());
            StringAssert.Contains(card.GetProperty("presentation_note").GetString(), "not a refusal code");
            CollectionAssert.AreEqual(
                new[] { "51", "91" },
                card.GetProperty("containment").GetProperty("decisions").EnumerateArray().Select(static value => value.GetString()).ToArray());
            Assert.AreEqual("disabled", card.GetProperty("containment").GetProperty("model_gloss").GetString());
            Assert.AreEqual(JsonValueKind.False, card.GetProperty("question_read").ValueKind);
            Assert.DoesNotContain(question, card.GetRawText(), "the card never echoes the question.");
            bodies.Add(card.GetRawText());
        }

        Assert.AreEqual(1, bodies.Distinct(StringComparer.Ordinal).Count(), "the question is not read: every question answers the same card.");

        using var first = JsonDocument.Parse(bodies[0]);
        var actions = first.RootElement.GetProperty("deterministic_actions").EnumerateArray().ToArray();
        CollectionAssert.AreEqual(
            new[] { "resolve", "search", "as_of", "evidence_bundle" },
            actions.Select(static action => action.GetProperty("operation_id").GetString()).ToArray(),
            "resolver first, then search, then the text of a state.");
        var expectedRequired = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["resolve"] = ["identifier"],
            ["search"] = ["query", "language"],
            ["as_of"] = ["identifier", "date"],
            ["evidence_bundle"] = ["identifier", "date"],
        };
        foreach (var action in actions)
        {
            var operation = action.GetProperty("operation_id").GetString()!;
            Assert.AreEqual(
                V3RestRouteBinding.Served.Single(binding => binding.OperationId == operation).RawTarget,
                action.GetProperty("route").GetString(),
                operation);
            CollectionAssert.AreEqual(
                expectedRequired[operation],
                action.GetProperty("required_parameters").EnumerateArray().Select(static value => value.GetString()).ToArray(),
                $"{operation}: the parameters its reviewed request schema requires.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(action.GetProperty("answers").GetString()), operation);
            if (operation == "search")
            {
                CollectionAssert.AreEqual(
                    new[] { "fra" },
                    action.GetProperty("languages").EnumerateArray().Select(static value => value.GetString()).ToArray(),
                    "the languages this mount holds searchable text in.");
            }
            else
            {
                Assert.AreEqual(JsonValueKind.Null, action.GetProperty("languages").ValueKind, operation);
            }
        }

        // The search the card points to answers on this mount with the parameters it names.
        var search = await EnvelopeAsync(mount, V3RestRouteBinding.Search.RawTarget, "search", new { query = "loyer", language = "fra" });
        Assert.AreEqual(V3Verdicts.Answer, search.Verdict, search.Refusal?.Code);
    }

    [TestMethod]
    public async Task TheQuestionIsARequiredNonBlankStringAndNothingElseIsAccepted()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        foreach (var (scenario, parameters) in new (string, object)[]
                 {
                     ("no question", new { }),
                     ("a blank question", new { question = "   " }),
                     ("an empty question", new { question = "" }),
                     ("a question that is not a string", new { question = 7 }),
                     ("a parameter the schema does not declare", new { question = "Can I be fired?", language = "fr" }),
                 })
        {
            var context = await PostAsync(mount, RawTarget, JsonSerializer.Serialize(new { operation_id = "ask", parameters }));
            Assert.AreEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode, scenario);
            using var problem = JsonDocument.Parse(ResponseBytes(context));
            Assert.AreEqual("request_schema_invalid", problem.RootElement.GetProperty("code").GetString(), scenario);
        }
    }

    [TestMethod]
    public async Task WithoutTheLuxembourgIndexAskRefusesNoCorpusMountedAsEveryServedRouteDoes()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName));
        File.Delete(Path.Combine(fixture.Directory, V3CorpusMount.CapabilityManifestFileName));
        await fixture.AddEuropeCollisionAsync();
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);

        var envelope = await EnvelopeAsync(mount, RawTarget, "ask", new { question = "Can I be fired while on sick leave?" });
        Assert.AreEqual("no_corpus_mounted", envelope.Refusal?.Code);
        Assert.AreEqual("lu", envelope.Refusal!.HelpfulPayload.GetProperty("required_corpus").GetString());
    }
}
