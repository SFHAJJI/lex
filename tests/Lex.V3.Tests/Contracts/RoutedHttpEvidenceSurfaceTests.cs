using Lex.V3.Contracts.Source.Http;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

/// <summary>
/// The construction surface of routed HTTP evidence.
///
/// <para>
/// Why this type and not another. Evidence is the object every capability, receipt and closure in
/// the transport layer ultimately carries, and <see cref="RoutedHttpEvidence.Create"/> is public
/// and validates shape only: hop count, nulls and the ordinal chain. Nothing in it ties those hops
/// to anything retained, so well-formed evidence describing a fetch that never happened is
/// constructible from any assembly referencing Contracts.
/// </para>
/// <para>
/// It is not closed, and the reason is structural rather than a preference: every producer lives in
/// Lex.V3.Ingest, a different assembly, and Contracts grants friend access only to the two test
/// assemblies. Making Create internal would break the only legitimate caller. What replaces closure
/// is this pin plus the acceptance path: evidence entering a receipt goes through ParseAndVerify
/// against retained bytes, so unretained evidence is refused where it matters rather than at
/// construction.
/// </para>
/// <para>
/// So the pin's job is to make a new producer a visible diff. A method returning evidence, a
/// by-ref parameter carrying it, a field or property holding it, anywhere in Contracts, appears
/// here and must be justified in review rather than noticed later.
/// </para>
/// </summary>
[TestClass]
public sealed class RoutedHttpEvidenceSurfaceTests
{
    private const string Evidence = "Lex.V3.Contracts.Source.Http.RoutedHttpEvidence";
    private const string Core = "Lex.V3.Contracts.Source.Core.";

    [TestMethod]
    public void EvidenceIsMintedByExactlyTwoDeclaredPaths()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + Evidence + "::.ctor("
                + Core + "SourceArtifactRef, System.UInt64, System.UInt64, "
                + "Lex.V3.Contracts.Source.Http.RoutedHttpHop[], "
                + "Lex.V3.Contracts.Source.Http.RoutedHttpRouteOutcome) -> " + Evidence,
                "method public static " + Evidence + "::Create("
                + Core + "SourceArtifactRef, System.UInt64, System.UInt64, "
                + "System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Source.Http.RoutedHttpHop>, "
                + "Lex.V3.Contracts.Source.Http.RoutedHttpRouteOutcome) -> " + Evidence,
                "method public static " + Evidence + "::ParseAndVerify(System.ReadOnlySpan<System.Byte>) -> " + Evidence,
            },
            ConstructionSurface.Of(typeof(RoutedHttpEvidence)).ToArray(),
            "a new path onto evidence itself must be justified in review, not discovered later");
    }

    [TestMethod]
    public void EveryOtherHolderOfEvidenceInContractsIsPinned()
    {
        // One producer outside the type, the canonical JSON parser that ParseAndVerify delegates
        // to, and four holders that carry evidence without minting it. A fifth line here is the
        // finding.
        CollectionAssert.AreEqual(
            new[]
            {
                "by-ref-method public instance " + Core + "RepeatedEnumerationResolvedEvidence::Deconstruct("
                + "out " + Core + "MachineQueryPlan&, out " + Core + "MachineQueryInputArtifact&, "
                + "out " + Core + "MachineQueryRenderReceipt&, out " + Core + "IMachineQueryRenderer&, "
                + "out Lex.V3.Contracts.Source.Http.HttpLogicalRequest&, out " + Evidence + "&, "
                + "out Lex.V3.Contracts.Custody.DurableBlobWriteReceipt&, "
                + "out System.ReadOnlyMemory<System.Byte>&) -> System.Void",
                "field private instance " + Core + "RepeatedEnumerationResolvedEvidence::<HttpEvidence>k__BackingField -> " + Evidence,
                "method public static Lex.V3.Contracts.Source.Http.RoutedHttpCanonicalJson::ParseEvidence(System.ReadOnlySpan<System.Byte>) -> " + Evidence,
                "property public instance " + Core + "EnumerationDeliveryComparison+VerifiedRepeatedEnumerationEvidence::HttpEvidence() -> " + Evidence,
                "property public instance " + Core + "RepeatedEnumerationResolvedEvidence::HttpEvidence() -> " + Evidence,
            },
            ConstructionSurface.ProducersIn(typeof(RoutedHttpEvidence).Assembly, typeof(RoutedHttpEvidence), true).ToArray());
    }
}
