using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts.Platform.Assistant;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Assistant;

/// <summary>
/// The pipeline over typed results an executor hands it (docs/ANSWER-DOSSIER-1.md §4 to §7): the verdict code computes, the
/// advice boundary's order (R45(b)), the split (R31), the follow-ups (R35), the disclosures (R38), the gates in the code
/// path, the presentation results apart (R45(a)) and a byte-identical replay. The operations' shapes here are the ones the
/// mount sends; the mount-backed suite (V3AssistantMountTests) runs the same pipeline over a real index.
/// </summary>
[TestClass]
public sealed class AssistantPipelineTests
{
    private const string Work = "loi-1991-08-10-n3";
    private const string Text = "Art. 1er. La profession d’avocat est une profession libérale et indépendante.";
    private static readonly string TextSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Text)));
    private static readonly string StateSha256 = new('a', 64);
    private static readonly V3AssistantEnvironment Environment = new("2026-10-28", "corpus-0123456789abcdef", new string('b', 64), ["fra"]);

    private sealed class FixedPlanner(string json) : IAssistantPlanner
    {
        public string PlannerId => "fixed-test-planner/1";

        public V3PlannerOutput Plan(V3PlannerInput input, CancellationToken cancellationToken) => new(json, 0);
    }

    private sealed class ThrowingPlanner : IAssistantPlanner
    {
        public string PlannerId => "throwing-test-planner/1";

        public V3PlannerOutput Plan(V3PlannerInput input, CancellationToken cancellationToken) => throw new HttpRequestException("the provider is unreachable");
    }

    /// <summary>An executor over fixed typed results: search opens or hits, the evidence bundle of one article, refusals.</summary>
    private sealed class Executor
    {
        public List<(string Operation, string Parameters)> Calls { get; } = [];

        public string SearchKind { get; init; } = "opens";

        public string? BundleText { get; init; }

        public bool Provisional { get; init; }

        public V3StepOutcome Run(string operationId, string parametersJson)
        {
            Calls.Add((operationId, parametersJson));
            using var parameters = JsonDocument.Parse(parametersJson);
            return operationId switch
            {
                "search" => Search(parametersJson),
                "evidence_bundle" => Bundle(parametersJson, parameters.RootElement),
                _ => Outcome(operationId, parametersJson, "work_record", new { requested_identifier = parameters.RootElement.GetProperty("identifier").GetString(), publisher = "lu-legilux" }),
            };
        }

        private V3StepOutcome Search(string parametersJson) => SearchKind switch
        {
            "opens" => Outcome("search", parametersJson, "quote", new
            {
                publisher = "lu-legilux",
                reference = new { outcome = "opens", opens = new { page = "reading", publisher = "lu-legilux", address = $"/lu-legilux/{Work}/fra/art_1er", work = Work, language = "fra", state_date = "2024-02-01", article_id = "art_1er" } },
                lanes_run = false,
                hits = Array.Empty<object>(),
            }),
            "two_works" => Outcome("search", parametersJson, "quote", new
            {
                publisher = "lu-legilux",
                reference = new { outcome = "no_reference" },
                lanes_run = true,
                hits = new[] { Hit(Work, "art_15"), Hit("loi-2000-01-01-n1", "art_2") },
            }),
            "two_articles" => Outcome("search", parametersJson, "quote", new
            {
                publisher = "lu-legilux",
                reference = new { outcome = "no_reference" },
                lanes_run = true,
                hits = new[] { Hit(Work, "art_15"), Hit(Work, "art_16") },
            }),
            "no_hit" => Outcome("search", parametersJson, "quote", new { publisher = "lu-legilux", reference = new { outcome = "no_reference" }, lanes_run = true, hits = Array.Empty<object>() }),
            "no_version" => Refusal("search", parametersJson, "no_version_for_date", new { requested_identifier = $"/lu-legilux/{Work}", requested_date = "2020-01-01", history_begins = "2024-02-01", nearest_earlier = (string?)null, nearest_later = "2024-02-01" }),
            _ => throw new InvalidOperationException(SearchKind),
        };

        private static object Hit(string work, string article) => new { work_key = work, publisher_id = article, applicability_date = "2024-02-01" };

        private V3StepOutcome Bundle(string parametersJson, JsonElement parameters) => Outcome("evidence_bundle", parametersJson, "evidence_bundle", new
        {
            publisher = "lu-legilux",
            work_key = Work,
            requested_identifier = parameters.GetProperty("identifier").GetString(),
            as_published = new { journal_officiel_iri = "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo" },
            states = new[]
            {
                new
                {
                    applicability_date = "2024-02-01",
                    applicable_to = (string?)null,
                    date_role = "applicability_in_file",
                    permalink = $"/lu-legilux/{Work}/2024-02-01--{StateSha256}/fra",
                    state_sha256 = StateSha256,
                    state_kind = "as_published",
                    provisional = Provisional,
                    language = "fra",
                    articles = new[]
                    {
                        new
                        {
                            publisher_id = "art_1er",
                            language = "fra",
                            text = BundleText ?? Text,
                            text_sha256 = TextSha256,
                            official_source = "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3/jo",
                            article_permalink = $"/lu-legilux/{Work}/2024-02-01--{StateSha256}/fra/art_1er",
                            validity_conflict = true,
                            article_valid_from = "2021-08-22",
                        },
                    },
                },
            },
        });

        private static V3StepOutcome Outcome(string operation, string parametersJson, string objectType, object result) =>
            new(operation, parametersJson, objectType, "answer", JsonSerializer.SerializeToElement(result), null, null);

        private static V3StepOutcome Refusal(string operation, string parametersJson, string code, object payload) =>
            new(operation, parametersJson, null, null, null, code, JsonSerializer.SerializeToElement(payload));
    }

    private static V3AnswerDossier Answered(string question, Executor? executor = null, IAssistantPlanner? planner = null)
    {
        var outcome = V3AssistantPipeline.Answer(new V3AssistantRequest(question), planner ?? new AssistantRuleTestPlanner(), (executor ?? new Executor()).Run, Environment, CancellationToken.None);
        return outcome is V3AskAnswered answered ? answered.Dossier : throw new AssertFailedException($"{question}: {outcome.Reason}");
    }

    private static string[] Kinds(V3DossierPart part) => part.Blocks.Select(static block => block is V3ClaimBlock claim ? claim.Claim.TemplateId : block.Kind).ToArray();

    [TestMethod]
    public void ALookupAnswersTheLawVerbatimAndDatedWithThePlanFirstAndItsDisclosures()
    {
        var executor = new Executor();
        var dossier = Answered("Que dit l'article 1er de la loi du 10 août 1991 ?", executor);
        Assert.AreEqual(V3AssistantVerdict.Answer, dossier.Verdict);
        Assert.AreEqual(V3AnswerLocale.Fr, dossier.Locale);
        Assert.AreEqual(V3AnswerTemplates.VerdictAnswer, dossier.Label.TemplateId);
        CollectionAssert.AreEqual(new[] { V3AnswerTemplates.NoticePreview, V3AnswerTemplates.NoticeAssembledByModel, V3AnswerTemplates.NoticeNotLegalAdvice }, dossier.Notices.Select(static notice => notice.TemplateId).ToArray(), "the preview line while the people's reviews are not done, then R33 and R50, on every answer");
        CollectionAssert.AreEqual(
            new[] { V3AnswerTemplates.PlanHeading, V3AnswerTemplates.PlanDateToday, V3AnswerTemplates.PlanSearchDated, V3AnswerTemplates.PlanFollowUpSingleArticle, V3AnswerTemplates.PlanEvidenceBundleArticle },
            dossier.PlanLines.Select(static line => line.TemplateId).ToArray(), "the validated plan, with today said aloud (R13), never a silent date");
        var quote = dossier.Quotes.Single();
        Assert.AreEqual(Text, quote.Text);
        Assert.AreEqual(TextSha256, quote.ArticleTextSha256);
        CollectionAssert.AreEqual(
            new[] { V3AnswerTemplates.LawQuoteHeading, "quote", V3AnswerTemplates.LawIntervalLuOpen, V3AnswerTemplates.LawContentHash, V3AnswerTemplates.LawOfficialSource, V3AnswerTemplates.LawPermalink, "disclosure", "disclosure" },
            Kinds(dossier.Parts[0]));
        CollectionAssert.AreEquivalent(new[] { V3DisclosureFlag.Authenticity, V3DisclosureFlag.ValidityConflict },
            dossier.Blocks.OfType<V3DisclosureBlock>().Select(static block => block.Flag).ToArray(), "R38: every flag on the served object disclosed");
        Assert.AreEqual(0, V3AnswerGates.NonTemplateStrings(dossier.ToUtf8()));

        // Execution went through the operations, with the user's words as the query (R34) and the date bound by code.
        Assert.AreEqual("search", executor.Calls[0].Operation);
        StringAssert.Contains(executor.Calls[0].Parameters, "\"query\":\"article 1er de la loi du 10 août 1991\"");
        StringAssert.Contains(executor.Calls[0].Parameters, "\"limit\":10", "R45(f): code caps the result size");
        StringAssert.Contains(executor.Calls[1].Parameters, "\"article\":\"art_1er\"");
        CollectionAssert.AreEqual(new[] { "result", "result" }, dossier.Trace.Steps.Select(static step => step.Outcome).ToArray());
    }

    [TestMethod]
    public void AProvisionalStateIsDisclosedAndAMisquotationIsNeverServed()
    {
        var provisional = Answered("Que dit l'article 1er de la loi du 10 août 1991 ?", new Executor { Provisional = true });
        CollectionAssert.Contains(provisional.Blocks.OfType<V3DisclosureBlock>().Select(static block => block.Flag).ToArray(), V3DisclosureFlag.Provisional);
        Assert.IsTrue(V3AnswerGates.DisclosureComplete(provisional));

        // An article whose text does not hash to the digest it is served with is a quotation the gate does not bind.
        var outcome = V3AssistantPipeline.Answer(
            new V3AssistantRequest("Que dit l'article 1er de la loi du 10 août 1991 ?"),
            new AssistantRuleTestPlanner(),
            new Executor { BundleText = Text + " (altered)" }.Run,
            Environment,
            CancellationToken.None);
        Assert.AreEqual(V3AskOutcomeReason.GateFailed, outcome.Reason);
        Assert.AreEqual(V3AskPresentationResult.AssistantUnavailable, ((V3AskPresented)outcome).Result);
    }

    [TestMethod]
    public void AWholeApplicationQuestionOpensWithTheBoundaryAndNamesNoAdviser()
    {
        var bare = Answered("Puis-je être licencié pendant un congé de maladie ?", new Executor { SearchKind = "no_hit" });
        Assert.AreEqual(V3AssistantVerdict.Refuse, bare.Verdict);
        Assert.IsTrue(bare.Parts[0].Application);
        CollectionAssert.AreEqual(new[] { V3AnswerTemplates.BoundaryWholeNoText, "handoff" }, Kinds(bare.Parts[0]), "R45(b): only a whole application opens with the refusal");
        Assert.AreEqual("guichet-lu", bare.Blocks.OfType<V3HandoffBlock>().Single().Entry.Id);
        Assert.IsFalse(bare.Blocks.OfType<V3HandoffBlock>().Single().Entry.ReviewedByPerson);
        Assert.IsFalse(bare.Blocks.OfType<V3ClaimBlock>().Any(static claim => claim.Claim.TemplateId == V3AnswerTemplates.BoundaryWhole),
            "R14's 'here is who can advise you' is never said while the registry names no adviser");

        var withLaw = Answered("Puis-je être sanctionné selon l'article 1er de la loi du 10 août 1991 ?");
        Assert.AreEqual(V3AssistantVerdict.Refuse, withLaw.Verdict);
        var kinds = Kinds(withLaw.Parts[0]);
        Assert.AreEqual(V3AnswerTemplates.BoundaryWholeNoAdviser, kinds[0]);
        Assert.AreEqual(V3AnswerTemplates.LawQuoteHeading, kinds[1], "then the law, verbatim and dated");
        Assert.AreEqual("handoff", kinds[^1]);
    }

    [TestMethod]
    public void AMixedQuestionSplitsAndTheAnsweredHalfNeverCarriesTheRefusedHalf()
    {
        var dossier = Answered("Que dit l'article 1er de la loi du 10 août 1991, et puis-je exercer sans inscription ?");
        Assert.AreEqual(V3AssistantVerdict.Split, dossier.Verdict);
        Assert.AreEqual(V3AnswerTemplates.VerdictSplit, dossier.Label.TemplateId);
        Assert.AreEqual(V3AnswerTemplates.PlanSplit, dossier.PlanLines[1].TemplateId);
        var (lookup, application) = (dossier.Parts[0], dossier.Parts[1]);
        Assert.IsFalse(lookup.Application);
        Assert.AreEqual(V3AssistantVerdict.Answer, lookup.Verdict);
        Assert.IsTrue(application.Application);
        Assert.AreEqual(V3AssistantVerdict.Refuse, application.Verdict);
        Assert.IsFalse(Kinds(lookup).Any(static kind => kind.StartsWith("boundary.", StringComparison.Ordinal)), "the answered half carries no boundary");
        var applied = Kinds(application);
        Assert.IsTrue(Array.IndexOf(applied, V3AnswerTemplates.BoundaryLine) >= 0 && Array.IndexOf(applied, V3AnswerTemplates.BoundaryNoAdviser) > Array.IndexOf(applied, V3AnswerTemplates.BoundaryLine),
            "R45(b): the law first, then one boundary line, then the handoff");
    }

    [TestMethod]
    public void ClarifyPointAndARefusalKeptVisibleWithItsNearestState()
    {
        var acts = Answered("Que dit la loi sur « assemblée générale » ?", new Executor { SearchKind = "two_works" });
        Assert.AreEqual(V3AssistantVerdict.Clarify, acts.Verdict);
        Assert.AreEqual(V3AnswerTemplates.ClarifyWhichAct, Kinds(acts.Parts[0])[0]);
        Assert.AreEqual(2, acts.Blocks.OfType<V3ActionBlock>().Count(), "one question, with the candidates as actions");
        Assert.AreEqual("not_fired", acts.Trace.Steps.Single(static step => step.StepId == "f1").Outcome, "R35: the follow-up's condition did not hold");

        var articles = Answered("Que dit la loi sur « assemblée générale » ?", new Executor { SearchKind = "two_articles" });
        Assert.AreEqual(V3AssistantVerdict.Clarify, articles.Verdict);
        Assert.AreEqual(V3AnswerTemplates.ClarifyWhichArticle, Kinds(articles.Parts[0])[0]);

        var none = Answered("Que dit la loi sur « bail commercial » ?", new Executor { SearchKind = "no_hit" });
        Assert.AreEqual(V3AssistantVerdict.Point, none.Verdict);
        Assert.AreEqual(V3AnswerTemplates.PointNoHit, Kinds(none.Parts[0])[0]);
        Assert.AreEqual("legilux", none.Blocks.OfType<V3HandoffBlock>().Single().Entry.Id, "the official source of the publisher searched");

        var past = Answered("Que disait l'article 1er de la loi du 10 août 1991 le 1er janvier 2020 ?", new Executor { SearchKind = "no_version" });
        Assert.AreEqual(V3AssistantVerdict.Point, past.Verdict);
        var refusal = past.Blocks.OfType<V3RefusalBlock>().Single();
        Assert.AreEqual("no_version_for_date", refusal.Code);
        Assert.AreEqual(V3AnswerTemplates.RefusalNoVersionForDate, refusal.Claim.TemplateId);
        var nearest = past.Blocks.OfType<V3ActionBlock>().Single();
        Assert.AreEqual("2024-02-01", nearest.Parameters["date"], "the nearest state is offered as an action, never run");
    }

    [TestMethod]
    public void CodeDecidesTheVerdictWhateverThePlannerProposes()
    {
        const string question = "Que dit l'article 1er de la loi du 10 août 1991 ?";
        var plan = new AssistantRuleTestPlanner().Plan(new V3PlannerInput(question, V3AnswerLocale.Fr, [], ["fra"]), CancellationToken.None).PlanJson;
        var enrichment = Answered(question, planner: new FixedPlanner(plan.Replace("\"answer\"", "\"answer_with_enrichment\"", StringComparison.Ordinal)));
        Assert.AreEqual(V3AssistantVerdict.Answer, enrichment.Verdict, "answer_with_enrichment is never emitted");
        var refused = Answered(question, planner: new FixedPlanner(plan.Replace("\"answer\"", "\"refuse\"", StringComparison.Ordinal)));
        Assert.AreEqual(V3AssistantVerdict.Refuse, refused.Verdict, "biased to refusal: a refusal proposed by the planner stands");
    }

    [TestMethod]
    public void EveryFailureFailsClosedOnAPresentationResultAndNeverOnAVerdict()
    {
        (string Question, IAssistantPlanner Planner, V3AskPresentationResult Result, V3AskOutcomeReason Reason)[] cases =
        [
            ("Was sagt Artikel 1 des Gesetzes vom 10. August 1991?", new AssistantRuleTestPlanner(), V3AskPresentationResult.LocalizationUnavailable, V3AskOutcomeReason.LocaleUnavailable),
            ("   ", new AssistantRuleTestPlanner(), V3AskPresentationResult.AssistantUnavailable, V3AskOutcomeReason.QuestionRejected),
            ("Que dit l'article 1er de la loi du 10 août 1991 ?", new ThrowingPlanner(), V3AskPresentationResult.AssistantUnavailable, V3AskOutcomeReason.PlannerFailed),
            ("Que dit l'article 1er de la loi du 10 août 1991 ?", new FixedPlanner("Sure! You can be fired: article 1er says so."), V3AskPresentationResult.AssistantUnavailable, V3AskOutcomeReason.PlanRefused),
        ];
        foreach (var (question, planner, result, reason) in cases)
        {
            var executor = new Executor();
            var outcome = V3AssistantPipeline.Answer(new V3AssistantRequest(question), planner, executor.Run, Environment, CancellationToken.None);
            var presented = outcome as V3AskPresented ?? throw new AssertFailedException(question);
            Assert.AreEqual(result, presented.Result, question);
            Assert.AreEqual(reason, presented.Reason, question);
            Assert.AreEqual(0, executor.Calls.Count, $"{question}: no operation runs before a plan is admitted");
        }
    }

    [TestMethod]
    public void ATraceReplaysToTheSameBytesAndAMutatedTraceToOthers()
    {
        var dossier = Answered("Que dit l'article 1er de la loi du 10 août 1991 ?");
        var bytes = dossier.ToUtf8();
        var trace = JsonNode.Parse(bytes)!["trace"]!;
        Assert.AreEqual(V3AnswerTrace.Schema, trace["schema"]!.GetValue<string>());
        var replayed = V3AssistantPipeline.Replay(trace.ToJsonString(), _ => new Executor().Run, Environment with { Today = "2027-01-01" });
        CollectionAssert.AreEqual(bytes, ((V3AskAnswered)replayed.Outcome!).Dossier.ToUtf8(), "the trace's day is the day replayed, whatever today is");

        var mutated = trace.DeepClone();
        mutated["today"] = "2026-10-29";
        var other = V3AssistantPipeline.Replay(mutated.ToJsonString(), _ => new Executor().Run, Environment);
        Assert.IsFalse(((V3AskAnswered)other.Outcome!).Dossier.ToUtf8().SequenceEqual(bytes), "the control: another day replays to other bytes");
        Assert.IsTrue(V3AssistantPipeline.Replay(trace.ToJsonString(), _ => new Executor().Run, Environment with { SnapshotSha256 = new string('c', 64) }).SnapshotMismatch);
    }
}
