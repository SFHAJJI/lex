using Lex.V3.Tests.Contracts.Source.Absence;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

[TestClass]
public sealed class LuxembourgSourceProfileTests
{
    private const string Jolux =
        "http://data.legilux.public.lu/resource/ontology/jolux#";

    [TestMethod]
    public void PublisherOriginEndpointAndObjectIdentityRemainDistinct()
    {
        var profile = VerifiedLuxembourgSourceProfile.Open(CompleteSnapshot());
        var objectRef = ObjectRef(
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1");
        var observation = Observation(objectRef);

        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            profile.Resolve(Proven([observation])));

        Assert.AreEqual("https://data.legilux.public.lu", profile.PublisherOrigin);
        Assert.AreEqual(
            "https://data.legilux.public.lu/sparqlendpoint",
            profile.SparqlEndpoint);
        Assert.AreEqual(objectRef.PublisherUri, resolved.Resources[0].ObjectRef.PublisherUri);
        Assert.AreNotEqual(profile.PublisherOrigin, resolved.Resources[0].ObjectRef.PublisherUri);
        Assert.AreNotEqual(profile.SparqlEndpoint, resolved.Resources[0].ObjectRef.PublisherUri);
    }

    [TestMethod]
    public void CompleteVocabularyOrderCannotChangeProfileIdentity()
    {
        var forward = CompleteSnapshot();
        var reverse = new LuxembourgVocabularySnapshot(
            forward.ObservationRef,
            forward.CompleteEnumerationRef,
            forward.IriValues.Reverse().ToArray(),
            forward.LiteralValues.Reverse().ToArray());

        var first = VerifiedLuxembourgSourceProfile.Open(forward);
        var second = VerifiedLuxembourgSourceProfile.Open(reverse);

        Assert.AreEqual(first.ScopeBinding.SourceProfileRef, second.ScopeBinding.SourceProfileRef);
        Assert.AreEqual(first.ScopeBinding.SelectorTableRef, second.ScopeBinding.SelectorTableRef);
        CollectionAssert.AreEqual(
            first.ObservedIriVocabulary.ToArray(),
            second.ObservedIriVocabulary.ToArray());

        Assert.ThrowsExactly<ArgumentException>(() =>
            VerifiedLuxembourgSourceProfile.Open(new LuxembourgVocabularySnapshot(
                forward.ObservationRef,
                forward.CompleteEnumerationRef,
                [.. forward.IriValues, forward.IriValues[0]],
                forward.LiteralValues)));
    }

    [TestMethod]
    public void IriRowsCannotAliasOneLiteralRowInTheProfileDigest()
    {
        const string datatype = "https://example.invalid/digest-datatype";
        const string lexical = "https://example.invalid/digest-value";
        var required = VerifiedLuxembourgSourceProfile.RequiredIriVocabulary;
        var twoIriRows = VerifiedLuxembourgSourceProfile.Open(new LuxembourgVocabularySnapshot(
            ObservationRef,
            EnumerationRef,
            [
                .. required,
                new LuxembourgIriVocabularyValue(LuxembourgVocabularyKind.Language, datatype),
                new LuxembourgIriVocabularyValue(
                    LuxembourgVocabularyKind.PublicationFamily,
                    lexical),
            ],
            []));
        var oneLiteralRow = VerifiedLuxembourgSourceProfile.Open(new LuxembourgVocabularySnapshot(
            ObservationRef,
            EnumerationRef,
            required,
            [
                new LuxembourgLiteralVocabularyValue(
                    LuxembourgVocabularyKind.Language,
                    datatype,
                    "6",
                    lexical),
            ]));

        Assert.AreNotEqual(
            twoIriRows.ScopeBinding.SourceProfileRef.Sha256,
            oneLiteralRow.ScopeBinding.SourceProfileRef.Sha256);
    }

