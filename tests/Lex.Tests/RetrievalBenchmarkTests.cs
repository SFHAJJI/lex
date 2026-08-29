using Lex.Index;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Lex.Tests;

public sealed class RetrievalBenchmarkTests
{
    [Fact]
    public void Public_suite_has_the_frozen_cross_publisher_shape()
    {
        var cases = Cases();

        Assert.Equal(200, cases.Count);
        Assert.Equal(200, cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(200, cases.Select(c => string.Join('|',
            c.Category, c.Collection, c.Split, c.Query, c.Language, c.TimeScope, c.AsOf,
            c.Hierarchy, c.Domain, c.ExpectedResolution, c.ExpectedRole, c.ExpectNoHits,
            string.Join(',', c.RelevantWorks))).Distinct(StringComparer.Ordinal).Count());
        var expectedCounts = new Dictionary<string, int>
        {
            ["exact"] = 30, ["temporal"] = 25, ["conceptual"] = 40,
            ["bilingual"] = 25, ["fuzzy"] = 15, ["hierarchy"] = 15,
            ["role"] = 10, ["comparison"] = 10, ["negative"] = 10,
            ["ambiguity"] = 10, ["gap"] = 10,
        };
        Assert.Equal(expectedCounts, cases.GroupBy(c => c.Category)
            .ToDictionary(group => group.Key, group => group.Count()));
        Assert.Contains(cases, c => c.Category == "exact" && c.Query == "GDPR"
            && c.RelevantWorks.SequenceEqual(["eu-eurlex:32016r0679"]));
        Assert.Contains(cases, c => c.Category == "exact" && c.Query == "Code du travail"
            && c.RelevantWorks.SequenceEqual(["lu-legilux:loi-2006-07-31-n2"]));
        Assert.All(cases, c =>
        {
            Assert.NotEmpty(c.Explanation);
            Assert.Equal("engineer-reviewed", c.ReviewStatus);
            Assert.Contains(c.Collection, new[] { "eu-eurlex", "lu-legilux" });
            Assert.Contains(c.Split, new[] { "tuning", "holdout" });
            Assert.All(c.RelevantWorks, work => Assert.StartsWith(c.Collection + ":", work));
        });
        Assert.Contains(cases, c => c.Split == "tuning" && c.Collection == "eu-eurlex");
        Assert.Contains(cases, c => c.Split == "holdout" && c.Collection == "eu-eurlex");
        Assert.Contains(cases, c => c.Split == "tuning" && c.Collection == "lu-legilux");
        Assert.Contains(cases, c => c.Split == "holdout" && c.Collection == "lu-legilux");
        Assert.All(cases.Where(c => c.Category is "negative" or "gap"), c =>
        {
            Assert.Empty(c.RelevantWorks);
            Assert.True(c.ExpectNoHits);
        });
        Assert.All(cases.Where(c => c.Category == "comparison"), c =>
            Assert.Equal(2, c.RelevantWorks.Count));
        Assert.All(cases.Where(c => c.Category == "role"), c =>
            Assert.Contains(c.ExpectedRole, new[] { "delegated", "implementing" }));
        var filterDomains = cases.Where(c => c.Category == "hierarchy")
            .Select(c => c.Domain).Where(d => d is not null).Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var expectedDomains = new HashSet<string>(
        [
            "financial-services", "aml-corporate", "competition", "tax", "employment",
            "consumer-environment", "procurement-and-ip", "judicial-cooperation", "primary-eu-law",
        ], StringComparer.Ordinal);
        Assert.True(expectedDomains.IsSubsetOf(filterDomains),
            $"Missing filter domains: {string.Join(", ", expectedDomains.Except(filterDomains))}");
        var temporalDates = cases.Where(c => c.Category == "temporal" && c.AsOf is not null)
            .GroupBy(c => c.RelevantWorks.Single())
            .FirstOrDefault(group => group.Select(c => c.AsOf).Distinct(StringComparer.Ordinal).Count() > 1);
        Assert.NotNull(temporalDates);
        Assert.Contains(temporalDates!, c => c.Split == "tuning");
        Assert.Contains(temporalDates!, c => c.Split == "holdout");
    }

    [Fact]
    public void Metrics_compare_canonical_collection_and_work_identity()
    {
        var wrongPublisher = Doc("lu-legilux", "shared");
        var hit = new RetrievalHit(wrongPublisher,
            new ProvisionRow("rid", 0, "art_1", "p", "article", "1", null, null, null,
                "title", "text", new string('a', 64)), "text", 1, ["keyword"]);
        var benchmarkCase = new RetrievalBenchmarkCase(
            "identity-001", "exact", "shared", "en", "all_versions", null,
            ["eu-eurlex:shared"], "Canonical identity includes the collection.",
            "engineer-reviewed", Collection: "eu-eurlex", Split: "holdout");

        var metrics = RetrievalBenchmarkRunner.Evaluate("identity", [benchmarkCase],
            _ => new SearchExecution("keyword", [hit], [], new SearchQueryPlan(
                "shared", "shared", [], null, null, false)), null);

        Assert.False(metrics.Mrr.HasMeasuredValue);
        Assert.Equal(0, metrics.Mrr.Denominator);
        Assert.Equal("insufficient_denominator", metrics.Mrr.Status);
        Assert.Equal(0, MetricValue(metrics.WorkMrr));
        Assert.Equal(0, MetricValue(metrics.WorkRecallAt10));
        Assert.Equal(0, MetricValue(metrics.WorkNdcgAt10));
    }

    [Fact]
    public void Negative_metrics_have_explicit_denominators_and_no_division_by_zero()
    {
        var benchmarkCase = new RetrievalBenchmarkCase(
            "negative-001", "negative", "zxqv", "en", "all_versions", null, [],
            "No held work is relevant.", "engineer-reviewed", Collection: "eu-eurlex",
            Split: "holdout", ExpectedResolution: "not_requested", ExpectNoHits: true);

        var metrics = RetrievalBenchmarkRunner.Evaluate("negative", [benchmarkCase],
            _ => new SearchExecution("keyword", [], [], new SearchQueryPlan(
                "zxqv", "zxqv", [], null, null, false)), null);

        Assert.Equal(1, MetricValue(metrics.NoHitAccuracy));
        Assert.Equal(1, metrics.NoHitAccuracy.Denominator);
        Assert.Equal("measured", metrics.NoHitAccuracy.Status);
        Assert.Equal(1, MetricValue(metrics.ResolutionAccuracy));
        Assert.Equal(1, metrics.ResolutionAccuracy.Denominator);
        Assert.False(metrics.Mrr.HasMeasuredValue);
        Assert.Equal(0, metrics.Mrr.Denominator);
        Assert.Equal("insufficient_denominator", metrics.Mrr.Status);

        var json = System.Text.Json.JsonSerializer.Serialize(metrics,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
            });
        Assert.Contains("\"value\":null", json);
        Assert.Contains("\"denominator\":0", json);
        Assert.Contains("\"status\":\"insufficient_denominator\"", json);
    }

    [Fact]
    public void Empty_metric_families_are_insufficient_instead_of_perfect()
    {
        var metrics = RetrievalBenchmarkRunner.Evaluate(
            "empty", Array.Empty<RetrievalBenchmarkCase>(),
            _ => throw new InvalidOperationException("An empty stage must not search."), null);

        var observations = new[]
        {
            metrics.Mrr, metrics.RecallAt10, metrics.NdcgAt10,
            metrics.WorkMrr, metrics.WorkRecallAt10, metrics.WorkNdcgAt10,
            metrics.ExactFirstAccuracy, metrics.TemporalLeakageFailures,
            metrics.P50Ms, metrics.P95Ms, metrics.P99Ms, metrics.NoHitAccuracy,
            metrics.ResolutionAccuracy, metrics.RoleIntentAccuracy,
        };
        Assert.All(observations, observation =>
        {
            Assert.False(observation.HasMeasuredValue);
            Assert.Throws<InvalidOperationException>(() => observation.RequireMeasuredValue());
            Assert.Equal(0, observation.Denominator);
            Assert.Equal("insufficient_denominator", observation.Status);
        });
    }

    [Fact]
    public void Anchor_qrels_give_zero_credit_to_the_wrong_provision_in_the_right_work()
    {
        var document = Doc("eu-eurlex", "known");
        var wrongProvision = new RetrievalHit(document,
            new ProvisionRow("rid", 0, "art_wrong", "p", "article", "wrong", null, null, null,
                "title", "text", new string('a', 64)), "text", 1, ["keyword"]);
        var benchmarkCase = new RetrievalBenchmarkCase(
            "anchor-001", "conceptual", "known", "en", "all_versions", null,
            ["eu-eurlex:known"], "The operative provision is reviewed.",
            "engineer-reviewed", Collection: "eu-eurlex", Split: "holdout",
            RelevantAnchors: [new("eu-eurlex:known", "art_right", 3)]);

        var metrics = RetrievalBenchmarkRunner.Evaluate("anchor", [benchmarkCase],
            _ => new SearchExecution("keyword", [wrongProvision], [], null), null);

        Assert.Equal(0, MetricValue(metrics.Mrr));
        Assert.Equal(1, metrics.Mrr.Denominator);
        Assert.Equal(0, MetricValue(metrics.RecallAt10));
        Assert.Equal(0, MetricValue(metrics.NdcgAt10));
        Assert.Equal(1, MetricValue(metrics.WorkMrr));
        Assert.Equal(1, MetricValue(metrics.WorkRecallAt10));
        Assert.Equal(1, MetricValue(metrics.WorkNdcgAt10));

        var secondary = wrongProvision with
        {
            Provision = wrongProvision.Provision with { Anchor = "art_secondary" },
        };
        var gradedCase = benchmarkCase with
        {
            RelevantAnchors =
            [
                new("eu-eurlex:known", "art_right", 3),
                new("eu-eurlex:known", "art_secondary", 1),
            ],
        };
        var gradedMetrics = RetrievalBenchmarkRunner.Evaluate("anchor", [gradedCase],
            _ => new SearchExecution("keyword", [secondary], [], null), null);
        Assert.Equal(0.5, MetricValue(gradedMetrics.RecallAt10));
        Assert.Equal(1, gradedMetrics.RecallAt10.Denominator);
    }

