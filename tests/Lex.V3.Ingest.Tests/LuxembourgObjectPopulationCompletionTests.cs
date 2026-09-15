using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The Luxembourg run's object population, closed against the subjects the run derived before any
/// manifest existed. Every hostile input below is built from a real delivered run and changes
/// exactly one thing, so a refusal names the rule it broke rather than one of several differences.
/// </summary>
[TestClass]
public sealed class LuxembourgObjectPopulationCompletionTests
{
    [TestMethod]
    public async Task TheClosedPopulationIsEveryObservedSubjectOfARealRun()
    {
        var run = await LuxembourgQueryExecutionAdapterTests.RunTwoSubjectDeliveredForPopulationAsync();
        Assert.IsNull(run.Refusal, $"code={run.Refusal?.Code} detail={run.Refusal?.Detail}");

        var population = LuxembourgObjectPopulationCompletion.TryClose(run, out var refusal, out var detail);

        Assert.IsNotNull(population, $"{refusal}: {detail}");
        Assert.AreEqual(LuxembourgObjectPopulationRefusal.None, refusal);
        Assert.AreEqual(2, population!.ObjectCount);

        // The subjects the census delivered and the publisher URIs the reopened corpus/6 set carries
        // are the same set. These two values reached here down different roads -- one from the
        // census family's own rows, the other through resolve, reduce, write and reopen -- so their
        // agreement is the population claim, not a restatement of one value.
        CollectionAssert.AreEquivalent(
            LuxembourgQueryExecutionAdapterTests.PopulationHarnessSubjects,
            population.Members.Select(static member => member.ObjectRef.PublisherUri).ToArray());
    }

    [TestMethod]
    public async Task ARefusedRunHasNoPopulationToClose()
    {
        var refused = LuxembourgQueryExecutionResult.Refused(
            (await LuxembourgQueryExecutionAdapterTests.RunTwoSubjectDeliveredForPopulationAsync()).Topology,
            [],
            [],
            new LuxembourgQueryExecutionRefusalDetail(
                LuxembourgQueryExecutionRefusal.PopulationLedgerNotCompleted,
                null,
                "refused for this test"));

        var population = LuxembourgObjectPopulationCompletion.TryClose(
            refused, out var refusal, out var detail);

        Assert.IsNull(population);
        Assert.AreEqual(LuxembourgObjectPopulationRefusal.RunNotComplete, refusal);
        StringAssert.Contains(detail, "the run refused");
    }

    [TestMethod]
    public async Task ARecordWhoseSubjectTheRunNeverObservedRefuses()
    {
        var run = await LuxembourgQueryExecutionAdapterTests.RunTwoSubjectDeliveredForPopulationAsync();

        // The population is untouched; the run's own derived subject list loses one entry. A closer
        // that trusted the record set to be its own expectation would see nothing wrong here.
        var narrowed = Rebuild(
            run, subjects: [LuxembourgQueryExecutionAdapterTests.PopulationHarnessSubjects[0]]);

        var population = LuxembourgObjectPopulationCompletion.TryClose(
            narrowed, out var refusal, out var detail);

        Assert.IsNull(population);
        Assert.AreEqual(LuxembourgObjectPopulationRefusal.MemberIsNotAnObservedSubject, refusal);
        StringAssert.Contains(
            detail, LuxembourgQueryExecutionAdapterTests.PopulationHarnessSubjects[1]);
    }

    [TestMethod]
    public async Task AShortenedRecordSetLeavesAnObservedSubjectWithNoMember()
    {
        var run = await LuxembourgQueryExecutionAdapterTests.RunTwoSubjectDeliveredForPopulationAsync();

        // Still a valid, verified set: strictly ordered, no object named twice, canonicalizing to
        // its own digest. What it is not is this run's whole population.
        var shortened = Rebuild(run, corpusRecordSet: WithoutItsFirstRecord(run.CorpusRecordSet!));

        var population = LuxembourgObjectPopulationCompletion.TryClose(
            shortened, out var refusal, out var detail);

        Assert.IsNull(population);
        Assert.AreEqual(LuxembourgObjectPopulationRefusal.ObservedSubjectHasNoMember, refusal);
        Assert.IsNotNull(detail);
    }

    [TestMethod]
    public async Task TwoRecordsClaimingOneSubjectRefuse()
    {
        var run = await LuxembourgQueryExecutionAdapterTests.RunTwoSubjectDeliveredForPopulationAsync();

        // The set is ordered by object ordinal, which is not the census's delivery order, so the
        // duplicated subject is read from the record itself rather than assumed to be the first
        // harness subject.
        var duplicatedSubject = run.CorpusRecordSet!.Set.Records[0].ObjectRef.PublisherUri;

        // The set's own constructor refuses two records at one ordinal and two records naming one
        // exact object reference. Neither refuses two DIFFERENT object references carrying the same
        // publisher URI, which is how one subject enters a population twice.
        var doubled = Rebuild(run, corpusRecordSet: WithItsFirstSubjectClaimedTwice(run.CorpusRecordSet!));

        var population = LuxembourgObjectPopulationCompletion.TryClose(
            doubled, out var refusal, out var detail);

        Assert.IsNull(population);
        Assert.AreEqual(LuxembourgObjectPopulationRefusal.SubjectClaimedTwice, refusal);
        StringAssert.Contains(detail, duplicatedSubject);
    }