    [TestMethod]
    public void LiteralVocabularyPreservesRawTermsAndDerivesSelectorTruth()
    {
        var profile = VerifiedLuxembourgSourceProfile.Open(new LuxembourgVocabularySnapshot(
            ObservationRef,
            EnumerationRef,
            VerifiedLuxembourgSourceProfile.RequiredIriVocabulary,
            [
                new LuxembourgLiteralVocabularyValue(
                    LuxembourgVocabularyKind.Language,
                    LuxembourgLiteralCanonicalizer.RdfLangString,
                    "FR-latn",
                    "Texte coordonne"),
                new LuxembourgLiteralVocabularyValue(
                    LuxembourgVocabularyKind.LegalValue,
                    "http://www.w3.org/2001/XMLSchema#boolean",
                    string.Empty,
                    "1"),
                new LuxembourgLiteralVocabularyValue(
                    LuxembourgVocabularyKind.Rights,
                    LuxembourgLiteralCanonicalizer.XsdString,
                    string.Empty,
                    string.Empty),
            ]));

        var language = profile.ObservedLiteralVocabulary.Single(value =>
            value.Kind == LuxembourgVocabularyKind.Language);
        Assert.AreEqual("FR-latn", language.RawLanguageTagOrEmpty);
        Assert.AreEqual("fr-latn", language.LanguageTag);
        Assert.AreEqual("Texte coordonne", language.RawLexicalValue);
        Assert.AreEqual("Texte coordonne", language.CanonicalSelectorLexicalValue);
        Assert.AreEqual(LuxembourgLiteralDisposition.Accepted, language.Disposition);

        var unruled = profile.ObservedLiteralVocabulary.Single(value =>
            value.Kind == LuxembourgVocabularyKind.LegalValue);
        Assert.AreEqual("1", unruled.RawLexicalValue);
        Assert.AreEqual(
            "http://www.w3.org/2001/XMLSchema#boolean",
            unruled.RawDatatypeIriOrEmpty);
        Assert.IsNull(unruled.CanonicalSelectorLexicalValue);
        Assert.AreEqual(LuxembourgLiteralDisposition.TypedQuarantine, unruled.Disposition);

        var empty = profile.ObservedLiteralVocabulary.Single(value =>
            value.Kind == LuxembourgVocabularyKind.Rights);
        Assert.AreEqual(string.Empty, empty.RawLexicalValue);
        Assert.AreEqual(string.Empty, empty.CanonicalSelectorLexicalValue);
    }

    [TestMethod]
    public void VocabularyCanonicalOrderUsesUnicodeScalarValues()
    {
        const string privateUse = "https://example.invalid/\uE000";
        const string supplementary = "https://example.invalid/\U00010000";
        var profile = VerifiedLuxembourgSourceProfile.Open(new LuxembourgVocabularySnapshot(
            ObservationRef,
            EnumerationRef,
            [
                .. VerifiedLuxembourgSourceProfile.RequiredIriVocabulary,
                new LuxembourgIriVocabularyValue(
                    LuxembourgVocabularyKind.PublicationFamily,
                    supplementary),
                new LuxembourgIriVocabularyValue(
                    LuxembourgVocabularyKind.PublicationFamily,
                    privateUse),
            ],
            []));

        CollectionAssert.AreEqual(
            new[] { privateUse, supplementary },
            profile.ObservedIriVocabulary
                .Where(static value =>
                    value.Kind == LuxembourgVocabularyKind.PublicationFamily)
                .Select(static value => value.FullIri)
                .ToArray());
    }

    [TestMethod]
    public void RequiredRulesIncludeOrdinaryActsSupportAndUntypedCites()
    {
        var required = VerifiedLuxembourgSourceProfile.RequiredIriVocabulary;

        Assert.IsTrue(required.Any(value =>
            value.Kind == LuxembourgVocabularyKind.TypeDocument &&
            value.FullIri.EndsWith("/A", StringComparison.Ordinal)));
        Assert.IsTrue(required.Any(value =>
            value.Kind == LuxembourgVocabularyKind.ResourceClass &&
            value.FullIri == Jolux + "DraftRelatedDocument"));
        Assert.IsTrue(required.Any(value =>
            value.Kind == LuxembourgVocabularyKind.RelationPredicate &&
            value.FullIri == Jolux + "cites"));

        var profile = VerifiedLuxembourgSourceProfile.Open(CompleteSnapshot());
        var cites = profile.RelationRules.Single(rule => rule.PredicateIri == Jolux + "cites");
        Assert.AreEqual(LuxembourgRelationSemantic.AssertedCitation, cites.Semantic);
    }

