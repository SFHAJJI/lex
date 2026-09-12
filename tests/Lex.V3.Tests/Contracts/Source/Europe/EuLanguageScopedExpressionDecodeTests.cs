using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The EU language-scoped expression decode: the representation B31-L0071 says the corpus lacks, and
/// the ways a decoder could quietly reacquire the merge it exists to remove.
/// </summary>
/// <remarks>
/// The tests that matter most here are not the happy paths. They are the ones that fail if language
/// is ever folded into identity, if an unmappable language is ever defaulted, if a date is ever
/// attached to an expression whose Work the publisher did not state it for, or if a refused delivery
/// ever leaves an append-only store half written.
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
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
    private const string ObservedAtText = "2026-01-02T03:04:05.0000000+00:00";

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

    private static readonly RepeatedEnumerationInterpretationProfile ObjectFactsProfile =
        EuObjectFactsDiscoveryPlan.Create().CreateDeliveryProfile(EuObjectFactsQuerySet.ObjectFacts);
    private static readonly RepeatedEnumerationInterpretationProfile ExpressionFactsProfile =
        EuObjectFactsDiscoveryPlan.Create().CreateDeliveryProfile(EuObjectFactsQuerySet.ExpressionFacts);

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
            [.. ExpressionRows(WorkOne, ExprEnglish, English), .. ExpressionRows(WorkOne, ExprFrench, French)],
            out var refusal,
            out _);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.HasCount(2, decoded, "the older fold reports one observation for this delivery.");
        CollectionAssert.AreEqual(
            new[] { English, French },
            decoded.Select(static expression => expression.OfficialLanguage).ToArray(),
            "both languages, each as itself.");
    }

    /// <summary>The exact defect: a second same-language expression of one Work.</summary>
    [TestMethod]
    public void TwoSameLanguageCorrigendumExpressionsOfOneWorkBothSurvive()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            [
                .. ExpressionRows(WorkOne, ExprFrench, French),
                .. ExpressionRows(WorkOne, ExprFrenchCorrigendumTwo, French),
            ],
            out var refusal,
            out _);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.HasCount(
            2, decoded, "a language-keyed identity would have folded the second corrigendum away.");
        Assert.HasCount(2, set.Expressions);
    }

    /// <summary>
    /// The 385 corrigenda with no ENG or FRA counterpart: a language with no scope enum member
    /// survives, verbatim.
    /// </summary>
    [TestMethod]
    public void ALanguageOutsideTheClosedScopeEnumSurvivesVerbatim()
    {
        Assert.IsFalse(
            Enum.GetNames<EuOfficialLanguage>().Any(name => name.Contains("Norwegian", StringComparison.Ordinal)),
            "this test is only meaningful while NOR is outside the closed twenty-four.");

        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(set, ExpressionRows(WorkOne, ExprNorwegian, Norwegian), out var refusal, out _);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.AreEqual(
            Norwegian,
            decoded[0].OfficialLanguage,
            "mapping onto the closed enum would have defaulted or dropped this.");
    }

    /// <summary>Identity is the publisher's Work and Expression, and neither is the language.</summary>
    [TestMethod]
    public void IdentityIsThePublisherWorkAndExpressionAndCarriesNoLanguage()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(set, ExpressionRows(WorkOne, ExprFrench, French), out _, out _);

        Assert.IsNotNull(decoded);
        Assert.AreEqual(WorkOne, decoded[0].Identity.PublisherWorkId);
        Assert.AreEqual(ExprFrench, decoded[0].Identity.PublisherExpressionId);
        Assert.IsFalse(
            decoded[0].Identity.ToString()!.Contains(French, StringComparison.Ordinal),
            "the language must not have reached the identity.");
    }

    // ---- Language: never defaulted, never merged. ----

    [TestMethod]
    public void AnExpressionWithNoLanguageRefusesRatherThanDefaulting()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            [XBoundRow(WorkOne, ExprEnglish, BelongsToWorkIri, new PValue(WorkOne))],
            out var refusal,
            out var offending);

        Assert.IsNull(decoded);
        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.ExpressionLanguageMissing, refusal);
        Assert.AreEqual(ExprEnglish, offending);
        Assert.IsEmpty(set.Expressions);
    }

    [TestMethod]
    public void TwoLanguagesForOneExpressionRefuseRatherThanMerging()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            [
                .. ExpressionRows(WorkOne, ExprEnglish, English),
                XBoundRow(WorkOne, ExprEnglish, UsesLanguageIri, new PValue(French)),
            ],
            out var refusal,
            out var offending);

        Assert.IsNull(decoded);
        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.ConflictingExpressionLanguage, refusal);
        Assert.AreEqual(ExprEnglish, offending);
    }

    [TestMethod]
    public void RepresentingOneLanguageTwiceIsNotAConflict()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            [
                .. ExpressionRows(WorkOne, ExprEnglish, English),
                XBoundRow(WorkOne, ExprEnglish, UsesLanguageIri, new PValue(English)),
            ],
            out var refusal,
            out _);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.HasCount(1, decoded);
    }

    // ---- Closure: X proves its own, and the two columns must agree. ----

    [TestMethod]
    public void AnExpressionWithoutItsOwnBelongsToWorkRowRefuses()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            [XBoundRow(WorkOne, ExprEnglish, UsesLanguageIri, new PValue(English))],
            out var refusal,
            out var offending);

        Assert.IsNull(decoded);
        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.ExpressionSubjectNotSelfClosed, refusal);
        Assert.AreEqual(ExprEnglish, offending);
    }

    [TestMethod]
    public void ABelongsToWorkValueDisagreeingWithTheParentColumnRefuses()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            [
                XBoundRow(WorkOne, ExprEnglish, BelongsToWorkIri, new PValue(WorkTwo)),
                XBoundRow(WorkOne, ExprEnglish, UsesLanguageIri, new PValue(English)),
            ],
            out var refusal,
            out var offending);

        Assert.IsNull(decoded);
        Assert.AreEqual(
            EuLanguageScopedExpressionDecodeRefusal.ExpressionWorkDisagreesWithBelongsToWork, refusal);
        Assert.AreEqual(WorkTwo, offending);
    }

    [TestMethod]
    public void AFamilyXRowWhoseShapeBreaksItsProfileRefuses()
    {
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Literal(WorkOne, XsdString, null),
            RepeatedEnumerationRdfTerm.Iri(ExprEnglish),
            RepeatedEnumerationRdfTerm.Iri(BelongsToWorkIri),
            RepeatedEnumerationRdfTerm.Iri(WorkOne),
            RepeatedEnumerationRdfTerm.Literal("iri", null, null),
            RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
            RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
            RepeatedEnumerationRdfTerm.Literal("1", XsdInteger, null),
            RepeatedEnumerationRdfTerm.Literal(ExprEnglish, null, null),
            RepeatedEnumerationRdfTerm.Literal(BelongsToWorkIri, null, null),
            RepeatedEnumerationRdfTerm.Literal("iri", null, null),
            RepeatedEnumerationRdfTerm.Literal(WorkOne, null, null),
            RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
            RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
        };
        var row = new RepeatedEnumerationRow(
            Array.AsReadOnly(terms), Array.AsReadOnly(terms[0..4]), Array.AsReadOnly(terms[8..14]));

        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(set, [row], out var refusal, out _);

        Assert.IsNull(decoded);
        Assert.AreEqual(
            EuLanguageScopedExpressionDecodeRefusal.ExpressionRowTermKindMismatch,
            refusal,
            "a parent bound to a literal is not the shape family X promises.");
    }

    // ---- The publisher's date: carried, attributed to its own Work, never invented. ----

    [TestMethod]
    public void ThePublisherWorkDateIsCarriedWithItsOwnLexicalFormAndDatatype()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            ExpressionRows(WorkOne, ExprFrench, French),
            [PBoundRow(WorkOne, WorkDateIri, new PValue("2016-05-04", IsIri: false, Datatype: XsdDate))],
            out var refusal,
            out _);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.IsNotNull(decoded[0].PublisherCorrigendumDate);
        Assert.AreEqual("2016-05-04", decoded[0].PublisherCorrigendumDate!.RawLexical);
        Assert.AreEqual(XsdDate, decoded[0].PublisherCorrigendumDate!.DatatypeIri);
    }

    /// <summary>
    /// The guard that stops a date wandering: each expression takes the date of its own Work, and
    /// a decoder that took "the date" rather than "this Work's date" dies here.
    /// </summary>
    [TestMethod]
    public void EachExpressionTakesTheDateOfItsOwnWorkAndNoOther()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            [.. ExpressionRows(WorkOne, ExprFrench, French), .. ExpressionRows(WorkTwo, ExprOfWorkTwo, English)],
            [
                PBoundRow(WorkOne, WorkDateIri, new PValue("2016-05-04", IsIri: false, Datatype: XsdDate)),
                PBoundRow(WorkTwo, WorkDateIri, new PValue("2018-11-21", IsIri: false, Datatype: XsdDate)),
            ],
            out var refusal,
            out _);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.AreEqual("2016-05-04", decoded[0].PublisherCorrigendumDate!.RawLexical);
        Assert.AreEqual("2018-11-21", decoded[1].PublisherCorrigendumDate!.RawLexical);
    }

    /// <summary>A Work the publisher dated nothing for keeps no date, and borrows none.</summary>
    [TestMethod]
    public void AnExpressionWhoseWorkHasNoDateKeepsNoneAndBorrowsNone()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            [.. ExpressionRows(WorkOne, ExprFrench, French), .. ExpressionRows(WorkTwo, ExprOfWorkTwo, English)],
            [PBoundRow(WorkOne, WorkDateIri, new PValue("2016-05-04", IsIri: false, Datatype: XsdDate))],
            out _,
            out _);

        Assert.IsNotNull(decoded);
        Assert.IsNotNull(decoded[0].PublisherCorrigendumDate);
        Assert.IsNull(
            decoded[1].PublisherCorrigendumDate,
            "the other Work's date must not have been attached to this one.");
    }

    [TestMethod]
    public void AnAbsentWorkDateStaysAbsentRatherThanDefaulting()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(set, ExpressionRows(WorkOne, ExprFrench, French), out _, out _);

        Assert.IsNotNull(decoded);
        Assert.IsNull(decoded[0].PublisherCorrigendumDate);
    }

    [TestMethod]
    public void AnUnboundWorkDateRowIsAnAbsenceRatherThanADate()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            ExpressionRows(WorkOne, ExprFrench, French),
            [PUnboundRow(WorkOne, WorkDateIri)],
            out var refusal,
            out _);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.IsNull(decoded[0].PublisherCorrigendumDate);
    }

    [TestMethod]
    public void TwoDifferentDatesForOneWorkRefuseRatherThanPickingOne()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            ExpressionRows(WorkOne, ExprFrench, French),
            [
                PBoundRow(WorkOne, WorkDateIri, new PValue("2016-05-04", IsIri: false, Datatype: XsdDate)),
                PBoundRow(WorkOne, WorkDateIri, new PValue("2016-05-05", IsIri: false, Datatype: XsdDate)),
            ],
            out var refusal,
            out var offending);

        Assert.IsNull(decoded);
        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.ConflictingWorkDate, refusal);
        Assert.AreEqual(WorkOne, offending);
    }

    [TestMethod]
    public void OneDateRepeatedIdenticallyIsNotAConflict()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            ExpressionRows(WorkOne, ExprFrench, French),
            [
                PBoundRow(WorkOne, WorkDateIri, new PValue("2016-05-04", IsIri: false, Datatype: XsdDate)),
                PBoundRow(WorkOne, WorkDateIri, new PValue("2016-05-04", IsIri: false, Datatype: XsdDate)),
            ],
            out var refusal,
            out _);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
    }

    [TestMethod]
    public void ADateBoundToAnIriRefusesRatherThanBeingCoerced()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            ExpressionRows(WorkOne, ExprFrench, French),
            [PBoundRow(WorkOne, WorkDateIri, new PValue(WorkTwo))],
            out var refusal,
            out var offending);

        Assert.IsNull(decoded);
        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.WorkDateRowTermKindMismatch, refusal);
        Assert.AreEqual(WorkOne, offending);
    }

    /// <summary>
    /// Family P legitimately describes objects this delivery has no Expression of. Refusing those
    /// would refuse correct publisher data, so they are out of scope rather than dropped.
    /// </summary>
    [TestMethod]
    public void ObjectFactRowsAboutOtherSubjectsAndOtherPredicatesAreNotRefused()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            ExpressionRows(WorkOne, ExprFrench, French),
            [
                PBoundRow(WorkTwo, WorkDateIri, new PValue("2018-11-21", IsIri: false, Datatype: XsdDate)),
                PBoundRow(WorkOne, CelexIri, new PValue("32016R0679", IsIri: false, Datatype: XsdString)),
            ],
            out var refusal,
            out _);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.IsNull(decoded[0].PublisherCorrigendumDate, "the other Work's date is not this one's.");
    }

    // ---- Provenance, atomicity and replay. ----

    [TestMethod]
    public void EveryDecodedExpressionCarriesTheExactRetainedReceiptAndSourceObject()
    {
        var set = new LanguageScopedExpressionSet();
        var receipt = Receipt('a');
        var sourceObject = SourceObject();
        var decoded = EuLanguageScopedExpressionDecode.TryDecode(
            [.. ExpressionRows(WorkOne, ExprEnglish, English), .. ExpressionRows(WorkOne, ExprFrench, French)],
            ExpressionFactsProfile,
            [],
            ObjectFactsProfile,
            sourceObject,
            receipt,
            set,
            out _,
            out _);

        Assert.IsNotNull(decoded);
        foreach (var expression in decoded)
        {
            Assert.AreSame(receipt, expression.RetainedTransportBytes);
            Assert.AreSame(sourceObject, expression.SourceObject);
            Assert.AreEqual(receipt.Reference.ContentSha256, expression.CanonicalBytesSha256);
        }
    }

    /// <summary>A refused delivery must leave an append-only store exactly as it was found.</summary>
    [TestMethod]
    public void ARefusedDeliveryLeavesTheDestinationSetUntouched()
    {
        var set = new LanguageScopedExpressionSet();
        Decode(set, ExpressionRows(WorkOne, ExprEnglish, English), out _, out _);
        Assert.HasCount(1, set.Expressions);

        var decoded = Decode(
            set,
            [
                .. ExpressionRows(WorkOne, ExprFrench, French),
                XBoundRow(WorkOne, ExprNorwegian, UsesLanguageIri, new PValue(Norwegian)),
            ],
            out var refusal,
            out _);

        Assert.IsNull(decoded);
        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.ExpressionSubjectNotSelfClosed, refusal);
        Assert.HasCount(
            1, set.Expressions, "the admissible French expression must not have been appended.");
        Assert.AreEqual(ExprEnglish, set.Expressions[0].Identity.PublisherExpressionId);
    }

    /// <summary>
    /// A conflict with what the set already holds is found before the first append, not halfway
    /// through one.
    /// </summary>
    [TestMethod]
    public void AConflictWithAHeldExpressionRefusesWithoutAppendingTheAdmissibleOnes()
    {
        var set = new LanguageScopedExpressionSet();
        EuLanguageScopedExpressionDecode.TryDecode(
            ExpressionRows(WorkOne, ExprFrench, French),
            ExpressionFactsProfile,
            [],
            ObjectFactsProfile,
            SourceObject(),
            Receipt('a'),
            set,
            out _,
            out _);
        Assert.HasCount(1, set.Expressions);

        // The same identity, from a different retained transport: different bytes, same identity.
        var decoded = EuLanguageScopedExpressionDecode.TryDecode(
            [.. ExpressionRows(WorkOne, ExprEnglish, English), .. ExpressionRows(WorkOne, ExprFrench, French)],
            ExpressionFactsProfile,
            [],
            ObjectFactsProfile,
            SourceObject(),
            Receipt('b'),
            set,
            out var refusal,
            out var offending);

        Assert.IsNull(decoded);
        Assert.AreEqual(
            EuLanguageScopedExpressionDecodeRefusal.AppendConflictsWithHeldExpression, refusal);
        Assert.AreEqual(ExprFrench, offending);
        Assert.HasCount(
            1,
            set.Expressions,
            "the English expression preceded the conflicting French one and must not have landed.");
    }

    [TestMethod]
    public void ReplayingOneDeliveryConvergesRatherThanGrowingTheStore()
    {
        var set = new LanguageScopedExpressionSet();
        var rows = ExpressionRows(WorkOne, ExprFrench, French);

        Assert.IsNotNull(Decode(set, rows, out _, out _));
        Assert.IsNotNull(Decode(set, rows, out var refusal, out _));

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.HasCount(1, set.Expressions);
    }

    [TestMethod]
    public void ExpressionsAreAppendedInTheOrderThePublisherStatedThem()
    {
        var set = new LanguageScopedExpressionSet();
        Decode(
            set,
            [
                .. ExpressionRows(WorkOne, ExprFrench, French),
                .. ExpressionRows(WorkOne, ExprEnglish, English),
                .. ExpressionRows(WorkOne, ExprNorwegian, Norwegian),
            ],
            out _,
            out _);

        CollectionAssert.AreEqual(
            new[] { ExprFrench, ExprEnglish, ExprNorwegian },
            set.Expressions.Select(static expression => expression.Identity.PublisherExpressionId).ToArray());
    }

    /// <summary>
    /// A non-identity predicate does not create an expression of its own, but it does have to belong
    /// to one that exists.
    /// </summary>
    [TestMethod]
    public void ATitleRowOnAnAdmittedExpressionAddsNoSecondExpression()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            [
                .. ExpressionRows(WorkOne, ExprFrench, French),
                XBoundRow(
                    WorkOne,
                    ExprFrench,
                    TitleIri,
                    new PValue("Rectificatif", IsIri: false, Datatype: XsdString)),
            ],
            out var refusal,
            out _);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.HasCount(1, decoded);
    }

    // ---- Surface. ----

    /// <summary>
    /// There is no parameter by which a caller asserts that a decode is publisher-backed. The
    /// contract accepts no such claim, and neither does the door that feeds it.
    /// </summary>
    [TestMethod]
    public void TheDecodeSurfaceTakesNoProvenanceOrOverrideAssertion()
    {
        var methods = typeof(EuLanguageScopedExpressionDecode)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        Assert.HasCount(1, methods, "one door.");
        Assert.IsEmpty(
            methods[0].GetParameters().Where(static parameter =>
                parameter.ParameterType == typeof(bool) || parameter.HasDefaultValue),
            "a boolean or defaulted parameter here would be a caller's claim about evidence.");
    }

    [TestMethod]
    public void EveryNullArgumentIsACallerContractViolation()
    {
        var set = new LanguageScopedExpressionSet();
        Assert.ThrowsExactly<ArgumentNullException>(() => EuLanguageScopedExpressionDecode.TryDecode(
            null!, ExpressionFactsProfile, [], ObjectFactsProfile, SourceObject(), Receipt('a'), set,
            out _, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => EuLanguageScopedExpressionDecode.TryDecode(
            [], null!, [], ObjectFactsProfile, SourceObject(), Receipt('a'), set, out _, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => EuLanguageScopedExpressionDecode.TryDecode(
            [], ExpressionFactsProfile, null!, ObjectFactsProfile, SourceObject(), Receipt('a'), set,
            out _, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => EuLanguageScopedExpressionDecode.TryDecode(
            [], ExpressionFactsProfile, [], null!, SourceObject(), Receipt('a'), set, out _, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => EuLanguageScopedExpressionDecode.TryDecode(
            [], ExpressionFactsProfile, [], ObjectFactsProfile, null!, Receipt('a'), set, out _, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => EuLanguageScopedExpressionDecode.TryDecode(
            [], ExpressionFactsProfile, [], ObjectFactsProfile, SourceObject(), null!, set, out _, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => EuLanguageScopedExpressionDecode.TryDecode(
            [], ExpressionFactsProfile, [], ObjectFactsProfile, SourceObject(), Receipt('a'), null!,
            out _, out _));
    }

    [TestMethod]
    public void AnEmptyDeliveryAppendsNothingAndRefusesNothing()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(set, [], out var refusal, out var offending);

        Assert.IsNotNull(decoded);
        Assert.IsEmpty(decoded);
        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNull(offending);
    }

    /// <summary>
    /// Two language IRIs differing only in case are two different IRIs. Merging them would be the
    /// same silent fold this door exists to remove, in a subtler spelling.
    /// </summary>
    [TestMethod]
    public void TwoLanguageIrisDifferingOnlyByCaseAreNotOneLanguage()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            [
                .. ExpressionRows(WorkOne, ExprEnglish, English),
                XBoundRow(WorkOne, ExprEnglish, UsesLanguageIri, new PValue(LanguageBase + "eng")),
            ],
            out var refusal,
            out _);

        Assert.IsNull(decoded);
        Assert.AreEqual(
            EuLanguageScopedExpressionDecodeRefusal.ConflictingExpressionLanguage, refusal);
    }

    /// <summary>
    /// The datatype is the publisher's, not this build's guess.
    /// <see cref="ThePublisherWorkDateIsCarriedWithItsOwnLexicalFormAndDatatype"/> cannot see a
    /// substituted <c>xsd:date</c>, because it asserts that exact constant. This one uses a datatype
    /// nothing in this build would ever choose, so a substitution has nowhere to hide.
    /// </summary>
    [TestMethod]
    public void ThePublisherDateDatatypeIsCarriedRatherThanAssumed()
    {
        const string GYearMonth = "http://www.w3.org/2001/XMLSchema#gYearMonth";
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            ExpressionRows(WorkOne, ExprFrench, French),
            [PBoundRow(WorkOne, WorkDateIri, new PValue("2016-05", IsIri: false, Datatype: GYearMonth))],
            out var refusal,
            out _);

        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.None, refusal);
        Assert.IsNotNull(decoded);
        Assert.AreEqual("2016-05", decoded[0].PublisherCorrigendumDate!.RawLexical);
        Assert.AreEqual(
            GYearMonth,
            decoded[0].PublisherCorrigendumDate!.DatatypeIri,
            "an assumed datatype claims a precision the publisher never stated.");
    }

    /// <summary>
    /// A date literal the publisher gave no datatype refuses rather than being assigned one. Until
    /// this existed the only thing stopping that guard's removal was the compiler.
    /// </summary>
    [TestMethod]
    public void ADateLiteralWithNoDatatypeRefusesRatherThanBeingGivenOne()
    {
        var set = new LanguageScopedExpressionSet();
        var decoded = Decode(
            set,
            ExpressionRows(WorkOne, ExprFrench, French),
            [PBoundRow(WorkOne, WorkDateIri, new PValue("2016-05-04", IsIri: false))],
            out var refusal,
            out var offending);

        Assert.IsNull(decoded);
        Assert.AreEqual(EuLanguageScopedExpressionDecodeRefusal.WorkDateRowTermKindMismatch, refusal);
        Assert.AreEqual(WorkOne, offending);
    }

    /// <summary>
    /// Every refusal's wire token is exactly what a reader will see. The closed-vocabulary census
    /// pins member names rather than tokens, so without this a token rename is invisible.
    /// </summary>
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
            },
            Enum.GetValues<EuLanguageScopedExpressionDecodeRefusal>()
                .Select(member => ContractJson.Serialize(member))
                .ToArray());
    }

    // ---- Fixtures. ----

    private static IReadOnlyList<LanguageScopedExpression>? Decode(
        LanguageScopedExpressionSet into,
        IReadOnlyList<RepeatedEnumerationRow> expressionRows,
        out EuLanguageScopedExpressionDecodeRefusal refusal,
        out string? offendingIri) =>
        Decode(into, expressionRows, [], out refusal, out offendingIri);

    private static IReadOnlyList<LanguageScopedExpression>? Decode(
        LanguageScopedExpressionSet into,
        IReadOnlyList<RepeatedEnumerationRow> expressionRows,
        IReadOnlyList<RepeatedEnumerationRow> objectRows,
        out EuLanguageScopedExpressionDecodeRefusal refusal,
        out string? offendingIri) =>
        EuLanguageScopedExpressionDecode.TryDecode(
            expressionRows,
            ExpressionFactsProfile,
            objectRows,
            ObjectFactsProfile,
            SourceObject(),
            Receipt('a'),
            into,
            out refusal,
            out offendingIri);

    private sealed record PValue(string Value, bool IsIri = true, string? Datatype = null, string? Lang = null);

    /// <summary>One Expression's complete family-X rows: belongs-to-work plus a language.</summary>
    private static IReadOnlyList<RepeatedEnumerationRow> ExpressionRows(
        string parentIri, string expressionIri, string languageAuthorityIri) =>
        [
            XBoundRow(parentIri, expressionIri, BelongsToWorkIri, new PValue(parentIri)),
            XBoundRow(parentIri, expressionIri, UsesLanguageIri, new PValue(languageAuthorityIri)),
        ];

    private static RepeatedEnumerationRow XBoundRow(
        string parentIri, string expressionIri, string predicateIri, PValue value)
    {
        var kind = value.IsIri ? "iri" : "literal";
        var datatype = value.IsIri ? string.Empty : value.Datatype ?? string.Empty;
        var lang = value.IsIri ? string.Empty : value.Lang ?? string.Empty;
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(parentIri),
            RepeatedEnumerationRdfTerm.Iri(expressionIri),
            RepeatedEnumerationRdfTerm.Iri(predicateIri),
            value.IsIri
                ? RepeatedEnumerationRdfTerm.Iri(value.Value)
                : RepeatedEnumerationRdfTerm.Literal(value.Value, value.Datatype, value.Lang),
            RepeatedEnumerationRdfTerm.Literal(kind, null, null),
            RepeatedEnumerationRdfTerm.Literal(datatype, null, null),
            RepeatedEnumerationRdfTerm.Literal(lang, null, null),
            RepeatedEnumerationRdfTerm.Literal("1", XsdInteger, null),
            RepeatedEnumerationRdfTerm.Literal(expressionIri, null, null),
            RepeatedEnumerationRdfTerm.Literal(predicateIri, null, null),
            RepeatedEnumerationRdfTerm.Literal(kind, null, null),
            RepeatedEnumerationRdfTerm.Literal(value.Value, null, null),
            RepeatedEnumerationRdfTerm.Literal(datatype, null, null),
            RepeatedEnumerationRdfTerm.Literal(lang, null, null),
        };
        return new RepeatedEnumerationRow(
            Array.AsReadOnly(terms), Array.AsReadOnly(terms[0..4]), Array.AsReadOnly(terms[8..14]));
    }

    private static RepeatedEnumerationRow PBoundRow(string objectIri, string predicateIri, PValue value)
    {
        var kind = value.IsIri ? "iri" : "literal";
        var datatype = value.IsIri ? string.Empty : value.Datatype ?? string.Empty;
        var lang = value.IsIri ? string.Empty : value.Lang ?? string.Empty;
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(objectIri),
            RepeatedEnumerationRdfTerm.Iri(predicateIri),
            value.IsIri
                ? RepeatedEnumerationRdfTerm.Iri(value.Value)
                : RepeatedEnumerationRdfTerm.Literal(value.Value, value.Datatype, value.Lang),
            RepeatedEnumerationRdfTerm.Literal(kind, null, null),
            RepeatedEnumerationRdfTerm.Literal(datatype, null, null),
            RepeatedEnumerationRdfTerm.Literal(lang, null, null),
            RepeatedEnumerationRdfTerm.Literal("1", XsdInteger, null),
            RepeatedEnumerationRdfTerm.Literal(objectIri, null, null),
            RepeatedEnumerationRdfTerm.Literal(predicateIri, null, null),
            RepeatedEnumerationRdfTerm.Literal(kind, null, null),
            RepeatedEnumerationRdfTerm.Literal(value.Value, null, null),
            RepeatedEnumerationRdfTerm.Literal(datatype, null, null),
            RepeatedEnumerationRdfTerm.Literal(lang, null, null),
        };
        return new RepeatedEnumerationRow(
            Array.AsReadOnly(terms), Array.AsReadOnly(terms[0..3]), Array.AsReadOnly(terms[7..13]));
    }

    private static RepeatedEnumerationRow PUnboundRow(string objectIri, string predicateIri)
    {
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(objectIri),
            RepeatedEnumerationRdfTerm.Iri(predicateIri),
            RepeatedEnumerationRdfTerm.Unbound(),
            RepeatedEnumerationRdfTerm.Literal("unbound", null, null),
            RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
            RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
            RepeatedEnumerationRdfTerm.Literal("0", XsdInteger, null),
            RepeatedEnumerationRdfTerm.Literal(objectIri, null, null),
            RepeatedEnumerationRdfTerm.Literal(predicateIri, null, null),
            RepeatedEnumerationRdfTerm.Literal("unbound", null, null),
            RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
            RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
            RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
        };
        return new RepeatedEnumerationRow(
            Array.AsReadOnly(terms), Array.AsReadOnly(terms[0..3]), Array.AsReadOnly(terms[7..13]));
    }

    private static SourceObjectRef SourceObject() => new(
        SourceCoreSchemaIds.SourceObjectRef,
        SourceAuthority.Cellar,
        new SourceRegistryMemberRef(ArtifactRef('b', '2'), "eu-object-facts"),
        WorkOne,
        "cellar|work|32016R0679",
        Sha256("cellar|work|32016R0679"),
        ArtifactRef('a', '1'),
        parentKeyRef: null);

    private static DurableBlobWriteReceipt Receipt(char contentFill)
    {
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef,
            new string(contentFill, 64),
            4,
            CustodyClass.NightlyFloor90d);
        return new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            reference,
            new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.FileSystemUnenforced1,
                policyKey: null,
                CustodyProtection.NotEnforced,
                DateTimeOffset.Parse(ObservedAtText, System.Globalization.CultureInfo.InvariantCulture),
                protectedUntil: null));
    }

    private static SourceArtifactRef ArtifactRef(char resourceFill, char digestFill) => new(
        $"urn:uuid:{new string(resourceFill, 8)}-{new string(resourceFill, 4)}-4{new string(resourceFill, 3)}-8{new string(resourceFill, 3)}-{new string(resourceFill, 12)}",
        new string(digestFill, 64));

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
