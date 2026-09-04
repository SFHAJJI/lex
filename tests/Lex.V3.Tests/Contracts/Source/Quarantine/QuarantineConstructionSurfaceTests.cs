using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Quarantine;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Quarantine;

/// <summary>
/// The construction surface of the D3-04 quarantined prior-coordinate inventory types.
///
/// <para>
/// Transcribed from a deliberate <c>Assert.Fail("ACTUAL&gt;&gt;&gt;" + ...)</c> probe run against
/// the real types (per the working discipline for guarded types in this codebase), not guessed:
/// records auto-generate a private copy constructor and a <c>&lt;Clone&gt;$()</c> method that the
/// reflection sweep reports, and both appear below exactly as produced.
/// </para>
/// <para>
/// <see cref="QuarantinePriorCoordinateReproduction"/> and
/// <see cref="QuarantinedPriorCoordinateInventory"/> are gates: each has exactly one checked door,
/// a private constructor plus the one public static <c>TryCreate</c>/<c>TryReconcile</c> that is
/// its only caller, and the <c>ProducersIn</c> checks below confirm nothing else in
/// <c>Lex.V3.Contracts</c> can hand out an instance without going through it. The remaining four
/// types are open wire shapes on purpose -- untrusted data a caller constructs directly, never
/// evidence a gate mints -- exactly as <c>CutCompletionClaim</c> and its siblings are documented in
/// <c>CutReleaseGateConstructionSurfaceTests</c>. Pinned anyway, so a second constructor or an
/// unexpected new producer on any of them is a visible diff rather than silent.
/// </para>
/// </summary>
[TestClass]
public sealed class QuarantineConstructionSurfaceTests
{
    private const string N = "Lex.V3.Contracts.Source.Quarantine.";
    private const string Core = "Lex.V3.Contracts.Source.Core.";

    [TestMethod]
    public void APriorPublicCoordinateHasExactlyItsOwnPublicConstructor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "PriorPublicCoordinate::.ctor("
                + N + "PriorPublicCoordinate) -> " + N + "PriorPublicCoordinate",
                "constructor public instance " + N + "PriorPublicCoordinate::.ctor(System.String, "
                + "System.String, System.String, System.String?) -> " + N + "PriorPublicCoordinate",
                "method public instance " + N + "PriorPublicCoordinate::<Clone>$() -> "
                + N + "PriorPublicCoordinate",
            },
            ConstructionSurface.Of(typeof(PriorPublicCoordinate)).ToArray());
    }

    [TestMethod]
    public void AReproductionHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "QuarantinePriorCoordinateReproduction::.ctor("
                + N + "QuarantineReproducerRole, System.String, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "PriorPublicCoordinate>, "
                + "System.String) -> " + N + "QuarantinePriorCoordinateReproduction",
                "method public static " + N + "QuarantinePriorCoordinateReproduction::TryCreate("
                + N + "QuarantineReproducerRole, System.String, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "PriorPublicCoordinate>, "
                + "out " + N + "QuarantineReproductionRefusal&) -> "
                + N + "QuarantinePriorCoordinateReproduction?",
            },
            ConstructionSurface.Of(typeof(QuarantinePriorCoordinateReproduction)).ToArray());

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                    typeof(QuarantinePriorCoordinateReproduction).Assembly,
                    typeof(QuarantinePriorCoordinateReproduction),
                    true)
                .ToArray(),
            "nothing else in Contracts may hand out a reproduction it did not derive a digest for");
    }

    [TestMethod]
    public void AVerifierReceiptHasExactlyItsOwnPublicConstructor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "QuarantineVerifierReceipt::.ctor("
                + N + "QuarantineVerifierReceipt) -> " + N + "QuarantineVerifierReceipt",
                "constructor public instance " + N + "QuarantineVerifierReceipt::.ctor(System.String, "
                + "System.Boolean, System.String) -> " + N + "QuarantineVerifierReceipt",
                "method public instance " + N + "QuarantineVerifierReceipt::<Clone>$() -> "
                + N + "QuarantineVerifierReceipt",
            },
            ConstructionSurface.Of(typeof(QuarantineVerifierReceipt)).ToArray());
    }

    [TestMethod]
    public void AnIssuerHasExactlyItsOwnPublicConstructor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "QuarantineIssuer::.ctor("
                + N + "QuarantineIssuer) -> " + N + "QuarantineIssuer",
                "constructor public instance " + N + "QuarantineIssuer::.ctor(System.String, "
                + "System.String, System.String) -> " + N + "QuarantineIssuer",
                "method public instance " + N + "QuarantineIssuer::<Clone>$() -> " + N + "QuarantineIssuer",
            },
            ConstructionSurface.Of(typeof(QuarantineIssuer)).ToArray());
    }

    [TestMethod]
    public void AnAttestationHasExactlyItsOwnPublicConstructor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "QuarantineAttestation::.ctor("
                + N + "QuarantineAttestation) -> " + N + "QuarantineAttestation",
                "constructor public instance " + N + "QuarantineAttestation::.ctor(System.String, "
                + "System.String, System.String, System.String, " + N + "QuarantineIssuer) -> "
                + N + "QuarantineAttestation",
                "method public instance " + N + "QuarantineAttestation::<Clone>$() -> "
                + N + "QuarantineAttestation",
            },
            ConstructionSurface.Of(typeof(QuarantineAttestation)).ToArray());
    }

    [TestMethod]
    public void AnInventoryHasExactlyOneCheckedDoor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "QuarantinedPriorCoordinateInventory::.ctor("
                + "System.Collections.Generic.IReadOnlyList<" + N + "PriorPublicCoordinate>, "
                + "System.String, System.String, " + Core + "SourceArtifactRef, "
                + N + "QuarantineVerifierReceipt, " + N + "QuarantineAttestation, "
                + N + "QuarantineReproducerRole, System.String, " + N + "QuarantineReproducerRole, "
                + "System.String) -> " + N + "QuarantinedPriorCoordinateInventory",
                "method public static " + N + "QuarantinedPriorCoordinateInventory::TryReconcile("
                + N + "QuarantinePriorCoordinateReproduction, "
                + N + "QuarantinePriorCoordinateReproduction, System.String, "
                + Core + "SourceArtifactRef, " + N + "QuarantineVerifierReceipt, "
                + N + "QuarantineAttestation, out " + N + "QuarantineInventoryRefusal&) -> "
                + N + "QuarantinedPriorCoordinateInventory?",
            },
            ConstructionSurface.Of(typeof(QuarantinedPriorCoordinateInventory)).ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                // Not a second mint: VerifySignature takes an already-reconciled inventory (the
                // private constructor makes any other origin impossible) and returns that same
                // reference once its signature checks out. ConstructionSurface reads signatures
                // only, so it cannot see "same instance in, same instance out" and reports this as
                // a producer exactly as it would a real second door -- pinned here explicitly, per
                // this test class's own summary, rather than silently exempted.
                "method public static " + N + "QuarantineInventoryCanonicalizer::VerifySignature("
                + N + "QuarantinedPriorCoordinateInventory, System.Security.Cryptography.ECDsa) -> "
                + N + "QuarantinedPriorCoordinateInventory",
            },
            ConstructionSurface.ProducersIn(
                    typeof(QuarantinedPriorCoordinateInventory).Assembly,
                    typeof(QuarantinedPriorCoordinateInventory),
                    true)
                .ToArray(),
            "nothing else in Contracts may hand out an inventory it did not reconcile, except the "
            + "pass-through verify above");
    }
}
