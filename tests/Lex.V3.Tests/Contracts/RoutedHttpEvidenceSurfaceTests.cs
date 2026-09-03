using Lex.V3.Contracts.Source.Http;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

/// <summary>
/// The construction surface of routed HTTP evidence.
///
/// <para>
/// Why this type and not another. Evidence is the object every capability, receipt and closure in
/// the transport layer ultimately carries. <see cref="RoutedHttpEvidence.Create"/> is public because
/// every producer lives in Lex.V3.Ingest, a different assembly, and Contracts grants friend access
/// only to the two test assemblies (making Create internal would break the only legitimate caller,
/// and Decision 80 refused moving the door into a separate assembly as the same hole moved rather
/// than closed). Visibility alone was tried and rejected as the guard: an earlier design attempted
/// to make this door internal-plus-InternalsVisibleTo(Ingest), and the reviewer ruled it out because
/// that grant is assembly-wide and would have reopened the D1-Core guard that keeps Contracts from
/// ever handing Ingest blanket internal access. Decision 80 instead demands evidence of genuine
/// construction: <see cref="RoutedHttpEvidence.Create"/> now requires, for every hop, the exact
/// custody write receipt that held its body, checked against that hop's own retained digest, length
/// and claimed write-receipt digest before it will mint anything. A caller who never called custody
/// cannot fabricate a matching receipt for free; it must reproduce every one of those equalities.
/// </para>
/// <para>
/// <see cref="RoutedHttpEvidence.CreateFromVerifiedHops"/> exists only because the receipt is proof
/// of construction, not wire data: the canonical JSON round trip has no receipt to hand back, so
/// <c>RoutedHttpCanonicalJson.ParseEvidence</c> reconstructs through this internal, receipt-free path
/// instead of the public door. It is deliberately visible here rather than hidden, so that path is
/// reviewed exactly like any other producer, not assumed safe by omission.
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
    public void EvidenceIsMintedByExactlyFourDeclaredPaths()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + Evidence + "::.ctor("
                + Core + "SourceArtifactRef, System.UInt64, System.UInt64, "
                + "Lex.V3.Contracts.Source.Http.RoutedHttpHop[], "
                + "Lex.V3.Contracts.Source.Http.RoutedHttpRouteOutcome) -> " + Evidence,
                // Internal, not a second unguarded production door: it carries no receipt parameter
                // because it has none to check, and its only callers are Create (after the receipt
                // check passes) and ParseEvidence (reconstructing an already-canonical value for a
                // round trip, per the type's own remarks on why that is not evidence-minting).
                "method internal static " + Evidence + "::CreateFromVerifiedHops("
                + Core + "SourceArtifactRef, System.UInt64, System.UInt64, "
                + "System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Source.Http.RoutedHttpHop>, "
                + "Lex.V3.Contracts.Source.Http.RoutedHttpRouteOutcome) -> " + Evidence,
                // Decision 80: the receipt-checked public door. RequireHopWriteReceipts runs first
                // and CreateFromVerifiedHops is reached only once every hop's receipt has already
                // been proven, so this new sixth parameter is where the door's whole strength lives.
                "method public static " + Evidence + "::Create("
                + Core + "SourceArtifactRef, System.UInt64, System.UInt64, "
                + "System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Source.Http.RoutedHttpHop>, "
                + "Lex.V3.Contracts.Source.Http.RoutedHttpRouteOutcome, "
                + "System.Collections.Generic.IReadOnlyDictionary<System.String, "
                + "Lex.V3.Contracts.Custody.DurableBlobWriteReceipt>) -> " + Evidence,
                "method public static " + Evidence + "::ParseAndVerify(System.ReadOnlySpan<System.Byte>) -> " + Evidence,
            },
            ConstructionSurface.Of(typeof(RoutedHttpEvidence)).ToArray(),
            "a new path onto evidence itself must be justified in review, not discovered later");
    }

    [TestMethod]
    public void EveryOtherHolderOfEvidenceInContractsIsPinned()
    {
        // One producer outside the type, the canonical JSON parser that ParseAndVerify delegates
        // to, and six holders that carry evidence without minting it: the four original,
        // plus LuxembourgObservedTransport's compiler-generated Deconstruct, backing field and
        // property (Lex.V3.Ingest's executor design synthesis, D1-03) - one record carrying the
        // evidence for one delivery observation alongside the request and receipt it belongs with,
        // never minting evidence itself. A ninth line here is the finding.
        const string Luxembourg = "Lex.V3.Contracts.Source.Luxembourg.";
        CollectionAssert.AreEqual(
            new[]
            {
                "by-ref-method public instance " + Core + "RepeatedEnumerationResolvedEvidence::Deconstruct("
                + "out " + Core + "MachineQueryPlan&, out " + Core + "MachineQueryInputArtifact&, "
                + "out " + Core + "MachineQueryRenderReceipt&, out " + Core + "IMachineQueryRenderer&, "
                + "out Lex.V3.Contracts.Source.Http.HttpLogicalRequest&, out " + Evidence + "&, "
                + "out Lex.V3.Contracts.Custody.DurableBlobWriteReceipt&, "
                + "out System.ReadOnlyMemory<System.Byte>&) -> System.Void",
                "by-ref-method public instance " + Luxembourg + "LuxembourgObservedTransport::Deconstruct("
                + "out Lex.V3.Contracts.Source.Http.HttpLogicalRequest&, out " + Evidence + "&, "
                + "out Lex.V3.Contracts.Custody.DurableBlobWriteReceipt&, "
                + "out System.ReadOnlyMemory<System.Byte>&) -> System.Void",
                "field private instance " + Core + "RepeatedEnumerationResolvedEvidence::<HttpEvidence>k__BackingField -> " + Evidence,
                "field private instance " + Luxembourg + "LuxembourgObservedTransport::<HttpEvidence>k__BackingField -> " + Evidence,
                "method public static Lex.V3.Contracts.Source.Http.RoutedHttpCanonicalJson::ParseEvidence(System.ReadOnlySpan<System.Byte>) -> " + Evidence,
                "property public instance " + Core + "EnumerationDeliveryComparison+VerifiedRepeatedEnumerationEvidence::HttpEvidence() -> " + Evidence,
                "property public instance " + Core + "RepeatedEnumerationResolvedEvidence::HttpEvidence() -> " + Evidence,
                "property public instance " + Luxembourg + "LuxembourgObservedTransport::HttpEvidence() -> " + Evidence,
            },
            ConstructionSurface.ProducersIn(typeof(RoutedHttpEvidence).Assembly, typeof(RoutedHttpEvidence), true).ToArray());
    }
}
