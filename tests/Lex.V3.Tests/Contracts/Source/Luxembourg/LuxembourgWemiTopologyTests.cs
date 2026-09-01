using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

[TestClass]
public sealed class LuxembourgWemiTopologyTests
{
    [TestMethod]
    public void PreservesEveryExplicitLanguageAndFormatCandidate()
    {
        var assertions = new[]
        {
            Type(Root, "Act"),
            Iri(Root, IsRealizedBy, ExpressionFr),
            Iri(Root, IsRealizedBy, ExpressionDe),
            Type(ExpressionFr, "Expression"),
            Iri(ExpressionFr, Language, LanguageFra),
            Iri(ExpressionFr, IsEmbodiedBy, ManifestationFrXml),
            Iri(ExpressionFr, IsEmbodiedBy, ManifestationFrPdf),
            Type(ExpressionDe, "Expression"),
            Iri(ExpressionDe, Language, LanguageDeu),
            Iri(ExpressionDe, IsEmbodiedBy, ManifestationDeXml),
            Type(ManifestationFrXml, "Manifestation"),
            Iri(ManifestationFrXml, UserFormat, FormatXml),
            Iri(ManifestationFrXml, IsExemplifiedBy, ItemFrXml),
            Type(ManifestationFrPdf, "Manifestation"),
            Iri(ManifestationFrPdf, UserFormat, FormatPdf),
            Iri(ManifestationFrPdf, IsExemplifiedBy, ItemFrPdf),
            Type(ManifestationDeXml, "Manifestation"),
            Iri(ManifestationDeXml, UserFormat, FormatXml),
            Iri(ManifestationDeXml, IsExemplifiedBy, ItemDeXml),
        };

        var result = LuxembourgWemiTopology.Resolve(Root, assertions, ObservationRef);

        CollectionAssert.AreEqual(
            new[]
            {
                $"{LanguageDeu}|{FormatXml}|{ExpressionDe}|{ManifestationDeXml}|{ItemDeXml}",
                $"{LanguageFra}|{FormatPdf}|{ExpressionFr}|{ManifestationFrPdf}|{ItemFrPdf}",
                $"{LanguageFra}|{FormatXml}|{ExpressionFr}|{ManifestationFrXml}|{ItemFrXml}",
            },
            result.Candidates.Select(Signature).ToArray());
        Assert.IsTrue(result.Candidates.All(candidate =>
            candidate.Disposition ==
            LuxembourgWemiCandidateDisposition.StructurallyConsistent));
        Assert.HasCount(0, result.Blockers);
    }

    [TestMethod]
    public void IsExemplifiedByNeverSubstitutesForIsEmbodiedBy()
    {
        var result = LuxembourgWemiTopology.Resolve(
            Root,
            [
                Type(Root, "Act"),
                Iri(Root, IsRealizedBy, ExpressionFr),
                Type(ExpressionFr, "Expression"),
                Iri(ExpressionFr, Language, LanguageFra),
                Iri(ExpressionFr, IsExemplifiedBy, ManifestationFrXml),
                Type(ManifestationFrXml, "Manifestation"),
            Iri(ManifestationFrXml, UserFormat, FormatXml),
            Iri(ManifestationFrXml, IsExemplifiedBy, ItemFrXml),
            ],
            ObservationRef);

        Assert.HasCount(0, result.Candidates);
        Assert.IsTrue(result.Blockers.Any(blocker =>
            blocker.Code == LuxembourgWemiBlockerCode.EmbodimentMissing &&
            blocker.SubjectIri == ExpressionFr));
    }

    [TestMethod]
    public void UriShapeNeverCreatesAnUnassertedPath()
    {
        var result = LuxembourgWemiTopology.Resolve(
            Root,
            [
                Type(Root, "Act"),
                Type(ExpressionFr, "Expression"),
                Iri(ExpressionFr, Language, LanguageFra),
                Type(ManifestationFrXml, "Manifestation"),
                Iri(ManifestationFrXml, UserFormat, FormatXml),
            ],
            ObservationRef);

        Assert.HasCount(0, result.Candidates);
        Assert.IsTrue(result.Blockers.Any(blocker =>
            blocker.Code == LuxembourgWemiBlockerCode.RealizationMissing));
    }

