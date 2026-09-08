using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// D1-05g/A2: family A's decode, from acquired reification rows into the accepted
/// <see cref="EuDateAxiomBinding"/> surface.
/// </summary>
/// <remarks>
/// <para>
/// What here is measured and what is constructed, said plainly rather than left to be assumed.
/// Measured on the family-A single-seed run recorded on issue #410: the axiom node arrives as a
/// skolemised <c>.well-known/genid/&lt;work&gt;/&lt;axiom&gt;</c> IRI rather than a blank node; the
/// fd_335 carrier arrives brace-delimited and pipe-separated as
/// <c>{EV|http://publications.europa.eu/resource/authority/fd_335/EV}</c>; and
/// <c>annotation#type_of_date</c> was present on only two of that seed's three axioms, which is why
/// the no-qualifier path below is an ordinary delivered shape and not an error case. The work IRI is
/// 32003L0088's real Cellar identity, already pinned by the family-M tests.
/// </para>
/// <para>
/// Constructed, and labelled as such wherever it appears: every malformed variant. A publisher that
/// emits a target IRI where a literal belongs, or a carrier whose authority names a different
/// concept from its code, has not been observed. These fixtures exist to prove each typed refusal is
/// reachable rather than decorative, which is a claim about this decoder, not about the publisher.
/// </para>
/// <para>
/// Contracts-only: nothing here calls a store or a publisher endpoint.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuReifiedAxiomDecodeTests
{
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
    private const string XsdGYear = "http://www.w3.org/2001/XMLSchema#gYear";
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    // 32003L0088, the Working Time Directive: a real Appendix A seed's Cellar work identity.
    private const string Work =
        "http://publications.europa.eu/resource/cellar/050dd964-4f94-4c61-ab50-89217a0d90e2";

    // The skolemised shape family A measured, with this file's own suffix so it names a fixture.
    private const string Axiom =
        "http://publications.europa.eu/.well-known/genid/050dd964/a2decode-1";

    private const string TypeOfDate = EuAmendmentRelationVocabulary.AnnotationNamespace + "type_of_date";
    private const string CommentOnDate = EuAmendmentRelationVocabulary.AnnotationNamespace + "comment_on_date";
    private const string Fd335 = "http://publications.europa.eu/resource/authority/fd_335/";

    private const string Observation = "eu-axiom-facts/32003L0088/1";

    private static readonly RepeatedEnumerationInterpretationProfile AxiomProfile =
        EuObjectFactsDiscoveryPlan.Create().CreateDeliveryProfile(EuObjectFactsQuerySet.ReifiedAxiomFacts);

    private static RepeatedEnumerationRow Row(string predicateIri, RepeatedEnumerationRdfTerm value) =>
        Row(Axiom, predicateIri, value);

    private static RepeatedEnumerationRow Row(
        string axiomIri, string predicateIri, RepeatedEnumerationRdfTerm value) =>
        Row(RepeatedEnumerationRdfTerm.Iri(axiomIri), RepeatedEnumerationRdfTerm.Iri(predicateIri), value);

    /// <summary>
    /// A row whose projected <c>value_kind</c> marker says <paramref name="markerOverride"/> while
    /// its terms say whatever they say. Only a test constructs these: the query's own BIND cannot
    /// disagree with the term it was computed from, which is exactly why a decoder must not take
    /// the marker's word for it.
    /// </summary>
    private static RepeatedEnumerationRow RowMarked(
        string predicateIri, RepeatedEnumerationRdfTerm value, string markerOverride) =>
        Row(
            RepeatedEnumerationRdfTerm.Iri(Axiom),
            RepeatedEnumerationRdfTerm.Iri(predicateIri),
            value,
            markerOverride);

    private static RepeatedEnumerationRow Row(
        RepeatedEnumerationRdfTerm axiom,
        RepeatedEnumerationRdfTerm predicate,
        RepeatedEnumerationRdfTerm value,
        string? markerOverride = null)
    {
        var kind = markerOverride ?? value.Kind switch
        {
            RepeatedEnumerationRdfTermKind.Iri => "iri",
            RepeatedEnumerationRdfTermKind.Literal => "literal",
            _ => "unbound",
        };
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(Work),
            axiom,
            predicate,
            value,
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

    /// <summary>The family's typed "this work reifies nothing", exactly as its absence branch binds it.</summary>
    private static RepeatedEnumerationRow AbsenceRow()
    {
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(Work),
            RepeatedEnumerationRdfTerm.Unbound(),
            RepeatedEnumerationRdfTerm.Unbound(),
            RepeatedEnumerationRdfTerm.Unbound(),
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

    /// <summary>
    /// A well-formed axiom on <paramref name="datePredicate"/>, which a case then adds rows to.
    /// </summary>
    private static List<RepeatedEnumerationRow> WellFormed(
        string datePredicate = EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri,
        string target = "2003-08-02",
        string targetDatatype = XsdDate) =>
    [
        Row(EuObjectFactsDiscoveryPlan.AnnotatedSourcePredicateIri, RepeatedEnumerationRdfTerm.Iri(Work)),
        Row(EuObjectFactsDiscoveryPlan.AnnotatedPropertyPredicateIri, RepeatedEnumerationRdfTerm.Iri(datePredicate)),
        Row(
            EuObjectFactsDiscoveryPlan.RdfTypePredicateIri,
            RepeatedEnumerationRdfTerm.Iri(EuObjectFactsDiscoveryPlan.OwlAxiomClassIri)),
        Row(
            EuObjectFactsDiscoveryPlan.AnnotatedTargetPredicateIri,
            RepeatedEnumerationRdfTerm.Literal(target, targetDatatype, null)),
    ];

    private static IReadOnlyList<EuDateAxiomBinding>? Decode(
        IReadOnlyList<RepeatedEnumerationRow> rows, out EuReifiedAxiomDecodeRefusal refusal) =>
        EuReifiedAxiomDecode.TryDecode(rows, AxiomProfile, Observation, out refusal, out _);

    [TestMethod]
    public void AWellFormedAxiomCarryingItsFd335QualifierDecodesToThePinnedRole()
    {
        var rows = WellFormed();
        rows.Add(Row(TypeOfDate, RepeatedEnumerationRdfTerm.Literal("{EV|" + Fd335 + "EV}", XsdString, null)));

        var bindings = Decode(rows, out var refusal);

        Assert.AreEqual(EuReifiedAxiomDecodeRefusal.None, refusal);
        Assert.IsNotNull(bindings);
        Assert.HasCount(1, bindings);
        Assert.AreEqual("EV", bindings[0].RawQualifierCode);
        Assert.AreEqual(DateSemanticRole.EntryIntoForce, bindings[0].Fact.SemanticRole);
        Assert.AreEqual(Axiom, bindings[0].Axiom.RemoteAxiomId);
        Assert.AreEqual(EuReifiedAxiomDecode.ParsedByAuthority, bindings[0].ParsedByAuthority);
    }

    /// <summary>
    /// The label is ours, from the accepted table, keyed by the code the publisher sent. The wire
    /// carries the code and its authority IRI and no label at all, so a label appearing here for a
    /// code the table does not pin would be an invention.
    /// </summary>
    [TestMethod]
    public void TheQualifierLabelComesFromTheAcceptedTableAndNotFromTheWire()
    {
        var pinned = WellFormed();
        pinned.Add(Row(TypeOfDate, RepeatedEnumerationRdfTerm.Literal("{EV|" + Fd335 + "EV}", XsdString, null)));

        // Well-formed, authority agrees with its code, but the accepted table pins no such code.
        var unpinned = WellFormed();
        unpinned.Add(Row(TypeOfDate, RepeatedEnumerationRdfTerm.Literal("{ZZ|" + Fd335 + "ZZ}", XsdString, null)));

        var withPin = Decode(pinned, out _);
        var withoutPin = Decode(unpinned, out var unpinnedRefusal);

        Assert.IsNotNull(withPin);
        Assert.AreEqual("Entry into force", withPin[0].QualifierLabel);

        // An unpinned code is retained as evidence, not refused and not relabelled.
        Assert.AreEqual(EuReifiedAxiomDecodeRefusal.None, unpinnedRefusal);
        Assert.IsNotNull(withoutPin);
        Assert.AreEqual("ZZ", withoutPin[0].RawQualifierCode);
        Assert.IsNull(withoutPin[0].QualifierLabel);
        Assert.AreEqual(DateSemanticRole.RoleNotStatedByPublisher, withoutPin[0].Fact.SemanticRole);
    }

    /// <summary>
    /// The ordinary no-qualifier path. Two of family A's three measured axioms carried
    /// <c>type_of_date</c>; the third did not, and it must still decode with a role read from the
    /// predicate it annotates rather than disappear or refuse.
    /// </summary>
    [TestMethod]
    public void AnAxiomWithNoQualifierTakesItsRoleFromThePredicateItAnnotates()
    {
        (string Predicate, DateSemanticRole Role)[] cases =
        [
            (EuDateQualifierVocabulary.EndOfValidityPredicateUri, DateSemanticRole.EndOfValidity),
            (EuDateQualifierVocabulary.SignatureDatePredicateUri, DateSemanticRole.SignatureDate),
            (EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri,
                DateSemanticRole.RoleNotStatedByPublisher),
            (EuDateQualifierVocabulary.DeadlinePredicateUri, DateSemanticRole.RoleNotStatedByPublisher),
        ];

        foreach (var (predicate, role) in cases)
        {
            var bindings = Decode(WellFormed(predicate), out var refusal);

            Assert.AreEqual(EuReifiedAxiomDecodeRefusal.None, refusal, predicate);
            Assert.IsNotNull(bindings, predicate);
            Assert.HasCount(1, bindings, predicate);
            Assert.IsNull(bindings[0].RawQualifierCode, predicate);
            Assert.IsNull(bindings[0].QualifierLabel, predicate);
            Assert.AreEqual(role, bindings[0].Fact.SemanticRole, predicate);
        }
    }

    /// <summary>
    /// Family A asks for every property on the axiom rather than a list it chose, precisely so an
    /// unmodelled one is retained. This proves the decode keeps that promise instead of discarding
    /// what it has no field for.
    /// </summary>
    [TestMethod]
    public void APropertyThisDecodeModelsNothingAboutIsRetainedAsEvidenceRatherThanDropped()
    {
        const string Unmodelled = "http://publications.europa.eu/ontology/annotation#some_later_property";

        var rows = WellFormed();
        rows.Add(Row(TypeOfDate, RepeatedEnumerationRdfTerm.Literal("{EV|" + Fd335 + "EV}", XsdString, null)));
        rows.Add(Row(CommentOnDate, RepeatedEnumerationRdfTerm.Literal("see Article 28", XsdString, null)));
        rows.Add(Row(Unmodelled, RepeatedEnumerationRdfTerm.Literal("a value nothing here reads", XsdString, null)));

        var bindings = Decode(rows, out var refusal);

        Assert.AreEqual(EuReifiedAxiomDecodeRefusal.None, refusal);
        Assert.IsNotNull(bindings);
        Assert.AreEqual("see Article 28", bindings[0].PublisherComment);

        var retained = bindings[0].Axiom.Qualifiers;
        CollectionAssert.AreEqual(
            new[]
            {
                EuObjectFactsDiscoveryPlan.AnnotatedSourcePredicateIri,
                EuObjectFactsDiscoveryPlan.AnnotatedPropertyPredicateIri,
                EuObjectFactsDiscoveryPlan.RdfTypePredicateIri,
                EuObjectFactsDiscoveryPlan.AnnotatedTargetPredicateIri,
                TypeOfDate,
                CommentOnDate,
                Unmodelled,
            },
            retained.Select(static qualifier => qualifier.PredicateUri).ToArray(),
            "every delivered property must survive into the axiom's retained qualifiers");
        Assert.AreEqual(
            "a value nothing here reads",
            retained.Single(static qualifier => qualifier.PredicateUri == Unmodelled).RawValue);
    }

    /// <summary>
    /// A repeated property this decode models nothing about is retained in full, because it decides
    /// nothing and discarding it would be the silence this family exists to prevent.
    /// </summary>
    [TestMethod]
    public void ARepeatedUnmodelledPropertyIsRetainedInFullRatherThanReducedToOneOccurrence()
    {
        const string Unmodelled = "http://publications.europa.eu/ontology/annotation#quality_issue";

        var rows = WellFormed();
        rows.Add(Row(Unmodelled, RepeatedEnumerationRdfTerm.Literal("first", XsdString, null)));
        rows.Add(Row(Unmodelled, RepeatedEnumerationRdfTerm.Literal("second", XsdString, null)));

        var bindings = Decode(rows, out var refusal);

        Assert.AreEqual(EuReifiedAxiomDecodeRefusal.None, refusal);
        Assert.IsNotNull(bindings);
        CollectionAssert.AreEqual(
            new[] { "first", "second" },
            bindings[0].Axiom.Qualifiers
                .Where(static qualifier => qualifier.PredicateUri == Unmodelled)
                .Select(static qualifier => qualifier.RawValue)
                .ToArray());
    }

    /// <summary>
    /// An exactly repeated modelled predicate is one fact stated twice, which decides the same
    /// thing either way and is therefore admitted.
    /// </summary>
    [TestMethod]
    public void AnExactlyRepeatedModelledPredicateIsTheSameFactTwiceAndIsAdmitted()
    {
        var rows = WellFormed();
        rows.Add(Row(CommentOnDate, RepeatedEnumerationRdfTerm.Literal("see Article 28", XsdString, null)));
        rows.Add(Row(CommentOnDate, RepeatedEnumerationRdfTerm.Literal("see Article 28", XsdString, null)));

        var bindings = Decode(rows, out var refusal);

        Assert.AreEqual(EuReifiedAxiomDecodeRefusal.None, refusal);
        Assert.IsNotNull(bindings);
        Assert.AreEqual("see Article 28", bindings[0].PublisherComment);
    }

    /// <summary>
    /// A modelled predicate delivered twice with disagreeing values is refused, and refused the
    /// same way whichever order the rows arrive in.
    /// </summary>
    /// <remarks>
    /// This replaces a guard I wrote that asserted the opposite - that the first occurrence wins
    /// and the rest are merely retained. That made the accepted role depend on delivery order:
    /// reversing two annotatedProperty rows selected a different source predicate and a different
    /// DateSemanticRole for the same axiom. The reversal assertions below are the point, because a
    /// reader that resolved the ambiguity at all would answer them differently.
    /// </remarks>
    [TestMethod]
    public void AModelledPredicateDeliveredTwiceWithDisagreeingValuesIsRefusedInEitherOrder()
    {
        (string Predicate, RepeatedEnumerationRdfTerm Second)[] conflicts =
        [
            (EuObjectFactsDiscoveryPlan.AnnotatedPropertyPredicateIri,
                RepeatedEnumerationRdfTerm.Iri(EuDateQualifierVocabulary.EndOfValidityPredicateUri)),
            (EuObjectFactsDiscoveryPlan.AnnotatedSourcePredicateIri,
                RepeatedEnumerationRdfTerm.Iri("http://publications.europa.eu/resource/cellar/other-work")),
            (EuObjectFactsDiscoveryPlan.AnnotatedTargetPredicateIri,
                RepeatedEnumerationRdfTerm.Literal("2011-01-01", XsdDate, null)),
            (EuObjectFactsDiscoveryPlan.RdfTypePredicateIri,
                RepeatedEnumerationRdfTerm.Iri("http://www.w3.org/2002/07/owl#Class")),
            (TypeOfDate, RepeatedEnumerationRdfTerm.Literal("{MA|" + Fd335 + "MA}", XsdString, null)),
            (CommentOnDate, RepeatedEnumerationRdfTerm.Literal("a different comment", XsdString, null)),
        ];

        foreach (var (predicate, second) in conflicts)
        {
            // Every modelled predicate is present exactly once first, so the added row below is
            // genuinely a second occurrence rather than the only one.
            var forward = WellFormed();
            forward.Add(Row(TypeOfDate, RepeatedEnumerationRdfTerm.Literal("{EV|" + Fd335 + "EV}", XsdString, null)));
            forward.Add(Row(CommentOnDate, RepeatedEnumerationRdfTerm.Literal("see Article 28", XsdString, null)));
            forward.Add(Row(predicate, second));

            var reversed = new List<RepeatedEnumerationRow>(forward);
            reversed.Reverse();

            var forwardBindings = Decode(forward, out var forwardRefusal);
            var reversedBindings = Decode(reversed, out var reversedRefusal);

            Assert.IsNull(forwardBindings, predicate);
            Assert.IsNull(reversedBindings, predicate + " (reversed)");
            Assert.AreEqual(
                EuReifiedAxiomDecodeRefusal.ModelledPredicateDeliveredMoreThanOnce, forwardRefusal, predicate);
            Assert.AreEqual(
                forwardRefusal,
                reversedRefusal,
                predicate + ": delivery order must not change the answer");
        }
    }

    /// <summary>
    /// A fully bound row whose projected marker merely SAYS it is unbound must refuse, not vanish.
    /// </summary>
    /// <remarks>
    /// The marker is a value the query computed about the row. A reader that skipped on it alone
    /// would drop real qualifier evidence on the strength of a computed label - a false absence
    /// manufactured inside our own reader rather than delivered by the publisher, which is the
    /// worst version of the shape S2-A05 refuses.
    /// </remarks>
    [TestMethod]
    public void ABoundRowWhoseMarkerClaimsAbsenceIsRefusedRatherThanSilentlyDropped()
    {
        var rows = WellFormed();
        rows.Add(RowMarked(
            TypeOfDate, RepeatedEnumerationRdfTerm.Literal("{EV|" + Fd335 + "EV}", XsdString, null), "unbound"));

        var bindings = EuReifiedAxiomDecode.TryDecode(
            rows, AxiomProfile, Observation, out var refusal, out var offendingValue);

        Assert.IsNull(bindings, "a bound qualifier row must not disappear because a marker claims absence");
        Assert.AreEqual(EuReifiedAxiomDecodeRefusal.RowShapeContradictsItsProjectedKind, refusal);
        Assert.AreEqual(Axiom, offendingValue);
    }

    /// <summary>
    /// The marker is checked in both directions: a bound marker beside terms that are not of that
    /// kind is the same contradiction read from the other side.
    /// </summary>
    [TestMethod]
    public void AMarkerThatDisagreesWithItsOwnValueTermIsRefused()
    {
        var iriMarkerOnALiteral = WellFormed();
        iriMarkerOnALiteral.Add(RowMarked(
            CommentOnDate, RepeatedEnumerationRdfTerm.Literal("plain text", XsdString, null), "iri"));

        var unknownMarker = WellFormed();
        unknownMarker.Add(RowMarked(
            CommentOnDate, RepeatedEnumerationRdfTerm.Literal("plain text", XsdString, null), "something_else"));

        foreach (var rows in new[] { iriMarkerOnALiteral, unknownMarker })
        {
            var bindings = Decode(rows, out var refusal);

            Assert.IsNull(bindings);
            Assert.AreEqual(EuReifiedAxiomDecodeRefusal.RowShapeContradictsItsProjectedKind, refusal);
        }
    }

    /// <summary>
    /// A type_of_date carrier that is present but not a literal is malformed, not missing.
    /// </summary>
    /// <remarks>
    /// Reading it as missing would convert a qualifier the publisher did assert into one it never
    /// stated, and the role would then be derived from the predicate as though nothing had been
    /// sent - the binding would look ordinary and carry a role the evidence does not support.
    /// </remarks>
    [TestMethod]
    public void APresentButNonLiteralQualifierCarrierIsMalformedRatherThanAbsent()
    {
        var rows = WellFormed();
        rows.Add(Row(TypeOfDate, RepeatedEnumerationRdfTerm.Iri(Fd335 + "EV")));

        var bindings = Decode(rows, out var refusal);

        Assert.IsNull(bindings, "an IRI carrier must not fall through to the no-qualifier role path");
        Assert.AreEqual(EuReifiedAxiomDecodeRefusal.QualifierTermMalformed, refusal);
    }

    /// <summary>
    /// Decision 58 and S2-A05: the absence branch's row is a delivered fact, not a failure. It must
    /// decode to no bindings without refusing, and without inventing one.
    /// </summary>
    [TestMethod]
    public void TheFamilysTypedAbsenceRowDecodesToNoBindingsAndNoRefusal()
    {
        var bindings = Decode([AbsenceRow()], out var refusal);

        Assert.AreEqual(EuReifiedAxiomDecodeRefusal.None, refusal);
        Assert.IsNotNull(bindings);
        Assert.IsEmpty(bindings);
    }

    [TestMethod]
    public void TwoAxiomsOnOneWorkDecodeSeparatelyAndInDeliveredOrder()
    {
        const string Second = "http://publications.europa.eu/.well-known/genid/050dd964/a2decode-2";

        List<RepeatedEnumerationRow> rows = [.. WellFormed()];
        rows.Add(Row(
            Second, EuObjectFactsDiscoveryPlan.AnnotatedSourcePredicateIri, RepeatedEnumerationRdfTerm.Iri(Work)));
        rows.Add(Row(
            Second,
            EuObjectFactsDiscoveryPlan.AnnotatedPropertyPredicateIri,
            RepeatedEnumerationRdfTerm.Iri(EuDateQualifierVocabulary.EndOfValidityPredicateUri)));
        rows.Add(Row(
            Second,
            EuObjectFactsDiscoveryPlan.RdfTypePredicateIri,
            RepeatedEnumerationRdfTerm.Iri(EuObjectFactsDiscoveryPlan.OwlAxiomClassIri)));
        rows.Add(Row(
            Second,
            EuObjectFactsDiscoveryPlan.AnnotatedTargetPredicateIri,
            RepeatedEnumerationRdfTerm.Literal("2010", XsdGYear, null)));

        var bindings = Decode(rows, out var refusal);

        Assert.AreEqual(EuReifiedAxiomDecodeRefusal.None, refusal);
        Assert.IsNotNull(bindings);
        Assert.HasCount(2, bindings);
        CollectionAssert.AreEqual(
            new[] { Axiom, Second },
            bindings.Select(static binding => binding.Axiom.RemoteAxiomId).ToArray());
        Assert.AreEqual(DatePrecision.YearMonthDay, bindings[0].Fact.Date.Precision);
        Assert.AreEqual(DatePrecision.Year, bindings[1].Fact.Date.Precision);
        Assert.AreEqual(DateSemanticRole.EndOfValidity, bindings[1].Fact.SemanticRole);
    }

    /// <summary>
    /// Every refusal this decode declares is reachable from a delivered shape. A refusal member no
    /// fixture can produce is not a guarantee, it is decoration, and the two are indistinguishable
    /// from the enum alone. The final assertion is the point of the test: it fails if a member is
    /// ever added without a case here that drives it.
    /// </summary>
    [TestMethod]
    public void EveryDeclaredRefusalIsReachableFromSomeDeliveredShape()
    {
        var malformedAxiomNode = WellFormed();
        malformedAxiomNode.Add(Row(
            RepeatedEnumerationRdfTerm.Literal("not an IRI", XsdString, null),
            RepeatedEnumerationRdfTerm.Iri(CommentOnDate),
            RepeatedEnumerationRdfTerm.Literal("x", XsdString, null)));

        var noSource = WellFormed();
        noSource.RemoveAt(0);

        // A real CDM predicate, and deliberately not one of the four this family admits.
        var unadmittedProperty = WellFormed(EuConsolidationDiscoveryPlan.Cdm + "resource_legal_id_celex");

        var noType = WellFormed();
        noType.RemoveAll(static row => row.Terms[2].Value == EuObjectFactsDiscoveryPlan.RdfTypePredicateIri);

        var targetIsAnIri = WellFormed();
        targetIsAnIri.RemoveAt(3);
        targetIsAnIri.Add(Row(
            EuObjectFactsDiscoveryPlan.AnnotatedTargetPredicateIri, RepeatedEnumerationRdfTerm.Iri(Work)));

        var wrongDatatype = WellFormed(targetDatatype: XsdString);

        var malformedCarrier = WellFormed();
        malformedCarrier.Add(Row(TypeOfDate, RepeatedEnumerationRdfTerm.Literal("EV", XsdString, null)));

        var carrierDisagrees = WellFormed();
        carrierDisagrees.Add(Row(
            TypeOfDate, RepeatedEnumerationRdfTerm.Literal("{EV|" + Fd335 + "MA}", XsdString, null)));

        // "EV" is pinned to entry-into-force; review/23 evidences it on no other predicate, so the
        // accepted contract refuses the pair rather than letting this decode retarget the role.
        var pinnedCodeOnTheWrongPredicate = WellFormed(EuDateQualifierVocabulary.DeadlinePredicateUri);
        pinnedCodeOnTheWrongPredicate.Add(
            Row(TypeOfDate, RepeatedEnumerationRdfTerm.Literal("{EV|" + Fd335 + "EV}", XsdString, null)));

        var markerContradictsTerms = WellFormed();
        markerContradictsTerms.Add(RowMarked(
            CommentOnDate, RepeatedEnumerationRdfTerm.Literal("bound after all", XsdString, null), "unbound"));

        var contradictoryRepetition = WellFormed();
        contradictoryRepetition.Add(Row(
            EuObjectFactsDiscoveryPlan.AnnotatedTargetPredicateIri,
            RepeatedEnumerationRdfTerm.Literal("2011-01-01", XsdDate, null)));

        (EuReifiedAxiomDecodeRefusal Expected, List<RepeatedEnumerationRow> Rows)[] cases =
        [
            (EuReifiedAxiomDecodeRefusal.RowShapeContradictsItsProjectedKind, markerContradictsTerms),
            (EuReifiedAxiomDecodeRefusal.ModelledPredicateDeliveredMoreThanOnce, contradictoryRepetition),
            (EuReifiedAxiomDecodeRefusal.AxiomNodeNotAnIri, malformedAxiomNode),
            (EuReifiedAxiomDecodeRefusal.AnnotatedSourceMissingOrNotAnIri, noSource),
            (EuReifiedAxiomDecodeRefusal.AnnotatedPropertyMissingOrNotAdmitted, unadmittedProperty),
            (EuReifiedAxiomDecodeRefusal.AxiomTypeMissingOrNotOwlAxiom, noType),
            (EuReifiedAxiomDecodeRefusal.AnnotatedTargetMissingOrNotALiteral, targetIsAnIri),
            (EuReifiedAxiomDecodeRefusal.AnnotatedTargetDatatypeNotADateShape, wrongDatatype),
            (EuReifiedAxiomDecodeRefusal.QualifierTermMalformed, malformedCarrier),
            (EuReifiedAxiomDecodeRefusal.QualifierAuthorityDisagreesWithItsCode, carrierDisagrees),
            (EuReifiedAxiomDecodeRefusal.BindingRefusedByTheAcceptedContract, pinnedCodeOnTheWrongPredicate),
        ];

        var reached = new List<EuReifiedAxiomDecodeRefusal>();
        foreach (var (expected, rows) in cases)
        {
            var bindings = Decode(rows, out var refusal);

            Assert.IsNull(bindings, expected + " must refuse rather than hand back a binding");
            Assert.AreEqual(expected, refusal);
            reached.Add(refusal);
        }

        CollectionAssert.AreEqual(
            Enum.GetValues<EuReifiedAxiomDecodeRefusal>()
                .Where(static member => member != EuReifiedAxiomDecodeRefusal.None)
                .ToArray(),
            reached.Distinct().Order().ToArray(),
            "every declared refusal must be driven by a case above, and each case must reach a distinct one");
    }

    /// <summary>
    /// A refusal names what offended, so a run can be diagnosed from its retained evidence rather
    /// than by re-running the publisher. That is the same lesson the population report's missing
    /// refusal taught, applied here before the fact rather than after it.
    /// </summary>
    [TestMethod]
    public void ARefusalNamesTheOffendingValueRatherThanRefusingAnonymously()
    {
        var rows = WellFormed(targetDatatype: XsdString);

        var bindings = EuReifiedAxiomDecode.TryDecode(
            rows, AxiomProfile, Observation, out var refusal, out var offendingValue);

        Assert.IsNull(bindings);
        Assert.AreEqual(EuReifiedAxiomDecodeRefusal.AnnotatedTargetDatatypeNotADateShape, refusal);
        Assert.AreEqual(XsdString, offendingValue);
    }

    /// <summary>
    /// One undecodable axiom refuses the whole delivery rather than being skipped past. A decode
    /// that dropped it would hand back a smaller, cleaner axiom set than the publisher sent, which
    /// is the false-absence shape S2-A05 exists to refuse.
    /// </summary>
    [TestMethod]
    public void OneUndecodableAxiomRefusesTheDeliveryRatherThanBeingSkipped()
    {
        const string Broken = "http://publications.europa.eu/.well-known/genid/050dd964/a2decode-broken";

        List<RepeatedEnumerationRow> rows = [.. WellFormed()];
        rows.Add(Row(
            Broken, EuObjectFactsDiscoveryPlan.AnnotatedSourcePredicateIri, RepeatedEnumerationRdfTerm.Iri(Work)));
        rows.Add(Row(
            Broken,
            EuObjectFactsDiscoveryPlan.AnnotatedPropertyPredicateIri,
            RepeatedEnumerationRdfTerm.Iri(EuDateQualifierVocabulary.DeadlinePredicateUri)));
        rows.Add(Row(
            Broken,
            EuObjectFactsDiscoveryPlan.AnnotatedTargetPredicateIri,
            RepeatedEnumerationRdfTerm.Literal("2006-08-01", XsdDate, null)));

        var bindings = Decode(rows, out var refusal);

        Assert.IsNull(bindings, "the well-formed sibling must not be handed back while its neighbour is dropped");
        Assert.AreEqual(EuReifiedAxiomDecodeRefusal.AxiomTypeMissingOrNotOwlAxiom, refusal);
    }
}
