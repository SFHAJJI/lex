using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Tests.Contracts.Source.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The EU language-scoped expression decode: the representation B31-L0071 says the corpus lacks, and
/// the ways a decoder could quietly reacquire the merge - or the unproven provenance - it exists to
/// remove.
/// </summary>
/// <remarks>
/// <para>
/// EVERY DELIVERY HERE IS PROVEN, NOT HANDED IN. The first head of this slice took bare row lists
/// and a caller-chosen write receipt, which let a caller pair any well-shaped rows with any valid
/// custody receipt. That door is closed: rows reach the decode only through
/// <see cref="VerifiedRepeatedEnumerationRows.TryOpen"/>, and the provenance it records is built
/// from the reopened pages themselves. These tests therefore mint real deliveries, real proofs and
/// real page evidence; there is no shortcut left that would let them do otherwise.
/// </para>
/// <para>
/// TWO RETRACTIONS, KEPT RATHER THAN QUIETLY DELETED. First, an earlier version of this comment said
/// every delivery here had to be single-page because the shared fixture minted one page per pass,
/// and recorded a first-page-only lineage mutation as equivalent-through-this-door on that basis.
/// That was simply wrong: the fixture already chains pages (<c>CreateTwoPage</c>), and
/// <c>CreatePagedRaw</c> builds multi-page deliveries over caller-built bodies.
/// </para>
/// <para>
/// Second, the multi-page tests that replaced it asserted DELIVERY-WIDE lineage - every page of the
/// family cited by every expression, including an empty terminal page labelled
/// <c>identity_and_language</c>. That contradicted the contribution vocabulary those tests were
/// written against: the role means "bytes that stated the expression's identity and its language",
/// and a page carrying none of an expression's rows stated neither. The assertions were written to
/// match the implementation instead of the design, which is the wrong direction, and they were green
/// the whole time. The expectations below now follow the design: an expression cites the pages that
/// carried its own rows, and completion evidence stays with the proof-bound delivery.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuLanguageScopedExpressionDecodeTests
{
    private const string WorkOne =
        "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";
    private const string WorkTwo =
        "http://publications.europa.eu/resource/cellar/99999999-9999-4999-8999-999999999999";
    private const string ExprEnglish = WorkOne + ".ENG";
    private const string ExprFrench = WorkOne + ".FRA";
    private const string ExprFrenchCorrigendumTwo = WorkOne + ".FRA.R02";
    private const string ExprNorwegian = WorkOne + ".NOR";
    private const string ExprOfWorkTwo = WorkTwo + ".ENG";

    private const string LanguageBase = "http://publications.europa.eu/resource/authority/language/";
    private const string English = LanguageBase + "ENG";
    private const string French = LanguageBase + "FRA";

    /// <summary>Outside the closed twenty-four on purpose. Cellar carries ninety-four.</summary>
    private const string Norwegian = LanguageBase + "NOR";

    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";

    private static readonly string BelongsToWorkIri =
        EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionBelongsToWork);
    private static readonly string UsesLanguageIri =
        EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionUsesLanguage);
    private static readonly string TitleIri =
        EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionTitle);
    private static readonly string WorkDateIri =
        EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.WorkDateDocument);
    private static readonly string CelexIri =
        EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ResourceLegalIdCelex);

    private static readonly string[] XProjection =
        ["parent", "object", "predicate", "value", "value_kind", "datatype_iri", "language_tag", "cursor"];
    private static readonly string[] XKey = ["parent", "object", "predicate", "value"];
    private static readonly string[] PProjection =
        ["object", "predicate", "value", "value_kind", "cursor"];
    private static readonly string[] PKey = ["object", "predicate", "value"];

    // ---- The binding between these fixtures and the publisher's real family. ----

    /// <summary>
    /// The fixtures below mint deliveries under a projection of this file's own making, which proves
    /// nothing unless the real family actually projects the variables this decode reads. This is
    /// that check, and it is why a fixture-shaped profile is honest here rather than convenient.
    /// </summary>
    [TestMethod]
    public void TheRealExpressionFactsProfileProjectsEveryVariableThisDecodeReads()
    {
        var real = EuObjectFactsDiscoveryPlan.Create()
            .CreateDeliveryProfile(EuObjectFactsQuerySet.ExpressionFacts);

        foreach (var variable in new[]
            { "parent", "object", "predicate", "value", "value_kind", "datatype_iri", "language_tag" })
        {
            Assert.Contains(
                variable,
                real.ProjectionVariables,
                $"the decode reads '{variable}' and the real family must project it.");
        }
    }

    /// <summary>The same, for the family the publisher's date is read from.</summary>
    [TestMethod]
    public void TheRealObjectFactsProfileProjectsEveryVariableThisDecodeReads()
    {
        var real = EuObjectFactsDiscoveryPlan.Create()
            .CreateDeliveryProfile(EuObjectFactsQuerySet.ObjectFacts);

        foreach (var variable in new[] { "object", "predicate", "value", "value_kind" })
        {
            Assert.Contains(variable, real.ProjectionVariables);
        }
    }

    // ---- The defect B31-L0071 names, stated as properties that must hold. ----

    /// <summary>
    /// Two languages of one Work both survive. <c>EuCellarObjectDecode.BuildLanguageObservation</c>
    /// reports English for this exact delivery and loses the French expression entirely.
    /// </summary>
    [TestMethod]
    public void TwoLanguagesOfOneWorkBothSurviveDecode()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            [.. Expression(WorkOne, ExprEnglish, English), .. Expression(WorkOne, ExprFrench, French)],
            out var refusal,
            out _);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.HasCount(2, decoded, "the older fold reports one observation for this delivery.");
        CollectionAssert.AreEqual(
            new[] { English, French },
            decoded.Select(static expression => expression.OfficialLanguage).ToArray());
    }

    /// <summary>The exact defect: a second same-language expression of one Work.</summary>
    [TestMethod]
    public void TwoSameLanguageCorrigendumExpressionsOfOneWorkBothSurvive()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            [
                .. Expression(WorkOne, ExprFrench, French),
                .. Expression(WorkOne, ExprFrenchCorrigendumTwo, French),
            ],
            out var refusal,
            out _);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.HasCount(2, decoded, "a language-keyed identity would have folded the second away.");
    }

    /// <summary>
    /// The 385 corrigenda with no ENG or FRA counterpart: a language with no scope enum member
    /// survives, verbatim.
    /// </summary>
    [TestMethod]
    public void ALanguageOutsideTheClosedScopeEnumSurvivesVerbatim()
    {
        Assert.IsFalse(
            Enum.GetNames<EuOfficialLanguage>().Any(static name =>
                name.Contains("Norwegian", StringComparison.Ordinal)),
            "this test is only meaningful while NOR is outside the closed twenty-four.");

        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(set, Expression(WorkOne, ExprNorwegian, Norwegian), out var refusal, out _);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.AreEqual(Norwegian, decoded[0].OfficialLanguage);
    }

    // ---- Provenance: proven, never handed in. ----

    /// <summary>
    /// The finding that sank the first head, stated as a test. Rows cannot be supplied; they are
    /// reopened from retained bytes and checked against the proof. Substituted page bytes are
    /// refused at that door, so no expression can cite custody nobody established.
    /// </summary>
    [TestMethod]
    public void SubstitutedPageBytesRefuseRatherThanDecoding()
    {
        var rows = Expression(WorkOne, ExprFrench, French);
        var honest = XFixture(rows);
        var delivery = honest.Create(string.Empty, string.Empty);
        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            "laws", delivery, CustodyMembership.Floored, out _);
        Assert.IsNotNull(proof);

        // Bytes that are perfectly well-formed for a DIFFERENT expression, standing in for the page
        // this proof was minted over. A caller-supplied row list would have accepted them.
        var substituted = honest.Resolve(delivery.PagesA.Pages[0].Evidence) with
        {
            RetainedPayloadBytes = Encoding.UTF8.GetBytes(
                RowsJson(XProjection, Expression(WorkOne, ExprEnglish, English))),
        };

        var set = new LanguageScopedExpressionSet();
        var decoded = EuLanguageScopedExpressionDecode.TryDecode(
            new EuProofBoundDelivery(
                proof!, delivery, honest.ProfileForTest, delivery.InterpretationProfileRef,
                delivery.CountA.HttpEvidenceRef, [substituted]),
            null,
            SourceObject(),
            set,
            out var refusal,
            out var refusalDetail,
            out _);

        Assert.IsNull(decoded);
        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.ExpressionRowsRefused, refusal);
        Assert.IsNotNull(refusalDetail);
        Assert.AreNotEqual(
            RepeatedEnumerationRowsOpenRefusal.None.ToString(),
            refusalDetail,
            "the refusal must carry the reopen door's own reason rather than a summary of it.");
        Assert.IsEmpty(set.Expressions);
    }

    /// <summary>Every expression's lineage is the delivery's own reopened page receipts.</summary>
    [TestMethod]
    public void LineageIsBuiltFromTheReopenedPagesRatherThanAnyCallerChoice()
    {
        var rows = Expression(WorkOne, ExprFrench, French);
        var fixture = XFixture(rows);
        var delivery = fixture.Create(string.Empty, string.Empty);
        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            "laws", delivery, CustodyMembership.Floored, out _);
        var pages = Pages(fixture, delivery);

        var decoded = EuLanguageScopedExpressionDecode.TryDecode(
            new EuProofBoundDelivery(
                proof!, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
                delivery.CountA.HttpEvidenceRef, pages),
            null,
            SourceObject(),
            new LanguageScopedExpressionSet(),
            out _,
            out _,
            out _);

        Assert.IsNotNull(decoded);
        CollectionAssert.AreEquivalent(
            pages.Select(static page => page.DurableWriteReceipt.Reference.ContentSha256).ToArray(),
            decoded[0].Lineage.Entries.Select(static entry => entry.ContentSha256).ToArray(),
            "the lineage must name exactly the bytes this delivery was reopened from.");
        Assert.IsTrue(decoded[0].Lineage.Entries.All(static entry =>
            entry.Contribution == LanguageScopedExpressionContribution.IdentityAndLanguage));
    }

    /// <summary>
    /// The same rows, delivered over one page and over three, decode to the same expressions - and
    /// each cites every page it was decoded from.
    /// </summary>
    /// <remarks>
    /// Two of the coordinator's invariants meet here. Semantic identity must not depend on which
    /// page held a row, so the content digests must match across the two pagings. And the lineage
    /// must be complete, so the three-page delivery must cite three retained bodies rather than the
    /// first one: a decoder that recorded only <c>PagesInOrder[0]</c> passes every single-page test
    /// in this file and dies here.
    /// </remarks>
    [TestMethod]
    public void TheSameRowsPagedDifferentlyDecodeIdenticallyAndCiteEveryPage()
    {
        var rows = new List<string>();
        rows.AddRange(Expression(WorkOne, ExprEnglish, English));
        rows.AddRange(Expression(WorkOne, ExprFrench, French));
        rows.AddRange(Expression(WorkOne, ExprNorwegian, Norwegian));

        var wide = DecodePaged(rows, rowLimitA: 9, rowLimitB: 4);
        var narrow = DecodePaged(rows, rowLimitA: 3, rowLimitB: 5);

        Assert.IsNotNull(wide);
        Assert.IsNotNull(narrow);
        CollectionAssert.AreEqual(
            wide.Select(static expression => expression.CanonicalContentSha256).ToArray(),
            narrow.Select(static expression => expression.CanonicalContentSha256).ToArray(),
            "how the publisher paged its answer is not part of what it said.");

        Assert.HasCount(1, wide[0].Lineage.Entries, "six rows under a limit of nine is one page.");

        // At a limit of three, rows 0-2 are on page 0 and rows 3-5 on page 1, and a third, empty
        // page terminates the pass. The first expression's rows are both on page 0 and the third's
        // both on page 1, so each cites one page. The second expression's rows are 2 and 3, which
        // fall either side of the boundary: it is the one that legitimately cites two. Nothing
        // cites the empty terminal page.
        Assert.HasCount(1, narrow[0].Lineage.Entries, "its rows are both on the first page.");
        Assert.HasCount(2, narrow[1].Lineage.Entries, "this one straddles the page boundary.");
        Assert.HasCount(1, narrow[2].Lineage.Entries, "and this one is wholly on the second page.");
        Assert.AreNotEqual(
            narrow[0].Lineage.Entries[0].ContentSha256,
            narrow[2].Lineage.Entries[0].ContentSha256,
            "two expressions on different pages must not cite the same body.");
    }

    /// <summary>
    /// A delivery whose row count divides evenly by its page limit ends on an empty page, and no
    /// expression cites it.
    /// </summary>
    /// <remarks>
    /// An empty page stated no identity and no language, so labelling it with either role would be a
    /// claim about bytes that carried nothing. What it does establish - that the enumeration
    /// finished - is a property of the delivery, and the proof-bound delivery already carries it.
    /// </remarks>
    [TestMethod]
    public void ATerminatingEmptyPageIsCitedByNoExpression()
    {
        var rows = new List<string>();
        rows.AddRange(Expression(WorkOne, ExprEnglish, English));
        rows.AddRange(Expression(WorkOne, ExprFrench, French));

        var decoded = DecodePaged(rows, rowLimitA: 2, rowLimitB: 3);

        Assert.IsNotNull(decoded);
        Assert.HasCount(1, decoded[0].Lineage.Entries, "its two rows are both on the first page.");
        Assert.HasCount(1, decoded[1].Lineage.Entries, "and its two on the second.");
        Assert.AreNotEqual(
            decoded[0].Lineage.Entries[0].ContentSha256,
            decoded[1].Lineage.Entries[0].ContentSha256);
    }

    /// <summary>
    /// Under a policy that lets a page before the last be short, page boundaries cannot be derived
    /// from the row limit, and this refuses rather than attributing rows to the wrong bytes.
    /// </summary>
    /// <remarks>
    /// <c>ShortPageTerminal</c> is what makes the arithmetic exact: <c>RequireContinuation</c>
    /// enforces that every page before the last holds exactly the limit.
    /// <c>EmptySuccessorAfterShortPage</c> requires only that a prior page be non-empty and within
    /// the limit, so row <c>i</c> is no longer necessarily on page <c>i / limit</c>. Guessing there
    /// would omit a contributing body, and an incomplete provenance claim is worse than none.
    /// </remarks>
    [TestMethod]
    public void ADeliveryWhosePagePolicyHidesBoundariesRefusesRatherThanGuessing()
    {
        var rows = new List<string>();
        rows.AddRange(Expression(WorkOne, ExprEnglish, English));
        rows.AddRange(Expression(WorkOne, ExprFrench, French));

        var decoded = DecodePaged(
            rows,
            rowLimitA: 3,
            rowLimitB: 5,
            RepeatedEnumerationTerminalPagePolicy.EmptySuccessorAfterShortPage,
            out var refusal);

        Assert.IsNull(decoded);
        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.PageAttributionUnavailable, refusal);
    }

    // ---- The publisher's date, and its own lineage. ----

    [TestMethod]
    public void ThePublisherWorkDateIsCarriedWithItsOwnLexicalFormAndDatatype()
    {
        var decoded = DecodeWithDates(
            Expression(WorkOne, ExprFrench, French),
            [PRow(WorkOne, WorkDateIri, "2016-05-04", XsdDate)],
            out var refusal);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.AreEqual("2016-05-04", decoded[0].PublisherCorrigendumDate!.RawLexical);
        Assert.AreEqual(XsdDate, decoded[0].PublisherCorrigendumDate!.DatatypeIri);
    }

    /// <summary>
    /// The datatype is the publisher's, not this build's guess. A test asserting <c>xsd:date</c>
    /// cannot see a substituted <c>xsd:date</c>, so this one uses a datatype nothing here would pick.
    /// </summary>
    [TestMethod]
    public void ThePublisherDateDatatypeIsCarriedRatherThanAssumed()
    {
        const string GYearMonth = "http://www.w3.org/2001/XMLSchema#gYearMonth";
        var decoded = DecodeWithDates(
            Expression(WorkOne, ExprFrench, French),
            [PRow(WorkOne, WorkDateIri, "2016-05", GYearMonth)],
            out _);

        Assert.IsNotNull(decoded);
        Assert.AreEqual(GYearMonth, decoded[0].PublisherCorrigendumDate!.DatatypeIri);
    }

    /// <summary>
    /// A dated expression cites the date family's own retained bytes, distinguished by role. This is
    /// the multi-family lineage the carrier was amended to express: one receipt could not say it.
    /// </summary>
    [TestMethod]
    public void ADatedExpressionCitesTheDateFamilysOwnRetainedBytes()
    {
        var decoded = DecodeWithDates(
            Expression(WorkOne, ExprFrench, French),
            [PRow(WorkOne, WorkDateIri, "2016-05-04", XsdDate)],
            out _);

        Assert.IsNotNull(decoded);
        var lineage = decoded[0].Lineage;
        Assert.IsTrue(lineage.CarriesDateContribution);
        Assert.HasCount(
            1,
            lineage.Entries.Where(static entry =>
                entry.Contribution == LanguageScopedExpressionContribution.PublisherDate),
            "the date's own bytes, cited as the date's.");
        Assert.HasCount(
            1,
            lineage.Entries.Where(static entry =>
                entry.Contribution == LanguageScopedExpressionContribution.IdentityAndLanguage));
        Assert.AreNotEqual(
            lineage.Entries[0].ContentSha256,
            lineage.Entries[1].ContentSha256,
            "two families, two retained bodies.");
    }

    /// <summary>
    /// Date-lineage loss, as a test. An expression whose Work the publisher dated nothing for must
    /// cite no date bytes, even though the date family was delivered and read.
    /// </summary>
    [TestMethod]
    public void AnUndatedExpressionCitesNoDateBytesEvenWhenTheDateFamilyWasRead()
    {
        var decoded = DecodeWithDates(
            [.. Expression(WorkOne, ExprFrench, French), .. Expression(WorkTwo, ExprOfWorkTwo, English)],
            [PRow(WorkOne, WorkDateIri, "2016-05-04", XsdDate)],
            out _);

        Assert.IsNotNull(decoded);
        Assert.IsNotNull(decoded[0].PublisherCorrigendumDate);
        Assert.IsTrue(decoded[0].Lineage.CarriesDateContribution);

        Assert.IsNull(
            decoded[1].PublisherCorrigendumDate,
            "the other Work's date must not have been attached to this one.");
        Assert.IsFalse(
            decoded[1].Lineage.CarriesDateContribution,
            "and it must not cite the bytes of a family that did not date it.");
    }

    [TestMethod]
    public void EachExpressionTakesTheDateOfItsOwnWorkAndNoOther()
    {
        var decoded = DecodeWithDates(
            [.. Expression(WorkOne, ExprFrench, French), .. Expression(WorkTwo, ExprOfWorkTwo, English)],
            [
                PRow(WorkOne, WorkDateIri, "2016-05-04", XsdDate),
                PRow(WorkTwo, WorkDateIri, "2018-11-21", XsdDate),
            ],
            out _);

        Assert.IsNotNull(decoded);
        Assert.AreEqual("2016-05-04", decoded[0].PublisherCorrigendumDate!.RawLexical);
        Assert.AreEqual("2018-11-21", decoded[1].PublisherCorrigendumDate!.RawLexical);
    }

    [TestMethod]
    public void AnAbsentWorkDateStaysAbsentRatherThanDefaulting()
    {
        var decoded = Decode(
            new LanguageScopedExpressionSet(),
            Expression(WorkOne, ExprFrench, French),
            out _,
            out _);

        Assert.IsNotNull(decoded);
        Assert.IsNull(decoded[0].PublisherCorrigendumDate);
        Assert.IsFalse(decoded[0].Lineage.CarriesDateContribution);
    }

    /// <summary>
    /// An unbound date row is the publisher's typed absence, not a date.
    /// </summary>
    /// <remarks>
    /// This test existed before the provenance repair and was lost when this file was rewritten onto
    /// proof-bound deliveries. The mutation pass caught its absence - "an unbound date row treated as
    /// a date" survived - which is the only reason it is back. Dropping a guard while rewriting the
    /// file around it is exactly the failure a deletion-grade pass is for.
    /// </remarks>
    [TestMethod]
    public void AnUnboundWorkDateRowIsAnAbsenceRatherThanADate()
    {
        var decoded = DecodeWithDates(
            Expression(WorkOne, ExprFrench, French),
            [PUnboundRow(WorkOne, WorkDateIri)],
            out var refusal,
            objectFactsKey: ["object", "predicate"]);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.IsNull(decoded[0].PublisherCorrigendumDate);
        Assert.IsFalse(
            decoded[0].Lineage.CarriesDateContribution,
            "an absence cites no bytes as having stated a date.");
    }

    [TestMethod]
    public void TwoDifferentDatesForOneWorkRefuseRatherThanPickingOne()
    {
        var decoded = DecodeWithDates(
            Expression(WorkOne, ExprFrench, French),
            [
                PRow(WorkOne, WorkDateIri, "2016-05-04", XsdDate),
                PRow(WorkOne, WorkDateIri, "2016-05-05", XsdDate),
            ],
            out var refusal);

        Assert.IsNull(decoded);
        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.ConflictingWorkDate, refusal);
    }

    [TestMethod]
    public void ADateBoundToAnIriRefusesRatherThanBeingCoerced()
    {
        var decoded = DecodeWithDates(
            Expression(WorkOne, ExprFrench, French),
            [PIriRow(WorkOne, WorkDateIri, WorkTwo)],
            out var refusal);

        Assert.IsNull(decoded);
        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.WorkDateRowTermKindMismatch, refusal);
    }

    /// <summary>
    /// Family P legitimately describes objects this delivery has no Expression of. Refusing those
    /// would refuse correct publisher data, so they are out of scope rather than dropped.
    /// </summary>
    [TestMethod]
    public void ObjectFactRowsAboutOtherSubjectsAndOtherPredicatesAreNotRefused()
    {
        var decoded = DecodeWithDates(
            Expression(WorkOne, ExprFrench, French),
            [
                PRow(WorkTwo, WorkDateIri, "2018-11-21", XsdDate),
                PRow(WorkOne, CelexIri, "32016R0679", XsdString),
            ],
            out var refusal);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.IsNull(decoded[0].PublisherCorrigendumDate, "the other Work's date is not this one's.");
        Assert.IsFalse(decoded[0].Lineage.CarriesDateContribution);
    }

    /// <summary>
    /// The date cites the page that actually stated it, not the date family's first page.
    /// </summary>
    /// <remarks>
    /// Every other date test here delivers a single-page date family, where citing "the first page"
    /// and citing "the page that stated it" are the same bytes and no test can tell them apart. The
    /// date row below is deliberately on the second page.
    /// </remarks>
    [TestMethod]
    public void TheDateCitesThePageThatStatedItRatherThanTheFirst()
    {
        // Two rows this door does not read, then the date. At a limit of two the date is on page 1.
        var objectRows = new[]
        {
            PRow(WorkTwo, CelexIri, "32018R1725", XsdString),
            PRow(WorkOne, CelexIri, "32016R0679", XsdString),
            PRow(WorkOne, WorkDateIri, "2016-05-04", XsdDate),
        };

        var dateFacts = BoundPagedObjectFacts(objectRows, rowLimitA: 2, rowLimitB: 3);
        var decoded = EuLanguageScopedExpressionDecode.TryDecode(
            Bound(Expression(WorkOne, ExprFrench, French)),
            dateFacts,
            SourceObject(),
            new LanguageScopedExpressionSet(),
            out var refusal,
            out _,
            out _);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        var dateEntry = decoded[0].Lineage.Entries.Single(static entry =>
            entry.Contribution == LanguageScopedExpressionContribution.PublisherDate);
        Assert.AreEqual(
            dateFacts.PagesInOrder[1].DurableWriteReceipt.Reference.ContentSha256,
            dateEntry.ContentSha256,
            "the date was stated on the second page, and that is the body that stated it.");
        Assert.AreNotEqual(
            dateFacts.PagesInOrder[0].DurableWriteReceipt.Reference.ContentSha256,
            dateEntry.ContentSha256);
    }

    // ---- Language: never defaulted, never merged. ----

    [TestMethod]
    public void AnExpressionWithNoLanguageRefusesRatherThanDefaulting()
    {
        var decoded = Decode(
            new LanguageScopedExpressionSet(),
            [XRow(WorkOne, ExprEnglish, BelongsToWorkIri, WorkOne)],
            out var refusal,
            out var offending);

        Assert.IsNull(decoded);
        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.ExpressionLanguageMissing, refusal);
        Assert.AreEqual(ExprEnglish, offending);
    }

    [TestMethod]
    public void TwoLanguagesForOneExpressionRefuseRatherThanMerging()
    {
        var decoded = Decode(
            new LanguageScopedExpressionSet(),
            [
                .. Expression(WorkOne, ExprEnglish, English),
                XRow(WorkOne, ExprEnglish, UsesLanguageIri, French),
            ],
            out var refusal,
            out _);

        Assert.IsNull(decoded);
        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.ConflictingExpressionLanguage, refusal);
    }

    /// <summary>Two language IRIs differing only in case are two different IRIs.</summary>
    [TestMethod]
    public void TwoLanguageIrisDifferingOnlyByCaseAreNotOneLanguage()
    {
        var decoded = Decode(
            new LanguageScopedExpressionSet(),
            [
                .. Expression(WorkOne, ExprEnglish, English),
                XRow(WorkOne, ExprEnglish, UsesLanguageIri, LanguageBase + "eng"),
            ],
            out var refusal,
            out _);

        Assert.IsNull(decoded);
        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.ConflictingExpressionLanguage, refusal);
    }

    // ---- Closure. ----

    [TestMethod]
    public void AnExpressionWithoutItsOwnBelongsToWorkRowRefuses()
    {
        var decoded = Decode(
            new LanguageScopedExpressionSet(),
            [XRow(WorkOne, ExprEnglish, UsesLanguageIri, English)],
            out var refusal,
            out var offending);

        Assert.IsNull(decoded);
        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.ExpressionSubjectNotSelfClosed, refusal);
        Assert.AreEqual(ExprEnglish, offending);
    }

    [TestMethod]
    public void ABelongsToWorkValueDisagreeingWithTheParentColumnRefuses()
    {
        var decoded = Decode(
            new LanguageScopedExpressionSet(),
            [
                XRow(WorkOne, ExprEnglish, BelongsToWorkIri, WorkTwo),
                XRow(WorkOne, ExprEnglish, UsesLanguageIri, English),
            ],
            out var refusal,
            out var offending);

        Assert.IsNull(decoded);
        Assert.AreEqual(
            EuLanguageScopedExpressionDecodeRefusal.ExpressionWorkDisagreesWithBelongsToWork, refusal);
        Assert.AreEqual(WorkTwo, offending);
    }

    [TestMethod]
    public void ATitleRowOnAnAdmittedExpressionAddsNoSecondExpression()
    {
        var decoded = Decode(
            new LanguageScopedExpressionSet(),
            [
                .. Expression(WorkOne, ExprFrench, French),
                XLiteralRow(WorkOne, ExprFrench, TitleIri, "Rectificatif", XsdString),
            ],
            out var refusal,
            out _);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.HasCount(1, decoded);
    }

    // ---- Atomicity and replay. ----

    [TestMethod]
    public void ARefusedDeliveryLeavesTheDestinationSetUntouched()
    {
        var set = new LanguageScopedExpressionSet();
        Decode(set, Expression(WorkOne, ExprEnglish, English), out _, out _);
        Assert.HasCount(1, set.Expressions);

        var decoded = Decode(
            set,
            [
                .. Expression(WorkOne, ExprFrench, French),
                XRow(WorkOne, ExprNorwegian, UsesLanguageIri, Norwegian),
            ],
            out var refusal,
            out _);

        Assert.IsNull(decoded);
        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.ExpressionSubjectNotSelfClosed, refusal);
        Assert.HasCount(
            1, set.Expressions, "the admissible French expression must not have been appended.");
    }

    [TestMethod]
    public void ReplayingOneDeliveryConvergesRatherThanGrowingTheStore()
    {
        var set = new LanguageScopedExpressionSet();
        var rows = Expression(WorkOne, ExprFrench, French);

        Assert.IsNotNull(Decode(set, rows, out _, out _));
        Assert.IsNotNull(Decode(set, rows, out var refusal, out _));

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.HasCount(1, set.Expressions);
    }

    /// <summary>
    /// A conflicting re-presentation is found before the first append, so nothing lands halfway.
    /// The second delivery says the same expression is in a different language.
    /// </summary>
    [TestMethod]
    public void AConflictWithAHeldExpressionRefusesWithoutAppendingTheAdmissibleOnes()
    {
        var set = new LanguageScopedExpressionSet();
        Decode(set, Expression(WorkOne, ExprFrench, French), out _, out _);
        Assert.HasCount(1, set.Expressions);

        var decoded = Decode(
            set,
            [.. Expression(WorkOne, ExprEnglish, English), .. Expression(WorkOne, ExprFrench, Norwegian)],
            out var refusal,
            out _);

        Assert.IsNull(decoded);
        Assert.AreEqual(
            EuLanguageScopedExpressionDecodeRefusal.AppendConflictsWithHeldExpression, refusal);
        Assert.HasCount(
            1,
            set.Expressions,
            "the English expression preceded the conflicting one and must not have landed.");
    }

    [TestMethod]
    public void EveryNullArgumentIsACallerContractViolation()
    {
        var set = new LanguageScopedExpressionSet();
        Assert.ThrowsExactly<ArgumentNullException>(() => EuLanguageScopedExpressionDecode.TryDecode(
            null!, null, SourceObject(), set, out _, out _, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => EuLanguageScopedExpressionDecode.TryDecode(
            Bound(Expression(WorkOne, ExprFrench, French)), null, null!, set, out _, out _, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => EuLanguageScopedExpressionDecode.TryDecode(
            Bound(Expression(WorkOne, ExprFrench, French)), null, SourceObject(), null!,
            out _, out _, out _));
    }

    /// <summary>
    /// There is no parameter by which a caller asserts provenance. On the first head this claim was
    /// written in the source while the door stood open; it is asserted here instead.
    /// </summary>
    [TestMethod]
    public void TheDecodeSurfaceTakesNoRowsNoReceiptAndNoProvenanceAssertion()
    {
        var parameters = typeof(EuLanguageScopedExpressionDecode)
            .GetMethod(nameof(EuLanguageScopedExpressionDecode.TryDecode))!
            .GetParameters();

        Assert.IsEmpty(
            parameters.Where(static parameter =>
                parameter.ParameterType == typeof(bool) ||
                parameter.ParameterType == typeof(DurableBlobWriteReceipt) ||
                parameter.ParameterType == typeof(IReadOnlyList<RepeatedEnumerationRow>) ||
                parameter.HasDefaultValue),
            "rows, a receipt, a boolean or a defaulted parameter would each be a caller's claim.");
    }

    [TestMethod]
    public void EveryRefusalHasItsExactWireToken()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "\"none\"",
                "\"expression_row_term_kind_mismatch\"",
                "\"expression_subject_not_self_closed\"",
                "\"expression_work_disagrees_with_belongs_to_work\"",
                "\"expression_language_missing\"",
                "\"conflicting_expression_language\"",
                "\"work_date_row_term_kind_mismatch\"",
                "\"conflicting_work_date\"",
                "\"append_conflicts_with_held_expression\"",
                "\"expression_rows_refused\"",
                "\"date_rows_refused\"",
                "\"page_attribution_unavailable\"",
            },
            Enum.GetValues<EuLanguageScopedExpressionDecodeRefusal>()
                .Select(member => ContractJson.Serialize(member))
                .ToArray());
    }

    // ---- Fixtures. Every delivery below is minted, proven and reopened. ----

    private static IReadOnlyList<LanguageScopedExpression>? Decode(
        LanguageScopedExpressionSet into,
        IReadOnlyList<string> expressionRows,
        out EuLanguageScopedExpressionDecodeRefusal refusal,
        out string? offendingIri) =>
        EuLanguageScopedExpressionDecode.TryDecode(
            Bound(expressionRows), null, SourceObject(), into, out refusal, out _, out offendingIri);

    private static IReadOnlyList<LanguageScopedExpression>? DecodeWithDates(
        IReadOnlyList<string> expressionRows,
        IReadOnlyList<string> objectRows,
        out EuLanguageScopedExpressionDecodeRefusal refusal,
        IReadOnlyList<string>? objectFactsKey = null) =>
        EuLanguageScopedExpressionDecode.TryDecode(
            Bound(expressionRows),
            BoundObjectFacts(objectRows, objectFactsKey),
            SourceObject(),
            new LanguageScopedExpressionSet(),
            out refusal,
            out _,
            out _);

    private static IReadOnlyList<LanguageScopedExpression>? DecodePaged(
        IReadOnlyList<string> rows,
        int rowLimitA,
        int rowLimitB,
        RepeatedEnumerationTerminalPagePolicy terminalPagePolicy,
        out EuLanguageScopedExpressionDecodeRefusal refusal) =>
        DecodePagedCore(rows, rowLimitA, rowLimitB, terminalPagePolicy, out refusal);

    private static IReadOnlyList<LanguageScopedExpression>? DecodePaged(
        IReadOnlyList<string> rows, int rowLimitA, int rowLimitB) =>
        DecodePagedCore(
            rows, rowLimitA, rowLimitB,
            RepeatedEnumerationTerminalPagePolicy.ShortPageTerminal, out _);

    private static IReadOnlyList<LanguageScopedExpression>? DecodePagedCore(
        IReadOnlyList<string> rows,
        int rowLimitA,
        int rowLimitB,
        RepeatedEnumerationTerminalPagePolicy terminalPagePolicy,
        out EuLanguageScopedExpressionDecodeRefusal refusal)
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture(
            expectedCount: rows.Count,
            maximumDeliverableRows: 999,
            terminalPagePolicy: terminalPagePolicy,
            projectionVariables: XProjection,
            canonicalKeyVariables: XKey);
        var delivery = fixture.CreatePagedRaw(
            rows.Count,
            rowLimitA,
            rowLimitB,
            (first, take) => RowsJson(XProjection, [.. rows.Skip(first).Take(take)], first),
            Cursor);
        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            "laws", delivery, CustodyMembership.Floored, out var proofRefusal);
        Assert.IsNotNull(proof, $"the fixture must mint an admitting proof: {proofRefusal}");

        return EuLanguageScopedExpressionDecode.TryDecode(
            new EuProofBoundDelivery(
                proof!, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
                delivery.CountA.HttpEvidenceRef, Pages(fixture, delivery)),
            null,
            SourceObject(),
            new LanguageScopedExpressionSet(),
            out refusal,
            out _,
            out _);
    }

    private static EuProofBoundDelivery BoundPagedObjectFacts(
        IReadOnlyList<string> rows, int rowLimitA, int rowLimitB)
    {
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture(
            expectedCount: rows.Count,
            maximumDeliverableRows: 999,
            projectionVariables: PProjection,
            canonicalKeyVariables: PKey);
        var delivery = fixture.CreatePagedRaw(
            rows.Count,
            rowLimitA,
            rowLimitB,
            (first, take) => RowsJson(PProjection, [.. rows.Skip(first).Take(take)], first),
            Cursor);
        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            "laws", delivery, CustodyMembership.Floored, out var proofRefusal);
        Assert.IsNotNull(proof, $"the fixture must mint an admitting proof: {proofRefusal}");
        return new EuProofBoundDelivery(
            proof!, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, Pages(fixture, delivery));
    }

    private static EuProofBoundDelivery Bound(IReadOnlyList<string> rows)
    {
        var fixture = XFixture(rows);
        var delivery = fixture.Create(string.Empty, string.Empty);
        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            "laws", delivery, CustodyMembership.Floored, out var proofRefusal);
        Assert.IsNotNull(proof, $"the fixture must mint an admitting proof: {proofRefusal}");
        return new EuProofBoundDelivery(
            proof!, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, Pages(fixture, delivery));
    }

    /// <param name="canonicalKey">
    /// Defaults to a key including <c>value</c>, which is what lets two rows share a subject and
    /// predicate while stating different dates. A delivery carrying an UNBOUND value cannot key on
    /// it - <c>VerifyPages</c> requires canonical-key components to be bound - so the unbound-date
    /// test passes a narrower key.
    /// </param>
    private static EuProofBoundDelivery BoundObjectFacts(
        IReadOnlyList<string> rows, IReadOnlyList<string>? canonicalKey = null)
    {
        var body = RowsJson(PProjection, rows);
        var fixture = new RepeatedEnumerationDeliveryProofTests.Fixture(
            rawRowsA: body,
            rawRowsB: body,
            expectedCount: rows.Count,
            projectionVariables: PProjection,
            canonicalKeyVariables: canonicalKey ?? PKey);
        var delivery = fixture.Create(string.Empty, string.Empty);
        var proof = AbsenceFamilyEnumerationProof.TryCreate(
            "laws", delivery, CustodyMembership.Floored, out var proofRefusal);
        Assert.IsNotNull(proof, $"the fixture must mint an admitting proof: {proofRefusal}");
        return new EuProofBoundDelivery(
            proof!, delivery, fixture.ProfileForTest, delivery.InterpretationProfileRef,
            delivery.CountA.HttpEvidenceRef, Pages(fixture, delivery));
    }

    private static RepeatedEnumerationDeliveryProofTests.Fixture XFixture(IReadOnlyList<string> rows)
    {
        var body = RowsJson(XProjection, rows);
        return new RepeatedEnumerationDeliveryProofTests.Fixture(
            rawRowsA: body,
            rawRowsB: body,
            expectedCount: rows.Count,
            projectionVariables: XProjection,
            canonicalKeyVariables: XKey);
    }

    private static IReadOnlyList<RepeatedEnumerationResolvedEvidence> Pages(
        RepeatedEnumerationDeliveryProofTests.Fixture fixture,
        EnumerationDeliveryComparison delivery) =>
        [.. delivery.PagesA.Pages
            .OrderBy(static page => page.Ordinal)
            .Select(page => fixture.Resolve(page.Evidence))];

    // ---- Row bodies. ----

    /// <summary>One Expression's complete family-X rows: belongs-to-work plus a language.</summary>
    private static IReadOnlyList<string> Expression(
        string parentIri, string expressionIri, string languageAuthorityIri) =>
        [
            XRow(parentIri, expressionIri, BelongsToWorkIri, parentIri),
            XRow(parentIri, expressionIri, UsesLanguageIri, languageAuthorityIri),
        ];

    private static string XRow(string parentIri, string expressionIri, string predicateIri, string valueIri) =>
        Binding(
            ("parent", Uri(parentIri)),
            ("object", Uri(expressionIri)),
            ("predicate", Uri(predicateIri)),
            ("value", Uri(valueIri)),
            ("value_kind", Literal("iri", null)),
            ("datatype_iri", Literal(string.Empty, null)),
            ("language_tag", Literal(string.Empty, null)));

    private static string XLiteralRow(
        string parentIri, string expressionIri, string predicateIri, string value, string datatype) =>
        Binding(
            ("parent", Uri(parentIri)),
            ("object", Uri(expressionIri)),
            ("predicate", Uri(predicateIri)),
            ("value", Literal(value, datatype)),
            ("value_kind", Literal("literal", null)),
            ("datatype_iri", Literal(datatype, null)),
            ("language_tag", Literal(string.Empty, null)));

    private static string PRow(string objectIri, string predicateIri, string value, string datatype) =>
        Binding(
            ("object", Uri(objectIri)),
            ("predicate", Uri(predicateIri)),
            ("value", Literal(value, datatype)),
            ("value_kind", Literal("literal", null)));

    /// <summary>An asked-and-unanswered object-facts row: the value variable is simply absent.</summary>
    private static string PUnboundRow(string objectIri, string predicateIri) =>
        Binding(
            ("object", Uri(objectIri)),
            ("predicate", Uri(predicateIri)),
            ("value_kind", Literal("unbound", null)));

    private static string PIriRow(string objectIri, string predicateIri, string valueIri) =>
        Binding(
            ("object", Uri(objectIri)),
            ("predicate", Uri(predicateIri)),
            ("value", Uri(valueIri)),
            ("value_kind", Literal("iri", null)));

    private static string Uri(string value) => $"{{\"type\":\"uri\",\"value\":{Json(value)}}}";

    private static string Literal(string value, string? datatype) =>
        datatype is null
            ? $"{{\"type\":\"literal\",\"value\":{Json(value)}}}"
            : $"{{\"type\":\"literal\",\"datatype\":{Json(datatype)},\"value\":{Json(value)}}}";

    private static string Binding(params (string Name, string Term)[] terms) =>
        string.Join(',', terms.Select(static term => $"{Json(term.Name)}:{term.Term}"));

    /// <summary>
    /// The delivered page body. The cursor is appended per row here rather than by each row builder,
    /// so it is unique by construction and no fixture row can accidentally share one.
    /// </summary>
    private static string RowsJson(
        IReadOnlyList<string> projection, IReadOnlyList<string> rows, int firstRowIndex = 0) =>
        "{\"head\":{\"link\":[],\"vars\":[" +
        string.Join(',', projection.Select(Json)) +
        "]},\"results\":{\"distinct\":false,\"ordered\":true,\"bindings\":[" +
        string.Join(
            ',',
            rows.Select((row, index) =>
                "{" + row + ",\"cursor\":{\"type\":\"literal\",\"value\":\""
                + Cursor(firstRowIndex + index) + "\"}}")) +
        "]}}";

    /// <summary>
    /// Zero-padded on purpose. Cursors are compared as text, so unpadded ordinals put "10" before
    /// "9" from the tenth row on and the delivery refuses for a reason that has nothing to do with
    /// what is being tested.
    /// </summary>
    private static string Cursor(int rowIndex) => rowIndex.ToString("D4");

    private static string Json(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static SourceObjectRef SourceObject() => new(
        SourceCoreSchemaIds.SourceObjectRef,
        SourceAuthority.Cellar,
        new SourceRegistryMemberRef(ArtifactRef('b', '2'), "eu-object-facts"),
        WorkOne,
        "cellar|work|32016R0679",
        Sha256("cellar|work|32016R0679"),
        ArtifactRef('a', '1'),
        parentKeyRef: null);

    private static SourceArtifactRef ArtifactRef(char resourceFill, char digestFill) => new(
        $"urn:uuid:{new string(resourceFill, 8)}-{new string(resourceFill, 4)}-4{new string(resourceFill, 3)}-8{new string(resourceFill, 3)}-{new string(resourceFill, 12)}",
        new string(digestFill, 64));

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