    [TestMethod]
    public void AssertionsFromAnotherObservationCannotCompleteThePath()
    {
        var assertions = ExactPath()
            .Select(assertion => new LuxembourgObservedAssertion(
                assertion.SubjectIri,
                assertion.PredicateIri,
                assertion.ObjectKind,
                assertion.ObjectIriOrLexical,
                assertion.DatatypeIriOrEmpty,
                assertion.LanguageTagOrEmpty,
                OtherObservationRef))
            .ToArray();

        var result = LuxembourgWemiTopology.Resolve(Root, assertions, ObservationRef);

        Assert.HasCount(0, result.Candidates);
        Assert.IsTrue(result.Blockers.Any(blocker =>
            blocker.Code == LuxembourgWemiBlockerCode.ObservationMismatch));
        Assert.IsTrue(result.Blockers.Any(blocker =>
            blocker.Code == LuxembourgWemiBlockerCode.RootTypeMissing));
        Assert.IsTrue(result.Blockers.Any(blocker =>
            blocker.Code == LuxembourgWemiBlockerCode.RealizationMissing));
    }

    [TestMethod]
    public void EveryTraversedNodeRequiresItsOwnExactType()
    {
        var result = LuxembourgWemiTopology.Resolve(
            Root,
            [
                Type(Root, "Act"),
                Iri(Root, IsRealizedBy, ExpressionFr),
                Type(ExpressionFr, "Manifestation"),
                Iri(ExpressionFr, Language, LanguageFra),
                Iri(ExpressionFr, IsEmbodiedBy, ManifestationFrXml),
                Type(ManifestationFrXml, "Expression"),
                Iri(ManifestationFrXml, UserFormat, FormatXml),
                Iri(ManifestationFrXml, IsExemplifiedBy, ItemFrXml),
            ],
            ObservationRef);

        var candidate = result.Candidates.Single();
        Assert.AreEqual(
            LuxembourgWemiCandidateDisposition.TypedQuarantine,
            candidate.Disposition);
        CollectionAssert.AreEqual(
            new[]
            {
                LuxembourgWemiBlockerCode.ExpressionTypeMismatch,
                LuxembourgWemiBlockerCode.ManifestationTypeMismatch,
            },
            candidate.BlockerCodes.ToArray());
    }

    [TestMethod]
    public void ConflictIsLimitedToOneExactCoordinate()
    {
        var expressionTwo = Root + "/fr-alt";
        var manifestationTwo = expressionTwo + "/xml";
        var result = LuxembourgWemiTopology.Resolve(
            Root,
            [
                .. ExactPath(),
                Iri(Root, IsRealizedBy, expressionTwo),
                Type(expressionTwo, "Expression"),
                Iri(expressionTwo, Language, LanguageFra),
                Iri(expressionTwo, IsEmbodiedBy, manifestationTwo),
                Type(manifestationTwo, "Manifestation"),
                Iri(manifestationTwo, UserFormat, FormatXml),
                Iri(
                    manifestationTwo,
                    IsExemplifiedBy,
                    "http://data.legilux.public.lu/filestore/body-fr-alt.xml"),
                Iri(Root, IsRealizedBy, ExpressionDe),
                Type(ExpressionDe, "Expression"),
                Iri(ExpressionDe, Language, LanguageDeu),
                Iri(ExpressionDe, IsEmbodiedBy, ManifestationDeXml),
                Type(ManifestationDeXml, "Manifestation"),
                Iri(ManifestationDeXml, UserFormat, FormatXml),
                Iri(ManifestationDeXml, IsExemplifiedBy, ItemDeXml),
            ],
            ObservationRef);

        var french = result.Candidates.Where(candidate =>
            candidate.LanguageIri == LanguageFra && candidate.FormatIri == FormatXml).ToArray();
        var german = result.Candidates.Single(candidate =>
            candidate.LanguageIri == LanguageDeu && candidate.FormatIri == FormatXml);

        Assert.HasCount(2, french);
        Assert.IsTrue(french.All(candidate =>
            candidate.Disposition == LuxembourgWemiCandidateDisposition.TypedQuarantine &&
            candidate.BlockerCodes.Contains(LuxembourgWemiBlockerCode.CoordinateConflict)));
        Assert.AreEqual(
            LuxembourgWemiCandidateDisposition.StructurallyConsistent,
            german.Disposition);
        Assert.AreEqual(
            1,
            result.Blockers.Count(blocker =>
                blocker.Code == LuxembourgWemiBlockerCode.CoordinateConflict));
    }

