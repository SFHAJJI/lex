using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Europe;
using System.Reflection;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuLocatedAmendmentProducerTests
{
    private const string Source = "http://publications.europa.eu/resource/cellar/00000000-0000-4000-8000-000000000011";
    private const string Held = "http://publications.europa.eu/resource/cellar/00000000-0000-4000-8000-000000000012";
    private const string Pending = "http://publications.europa.eu/resource/cellar/00000000-0000-4000-8000-000000000013";
    private const string Outside = "http://publications.europa.eu/resource/cellar/00000000-0000-4000-8000-000000000014";

    [TestMethod]
    public void ReopenedCorpusRecordsDecideAllThreeTargetBodyScopes()
    {
        var result = EuLocatedAmendmentProducer.Produce(
            [Observation(Held, 1), Observation(Pending, 2), Observation(Outside, 3)],
            Corpus(Source, Held, Pending));

        Assert.HasCount(3, result.Admitted);
        Assert.IsEmpty(result.Ambiguous);
        Assert.IsEmpty(result.Excluded);
        CollectionAssert.AreEqual(
            new[]
            {
                TargetBodyScope.BodyInScopeHeld,
                TargetBodyScope.BodyInScopeNotHeld,
                TargetBodyScope.BodyOutsideScope,
            },
            result.Admitted.Select(item => item.Axiom.Edge.Fact.TargetBodyScope).ToArray());
    }

    [TestMethod]
    public void EveryDeliveredObservationIsAdmittedAmbiguousOrExplicitlyExcluded()
    {
        var ambiguity = Observation([Held, Outside], 4);
        var sourceOutside = Observation(Held, 5, Outside);
        var malformed = Observation(Held, 6, properties: Properties(includeRole: false));

        var result = EuLocatedAmendmentProducer.Produce(
            [ambiguity, sourceOutside, malformed], Corpus(Source, Held));

        Assert.IsEmpty(result.Admitted);
        Assert.HasCount(1, result.Ambiguous);
        Assert.AreSame(ambiguity, result.Ambiguous[0].Observation);
        Assert.HasCount(2, result.Excluded);
        Assert.AreEqual(3, result.Admitted.Count + result.Ambiguous.Count + result.Excluded.Count);
        Assert.IsTrue(result.Excluded.Any(item =>
            item.Kind == EuLocatedAmendmentExclusionKind.SourceOutsideVerifiedCorpus));
        var qualifier = result.Excluded.Single(item =>
            item.Kind == EuLocatedAmendmentExclusionKind.ProjectionRefused);
        Assert.AreEqual(EuLocatedAmendmentAxiomProjectionRefusal.RequiredQualifierMissing,
            qualifier.ProjectionRefusal);
        Assert.AreEqual(EuAmendmentRelationVocabulary.Role2Uri, qualifier.Detail);
    }

    [TestMethod]
    public void PublisherEvidenceAloneMintsDisclosuresAndCoverageRemainsUnmeasured()
    {
        var ambiguity = Observation([Held, Outside], 7);

        var result = EuLocatedAmendmentProducer.Produce([ambiguity], Corpus(Source, Held));

        Assert.AreEqual("unmeasured", result.Coverage.State);
        Assert.AreEqual(
            "complete_textual_change_range_measurement",
            result.Coverage.UnmetPrerequisite);
        CollectionAssert.AreEqual(
            new[] { Held, Outside },
            result.Ambiguous.Single().CandidateTargetIris.ToArray());
        Assert.AreSame(ambiguity, result.Ambiguous.Single().Observation);
    }

    [TestMethod]
    public void CallersCannotConstructPartitionsCoverageOrAmbiguityCandidates()
    {
        var sealedTypes = new[]
        {
            typeof(EuLocatedAmendmentProduction),
            typeof(EuPublisherMarkedAmendmentAttribution),
            typeof(EuLocatedAmendmentAmbiguity),
            typeof(EuLocatedAmendmentExclusion),
            typeof(EuAmendmentAttributionCoverage),
        };

        foreach (var type in sealedTypes)
        {
            Assert.IsEmpty(
                type.GetConstructors(BindingFlags.Instance | BindingFlags.Public),
                $"{type.Name} exposes a public constructor.");
        }

        Assert.IsFalse(typeof(EuLocatedAmendmentAxiomProjection).IsPublic,
            "the corpus-dependent projection must remain an ingest-internal operation.");
    }

    private static EuLocatedAmendmentAxiomObservation Observation(
        string target,
        int ordinal,
        string source = Source,
        IReadOnlyList<EuLocatedAmendmentRawProperty>? properties = null) =>
        Observation([target], ordinal, source, properties);

    private static EuLocatedAmendmentAxiomObservation Observation(
        IReadOnlyList<string> targets,
        int ordinal,
        string source = Source,
        IReadOnlyList<EuLocatedAmendmentRawProperty>? properties = null)
    {
        var profile = EuObjectFactsDiscoveryPlan.Create()
            .CreateDeliveryProfile(EuObjectFactsQuerySet.LocatedAmendmentFacts);
        var profileRef = RepeatedEnumerationInterpretationProfileIdentity.Create(
            $"urn:uuid:00000000-0000-4000-8000-{ordinal:D12}", profile);
        return new EuLocatedAmendmentAxiomObservation(
            $"http://publications.europa.eu/.well-known/genid/located-amendment/{ordinal}",
            source,
            EuAmendmentRelationVocabulary.AmendsPredicateUri,
            targets,
            properties ?? Properties(),
            profileRef);
    }

    private static IReadOnlyList<EuLocatedAmendmentRawProperty> Properties(bool includeRole = true)
    {
        var values = new List<EuLocatedAmendmentRawProperty>
        {
            Property(EuAmendmentRelationVocabulary.ReferenceToModifiedLocationUri,
                "{AN|http://publications.europa.eu/resource/authority/fd_370/AN} 1"),
            Property(EuAmendmentRelationVocabulary.TypeOfLinkTargetUri, "MS"),
        };
        if (includeRole)
            values.Add(Property(EuAmendmentRelationVocabulary.Role2Uri,
                "{R|http://publications.europa.eu/resource/authority/fd_375/R}"));
        return values;
    }

    private static EuLocatedAmendmentRawProperty Property(string predicate, string value) =>
        new(predicate, RepeatedEnumerationRdfTerm.Literal(value, null, null));

    private static VerifiedCorpusRecordSet Corpus(params string[] iris)
    {
        var records = iris.Select((iri, ordinal) => Record(iri, ordinal, iri == Held)).ToArray();
        return new VerifiedCorpusRecordSet(new CorpusRecordSet(
            CorpusRecordSetSchemaIds.Set, ManifestRef(), RunRef(), records));
    }

    private static CorpusRecord Record(string iri, int ordinal, bool held) => new(
        CorpusRecordSchemaIds.Record,
        Object(iri),
        ordinal,
        ScopeDisposition.AcceptedSelected,
        ScopeDisposition.AcceptedSelected,
        ScopeDisposition.AcceptedSelected,
        ScopeDisposition.AcceptedSelected,
        held
            ? CorpusBodyRecord.Held(HeldReceipt())
            : CorpusBodyRecord.PendingAcquisition(CorpusBodyPendingAcquisitionReason.NotYetAcquired()),
        ManifestRef(),
        RunRef());

    private static SourceObjectRef Object(string iri)
    {
        var key = "eu-work:" + iri[^36..];
        return new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Cellar,
            new SourceRegistryMemberRef(
                new SourceArtifactRef("urn:uuid:10000000-0000-4000-8000-000000000001", new string('a', 64)),
                "eu_work"),
            iri,
            key,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(key))),
            new SourceArtifactRef("urn:uuid:10000000-0000-4000-8000-000000000002", new string('b', 64)),
            null);
    }

    private static SourceArtifactRef ManifestRef() =>
        new("urn:uuid:10000000-0000-4000-8000-000000000003", new string('c', 64));

    private static SourceArtifactRef RunRef() =>
        new("urn:uuid:10000000-0000-4000-8000-000000000004", new string('d', 64));

    private static DurableBlobWriteReceipt HeldReceipt()
    {
        var observedAt = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
        var blob = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef, new string('e', 64), 128, CustodyClass.NightlyFloor90d);
        var policy = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            blob,
            CustodyVerificationProfile.ImmutableObject1,
            Guid.Parse("10000000-0000-4000-8000-000000000005"),
            CustodyProtection.LockedTime,
            observedAt,
            observedAt.AddDays(91));
        return new DurableBlobWriteReceipt(CustodySchemaIds.DurableBlobWriteReceipt, blob, policy);
    }
}
