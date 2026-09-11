using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class EuLocatedAmendmentAxiomProjectionTests
{
    private const string Source = "http://publications.europa.eu/resource/cellar/00000000-0000-4000-8000-000000000001";
    private const string Target = "http://publications.europa.eu/resource/cellar/00000000-0000-4000-8000-000000000002";
    private const string OtherTarget = "http://publications.europa.eu/resource/cellar/00000000-0000-4000-8000-000000000003";
    private const string Axiom = "http://publications.europa.eu/.well-known/genid/located-amendment/projection";
    private const string Fd370 = "http://publications.europa.eu/resource/authority/fd_370";
    private const string Fd375 = "http://publications.europa.eu/resource/authority/fd_375";

    [TestMethod]
    public void AProofBoundSingleTargetObservationProducesTheAcceptedAxiomAndRetainsItsEvidence()
    {
        var observation = Observation([Target], Properties());

        var projection = EuLocatedAmendmentAxiomProjection.TryCreate(
            observation,
            Identity(Source),
            Identity(Target),
            TargetBodyScope.BodyInScopeHeld,
            out var refusal,
            out var offendingPredicate);

        Assert.AreEqual(EuLocatedAmendmentAxiomProjectionRefusal.None, refusal);
        Assert.IsNull(offendingPredicate);
        Assert.IsNotNull(projection);
        Assert.AreSame(observation, projection.Observation);
        Assert.AreEqual(observation.InterpretationProfileRefs.Single(), projection.InterpretationProfileRef);
        Assert.AreEqual(TargetBodyScope.BodyInScopeHeld, projection.Axiom.Edge.Fact.TargetBodyScope);
        Assert.AreEqual(observation.InterpretationProfileRefs.Single().ResourceId,
            projection.Axiom.Edge.Asserted.SourceObservationId);
        Assert.AreEqual(Source, projection.Axiom.Edge.Asserted.Source.Value(FactsIdentifierFamily.CellarWorkUri));
        Assert.AreEqual(Target, projection.Axiom.Edge.Asserted.Target.Value(FactsIdentifierFamily.CellarWorkUri));
        Assert.AreEqual("{AN|" + Fd370 + "/AN} 1", projection.Axiom.Location.RawValue);
        Assert.AreEqual("R", projection.Axiom.Role.Code);
        Assert.AreEqual("2010/01/01", projection.Axiom.StartOfValidity!.RawLexicalValue);
        Assert.IsNull(projection.Axiom.EndOfValidity);
        Assert.AreEqual("MS", projection.Axiom.TypeOfLinkTarget);
        Assert.HasCount(Properties().Count, projection.Observation.RawProperties);
    }

    [TestMethod]
    public void AmbiguousOrIdentitySubstitutedObservationsCannotMintAFinalAxiom()
    {
        var cases = new[]
        {
            (Observation([Target, OtherTarget], Properties()), Identity(Source), Identity(Target),
                EuLocatedAmendmentAxiomProjectionRefusal.PublisherTargetAmbiguous),
            (Observation([Target], Properties(), source: OtherTarget), Identity(Source), Identity(Target),
                EuLocatedAmendmentAxiomProjectionRefusal.SourceIdentityDoesNotMatchObservation),
            (Observation([Target], Properties()), Identity(Source), Identity(OtherTarget),
                EuLocatedAmendmentAxiomProjectionRefusal.TargetIdentityDoesNotMatchObservation),
        };

        foreach (var item in cases)
        {
            var projection = EuLocatedAmendmentAxiomProjection.TryCreate(
                item.Item1, item.Item2, item.Item3, TargetBodyScope.BodyOutsideScope,
                out var refusal, out _);

            Assert.IsNull(projection, item.Item4.ToString());
            Assert.AreEqual(item.Item4, refusal);
        }
    }

    [TestMethod]
    public void MissingRepeatedOrNonLiteralKnownQualifiersReachTypedRefusals()
    {
        var missing = Properties();
        missing.RemoveAll(property => property.PredicateIri == EuAmendmentRelationVocabulary.ReferenceToModifiedLocationUri);

        var repeated = Properties();
        repeated.Add(Property(EuAmendmentRelationVocabulary.TypeOfLinkTargetUri, "MS"));

        var wrongKind = Properties();
        Replace(wrongKind, EuAmendmentRelationVocabulary.Role2Uri,
            new EuLocatedAmendmentRawProperty(
                EuAmendmentRelationVocabulary.Role2Uri,
                RepeatedEnumerationRdfTerm.Iri(Fd375 + "/R")));

        var cases = new[]
        {
            (missing, EuLocatedAmendmentAxiomProjectionRefusal.RequiredQualifierMissing,
                EuAmendmentRelationVocabulary.ReferenceToModifiedLocationUri),
            (repeated, EuLocatedAmendmentAxiomProjectionRefusal.QualifierRepeated,
                EuAmendmentRelationVocabulary.TypeOfLinkTargetUri),
            (wrongKind, EuLocatedAmendmentAxiomProjectionRefusal.QualifierNotPlainLiteral,
                EuAmendmentRelationVocabulary.Role2Uri),
        };

        foreach (var item in cases)
        {
            var projection = EuLocatedAmendmentAxiomProjection.TryCreate(
                Observation([Target], item.Item1), Identity(Source), Identity(Target),
                TargetBodyScope.BodyInScopeNotHeld, out var refusal, out var predicate);

            Assert.IsNull(projection, item.Item2.ToString());
            Assert.AreEqual(item.Item2, refusal);
            Assert.AreEqual(item.Item3, predicate);
        }
    }

    [TestMethod]
    public void AQualifierWhoseLexicalValueViolatesTheAcceptedTypeReachesATypedRefusal()
    {
        var malformed = Properties();
        Replace(malformed, EuAmendmentRelationVocabulary.TypeOfLinkTargetUri,
            Property(EuAmendmentRelationVocabulary.TypeOfLinkTargetUri, "M|S"));

        var projection = EuLocatedAmendmentAxiomProjection.TryCreate(
            Observation([Target], malformed), Identity(Source), Identity(Target),
            TargetBodyScope.BodyInScopeNotHeld, out var refusal, out var predicate);

        Assert.IsNull(projection);
        Assert.AreEqual(EuLocatedAmendmentAxiomProjectionRefusal.QualifierValueNotAdmitted, refusal);
        Assert.AreEqual(EuAmendmentRelationVocabulary.TypeOfLinkTargetUri, predicate);
    }

    private static OfficialIdentitySet Identity(string iri) =>
        new(PublisherId.EuEurLex,
            [new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, iri)]);

    private static EuLocatedAmendmentAxiomObservation Observation(
        IReadOnlyList<string> targets,
        IReadOnlyList<EuLocatedAmendmentRawProperty> properties,
        string source = Source)
    {
        var profile = EuObjectFactsDiscoveryPlan.Create()
            .CreateDeliveryProfile(EuObjectFactsQuerySet.LocatedAmendmentFacts);
        var profileRef = RepeatedEnumerationInterpretationProfileIdentity.Create(
            "urn:uuid:00000000-0000-4000-8000-00000000a808", profile);
        return new EuLocatedAmendmentAxiomObservation(
            Axiom, [source], EuAmendmentRelationVocabulary.AmendsPredicateUri,
            targets, properties, [profileRef]);
    }

    private static List<EuLocatedAmendmentRawProperty> Properties() =>
    [
        Property(EuAmendmentRelationVocabulary.ReferenceToModifiedLocationUri,
            "{AN|" + Fd370 + "/AN} 1"),
        Property(EuAmendmentRelationVocabulary.Role2Uri, "{R|" + Fd375 + "/R}"),
        Property(EuAmendmentRelationVocabulary.StartOfValidityUri, "2010/01/01"),
        Property(EuAmendmentRelationVocabulary.TypeOfLinkTargetUri, "MS"),
        Property(EuAmendmentRelationVocabulary.AnnotationNamespace + "publisher_extension", "retained"),
    ];

    private static EuLocatedAmendmentRawProperty Property(string predicate, string value) =>
        new(predicate, RepeatedEnumerationRdfTerm.Literal(value, null, null));

    private static void Replace(
        List<EuLocatedAmendmentRawProperty> properties,
        string predicate,
        EuLocatedAmendmentRawProperty replacement)
    {
        var index = properties.FindIndex(property => property.PredicateIri == predicate);
        properties[index] = replacement;
    }
}