    [TestMethod]
    public void RelevantLiteralObjectsAreBlockedAndCannotCreateEdges()
    {
        var result = LuxembourgWemiTopology.Resolve(
            Root,
            [
                Type(Root, "Act"),
                Literal(Root, IsRealizedBy, ExpressionFr),
            ],
            ObservationRef);

        CollectionAssert.IsSubsetOf(
            new[]
            {
                LuxembourgWemiBlockerCode.RealizationObjectInvalid,
                LuxembourgWemiBlockerCode.RealizationMissing,
            },
            result.Blockers.Select(blocker => blocker.Code).ToArray());
    }

    [TestMethod]
    public void IriTermsWithLiteralMetadataCannotCompleteAnyWemiStep()
    {
        const string xsdString = "http://www.w3.org/2001/XMLSchema#string";
        var cases = new[]
        {
            (
                Assertions: ExactPath()
                    .Where(assertion =>
                        assertion.SubjectIri != Root || assertion.PredicateIri != RdfType)
                    .Append(MalformedIri(Root, RdfType, Jolux + "Act", xsdString))
                    .ToArray(),
                Blocker: LuxembourgWemiBlockerCode.RootTypeObjectInvalid),
            (
                Assertions: ExactPath()
                    .Where(assertion => assertion.PredicateIri != IsRealizedBy)
                    .Append(MalformedIri(Root, IsRealizedBy, ExpressionFr, xsdString))
                    .ToArray(),
                Blocker: LuxembourgWemiBlockerCode.RealizationObjectInvalid),
            (
                Assertions: ExactPath()
                    .Where(assertion => assertion.PredicateIri != IsEmbodiedBy)
                    .Append(MalformedIri(
                        ExpressionFr,
                        IsEmbodiedBy,
                        ManifestationFrXml,
                        xsdString))
                    .ToArray(),
                Blocker: LuxembourgWemiBlockerCode.EmbodimentObjectInvalid),
            (
                Assertions: ExactPath()
                    .Where(assertion => assertion.PredicateIri != IsExemplifiedBy)
                    .Append(MalformedIri(
                        ManifestationFrXml,
                        IsExemplifiedBy,
                        ItemFrXml,
                        xsdString))
                    .ToArray(),
                Blocker: LuxembourgWemiBlockerCode.ManifestationItemObjectInvalid),
        };

        foreach (var hostileCase in cases)
        {
            var result = LuxembourgWemiTopology.Resolve(
                Root,
                hostileCase.Assertions,
                ObservationRef);

            Assert.IsFalse(result.Candidates.Any(candidate =>
                candidate.Disposition ==
                LuxembourgWemiCandidateDisposition.StructurallyConsistent));
            Assert.IsTrue(result.Blockers.Any(blocker =>
                blocker.Code == hostileCase.Blocker));
        }
    }

    [TestMethod]
    public void InvalidRealizationSiblingQuarantinesValidRootCandidates()
    {
        var result = LuxembourgWemiTopology.Resolve(
            Root,
            [.. ExactPath(), Literal(Root, IsRealizedBy, Root + "/invalid")],
            ObservationRef);

        var candidate = result.Candidates.Single();
        Assert.AreEqual(
            LuxembourgWemiCandidateDisposition.TypedQuarantine,
            candidate.Disposition);
        CollectionAssert.Contains(
            candidate.BlockerCodes.ToArray(),
            LuxembourgWemiBlockerCode.RealizationObjectInvalid);
    }

