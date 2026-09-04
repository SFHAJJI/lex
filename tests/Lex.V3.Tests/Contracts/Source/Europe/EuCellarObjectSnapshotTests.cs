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

    // ---- Fold-in: Decision 64's "not observed" versus "observed absent" distinction is the whole
    // reason this enum has three members rather than two, and nothing before this fold-in ever
    // constructed the third one or a genuinely value-bearing ObservedPresent -- so the distinction
    // the type exists to make was unproven. Each state is driven here on its own, plus the blank-
    // value guard ObservedPresent's own values must satisfy. -----------------------------------------

    [TestMethod]
    public void AnObservedAbsentPredicateConstructsCleanlyWithNoValues()
    {
        // Asked, and the observation completed with nothing found: a real, complete, negative
        // observation, never confused with NotObserved's coverage gap.
        var observation = new EuPredicateObservation(
            EuCdmPredicate.ResourceLegalIdCelex,
            EuPredicateObservationState.ObservedAbsent,
            [],
            Artifact("x"));
        Assert.AreEqual(EuPredicateObservationState.ObservedAbsent, observation.State);
        Assert.IsEmpty(observation.Values);
    }

    [TestMethod]
    public void AGenuinelyValidObservedPresentPredicateCarriesItsValueThrough()
    {
        // The happy path Decision 64 actually names: asked, and the publisher supplied a value.
        // Every other test touching ObservedPresent before this fold-in drove only its refusals.
        var observation = new EuPredicateObservation(
            EuCdmPredicate.ResourceLegalIdCelex,
            EuPredicateObservationState.ObservedPresent,
            ["32016R0679"],
            Artifact("x"));
        Assert.AreEqual(EuPredicateObservationState.ObservedPresent, observation.State);
        CollectionAssert.AreEqual(new[] { "32016R0679" }, observation.Values.ToArray());
    }

    [TestMethod]
    public void AnObservedPresentPredicateWithABlankValueAmongRealOnesThrows()
    {
        // The blank-value guard is a second, separate check from the empty-values guard
        // APredicateObservedPresentWithNoValuesThrows already drives: a non-empty list that still
        // contains a blank entry must be refused too, not only a wholly empty one.
        Assert.ThrowsExactly<ArgumentException>(() => new EuPredicateObservation(
            EuCdmPredicate.ResourceLegalIdCelex,
            EuPredicateObservationState.ObservedPresent,
            ["32016R0679", "   "],
            Artifact("x")));
    }

    [TestMethod]
    public void ASnapshotCarriesAnObservedAbsentPredicateThroughItsAccessor()
    {
        // The distinction survives the whole snapshot, not only the leaf record: a predicate
        // observed-absent on one object reads back as ObservedAbsent through Predicate(), never
        // silently folded into NotObserved's own default the rest of this file's fixtures use.
        var predicates = AllPredicatesNotObserved()
            .Select(p => p.Predicate == EuCdmPredicate.ResourceLegalIdCelex
                ? new EuPredicateObservation(
                    p.Predicate, EuPredicateObservationState.ObservedAbsent, [], p.EvidenceRef)
                : p)
            .ToArray();

        var snapshot = Build(out var refusal, predicates: predicates);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(EuCellarObjectSnapshotRefusal.None, refusal);
        Assert.AreEqual(
            EuPredicateObservationState.ObservedAbsent,
            snapshot!.Predicate(EuCdmPredicate.ResourceLegalIdCelex).State);
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

    // ---- Fold-in for the D1-05 contracts refreeze (lex-event-20260903T232818026Z-be06bed3108f4128b6de31b5b008c518):
    // the seven observation records below carried no ConstructionSurface.Of pin anywhere in the tree,
    // the second half of fold-in five, even though the fold-in five packet said all seven were closed.
    // Each gets an exact Of pin over its own construction path plus an exact ProducersIn pin over its
    // external producers across Lex.V3.Contracts, print-actual-then-transcribe, so a new unreviewed
    // door into any of the seven raw observation types this file's own snapshot restricts predicate,
    // relation-edge, relation-family, channel, language-expression, format and content-class
    // observations to fails one of these tests rather than passing silently. ---------------------------

    [TestMethod]
    public void EuPredicateObservationHasExactlyOneConstructionPath()
    {
        const string C = "Lex.V3.Contracts.";
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuPredicateObservation::.ctor(" + N
                    + "EuPredicateObservation) -> " + N + "EuPredicateObservation",
                "constructor public instance " + N + "EuPredicateObservation::.ctor(" + C
                    + "EuCdmPredicate, " + N + "EuPredicateObservationState, "
                    + "System.Collections.Generic.IReadOnlyList<System.String>, "
                    + "Lex.V3.Contracts.Source.Core.SourceArtifactRef) -> " + N + "EuPredicateObservation",
                "method public instance " + N + "EuPredicateObservation::<Clone>$() -> " + N
                    + "EuPredicateObservation",
            },
            ConstructionSurface.Of(typeof(EuPredicateObservation)).ToArray());
    }

    [TestMethod]
    public void EuCellarObjectSnapshotAndEuCellarObjectDecodeAreTheOnlyRecognisedExternalProducersOfEuPredicateObservation()
    {
        const string C = "Lex.V3.Contracts.";
        var assembly = typeof(EuCellarObjectSnapshot).Assembly;
        CollectionAssert.AreEqual(
            new[]
            {
                // D1-05c-1's decode reads family P's own rows and either builds one
                // EuPredicateObservation per closed CDM predicate or refuses; the door for this is a
                // TryBuild* method (a genuine outcome, not a guaranteed value), so the observation
                // reaches callers through its own `out` parameter rather than a bare return.
                "by-ref-method private static " + N + "EuCellarObjectDecode::TryBuildPredicateObservation("
                    + C + "EuCdmPredicate, System.Collections.Generic.IReadOnlyList<" + N
                    + "EuCellarObjectDecode+ObjectFactRow>, Lex.V3.Contracts.Source.Core.SourceArtifactRef, "
                    + "out " + N + "EuPredicateObservation&) -> System.Boolean",
                "field private instance " + N + "EuCellarObjectSnapshot::<PredicateObservations>k__BackingField -> "
                    + "System.Collections.Generic.IReadOnlyList<" + N + "EuPredicateObservation>",
                "field private instance " + N + "EuCellarObjectSnapshot::_predicateIndex -> "
                    + "System.Collections.Generic.IReadOnlyDictionary<" + C + "EuCdmPredicate, " + N
                    + "EuPredicateObservation>",
                "method public instance " + N + "EuCellarObjectSnapshot::Predicate(" + C
                    + "EuCdmPredicate) -> " + N + "EuPredicateObservation",
                "property public instance " + N + "EuCellarObjectSnapshot::PredicateObservations() -> "
                    + "System.Collections.Generic.IReadOnlyList<" + N + "EuPredicateObservation>",
            },
            ConstructionSurface.ProducersIn(assembly, typeof(EuPredicateObservation), includeNonPublic: true)
                .ToArray(),
            "the exact set of external producers of EuPredicateObservation across Lex.V3.Contracts.");
    }

    [TestMethod]
    public void EuRelationEdgeObservationHasExactlyOneConstructionPath()
    {
        const string C = "Lex.V3.Contracts.";
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuRelationEdgeObservation::.ctor(" + N
                    + "EuRelationEdgeObservation) -> " + N + "EuRelationEdgeObservation",
                "constructor public instance " + N + "EuRelationEdgeObservation::.ctor(" + C
                    + "EuRelationFamily, " + C + "EuRelationAuthority, System.String, "
                    + "Lex.V3.Contracts.Source.Core.SourceArtifactRef) -> " + N + "EuRelationEdgeObservation",
                "method public instance " + N + "EuRelationEdgeObservation::<Clone>$() -> " + N
                    + "EuRelationEdgeObservation",
            },
            ConstructionSurface.Of(typeof(EuRelationEdgeObservation)).ToArray());
    }

    [TestMethod]
    public void EuRelationFamilyObservationAndEuCellarObjectDecodeAreTheOnlyRecognisedExternalProducersOfEuRelationEdgeObservation()
    {
        var assembly = typeof(EuCellarObjectSnapshot).Assembly;
        // D1-05c-1 retires D1-05b's BuildConsolidatedBasedOnEdge (the OntologyAuthorizedInverse
        // builder) along with its own pin here: EuCellarObjectDecode now constructs every edge from
        // inside TryBuildRelationFamilyObservation's own loop body, and that method's SIGNATURE never
        // names EuRelationEdgeObservation (only the family-observation type it hands out and the raw
        // row list it reads) - a real reduction in this decode's own recognised producer surface for
        // the edge type specifically, print-actual-then-transcribe, matching this file's own
        // documented limitation that ConstructionSurface reads signatures only.
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "EuRelationFamilyObservation::<Edges>k__BackingField -> "
                    + "System.Collections.Generic.IReadOnlyList<" + N + "EuRelationEdgeObservation>",
                "property public instance " + N + "EuRelationFamilyObservation::Edges() -> "
                    + "System.Collections.Generic.IReadOnlyList<" + N + "EuRelationEdgeObservation>",
            },
            ConstructionSurface.ProducersIn(assembly, typeof(EuRelationEdgeObservation), includeNonPublic: true)
                .ToArray(),
            "the exact set of external producers of EuRelationEdgeObservation across Lex.V3.Contracts.");
    }

    [TestMethod]
    public void EuRelationFamilyObservationHasExactlyOneConstructionPath()
    {
        const string C = "Lex.V3.Contracts.";
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuRelationFamilyObservation::.ctor(" + N
                    + "EuRelationFamilyObservation) -> " + N + "EuRelationFamilyObservation",
                "constructor public instance " + N + "EuRelationFamilyObservation::.ctor(" + C
                    + "EuRelationFamily, " + C + "EuRelationAcquisitionState, "
                    + "System.Collections.Generic.IReadOnlyList<" + N + "EuRelationEdgeObservation>, "
                    + "Lex.V3.Contracts.Source.Core.SourceArtifactRef) -> " + N + "EuRelationFamilyObservation",
                "method public instance " + N + "EuRelationFamilyObservation::<Clone>$() -> " + N
                    + "EuRelationFamilyObservation",
            },
            ConstructionSurface.Of(typeof(EuRelationFamilyObservation)).ToArray());
    }

    [TestMethod]
    public void EuCellarObjectSnapshotAndEuCellarObjectDecodeAreTheOnlyRecognisedExternalProducersOfEuRelationFamilyObservation()
    {
        const string C = "Lex.V3.Contracts.";
        var assembly = typeof(EuCellarObjectSnapshot).Assembly;
        CollectionAssert.AreEqual(
            new[]
            {
                // D1-05c-1's decode reads family P's own rows and either builds one
                // EuRelationFamilyObservation per read relation family or refuses.
                "by-ref-method private static " + N
                    + "EuCellarObjectDecode::TryBuildRelationFamilyObservation(" + C
                    + "EuRelationFamily, System.Collections.Generic.IReadOnlyList<" + N
                    + "EuCellarObjectDecode+ObjectFactRow>, Lex.V3.Contracts.Source.Core.SourceArtifactRef, "
                    + "out " + N + "EuRelationFamilyObservation&) -> System.Boolean",
                "field private instance " + N + "EuCellarObjectSnapshot::<RelationObservations>k__BackingField -> "
                    + "System.Collections.Generic.IReadOnlyList<" + N + "EuRelationFamilyObservation>",
                "field private instance " + N + "EuCellarObjectSnapshot::_relationIndex -> "
                    + "System.Collections.Generic.IReadOnlyDictionary<" + C + "EuRelationFamily, " + N
                    + "EuRelationFamilyObservation>",
                "method public instance " + N + "EuCellarObjectSnapshot::Relation(" + C
                    + "EuRelationFamily) -> " + N + "EuRelationFamilyObservation",
                "property public instance " + N + "EuCellarObjectSnapshot::RelationObservations() -> "
                    + "System.Collections.Generic.IReadOnlyList<" + N + "EuRelationFamilyObservation>",
            },
            ConstructionSurface.ProducersIn(assembly, typeof(EuRelationFamilyObservation), includeNonPublic: true)
                .ToArray(),
            "the exact set of external producers of EuRelationFamilyObservation across Lex.V3.Contracts.");
    }

    [TestMethod]
    public void EuChannelObservationHasExactlyOneConstructionPath()
    {
        const string C = "Lex.V3.Contracts.";
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuChannelObservation::.ctor(" + N
                    + "EuChannelObservation) -> " + N + "EuChannelObservation",
                "constructor public instance " + N + "EuChannelObservation::.ctor(" + C
                    + "EuChannel, System.String, System.String, "
                    + "Lex.V3.Contracts.Source.Core.SourceArtifactRef) -> " + N + "EuChannelObservation",
                "method public instance " + N + "EuChannelObservation::<Clone>$() -> " + N
                    + "EuChannelObservation",
            },
            ConstructionSurface.Of(typeof(EuChannelObservation)).ToArray());
    }

    [TestMethod]
    public void EuCellarObjectSnapshotAndEuCellarObjectDecodeAreTheOnlyRecognisedExternalProducersOfEuChannelObservation()
    {
        var assembly = typeof(EuCellarObjectSnapshot).Assembly;
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "EuCellarObjectSnapshot::<Channel>k__BackingField -> " + N
                    + "EuChannelObservation",
                // D1-05b's decode always observes the Cellar SPARQL endpoint channel; BuildChannel is
                // a named method (rather than an inline expression) precisely so this call site has a
                // return type this tool recognises as a producer.
                "method private static " + N + "EuCellarObjectDecode::BuildChannel("
                    + "Lex.V3.Contracts.Source.Core.SourceArtifactRef) -> " + N
                    + "EuChannelObservation",
                "property public instance " + N + "EuCellarObjectSnapshot::Channel() -> " + N
                    + "EuChannelObservation",
            },
            ConstructionSurface.ProducersIn(assembly, typeof(EuChannelObservation), includeNonPublic: true)
                .ToArray(),
            "the exact set of external producers of EuChannelObservation across Lex.V3.Contracts.");
    }

    [TestMethod]
    public void EuLanguageExpressionObservationHasExactlyOneConstructionPath()
    {
        const string C = "Lex.V3.Contracts.";
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuLanguageExpressionObservation::.ctor(" + N
                    + "EuLanguageExpressionObservation) -> " + N + "EuLanguageExpressionObservation",
                "constructor public instance " + N + "EuLanguageExpressionObservation::.ctor(" + C
                    + "EuOfficialLanguage, " + N + "EuExpressionObservationState, System.String, "
                    + "System.String, Lex.V3.Contracts.Source.Core.SourceArtifactRef) -> " + N
                    + "EuLanguageExpressionObservation",
                "method public instance " + N + "EuLanguageExpressionObservation::<Clone>$() -> " + N
                    + "EuLanguageExpressionObservation",
            },
            ConstructionSurface.Of(typeof(EuLanguageExpressionObservation)).ToArray());
    }

    [TestMethod]
    public void EuCellarObjectSnapshotAndEuCellarObjectDecodeAreTheOnlyRecognisedExternalProducersOfEuLanguageExpressionObservation()
    {
        var assembly = typeof(EuCellarObjectSnapshot).Assembly;
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "EuCellarObjectSnapshot::<Language>k__BackingField -> " + N
                    + "EuLanguageExpressionObservation",
                // D1-05c-1's decode fills the language observation from family X's own rows (queue
                // item 18's own line: "the language observation filled from X").
                "method private static " + N + "EuCellarObjectDecode::BuildLanguageObservation("
                    + "System.Collections.Generic.IReadOnlyList<" + N
                    + "EuCellarObjectDecode+ExpressionFactRow>, "
                    + "Lex.V3.Contracts.Source.Core.SourceArtifactRef) -> " + N
                    + "EuLanguageExpressionObservation",
                "property public instance " + N + "EuCellarObjectSnapshot::Language() -> " + N
                    + "EuLanguageExpressionObservation",
            },
            ConstructionSurface.ProducersIn(assembly, typeof(EuLanguageExpressionObservation), includeNonPublic: true)
                .ToArray(),
            "the exact set of external producers of EuLanguageExpressionObservation across Lex.V3.Contracts.");
    }

    /// <summary>
    /// The closed language-expression state vocabulary, pinned member by member.
    /// </summary>
    /// <remarks>
    /// Added by D1-05d, which REMOVED a public member from this enum
    /// (<c>ExpressionObservedBodyHeld</c>, see
    /// <see cref="EuExpressionObservationState.ExpressionObservedBodyCandidate"/>'s own remarks for
    /// why removed rather than reserved) and found that no gate noticed, because this vocabulary had
    /// no construction-surface pin at all. Every marker below is transcribed from
    /// ConstructionSurface.Of's own printed output, never hand-derived. The retired member's absence
    /// is what this pin exists to hold: re-adding a member that claims a body is held would fail
    /// here first.
    /// </remarks>
    [TestMethod]
    public void TheExpressionObservationStateVocabularyHasExactlyThreeMembersAndClaimsNoHeldBody()
    {
        const string T = N + "EuExpressionObservationState";
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + T + "::ExpressionObservedBodyCandidate -> " + T,
                "field public static " + T + "::ExpressionObservedBodyNotHeld -> " + T,
                "field public static " + T + "::NotObserved -> " + T,
            },
            ConstructionSurface.Of(typeof(EuExpressionObservationState)).ToArray());

        // Said as a property rather than only as a transcribed list, so the intent survives a future
        // re-transcription: no member of this decode-time vocabulary may assert that a body is held.
        // That fact has exactly one home, CorpusBodyRecordKind, decided after acquisition.
        foreach (var name in Enum.GetNames<EuExpressionObservationState>())
        {
            Assert.IsFalse(
                name.EndsWith("BodyHeld", StringComparison.Ordinal),
                $"{name} claims a held body on a decode-time observation taken before any fetch.");
        }
    }

    [TestMethod]
    public void EuFormatObservationHasExactlyOneConstructionPath()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuFormatObservation::.ctor(" + N
                    + "EuFormatObservation) -> " + N + "EuFormatObservation",
                "constructor public instance " + N + "EuFormatObservation::.ctor(" + N
                    + "EuManifestationFormat, " + N + "EuFormatBodyAdmission, System.String, "
                    + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, "
                    + "System.Collections.Generic.IReadOnlyList<" + N + "EuManifestationFormat>) -> "
                    + N + "EuFormatObservation",
                "method public instance " + N + "EuFormatObservation::<Clone>$() -> " + N
                    + "EuFormatObservation",
            },
            ConstructionSurface.Of(typeof(EuFormatObservation)).ToArray());
    }

    [TestMethod]
    public void EuCellarObjectSnapshotIsTheOnlyRecognisedExternalProducerOfEuFormatObservation()
    {
        var assembly = typeof(EuCellarObjectSnapshot).Assembly;
        CollectionAssert.AreEqual(
            new[]
            {
                // D1-05d: family M's listing decode is now a real external producer -- it is the
                // one door that mints a format observation from the office's own listing.
                "field private instance " + N + "EuCellarObjectSnapshot::<Format>k__BackingField -> " + N
                    + "EuFormatObservation",
                "method public static " + N + "EuManifestationListingDecode::Observe("
                    + "System.Collections.Generic.IReadOnlyCollection<" + N + "EuManifestationFormat>, "
                    + "Lex.V3.Contracts.Source.Core.SourceArtifactRef) -> " + N + "EuFormatObservation",
                // The unreadable-listing door: a second real producer, added when an unadmitted
                // manifestation type stopped refusing the whole decode and became one Work's own
                // typed quarantine.
                "method public static " + N + "EuManifestationListingDecode::ObserveUnreadableListing("
                    + "System.Collections.Generic.IReadOnlyCollection<" + N + "EuManifestationFormat>, "
                    + "System.String, Lex.V3.Contracts.Source.Core.SourceArtifactRef) -> " + N
                    + "EuFormatObservation",
                "method public static " + N + "EuManifestationListingDecode::TryDecode("
                    + "System.Collections.Generic.IReadOnlySet<System.String>, "
                    + "System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Source.Core.RepeatedEnumerationRow>, "
                    + "Lex.V3.Contracts.Source.Core.RepeatedEnumerationInterpretationProfile, "
                    + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, out " + N
                    + "EuManifestationListingRefusal&, out System.String&, out System.String&) -> "
                    + "System.Collections.Generic.IReadOnlyDictionary<System.String, " + N
                    + "EuFormatObservation>",
                "property public instance " + N + "EuCellarObjectSnapshot::Format() -> " + N
                    + "EuFormatObservation",
            },
            ConstructionSurface.ProducersIn(assembly, typeof(EuFormatObservation), includeNonPublic: true)
                .ToArray(),
            "the exact set of external producers of EuFormatObservation across Lex.V3.Contracts.");
    }

    [TestMethod]
    public void EuContentClassObservationHasExactlyOneConstructionPath()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuContentClassObservation::.ctor(" + N
                    + "EuContentClassObservation) -> " + N + "EuContentClassObservation",
                "constructor public instance " + N + "EuContentClassObservation::.ctor(" + N
                    + "EuContentClass, Lex.V3.Contracts.Source.Core.SourceArtifactRef) -> " + N
                    + "EuContentClassObservation",
                "method public instance " + N + "EuContentClassObservation::<Clone>$() -> " + N
                    + "EuContentClassObservation",
            },
            ConstructionSurface.Of(typeof(EuContentClassObservation)).ToArray());
    }

    [TestMethod]
    public void EuCellarObjectSnapshotIsTheOnlyRecognisedExternalProducerOfEuContentClassObservation()
    {
        // Two properties, not one: EuContentClassObservation is the shared leaf type for both the
        // rights axis (Rights) and the supporting-document axis (Supporting), and both are real,
        // independent doors onto it.
        var assembly = typeof(EuCellarObjectSnapshot).Assembly;
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N + "EuCellarObjectSnapshot::<Rights>k__BackingField -> " + N
                    + "EuContentClassObservation",
                "field private instance " + N + "EuCellarObjectSnapshot::<Supporting>k__BackingField -> " + N
                    + "EuContentClassObservation",
                "property public instance " + N + "EuCellarObjectSnapshot::Rights() -> " + N
                    + "EuContentClassObservation",
                "property public instance " + N + "EuCellarObjectSnapshot::Supporting() -> " + N
                    + "EuContentClassObservation",
            },
            ConstructionSurface.ProducersIn(assembly, typeof(EuContentClassObservation), includeNonPublic: true)
                .ToArray(),
            "the exact set of external producers of EuContentClassObservation across Lex.V3.Contracts.");
    }

    // ---- Fold-in for the D1-05b decode refreeze (lex-event-20260904T025508487Z-0d433eb3f5254b6188c05ab22e962acd):
    // this type's own Of pin (TheSnapshotHasExactlyOneConstructionPath above) had no matching
    // ProducersIn pin, and EuCellarObjectDecode.TryDecode is now a real external producer of
    // EuCellarObjectSnapshot (it calls TryObserve directly). Print-actual-then-transcribe, the same
    // technique the seven observation-record pins above already use. ------------------------------

    [TestMethod]
    public void EuCellarObjectDecodeIsTheOnlyRecognisedExternalProducerOfEuCellarObjectSnapshot()
    {
        const string C = "Lex.V3.Contracts.";
        const string RowList =
            "System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Source.Core.RepeatedEnumerationRow>, "
            + "Lex.V3.Contracts.Source.Core.RepeatedEnumerationInterpretationProfile, ";
        var assembly = typeof(EuCellarObjectSnapshot).Assembly;
        CollectionAssert.AreEqual(
            new[]
            {
                // TryDecode's own per-object helper: a real, separate door, since it is the method
                // that actually calls EuCellarObjectSnapshot.TryObserve.
                "method private static " + N + "EuCellarObjectDecode::BuildOneObject(System.String, "
                    + "System.Boolean, System.String, "
                    + "System.Collections.Generic.IReadOnlyList<" + N + "EuCellarObjectDecode+ObjectFactRow>, "
                    + "System.Collections.Generic.IReadOnlyList<" + N + "EuCellarObjectDecode+ExpressionFactRow>, "
                    + N + "EuFormatObservation, "
                    + C + "EuActForm, Lex.V3.Contracts.Source.Core.SourceArtifactRef, out " + N
                    + "EuCellarObjectDecodeRefusal&, out System.String&, out " + N
                    + "EuCellarObjectSnapshotRefusal&) -> " + N + "EuCellarObjectSnapshot",
                "method public static " + N + "EuCellarObjectDecode::TryDecode(System.String, "
                    + RowList + RowList + RowList + RowList
                    + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, " + C + "EuActForm, "
                    + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, out " + N
                    + "EuCellarObjectDecodeRefusal&, out System.String&, out " + N
                    + "EuCellarObjectSnapshotRefusal&, out " + N + "EuManifestationListingRefusal&) -> "
                    + "System.Collections.Generic.IReadOnlyList<" + N + "EuCellarObjectSnapshot>",
            },
            ConstructionSurface.ProducersIn(assembly, typeof(EuCellarObjectSnapshot), includeNonPublic: true)
                .ToArray(),
            "the exact set of external producers of EuCellarObjectSnapshot across Lex.V3.Contracts.");
    }
}
