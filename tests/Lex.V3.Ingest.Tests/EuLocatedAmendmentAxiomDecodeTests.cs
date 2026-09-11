using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuLocatedAmendmentAxiomDecodeTests
{
    private const string Work = "http://publications.europa.eu/resource/cellar/source-work";
    private const string OtherSource = "http://publications.europa.eu/resource/cellar/other-source-work";
    private const string Target = "http://publications.europa.eu/resource/cellar/target-work";
    private const string OtherTarget = "http://publications.europa.eu/resource/cellar/other-target-work";
    private const string Axiom = "http://publications.europa.eu/.well-known/genid/located-amendment/1";
    private const string Unknown = EuAmendmentRelationVocabulary.AnnotationNamespace + "publisher_extension";

    private static readonly RepeatedEnumerationInterpretationProfile Profile =
        EuObjectFactsDiscoveryPlan.Create().CreateDeliveryProfile(EuObjectFactsQuerySet.LocatedAmendmentFacts);
    private static readonly SourceArtifactRef Delivery =
        RepeatedEnumerationInterpretationProfileIdentity.Create(
            "urn:uuid:00000000-0000-4000-8000-00000000a801", Profile);

    [TestMethod]
    public void AProofBoundAxiomRetainsEveryDeliveredPropertyWithoutMintingBodyScope()
    {
        var rows = WellFormed();
        rows.Add(Row(Unknown, RepeatedEnumerationRdfTerm.Literal("publisher bytes", null, "fr")));

        var observations = EuLocatedAmendmentAxiomDecode.TryDecode(
            rows, Profile, Delivery, out var refusal, out var offendingValue);

        Assert.AreEqual(EuLocatedAmendmentAxiomDecodeRefusal.None, refusal);
        Assert.IsNull(offendingValue);
        Assert.IsNotNull(observations);
        Assert.HasCount(1, observations);
        var observation = observations[0];
        Assert.AreEqual(Axiom, observation.AxiomIri);
        CollectionAssert.AreEqual(new[] { Work }, observation.AnnotatedSourceIris.ToArray());
        Assert.AreEqual(EuAmendmentRelationVocabulary.AmendsPredicateUri, observation.AnnotatedPropertyIri);
        CollectionAssert.AreEqual(new[] { Target }, observation.AnnotatedTargetIris.ToArray());
        Assert.AreSame(Delivery, observation.InterpretationProfileRefs.Single());
        Assert.HasCount(rows.Count, observation.RawProperties);
        var retained = observation.RawProperties.Single(property => property.PredicateIri == Unknown);
        Assert.AreEqual("publisher bytes", retained.Value.Value);
        Assert.AreEqual("fr", retained.Value.Language);
        Assert.IsFalse(observation.IsPublisherSourceAmbiguous);
        Assert.IsFalse(
            observation.GetType().GetProperties().Any(property => property.PropertyType == typeof(TargetBodyScope)),
            "the pre-corpus decoder must not mint the final body-scope claim");
    }

    [TestMethod]
    public void MultiplePublisherTargetsAreRetainedButDoNotClaimInstrumentAmbiguity()
    {
        var rows = WellFormed();
        rows.Add(Row(
            EuObjectFactsDiscoveryPlan.AnnotatedTargetPredicateIri,
            RepeatedEnumerationRdfTerm.Iri(OtherTarget)));

        var observations = Decode(rows, out var refusal);

        Assert.AreEqual(EuLocatedAmendmentAxiomDecodeRefusal.None, refusal);
        Assert.IsNotNull(observations);
        Assert.HasCount(1, observations);
        var observation = observations[0];
        Assert.IsFalse(observation.IsPublisherSourceAmbiguous);
        CollectionAssert.AreEqual(
            new[] { OtherTarget, Target }.Order(StringComparer.Ordinal).ToArray(),
            observation.AnnotatedTargetIris.ToArray());
        Assert.AreEqual(2, observation.RawProperties.Count(property =>
            property.PredicateIri == EuObjectFactsDiscoveryPlan.AnnotatedTargetPredicateIri));
    }

    [TestMethod]
    public void MultiplePublisherSourcesBecomeInstrumentAmbiguityAndPreserveEveryRawRow()
    {
        var rows = WellFormed();
        rows.Add(Row(
            EuObjectFactsDiscoveryPlan.AnnotatedSourcePredicateIri,
            RepeatedEnumerationRdfTerm.Iri(OtherSource)));

        var observations = Decode(rows, out var refusal, out var offendingValue);

        Assert.AreEqual(EuLocatedAmendmentAxiomDecodeRefusal.None, refusal);
        Assert.IsNull(offendingValue);
        Assert.IsNotNull(observations);
        var observation = observations.Single();
        Assert.IsTrue(observation.IsPublisherSourceAmbiguous);
        CollectionAssert.AreEqual(
            new[] { OtherSource, Work }.Order(StringComparer.Ordinal).ToArray(),
            observation.AnnotatedSourceIris.ToArray());
        Assert.AreEqual(2, observation.RawProperties.Count(property =>
            property.PredicateIri == EuObjectFactsDiscoveryPlan.AnnotatedSourcePredicateIri));
    }

    [TestMethod]
    public void MalformedOrUnadmittedRowsReachTypedRefusalRatherThanDisappearing()
    {
        var wrongProperty = WellFormed();
        Replace(wrongProperty, EuObjectFactsDiscoveryPlan.AnnotatedPropertyPredicateIri,
            RepeatedEnumerationRdfTerm.Iri(EuDateQualifierVocabulary.DeadlinePredicateUri));

        var targetLiteral = WellFormed();
        Replace(targetLiteral, EuObjectFactsDiscoveryPlan.AnnotatedTargetPredicateIri,
            RepeatedEnumerationRdfTerm.Literal(Target, null, null));

        var blankAxiom = WellFormed();
        blankAxiom.Add(Row(
            RepeatedEnumerationRdfTerm.BlankNode("unaddressable"),
            RepeatedEnumerationRdfTerm.Iri(Unknown),
            RepeatedEnumerationRdfTerm.Literal("retained", null, null)));

        var cases = new[]
        {
            (Rows: wrongProperty, Expected: EuLocatedAmendmentAxiomDecodeRefusal.AnnotatedPropertyMissingOrNotAdmitted),
            (Rows: targetLiteral, Expected: EuLocatedAmendmentAxiomDecodeRefusal.AnnotatedTargetMissingOrNotAnIri),
            (Rows: blankAxiom, Expected: EuLocatedAmendmentAxiomDecodeRefusal.AxiomNodeNotAnIri),
        };

        foreach (var item in cases)
        {
            var observations = Decode(item.Rows, out var refusal);
            Assert.IsNull(observations, item.Expected.ToString());
            Assert.AreEqual(item.Expected, refusal);
        }
    }

    [TestMethod]
    public void ACompletedEmptyFamilyRemainsAValidExplicitAbsence()
    {
        var observations = Decode([AbsenceRow()], out var refusal);

        Assert.AreEqual(EuLocatedAmendmentAxiomDecodeRefusal.None, refusal);
        Assert.IsNotNull(observations);
        Assert.IsEmpty(observations);
    }

    [TestMethod]
    public void AnAbsentParentAndAPresentParentCanShareOneBatch()
    {
        var observations = Decode([AbsenceRow(), .. WellFormed()], out var refusal);

        Assert.AreEqual(EuLocatedAmendmentAxiomDecodeRefusal.None, refusal);
        Assert.IsNotNull(observations);
        Assert.HasCount(1, observations);
    }

    [TestMethod]
    public void CallersCannotMintRawPropertiesOrPublisherTargetCandidates()
    {
        Assert.IsFalse(typeof(EuLocatedAmendmentAxiomDecode).IsPublic,
            "naked rows and a self-hashed profile must not be a public publisher-evidence door");
        Assert.IsEmpty(typeof(EuLocatedAmendmentRawProperty).GetConstructors());
        Assert.IsEmpty(typeof(EuLocatedAmendmentAxiomObservation).GetConstructors());

        var publicFactories = typeof(EuLocatedAmendmentAxiomObservation)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.IsEmpty(publicFactories, "publisher-evidenced ambiguity must come only from decoded delivery rows");
    }

    [TestMethod]
    public void MissingAbsenceEvidenceAndAParentSourceMismatchBothRefuse()
    {
        var empty = Decode([], out var emptyRefusal);
        Assert.IsNull(empty);
        Assert.AreEqual(EuLocatedAmendmentAxiomDecodeRefusal.DeliveryCarriesNoRows, emptyRefusal);

        var wrongParent = WellFormed();
        Replace(wrongParent, EuObjectFactsDiscoveryPlan.AnnotatedSourcePredicateIri,
            RepeatedEnumerationRdfTerm.Iri(OtherTarget));
        var mismatch = Decode(wrongParent, out var mismatchRefusal);
        Assert.IsNull(mismatch);
        Assert.AreEqual(
            EuLocatedAmendmentAxiomDecodeRefusal.AnnotatedSourceDisagreesWithSelectedParent,
            mismatchRefusal);
    }

    [TestMethod]
    public void AnotherFamilyProfileOrAnUnboundProfileReferenceCannotMintLocatedEvidence()
    {
        var dateProfile = EuObjectFactsDiscoveryPlan.Create()
            .CreateDeliveryProfile(EuObjectFactsQuerySet.ReifiedAxiomFacts);
        var dateProfileRef = RepeatedEnumerationInterpretationProfileIdentity.Create(
            Delivery.ResourceId, dateProfile);

        var wrongFamily = EuLocatedAmendmentAxiomDecode.TryDecode(
            WellFormed(), dateProfile, dateProfileRef, out var familyRefusal, out _);
        var unboundReference = EuLocatedAmendmentAxiomDecode.TryDecode(
            WellFormed(), Profile,
            new SourceArtifactRef(Delivery.ResourceId, new string('a', 64)),
            out var referenceRefusal, out _);

        Assert.IsNull(wrongFamily);
        Assert.AreEqual(
            EuLocatedAmendmentAxiomDecodeRefusal.InterpretationProfileNotLocatedAmendmentFacts,
            familyRefusal);
        Assert.IsNull(unboundReference);
        Assert.AreEqual(
            EuLocatedAmendmentAxiomDecodeRefusal.InterpretationProfileDoesNotBindReference,
            referenceRefusal);
    }

    [TestMethod]
    public void EveryDeclaredRefusalIsReachableFromADeliveredShape()
    {
        var markerConflict = WellFormed();
        markerConflict[0] = RewriteTerm(
            markerConflict[0], 4, RepeatedEnumerationRdfTerm.Literal("unbound", null, null));

        var badParent = WellFormed();
        badParent[0] = RewriteTerm(badParent[0], 0, RepeatedEnumerationRdfTerm.Literal(Work, null, null));

        var badAxiom = WellFormed();
        badAxiom[0] = RewriteTerm(badAxiom[0], 1, RepeatedEnumerationRdfTerm.BlankNode("axiom"));

        var badPredicate = WellFormed();
        badPredicate[0] = RewriteTerm(badPredicate[0], 2, RepeatedEnumerationRdfTerm.BlankNode("predicate"));

        var blankValue = WellFormed();
        blankValue.Add(Row(Unknown, RepeatedEnumerationRdfTerm.BlankNode("result-scoped")));

        var noSource = WellFormed();
        noSource.RemoveAll(row => row.Terms[2].Value == EuObjectFactsDiscoveryPlan.AnnotatedSourcePredicateIri);

        var sourceMismatch = WellFormed();
        Replace(sourceMismatch, EuObjectFactsDiscoveryPlan.AnnotatedSourcePredicateIri,
            RepeatedEnumerationRdfTerm.Iri(OtherTarget));

        var wrongProperty = WellFormed();
        Replace(wrongProperty, EuObjectFactsDiscoveryPlan.AnnotatedPropertyPredicateIri,
            RepeatedEnumerationRdfTerm.Iri(EuDateQualifierVocabulary.DeadlinePredicateUri));

        var noType = WellFormed();
        noType.RemoveAll(row => row.Terms[2].Value == EuObjectFactsDiscoveryPlan.RdfTypePredicateIri);

        var targetLiteral = WellFormed();
        Replace(targetLiteral, EuObjectFactsDiscoveryPlan.AnnotatedTargetPredicateIri,
            RepeatedEnumerationRdfTerm.Literal(Target, null, null));

        var conflictingModelledProperty = WellFormed();
        conflictingModelledProperty.Add(Row(EuObjectFactsDiscoveryPlan.AnnotatedPropertyPredicateIri,
            RepeatedEnumerationRdfTerm.Iri(EuDateQualifierVocabulary.DeadlinePredicateUri)));

        (EuLocatedAmendmentAxiomDecodeRefusal Expected, IReadOnlyList<RepeatedEnumerationRow> Rows)[] cases =
        [
            (EuLocatedAmendmentAxiomDecodeRefusal.RowShapeContradictsItsProjectedKind, markerConflict),
            (EuLocatedAmendmentAxiomDecodeRefusal.DeliveryCarriesNoRows, []),
            (EuLocatedAmendmentAxiomDecodeRefusal.ParentMissingOrNotAnIri, badParent),
            (EuLocatedAmendmentAxiomDecodeRefusal.AxiomNodeNotAnIri, badAxiom),
            (EuLocatedAmendmentAxiomDecodeRefusal.PredicateNotAnIri, badPredicate),
            (EuLocatedAmendmentAxiomDecodeRefusal.PropertyValueIsANonAddressableBlankNode, blankValue),
            (EuLocatedAmendmentAxiomDecodeRefusal.AnnotatedSourceMissingOrNotAnIri, noSource),
            (EuLocatedAmendmentAxiomDecodeRefusal.AnnotatedSourceDisagreesWithSelectedParent, sourceMismatch),
            (EuLocatedAmendmentAxiomDecodeRefusal.AnnotatedPropertyMissingOrNotAdmitted, wrongProperty),
            (EuLocatedAmendmentAxiomDecodeRefusal.AxiomTypeMissingOrNotOwlAxiom, noType),
            (EuLocatedAmendmentAxiomDecodeRefusal.AnnotatedTargetMissingOrNotAnIri, targetLiteral),
            (EuLocatedAmendmentAxiomDecodeRefusal.ModelledPredicateDeliveredMoreThanOnce,
                conflictingModelledProperty),
        ];

        var reached = new List<EuLocatedAmendmentAxiomDecodeRefusal>();
        foreach (var item in cases)
        {
            var observations = Decode(item.Rows, out var refusal);
            Assert.IsNull(observations, item.Expected.ToString());
            Assert.AreEqual(item.Expected, refusal, item.Expected.ToString());
            reached.Add(refusal);
        }

        CollectionAssert.AreEqual(
            Enum.GetValues<EuLocatedAmendmentAxiomDecodeRefusal>()
                .Where(value => value is not EuLocatedAmendmentAxiomDecodeRefusal.None
                    and not EuLocatedAmendmentAxiomDecodeRefusal.InterpretationProfileDoesNotBindReference
                    and not EuLocatedAmendmentAxiomDecodeRefusal.InterpretationProfileNotLocatedAmendmentFacts
                    and not EuLocatedAmendmentAxiomDecodeRefusal.BatchDuplicateAxiomDisagrees)
                .ToArray(),
            reached.Distinct().Order().ToArray());
    }

    private static IReadOnlyList<EuLocatedAmendmentAxiomObservation>? Decode(
        IReadOnlyList<RepeatedEnumerationRow> rows,
        out EuLocatedAmendmentAxiomDecodeRefusal refusal) =>
        Decode(rows, out refusal, out _);

    private static IReadOnlyList<EuLocatedAmendmentAxiomObservation>? Decode(
        IReadOnlyList<RepeatedEnumerationRow> rows,
        out EuLocatedAmendmentAxiomDecodeRefusal refusal,
        out string? offendingValue) =>
        EuLocatedAmendmentAxiomDecode.TryDecode(rows, Profile, Delivery, out refusal, out offendingValue);

    private static List<RepeatedEnumerationRow> WellFormed() =>
    [
        Row(EuObjectFactsDiscoveryPlan.RdfTypePredicateIri,
            RepeatedEnumerationRdfTerm.Iri(EuObjectFactsDiscoveryPlan.OwlAxiomClassIri)),
        Row(EuObjectFactsDiscoveryPlan.AnnotatedSourcePredicateIri, RepeatedEnumerationRdfTerm.Iri(Work)),
        Row(EuObjectFactsDiscoveryPlan.AnnotatedPropertyPredicateIri,
            RepeatedEnumerationRdfTerm.Iri(EuAmendmentRelationVocabulary.AmendsPredicateUri)),
        Row(EuObjectFactsDiscoveryPlan.AnnotatedTargetPredicateIri, RepeatedEnumerationRdfTerm.Iri(Target)),
    ];

    private static void Replace(
        List<RepeatedEnumerationRow> rows,
        string predicate,
        RepeatedEnumerationRdfTerm value)
    {
        var index = rows.FindIndex(row => row.Terms[2].Value == predicate);
        rows[index] = Row(predicate, value);
    }

    private static RepeatedEnumerationRow Row(string predicate, RepeatedEnumerationRdfTerm value) =>
        Row(RepeatedEnumerationRdfTerm.Iri(Axiom), RepeatedEnumerationRdfTerm.Iri(predicate), value);

    private static RepeatedEnumerationRow Row(
        RepeatedEnumerationRdfTerm axiom,
        RepeatedEnumerationRdfTerm predicate,
        RepeatedEnumerationRdfTerm value)
    {
        var kind = value.Kind switch
        {
            RepeatedEnumerationRdfTermKind.Iri => "iri",
            RepeatedEnumerationRdfTermKind.Literal => "literal",
            RepeatedEnumerationRdfTermKind.BlankNode => "unsupported_blank_node",
            _ => "unbound",
        };
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(Work), axiom, predicate, value,
            RepeatedEnumerationRdfTerm.Literal(kind, null, null),
            RepeatedEnumerationRdfTerm.Literal(value.Datatype ?? "", null, null),
            RepeatedEnumerationRdfTerm.Literal(value.Language ?? "", null, null),
            RepeatedEnumerationRdfTerm.Literal(Work, null, null),
            RepeatedEnumerationRdfTerm.Literal(axiom.Value ?? "", null, null),
            RepeatedEnumerationRdfTerm.Literal(predicate.Value ?? "", null, null),
            RepeatedEnumerationRdfTerm.Literal(value.Value ?? "", null, null),
            RepeatedEnumerationRdfTerm.Literal(kind, null, null),
            RepeatedEnumerationRdfTerm.Literal(value.Datatype ?? "", null, null),
            RepeatedEnumerationRdfTerm.Literal(value.Language ?? "", null, null),
        };
        return new RepeatedEnumerationRow(
            Array.AsReadOnly(terms), Array.AsReadOnly(terms[7..]), Array.AsReadOnly(terms[7..]));
    }

    private static RepeatedEnumerationRow RewriteTerm(
        RepeatedEnumerationRow row,
        int index,
        RepeatedEnumerationRdfTerm replacement)
    {
        var terms = row.Terms.ToArray();
        terms[index] = replacement;
        return new RepeatedEnumerationRow(Array.AsReadOnly(terms), row.CanonicalKey, row.Cursor);
    }

    private static RepeatedEnumerationRow AbsenceRow()
    {
        var unbound = RepeatedEnumerationRdfTerm.Unbound();
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(Work), unbound, unbound, unbound,
            RepeatedEnumerationRdfTerm.Literal("unbound", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal(Work, null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("unbound", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
        };
        return new RepeatedEnumerationRow(
            Array.AsReadOnly(terms), Array.AsReadOnly(terms[7..]), Array.AsReadOnly(terms[7..]));
    }
}