    [TestMethod]
    public void PredicateAuthorityIsExactlyTwentySixAssertionsAndEighteenRelations()
    {
        var expectedAssertions = new[]
        {
            "http://www.w3.org/1999/02/22-rdf-syntax-ns#type",
            Jolux + "dateApplicability",
            Jolux + "dateDocument",
            Jolux + "dateEndApplicability",
            Jolux + "dateEntryInForce",
            Jolux + "dateNoLongerInForce",
            Jolux + "historicalLegalId",
            Jolux + "inForceStatus",
            Jolux + "isEmbodiedBy",
            Jolux + "isExemplifiedBy",
            Jolux + "isMemberOf",
            Jolux + "isPartOf",
            Jolux + "isRealizedBy",
            Jolux + "language",
            Jolux + "legalValue",
            Jolux + "license",
            Jolux + "previousIsExemplifiedBy",
            Jolux + "publicationDate",
            Jolux + "publisher",
            Jolux + "responsibilityOf",
            Jolux + "rights",
            Jolux + "rightsHolder",
            Jolux + "title",
            Jolux + "titleShort",
            Jolux + "typeDocument",
            Jolux + "userFormat",
        }.Order(StringComparer.Ordinal).ToArray();
        var expectedRelations = new[]
        {
            "basedOn", "basicAct", "cites", "consolidates", "hasIndirectImpact",
            "impactConsolidatedBy", "impactConsolidatedByExpression", "impactFromLegalResource",
            "impactToExpression", "impactToLegalResource", "legalAnalysisHasLegalResourceImpact",
            "legalResourceImpactHasDateEntryInForce", "legalResourceImpactHasType",
            "modifiedTempBy", "modifies", "rectifies", "repeals", "transposes",
        }.Select(static value => Jolux + value).Order(StringComparer.Ordinal).ToArray();
        var required = VerifiedLuxembourgSourceProfile.RequiredIriVocabulary;

        CollectionAssert.AreEqual(
            expectedAssertions,
            required.Where(static value => value.Kind == LuxembourgVocabularyKind.AssertionPredicate)
                .Select(static value => value.FullIri).ToArray());
        CollectionAssert.AreEqual(
            expectedRelations,
            required.Where(static value => value.Kind == LuxembourgVocabularyKind.RelationPredicate)
                .Select(static value => value.FullIri).ToArray());
    }

    [TestMethod]
    public void CitesPreservesPublisherDirectionWithoutInventingInterpretation()
    {
        var profile = VerifiedLuxembourgSourceProfile.Open(CompleteSnapshot());
        var source = ObjectRef(
            "http://data.legilux.public.lu/eli/etat/leg/acc/2026/01/01/a1");
        var target = "http://data.legilux.public.lu/eli/etat/leg/loi/2025/01/01/a2";
        var relation = new LuxembourgObservedRelation(
            source.PublisherUri,
            Jolux + "cites",
            target,
            ObservationRef);

        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            profile.Resolve(Proven([Observation(source, relations: [relation])])));
        var resource = resolved.Resources.Single();
        var retained = resource.Relations.Single();

