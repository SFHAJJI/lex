using System.Reflection;
using System.Runtime.CompilerServices;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// The construction surface of the two citations an outside consumer forged.
/// </summary>
/// <remarks>
/// <para>
/// WHAT WAS REPRODUCED. Referencing only <c>Lex.V3.Contracts</c> - no producer, no enumeration
/// result, no friend grant - a reviewer constructed
/// <see cref="LuxembourgInitialDraftInventoryCitation"/> and
/// <see cref="LuxembourgDraftBatchCitation"/>, called <c>LuxembourgDraftBatchAssignment.Over</c> to
/// obtain an assignment, and called the public <c>TryComplete</c>. It returned a minted coverage
/// with <c>refusal=None</c>, derived absences and an unresolved gap, for a family the caller had
/// named <c>caller-invented-family</c>. The assignment introduced to make membership structural
/// proved only that the caller's population matched the caller's own citation, because both were
/// the caller's to write.
/// </para>
/// <para>
/// WHY THIS IS ASSERTED OVER METADATA AND NOT BY A PROBE THAT FAILS TO COMPILE. A synthetic consumer
/// project would have to fail to build to prove the negative, so it could not be in
/// <c>Lex.V3.slnx</c>, so CI would never restore it; and this repository has already written down
/// the general objection, at <c>EuFactsEvidenceBundle</c>: "the compile-time exclusion cannot be
/// exercised by a test - code that does not compile cannot be run." <see cref="ConstructionSurface"/>
/// reads CIL accessibility flags instead, and those return the same answer no matter which assembly
/// asks. Friend access is a C# compile-time rule, not a metadata bit, so the fact that
/// <c>Lex.V3.Tests</c> is a friend of Contracts cannot soften anything asserted here. That is
/// precisely what a friend test declining to forge could not establish.
/// </para>
/// <para>
/// AND THE PRODUCTION ASSEMBLY IS ITSELF THE OUTSIDE CONSUMER. <c>Lex.V3.Ingest</c>, where both
/// mint sites live, holds no friend grant - pinned below and independently at
/// <c>MachineQueryPlanContractTests.ProductionIngestCannotBypassContractConstructionControls</c>. So
/// every solution build is a standing proof that these citations are reachable from outside
/// Contracts only through a door that demands an enumeration proof.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgInventoryCitationConstructionSurfaceTests
{
    [TestMethod]
    public void NeitherCitationHasAPublicConstructor()
    {
        Assert.AreEqual(
            0,
            typeof(LuxembourgInitialDraftInventoryCitation).GetConstructors().Length,
            "a public constructor is the forgery, restored.");
        Assert.AreEqual(
            0,
            typeof(LuxembourgDraftBatchCitation).GetConstructors().Length,
            "and the batch citation was the other half of the forged pair.");
    }

    /// <summary>
    /// The only way to obtain either citation is a door that demands an enumeration proof.
    /// </summary>
    /// <remarks>
    /// Asserted as the exact door NAME plus the exact parameter TYPES, not as a count. A count is
    /// satisfied by a new door taking a caller's own values, which is the failure this whole change
    /// exists to undo: a public door whose arguments are all forgeable is worth no more than a
    /// public constructor, and that is how the previous repair's own remarks came to say something
    /// untrue. <c>AbsenceFamilyEnumerationProof</c> is the argument that cannot be written out of
    /// nothing - its constructor is private, and its only door refuses any outcome but equal
    /// selections over an <c>EnumerationDeliveryComparison</c> whose own only door replayed two
    /// independently agreeing, custody-verified passes.
    /// </remarks>
    [TestMethod]
    public void TheOnlyDoorOnEachCitationDemandsAnEnumerationProof()
    {
        AssertSingleDoorTakingAProof(typeof(LuxembourgInitialDraftInventoryCitation), "MintedOver");
        AssertSingleDoorTakingAProof(typeof(LuxembourgDraftBatchCitation), "ForDelivery");
    }

    private static void AssertSingleDoorTakingAProof(Type citation, string expectedDoor)
    {
        var doors = citation
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.ReturnType == citation)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { expectedDoor },
            doors.Select(static method => method.Name).OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
            citation.Name + " must have exactly one door that yields it.");

        var parameters = doors[0].GetParameters();
        Assert.AreEqual(
            "Lex.V3.Contracts.Source.Absence.AbsenceFamilyEnumerationProof",
            parameters[0].ParameterType.FullName,
            "the door's first argument must be the thing a caller cannot fabricate. A door taking "
                + "only values the caller already holds is a public constructor with extra steps.");

        Assert.IsFalse(
            parameters.Any(static parameter =>
                parameter.ParameterType == typeof(SourceArtifactRef)),
            "the acquisition run must be READ OFF the proof, never accepted beside it: accepting it "
                + "is exactly how a forged citation named a run that never happened.");
    }

    /// <summary>
    /// No member of either citation can be rewritten, so <c>with</c> cannot reopen construction.
    /// </summary>
    /// <remarks>
    /// THE QUIETEST WAY TO UNDO ALL OF THIS. A positional record declares every member
    /// <c>{ get; init; }</c> and leaves <c>&lt;Clone&gt;$</c> public, so making only the constructor
    /// private would still let anyone holding one honest citation write
    /// <c>honest with { FamilyKey = "caller-invented-family" }</c> - and honest citations ARE handed
    /// out publicly, on the assignment, the coverage, every absence and every gap. The members are
    /// get-only, so <c>&lt;Clone&gt;$</c> survives on the census row but has nothing it can change;
    /// an external consumer attempting it gets CS0200. Anyone restoring positional syntax here
    /// reopens the finding without touching the constructor, and this is what says so.
    /// </remarks>
    [TestMethod]
    public void NoMemberOfEitherCitationCanBeRewritten()
    {
        foreach (var citation in new[]
                 {
                     typeof(LuxembourgInitialDraftInventoryCitation),
                     typeof(LuxembourgDraftBatchCitation),
                 })
        {
            var writable = citation
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(static property => property.SetMethod is not null)
                .Select(static property => property.Name)
                .ToArray();

            CollectionAssert.AreEqual(
                Array.Empty<string>(),
                writable,
                citation.Name + " has a settable member, so `with` can rewrite what a proof "
                    + "established. Positional record syntax reintroduces exactly this.");
        }
    }

    /// <summary>
    /// Neither the producers' assembly nor anything else may be granted blanket internal access.
    /// </summary>
    /// <remarks>
    /// Making the doors internal and granting <c>InternalsVisibleTo("Lex.V3.Ingest")</c> was tried
    /// while repairing this finding, and <c>ProductionIngestCannotBypassContractConstructionControls</c>
    /// failed on the next run. The reasoning is recorded at <c>RoutedHttpEvidenceSurfaceTests</c>:
    /// the grant is assembly-wide and reopens the D1-Core guard that keeps Contracts from ever
    /// handing Ingest blanket internal access. This restates it against these two types, so a future
    /// reader repairing this same finding meets the answer at the type rather than three files away.
    /// </remarks>
    [TestMethod]
    public void TheCitationsAreNotReachableByGrantingIngestInternalAccess()
    {
        var granted = typeof(LuxembourgInitialDraftInventoryCitation).Assembly
            .GetCustomAttributes(typeof(InternalsVisibleToAttribute), inherit: false)
            .Cast<InternalsVisibleToAttribute>()
            .Select(static attribute => attribute.AssemblyName)
            .ToArray();

        Assert.IsFalse(
            granted.Any(static name =>
                name.StartsWith("Lex.V3.Ingest,", StringComparison.Ordinal) ||
                string.Equals(name, "Lex.V3.Ingest", StringComparison.Ordinal)),
            "visibility was tried as the guard and refused; the door demands a proof instead.");

        CollectionAssert.AreEquivalent(
            new[] { "Lex.V3.Tests", "Lex.V3.Ingest.Tests" },
            granted,
            "the friend list is the two test assemblies and stays that way.");
    }
}
