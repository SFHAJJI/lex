using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Stage 2 item E8, the Luxembourg draft graph: the plan, the executor entry and the producer that
/// turn delivered <c>InitialDraft</c> property rows into records.
/// </summary>
/// <remarks>
/// The admit path is asserted first and end to end. Before this family existed,
/// <c>LuxembourgDraftRelationPredicate</c> was a closed vocabulary reached by nothing — this
/// repository declared <c>draftTransposes</c> and never asked the publisher about it — and two
/// sibling families reached integration whose decoders no code called.
/// </remarks>
[TestClass]
public sealed class LuxembourgDraftGraphProducerTests
{
    private const string Draft = "http://data.legilux.public.lu/resource/draft/8357";
    private const string OtherDraft = "http://data.legilux.public.lu/resource/draft/8358";
    private const string Directive = "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";
    private const string DossierPage = "https://www.chd.lu/fr/dossier/8357";
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
    private const string XsdAnyUri = "http://www.w3.org/2001/XMLSchema#anyURI";
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    private static readonly SourceArtifactRef Evidence = new(
        "urn:uuid:5f28c063-9a41-4b7e-8d05-c31742ef96ba", new string('d', 64));

    private static RepeatedEnumerationInterpretationProfile Profile() =>
        LuxembourgDraftGraphDiscoveryPlan.Create().CreateDeliveryProfile();

    private static RepeatedEnumerationRdfTerm Iri(string value) =>
        RepeatedEnumerationRdfTerm.Iri(value);

    private static RepeatedEnumerationRdfTerm Literal(string value, string? datatype = null, string? language = null) =>
        RepeatedEnumerationRdfTerm.Literal(value, datatype, language);

    private static RepeatedEnumerationRdfTerm Unbound() => RepeatedEnumerationRdfTerm.Unbound();

    /// <summary>
    /// One delivered row, with every term, marker, qualifier column and cursor key derived from the
    /// term unless a test overrides exactly one.
    /// </summary>
    /// <remarks>
    /// Coherent by construction, so an honest row needs no arranging and a contradiction has to be
    /// asked for. A fixture that hard-codes what it is meant to be varying cannot express the
    /// deliveries a producer exists to refuse — three tests in the sibling procedure-event file
    /// passed for the wrong reason before its fixture was built this way.
    /// </remarks>
    private static RepeatedEnumerationRow Row(
        string draft = Draft,
        string? predicate = null,
        RepeatedEnumerationRdfTerm? value = null,
        string? draftKind = null,
        string? valueKind = null,
        string? datatype = null,
        string? language = null,
        RepeatedEnumerationRdfTerm? multiplicity = null,
        string? key1 = null,
        string? key2 = null,
        string? key3 = null,
        string? key4 = null,
        string? key5 = null,
        string? key6 = null,
        string? key7 = null)
    {
        var draftTerm = Iri(draft);
        var predicateIri = predicate ?? LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri;
        var valueTerm = value ?? Iri(Directive);
        var datatypeColumn = datatype ?? Qualifier(valueTerm, static term => term.Datatype);
        var languageColumn = language ?? Qualifier(valueTerm, static term => term.Language);

        var terms = new List<RepeatedEnumerationRdfTerm>
        {
            draftTerm,
            Literal(draftKind ?? Marker(draftTerm)),
            Iri(predicateIri),
            valueTerm,
            Literal(valueKind ?? Marker(valueTerm)),
            Literal(datatypeColumn),
            Literal(languageColumn),
            multiplicity ?? Literal("1", XsdInteger),
            Literal(key1 ?? draft),
            Literal(key2 ?? Marker(draftTerm)),
            Literal(key3 ?? predicateIri),
            Literal(key4 ?? valueTerm.Value ?? string.Empty),
            Literal(key5 ?? Marker(valueTerm)),
            Literal(key6 ?? datatypeColumn),
            Literal(key7 ?? languageColumn),
        };

        return new RepeatedEnumerationRow(terms, terms, terms);
    }

    private static string Marker(RepeatedEnumerationRdfTerm term) => term.Kind switch
    {
        RepeatedEnumerationRdfTermKind.Iri => "iri",
        RepeatedEnumerationRdfTermKind.Literal => "literal",
        RepeatedEnumerationRdfTermKind.BlankNode => "unsupported_blank_node",
        _ => LuxembourgDraftGraphDiscoveryPlan.UnboundKind,
    };

    private static string Qualifier(
        RepeatedEnumerationRdfTerm term, Func<RepeatedEnumerationRdfTerm, string?> select) =>
        term.Kind == RepeatedEnumerationRdfTermKind.Literal ? select(term) ?? string.Empty : string.Empty;

