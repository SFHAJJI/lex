using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

[TestClass]
public sealed class LuxembourgSourceProfileAdversarialProofTests
{
    [TestMethod]
    public void RequiredVocabularyInventoryIsPinnedIndependently()
    {
        CollectionAssert.AreEqual(
            ExpectedVocabulary()
                .Where(static value => value.Kind != LuxembourgVocabularyKind.LegalValue)
                .Select(VocabularySignature)
                .ToArray(),
            VerifiedLuxembourgSourceProfile.RequiredIriVocabulary
                .Where(static value => value.Kind != LuxembourgVocabularyKind.LegalValue)
                .Select(VocabularySignature)
                .ToArray());
        CollectionAssert.AreEqual(
            ExpectedVocabulary().Select(VocabularySignature).ToArray(),
            VerifiedLuxembourgSourceProfile.RequiredIriVocabulary
                .Select(VocabularySignature)
                .ToArray());
    }

    [TestMethod]
    public void CompleteAuthorityVocabularyWithoutUnmintedSvgStillOpens()
    {
        var authorityVocabulary = ExpectedVocabulary()
            .Select(static value => new LuxembourgIriVocabularyValue(value.Kind, value.Iri))
            .ToArray();

        Assert.IsFalse(authorityVocabulary.Any(value =>
            value.Kind == LuxembourgVocabularyKind.UserFormat &&
            value.FullIri == UserFormatPrefix + "svg"));
        CollectionAssert.IsSubsetOf(
            new[] { "jpeg", "jpg", "xls", "xlsx", "xml-lux", "zip" }
                .Select(value => UserFormatPrefix + value)
                .ToArray(),
            authorityVocabulary
                .Where(static value => value.Kind == LuxembourgVocabularyKind.UserFormat)
                .Select(static value => value.FullIri)
                .ToArray());

        var profile = VerifiedLuxembourgSourceProfile.Open(new LuxembourgVocabularySnapshot(
            ObservationRef,
            CompleteEnumerationRef,
            authorityVocabulary,
            []));

        Assert.HasCount(authorityVocabulary.Length, profile.ObservedIriVocabulary);
    }

    [TestMethod]
    public void ProfileAndSelectorTableDigestsArePinnedIndependently()
    {
        var profile = ExpectedProfile();

        Assert.AreEqual(
            "7e693eda015f85ecf09b30f455914d57cf23870aea99ceb6b567292b840ef798",
            profile.ScopeBinding.SourceProfileRef.Sha256);
        Assert.AreEqual(
            "c25b96ade3fe55ebc81ee4135859954abc46c04026124336d7a2ab493809fb3e",
            profile.ScopeBinding.SelectorTableRef.Sha256);
    }

