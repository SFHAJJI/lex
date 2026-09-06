using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.TestSupport;
using Lex.V3.Tests.Contracts.Source.Absence;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgPerWorkQuarantineTests
{
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";
    private const string Authority = "http://data.legilux.public.lu/resource/authority/";
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string NormalWork = "http://data.legilux.public.lu/eli/etat/leg/code/normal";
    private const string ChangedWork = "http://data.legilux.public.lu/eli/etat/leg/code/changed";
    private const string UnknownType = Authority + "resource-type/FUTURE";
    private static readonly SourceArtifactRef ObservationRef = new(
        "urn:uuid:186e7a5e-f4a3-421a-a3c0-caa6cde89912", new string('a', 64));

    [TestMethod]
    public void AnUnexpectedTypeQuarantinesItsWorkAndPreservesAnotherWorksBodyAndTheRawValue()
    {
        var profile = LuxembourgProfiles.Opened(new LuxembourgVocabularySnapshot(
            ObservationRef, ObservationRef, VerifiedLuxembourgSourceProfile.RequiredIriVocabulary, []));
        var normal = Observation(NormalWork, Authority + "resource-type/TC", profile);
        var changed = Observation(ChangedWork, UnknownType, profile);
        var baseline = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(profile.Resolve(
            LuxembourgProvenResourceObservations.RequireProven(AbsenceFixtures.Proof(), [normal])));
        Assert.AreEqual(LuScopeTerminalState.AcceptedCandidate, baseline.Resources.Single().Dimensions.Body.State,
            "The unchanged work must be a valid body candidate before adding the other work's drift.");

        // The value first appears in this delivered work. Registering it in the profile fixture
        // would bypass the production drift check and hide the whole-run refusal under test.
        Assert.IsFalse(profile.ContainsVocabulary(LuxembourgVocabularyKind.TypeDocument, UnknownType));
        var result = profile.Resolve(LuxembourgProvenResourceObservations.RequireProven(
            AbsenceFixtures.Proof(), [normal, changed]));

        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(result,
            "One newly observed publisher type must not refuse every work in a proven enumeration. " +
            $"Actual failure: {(result as LuxembourgProfileResolution.Failed)?.Failure.Code} " +
            $"on {(result as LuxembourgProfileResolution.Failed)?.Failure.Subject}");
        Assert.HasCount(2, resolved.Resources);
        var accepted = resolved.Resources.Single(resource => resource.ObjectRef.PublisherUri == NormalWork);
        Assert.AreEqual(LuScopeTerminalState.AcceptedCandidate, accepted.Dimensions.Body.State);
        Assert.AreEqual(LuxembourgBodyCandidateDisposition.AcceptedCandidate,
            accepted.BodyJoin.Candidates.Single().Disposition);

        var quarantined = resolved.Resources.Single(resource => resource.ObjectRef.PublisherUri == ChangedWork);
        Assert.AreEqual(LuScopeTerminalState.TypedQuarantine, quarantined.Dimensions.PublicationFamily.State);
        Assert.AreEqual(LuScopeTerminalState.TypedQuarantine, quarantined.Dimensions.Body.State);
        var retained = quarantined.Assertions.Single(assertion =>
            assertion.Assertion.SubjectIri == ChangedWork && assertion.Assertion.PredicateIri == Jolux + "typeDocument");
        Assert.AreEqual(LuxembourgAssertionDisposition.TypedQuarantine, retained.Disposition);
        Assert.AreEqual(UnknownType, retained.Assertion.ObjectIriOrLexical);
        Assert.AreEqual(ObservationRef, retained.Assertion.ObservationRef);
        var familySelector = resolved.ScopeInputs.Single(input => input.ObjectRef.PublisherUri == ChangedWork)
            .Selectors[LuxembourgScopeResolver.PublicationFamilySelectorIndex];
        CollectionAssert.AreEqual(new[] { UnknownType }, familySelector.CanonicalValues.ToArray());
    }

    private static LuxembourgResourceObservation Observation(
        string work, string type, VerifiedLuxembourgSourceProfile profile)
    {
        var expression = work + "/fr";
        var manifestation = expression + "/xml";
        return LuxembourgQueryExecutionAdapter.BuildResourceObservation(work,
        [
            Iri(work, RdfType, Jolux + "Act"),
            Iri(work, Jolux + "typeDocument", type),
            Iri(work, Jolux + "isRealizedBy", expression),
            Iri(expression, RdfType, Jolux + "Expression"),
            Iri(expression, Jolux + "language", "http://publications.europa.eu/resource/authority/language/FRA"),
            Iri(expression, Jolux + "isEmbodiedBy", manifestation),
            Iri(manifestation, RdfType, Jolux + "Manifestation"),
            Iri(manifestation, Jolux + "userFormat", Authority + "user-format/xml"),
            Iri(manifestation, Jolux + "isExemplifiedBy",
                work.Replace("/eli/", "/filestore/eli/", StringComparison.Ordinal) + "/fr/xml/document.xml"),
        ], ObservationRef, profile.ScopeBinding.SourceProfileRef);
    }

    private static LuxembourgObservedAssertion Iri(string subject, string predicate, string value) =>
        new(subject, predicate, LuxembourgAssertionObjectKind.Iri, value, "", "", ObservationRef);
}