    [TestMethod]
    public async Task ADerivedSubjectListNamingOneSubjectTwiceRefuses()
    {
        var run = await LuxembourgQueryExecutionAdapterTests.RunTwoSubjectDeliveredForPopulationAsync();
        var subject = LuxembourgQueryExecutionAdapterTests.PopulationHarnessSubjects[0];

        var duplicated = Rebuild(run, subjects: [subject, subject]);

        var population = LuxembourgObjectPopulationCompletion.TryClose(
            duplicated, out var refusal, out var detail);

        Assert.IsNull(population);
        Assert.AreEqual(LuxembourgObjectPopulationRefusal.ObservedSubjectDeliveredTwice, refusal);
        StringAssert.Contains(detail, subject);
    }

    [TestMethod]
    public async Task AnAcquisitionOutcomeOutsideThePopulationRefuses()
    {
        var run = await LuxembourgQueryExecutionAdapterTests.RunTwoSubjectDeliveredForPopulationAsync();
        var outcomes = run.DocumentAcquisitionOutcomesByOrdinal!.ToDictionary(
            static entry => entry.Key, static entry => entry.Value);
        outcomes[4096] = CorpusAcquisitionOutcome.Refused(
            CorpusAcquisitionRefusalReason.RobotsDisallowed);

        var population = LuxembourgObjectPopulationCompletion.TryClose(
            Rebuild(run, outcomes: outcomes), out var refusal, out var detail);

        Assert.IsNull(population);
        Assert.AreEqual(LuxembourgObjectPopulationRefusal.OutcomeOutsidePopulation, refusal);
        StringAssert.Contains(detail, "4096");
    }

    // ---- Fixtures. ----

    /// <summary>
    /// The same set less its first record, re-canonicalized and re-verified so it arrives through
    /// <see cref="VerifiedCorpusRecordSet.ParseAndVerify"/> exactly as a real one would. Dropping the
    /// FIRST record keeps the strict ordinal ordering the set's own constructor requires, so nothing
    /// about this input is invalid except its size.
    /// </summary>
    private static VerifiedCorpusRecordSet WithoutItsFirstRecord(VerifiedCorpusRecordSet original) =>
        Reverify(original, original.Set.Records.Skip(1).ToList());

    /// <summary>
    /// The same set plus one more record for the first record's own publisher URI, distinct as an
    /// object reference only by its authority, appended past the last ordinal so the set's strict
    /// ordering still holds. Every invariant the set itself enforces is satisfied; the population
    /// rule this breaks is the one only a closer can see.
    /// </summary>
    private static VerifiedCorpusRecordSet WithItsFirstSubjectClaimedTwice(
        VerifiedCorpusRecordSet original)
    {
        var first = original.Set.Records[0];
        var source = first.ObjectRef;
        var twin = new CorpusRecord(
            first.Schema,
            new SourceObjectRef(
                source.Schema,
                source.Authority == SourceAuthority.Jolux ? SourceAuthority.Cellar : SourceAuthority.Jolux,
                source.EntityKind,
                source.PublisherUri,
                source.CanonicalKey,
                source.CanonicalKeySha256,
                source.IdentityProfileRef,
                source.ParentKeyRef),
            original.Set.Records[^1].ObjectOrdinal + 1,
            first.RecordDisposition,
            first.BodyDisposition,
            first.RelationDisposition,
            first.SupportingDocumentDisposition,
            first.Body,
            first.ManifestRef,
            first.RunIdentity);
        return Reverify(original, [.. original.Set.Records, twin]);
    }

    private static VerifiedCorpusRecordSet Reverify(
        VerifiedCorpusRecordSet original, IReadOnlyList<CorpusRecord> records)
    {
        var set = original.Set;
        var rebuilt = new CorpusRecordSet(set.Schema, set.ManifestRef, set.RunIdentity, records);
        using var canonical = new MemoryStream();
        var sha256 = CorpusRecordSetCanonicalWriter.Write(canonical, rebuilt);
        return VerifiedCorpusRecordSet.ParseAndVerify(
            new SourceArtifactRef(set.RunIdentity.ResourceId, sha256), canonical.ToArray());
    }

    private static LuxembourgQueryExecutionResult Rebuild(
        LuxembourgQueryExecutionResult run,
        IReadOnlyList<string>? subjects = null,
        IReadOnlyDictionary<int, CorpusAcquisitionOutcome>? outcomes = null,
        VerifiedCorpusRecordSet? corpusRecordSet = null) =>
        LuxembourgQueryExecutionResult.Delivered(
            run.Topology,
            run.FamilyOutcomes,
            run.RelationFamilyAcquisitions,
            run.ResolvedRelations,
            run.LocalInboundRelations,
            run.TypedAssertions,
            subjects ?? run.ResourceObservationSubjects,
            run.ResourceObservationExclusions,
            run.ScopeManifestReceipt!,
            run.ScopeManifestCanonicalSha256!,
            outcomes ?? run.DocumentAcquisitionOutcomesByOrdinal!,
            run.CorpusRecordSetRef!,
            corpusRecordSet ?? run.CorpusRecordSet!,
            run.GazetteBodySetsByOrdinal!,
            run.GazetteListingFetchRefusalsByOrdinal!,
            run.GazetteListingsWithContradictoryLegalValueByOrdinal!,
            run.PopulationLedger!);
}