    [TestMethod]
    public void InvalidEmbodimentSiblingQuarantinesTheOtherwiseValidTuple()
    {
        var result = LuxembourgWemiTopology.Resolve(
            Root,
            [.. ExactPath(), Literal(ExpressionFr, IsEmbodiedBy, ManifestationFrPdf)],
            ObservationRef);

        var candidate = result.Candidates.Single();
        Assert.AreEqual(
            LuxembourgWemiCandidateDisposition.TypedQuarantine,
            candidate.Disposition);
        CollectionAssert.Contains(
            candidate.BlockerCodes.ToArray(),
            LuxembourgWemiBlockerCode.EmbodimentObjectInvalid);
    }

    [TestMethod]
    public void AnotherObservationContaminatesTheOtherwiseValidTuple()
    {
        var otherObservationAssertion = new LuxembourgObservedAssertion(
            Root,
            IsRealizedBy,
            LuxembourgAssertionObjectKind.Iri,
            ExpressionDe,
            string.Empty,
            string.Empty,
            OtherObservationRef);

        var result = LuxembourgWemiTopology.Resolve(
            Root,
            [.. ExactPath(), otherObservationAssertion],
            ObservationRef);

        var candidate = result.Candidates.Single();
        Assert.AreEqual(
            LuxembourgWemiCandidateDisposition.TypedQuarantine,
            candidate.Disposition);
        CollectionAssert.Contains(
            candidate.BlockerCodes.ToArray(),
            LuxembourgWemiBlockerCode.ObservationMismatch);
    }

    [TestMethod]
    public void InvalidFormatSiblingQuarantinesValidManifestationCandidate()
    {
        var result = LuxembourgWemiTopology.Resolve(
            Root,
            [.. ExactPath(), Literal(ManifestationFrXml, UserFormat, "xml")],
            ObservationRef);

        var candidate = result.Candidates.Single();
        Assert.AreEqual(
            LuxembourgWemiCandidateDisposition.TypedQuarantine,
            candidate.Disposition);
        CollectionAssert.Contains(
            candidate.BlockerCodes.ToArray(),
            LuxembourgWemiBlockerCode.ManifestationFormatObjectInvalid);
    }

    [TestMethod]
    public void OneExpressionWithTwoLanguagesIsASelectorConflict()
    {
        var result = LuxembourgWemiTopology.Resolve(
            Root,
            [.. ExactPath(), Iri(ExpressionFr, Language, LanguageDeu)],
            ObservationRef);

        Assert.HasCount(2, result.Candidates);
        Assert.IsTrue(result.Candidates.All(candidate =>
            candidate.Disposition == LuxembourgWemiCandidateDisposition.TypedQuarantine &&
            candidate.BlockerCodes.Contains(
                LuxembourgWemiBlockerCode.ExpressionLanguageConflict)));
        Assert.IsFalse(result.Blockers.Any(blocker =>
            blocker.Code == LuxembourgWemiBlockerCode.CoordinateConflict));
    }

    [TestMethod]
    public void OneManifestationWithTwoFormatsIsASelectorConflict()
    {
        var result = LuxembourgWemiTopology.Resolve(
            Root,
            [.. ExactPath(), Iri(ManifestationFrXml, UserFormat, FormatPdf)],
            ObservationRef);

        Assert.HasCount(2, result.Candidates);
        Assert.IsTrue(result.Candidates.All(candidate =>
            candidate.Disposition == LuxembourgWemiCandidateDisposition.TypedQuarantine &&
            candidate.BlockerCodes.Contains(
                LuxembourgWemiBlockerCode.ManifestationFormatConflict)));
        Assert.IsFalse(result.Blockers.Any(blocker =>
            blocker.Code == LuxembourgWemiBlockerCode.CoordinateConflict));
    }