    private static LuxembourgDraftGraphProductionResult Decode(params RepeatedEnumerationRow[] rows) =>
        LuxembourgDraftGraphProducer.DecodeRows(rows, Profile(), Evidence);

    /// <summary>A delivered transposition intention becomes a record carrying its exact terms.</summary>
    [TestMethod]
    public void ADeliveredTranspositionBecomesARecord()
    {
        var result = Decode(Row());

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(1, result.Records!);

        var record = result.Records![0];
        Assert.AreEqual(Draft, record.DraftIri);
        Assert.AreEqual(LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri, record.PredicateIri);
        Assert.AreEqual(Directive, record.Value);
        Assert.AreEqual("iri", record.ValueKind);
        Assert.AreEqual(Evidence.ResourceId, record.SourceObservationId);
    }

    /// <summary>
    /// A property the publisher holds nothing for is an ANSWER, delivered and recorded.
    /// </summary>
    /// <remarks>
    /// The plan asks for the absence by name with <c>FILTER NOT EXISTS</c>, so "this draft
    /// transposes nothing" is a row rather than a missing row. Dropping it would make that
    /// indistinguishable from a draft nobody asked about, which is the false absence S2-A03 forbids
    /// and the defect this family exists to end.
    /// </remarks>
    [TestMethod]
    public void APropertyTheDraftHoldsNothingForIsRecordedRatherThanAbsent()
    {
        var result = Decode(Row(value: Unbound()));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(1, result.Records!);
        Assert.IsNull(result.Records![0].Value);
        Assert.AreEqual(
            LuxembourgDraftGraphDiscoveryPlan.UnboundKind, result.Records![0].ValueKind,
            "the marker says the publisher holds none, which is the fact.");
    }

    /// <summary>
    /// A literal value keeps its datatype and its language, because those are what tell two apart.
    /// </summary>
    /// <remarks>
    /// Two literals sharing a lexical form and differing in datatype are different facts — the rule
    /// two review rounds settled on the procedure-event plan. A record keeping only the lexical form
    /// could not distinguish them after the fact even though the delivery could.
    /// </remarks>
    [TestMethod]
    public void ALiteralValueKeepsItsDatatypeAndLanguage()
    {
        var dated = Decode(Row(
            predicate: LuxembourgDraftGraphDiscoveryPlan.ReferralDatePredicateIri,
            value: Literal("2024-05-22", XsdDate)));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.None, dated.Refusal, dated.Detail);
        Assert.AreEqual(XsdDate, dated.Records![0].ValueDatatypeIri);
        Assert.AreEqual(string.Empty, dated.Records![0].ValueLanguageTag);