    [Fact]
    public void Metric_tuple_coherence_and_the_explainer_renderer_fail_closed()
    {
        var insufficient = RetrievalMetricObservation.Insufficient();
        var measured = RetrievalMetricObservation.Measured(12.5, 8);

        Assert.True(insufficient.IsStructurallyCoherent());
        Assert.True(measured.IsStructurallyCoherent());
        Assert.False(insufficient.HasMeasuredValue);
        Assert.Throws<InvalidOperationException>(() => insufficient.RequireMeasuredValue());
        Assert.True(measured.HasMeasuredValue);
        Assert.Equal(12.5, measured.RequireMeasuredValue());
        Assert.False(RetrievalMetricObservation.FromSerialized(1, 0, "measured")
            .IsStructurallyCoherent());
        Assert.False(RetrievalMetricObservation.FromSerialized(null, 8, "insufficient_denominator")
            .IsStructurallyCoherent());
        Assert.False(RetrievalMetricObservation.FromSerialized(null, 0, "not_measured")
            .IsStructurallyCoherent());
        Assert.False(RetrievalMetricObservation.FromSerialized(double.NaN, 8, "measured")
            .HasMeasuredValue);
        Assert.False(RetrievalMetricObservation.FromSerialized(
            double.PositiveInfinity, 8, "measured").HasMeasuredValue);
        Assert.Equal("insufficient_denominator (n=0)",
            Lex.Web.ExplainerEndpoints.FormatBenchmarkMetric(insufficient, "0.0"));
        Assert.Equal("12.5 ms (n=8)",
            Lex.Web.ExplainerEndpoints.FormatBenchmarkMetric(measured, "0.0", " ms"));
        Assert.Equal("invalid_metric", Lex.Web.ExplainerEndpoints.FormatBenchmarkMetric(
            RetrievalMetricObservation.FromSerialized(1, 0, "measured"), "0.0"));
    }

    [Fact]
    public void Metric_observations_make_absent_values_unrepresentable_in_comparisons()
    {
        static string Signature(System.Reflection.MethodInfo method) =>
            $"{method.ReturnType} {method.Name}("
            + $"{string.Join(',', method.GetParameters().Select(parameter => parameter.ParameterType))})";

        var type = typeof(RetrievalMetricObservation);

        Assert.Equal(typeof(object), type.BaseType);
        Assert.Empty(type.GetConstructors());
        var interfaces = type.GetInterfaces().OrderBy(item => item.FullName, StringComparer.Ordinal)
            .ToArray();
        var interfaceBindings = interfaces.SelectMany(interfaceType =>
        {
            var map = type.GetInterfaceMap(interfaceType);
            return map.InterfaceMethods.Select((method, index) =>
                $"{interfaceType}::{Signature(method)} -> {Signature(map.TargetMethods[index])}");
        }).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal([
            "System.IEquatable`1[Lex.Index.RetrievalMetricObservation]::System.Boolean Equals(Lex.Index.RetrievalMetricObservation) -> System.Boolean Equals(Lex.Index.RetrievalMetricObservation)",
        ], interfaceBindings);
        Assert.Equal([
            "System.IEquatable`1[Lex.Index.RetrievalMetricObservation]",
        ], interfaces.Select(item => item.ToString()).ToArray());
        Assert.Empty(type.GetFields());
        Assert.Equal(["Denominator", "HasMeasuredValue", "Status"], type.GetProperties()
            .Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal([
            "Lex.Index.RetrievalMetricObservation <Clone>$()",
            "Lex.Index.RetrievalMetricObservation Insufficient()",
            "Lex.Index.RetrievalMetricObservation Measured(System.Double,System.Int32)",
            "System.Boolean Equals(Lex.Index.RetrievalMetricObservation)",
            "System.Boolean Equals(System.Object)",
            "System.Boolean get_HasMeasuredValue()",
            "System.Boolean IsStructurallyCoherent()",
            "System.Boolean op_Equality(Lex.Index.RetrievalMetricObservation,Lex.Index.RetrievalMetricObservation)",
            "System.Boolean op_Inequality(Lex.Index.RetrievalMetricObservation,Lex.Index.RetrievalMetricObservation)",
            "System.Double RequireMeasuredValue()",
            "System.Int32 GetHashCode()",
            "System.Int32 get_Denominator()",
            "System.String get_Status()",
            "System.String ToString()",
        ], type.GetMethods()
            .Where(method => method.DeclaringType == type)
            .Select(Signature)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray());

        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        };
        var json = System.Text.Json.JsonSerializer.Serialize(
            RetrievalMetricObservation.Measured(12.5, 8), options);
        Assert.Equal("{\"value\":12.5,\"denominator\":8,\"status\":\"measured\"}", json);
        var measured = System.Text.Json.JsonSerializer.Deserialize<RetrievalMetricObservation>(
            json, options);
        Assert.NotNull(measured);
        Assert.Equal(12.5, MetricValue(measured));
        var insufficient = System.Text.Json.JsonSerializer.Deserialize<RetrievalMetricObservation>(
            "{\"value\":null,\"denominator\":0,\"status\":\"insufficient_denominator\"}", options);
        Assert.NotNull(insufficient);
        Assert.False(insufficient.HasMeasuredValue);
        Assert.Throws<InvalidOperationException>(() => insufficient.RequireMeasuredValue());
    }

    [Fact]
    public void Lifted_null_gate_cannot_pass_an_absent_metric()
    {
        static bool PassesFloor(RetrievalMetricObservation observation) =>
            observation.HasMeasuredValue && !(observation.RequireMeasuredValue() < 0.5);

        Assert.False(PassesFloor(RetrievalMetricObservation.Insufficient()));
        Assert.True(PassesFloor(RetrievalMetricObservation.Measured(0.5, 8)));
    }

    [Fact]
    public void Negative_controls_are_derived_from_the_hybrid_holdout_arm()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Lex.Index", "RetrievalBenchmark.cs"));