    [TestMethod]
    public void CurrentItemCompletesTheTupleAndPreviousItemRemainsPointOnlyEvidence()
    {
        var result = LuxembourgWemiTopology.Resolve(
            Root,
            [.. ExactPath(), Iri(ManifestationFrXml, PreviousIsExemplifiedBy, PreviousItem)],
            ObservationRef);

        Assert.AreEqual(ItemFrXml, result.Candidates.Single().ItemIri);
        var previous = result.PreviousItems.Single();
        Assert.AreEqual(ManifestationFrXml, previous.ManifestationIri);
        Assert.AreEqual(PreviousItem, previous.ItemIri);
        Assert.AreEqual(
            LuxembourgPreviousItemDisposition.PointReplacedFile,
            previous.Disposition);
    }

    [TestMethod]
    public void PreviousItemCannotSubstituteForTheCurrentFilestoreItem()
    {
        var assertions = ExactPath()
            .Where(assertion => assertion.PredicateIri != IsExemplifiedBy)
            .Append(Iri(ManifestationFrXml, PreviousIsExemplifiedBy, PreviousItem))
            .ToArray();

        var result = LuxembourgWemiTopology.Resolve(Root, assertions, ObservationRef);

        Assert.HasCount(0, result.Candidates);
        Assert.HasCount(1, result.PreviousItems);
        Assert.IsTrue(result.Blockers.Any(blocker =>
            blocker.Code == LuxembourgWemiBlockerCode.ManifestationItemMissing));
    }

    [TestMethod]
    public void PreviousItemOnAnUnrelatedTypedManifestationIsNotBoundToThisRoot()
    {
        var unrelatedManifestation = Root + "/unrelated/xml";
        var result = LuxembourgWemiTopology.Resolve(
            Root,
            [
                .. ExactPath(),
                Type(unrelatedManifestation, "Manifestation"),
                Iri(unrelatedManifestation, PreviousIsExemplifiedBy, PreviousItem),
            ],
            ObservationRef);

        var previous = result.PreviousItems.Single();
        Assert.AreEqual(
            LuxembourgPreviousItemDisposition.TypedQuarantineManifestationUnproven,
            previous.Disposition);
    }

    [TestMethod]
    public void DataHostFileFamilyCannotActAsTheCurrentFilestoreItem()
    {
        var assertions = ExactPath()
            .Where(assertion => assertion.PredicateIri != IsExemplifiedBy)
            .Append(Iri(ManifestationFrXml, IsExemplifiedBy, PreviousItem))
            .ToArray();

        var result = LuxembourgWemiTopology.Resolve(Root, assertions, ObservationRef);

        var candidate = result.Candidates.Single();
        Assert.AreEqual(
            LuxembourgWemiCandidateDisposition.TypedQuarantine,
            candidate.Disposition);
        CollectionAssert.Contains(
            candidate.BlockerCodes.ToArray(),
            LuxembourgWemiBlockerCode.ManifestationItemUriFamilyNotAdmitted);
    }

    [TestMethod]
    public void MultipleCurrentItemsAreRetainedAndQuarantinedWithoutFirstWins()
    {
        const string secondItem =
            "http://data.legilux.public.lu/filestore/body-z.xml";
        var result = LuxembourgWemiTopology.Resolve(
            Root,
            [.. ExactPath(), Iri(ManifestationFrXml, IsExemplifiedBy, secondItem)],
            ObservationRef);

        Assert.HasCount(2, result.Candidates);
        CollectionAssert.AreEqual(
            new[] { ItemFrXml, secondItem },
            result.Candidates.Select(candidate => candidate.ItemIri).ToArray());
        Assert.IsTrue(result.Candidates.All(candidate =>
            candidate.Disposition == LuxembourgWemiCandidateDisposition.TypedQuarantine &&
            candidate.BlockerCodes.Contains(
                LuxembourgWemiBlockerCode.ManifestationItemConflict)));
    }