        Assert.AreEqual(source.PublisherUri, retained.SubjectIri);
        Assert.AreEqual(Jolux + "cites", retained.PredicateIri);
        Assert.AreEqual(target, retained.ObjectIri);
        Assert.AreEqual(LuxembourgRelationSemantic.AssertedCitation, retained.Semantic);
        Assert.AreEqual(LuScopeTerminalState.AcceptedMetadata, resource.Dimensions.Relation.State);
        Assert.IsFalse(retained.Semantic.ToString().Contains("Interpret", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ProfileInvalidPublisherIdentityReturnsTypedFailureAndNoPartialInput()
    {
        var profile = VerifiedLuxembourgSourceProfile.Open(CompleteSnapshot());
        var outsidePublisher = ObjectRef("https://example.invalid/legal-resource");

        var failure = Assert.IsInstanceOfType<LuxembourgProfileResolution.Failed>(
            profile.Resolve(Proven([Observation(outsidePublisher)])));

        Assert.AreEqual(
            LuxembourgProfileResolutionFailureCode.InvalidPublisherIri,
            failure.Failure.Code);
        Assert.AreEqual(
            "profile_resolution_failed_invalid_publisher_iri",
            failure.Failure.ReasonCode);
    }

    [TestMethod]
    public void AssertionOutsideBoundVocabularyIsTypedScopeDrift()
    {
        var profile = VerifiedLuxembourgSourceProfile.Open(CompleteSnapshot());
        var objectRef = ObjectRef(
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1");
        var assertion = new LuxembourgObservedAssertion(
            objectRef.PublisherUri,
            Jolux + "typeDocument",
            LuxembourgAssertionObjectKind.Iri,
            "http://data.legilux.public.lu/resource/authority/resource-type/FUTURE",
            string.Empty,
            string.Empty,
            ObservationRef);

        var failure = Assert.IsInstanceOfType<LuxembourgProfileResolution.Failed>(
            profile.Resolve(Proven([Observation(objectRef, assertions: [assertion])])));

        Assert.AreEqual(
            LuxembourgProfileResolutionFailureCode.UnknownVocabularyDrift,
            failure.Failure.Code);
    }

    /// <summary>
    /// The proof door every scope resolution goes through
    /// (<see cref="LuxembourgProvenResourceObservations"/>), using a REAL proof from
    /// <see cref="AbsenceFixtures"/> rather than a relaxed test-only one.
    /// </summary>
    private static LuxembourgProvenResourceObservations Proven(
        params LuxembourgResourceObservation[] observations) =>
        LuxembourgProvenResourceObservations.RequireProven(AbsenceFixtures.Proof(), observations);

    private static LuxembourgVocabularySnapshot CompleteSnapshot() => new(
        ObservationRef,
        EnumerationRef,
        VerifiedLuxembourgSourceProfile.RequiredIriVocabulary.Reverse().ToArray(),
        []);

    private static LuxembourgResourceObservation Observation(
        SourceObjectRef objectRef,
        IReadOnlyList<LuxembourgObservedAssertion>? assertions = null,
        IReadOnlyList<LuxembourgObservedRelation>? relations = null) => new(
        objectRef,
        ObservationRef,
        assertions ?? [],
        relations ?? [],
        new LuxembourgSparqlRightsChannelObservations(
            ObservationRef,
            SparqlRightsEnumerationRef,
            []),
        new LuxembourgInFileRightsChannelObservations(
            ObservationRef,
            InFileRightsEnumerationRef,
            []));

    private static SourceObjectRef ObjectRef(string publisherUri) => new(
        SourceCoreSchemaIds.SourceObjectRef,
        SourceAuthority.Jolux,
        new SourceRegistryMemberRef(EntityRegistryRef, "legal_resource"),
        publisherUri,
        publisherUri,
        Sha256(publisherUri),
        IdentityProfileRef,
        null);

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static SourceArtifactRef ObservationRef { get; } = new(
        "urn:uuid:10dd0a6e-3fa4-468d-a2aa-570a93ec4bf0",
        new string('1', 64));

    private static SourceArtifactRef EnumerationRef { get; } = new(
        "urn:uuid:3f60c78d-6e8a-4208-9146-43b634db9bbc",
        new string('2', 64));

    private static SourceArtifactRef EntityRegistryRef { get; } = new(
        "urn:uuid:760b560c-15c2-407d-b38f-f99f4c59e345",
        new string('3', 64));

    private static SourceArtifactRef IdentityProfileRef { get; } = new(
        "urn:uuid:54b9c06f-ed04-4d07-8239-72dce5fed499",
        new string('4', 64));

    private static SourceArtifactRef SparqlRightsEnumerationRef { get; } = new(
        "urn:uuid:8b42bff0-128c-4daa-a111-d05452d9b0c8",
        new string('5', 64));

    private static SourceArtifactRef InFileRightsEnumerationRef { get; } = new(
        "urn:uuid:90a12718-936e-4e43-9be7-d1ee407cf9b5",
        new string('6', 64));
}
