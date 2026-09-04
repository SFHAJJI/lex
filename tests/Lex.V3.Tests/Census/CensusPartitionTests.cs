using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Census;

/// <summary>
/// The partition: every type the census had to account for is in one of the three pins or in the
/// declined list below, with a reason. 448 candidates when this was written, 405
/// pinned and 43 declined.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists. The three pins are exact about what they hold and say nothing about what they
/// leave out. The residual used to live in a commit message, which meant a static class holding
/// state that is not a token registry moved nothing at all: it was neither pinned nor declined,
/// and the sentence recording that stayed true only until the next such class was written. A
/// residual stated in prose is not a residual, it is a claim about one afternoon.
/// </para>
/// <para>
/// How it is enforced. <see cref="ClosedSurfaceCensus.Candidates"/> sweeps every type that is a
/// closed vocabulary, a construction-restricted type, or a static class holding any state, which is
/// wider than any of the three pins on purpose. The assertion is that this set equals the types the
/// three pins hold plus the declined names below. A type in none of the four fails. The declined
/// list is a literal and its reasons are prose, but its membership is not: a class that stops
/// matching its reason has to be moved or the test goes red.
/// </para>
/// <para>
/// The declined reasons, and why each was declined rather than pinned. A stateful static that is
/// not a token registry is a helper holding a cached serializer or one schema id, and pinning
/// helpers would drown the registry pin in rows nobody reads. A constant table is a set of numeric
/// limits, which is not a vocabulary: its values already have their own tests, and widening the
/// registry rule to swallow it would raise a coverage number without closing a defect. Neither
/// decision is enforced by anything except this list, which is why the list is here rather than in
/// a report.
/// </para>
/// </remarks>
[TestClass]
public sealed class CensusPartitionTests
{
    /// <summary>
    /// Types the census sweeps and has decided not to pin, as <c>full name: reason</c>. Membership
    /// is enforced by <see cref="EveryCensusCandidateIsPinnedOrDeclinedWithAReason"/>; the reasons
    /// are the part a person has to keep true.
    /// </summary>
    private static readonly string[] Declined =
    [
        "Lex.V3.Api.BoundedJsonBuffer: stateful static, not a token registry",
        "Lex.V3.Api.RequestReferenceFactory: stateful static, not a token registry",
        "Lex.V3.Api.SyntheticApiBootstrap: stateful static, not a token registry",
        "Lex.V3.Api.SyntheticResponseMapper: stateful static, not a token registry",
        "Lex.V3.Artifacts.SyntheticSliceArtifactCanonicalizer: stateful static, not a token registry",
        "Lex.V3.Artifacts.SyntheticTextNormalizer: stateful static, not a token registry",
        "Lex.V3.Contracts.ContractJson: stateful static, not a token registry",
        "Lex.V3.Contracts.ContractLine: stateful static, not a token registry",
        "Lex.V3.Contracts.Custody.CustodyBounds: stateful static, not a token registry",
        "Lex.V3.Contracts.PreviewContractLimits: constant table, not a vocabulary",
        "Lex.V3.Contracts.PreviewDocumentCanonicalizer: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Absence.AbsenceKey: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Absence.AbsenceTiming: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Absence.AbsenceValidation: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Core.ContentDerivedIdentity: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Core.EnumerationCursorEnvelope: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Core.MachineQueryPlanIdentity: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Core.MachineQueryRenderReceiptIdentity: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Core.MachineQueryValidation: constant table, not a vocabulary",
        "Lex.V3.Contracts.Source.Core.RepeatedEnumerationInterpretationProfileIdentity: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Core.RobotsExclusionPolicy: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Core.SourceCoreValidation: constant table, not a vocabulary",
        "Lex.V3.Contracts.Source.Corpus.CorpusRecordCanonicalWriter: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Corpus.CorpusRecordSchemaIds: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Corpus.CorpusRecordSetCanonicalWriter: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Corpus.CorpusRecordSetSchemaIds: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Europe.EuAcquisitionPeriod: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Http.RoutedHttpValidation: constant table, not a vocabulary",
        "Lex.V3.Contracts.Source.Luxembourg.LuxembourgQueryPageBinder: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Luxembourg.LuxembourgQueryPassPolicy: constant table, not a vocabulary",
        "Lex.V3.Contracts.Source.Luxembourg.LuxembourgQueryPlanIdentity: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Luxembourg.LuxembourgQueryText: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Luxembourg.LuxembourgSourceValidation: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Quarantine.QuarantineInventoryCanonicalizer: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Scope.ScopeManifestSchemaIds: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Scope.ScopeManifestSchemaResourceIds: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Scope.ScopeSchemaExporter: stateful static, not a token registry",
        "Lex.V3.Contracts.Source.Scope.ScopeSchemaHardener: stateful static, not a token registry",
        "Lex.V3.Contracts.SyntheticSliceContractLimits: constant table, not a vocabulary",
        "Lex.V3.Contracts.SyntheticSliceOperationCatalog: stateful static, not a token registry",
        "Lex.V3.Custody.Azure.AzureCustodySchemaIds: stateful static, not a token registry",
        "Lex.V3.Preview.SyntheticPreviewSourceDigest: constant table, not a vocabulary",
        "Lex.V3.Preview.SyntheticSourceStore: constant table, not a vocabulary",
    ];

    [TestMethod]
    public void EveryCensusCandidateIsPinnedOrDeclinedWithAReason()
    {
        var pinned = ClosedSurfaceCensus.ClosedVocabularies(CensusScope.SweptHere)
            .Concat(ClosedSurfaceCensus.GuardedConstruction(CensusScope.SweptHere))
            .Concat(ClosedSurfaceCensus.VocabularyRegistries(CensusScope.SweptHere))
            .Select(NameOf);

        CollectionAssert.AreEqual(
            ClosedSurfaceCensus.Candidates(CensusScope.SweptHere).ToArray(),
            pinned
                .Concat(Declined.Select(NameOf))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray(),
            "a swept type is in no pin and on no declined list, so nothing records what it is");
    }

    [TestMethod]
    public void ThePartitionTotalsAreExactlyThese()
    {
        Assert.AreEqual(
            448, ClosedSurfaceCensus.Candidates(CensusScope.SweptHere).Count, "candidates");
        Assert.AreEqual(
            211, ClosedSurfaceCensus.ClosedVocabularies(CensusScope.SweptHere).Count, "vocabularies");
        Assert.AreEqual(
            136, ClosedSurfaceCensus.GuardedConstruction(CensusScope.SweptHere).Count, "guarded types");
        Assert.AreEqual(
            58, ClosedSurfaceCensus.VocabularyRegistries(CensusScope.SweptHere).Count, "registries");
        Assert.AreEqual(43, Declined.Length, "declined");
    }

    private static string NameOf(string row) =>
        row[..row.IndexOf(':', StringComparison.Ordinal)];
}