    [TestMethod]
    public void SelectorAndProjectionInventoriesCarryEveryExpectedStateAndEvidenceKind()
    {
        var profile = ExpectedProfile();
        var resolved = Resolve(profile, EmptyObservation());
        var selectorKeys = SelectorKeys(profile);

        CollectionAssert.AreEqual(ExpectedSelectorKeys, selectorKeys);
        CollectionAssert.AreEqual(
            new[]
            {
                "0|Record|projection.record",
                "1|Body|projection.body",
                "2|Relation|projection.relation",
                "3|SupportingDocument|projection.supporting_document",
            },
            profile.ScopeBinding.OrderedRules
                .Select(rule =>
                    $"{rule.Ordinal}|{rule.Axis}|" +
                    profile.ScopeBinding.OrderedMembers[rule.RuleMemberOrdinal].MemberKey)
                .ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "selector.record|PublisherValuePresent|ObservedValueSet|observation|" + RootIri,
                "selector.relation|SelectorNotApplicable|none|rule:2|",
                "selector.supporting_document|SelectorNotApplicable|none|rule:3|",
                "selector.publication_family|PublisherValueAbsent|CompleteObservationAbsence|observation|",
                "selector.language|SelectorNotApplicable|none|rule:1|",
                "selector.format|SelectorNotApplicable|none|rule:1|",
                "selector.authenticity|SelectorNotApplicable|none|rule:1|",
                "selector.body_join|PublisherValuePresent|ObservedValueSet|observation|body_join_sha256:<digest>",
                "selector.rights_sparql|SelectorNotApplicable|none|rule:1|",
                "selector.rights_in_file|SelectorNotApplicable|none|rule:1|",
                "selector.transport_uri|SelectorNotApplicable|none|rule:1|",
                "selector.transport_robots|SelectorNotApplicable|none|rule:1|",
                "selector.transport_http|SelectorNotApplicable|none|rule:1|",
            },
            resolved.ScopeInputs.Single().Selectors
                .Select((selector, index) => SelectorSignature(
                    selectorKeys[index],
                    selector,
                    resolved.OrderedEvidenceArtifacts))
                .ToArray());
    }

    [TestMethod]
    public void ResolvedLuxembourgProfileReducesEndToEndIntoVerifiedScopeManifest()
    {
        var profile = ExpectedProfile();
        var resolved = Resolve(profile, BodyObservation([], []));
        var evidence = new AcceptingEvidenceResolver(CompleteEnumerationRef);

        var verified = profile.ReduceScope(resolved, evidence);

        Assert.AreEqual(ScopeManifestSchemaIds.Manifest, verified.Manifest.Schema);
        Assert.AreEqual(profile.ScopeBinding, verified.Manifest.Profile);
        Assert.AreEqual(CompleteEnumerationRef, verified.Manifest.CompleteEnumerationRef);
        Assert.HasCount(1, verified.Manifest.ObservedObjects);
        Assert.HasCount(1, verified.Manifest.Rows);
        Assert.HasCount(ExpectedSelectorKeys.Length, verified.Manifest.Rows.Single().Selectors);
        Assert.HasCount(4, verified.Manifest.Rows.Single().MatchedEvaluations);
        Assert.HasCount(16, verified.Manifest.Accounting);
        Assert.HasCount(0, verified.Manifest.BodyCandidateOrdinals);
        Assert.IsTrue(evidence.CompleteEnumerationBindings.Count > 0);
        Assert.IsTrue(evidence.SelectorObservationBindings.Count > 0);
        Assert.IsTrue(evidence.SelectorNotApplicableBindings.Count > 0);
        Assert.IsTrue(evidence.RuleEvaluationBindings.Count > 0);
    }

    [TestMethod]
    public void RelationSelectorFramesEveryShapeSection()
    {
        const string classOnly = "https://example.invalid/collision/0";
        const string classOrType = "https://example.invalid/collision/1";
        const string typeTwo = "https://example.invalid/collision/2";
        const string typeThree = "https://example.invalid/collision/3";
        var profile = Profile(
            new(LuxembourgVocabularyKind.ResourceClass, classOnly),
            new(LuxembourgVocabularyKind.ResourceClass, classOrType),
            new(LuxembourgVocabularyKind.TypeDocument, classOrType),
            new(LuxembourgVocabularyKind.TypeDocument, typeTwo),
            new(LuxembourgVocabularyKind.TypeDocument, typeThree));

        var first = Resolve(profile, RelationObservation(
            [Iri(RootIri, RdfType, classOnly),
             Iri(RootIri, TypeDocument, classOrType),
             Iri(RootIri, TypeDocument, typeTwo),
             Iri(RootIri, TypeDocument, typeThree)]));
        var second = Resolve(profile, RelationObservation(
            [Iri(RootIri, RdfType, classOnly),
             Iri(RootIri, RdfType, classOrType),
             Iri(RootIri, TypeDocument, typeTwo),
             Iri(RootIri, TypeDocument, typeThree)]));

        Assert.AreNotEqual(
            SelectorValues(profile, first, "selector.relation").Single(),
            SelectorValues(profile, second, "selector.relation").Single(),
            "Moving one token between classes and types must change the relation identity.");
    }

    [TestMethod]
    public void BodyJoinSelectorDistinguishesWhichRightsChannelSuppliedEvidence()
    {
        var profile = Profile();
        var sparqlOnly = Resolve(profile, BodyObservation(
            [Rights(SharedRightsEvidenceRef)],
            []));
        var inFileOnly = Resolve(profile, BodyObservation(
            [],
            [Rights(SharedRightsEvidenceRef)]));

        Assert.AreNotEqual(
            SelectorValues(profile, sparqlOnly, "selector.body_join").Single(),
            SelectorValues(profile, inFileOnly, "selector.body_join").Single(),
            "A SPARQL observation and an in-file observation are different evidence facts.");
    }

    [TestMethod]
    public void AcceptedConsolidatesShapeRejectsMissingOrIncompatibleClasses()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AcceptedShape([], [Jolux + "Act"]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            AcceptedShape([Jolux + "Expression"], [Jolux + "Manifestation"]));
    }

    [TestMethod]
    public void ResolvedAssertionRejectsNullAssertionAndUndefinedDisposition()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new LuxembourgResolvedAssertion(
            null!,
            LuxembourgAssertionDisposition.Accepted));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LuxembourgResolvedAssertion(
            Iri(RootIri, RdfType, Jolux + "Act"),
            (LuxembourgAssertionDisposition)0));
    }

    [TestMethod]
    public void FailedResolutionRejectsNullFailure()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new LuxembourgProfileResolution.Failed(null!));
    }

    [TestMethod]
    public void ResolverOnlyOutputTypesExposeNoPublicInstanceConstructor()
    {
        var resolverOnlyTypes = new[]
        {
            typeof(LuxembourgRelationRule),
            typeof(LuxembourgConsolidatesShape),
            typeof(LuxembourgResolvedRelation),
            typeof(LuxembourgResolvedAssertion),
            typeof(LuxembourgDimensionAccounting),
            typeof(LuxembourgResourceResolution),
            typeof(LuxembourgProfileResolutionFailure),
            typeof(LuxembourgProfileResolution.Failed),
            typeof(LuxembourgWemiBlocker),
            typeof(LuxembourgTypedRoleResolution),
        };

        foreach (var type in resolverOnlyTypes)
        {
            Assert.HasCount(
                0,
                type.GetConstructors(BindingFlags.Instance | BindingFlags.Public),
                type.FullName);
        }
    }

    [TestMethod]
    public void ResolverProducedAcceptedConsolidatesRelationCarriesCompleteAcceptedShape()
    {
        var profile = ExpectedProfile();
        var source = RelationObservation(
        [
            Iri(RootIri, RdfType, Jolux + "Act"),
            Iri(RootIri, TypeDocument, TypeDocumentPrefix + "TC"),
        ]);
        var target = MetadataObservation(
            RelationTargetIri,
        [
            Iri(RelationTargetIri, RdfType, Jolux + "Act"),
            Iri(RelationTargetIri, TypeDocument, TypeDocumentPrefix + "LOI"),
            Iri(RelationTargetIri, Jolux + "isMemberOf", RelationTargetParentIri),
        ]);

        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            profile.Resolve([source, target]));
        var relation = resolved.Resources.Single(resource =>
            resource.ObjectRef.PublisherUri == RootIri).Relations.Single();
        Assert.IsNotNull(relation.ConsolidatesShape);
        var shape = relation.ConsolidatesShape;

        Assert.AreEqual(LuxembourgRelationSemantic.ConsolidatesShapeRequired, relation.Semantic);
        Assert.AreEqual(LuxembourgRelationDisposition.Accepted, relation.Disposition);
        CollectionAssert.AreEqual(new[] { Jolux + "Act" }, shape.SubjectClasses.ToArray());
        CollectionAssert.AreEqual(
            new[] { TypeDocumentPrefix + "TC" },
            shape.SubjectTypeDocuments.ToArray());
        Assert.AreEqual(LuxembourgSelectorCardinality.Single, shape.SubjectTypeCardinality);
        CollectionAssert.AreEqual(new[] { Jolux + "Act" }, shape.TargetClasses.ToArray());
        CollectionAssert.AreEqual(
            new[] { TypeDocumentPrefix + "LOI" },
            shape.TargetTypeDocuments.ToArray());
        Assert.AreEqual(LuxembourgSelectorCardinality.Single, shape.TargetTypeCardinality);
        Assert.AreEqual(
            LuxembourgConsolidatesDirection.AssertedSubjectToObject,
            shape.Direction);
        Assert.AreEqual(
            LuxembourgConsolidatesShapeState.AcceptedTcToCompatibleAct,
            shape.State);
    }

    private static LuxembourgConsolidatesShape AcceptedShape(
        IReadOnlyList<string> subjectClasses,
        IReadOnlyList<string> targetClasses) => new(
        subjectClasses,
        [TypeDocumentPrefix + "TC"],
        LuxembourgSelectorCardinality.Single,
        targetClasses,
        [TypeDocumentPrefix + "LOI"],
        LuxembourgSelectorCardinality.Single,
        LuxembourgConsolidatesDirection.AssertedSubjectToObject,
        LuxembourgConsolidatesShapeState.AcceptedTcToCompatibleAct);

    private static VerifiedLuxembourgSourceProfile Profile(
        params LuxembourgIriVocabularyValue[] extraVocabulary) =>
        VerifiedLuxembourgSourceProfile.Open(new LuxembourgVocabularySnapshot(
            ObservationRef,
            CompleteEnumerationRef,
            [.. VerifiedLuxembourgSourceProfile.RequiredIriVocabulary, .. extraVocabulary],
            []));

    private static VerifiedLuxembourgSourceProfile ExpectedProfile() =>
        VerifiedLuxembourgSourceProfile.Open(new LuxembourgVocabularySnapshot(
            ObservationRef,
            CompleteEnumerationRef,
            ExpectedVocabulary()
                .Select(static value => new LuxembourgIriVocabularyValue(value.Kind, value.Iri))
                .ToArray(),
            []));

    private static LuxembourgProfileResolution.Resolved Resolve(
        VerifiedLuxembourgSourceProfile profile,
        LuxembourgResourceObservation observation) =>
        Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            profile.Resolve([observation]));

    private static LuxembourgResourceObservation RelationObservation(
        IReadOnlyList<LuxembourgObservedAssertion> assertions) => new(
        ObjectRef(RootIri),
        ObservationRef,
        assertions,
        [new LuxembourgObservedRelation(
            RootIri,
            Jolux + "consolidates",
            RelationTargetIri,
            ObservationRef)],
        new LuxembourgSparqlRightsChannelObservations(
            ObservationRef,
            SparqlEnumerationRef,
            []),
        new LuxembourgInFileRightsChannelObservations(
            ObservationRef,
            InFileEnumerationRef,
            []));

    private static LuxembourgResourceObservation EmptyObservation() => new(
        ObjectRef(RootIri),
        ObservationRef,
        [],
        [],
        new LuxembourgSparqlRightsChannelObservations(
            ObservationRef,
            SparqlEnumerationRef,
            []),
        new LuxembourgInFileRightsChannelObservations(
            ObservationRef,
            InFileEnumerationRef,
            []));

    private static LuxembourgResourceObservation MetadataObservation(
        string publisherIri,
        IReadOnlyList<LuxembourgObservedAssertion> assertions) => new(
        ObjectRef(publisherIri),
        ObservationRef,
        assertions,
        [],
        new LuxembourgSparqlRightsChannelObservations(
            ObservationRef,
            SparqlEnumerationRef,
            []),
        new LuxembourgInFileRightsChannelObservations(
            ObservationRef,
            InFileEnumerationRef,
            []));

    private static LuxembourgResourceObservation BodyObservation(
        IReadOnlyList<LuxembourgRightsChannelObservation> sparqlRights,
        IReadOnlyList<LuxembourgRightsChannelObservation> inFileRights) => new(
        ObjectRef(RootIri),
        ObservationRef,
        [
            Iri(RootIri, RdfType, Jolux + "Act"),
            Iri(RootIri, TypeDocument, TypeDocumentPrefix + "LOI"),
            Iri(RootIri, Jolux + "isMemberOf", RootParentIri),
            Iri(RootIri, Jolux + "isRealizedBy", ExpressionIri),
            Iri(ExpressionIri, RdfType, Jolux + "Expression"),
            Iri(ExpressionIri, Jolux + "language", LanguageFra),
            Iri(ExpressionIri, Jolux + "isEmbodiedBy", ManifestationIri),
            Iri(ManifestationIri, RdfType, Jolux + "Manifestation"),
            Iri(ManifestationIri, Jolux + "userFormat", UserFormatPrefix + "xml"),
            Iri(ManifestationIri, Jolux + "isExemplifiedBy", ItemIri),
        ],
        [],
        new LuxembourgSparqlRightsChannelObservations(
            ObservationRef,
            SparqlEnumerationRef,
            sparqlRights),
        new LuxembourgInFileRightsChannelObservations(
            ObservationRef,
            InFileEnumerationRef,
            inFileRights));

    private static LuxembourgRightsChannelObservation Rights(SourceArtifactRef evidenceRef) => new(
        ManifestationIri,
        ObservationRef,
        evidenceRef,
        ["http://creativecommons.org/licenses/by/4.0/"]);

    private static IReadOnlyList<string> SelectorValues(
        VerifiedLuxembourgSourceProfile profile,
        LuxembourgProfileResolution.Resolved resolution,
        string memberKey)
    {
        var selectorIndex = profile.ScopeBinding.OrderedSelectorMemberOrdinals
            .Select((memberOrdinal, index) => new
            {
                Index = index,
                Key = profile.ScopeBinding.OrderedMembers[memberOrdinal].MemberKey,
            })
            .Single(value => value.Key == memberKey)
            .Index;
        return resolution.ScopeInputs.Single().Selectors[selectorIndex].CanonicalValues;
    }

    private static string[] SelectorKeys(VerifiedLuxembourgSourceProfile profile) =>
        profile.ScopeBinding.OrderedSelectorMemberOrdinals
            .Select(ordinal => profile.ScopeBinding.OrderedMembers[ordinal].MemberKey)
            .ToArray();

    private static string SelectorSignature(
        string selectorKey,
        ScopeSelectorEvidence selector,
        IReadOnlyList<SourceArtifactRef> evidenceArtifacts)
    {
        var evidence = selector.EvidenceArtifactOrdinal is { } evidenceOrdinal
            ? evidenceArtifacts[evidenceOrdinal] == ObservationRef
                ? "observation"
                : "unexpected-artifact"
            : selector.RuleOrdinal is { } ruleOrdinal
                ? $"rule:{ruleOrdinal}"
                : "none";
        var values = selectorKey == "selector.body_join"
            ? NormalizeBodyJoinDigest(selector.CanonicalValues)
            : string.Join(',', selector.CanonicalValues);
        return $"{selectorKey}|{selector.State}|{selector.EvidenceKind?.ToString() ?? "none"}|" +
               $"{evidence}|{values}";
    }

    private static string NormalizeBodyJoinDigest(IReadOnlyList<string> values)
    {
        var value = values.Single();
        Assert.IsTrue(value.StartsWith("body_join_sha256:", StringComparison.Ordinal));
        Assert.AreEqual(64, value["body_join_sha256:".Length..].Length);
        Assert.IsTrue(value["body_join_sha256:".Length..].All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f'));
        return "body_join_sha256:<digest>";
    }

    private static string VocabularySignature(
        (LuxembourgVocabularyKind Kind, string Iri) value) =>
        $"{value.Kind}|{value.Iri}";

    private static string VocabularySignature(LuxembourgIriVocabularyValue value) =>
        $"{value.Kind}|{value.FullIri}";

    private static (LuxembourgVocabularyKind Kind, string Iri)[] ExpectedVocabulary() =>
    [
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "Act"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "Amendment"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "Article"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "Code"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "Collection"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "ComplexWork"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "Consolidation"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "DraftDocument"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "DraftRelatedDocument"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "EUDirective"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "EULegalResource"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "EUReglementation"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "Expression"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "InitialDraft"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "LegalResource"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "LegalResourceImpact"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "Manifestation"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "Memorial"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "OpinionConseilEtat"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "OpinionProfessionalOrganisation"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "PartyConditionToTreaty"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "RatificationRestriction"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "TaskForTreaty"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "TransmissionOfSignedInstrument"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "TreatyDocument"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "TreatyProcess"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "TreatySignature"),
        (LuxembourgVocabularyKind.ResourceClass, Jolux + "Work"),
        (LuxembourgVocabularyKind.ResourceClass, "http://www.w3.org/ns/prov#Entity"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "A"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "ACC"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "ACCA"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "AGC"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "AGD"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "AMIN"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "ARGD"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "CODE"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "CODE_RECUEIL"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "CONV"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "Constitution"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "DIV"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "LOI"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "ORD"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "PA"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "PROT"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "RBCL"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "RC"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "RCSF"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "RECT"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "RECUEIL"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "REG"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "RGC"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "RGD"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "RI"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "RILR"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "RMIN"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "ST"),
        (LuxembourgVocabularyKind.TypeDocument, TypeDocumentPrefix + "TC"),
        (LuxembourgVocabularyKind.UserFormat, UserFormatPrefix + "doc"),
        (LuxembourgVocabularyKind.UserFormat, UserFormatPrefix + "docx"),
        (LuxembourgVocabularyKind.UserFormat, UserFormatPrefix + "html"),
        (LuxembourgVocabularyKind.UserFormat, UserFormatPrefix + "jpeg"),
        (LuxembourgVocabularyKind.UserFormat, UserFormatPrefix + "jpg"),
        (LuxembourgVocabularyKind.UserFormat, UserFormatPrefix + "pdf"),
        (LuxembourgVocabularyKind.UserFormat, UserFormatPrefix + "pdfa"),
        (LuxembourgVocabularyKind.UserFormat, UserFormatPrefix + "xls"),
        (LuxembourgVocabularyKind.UserFormat, UserFormatPrefix + "xlsx"),
        (LuxembourgVocabularyKind.UserFormat, UserFormatPrefix + "xml"),
        (LuxembourgVocabularyKind.UserFormat, UserFormatPrefix + "xml-akomantoso"),
        (LuxembourgVocabularyKind.UserFormat, UserFormatPrefix + "xml-lux"),
        (LuxembourgVocabularyKind.UserFormat, UserFormatPrefix + "zip"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "basedOn"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "basicAct"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "cites"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "consolidates"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "hasIndirectImpact"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "impactConsolidatedBy"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "impactConsolidatedByExpression"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "impactFromLegalResource"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "impactToExpression"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "impactToLegalResource"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "legalAnalysisHasLegalResourceImpact"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "legalResourceImpactHasDateEntryInForce"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "legalResourceImpactHasType"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "modifiedTempBy"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "modifies"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "rectifies"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "repeals"),
        (LuxembourgVocabularyKind.RelationPredicate, Jolux + "transposes"),
        (LuxembourgVocabularyKind.Language, LanguagePrefix + "DEU"),
        (LuxembourgVocabularyKind.Language, LanguagePrefix + "ENG"),
        (LuxembourgVocabularyKind.Language, LanguagePrefix + "FRA"),
        (LuxembourgVocabularyKind.Language, LanguagePrefix + "LTZ"),
        (LuxembourgVocabularyKind.LegalValue, LegalValuePrefix + "definitif"),
        (LuxembourgVocabularyKind.LegalValue, LegalValuePrefix + "non-officiel"),
        (LuxembourgVocabularyKind.LegalValue, LegalValuePrefix + "officiel"),
        (LuxembourgVocabularyKind.Licence, "http://creativecommons.org/licenses/by/4.0/"),
        (LuxembourgVocabularyKind.Licence,
            "http://data.legilux.public.lu/resource/authority/license/licenceSCL"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "dateApplicability"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "dateDocument"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "dateEndApplicability"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "dateEntryInForce"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "dateNoLongerInForce"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "historicalLegalId"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "inForceStatus"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "isEmbodiedBy"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "isExemplifiedBy"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "isMemberOf"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "isPartOf"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "isRealizedBy"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "language"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "legalValue"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "license"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "previousIsExemplifiedBy"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "publicationDate"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "publisher"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "responsibilityOf"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "rights"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "rightsHolder"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "title"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "titleShort"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "typeDocument"),
        (LuxembourgVocabularyKind.AssertionPredicate, Jolux + "userFormat"),
        (LuxembourgVocabularyKind.AssertionPredicate, RdfType),
    ];

    private static LuxembourgObservedAssertion Iri(
        string subject,
        string predicate,
        string value) => new(
        subject,
        predicate,
        LuxembourgAssertionObjectKind.Iri,
        value,
        string.Empty,
        string.Empty,
        ObservationRef);

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

    private static SourceArtifactRef Artifact(string id, char digestCharacter) => new(
        "urn:uuid:" + id,
        new string(digestCharacter, 64));

    private const string Jolux =
        "http://data.legilux.public.lu/resource/ontology/jolux#";
    private const string RdfType =
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string TypeDocument = Jolux + "typeDocument";
    private const string TypeDocumentPrefix =
        "http://data.legilux.public.lu/resource/authority/resource-type/";
    private const string UserFormatPrefix =
        "http://data.legilux.public.lu/resource/authority/user-format/";
    private const string LanguagePrefix =
        "http://publications.europa.eu/resource/authority/language/";
    private const string LegalValuePrefix =
        "http://data.legilux.public.lu/resource/authority/statut-version/";
    private const string RootParentIri =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1";
    private const string RootIri = RootParentIri + "/jo";
    private const string RelationTargetIri =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2025/01/01/a2/jo";
    private const string RelationTargetParentIri =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2025/01/01/a2";
    private const string ExpressionIri = RootIri + "/fr";
    private const string ManifestationIri = ExpressionIri + "/xml";
    private const string ItemIri =
        "http://data.legilux.public.lu/filestore/body-fr.xml";
    private const string LanguageFra =
        "http://publications.europa.eu/resource/authority/language/FRA";

    private static SourceArtifactRef ObservationRef { get; } = Artifact(
        "10dd0a6e-3fa4-468d-a2aa-570a93ec4bf0",
        '1');
    private static SourceArtifactRef CompleteEnumerationRef { get; } = Artifact(
        "3f60c78d-6e8a-4208-9146-43b634db9bbc",
        '2');
    private static SourceArtifactRef SparqlEnumerationRef { get; } = Artifact(
        "7163031e-a002-4fa2-af00-868b92d77f54",
        '3');
    private static SourceArtifactRef InFileEnumerationRef { get; } = Artifact(
        "e7a24ee4-bb66-4352-8d35-edb8a3664526",
        '4');
    private static SourceArtifactRef SharedRightsEvidenceRef { get; } = Artifact(
        "5be675e4-64b4-42df-aae4-4f2da91a76d4",
        '5');
    private static SourceArtifactRef EntityRegistryRef { get; } = Artifact(
        "760b560c-15c2-407d-b38f-f99f4c59e345",
        '6');
    private static SourceArtifactRef IdentityProfileRef { get; } = Artifact(
        "54b9c06f-ed04-4d07-8239-72dce5fed499",
        '7');

    private static readonly string[] ExpectedSelectorKeys =
    [
        "selector.record",
        "selector.relation",
        "selector.supporting_document",
        "selector.publication_family",
        "selector.language",
        "selector.format",
        "selector.authenticity",
        "selector.body_join",
        "selector.rights_sparql",
        "selector.rights_in_file",
        "selector.transport_uri",
        "selector.transport_robots",
        "selector.transport_http",
    ];

    private sealed class AcceptingEvidenceResolver(SourceArtifactRef completeEnumerationRef)
        : IScopeReductionEvidenceResolver
    {
        public SourceArtifactRef CompleteEnumerationRef { get; } = completeEnumerationRef;

        public List<ScopeCompleteEnumerationBinding> CompleteEnumerationBindings { get; } = [];

        public List<ScopeSelectorObservationBinding> SelectorObservationBindings { get; } = [];

        public List<ScopeSelectorNotApplicableBinding> SelectorNotApplicableBindings { get; } = [];

        public List<ScopeRuleEvaluationBinding> RuleEvaluationBindings { get; } = [];

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding)
        {
            SelectorObservationBindings.Add(binding);
            return true;
        }

        public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding)
        {
            SelectorNotApplicableBindings.Add(binding);
            return true;
        }

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding)
        {
            RuleEvaluationBindings.Add(binding);
            return true;
        }

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding)
        {
            CompleteEnumerationBindings.Add(binding);
            return true;
        }
    }
}
