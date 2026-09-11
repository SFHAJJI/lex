using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// One real delivered run over a single Appendix A seed, where the ONLY thing a case varies is
/// family A's own script.
/// </summary>
/// <remarks>
/// Extracted rather than copied: every adapter test in this project builds this same scaffolding
/// inline, and four more copies of it would have buried the one line each A3 guard is actually
/// about. Everything except the family-A script is held identical across cases on purpose, so a
/// difference in outcome can only come from family A.
/// </remarks>
internal static class EuAxiomWiringHarness
{
    private static readonly SourceArtifactRef CompleteEnumerationRef = new(
        "urn:uuid:00000000-0000-4000-8000-0000000000f0",
        new string('a', 64));

    private const string ExpressionIri =
        "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000001.0001.01/DOC_1";

    private const string WatermarkLexical = "2026-01-01T00:00:00.0000000+01:00";

    /// <summary>
    /// Runs the adapter over Appendix A's first seed. <paramref name="axiomScript"/> is handed that
    /// seed's canonical root and returns family A's script, or null to deliver family A not at all.
    /// </summary>
    internal static async Task<EuQueryExecutionResult> RunAsync(
        Func<string, EuAcquisitionTestFixture.FamilyScript?> axiomScript,
        Func<string, EuAcquisitionTestFixture.FamilyScript>? locatedAmendmentScript = null)
    {
        var seed = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var rootIri = EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
            ?? throw new AssertFailedException("Appendix A's own seed root failed to canonicalize.");

        var pOutcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.WorkHasResourceType
                    ? EuAcquisitionTestFixture.RegulationResourceType
                    : (string?)null))
            .Concat(EuAcquisitionTestFixture.RelationPredicates.Select(predicate => (predicate, (string?)null)))
            .ToArray();
        var pRows = EuAcquisitionTestFixture.SortedObjectFactRows(rootIri, pOutcomes);
        var xRows = new[] { EuAcquisitionTestFixture.ExpressionFactRow(rootIri, ExpressionIri) };
        var wRows = new[] { EuAcquisitionTestFixture.RootWatermarkRow(rootIri, WatermarkLexical) };

        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            ["Census"] = EuAcquisitionTestFixture.ScriptFor(
                "Census", 0, [], EuAcquisitionTestFixture.CensusFamilyProjection),
            ["P"] = EuAcquisitionTestFixture.ScriptFor(
                "P", pRows.Count, pRows, EuAcquisitionTestFixture.ObjectFactsProjection),
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", xRows.Length, xRows, EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["W"] = EuAcquisitionTestFixture.ScriptFor(
                "W", wRows.Length, wRows, EuAcquisitionTestFixture.RootWatermarkProjection),
            ["M"] = EuAcquisitionTestFixture.ManifestationScriptFor(rootIri),
            ["Witness"] = new EuAcquisitionTestFixture.FamilyScript(
                "Witness",
                EuAcquisitionTestFixture.WitnessEmptyTraversalScript(rootIri, WatermarkLexical)),
        };

        var script = axiomScript(rootIri);
        if (script is not null)
        {
            scripts["A"] = script;
        }

        scripts["L"] = locatedAmendmentScript is null
            ? EuAcquisitionTestFixture.LocatedAmendmentAbsenceScriptFor(rootIri)
            : locatedAmendmentScript(rootIri);

        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequest = new EuCensusPartitionRunRequest(
            censusPlan, censusPlanId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(1));

        var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();

        return await adapter.RunAsync(
            [(censusRequest, EuAcquisitionTestFixture.SourceWitness())],
            new EuObjectFactsBatchPolicy(
                pPlan, pPlanId, EuAcquisitionTestFixture.BuildRendererSource(2),
                EuAcquisitionTestFixture.SourceWitness()),
            EuAcquisitionTestFixture.BuildRendererSource(9),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1009),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            CancellationToken.None);
    }

    /// <summary>
    /// The same run over TWO Appendix A seeds, so family A's rows for both works arrive in one
    /// batch and each seed pass must take only its own.
    /// </summary>
    /// <remarks>
    /// Both roots fall inside a single batch (<c>BatchCapacity</c> is 50), so this is the shape in
    /// which the adapter's per-seed narrowing is the only thing separating one work's axioms from
    /// another's. <paramref name="axiomScript"/> receives both canonical roots in ordinal order.
    /// </remarks>
    internal static async Task<EuQueryExecutionResult> RunTwoSeedAsync(
        Func<string, string, EuAcquisitionTestFixture.FamilyScript> axiomScript)
    {
        var seedOne = EuAppendixASeedMap.SeedsInCelexOrder[0];
        var seedTwo = EuAppendixASeedMap.SeedsInCelexOrder[1];
        var rootOne = EuPackRootCanonicalForm.TryCanonicalize(seedOne.WorkRoot, out _)!;
        var rootTwo = EuPackRootCanonicalForm.TryCanonicalize(seedTwo.WorkRoot, out _)!;
        var roots = new[] { rootOne, rootTwo }.OrderBy(static r => r, StringComparer.Ordinal).ToArray();

        var outcomes = EuAcquisitionTestFixture.ObjectAuthorityPredicates
            .Select(predicate => (
                predicate,
                ValueIri: predicate == EuAcquisitionTestFixture.WorkHasResourceType
                    ? EuAcquisitionTestFixture.RegulationResourceType
                    : (string?)null))
            .Concat(EuAcquisitionTestFixture.RelationPredicates.Select(predicate => (predicate, (string?)null)))
            .ToArray();

        // Every family's rows are concatenated in root order, which is the order its own cursor
        // leads on, so a page that is correct per work is also correct as one delivered page.
        var pRows = roots.SelectMany(root =>
            EuAcquisitionTestFixture.SortedObjectFactRows(root, outcomes)).ToArray();
        var xRows = roots
            .Select((root, index) => EuAcquisitionTestFixture.ExpressionFactRow(
                root, $"{root}.000{index + 1}.01/DOC_1"))
            .ToArray();
        var wRows = roots
            .Select(root => EuAcquisitionTestFixture.RootWatermarkRow(root, WatermarkLexical))
            .ToArray();
        var mRows = roots.SelectMany(root => EuAcquisitionTestFixture.RealBandListedTypes
            .OrderBy(static type => type, StringComparer.Ordinal)
            .Select(type => EuAcquisitionTestFixture.ManifestationFactsRow(root, type))).ToArray();

        var scripts = new Dictionary<string, EuAcquisitionTestFixture.FamilyScript>(StringComparer.Ordinal)
        {
            // One census script pass is consumed per seed, so two seeds need the single-seed
            // script's bodies twice over.
            ["Census"] = new EuAcquisitionTestFixture.FamilyScript(
                "Census",
                [
                    .. EuAcquisitionTestFixture.ScriptFor(
                        "Census", 0, [], EuAcquisitionTestFixture.CensusFamilyProjection).ResponseBodies,
                    .. EuAcquisitionTestFixture.ScriptFor(
                        "Census", 0, [], EuAcquisitionTestFixture.CensusFamilyProjection).ResponseBodies,
                ]),
            ["P"] = EuAcquisitionTestFixture.ScriptFor(
                "P", pRows.Length, pRows, EuAcquisitionTestFixture.ObjectFactsProjection),
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", xRows.Length, xRows, EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["W"] = EuAcquisitionTestFixture.ScriptFor(
                "W", wRows.Length, wRows, EuAcquisitionTestFixture.RootWatermarkProjection),
            ["M"] = EuAcquisitionTestFixture.ScriptFor(
                "M", mRows.Length, mRows, EuAcquisitionTestFixture.ManifestationFactsProjection),
            ["A"] = axiomScript(roots[0], roots[1]),
            ["L"] = EuAcquisitionTestFixture.LocatedAmendmentAbsenceScriptFor(roots),
            ["Witness"] = new EuAcquisitionTestFixture.FamilyScript(
                "Witness",
                EuAcquisitionTestFixture.WitnessEmptyTraversalScript(roots[1], WatermarkLexical)),
        };

        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(scripts);
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var executor = new EuRepeatedEnumerationExecutor(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var (pPlan, pPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();

        return await adapter.RunAsync(
            [
                (new EuCensusPartitionRunRequest(
                    censusPlan, censusPlanId, seedOne.Celex,
                    EuAcquisitionTestFixture.BuildRendererSource(1)),
                    EuAcquisitionTestFixture.SourceWitness()),
                (new EuCensusPartitionRunRequest(
                    censusPlan, censusPlanId, seedTwo.Celex,
                    EuAcquisitionTestFixture.BuildRendererSource(11)),
                    EuAcquisitionTestFixture.SourceWitness()),
            ],
            new EuObjectFactsBatchPolicy(
                pPlan, pPlanId, EuAcquisitionTestFixture.BuildRendererSource(2),
                EuAcquisitionTestFixture.SourceWitness()),
            EuAcquisitionTestFixture.BuildRendererSource(9),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(1009),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PermissiveEvidenceResolver(CompleteEnumerationRef),
            CancellationToken.None);
    }

    private sealed class PermissiveEvidenceResolver(SourceArtifactRef completeEnumerationRef)
        : IScopeReductionEvidenceResolver
    {
        public SourceArtifactRef CompleteEnumerationRef { get; } = completeEnumerationRef;

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) =>
            IsSha256(binding.ObjectRefSha256) && IsSha256(binding.SelectorEvidenceSha256);

        public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding) =>
            IsSha256(binding.ObjectRefSha256);

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) =>
            IsSha256(binding.ObjectRefSha256) &&
            IsSha256(binding.SelectorSetSha256) &&
            IsSha256(binding.RuleEvaluationSha256);

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) =>
            binding.CompleteEnumerationRef == CompleteEnumerationRef;

        private static bool IsSha256(string value) =>
            value.Length == 64 &&
            value.All(static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
    }
}
