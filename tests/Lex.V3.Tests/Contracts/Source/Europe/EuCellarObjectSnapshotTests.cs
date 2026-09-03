using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The per-object RDF snapshot: one admitted Cellar object's raw observed assertions and relations,
/// restricted to the closed EU predicate and relation-family vocabulary, with "not observed" kept
/// distinct from "observed absent" throughout.
/// </summary>
[TestClass]
public sealed class EuCellarObjectSnapshotTests
{
    private const string N = "Lex.V3.Contracts.Source.Europe.";

    private static string SeedA => EuAppendixASeedMap.PackRoots[0];
    private static string NotASeed =>
        "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000000";

    private static SourceArtifactRef Artifact(string id) =>
        new($"urn:uuid:{DeterministicGuid(id)}", Digest("evidence:" + id));

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>A stable GUID derived from an arbitrary fixture label, so any label is a valid urn:uuid.</summary>
    private static Guid DeterministicGuid(string label) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("guid:" + label))[..16]);

    private static SourceObjectRef ObjectRef() => new(
        SourceCoreSchemaIds.SourceObjectRef,
        SourceAuthority.Cellar,
        new SourceRegistryMemberRef(Artifact("44aa505f-d55f-4d6c-aef0-21ddcb46633d"), "work"),
        SeedA,
        "cellar:work:seed-a",
        Digest("cellar:work:seed-a"),
        Artifact("08ca1acc-142a-4807-8cc0-d84e412e1d07"),
        null);

    private static IReadOnlyList<EuPredicateObservation> AllPredicatesNotObserved() =>
        EuScopeVocabulary.CdmPredicates
            .Select(p => new EuPredicateObservation(
                p, EuPredicateObservationState.NotObserved, [], Artifact("p-" + p)))
            .ToArray();

    private static IReadOnlyList<EuRelationFamilyObservation> AllReadFamiliesUnacquired() =>
        EuScopeVocabulary.ReadRelationFamilies
            .Select(f => new EuRelationFamilyObservation(
                f, EuRelationAcquisitionState.Unacquired, [], null))
            .ToArray();

    private static EuChannelObservation Channel() =>
        new(EuChannel.CellarSparqlEndpoint, "eu_channel.sparql", "rule.channel", Artifact("channel"));

    private static EuCellarObjectSnapshot? Build(
        out EuCellarObjectSnapshotRefusal refusal,
        string? workRoot = null,
        IReadOnlyList<EuPredicateObservation>? predicates = null,
        IReadOnlyList<EuRelationFamilyObservation>? relations = null,
        EuLanguageExpressionObservation? language = null,
        EuFormatObservation? format = null,
        EuContentClassObservation? rights = null,
        EuContentClassObservation? supporting = null)
    {
        var snapshot = EuCellarObjectSnapshot.TryObserve(
            ObjectRef(),
            workRoot ?? SeedA,
            EuActForm.Regulation,
            Artifact("record"),
            predicates ?? AllPredicatesNotObserved(),
            Channel(),
            language,
            format,
            rights,
            relations ?? AllReadFamiliesUnacquired(),
            Artifact("relation-axis"),
            supporting,
            Artifact("supporting"),
            out refusal);
        return snapshot;
    }

    // ---- Construction surface. ----

    [TestMethod]
    public void TheSnapshotHasExactlyOneConstructionPath()
    {
        // EuActForm, EuCdmPredicate and EuRelationFamily are declared in Lex.V3.Contracts itself
        // (EuScopeDimensions.cs), not Lex.V3.Contracts.Source.Europe, so they carry the shorter
        // prefix C rather than N below.
        const string C = "Lex.V3.Contracts.";
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuCellarObjectSnapshot::.ctor("
                + "Lex.V3.Contracts.Source.Core.SourceObjectRef, System.String, " + C + "EuActForm, "
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuPredicateObservation>, "
                + "System.Collections.Generic.IReadOnlyDictionary<" + C + "EuCdmPredicate, " + N
                + "EuPredicateObservation>, " + N + "EuChannelObservation, " + N
                + "EuLanguageExpressionObservation, " + N + "EuFormatObservation, " + N
                + "EuContentClassObservation, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuRelationFamilyObservation>, "
                + "System.Collections.Generic.IReadOnlyDictionary<" + C + "EuRelationFamily, " + N
                + "EuRelationFamilyObservation>, Lex.V3.Contracts.Source.Core.SourceArtifactRef, " + N
                + "EuContentClassObservation, Lex.V3.Contracts.Source.Core.SourceArtifactRef) -> " + N
                + "EuCellarObjectSnapshot",
                "method public static " + N + "EuCellarObjectSnapshot::TryObserve("
                + "Lex.V3.Contracts.Source.Core.SourceObjectRef, System.String, " + C + "EuActForm, "
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuPredicateObservation>, " + N
                + "EuChannelObservation, " + N + "EuLanguageExpressionObservation, " + N
                + "EuFormatObservation, " + N + "EuContentClassObservation, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuRelationFamilyObservation>, "
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, " + N + "EuContentClassObservation, "
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, out " + N
                + "EuCellarObjectSnapshotRefusal&) -> " + N + "EuCellarObjectSnapshot",
            },
            ConstructionSurface.Of(typeof(EuCellarObjectSnapshot)).ToArray());
    }

    // ---- Happy path. ----

    [TestMethod]
    public void AMinimalSnapshotObservesCleanly()
    {
        var snapshot = Build(out var refusal);
        Assert.IsNotNull(snapshot);
        Assert.AreEqual(EuCellarObjectSnapshotRefusal.None, refusal);
        Assert.AreEqual(SeedA, snapshot.CanonicalWorkRoot);
        Assert.AreEqual(
            EuPredicateObservationState.NotObserved,
            snapshot.Predicate(EuCdmPredicate.ExpressionUsesLanguage).State);
        Assert.AreEqual(
            EuRelationAcquisitionState.Unacquired,
            snapshot.Relation(EuRelationFamily.Amends).Acquisition);
    }

    [TestMethod]
    public void AnHttpsSpelledWorkRootCanonicalizesToTheFrozenSeedForm()
    {
        var https = "https" + SeedA["http".Length..];
        var snapshot = Build(out var refusal, workRoot: https);
        Assert.IsNotNull(snapshot);
        Assert.AreEqual(SeedA, snapshot!.CanonicalWorkRoot);
    }

    // ---- Refusals, each driven on its own branch. ----

    [TestMethod]
    public void ANonCanonicalWorkRootRefuses()
    {
        Build(out var refusal, workRoot: SeedA + "?x=1");
        Assert.AreEqual(EuCellarObjectSnapshotRefusal.WorkRootNotCanonical, refusal);
    }

    [TestMethod]
    public void AWellFormedRootOutsideAppendixARefuses()
    {
        Build(out var refusal, workRoot: NotASeed);
        Assert.AreEqual(EuCellarObjectSnapshotRefusal.WorkRootOutsideAppendixAPack, refusal);
    }

    [TestMethod]
    public void AMissingPredicateObservationRefuses()
    {
        var predicates = AllPredicatesNotObserved().Skip(1).ToArray();
        Build(out var refusal, predicates: predicates);
        Assert.AreEqual(EuCellarObjectSnapshotRefusal.PredicateObservationMissing, refusal);
    }

    [TestMethod]
    public void ARepeatedPredicateObservationRefuses()
    {
        var predicates = AllPredicatesNotObserved().ToList();
        predicates.Add(predicates[0]);
        Build(out var refusal, predicates: predicates);
        Assert.AreEqual(EuCellarObjectSnapshotRefusal.PredicateObservationRepeated, refusal);
    }

    [TestMethod]
    public void AMissingRelationFamilyObservationRefuses()
    {
        var relations = AllReadFamiliesUnacquired().Skip(1).ToArray();
        Build(out var refusal, relations: relations);
        Assert.AreEqual(EuCellarObjectSnapshotRefusal.RelationFamilyObservationMissing, refusal);
    }

    [TestMethod]
    public void ARepeatedRelationFamilyObservationRefuses()
    {
        var relations = AllReadFamiliesUnacquired().ToList();
        relations.Add(relations[0]);
        Build(out var refusal, relations: relations);
        Assert.AreEqual(EuCellarObjectSnapshotRefusal.RelationFamilyObservationRepeated, refusal);
    }

    // ---- Leaf record validation (drives the constructors directly). ----

    [TestMethod]
    public void APredicateObservedPresentWithNoValuesThrows()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EuPredicateObservation(
            EuCdmPredicate.ResourceLegalIdCelex,
            EuPredicateObservationState.ObservedPresent,
            [],
            Artifact("x")));
    }

    [TestMethod]
    public void APredicateNotObservedWithValuesThrows()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EuPredicateObservation(
            EuCdmPredicate.ResourceLegalIdCelex,
            EuPredicateObservationState.NotObserved,
            ["32016R0679"],
            Artifact("x")));
    }

    [TestMethod]
    public void AnUnacquiredRelationFamilyWithEdgesThrows()
    {
        var edge = new EuRelationEdgeObservation(
            EuRelationFamily.Amends, EuRelationAuthority.PublisherAsserted, SeedA, Artifact("edge"));
        Assert.ThrowsExactly<ArgumentException>(() => new EuRelationFamilyObservation(
            EuRelationFamily.Amends, EuRelationAcquisitionState.Unacquired, [edge], null));
    }

    [TestMethod]
    public void ACompleteRelationFamilyWithNoCompletionEvidenceThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new EuRelationFamilyObservation(
            EuRelationFamily.Amends, EuRelationAcquisitionState.Complete, [], null));
    }

    [TestMethod]
    public void AnIncompleteRelationFamilyWithCompletionEvidenceThrows()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EuRelationFamilyObservation(
            EuRelationFamily.Amends, EuRelationAcquisitionState.Incomplete, [], Artifact("bad")));
    }

    [TestMethod]
    public void AnEdgeNamingTheWrongFamilyThrows()
    {
        var edge = new EuRelationEdgeObservation(
            EuRelationFamily.Corrects, EuRelationAuthority.PublisherAsserted, SeedA, Artifact("edge"));
        Assert.ThrowsExactly<ArgumentException>(() => new EuRelationFamilyObservation(
            EuRelationFamily.Amends,
            EuRelationAcquisitionState.Incomplete,
            [edge],
            null));
    }

    [TestMethod]
    public void ARelationEdgeMayNameATargetOutsideAppendixA()
    {
        // R7: a frontier target is identified-but-unheld evidence, never restricted to the pack.
        var edge = new EuRelationEdgeObservation(
            EuRelationFamily.Amends, EuRelationAuthority.PublisherAsserted, NotASeed, Artifact("edge"));
        Assert.AreEqual(NotASeed, edge.TargetWorkRoot);
    }

    [TestMethod]
    public void ARelationEdgeTargetIsStillCanonicalizedEvenWhenOutsideThePack()
    {
        var https = "https" + NotASeed["http".Length..];
        var edge = new EuRelationEdgeObservation(
            EuRelationFamily.Amends, EuRelationAuthority.PublisherAsserted, https, Artifact("edge"));
        Assert.AreEqual(NotASeed, edge.TargetWorkRoot);
    }

    [TestMethod]
    public void ANonCanonicalRelationEdgeTargetThrows()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EuRelationEdgeObservation(
            EuRelationFamily.Amends, EuRelationAuthority.PublisherAsserted, SeedA + "?x=1",
            Artifact("edge")));
    }
}
