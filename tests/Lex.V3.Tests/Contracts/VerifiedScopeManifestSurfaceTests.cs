using Lex.V3.Contracts.Source.Scope;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

/// <summary>
/// The construction surface of a verified scope manifest.
///
/// <para>
/// Why this type and not another. <see cref="VerifiedScopeManifest.Manifest"/> is the only content
/// door onto a reduced scope manifest, and it is public precisely because the constructor above it
/// is the closed door (Decision 80): holding an instance is the evidence that every reader-only
/// invariant already ran, so nothing needs InternalsVisibleTo to read the content once produced
/// legitimately. That guarantee is only as good as the list of things that can produce an instance
/// in the first place.
/// </para>
/// <para>
/// It is not closed by an assembly boundary: the constructor is internal to
/// <c>Lex.V3.Contracts</c>, so every legitimate producer lives inside this one assembly, and this
/// pin is what makes a new producer a visible diff rather than a silent addition. It includes
/// the live reducer, independent span and stream readers, and retained-byte comparison against
/// an existing graph with renewed evidence admission. The private canonical comparison helper
/// is pinned too. Luxembourg profile reduction wrappers are covered by the assembly sweep.
/// </para>
/// </summary>
[TestClass]
public sealed class VerifiedScopeManifestSurfaceTests
{
    private const string Verified = "Lex.V3.Contracts.Source.Scope.VerifiedScopeManifest";
    private const string Manifest = "Lex.V3.Contracts.Source.Scope.ScopeManifest";
    private const string Core = "Lex.V3.Contracts.Source.Core.";
    private const string Scope = "Lex.V3.Contracts.Source.Scope.";

    [TestMethod]
    public void VerifiedManifestsHaveExactlyTheseDeclaredPaths()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor internal instance "
                    + "Lex.V3.Contracts.Source.Scope.VerifiedScopeManifest::.ctor(Lex.V3.Contracts."
                    + "Source.Scope.ScopeManifest) -> "
                    + "Lex.V3.Contracts.Source.Scope.VerifiedScopeManifest",
                "method private static "
                    + "Lex.V3.Contracts.Source.Scope.VerifiedScopeManifest::VerifyCanonicalReadback"
                    + "(Lex.V3.Contracts.Source.Core.SourceArtifactRef, System.IO.Stream, "
                    + "Lex.V3.Contracts.Source.Scope.VerifiedScopeManifest) -> "
                    + "Lex.V3.Contracts.Source.Scope.VerifiedScopeManifest",
                "method public static "
                    + "Lex.V3.Contracts.Source.Scope.VerifiedScopeManifest::ParseAndVerify(Lex.V3.C"
                    + "ontracts.Source.Core.SourceArtifactRef, System.ReadOnlySpan<System.Byte>, "
                    + "Lex.V3.Contracts.Source.Scope.IScopeReductionEvidenceResolver) -> "
                    + "Lex.V3.Contracts.Source.Scope.VerifiedScopeManifest",
                "method public static "
                    + "Lex.V3.Contracts.Source.Scope.VerifiedScopeManifest::ParseAndVerifyStream(Le"
                    + "x.V3.Contracts.Source.Core.SourceArtifactRef, System.IO.Stream, "
                    + "Lex.V3.Contracts.Source.Scope.IScopeReductionEvidenceResolver) -> "
                    + "Lex.V3.Contracts.Source.Scope.VerifiedScopeManifest",
                "method public static "
                    + "Lex.V3.Contracts.Source.Scope.VerifiedScopeManifest::VerifyStreamAgainst(Lex"
                    + ".V3.Contracts.Source.Core.SourceArtifactRef, System.IO.Stream, "
                    + "Lex.V3.Contracts.Source.Scope.VerifiedScopeManifest, "
                    + "Lex.V3.Contracts.Source.Scope.IScopeReductionEvidenceResolver) -> "
                    + "Lex.V3.Contracts.Source.Scope.VerifiedScopeManifest",
            },
            ConstructionSurface.Of(typeof(VerifiedScopeManifest)).ToArray(),
            "a new path onto the verified manifest itself must be justified in review, not "
            + "discovered later");
    }

    [TestMethod]
    public void EveryOtherProducerOfAVerifiedManifestInContractsIsPinned()
    {
        // Two producers for a live, in-process reduction (ScopeReducer's own two public statics)
        // and one thin pass-through on the LU adapter's profile type. A fourth line here is the
        // finding: a new way to mint a verified manifest without going through ParseAndVerify or
        // ScopeReducer needs the same scrutiny these three already got.
        CollectionAssert.AreEqual(
            new[]
            {
                "method public instance Lex.V3.Contracts.Source.Luxembourg.VerifiedLuxembourgSourceProfile"
                + "::ReduceScope(Lex.V3.Contracts.Source.Luxembourg.LuxembourgProfileResolution+Resolved, "
                + Scope + "IScopeReductionEvidenceResolver) -> " + Verified,
                // D1-06c-LU-2's own overload, which projects this run's minted per-object fetch
                // addresses onto the reduction inputs and then calls ScopeReducer.Reduce exactly as
                // the two-parameter door does. It adds no verification of its own either, and the
                // shorter overload now forwards to it, so there is still one reduction path.
                "method public instance Lex.V3.Contracts.Source.Luxembourg.VerifiedLuxembourgSourceProfile"
                + "::ReduceScope(Lex.V3.Contracts.Source.Luxembourg.LuxembourgProfileResolution+Resolved, "
                + Scope + "IScopeReductionEvidenceResolver, "
                + "System.Collections.Generic.IReadOnlyDictionary<" + Core + "SourceObjectRef, "
                + Scope + "ScopeManifestFetchAddress>) -> " + Verified,
                "method public static " + Scope + "ScopeReducer::Reduce(" + Scope + "ScopeProfileBinding, "
                + "System.Collections.Generic.IReadOnlyList<" + Core + "SourceArtifactRef>, "
                + "System.Collections.Generic.IReadOnlyList<" + Core + "SourceObjectRef>, "
                + "System.Collections.Generic.IReadOnlyList<" + Scope + "ScopeObjectReductionInput>, "
                + Scope + "IScopeReductionEvidenceResolver) -> " + Verified,
                "method public static " + Scope + "ScopeReducer::VerifyAndOpen(" + Manifest + ", "
                + Scope + "IScopeReductionEvidenceResolver) -> " + Verified,
            },
            ConstructionSurface.ProducersIn(
                typeof(VerifiedScopeManifest).Assembly,
                typeof(VerifiedScopeManifest),
                true).ToArray());
    }
}