        Assert.Contains(
            "RetrievalBenchmarkControls.ShuffledTop10(caseSet, hybridHoldoutEvaluation)", source);
        Assert.Contains(
            "RetrievalBenchmarkControls.QrelsShuffle(caseSet, hybridHoldoutEvaluation)", source);
        Assert.DoesNotContain(
            "RetrievalBenchmarkControls.ShuffledTop10(caseSet, keywordHoldoutEvaluation)", source);
        Assert.DoesNotContain(
            "RetrievalBenchmarkControls.QrelsShuffle(caseSet, keywordHoldoutEvaluation)", source);
    }

    [Fact]
    public void Shuffled_top10_v2_is_deterministic_order_sensitive_and_nonranking_identical()
    {
        RetrievalHit Hit(string anchor) => new(Doc("eu-eurlex", "known"),
            new ProvisionRow("rid-" + anchor, 0, anchor, "p-" + anchor, "article", anchor,
                null, null, null, "title", "text", new string('a', 64)),
            "text", 1, ["keyword"]);
        var benchmarkCase = new RetrievalBenchmarkCase(
            "shuffle-001", "conceptual", "query", "en", "all_versions", null,
            ["eu-eurlex:known"], "Two graded anchors make ordering observable.",
            "engineer-reviewed", Collection: "eu-eurlex", Split: "holdout",
            ExpectedResolution: "resolved", ExpectedRole: "implementing",
            RelevantAnchors:
            [
                new("eu-eurlex:known", "art_a", 3),
                new("eu-eurlex:known", "art_b", 1),
            ]);
        var caseSet = new RetrievalBenchmarkCaseSet([benchmarkCase], new string('a', 64));
        var baseline = RetrievalBenchmarkEvaluation.Evaluate("keyword-holdout", [benchmarkCase],
            _ => new SearchExecution("keyword", [Hit("art_a"), Hit("art_b"), Hit("art_c")], [],
                new SearchQueryPlan("query", "query", [], null, "implementing", false,
                    WorkResolutionStatus: "resolved")), null);

        var first = RetrievalBenchmarkControls.ShuffledTop10(caseSet, baseline);
        var second = RetrievalBenchmarkControls.ShuffledTop10(caseSet, baseline);

        Assert.Equal(first.Result.Schema, second.Result.Schema);
        Assert.Equal(first.Result.Outcome, second.Result.Outcome);
        Assert.Equal(first.Result.EligibleDenominator, second.Result.EligibleDenominator);
        Assert.Equal(first.Result.FailedGateNames, second.Result.FailedGateNames);
        Assert.Equal(first.Result.AnchorNdcgAt10, second.Result.AnchorNdcgAt10);
        Assert.Equal(first.Evaluation.CaseResults.Select(row =>
                string.Join('|', row.CaseId, string.Join(',', row.RankedCoordinatesAt10))),
            second.Evaluation.CaseResults.Select(row =>
                string.Join('|', row.CaseId, string.Join(',', row.RankedCoordinatesAt10))));
        Assert.Equal("shuffled-top10/2", first.Result.Schema);
        Assert.Equal("detected", first.Result.Outcome);
        Assert.Equal("product_gate", first.Result.Severity);
        Assert.Equal(1, first.Result.EligibleDenominator);
        Assert.True(first.Result.MembershipIdentical);
        Assert.True(first.Result.NonRankingIdentical);
        Assert.NotEmpty(first.Result.FailedGateNames);
        Assert.NotEqual(baseline.CaseResults[0].RankedCoordinatesAt10,
            first.Evaluation.CaseResults[0].RankedCoordinatesAt10);
        Assert.NotEqual(
            RetrievalBenchmarkControls.ShuffledTop10SortKey(
                caseSet.Sha256, "eu-eurlex", "shuffle-001", 0,
                "eu-eurlex:known#art_a"),
            RetrievalBenchmarkControls.ShuffledTop10SortKey(
                caseSet.Sha256, "eu-eurlex", "shuffle-002", 0,
                "eu-eurlex:known#art_a"));
    }

    [Fact]
    public void Shuffled_top10_v2_rotates_an_unchanged_eligible_order_left()
    {
        const string collection = "eu-eurlex";
        const string firstCoordinate = "eu-eurlex:known#art_a";
        const string secondCoordinate = "eu-eurlex:known#art_b";
        var digest = new string('c', 64);
        var caseId = Enumerable.Range(0, 10_000).Select(index => $"rotation-{index}")
            .First(id => string.CompareOrdinal(
                RetrievalBenchmarkControls.ShuffledTop10SortKey(
                    digest, collection, id, 1, firstCoordinate),
                RetrievalBenchmarkControls.ShuffledTop10SortKey(
                    digest, collection, id, 2, secondCoordinate)) < 0);
        RetrievalHit Hit(string anchor) => new(Doc(collection, "known"),
            new ProvisionRow("rid-" + anchor, 0, anchor, "p-" + anchor, "article", anchor,
                null, null, null, "title", "text", new string('a', 64)),
            "text", 1, ["keyword"]);
        var benchmarkCase = new RetrievalBenchmarkCase(
            caseId, "conceptual", "query", "en", "all_versions", null,
            ["eu-eurlex:known"], "The unchanged hash order exercises the rotation guard.",
            "engineer-reviewed", Collection: collection, Split: "holdout",
            RelevantAnchors:
            [
                new("eu-eurlex:known", "art_a", 3),
                new("eu-eurlex:known", "art_b", 1),
            ]);
        var caseSet = new RetrievalBenchmarkCaseSet([benchmarkCase], digest);
        var baseline = RetrievalBenchmarkEvaluation.Evaluate("keyword-holdout", [benchmarkCase],
            _ => new SearchExecution("keyword", [Hit("art_a"), Hit("art_b")], [], null), null);

        var control = RetrievalBenchmarkControls.ShuffledTop10(caseSet, baseline);

        Assert.Equal("detected", control.Result.Outcome);
        Assert.Equal([secondCoordinate, firstCoordinate],
            control.Evaluation.CaseResults[0].RankedCoordinatesAt10);
        Assert.True(control.Result.UnrelatedDenominatorsAndGatesIdentical);
    }

    [Fact]
    public void Shuffled_top10_v2_keeps_exact_anchor_cases_in_the_ranking_denominator()
    {
        const string collection = "eu-eurlex";
        const string firstCoordinate = "eu-eurlex:known#art_a";
        const string secondCoordinate = "eu-eurlex:known#art_b";
        var digest = new string('d', 64);
        var caseId = Enumerable.Range(0, 10_000).Select(index => $"exact-rotation-{index}")
            .First(id => string.CompareOrdinal(
                RetrievalBenchmarkControls.ShuffledTop10SortKey(
                    digest, collection, id, 1, firstCoordinate),
                RetrievalBenchmarkControls.ShuffledTop10SortKey(
                    digest, collection, id, 2, secondCoordinate)) < 0);
        RetrievalHit Hit(string anchor) => new(Doc(collection, "known"),
            new ProvisionRow("rid-" + anchor, 0, anchor, "p-" + anchor, "article", anchor,
                null, null, null, "title", "text", new string('a', 64)),
            "text", 1, ["keyword"]);
        var benchmarkCase = new RetrievalBenchmarkCase(
            caseId, "exact", "query", "en", "all_versions", null,
            ["eu-eurlex:known"], "Exact-first is an anchor ranking metric.",
            "engineer-reviewed", Collection: collection, Split: "holdout",
            RelevantAnchors:
            [
                new("eu-eurlex:known", "art_a", 3),
            ]);
        var caseSet = new RetrievalBenchmarkCaseSet([benchmarkCase], digest);
        var baseline = RetrievalBenchmarkEvaluation.Evaluate("keyword-holdout", [benchmarkCase],
            _ => new SearchExecution("keyword", [Hit("art_a"), Hit("art_b")], [], null), null);

        var control = RetrievalBenchmarkControls.ShuffledTop10(caseSet, baseline);

        Assert.Equal(1, control.Result.EligibleDenominator);
        Assert.Equal("detected", control.Result.Outcome);
        Assert.Equal(1, MetricValue(baseline.Metrics.ExactFirstAccuracy));
        Assert.Equal(0, MetricValue(control.Evaluation.Metrics.ExactFirstAccuracy));
        Assert.True(control.Result.NonRankingIdentical);
        Assert.True(control.Result.UnrelatedDenominatorsAndGatesIdentical);
    }

    [Fact]
    public void Qrels_shuffle_v2_deranges_duplicate_sets_without_dropping_cases()
    {
        RetrievalHit Hit(string work, string anchor) => new(Doc("eu-eurlex", work),
            new ProvisionRow($"rid-{work}-{anchor}", 0, anchor, $"p-{work}-{anchor}",
                "article", anchor, null, null, null, "title", "text", new string('a', 64)),
            "text", 1, ["keyword"]);
        RetrievalBenchmarkCase Case(string id, string work, string anchor) => new(
            id, "conceptual", id, "en", "all_versions", null,
            [$"eu-eurlex:{work}"], "Reviewed provision qrel.", "engineer-reviewed",
            Collection: "eu-eurlex", Split: "holdout",
            RelevantAnchors: [new($"eu-eurlex:{work}", anchor, 3)]);
        var cases = new[]
        {
            Case("qrels-a1", "a", "art_a"),
            Case("qrels-a2", "a", "art_a"),
            Case("qrels-b", "b", "art_b"),
        };
        var caseSet = new RetrievalBenchmarkCaseSet(cases, new string('b', 64));
        var baseline = RetrievalBenchmarkEvaluation.Evaluate("keyword-holdout", cases,
            c => c.Id == "qrels-b"
                ? new SearchExecution("keyword", [Hit("b", "art_b")], [], null)
                : new SearchExecution("keyword", [Hit("a", "art_a")], [], null), null);

        var control = RetrievalBenchmarkControls.QrelsShuffle(caseSet, baseline);

        Assert.Equal("qrels-shuffle/2", control.Result.Schema);
        Assert.Equal("detected", control.Result.Outcome);
        Assert.Equal(3, control.Result.EligibleDenominator);
        Assert.Equal(0, control.Result.OwnQrelSetRetainedCount);
        Assert.Equal(0, MetricValue(control.Result.AnchorNdcgAt10));
        Assert.True(MetricValue(control.Result.AnchorNdcgAt10) < 0.15);
        Assert.NotEmpty(control.Result.FailedGateNames);
        Assert.True(control.Result.UnrelatedDenominatorsAndGatesIdentical);
        Assert.Equal(3, control.Evaluation.CaseResults.Count);
    }

    [Fact]
    public void Qrels_shuffle_v2_may_fail_exact_first_as_an_anchor_ranking_gate()
    {
        RetrievalHit Hit(string work, string anchor) => new(Doc("eu-eurlex", work),
            new ProvisionRow($"rid-{work}-{anchor}", 0, anchor, $"p-{work}-{anchor}",
                "article", anchor, null, null, null, "title", "text", new string('a', 64)),
            "text", 1, ["keyword"]);
        RetrievalBenchmarkCase Case(string id, string work, string anchor) => new(
            id, "exact", id, "en", "all_versions", null,
            [$"eu-eurlex:{work}"], "Reviewed exact provision qrel.", "engineer-reviewed",
            Collection: "eu-eurlex", Split: "holdout",
            RelevantAnchors: [new($"eu-eurlex:{work}", anchor, 3)]);
        var cases = new[]
        {
            Case("qrels-exact-a", "a", "art_a"),
            Case("qrels-exact-b", "b", "art_b"),
        };
        var caseSet = new RetrievalBenchmarkCaseSet(cases, new string('e', 64));
        var baseline = RetrievalBenchmarkEvaluation.Evaluate("keyword-holdout", cases,
            benchmarkCase => benchmarkCase.Id.EndsWith("-a", StringComparison.Ordinal)
                ? new SearchExecution("keyword", [Hit("a", "art_a")], [], null)
                : new SearchExecution("keyword", [Hit("b", "art_b")], [], null), null);

        var control = RetrievalBenchmarkControls.QrelsShuffle(caseSet, baseline);

        Assert.Equal("detected", control.Result.Outcome);
        Assert.Equal(2, control.Result.EligibleDenominator);
        Assert.Equal(1, MetricValue(baseline.Metrics.ExactFirstAccuracy));
        Assert.Equal(0, MetricValue(control.Evaluation.Metrics.ExactFirstAccuracy));
        Assert.True(control.Result.NonRankingIdentical);
        Assert.True(control.Result.UnrelatedDenominatorsAndGatesIdentical);
    }

    [Fact]
    public void Qrels_shuffle_v2_blocks_when_a_collection_has_only_one_distinct_qrel_set()
    {
        var cases = Enumerable.Range(0, 3).Select(index => new RetrievalBenchmarkCase(
            $"same-{index}", "conceptual", $"query-{index}", "en", "all_versions", null,
            ["eu-eurlex:known"], "Every case intentionally shares one anchor set.",
            "engineer-reviewed", Collection: "eu-eurlex", Split: "holdout",
            RelevantAnchors: [new("eu-eurlex:known", "art_1", 3)])).ToArray();
        var hit = new RetrievalHit(Doc("eu-eurlex", "known"),
            new ProvisionRow("rid", 0, "art_1", "p", "article", "1", null, null, null,
                "title", "text", new string('a', 64)), "text", 1, ["keyword"]);
        var caseSet = new RetrievalBenchmarkCaseSet(cases, new string('d', 64));
        var baseline = RetrievalBenchmarkEvaluation.Evaluate("keyword-holdout", cases,
            _ => new SearchExecution("keyword", [hit], [], null), null);

        var control = RetrievalBenchmarkControls.QrelsShuffle(caseSet, baseline);

        Assert.Equal("insufficient_denominator", control.Result.Outcome);
        Assert.Equal(0, control.Result.EligibleDenominator);
        Assert.Equal(0, control.Result.OwnQrelSetRetainedCount);
        Assert.Equal("insufficient_denominator", control.Result.AnchorNdcgAt10.Status);
        Assert.Equal(cases.Length, control.Evaluation.CaseResults.Count);
    }

    [Fact]
    public void Strata_block_below_eight_and_label_supported_small_invariants_only()
    {
        RetrievalBenchmarkCase Case(int index) => new(
            $"exact-{index}", "exact", $"query-{index}", "en", "all_versions", null,
            [$"eu-eurlex:work-{index}"], "Reviewed exact provision.", "engineer-reviewed",
            Collection: "eu-eurlex", Split: "holdout",
            RelevantAnchors: [new($"eu-eurlex:work-{index}", "art_1", 3)]);
        RetrievalHit Hit(int index) => new(Doc("eu-eurlex", $"work-{index}"),
            new ProvisionRow($"rid-{index}", 0, "art_1", $"p-{index}", "article", "1",
                null, null, null, "title", "text", new string('a', 64)),
            "text", 1, ["keyword"]);

        var sevenCases = Enumerable.Range(0, 7).Select(Case).ToArray();
        var seven = RetrievalBenchmarkEvaluation.Evaluate("hybrid-holdout", sevenCases,
            c => new SearchExecution("hybrid", [Hit(int.Parse(c.Id[6..]))], [], null), null);
        var sevenStrata = RetrievalBenchmarkStrata.Build("eu-eurlex", seven);
        var sevenExact = Assert.Single(sevenStrata,
            row => row.Category == "exact" && row.Metric == "anchor_exact_first_accuracy");

        Assert.Equal("blocking", sevenExact.Disposition);
        Assert.Equal(8, sevenExact.InvariantFloor);
        Assert.Equal(20, sevenExact.StatisticalFloor);
        Assert.Equal(7, sevenExact.Observation.Denominator);
        Assert.Equal("insufficient_denominator", sevenExact.SupportStatus);
        Assert.False(sevenExact.GatePassed);

        var eightCases = Enumerable.Range(0, 8).Select(Case).ToArray();
        var eight = RetrievalBenchmarkEvaluation.Evaluate("hybrid-holdout", eightCases,
            c => new SearchExecution("hybrid", [Hit(int.Parse(c.Id[6..]))], [], null), null);
        var eightStrata = RetrievalBenchmarkStrata.Build("eu-eurlex", eight);
        var eightExact = Assert.Single(eightStrata,
            row => row.Category == "exact" && row.Metric == "anchor_exact_first_accuracy");
        var ranking = Assert.Single(eightStrata,
            row => row.Category == "exact" && row.Metric == "anchor_ndcg_at10");
        var legacy = Assert.Single(eightStrata,
            row => row.Category == "exact" && row.Metric == "legacy_work_ndcg_at10");

        Assert.Equal("invariant_only_n8", eightExact.SupportStatus);
        Assert.True(eightExact.GatePassed);
        Assert.Equal("reported", ranking.Disposition);
        Assert.Null(ranking.GatePassed);
        Assert.Equal("reported", legacy.Disposition);
        Assert.Null(legacy.GatePassed);
    }

    [Fact]
    public void Every_blocking_metric_must_be_present_before_a_report_is_structurally_valid()
    {
        var report = Report();
        var blocking = report.HoldoutStrata!
            .Where(row => row.Disposition == "blocking").ToArray();

        Assert.Equal(5, blocking.Length);
        Assert.All(blocking, omitted =>
        {
            var withoutOne = report with
            {
                HoldoutStrata = report.HoldoutStrata!
                    .Where(row => !ReferenceEquals(row, omitted)).ToArray(),
            };
            Assert.False(RetrievalBenchmarkGate.IsStructurallyValid(withoutOne, "eu-eurlex"));
        });
        Assert.False(RetrievalBenchmarkGate.IsStructurallyValid(report with
        {
            HoldoutStrata = report.HoldoutStrata!
                .Where(row => row.Disposition == "reported").ToArray(),
        }, "eu-eurlex"));
    }

    [Fact]
    public void Signed_strata_are_an_exact_projection_of_the_bound_holdout_cases()
    {
        const string collection = "lu-legilux";
        var cases = Cases().Where(item => item.Collection == collection).ToArray();
        var strata = StrataForCases(cases, collection);
        var report = Report() with
        {
            HoldoutSampleCount = cases.Count(item => item.Split == "holdout"),
            HoldoutStrata = strata,
        };

        Assert.True(RetrievalBenchmarkGate.StrataMatchCases(report, cases, collection));
        for (var index = 0; index < strata.Count; index++)
        {
            var missing = report with
            {
                HoldoutStrata = strata.Where((_, position) => position != index).ToArray(),
            };
            Assert.False(RetrievalBenchmarkGate.StrataMatchCases(missing, cases, collection));
        }
        Assert.False(RetrievalBenchmarkGate.StrataMatchCases(report with
        {
            HoldoutStrata = strata.Concat([
                new RetrievalBenchmarkStratum("anchor_mrr", collection, "invented", "holdout",
                    "reported", 8, 20, "insufficient_denominator",
                    RetrievalMetricObservation.Insufficient(), null),
            ]).ToArray(),
        }, cases, collection));
        Assert.False(RetrievalBenchmarkGate.StrataMatchCases(report with
        {
            HoldoutStrata = strata.Select((row, index) => index == 0
                ? row with
                {
                    Observation = row.Observation.Denominator == 0
                        ? RetrievalMetricObservation.Measured(1, 1)
                        : RetrievalMetricObservation.Measured(
                            MetricValue(row.Observation), row.Observation.Denominator + 1),
                }
                : row).ToArray(),
        }, cases, collection));
    }

    [Fact]
    public void Public_benchmark_evidence_requires_the_exact_case_strata_projection()
    {
        const string collection = "eu-eurlex";
        var cases = Cases().Where(item => item.Collection == collection).ToArray();
        var report = ReportForCases(cases, collection);

        Assert.True(RetrievalBenchmarkGate.IsStructurallyValid(report, collection));
        Assert.True(Lex.Web.ExplainerEndpoints.BenchmarkRankingIsPublishable(
            report, cases, collection));
        Assert.False(Lex.Web.ExplainerEndpoints.BenchmarkRankingIsPublishable(
            report with
            {
                ShuffledTop10Control = report.ShuffledTop10Control! with
                    { Outcome = "escaped" },
            }, cases, collection));

        var drifted = report with
        {
            HoldoutStrata = report.HoldoutStrata!.Select((row, index) => index == 0
                ? row with { Category = "invented" }
                : row).ToArray(),
        };
        Assert.True(RetrievalBenchmarkGate.IsStructurallyValid(drifted, collection));
        Assert.False(RetrievalBenchmarkGate.StrataMatchCases(drifted, cases, collection));
        Assert.False(Lex.Web.ExplainerEndpoints.BenchmarkRankingIsPublishable(
            drifted, cases, collection));
    }

    [Fact]
    public void Comparison_ndcg_scores_every_relevant_work_not_only_the_first()
    {
        RetrievalHit Hit(string work) => new(Doc("eu-eurlex", work),
            new ProvisionRow("rid-" + work, 0, "art_1", "p-" + work, "article", "1",
                null, null, null, "title", "text", new string('a', 64)),
            "text", 1, ["keyword"]);
        var benchmarkCase = new RetrievalBenchmarkCase(
            "comparison-001", "comparison", "compare a and b", "en", "all_versions", null,
            ["eu-eurlex:a", "eu-eurlex:b"], "Both works are relevant.",
            "engineer-reviewed", Collection: "eu-eurlex", Split: "holdout");

        var metrics = RetrievalBenchmarkRunner.Evaluate("comparison", [benchmarkCase],
            _ => new SearchExecution("keyword", [Hit("noise"), Hit("noise"), Hit("a"), Hit("b")], [],
                new SearchQueryPlan("compare a and b", "", ["a", "b"], null, null, true)), null);

        Assert.Equal(1, MetricValue(metrics.WorkRecallAt10));
        Assert.InRange(MetricValue(metrics.WorkNdcgAt10), 0.69, 0.70);
        Assert.Equal(0.5, MetricValue(metrics.WorkMrr));
        Assert.False(metrics.Mrr.HasMeasuredValue);

        var incomplete = RetrievalBenchmarkRunner.Evaluate("comparison", [benchmarkCase],
            _ => new SearchExecution("keyword", [Hit("a")], [],
                new SearchQueryPlan("compare a and b", "", ["a", "b"], null, null, true)), null);
        Assert.Equal(0.5, MetricValue(incomplete.WorkRecallAt10));
        Assert.InRange(MetricValue(incomplete.WorkNdcgAt10), 0.61, 0.62);
    }

    [Fact]
    public void Frozen_pre_tuning_baseline_binds_the_exact_case_artifact_and_splits()
    {
        var root = RepoRoot();
        var casePath = Path.Combine(root, "evals", "retrieval-cases.json");
        var baselinePath = Path.Combine(root, "evals", "retrieval-baseline-v2.json");
        var baseline = JsonNode.Parse(File.ReadAllText(baselinePath))!.AsObject();
        var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(casePath)));
        var caseSet = RetrievalBenchmarkCatalog.LoadSet(casePath);
        var cases = caseSet.Cases;
        var typedBaseline = RetrievalBenchmarkCatalog.LoadBaseline(baselinePath);

        Assert.Equal(digest, baseline["cases_sha256"]!.GetValue<string>());
        Assert.Equal(cases.Count, baseline["sample_count"]!.GetValue<int>());
        Assert.Equal("pending_signed_production_artifacts",
            baseline["measurement_status"]!.GetValue<string>());
        Assert.Empty(RetrievalBenchmarkRunner.BaselineFailures(caseSet, typedBaseline));
        Assert.Contains("digest", Assert.Single(RetrievalBenchmarkRunner.BaselineFailures(
            caseSet with { Sha256 = new string('0', 64) }, typedBaseline)));
        foreach (var collection in cases.GroupBy(item => item.Collection))
        {
            Assert.Equal(collection.Count(item => item.Split == "tuning"),
                baseline["collections"]![collection.Key]!["tuning"]!.GetValue<int>());
            Assert.Equal(collection.Count(item => item.Split == "holdout"),
                baseline["collections"]![collection.Key]!["holdout"]!.GetValue<int>());
        }
    }

    [Fact]
    public void Malformed_gate_inputs_fail_closed_during_catalog_load()
    {
        static JsonObject Valid() => new()
        {
            ["id"] = "case-1", ["category"] = "exact", ["query"] = "identifier",
            ["language"] = "en", ["time_scope"] = "all_versions", ["as_of"] = null,
            ["relevant_works"] = new JsonArray("collection:work"),
            ["explanation"] = "reviewed identity lookup", ["review_status"] = "engineer-reviewed",
            ["collection"] = "collection", ["split"] = "holdout",
            ["expected_resolution"] = "resolved", ["expect_no_hits"] = false,
        };
        var malformed = new List<JsonObject>();
        var blankId = Valid(); blankId["id"] = " "; malformed.Add(blankId);
        var category = Valid(); category["category"] = "invented"; malformed.Add(category);
        var language = Valid(); language["language"] = "de"; malformed.Add(language);
        var time = Valid(); time["time_scope"] = "as_of"; time["as_of"] = "not-a-date"; malformed.Add(time);
        var missingWorks = Valid(); missingWorks["relevant_works"] = null; malformed.Add(missingWorks);
        var contradiction = Valid(); contradiction["expect_no_hits"] = true; malformed.Add(contradiction);
        var negative = Valid(); negative["category"] = "negative";
        negative["relevant_works"] = new JsonArray(); malformed.Add(negative);

        foreach (var item in malformed)
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                new JsonArray(item).ToJsonString()));
            Assert.Throws<InvalidDataException>(() => RetrievalBenchmarkCatalog.LoadSet(stream));
        }
    }

    [Fact]
    public void Normalized_queries_cannot_cross_the_tuning_holdout_boundary()
    {
        JsonObject Case(string id, string split, string query) => new()
        {
            ["id"] = id, ["category"] = "conceptual", ["query"] = query,
            ["language"] = "en", ["time_scope"] = "all_versions", ["as_of"] = null,
            ["relevant_works"] = new JsonArray("collection:work"),
            ["explanation"] = "same retrieval judgment", ["review_status"] = "engineer-reviewed",
            ["collection"] = "collection", ["split"] = split, ["expect_no_hits"] = false,
        };
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            new JsonArray(Case("one", "tuning", "Data-access duties"),
                Case("two", "holdout", "data access duties")).ToJsonString()));

        Assert.Throws<InvalidDataException>(() => RetrievalBenchmarkCatalog.LoadSet(stream));
    }

    [Fact]
    public void Release_identity_and_cross_collection_resources_fail_closed()
    {
        var eu = Report();
        var lu = Report() with { ManifestId = "other-publisher-manifest" };

        Assert.True(RetrievalBenchmarkGate.HasReleaseIdentity(eu));
        Assert.False(RetrievalBenchmarkGate.HasReleaseIdentity(eu with { CodeCommit = "uncommitted" }));
        Assert.False(RetrievalBenchmarkGate.HasReleaseIdentity(eu with { ManifestId = "unverified" }));
        Assert.False(RetrievalBenchmarkGate.HasReleaseIdentity(
            eu with { ResourceConfiguration = "not supplied" }));
        Assert.True(RetrievalBenchmarkGate.ReportsAreCompatible([eu, lu], 2));
        Assert.False(RetrievalBenchmarkGate.ReportsAreCompatible(
            [eu, lu with { ResourceConfiguration = "different" }], 2));
        Assert.False(RetrievalBenchmarkGate.ReportsAreCompatible(
            [eu, lu with { MemoryLimitBytes = eu.MemoryLimitBytes * 2 }], 2));
        Assert.False(RetrievalBenchmarkGate.ReportsAreCompatible([eu], 2));
    }

    [Fact]
    public void Verified_benchmark_claims_must_match_its_index_and_benchmark_manifests()
    {
        var report = Report() with
        {
            ManifestId = new string('1', 64),
            CodeCommit = new string('b', 40),
        };
        var manifests = new[]
        {
            new Lex.Web.VerifiedArtifactManifest("index.manifest.json", new string('1', 64),
                "key", report.CodeCommit, report.Timestamp, ["index-eu-eurlex.db"],
                new Dictionary<string, string>
                {
                    ["collection"] = "eu-eurlex",
                    ["corpus_commit"] = report.CorpusCommit,
                }),
            new Lex.Web.VerifiedArtifactManifest("benchmark.manifest.json", new string('2', 64),
                "key", report.CodeCommit, report.Timestamp,
                ["retrieval-benchmark-eu-eurlex.json", report.CaseResultsFile!],
                new Dictionary<string, string>
                {
                    ["collection"] = "eu-eurlex",
                    ["corpus_commit"] = report.CorpusCommit,
                    ["index_manifest_sha256"] = new string('1', 64),
                }),
        };

        Assert.True(Lex.Web.ExplainerEndpoints.BenchmarkClaimsMatchVerifiedManifests(
            report, "eu-eurlex", manifests));
        Assert.False(Lex.Web.ExplainerEndpoints.BenchmarkClaimsMatchVerifiedManifests(
            report with { ManifestId = new string('3', 64) }, "eu-eurlex", manifests));
        Assert.False(Lex.Web.ExplainerEndpoints.BenchmarkClaimsMatchVerifiedManifests(
            report with { CodeCommit = new string('c', 40) }, "eu-eurlex", manifests));
        Assert.False(Lex.Web.ExplainerEndpoints.BenchmarkClaimsMatchVerifiedManifests(
            report, "eu-eurlex",
            [manifests[0] with { CodeCommit = new string('c', 40) }, manifests[1]]));
        Assert.False(Lex.Web.ExplainerEndpoints.BenchmarkClaimsMatchVerifiedManifests(
            report, "eu-eurlex",
            [manifests[0], manifests[1] with
            {
                Sources = new Dictionary<string, string>
                {
                    ["collection"] = "eu-eurlex",
                    ["corpus_commit"] = report.CorpusCommit,
                    ["index_manifest_sha256"] = new string('4', 64),
                },
            }]));
    }

    [Fact]
    public void Hybrid_activation_requires_a_passing_report_bound_to_the_exact_runtime()
    {
        const string collection = "lu-legilux";
        var cases = Cases().Where(item => item.Collection == collection).ToArray();
        var baseline = RetrievalBenchmarkCatalog.LoadBaseline(
            Path.Combine(RepoRoot(), "evals", "retrieval-baseline-v2.json"));
        var report = Report() with
        {
            SampleCount = cases.Length,
            TuningSampleCount = cases.Count(item => item.Split == "tuning"),
            HoldoutSampleCount = cases.Count(item => item.Split == "holdout"),
            BaselineSchema = baseline.Schema,
            ExpectedCasesSha256 = baseline.CasesSha256,
            ActualCasesSha256 = baseline.CasesSha256,
            ReviewStatus = "reviewed",
            ReviewAttestation = $"{baseline.ReviewedBy}@{baseline.ReviewedAt}",
            CorpusCommit = new string('c', 40),
            ManifestId = new string('1', 64),
            ModelId = "test/e5",
            ModelRevision = "test-revision",
            KeywordTuning = Metrics(cases.Count(item => item.Split == "tuning")),
            HybridTuning = Metrics(cases.Count(item => item.Split == "tuning")),
            KeywordHoldout = Metrics(cases.Count(item => item.Split == "holdout")),
            HybridHoldout = Metrics(cases.Count(item => item.Split == "holdout")),
            HoldoutStrata = ValidStrata(collection),
            CaseResultsCount = 2 * cases.Count(item => item.Split == "tuning")
                               + 4 * cases.Count(item => item.Split == "holdout"),
        };
        var indexManifest = new Lex.Web.VerifiedArtifactManifest(
            "index-lu-legilux.manifest.json", report.ManifestId, "key", report.CodeCommit,
            report.Timestamp,
            ["index-lu-legilux.db", "index-lu-legilux.vectors", "model-manifest.json",
                "model.onnx", "sentencepiece.bpe.model"],
            new Dictionary<string, string>
            {
                ["collection"] = collection,
                ["corpus_commit"] = report.CorpusCommit,
            });
        var benchmarkManifest = new Lex.Web.VerifiedArtifactManifest(
            "retrieval-benchmark-lu-legilux.manifest.json", new string('2', 64), "key",
            report.CodeCommit, report.Timestamp,
            ["retrieval-benchmark-lu-legilux.json", report.CaseResultsFile!],
            new Dictionary<string, string>
            {
                ["collection"] = collection,
                ["corpus_commit"] = report.CorpusCommit,
                ["index_manifest_sha256"] = indexManifest.Sha256,
            });
        var stamp = new Dictionary<string, string>
        {
            ["collection"] = collection,
            ["code_commit"] = report.CodeCommit,
            ["corpus_commit"] = report.CorpusCommit,
            ["embedding_model"] = report.ModelId,
            ["embedding_revision"] = report.ModelRevision,
        };

        var accepted = Lex.Web.HybridActivationGate.Evaluate(
            report, collection, report.CodeCommit, stamp, report.ModelId,
            report.ModelRevision, indexManifest, benchmarkManifest);
        var failedGate = Lex.Web.HybridActivationGate.Evaluate(
            report with
            {
                ActivationGatePassed = false,
                GateFailures = ["holdout warm p95 exceeds 250 ms"],
            }, collection, report.CodeCommit, stamp, report.ModelId,
            report.ModelRevision, indexManifest, benchmarkManifest);
        var wrongRuntime = Lex.Web.HybridActivationGate.Evaluate(
            report, collection, new string('f', 40), stamp, report.ModelId,
            report.ModelRevision, indexManifest, benchmarkManifest);
        var malformedTuple = Lex.Web.HybridActivationGate.Evaluate(
            report with
            {
                HybridHoldout = report.HybridHoldout with
                {
                    NdcgAt10 = RetrievalMetricObservation.FromSerialized(1, 0, "measured"),
                },
            }, collection, report.CodeCommit, stamp, report.ModelId,
            report.ModelRevision, indexManifest, benchmarkManifest);
        var oldScalarSchema = Lex.Web.HybridActivationGate.Evaluate(
            report with { Schema = "lex-retrieval-benchmark/3" },
            collection, report.CodeCommit, stamp, report.ModelId,
            report.ModelRevision, indexManifest, benchmarkManifest);
        var missingControl = Lex.Web.HybridActivationGate.Evaluate(
            report with { ShuffledTop10Control = null },
            collection, report.CodeCommit, stamp, report.ModelId,
            report.ModelRevision, indexManifest, benchmarkManifest);
        var escapedControl = Lex.Web.HybridActivationGate.Evaluate(
            report with
            {
                ShuffledTop10Control = report.ShuffledTop10Control! with { Outcome = "escaped" },
            }, collection, report.CodeCommit, stamp, report.ModelId,
            report.ModelRevision, indexManifest, benchmarkManifest);
        var oneCaseMetrics = report.HybridHoldout with
        {
            ExactFirstAccuracy = RetrievalMetricObservation.Measured(1, 1),
            TemporalLeakageFailures = RetrievalMetricObservation.Measured(0, 1),
            NoHitAccuracy = RetrievalMetricObservation.Measured(1, 1),
            ResolutionAccuracy = RetrievalMetricObservation.Measured(1, 1),
            RoleIntentAccuracy = RetrievalMetricObservation.Measured(1, 1),
        };
        var omittedBlockingEvidence = report with
        {
            HybridHoldout = oneCaseMetrics,
            HoldoutStrata = report.HoldoutStrata!
                .Where(row => row.Disposition == "reported").ToArray(),
        };
        var omittedBlockingActivation = Lex.Web.HybridActivationGate.Evaluate(
            omittedBlockingEvidence, collection, report.CodeCommit, stamp, report.ModelId,
            report.ModelRevision, indexManifest, benchmarkManifest);
        var oversizedHoldout = 10_001 - report.TuningSampleCount;
        var oversizedReport = report with
        {
            SampleCount = 10_001,
            HoldoutSampleCount = oversizedHoldout,
            KeywordHoldout = Metrics(oversizedHoldout),
            HybridHoldout = Metrics(oversizedHoldout),
            CaseResultsCount = 2 * report.TuningSampleCount + 4 * oversizedHoldout,
        };
        var unsupportedGateMetrics = new[]
        {
            report.HybridHoldout with
                { ExactFirstAccuracy = RetrievalMetricObservation.Insufficient() },
            report.HybridHoldout with
                { TemporalLeakageFailures = RetrievalMetricObservation.Insufficient() },
            report.HybridHoldout with
                { NoHitAccuracy = RetrievalMetricObservation.Insufficient() },
            report.HybridHoldout with
                { ResolutionAccuracy = RetrievalMetricObservation.Insufficient() },
            report.HybridHoldout with
                { RoleIntentAccuracy = RetrievalMetricObservation.Insufficient() },
            report.HybridHoldout with
                { NdcgAt10 = RetrievalMetricObservation.Insufficient() },
            report.HybridHoldout with
                { P95Ms = RetrievalMetricObservation.Insufficient() },
        };

        Assert.True(RetrievalBenchmarkGate.IsStructurallyValid(report, collection));
        Assert.False(accepted.Activated);
        Assert.Equal("benchmark_invalid", accepted.Reason);
        Assert.False(failedGate.Activated);
        Assert.Equal("benchmark_invalid", failedGate.Reason);
        Assert.False(wrongRuntime.Activated);
        Assert.Equal("benchmark_identity_mismatch", wrongRuntime.Reason);
        Assert.False(malformedTuple.Activated);
        Assert.Equal("benchmark_invalid", malformedTuple.Reason);
        Assert.False(oldScalarSchema.Activated);
        Assert.Equal("benchmark_invalid", oldScalarSchema.Reason);
        Assert.False(missingControl.Activated);
        Assert.Equal("benchmark_invalid", missingControl.Reason);
        Assert.False(escapedControl.Activated);
        Assert.Equal("benchmark_invalid", escapedControl.Reason);
        Assert.False(RetrievalBenchmarkGate.IsStructurallyValid(
            omittedBlockingEvidence, collection));
        Assert.False(omittedBlockingActivation.Activated);
        Assert.Equal("benchmark_invalid", omittedBlockingActivation.Reason);
        Assert.False(RetrievalBenchmarkGate.IsStructurallyValid(oversizedReport, collection));
        Assert.False(RetrievalBenchmarkGate.IsStructurallyValid(report with
        {
            CaseResultsFile = "cases\ninjected.jsonl",
        }, collection));
        Assert.All(unsupportedGateMetrics, metrics =>
        {
            var result = Lex.Web.HybridActivationGate.Evaluate(
                report with { HybridHoldout = metrics }, collection, report.CodeCommit,
                stamp, report.ModelId, report.ModelRevision, indexManifest, benchmarkManifest);
            Assert.False(result.Activated);
            Assert.Equal("benchmark_invalid", result.Reason);
        });
    }

    [Fact]
    public void Index_registry_rejects_a_signed_v4_report_with_a_mixed_metric_tuple()
    {
        const string collection = "eu-eurlex";
        const string codeCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string corpusCommit = "corpus-commit";
        var directory = Path.Combine(Path.GetTempPath(), $"lex-benchmark-registry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var privateKey = StampSigner.CreateKeyPem();
            var root = ArtifactManifests.TrustRoot("benchmark-test", privateKey);
            var database = Path.Combine(directory, $"index-{collection}.db");
            var vectors = Path.ChangeExtension(database, ".vectors");
            var document = Doc(collection, "known");
            var provision = new ProvisionRow(
                $"{document.Key}|en|2024-01-01", 0, "art_1", $"{document.Key}#art_1",
                "article", "1", null, null, null, document.Title, "known legal text",
                Convert.ToHexStringLower(SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes("known legal text"))));
            using (var encoder = new BenchmarkFakeEncoder())
                IndexBuilder.Build(database, new Dictionary<string, string>
                {
                    ["collection"] = collection,
                    ["code_commit"] = codeCommit,
                    ["corpus_commit"] = corpusCommit,
                    ["scope_expected_works"] = "1",
                    ["build_issues_json"] = "[]",
                    ["build_issues_digest"] = Convert.ToHexStringLower(SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes("[]"))),
                }, [document], [provision], [], [], privateKey,
                    semantic: new SemanticBuildOptions(
                        encoder, vectors, "model-sha", "tokenizer-sha"));
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.WriteAllText(Path.Combine(directory, "model-manifest.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "model.onnx"), "model");
            File.WriteAllText(Path.Combine(directory, "sentencepiece.bpe.model"), "tokenizer");

            var indexManifest = ArtifactManifests.Create(directory,
                [$"index-{collection}.db", $"index-{collection}.vectors", "model-manifest.json",
                    "model.onnx", "sentencepiece.bpe.model"],
                root.KeyId, "2026-08-29T00:00:00Z", codeCommit,
                new Dictionary<string, string>
                {
                    ["collection"] = collection,
                    ["corpus_commit"] = corpusCommit,
                });
            var indexManifestBytes = ArtifactManifests.Serialize(indexManifest);
            WriteManifest(directory, $"index-{collection}", indexManifestBytes, privateKey);

            var cases = Cases().Where(item => item.Collection == collection).ToArray();
            var tuning = cases.Count(item => item.Split == "tuning");
            var holdout = cases.Count(item => item.Split == "holdout");
            var rowCount = 2 * tuning + 4 * holdout;
            var caseBytes = System.Text.Encoding.UTF8.GetBytes(string.Concat(
                Enumerable.Repeat("{\"schema\":\"synthetic-case\"}\n", rowCount)));
            var caseFile = $"retrieval-benchmark-{collection}.cases.jsonl";
            File.WriteAllBytes(Path.Combine(directory, caseFile), caseBytes);
            var insufficient = RetrievalMetricObservation.Insufficient();
            RetrievalNegativeControlResult InsufficientControl(string schema) => new(
                schema, "insufficient_denominator", "product_gate", 0, [], true, true,
                true, 0, insufficient);
            var report = Report() with
            {
                SampleCount = cases.Length,
                TuningSampleCount = tuning,
                HoldoutSampleCount = holdout,
                CodeCommit = codeCommit,
                CorpusCommit = corpusCommit,
                ManifestId = Convert.ToHexStringLower(SHA256.HashData(indexManifestBytes)),
                ModelId = "test/e5",
                ModelRevision = "test-revision",
                IndexBytes = new FileInfo(database).Length,
                VectorBytes = new FileInfo(vectors).Length,
                KeywordTuning = Metrics(tuning),
                HybridTuning = Metrics(tuning),
                KeywordHoldout = Metrics(holdout),
                HybridHoldout = Metrics(holdout) with
                {
                    NdcgAt10 = RetrievalMetricObservation.FromSerialized(1, 0, "measured"),
                },
                ActivationGatePassed = false,
                GateFailures = ["malformed anchor metric"],
                HoldoutStrata = ValidStrata(collection),
                ShuffledTop10Control = InsufficientControl("shuffled-top10/2"),
                QrelsShuffleControl = InsufficientControl("qrels-shuffle/2"),
                CaseResultsFile = caseFile,
                CaseResultsCount = rowCount,
                CaseResultsSha256 = Convert.ToHexStringLower(SHA256.HashData(caseBytes)),
            };
            var reportFile = $"retrieval-benchmark-{collection}.json";
            File.WriteAllBytes(Path.Combine(directory, reportFile),
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(report,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                    }));
            var benchmarkManifest = ArtifactManifests.Create(directory, [reportFile, caseFile],
                root.KeyId, report.Timestamp, codeCommit, new Dictionary<string, string>
                {
                    ["collection"] = collection,
                    ["corpus_commit"] = corpusCommit,
                    ["index_manifest_sha256"] = report.ManifestId,
                });
            WriteManifest(directory, $"retrieval-benchmark-{collection}",
                ArtifactManifests.Serialize(benchmarkManifest), privateKey);

            using var registry = new Lex.Web.IndexRegistry(
                Microsoft.Extensions.Options.Options.Create(new Lex.Web.LexOptions
                {
                    IndexDir = directory,
                    EmbeddingModelDir = directory,
                    CodeCommit = codeCommit,
                    RequiredPublishers = collection,
                }), Microsoft.Extensions.Logging.Abstractions.NullLogger<Lex.Web.IndexRegistry>.Instance,
                [root], _ => new BenchmarkFakeEncoder());

            Assert.Equal(1, registry.Count);
            Assert.Equal("benchmark_invalid", registry.HybridActivations[collection].Reason);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Runner_reports_tuning_and_holdout_without_cross_split_aggregation()
    {
        var db = Path.Combine(Path.GetTempPath(), $"lex-benchmark-{Guid.NewGuid():N}.db");
        try
        {
            var doc = Doc("eu-eurlex", "known");
            var provision = new ProvisionRow($"{doc.Key}|en|2024-01-01", 0, "art_1",
                $"{doc.Key}#art_1", "article", "1", null, null, null, doc.Title,
                "knownterm", Convert.ToHexStringLower(
                    SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("knownterm"))));
            IndexBuilder.Build(db, new Dictionary<string, string>
            {
                ["collection"] = "eu-eurlex",
                ["built_at"] = "2026-08-09T00:00:00Z",
                ["corpus_commit"] = "corpus-commit",
            }, [doc], [provision], [], [], null);
            using var reader = LexIndexReader.Open(db);
            var cases = new[]
            {
                new RetrievalBenchmarkCase("tuning", "conceptual", "knownterm", "en",
                    "all_versions", null, ["eu-eurlex:known"], "known hit",
                    "engineer-reviewed", Collection: "eu-eurlex", Split: "tuning"),
                new RetrievalBenchmarkCase("holdout", "exact", "missingterm", "en",
                    "all_versions", null, ["eu-eurlex:known"], "known miss",
                    "engineer-reviewed", Collection: "eu-eurlex", Split: "holdout"),
            };
            var caseSet = new RetrievalBenchmarkCaseSet(cases, new string('d', 64));
            var baseline = new RetrievalBenchmarkBaseline("lex-retrieval-baseline/2",
                "evals/retrieval-cases.json", caseSet.Sha256, 2, "engineer-reviewed",
                "reviewer", "2026-08-09");

            var run = RetrievalBenchmarkRunner.RunWithCaseResults(
                reader, caseSet, baseline, db, null,
                new string('b', 40), new string('1', 64), "runner", "2 GiB", 2_147_483_648,
                1, 1, DateTimeOffset.Parse("2026-08-09T00:00:00Z"), "case-results.jsonl");
            var report = run.Report;

            Assert.Equal("lex-retrieval-benchmark/4", report.Schema);
            Assert.False(report.KeywordTuning.Mrr.HasMeasuredValue);
            Assert.False(report.HybridTuning.Mrr.HasMeasuredValue);
            Assert.Equal(1, MetricValue(report.KeywordTuning.WorkMrr));
            Assert.Equal(1, MetricValue(report.HybridTuning.WorkMrr));
            Assert.Equal(0, MetricValue(report.KeywordHoldout.WorkMrr));
            Assert.Equal(0, MetricValue(report.HybridHoldout.WorkMrr));
            Assert.False(report.ActivationGatePassed);
            Assert.Equal("lex-retrieval-case-results/1", report.CaseResultsSchema);
            Assert.Equal("case-results.jsonl", report.CaseResultsFile);
            Assert.Equal(report.CaseResultsCount,
                run.CaseResultsJsonl.Count(value => value == (byte)'\n'));
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(run.CaseResultsJsonl)),
                report.CaseResultsSha256);
            Assert.True(RetrievalBenchmarkGate.CaseResultsBytesMatch(
                report, run.CaseResultsJsonl));
            Assert.DoesNotContain("knownterm", System.Text.Encoding.UTF8.GetString(run.CaseResultsJsonl));
            Assert.Equal("insufficient_denominator", report.ShuffledTop10Control!.Outcome);
            Assert.Equal("insufficient_denominator", report.QrelsShuffleControl!.Outcome);
            Assert.Contains(report.GateFailures, failure =>
                failure.StartsWith("product gate: shuffled-top10/2", StringComparison.Ordinal));
            Assert.NotEmpty(report.HoldoutStrata!);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(db); } catch { }
        }
    }

    [Fact]
    public void Artifact_writer_binds_and_atomically_publishes_adjacent_case_results()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lex-benchmark-artifact-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var insufficient = RetrievalMetricObservation.Insufficient();
            RetrievalBenchmarkCaseResult Row(string stage, string id, string split) => new(
                "lex-retrieval-case-result/1", stage, id, "eu-eurlex", "conceptual", split,
                new string('a', 64), [], [], insufficient, insufficient, insufficient,
                insufficient, insufficient, insufficient, insufficient, insufficient,
                RetrievalMetricObservation.Measured(1, 1), insufficient, insufficient,
                insufficient);
            var resultRows = Enumerable.Range(0, 8)
                .SelectMany(index => new[]
                {
                    Row("keyword-tuning", $"tuning-{index}", "tuning"),
                    Row("hybrid-tuning", $"tuning-{index}", "tuning"),
                })
                .Concat(Enumerable.Range(0, 8).SelectMany(index => new[]
                {
                    Row("keyword-holdout", $"holdout-{index}", "holdout"),
                    Row("hybrid-holdout", $"holdout-{index}", "holdout"),
                    Row("shuffled-top10/2", $"holdout-{index}", "holdout"),
                    Row("qrels-shuffle/2", $"holdout-{index}", "holdout"),
                })).ToArray();
            var rowJson = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
            };
            var rows = System.Text.Encoding.UTF8.GetBytes(string.Concat(resultRows.Select(row =>
                System.Text.Json.JsonSerializer.Serialize(row, rowJson) + "\n")));
            var report = Report() with
            {
                CaseResultsCount = 48,
                CaseResultsSha256 = Convert.ToHexStringLower(SHA256.HashData(rows)),
            };
            var reportPath = Path.Combine(directory, "retrieval-benchmark-eu-eurlex.json");
            var casePath = Path.Combine(directory, report.CaseResultsFile!);

            RetrievalBenchmarkArtifactWriter.Write(
                reportPath, casePath, new RetrievalBenchmarkRun(report, rows));

            Assert.Equal(rows, File.ReadAllBytes(casePath));
            var written = System.Text.Json.JsonSerializer.Deserialize<RetrievalBenchmarkReport>(
                File.ReadAllBytes(reportPath), new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                });
            Assert.NotNull(written);
            Assert.True(RetrievalBenchmarkGate.IsStructurallyValid(written!));
            Assert.True(RetrievalBenchmarkGate.CaseResultsMatch(written!, casePath));

            var invalidPath = Path.Combine(directory, "invalid-report.json");
            Assert.Throws<InvalidDataException>(() => RetrievalBenchmarkArtifactWriter.Write(
                invalidPath, Path.Combine(directory, "invalid-cases.jsonl"),
                new RetrievalBenchmarkRun(report with
                {
                    CaseResultsFile = "invalid-cases.jsonl",
                    CaseResultsSha256 = new string('0', 64),
                }, rows)));
            Assert.False(File.Exists(invalidPath));

            var queryBytes = System.Text.Encoding.UTF8.GetBytes(
                System.Text.Encoding.UTF8.GetString(rows)
                    .Replace("{", "{\"query\":\"private canary\",", StringComparison.Ordinal));
            var queryReport = report with
            {
                CaseResultsSha256 = Convert.ToHexStringLower(SHA256.HashData(queryBytes)),
            };
            Assert.False(RetrievalBenchmarkGate.CaseResultsBytesMatch(queryReport, queryBytes));
            Assert.Throws<InvalidDataException>(() => RetrievalBenchmarkArtifactWriter.Write(
                Path.Combine(directory, "query-report.json"), casePath,
                new RetrievalBenchmarkRun(queryReport, queryBytes)));

            var nestedQueryBytes = System.Text.Encoding.UTF8.GetBytes(
                System.Text.Encoding.UTF8.GetString(rows).Replace(
                    "\"mrr\":{", "\"mrr\":{\"query\":\"private canary\",",
                    StringComparison.Ordinal));
            var nestedQueryReport = report with
            {
                CaseResultsSha256 = Convert.ToHexStringLower(SHA256.HashData(nestedQueryBytes)),
            };
            Assert.False(RetrievalBenchmarkGate.CaseResultsBytesMatch(
                nestedQueryReport, nestedQueryBytes));

            var unterminatedBytes = rows[..^1];
            var unterminatedReport = report with
            {
                CaseResultsSha256 = Convert.ToHexStringLower(SHA256.HashData(unterminatedBytes)),
            };
            Assert.False(RetrievalBenchmarkGate.CaseResultsBytesMatch(
                unterminatedReport, unterminatedBytes));

            var oversizedLineRows = resultRows.ToArray();
            var oversizedCaseId = new string('x', 70_000);
            oversizedLineRows[0] = oversizedLineRows[0] with { CaseId = oversizedCaseId };
            oversizedLineRows[1] = oversizedLineRows[1] with { CaseId = oversizedCaseId };
            var oversizedLineBytes = System.Text.Encoding.UTF8.GetBytes(string.Concat(
                oversizedLineRows.Select(row =>
                    System.Text.Json.JsonSerializer.Serialize(row, rowJson) + "\n")));
            var oversizedLineReport = report with
            {
                CaseResultsSha256 = Convert.ToHexStringLower(SHA256.HashData(oversizedLineBytes)),
            };
            Assert.True(oversizedLineBytes.Length < report.CaseResultsCount * 64 * 1024);
            Assert.False(RetrievalBenchmarkGate.CaseResultsBytesMatch(
                oversizedLineReport, oversizedLineBytes));

            var preflightPath = Path.Combine(directory, "oversized-before-read.jsonl");
            using (var oversized = new FileStream(preflightPath, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
                oversized.SetLength(8 * 1024 * 1024);
            var preflightReport = report with
            {
                CaseResultsCount = 1,
                CaseResultsSha256 = new string('0', 64),
            };
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            Assert.False(RetrievalBenchmarkGate.CaseResultsMatch(
                preflightReport, preflightPath));
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Assert.True(allocated < 1024 * 1024,
                $"oversized input allocated {allocated} bytes before rejection");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Case_artifact_total_and_physical_line_size_boundaries_are_closed()
    {
        var manyRows = Report() with { CaseResultsCount = 40_000 };
        Assert.True(RetrievalBenchmarkGate.CaseResultsSizeIsValid(
            manyRows, RetrievalBenchmarkGate.MaxCaseResultsBytes));
        Assert.False(RetrievalBenchmarkGate.CaseResultsSizeIsValid(
            manyRows, (long)RetrievalBenchmarkGate.MaxCaseResultsBytes + 1));

        var oneRow = Report() with { CaseResultsCount = 1 };
        Assert.True(RetrievalBenchmarkGate.CaseResultsSizeIsValid(
            oneRow, RetrievalBenchmarkGate.MaxCaseResultLineBytes));
        Assert.False(RetrievalBenchmarkGate.CaseResultsSizeIsValid(
            oneRow, (long)RetrievalBenchmarkGate.MaxCaseResultLineBytes + 1));
    }

    [Fact]
    public void Activation_latency_is_authorized_only_by_holdout_measurements()
    {
        var fastTuning = new RetrievalMetrics(1, 1, 1, 1, 0, 1, 10, 20, 1, 1, 1);
        var slowHoldout = fastTuning with
        {
            P95Ms = RetrievalMetricObservation.Measured(251, 1),
        };

        Assert.Empty(RetrievalBenchmarkRunner.HoldoutLatencyFailures(fastTuning));
        Assert.Equal("holdout warm p95 exceeds 250 ms",
            Assert.Single(RetrievalBenchmarkRunner.HoldoutLatencyFailures(slowHoldout)));
    }

    [Fact]
    public void Deployment_fetches_index_and_benchmark_evidence_from_one_immutable_release()
    {
        var root = RepoRoot();
        var fetch = File.ReadAllText(Path.Combine(root, "deploy", "fetch-indexes.sh"));
        var dockerfile = File.ReadAllText(Path.Combine(root, "Dockerfile"));
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "deploy.yml"));

        Assert.Contains("benchmark=\"retrieval-benchmark-$collection.json\"", fetch);
        Assert.DoesNotContain("if [ \"$repo\" = \"lex-corpus-eu-eurlex\" ]", fetch);
        Assert.Contains("LEX_RELEASE_TAG_LU_LEGILUX", fetch);
        Assert.Contains("LEX_RELEASE_TAG_EU_EURLEX", fetch);
        Assert.Contains("release_base=\"https://github.com/SFHAJJI/$repo/releases/download/$release_tag\"", fetch);
        Assert.DoesNotContain("releases/latest/download", fetch);
        Assert.Contains("has_vectors", fetch);
        Assert.Contains("vector release is missing signed retrieval benchmark evidence", fetch);
        Assert.Contains(".sources.queue_ticket_id", fetch);
        Assert.Contains(".sources.index_manifest_sha256", fetch);
        Assert.Contains("signed queue ticket does not match the exact release tag", fetch);
        Assert.Contains("ARG LEX_RELEASE_TAG_LU_LEGILUX", dockerfile);
        Assert.Contains("ARG LEX_RELEASE_TAG_EU_EURLEX", dockerfile);
        Assert.Contains("\"lex-corpus-lu-legilux:lu-legilux:$lu_release_tag\"", workflow);
        Assert.Contains("\"lex-corpus-eu-eurlex:eu-eurlex:$eu_release_tag\"", workflow);
        Assert.Contains("benchmark_manifest=\"retrieval-benchmark-$collection.manifest.json\"", workflow);
        Assert.Contains("release_base=\"https://github.com/SFHAJJI/$repo/releases/download/$release_tag\"", workflow);
        Assert.DoesNotContain("releases/latest/download", workflow);
    }

    private static DocRow Doc(string collection, string work) => new(
        $"{collection}:{work}:2024-01-01", collection, work, $"urn:{work}", "REG", "en",
        "2024-01-01", null, "publisher", "2026-08-09T00:00:00Z", false,
        true, true, "record", "body", "https://example.test", "title", "title",
        null, "2024-01-01", null);

    private static RetrievalBenchmarkReport Report()
    {
        var measured = RetrievalMetricObservation.Measured(1, 8);
        var metrics = Metrics(8);
        return new RetrievalBenchmarkReport(
            "lex-retrieval-benchmark/4", "2026-08-09T00:00:00Z", 16, "reviewed",
            "lex-retrieval-baseline/2", new string('a', 64), new string('a', 64),
            "reviewer@2026-08-09", new string('b', 40), "corpus-commit", "manifest-digest",
            "intfloat/multilingual-e5-small", "model-revision", "runner-1", "1 cpu, 2 GiB",
            1, 1, 100, 2L * 1024 * 1024 * 1024, 100, 100,
            metrics, metrics, metrics, metrics, 8, 8, true, [],
            ValidStrata("eu-eurlex"),
            DetectedControl("shuffled-top10/2", measured),
            DetectedControl("qrels-shuffle/2", RetrievalMetricObservation.Measured(0, 8)),
            "lex-retrieval-case-results/1", "case-results.jsonl", 48, new string('d', 64));
    }

    private static RetrievalMetrics Metrics(int denominator)
    {
        var measured = RetrievalMetricObservation.Measured(1, denominator);
        return new RetrievalMetrics(
            measured, measured, measured, measured, measured, measured,
            measured, RetrievalMetricObservation.Measured(0, denominator),
            measured, measured, measured, measured, measured, measured);
    }

    private static IReadOnlyList<RetrievalBenchmarkStratum> ValidStrata(string collection) =>
    [
        new("anchor_exact_first_accuracy", collection, "exact", "holdout", "blocking",
            8, 20, "invariant_only_n8", RetrievalMetricObservation.Measured(1, 8), true),
        new("temporal_leakage_failures", collection, "temporal", "holdout", "blocking",
            8, 20, "invariant_only_n8", RetrievalMetricObservation.Measured(0, 8), true),
        new("no_hit_accuracy", collection, "negative", "holdout", "blocking",
            8, 20, "invariant_only_n8", RetrievalMetricObservation.Measured(1, 8), true),
        new("resolution_accuracy", collection, "resolution", "holdout", "blocking",
            8, 20, "invariant_only_n8", RetrievalMetricObservation.Measured(1, 8), true),
        new("role_intent_accuracy", collection, "role", "holdout", "blocking",
            8, 20, "invariant_only_n8", RetrievalMetricObservation.Measured(1, 8), true),
        new("anchor_ndcg_at10", collection, "conceptual", "holdout", "reported",
            8, 20, "invariant_only_n8", RetrievalMetricObservation.Measured(1, 8), null),
    ];

    private static IReadOnlyList<RetrievalBenchmarkStratum> StrataForCases(
        IReadOnlyCollection<RetrievalBenchmarkCase> cases, string collection)
    {
        var strata = new List<RetrievalBenchmarkStratum>();
        foreach (var category in cases.Where(item => item.Split == "holdout")
                     .GroupBy(item => item.Category, StringComparer.Ordinal))
        {
            var rows = category.ToArray();
            var anchorCount = rows.Count(item => item.RelevantAnchors is { Count: > 0 });
            var workCount = rows.Count(item => item.RelevantWorks.Count > 0);

            void AddReported(string metric, int denominator, int statisticalFloor = 20)
            {
                var observation = denominator == 0
                    ? RetrievalMetricObservation.Insufficient()
                    : RetrievalMetricObservation.Measured(1, denominator);
                strata.Add(new(metric, collection, category.Key, "holdout", "reported", 8,
                    statisticalFloor, Support(denominator, statisticalFloor), observation, null));
            }
            void AddBlocking(string metric, int denominator, double expected)
            {
                var observation = denominator == 0
                    ? RetrievalMetricObservation.Insufficient()
                    : RetrievalMetricObservation.Measured(expected, denominator);
                strata.Add(new(metric, collection, category.Key, "holdout", "blocking", 8, 20,
                    Support(denominator, 20), observation, denominator >= 8));
            }

            AddReported("anchor_mrr", anchorCount);
            AddReported("anchor_recall_at10", anchorCount);
            AddReported("anchor_ndcg_at10", anchorCount);
            AddReported("legacy_work_mrr", workCount);
            AddReported("legacy_work_recall_at10", workCount);
            AddReported("legacy_work_ndcg_at10", workCount);
            AddReported("latency_p95_ms", rows.Length, 200);
            if (category.Key == "exact")
                AddBlocking("anchor_exact_first_accuracy", anchorCount, 1);
            if (rows.Any(item => item.AsOf is not null))
                AddBlocking("temporal_leakage_failures",
                    rows.Count(item => item.AsOf is not null), 0);
            if (rows.Any(item => item.ExpectNoHits))
                AddBlocking("no_hit_accuracy", rows.Count(item => item.ExpectNoHits), 1);
            if (rows.Any(item => item.ExpectedResolution is not null))
                AddBlocking("resolution_accuracy",
                    rows.Count(item => item.ExpectedResolution is not null), 1);
            if (rows.Any(item => item.ExpectedRole is not null))
                AddBlocking("role_intent_accuracy",
                    rows.Count(item => item.ExpectedRole is not null), 1);
        }
        return strata;

        static string Support(int denominator, int statisticalFloor) => denominator switch
        {
            < 8 => "insufficient_denominator",
            _ when denominator < statisticalFloor => "invariant_only_n8",
            _ => "statistically_supported",
        };
    }

    private static RetrievalBenchmarkReport ReportForCases(
        IReadOnlyCollection<RetrievalBenchmarkCase> cases, string collection)
    {
        var tuning = cases.Where(item => item.Split == "tuning").ToArray();
        var holdout = cases.Where(item => item.Split == "holdout").ToArray();
        return Report() with
        {
            SampleCount = cases.Count,
            TuningSampleCount = tuning.Length,
            HoldoutSampleCount = holdout.Length,
            KeywordTuning = MetricsForCases(tuning),
            HybridTuning = MetricsForCases(tuning),
            KeywordHoldout = MetricsForCases(holdout),
            HybridHoldout = MetricsForCases(holdout),
            ActivationGatePassed = false,
            GateFailures = ["blocking evidence is below its support floor"],
            HoldoutStrata = StrataForCases(cases, collection),
            CaseResultsCount = 2 * tuning.Length + 4 * holdout.Length,
        };
    }

    private static RetrievalMetrics MetricsForCases(
        IReadOnlyCollection<RetrievalBenchmarkCase> cases)
    {
        var anchorCount = cases.Count(item => item.RelevantAnchors is { Count: > 0 });
        var workCount = cases.Count(item => item.RelevantWorks.Count > 0);
        var exactCount = cases.Count(item => item.Category == "exact"
                                             && item.RelevantAnchors is { Count: > 0 });
        var temporalCount = cases.Count(item => item.AsOf is not null);
        var noHitCount = cases.Count(item => item.ExpectNoHits);
        var resolutionCount = cases.Count(item => item.ExpectedResolution is not null);
        var roleCount = cases.Count(item => item.ExpectedRole is not null);

        static RetrievalMetricObservation Observation(double value, int denominator) =>
            denominator == 0
                ? RetrievalMetricObservation.Insufficient()
                : RetrievalMetricObservation.Measured(value, denominator);

        return new RetrievalMetrics(
            Observation(1, anchorCount), Observation(1, anchorCount),
            Observation(1, anchorCount), Observation(1, workCount),
            Observation(1, workCount), Observation(1, workCount),
            Observation(1, exactCount), Observation(0, temporalCount),
            Observation(1, cases.Count), Observation(1, cases.Count),
            Observation(1, cases.Count), Observation(1, noHitCount),
            Observation(1, resolutionCount), Observation(1, roleCount));
    }

    private static RetrievalNegativeControlResult DetectedControl(
        string schema, RetrievalMetricObservation ndcg) => new(
        schema, "detected", "product_gate", 8,
        ["anchor_ndcg_at10_not_below_unshuffled"], true, true, true, 0, ndcg);

    private static double MetricValue(RetrievalMetricObservation observation)
    {
        Assert.True(observation.HasMeasuredValue);
        return observation.RequireMeasuredValue();
    }

    private static void WriteManifest(
        string directory, string stem, byte[] bytes, string privateKey)
    {
        File.WriteAllBytes(Path.Combine(directory, $"{stem}.manifest.json"), bytes);
        File.WriteAllText(Path.Combine(directory, $"{stem}.manifest.sig"),
            ArtifactManifests.SignBase64(bytes, privateKey));
    }

    private static IReadOnlyList<RetrievalBenchmarkCase> Cases()
    {
        return RetrievalBenchmarkCatalog.Load(
            Path.Combine(RepoRoot(), "evals", "retrieval-cases.json"));
    }


    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "Lex.slnx")))
            directory = Directory.GetParent(directory)?.FullName
                        ?? throw new InvalidOperationException("Repository root not found.");
        return directory;
    }

    private sealed class BenchmarkFakeEncoder : ITextEncoder
    {
        public string ModelId => "test/e5";
        public string ModelRevision => "test-revision";
        public int Dimensions => 8;
        public int CountTokens(string text) =>
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        public int PrefixLengthForTokens(string text, int maxTokens) => text.Length;
        public int SuffixStartForTokens(string text, int maxTokens) => 0;
        public float[] Encode(string text, EmbeddingInputKind kind) => [1, 0, 0, 0, 0, 0, 0, 0];
        public void Dispose() { }
    }
}