    [TestMethod]
    public void InvalidCurrentItemSiblingQuarantinesButDoesNotEraseTheValidTuple()
    {
        var result = LuxembourgWemiTopology.Resolve(
            Root,
            [.. ExactPath(), Literal(ManifestationFrXml, IsExemplifiedBy, "not-an-item")],
            ObservationRef);

        var candidate = result.Candidates.Single();
        Assert.AreEqual(ItemFrXml, candidate.ItemIri);
        Assert.AreEqual(
            LuxembourgWemiCandidateDisposition.TypedQuarantine,
            candidate.Disposition);
        CollectionAssert.Contains(
            candidate.BlockerCodes.ToArray(),
            LuxembourgWemiBlockerCode.ManifestationItemObjectInvalid);
    }

    [TestMethod]
    public void CandidateConstructorCannotContradictTheNormalizedItemFamily()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgWemiCandidate(
            Root,
            ExpressionFr,
            ManifestationFrXml,
            PreviousItem,
            LanguageFra,
            FormatXml,
            ObservationRef,
            LuxembourgWemiCandidateDisposition.TypedQuarantine,
            [LuxembourgWemiBlockerCode.ManifestationTypeMismatch]));
        Assert.ThrowsExactly<ArgumentException>(() => new LuxembourgWemiCandidate(
            Root,
            ExpressionFr,
            ManifestationFrXml,
            ItemFrXml,
            LanguageFra,
            FormatXml,
            ObservationRef,
            LuxembourgWemiCandidateDisposition.TypedQuarantine,
            [LuxembourgWemiBlockerCode.ManifestationItemUriFamilyNotAdmitted]));
    }

    [TestMethod]
    [DataRow("http://data.legilux.public.lu/filestore/../file/replaced.xml")]
    [DataRow("http://data.legilux.public.lu/filestore/%2e%2e/file/replaced.xml")]
    public void NormalizedTraversalCannotForgeTheCurrentFilestoreFamily(string hostileItem)
    {
        var assertions = ExactPath()
            .Where(assertion => assertion.PredicateIri != IsExemplifiedBy)
            .Append(Iri(ManifestationFrXml, IsExemplifiedBy, hostileItem))
            .ToArray();

        var result = LuxembourgWemiTopology.Resolve(Root, assertions, ObservationRef);

        var candidate = result.Candidates.Single();
        Assert.AreEqual(
            LuxembourgWemiCandidateDisposition.TypedQuarantine,
            candidate.Disposition);
        CollectionAssert.Contains(
            candidate.BlockerCodes.ToArray(),
            LuxembourgWemiBlockerCode.ManifestationItemUriFamilyNotAdmitted);
    }

    [TestMethod]
    public void NormalizedTraversalCannotForgeThePreviousFileFamily()
    {
        const string hostilePrevious =
            "http://data.legilux.public.lu/file/../filestore/body.xml";
        var result = LuxembourgWemiTopology.Resolve(
            Root,
            [.. ExactPath(), Iri(ManifestationFrXml, PreviousIsExemplifiedBy, hostilePrevious)],
            ObservationRef);

        Assert.AreEqual(
            LuxembourgPreviousItemDisposition.TypedQuarantineUnruledUriFamily,
            result.PreviousItems.Single().Disposition);
    }

    [TestMethod]
    public void OutputAndOrderingDoNotDependOnMutableInputOrAssertionOrder()
    {
        var assertions = ExactPath().ToList();
        var first = LuxembourgWemiTopology.Resolve(Root, assertions, ObservationRef);
        assertions.Clear();
        var second = LuxembourgWemiTopology.Resolve(
            Root,
            ExactPath().Reverse().Concat(ExactPath()).ToArray(),
            ObservationRef);

        Assert.HasCount(1, first.Candidates);
        CollectionAssert.AreEqual(
            first.Candidates.Select(Signature).ToArray(),
            second.Candidates.Select(Signature).ToArray());
        CollectionAssert.AreEqual(
            first.Blockers.Select(BlockerSignature).ToArray(),
            second.Blockers.Select(BlockerSignature).ToArray());
    }

    private static LuxembourgObservedAssertion[] ExactPath() =>
    [
        Type(Root, "Act"),
        Iri(Root, IsRealizedBy, ExpressionFr),
        Type(ExpressionFr, "Expression"),
        Iri(ExpressionFr, Language, LanguageFra),
        Iri(ExpressionFr, IsEmbodiedBy, ManifestationFrXml),
        Type(ManifestationFrXml, "Manifestation"),
        Iri(ManifestationFrXml, UserFormat, FormatXml),
        Iri(ManifestationFrXml, IsExemplifiedBy, ItemFrXml),
    ];

    private static LuxembourgObservedAssertion Type(string subject, string localName) =>
        Iri(subject, RdfType, Jolux + localName);

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

    private static LuxembourgObservedAssertion MalformedIri(
        string subject,
        string predicate,
        string value,
        string datatypeIri) => new(
        subject,
        predicate,
        LuxembourgAssertionObjectKind.Iri,
        value,
        datatypeIri,
        string.Empty,
        ObservationRef);

    private static LuxembourgObservedAssertion Literal(
        string subject,
        string predicate,
        string value) => new(
        subject,
        predicate,
        LuxembourgAssertionObjectKind.Literal,
        value,
        "http://www.w3.org/2001/XMLSchema#string",
        string.Empty,
        ObservationRef);

    private static string Signature(LuxembourgWemiCandidate candidate) =>
        $"{candidate.LanguageIri}|{candidate.FormatIri}|{candidate.ExpressionIri}|" +
        $"{candidate.ManifestationIri}|{candidate.ItemIri}";

    private static string BlockerSignature(LuxembourgWemiBlocker blocker) =>
        $"{(int)blocker.Code}|{blocker.SubjectIri}|{blocker.PredicateIri}|" +
        $"{blocker.ObjectIriOrEmpty}|{blocker.LanguageIriOrEmpty}|{blocker.FormatIriOrEmpty}";

    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";
    private const string RdfType =
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string IsRealizedBy = Jolux + "isRealizedBy";
    private const string IsEmbodiedBy = Jolux + "isEmbodiedBy";
    private const string IsExemplifiedBy = Jolux + "isExemplifiedBy";
    private const string PreviousIsExemplifiedBy = Jolux + "previousIsExemplifiedBy";
    private const string Language = Jolux + "language";
    private const string UserFormat = Jolux + "userFormat";
    private const string Root =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo";
    private const string ExpressionFr = Root + "/fr";
    private const string ExpressionDe = Root + "/de";
    private const string ManifestationFrXml = ExpressionFr + "/xml";
    private const string ManifestationFrPdf = ExpressionFr + "/pdf";
    private const string ManifestationDeXml = ExpressionDe + "/xml";
    private const string ItemFrXml =
        "http://data.legilux.public.lu/filestore/body-fr.xml";
    private const string ItemFrPdf =
        "http://data.legilux.public.lu/filestore/body-fr.pdf";
    private const string ItemDeXml =
        "http://data.legilux.public.lu/filestore/body-de.xml";
    private const string PreviousItem =
        "http://data.legilux.public.lu/file/replaced-body.xml";
    private const string LanguageFra =
        "http://publications.europa.eu/resource/authority/language/FRA";
    private const string LanguageDeu =
        "http://publications.europa.eu/resource/authority/language/DEU";
    private const string FormatXml =
        "http://data.legilux.public.lu/resource/authority/user-format/xml";
    private const string FormatPdf =
        "http://data.legilux.public.lu/resource/authority/user-format/pdf";

    private static SourceArtifactRef ObservationRef { get; } = new(
        "urn:uuid:10dd0a6e-3fa4-468d-a2aa-570a93ec4bf0",
        new string('1', 64));

    private static SourceArtifactRef OtherObservationRef { get; } = new(
        "urn:uuid:a796278c-f25b-4c55-a4b1-42ee7ef1c345",
        new string('8', 64));
}
