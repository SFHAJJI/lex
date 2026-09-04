using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// D1-05's own primary enumeration: the closure query's discovered root set, structurally separate
/// from the witness (<see cref="EuFeedRootIntersection"/>) and structurally unable to admit a root
/// Appendix A never froze (point 9 of the D1-04/D1-05 design-synthesis ruling).
/// </summary>
[TestClass]
public sealed class EuPrimaryEnumerationRootBindingTests
{
    private const string N = "Lex.V3.Contracts.Source.Europe.";

    private static string SeedA => EuAppendixASeedMap.PackRoots[0];
    private static string SeedB => EuAppendixASeedMap.PackRoots[1];
    private static string NotASeed => "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000000";

    private static SourceArtifactRef PlanRef(string suffix = "1") =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000b1", new string(suffix[0], 64));

    [TestMethod]
    public void TheBindingHasExactlyOneConstructionPath()
    {
        // The type carries a private static readonly UTF8Encoding for its digest, which the
        // compiler gives a static constructor, exactly as EuFeedRootIntersection's own pin notes.
        // ConstructionSurface pins every constructor the type declares without distinguishing
        // instance from static, so it is pinned rather than filtered.
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuPrimaryEnumerationRootBinding::.ctor("
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, "
                + "System.Collections.Generic.HashSet<System.String>, System.String) -> " + N
                + "EuPrimaryEnumerationRootBinding",
                "constructor private static " + N + "EuPrimaryEnumerationRootBinding::.cctor() -> "
                + N + "EuPrimaryEnumerationRootBinding",
                "method public static " + N + "EuPrimaryEnumerationRootBinding::TryBind("
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<System.String>, out "
                + N + "EuPrimaryEnumerationRefusal&) -> " + N + "EuPrimaryEnumerationRootBinding?",
            },
            ConstructionSurface.Of(typeof(EuPrimaryEnumerationRootBinding)).ToArray());
    }

    [TestMethod]
    public void TwoRealSeedsBindCleanlyAndCanonicallySorted()
    {
        var binding = EuPrimaryEnumerationRootBinding.TryBind(
            PlanRef(), new[] { SeedB, SeedA }, out var refusal);

        Assert.IsNotNull(binding);
        Assert.AreEqual(EuPrimaryEnumerationRefusal.None, refusal);
        CollectionAssert.AreEqual(
            new[] { SeedA, SeedB }.OrderBy(static s => s, StringComparer.Ordinal).ToArray(),
            binding!.DiscoveredRoots.ToArray());
        Assert.IsTrue(binding.Contains(SeedA));
        Assert.IsTrue(binding.Contains(SeedB));
        Assert.IsFalse(binding.Contains(NotASeed));
    }

    [TestMethod]
    public void ABlankRootRefusesOnBlank()
    {
        var binding = EuPrimaryEnumerationRootBinding.TryBind(
            PlanRef(), new[] { SeedA, "   " }, out var refusal);
        Assert.IsNull(binding);
        Assert.AreEqual(EuPrimaryEnumerationRefusal.ResolvedRootBlank, refusal);
    }

    [TestMethod]
    public void ANonCanonicalRootRefusesOnNotCanonical()
    {
        var binding = EuPrimaryEnumerationRootBinding.TryBind(
            PlanRef(), new[] { SeedA + "?x=1" }, out var refusal);
        Assert.IsNull(binding);
        Assert.AreEqual(EuPrimaryEnumerationRefusal.ResolvedRootNotCanonical, refusal);
    }

    [TestMethod]
    public void AnHttpAndHttpsSpellingOfOneSeedRefusesOnRepeated()
    {
        var https = "https" + SeedA["http".Length..];
        var binding = EuPrimaryEnumerationRootBinding.TryBind(
            PlanRef(), new[] { SeedA, https }, out var refusal);
        Assert.IsNull(binding);
        Assert.AreEqual(EuPrimaryEnumerationRefusal.ResolvedRootRepeated, refusal);
    }

    [TestMethod]
    public void AWellFormedRootOutsideAppendixARefusesRatherThanSilentlyAdmitting()
    {
        // The point-9 case: NotASeed is a perfectly well formed, already-canonical http Cellar-shaped
        // URI. Nothing about its syntax is wrong. It is refused purely because Appendix A never froze
        // it, which is the whole content of this refusal.
        var binding = EuPrimaryEnumerationRootBinding.TryBind(
            PlanRef(), new[] { SeedA, NotASeed }, out var refusal);
        Assert.IsNull(binding);
        Assert.AreEqual(EuPrimaryEnumerationRefusal.ResolvedRootOutsideAppendixAPack, refusal);
    }

    // --- Fold-in: pin the binding digest's wire form with a hand-transcribed literal, never merely
    // a self-comparison. TheDigestIsSensitiveToTheDiscoveredRootsAndStableAcrossInputOrder below only
    // ever compares one computed digest against another computed digest, which can never catch the
    // wire form itself silently changing (a different join character, a reordered field, a different
    // hash) as long as both sides of every comparison still agree with each other. -------------------

    [TestMethod]
    public void TheBindingIdentityDigestIsPinnedToAFixedLiteralWireForm()
    {
        var binding = EuPrimaryEnumerationRootBinding.TryBind(PlanRef(), new[] { SeedA, SeedB }, out _)!;

        // Print-actual-then-transcribe: this is the exact SHA-256 BindingDigest computes today over
        // "eu_primary_enumeration_root_binding/1", this fixture's own PlanRef resource id and sha256,
        // and the two sorted discovered roots, newline joined. A change to the wire form (the schema
        // label, the join character, the field order, or the hash itself) is expected to change this
        // literal, which is exactly what makes it a pin rather than a tautology.
        Assert.AreEqual(
            "286339ed1ed391996b48a84b92556df915c5b33b453d8394fff2b4f1ee9f1e2f",
            binding.BindingIdentityDigest);
    }

    [TestMethod]
    public void TheDigestIsSensitiveToTheDiscoveredRootsAndStableAcrossInputOrder()
    {
        var withA = EuPrimaryEnumerationRootBinding.TryBind(PlanRef(), new[] { SeedA }, out _)!;
        var withAThenB = EuPrimaryEnumerationRootBinding.TryBind(PlanRef(), new[] { SeedA, SeedB }, out _)!;
        var withBThenA = EuPrimaryEnumerationRootBinding.TryBind(PlanRef(), new[] { SeedB, SeedA }, out _)!;

        Assert.AreNotEqual(withA.BindingIdentityDigest, withAThenB.BindingIdentityDigest);
        Assert.AreEqual(withAThenB.BindingIdentityDigest, withBThenA.BindingIdentityDigest);
    }

    [TestMethod]
    public void ANullRootThrowsRatherThanRefusing()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => EuPrimaryEnumerationRootBinding.TryBind(
                PlanRef(), new List<string> { null! }, out _));
    }
}