        var tagged = Decode(Row(
            predicate: LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri,
            value: Literal("en-cours", null, "fr")));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.None, tagged.Refusal, tagged.Detail);
        Assert.AreEqual("fr", tagged.Records![0].ValueLanguageTag);
    }

    /// <summary>
    /// A dossier URL is retained as text. Nothing here dereferences it.
    /// </summary>
    /// <remarks>
    /// <c>parliamentDraftUrl</c> names a page on a host this pipeline does not contact. Retaining a
    /// URL is not requesting one; this asserts the value survives as a string and, by the absence of
    /// any fetch in this family, that it is never followed.
    /// </remarks>
    [TestMethod]
    public void ADossierUrlIsRetainedAsTextAndNotFollowed()
    {
        var result = Decode(Row(
            predicate: LuxembourgDraftGraphDiscoveryPlan.ParliamentDraftUrlPredicateIri,
            value: Literal(DossierPage, XsdAnyUri)));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.None, result.Refusal, result.Detail);
        Assert.AreEqual(DossierPage, result.Records![0].Value);
        Assert.AreEqual(XsdAnyUri, result.Records![0].ValueDatatypeIri);
    }

    /// <summary>A row naming a property this family never asked about refuses the delivery.</summary>
    /// <remarks>
    /// The plan binds its predicates from a VALUES block, so an honest delivery cannot contain
    /// another. One that does is answering a question nobody asked, and admitting it would let the
    /// asked-about set this production publishes describe a delivery it does not match.
    /// </remarks>
    [TestMethod]
    public void ARowNamingAPropertyNeverAskedAboutIsRefused()
    {
        const string NeverAsked = "http://data.legilux.public.lu/resource/ontology/jolux#titleDraft";

        var result = Decode(Row(predicate: NeverAsked));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.PredicateNotAskedAbout, result.Refusal);
        StringAssert.Contains(result.Detail!, NeverAsked);
    }

    /// <summary>A marker disagreeing with its term refuses the delivery whole.</summary>
    [TestMethod]
    public void AMarkerThatDisagreesWithItsTermRefusesTheDelivery()
    {
        var result = Decode(Row(value: Literal("x"), valueKind: "iri"));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail!, "value_kind");
    }

    /// <summary>
    /// A qualifier column contradicting the term it describes refuses the delivery.
    /// </summary>
    /// <remarks>
    /// Both columns are bound from <c>DATATYPE()</c> and <c>LANG()</c> over the very term they
    /// qualify, so an honest delivery cannot disagree. A row that does keys as one fact and decodes
    /// as another — the marker-versus-term rule applied to the qualifiers, which the sibling
    /// procedure-event producer needed a repair to learn.
    /// </remarks>
    [TestMethod]
    public void AQualifierColumnContradictingItsTermIsRefused()
    {
        var wrongDatatype = Decode(Row(
            value: Literal("2024-05-22", XsdDate), datatype: XsdAnyUri));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.RowNotAdmitted, wrongDatatype.Refusal);
        StringAssert.Contains(wrongDatatype.Detail!, "datatype_iri");

        var wrongLanguage = Decode(Row(
            value: Literal("en-cours", null, "fr"), language: "de"));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.RowNotAdmitted, wrongLanguage.Refusal);
        StringAssert.Contains(wrongLanguage.Detail!, "language_tag");
    }

    /// <summary>
    /// Corrupting any one of the seven cursor keys refuses the row and names that key.
    /// </summary>
    /// <remarks>
    /// A sweep rather than seven near-identical tests, and driven off the live profile so a key
    /// added later is covered the day it appears. The honest value of several keys is a marker or an
    /// empty qualifier, and a producer that ignored one would agree with a fixture that also left it
    /// empty — two mutations survived the sibling family on exactly that.
    /// </remarks>
    [TestMethod]
    public void CorruptingAnySingleCursorKeyRefusesTheRowAndNamesIt()
    {
        var profile = Profile();
        var keys = profile.CursorVariables;

        Assert.HasCount(7, keys, "this family keys seven positions.");

        foreach (var key in keys)
        {
            var ordinal = profile.ProjectionVariables.ToList().IndexOf(key);
            Assert.IsGreaterThan(-1, ordinal, $"{key} is projected.");

            // A row rich enough that no key's honest value is empty by accident.
            var terms = Row(
                predicate: LuxembourgDraftGraphDiscoveryPlan.StatusDraftPredicateIri,
                value: Literal("en-cours", null, "fr")).Terms.ToList();
            terms[ordinal] = Literal((terms[ordinal].Value ?? string.Empty) + "-not-delivered");

            var result = LuxembourgDraftGraphProducer.DecodeRows(
                [new RepeatedEnumerationRow(terms, terms, terms)], profile, Evidence);

            Assert.AreEqual(
                LuxembourgDraftGraphProductionRefusal.RowNotAdmitted, result.Refusal,
                $"{key} was corrupted and the row was still admitted, so that key keys nothing.");
            StringAssert.Contains(
                result.Detail!, key,
                $"the refusal must name {key} rather than another key that happened to differ.");
        }
    }

    /// <summary>The grouped count must be the term COUNT(*) delivers.</summary>
    [TestMethod]
    public void ACountCarryingTheWrongDatatypeIsRefused()
    {
        var result = Decode(Row(multiplicity: Literal("3", "http://www.w3.org/2001/XMLSchema#string")));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.RowNotAdmitted, result.Refusal);
        StringAssert.Contains(result.Detail!, "multiplicity");
    }

    /// <summary>
    /// Asking about a property this run never asked refuses rather than answering empty.
    /// </summary>
    [TestMethod]
    public void APropertyThisRunNeverAskedAboutIsRefusedRatherThanAnsweredEmpty()
    {
        var result = Decode(Row(), Row(draft: OtherDraft, key1: OtherDraft));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(2, result.For(LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => result.For("http://data.legilux.public.lu/resource/ontology/jolux#titleDraft"));
    }

    /// <summary>A refused production answers no question at all.</summary>
    [TestMethod]
    public void ARefusedProductionAnswersNothing()
    {
        var result = Decode(Row(value: Literal("x"), valueKind: "iri"));

        Assert.AreEqual(LuxembourgDraftGraphProductionRefusal.RowNotAdmitted, result.Refusal);
        Assert.IsNull(result.Records);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => result.For(LuxembourgDraftGraphDiscoveryPlan.DraftTransposesPredicateIri));
    }
}
