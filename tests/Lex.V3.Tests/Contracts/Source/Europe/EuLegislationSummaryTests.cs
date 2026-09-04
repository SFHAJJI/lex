using System.Linq;
using System.Reflection;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// Stage 2 item E7: the EU Summary of Legislation record, built on the already-merged Facts layer
/// exactly as <see cref="EuCaseLawLinkBinding"/> (E6) and <see cref="EuDateAxiomBinding"/> (E1)
/// already are. See the remarks on <see cref="EuLegislationSummary"/> for the full reasoning,
/// including the correction of fact that a LegisSum record is a plain-language summary of a legal
/// act (CDM class <c>summary_legislation_eu</c>), never explanatory text minted alongside a
/// case-law judgment.
/// </summary>
/// <remarks>
/// Fixtures are hand built directly from review/23-research-temporal.md section 7, line 88's own
/// worked instance: <c>legissum:310401_2</c>, version <c>2.0.0</c>, obsolete <c>0</c>,
/// validated_by_institution <c>JUST</c>, drafted_in_language <c>ENG</c> are all real, proven values.
/// Pairing that exact record with the GDPR specifically is this file's own scaffolding, disclosed
/// where it is used: review/23 gives the worked instance's field values without naming which
/// specific act <c>legissum:310401_2</c> summarizes. Contract only: no live SPARQL call, no
/// adapter, no summary text.
/// </remarks>
[TestClass]
public sealed class EuLegislationSummaryTests
{
    // --- Fixtures ---------------------------------------------------------------------------

    /// <summary>GDPR, real CELEX (also E1's and E6's own fixture identity: 32016R0679).</summary>
    private static OfficialIdentitySet Gdpr() =>
        new(PublisherId.EuEurLex, [new OfficialIdentifier(FactsIdentifierFamily.Celex, "32016R0679")]);

    private static OfficialIdentitySet LuxembourgIdentity() =>
        new(PublisherId.LuLegilux, [new OfficialIdentifier(FactsIdentifierFamily.Memorial, "A123")]);

    /// <summary>
    /// review/23 section 7, line 88's own worked instance, every field value real and proven.
    /// Pairing it with the GDPR specifically is this file's own scaffolding (see the type remarks).
    /// </summary>
    private static EuLegislationSummary GdprSummary() => EuLegislationSummary.Create(
        workIdDocument: "legissum:310401_2",
        summarizedAct: Gdpr(),
        predicateUri: EuLegislationSummaryPredicateVocabulary.SummarizesResourceLegalPredicateUri,
        summarizedActBodyScope: TargetBodyScope.BodyInScopeHeld,
        draftedInLanguage: "ENG",
        version: "2.0.0",
        obsolete: false,
        validatedByInstitution: "JUST",
        sourceObservationId: "obs:legissum-310401-2-summarizes-gdpr");

    // --- The proven fields round-trip -----------------------------------------------------------

    [TestMethod]
    public void TheProvenFieldsFromReviewTwentyThreeSectionSevenLineEightyEightRoundTrip()
    {
        var summary = GdprSummary();

        Assert.AreEqual("legissum:310401_2", summary.WorkIdDocument);
        Assert.IsTrue(summary.SummarizedAct.SameIdentity(Gdpr()));
        Assert.AreEqual(
            EuLegislationSummaryPredicateVocabulary.SummarizesResourceLegalPredicateUri,
            summary.PredicateUri);
        Assert.AreEqual(TargetBodyScope.BodyInScopeHeld, summary.SummarizedActBodyScope);
        Assert.AreEqual("ENG", summary.DraftedInLanguage);
        Assert.AreEqual("2.0.0", summary.Version);
        Assert.IsFalse(summary.Obsolete);
        Assert.AreEqual("JUST", summary.ValidatedByInstitution);
        Assert.AreEqual("obs:legissum-310401-2-summarizes-gdpr", summary.SourceObservationId);
    }

    [TestMethod]
    public void AnObsoleteFlagOfTrueIsCarriedThrough()
    {
        // Structural coverage only: review/23 line 88 proves the "0" (not obsolete) value; no
        // worked instance of an obsolete legissum record exists there. "1" would map to true the
        // same way, disclosed here as scaffolding rather than a second proven value.
        var summary = EuLegislationSummary.Create(
            "legissum:310401_2", Gdpr(),
            EuLegislationSummaryPredicateVocabulary.SummarizesResourceLegalPredicateUri,
            TargetBodyScope.BodyInScopeNotHeld, "ENG", "2.0.0", true, "JUST",
            "obs:legissum-310401-2-obsolete");
        Assert.IsTrue(summary.Obsolete);
    }

    // --- The licence is reused from the already-reviewed EU rights matrix, never a parameter -----

    [TestMethod]
    public void TheLicenceIsAlwaysCcBy40ReadFromTheAlreadyReviewedRightsMatrix()
    {
        var summary = GdprSummary();

        Assert.AreEqual(EuReuseBasis.CcBy40, summary.Licence);
        // Not an independent claim: it is exactly what the already-reviewed matrix says for the
        // Summary content class (EuManifestationScope.cs), reused rather than restated.
        Assert.AreEqual(EuRightsDisposition.BasisFor(EuContentClass.Summary), summary.Licence);
    }

    [TestMethod]
    public void CreateHasNoParameterThroughWhichALicenceCouldBeSupplied()
    {
        // The direct enforcement of "computed, never a parameter", mirroring
        // EuCaseLawLinkTests.CreateHasNoParameterThroughWhichAnEcliStateOrACaseSideCouldBeSupplied
        // and EuDateAxiomBinding's own "single role home" precision.
        var parameters = typeof(EuLegislationSummary)
            .GetMethod(nameof(EuLegislationSummary.Create))!
            .GetParameters();

        Assert.IsFalse(parameters.Any(p => p.ParameterType == typeof(EuReuseBasis)));
        CollectionAssert.AreEqual(
            new[]
            {
                "workIdDocument", "summarizedAct", "predicateUri", "summarizedActBodyScope",
                "draftedInLanguage", "version", "obsolete", "validatedByInstitution",
                "sourceObservationId",
            },
            parameters.Select(p => p.Name).ToArray());
    }

    // --- The work id document: validated, never an OfficialIdentitySet (see the type remarks) ----

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("310401_2")]
    [DataRow("legissum:")]
    [DataRow(" legissum:310401_2")]
    [DataRow("legissum:310401_2 ")]
    public void AMalformedWorkIdDocumentIsRefused(string? workIdDocument)
    {
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => EuLegislationSummary.Create(
            workIdDocument!, Gdpr(),
            EuLegislationSummaryPredicateVocabulary.SummarizesResourceLegalPredicateUri,
            TargetBodyScope.BodyInScopeHeld, "ENG", "2.0.0", false, "JUST",
            "obs:malformed-work-id"));
        StringAssert.Contains(thrown.Message, "work id");
    }

    // --- The summarized act must be an EU identity: summary_legislation_eu is EU-only ------------

    [TestMethod]
    public void ASummarizedActThatIsALuxembourgIdentityIsRefused()
    {
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => EuLegislationSummary.Create(
            "legissum:310401_2", LuxembourgIdentity(),
            EuLegislationSummaryPredicateVocabulary.SummarizesResourceLegalPredicateUri,
            TargetBodyScope.BodyInScopeHeld, "ENG", "2.0.0", false, "JUST",
            "obs:lu-summarized-act"));
        StringAssert.Contains(thrown.Message, "EU-only");
    }

    [TestMethod]
    public void ANullSummarizedActIsRefused()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EuLegislationSummary.Create(
            "legissum:310401_2", null!,
            EuLegislationSummaryPredicateVocabulary.SummarizesResourceLegalPredicateUri,
            TargetBodyScope.BodyInScopeHeld, "ENG", "2.0.0", false, "JUST",
            "obs:null-summarized-act"));
    }

    // --- The predicate vocabulary is closed at the one review/23-evidenced predicate --------------

    [TestMethod]
    public void ThePinnedPredicateVocabularyIsExactlyTheOneReviewEvidencedPredicate()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                "http://publications.europa.eu/ontology/cdm#summary_legislation_eu_summarizes_resource_legal",
            },
            EuLegislationSummaryPredicateVocabulary.Pinned.ToArray());
    }

    [TestMethod]
    public void AnUnpinnedPredicateIsRefusedEvenWhenItIsARealNamedCdmPredicate()
    {
        // work_cites_work is a real, generic CDM predicate (review/23 section 3, line 54) that E6
        // pins for its own edge; it is deliberately not pinned here, because review/23 gives no
        // worked instance of it against a summary_legislation_eu work.
        Assert.ThrowsExactly<ArgumentException>(() => EuLegislationSummary.Create(
            "legissum:310401_2", Gdpr(), "http://publications.europa.eu/ontology/cdm#work_cites_work",
            TargetBodyScope.BodyInScopeHeld, "ENG", "2.0.0", false, "JUST",
            "obs:unpinned-predicate"));
    }

    [TestMethod]
    public void ANullPredicateIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => EuLegislationSummary.Create(
            "legissum:310401_2", Gdpr(), null!,
            TargetBodyScope.BodyInScopeHeld, "ENG", "2.0.0", false, "JUST",
            "obs:null-predicate"));
    }

    // --- The summarized act's own body scope is reused unrestricted (see the type remarks) --------

    [TestMethod]
    public void EveryTargetBodyScopeValueStaysLegitimateForTheSummarizedAct()
    {
        // Unlike E6, there is no "explanatory side sits at the target" asymmetry to guard against:
        // the summarized act is always the ordinary, non-explanatory side of this edge.
        foreach (var scope in Enum.GetValues<TargetBodyScope>())
        {
            var summary = EuLegislationSummary.Create(
                "legissum:310401_2", Gdpr(),
                EuLegislationSummaryPredicateVocabulary.SummarizesResourceLegalPredicateUri,
                scope, "ENG", "2.0.0", false, "JUST", "obs:body-scope-" + scope);
            Assert.AreEqual(scope, summary.SummarizedActBodyScope);
        }
    }

    // --- The drafted-in language and version grammars ----------------------------------------------

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("EN")]
    [DataRow("ENGL")]
    [DataRow("eng")]
    [DataRow("EN1")]
    public void AMalformedDraftedInLanguageIsRefused(string? draftedInLanguage)
    {
        Assert.ThrowsExactly<ArgumentException>(() => EuLegislationSummary.Create(
            "legissum:310401_2", Gdpr(),
            EuLegislationSummaryPredicateVocabulary.SummarizesResourceLegalPredicateUri,
            TargetBodyScope.BodyInScopeHeld, draftedInLanguage!, "2.0.0", false, "JUST",
            "obs:malformed-language"));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("2.0")]
    [DataRow("2.0.0.0")]
    [DataRow("a.b.c")]
    [DataRow("2..0")]
    [DataRow("2.0.")]
    public void AMalformedVersionIsRefused(string? version)
    {
        Assert.ThrowsExactly<ArgumentException>(() => EuLegislationSummary.Create(
            "legissum:310401_2", Gdpr(),
            EuLegislationSummaryPredicateVocabulary.SummarizesResourceLegalPredicateUri,
            TargetBodyScope.BodyInScopeHeld, "ENG", version!, false, "JUST",
            "obs:malformed-version"));
    }

    // --- The validating institution and the observation id ------------------------------------------

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow(" JUST")]
    public void AMalformedValidatingInstitutionIsRefused(string? validatedByInstitution)
    {
        Assert.ThrowsExactly<ArgumentException>(() => EuLegislationSummary.Create(
            "legissum:310401_2", Gdpr(),
            EuLegislationSummaryPredicateVocabulary.SummarizesResourceLegalPredicateUri,
            TargetBodyScope.BodyInScopeHeld, "ENG", "2.0.0", false, validatedByInstitution!,
            "obs:malformed-institution"));
    }

    [TestMethod]
    public void ANullSourceObservationIdIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => EuLegislationSummary.Create(
            "legissum:310401_2", Gdpr(),
            EuLegislationSummaryPredicateVocabulary.SummarizesResourceLegalPredicateUri,
            TargetBodyScope.BodyInScopeHeld, "ENG", "2.0.0", false, "JUST", null!));
    }

    // --- Construction surface ------------------------------------------------------------------------

    [TestMethod]
    public void TheRecordHasExactlyOneConstructionPath()
    {
        const string N = "Lex.V3.Contracts.Source.Europe.";
        const string Facts = "Lex.V3.Contracts.Facts.";
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "EuLegislationSummary::.ctor(System.String, "
                + Facts + "OfficialIdentitySet, System.String, " + Facts
                + "TargetBodyScope, System.String, System.String, System.Boolean, System.String, "
                + N + "EuReuseBasis, System.String) -> " + N + "EuLegislationSummary",
                "method public static " + N + "EuLegislationSummary::Create(System.String, "
                + Facts + "OfficialIdentitySet, System.String, " + Facts
                + "TargetBodyScope, System.String, System.String, System.Boolean, System.String, "
                + "System.String) -> " + N + "EuLegislationSummary",
            },
            ConstructionSurface.Of(typeof(EuLegislationSummary)).ToArray());
    }

    [TestMethod]
    public void NoOtherTypeInTheAssemblyHoldsOrProducesARecord()
    {
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(EuLegislationSummary).Assembly, typeof(EuLegislationSummary), true).ToArray());
    }

    [TestMethod]
    public void ThePublicPropertySurfaceIsExactlyTheseNine()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "DraftedInLanguage", "Licence", "Obsolete", "PredicateUri", "SourceObservationId",
                "SummarizedAct", "SummarizedActBodyScope", "ValidatedByInstitution", "Version",
                "WorkIdDocument",
            },
            typeof(EuLegislationSummary).GetProperties()
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray());
    }

    // --- The exclusion proof: SCOPE_RULING precision three -------------------------------------------

    /// <summary>
    /// The positive half of the proof: both of the bundle's admitted variants, from E1's and E6's
    /// own real binding types, actually construct and actually enter the bundle.
    /// </summary>
    [TestMethod]
    public void TheBundleAdmitsBothE1sDateAxiomEvidenceAndE6sCaseLawEvidence()
    {
        var dateAxiom = EuDateAxiomBinding.Create(
            work: Gdpr(),
            rawLexicalValue: "2016-05-24",
            datatypeUri: "http://www.w3.org/2001/XMLSchema#date",
            precision: DatePrecision.YearMonthDay,
            sourcePredicateUri: "http://publications.europa.eu/ontology/cdm#resource_legal_date_entry-into-force",
            axiom: new QualifiedAxiom(
                "axiom:32016r0679-entry-into-force-2016-05-24",
                [new AxiomQualifier(
                    "http://publications.europa.eu/ontology/annotation#type_of_date", "EV")]),
            rawQualifierCode: "EV",
            qualifierLabel: "Entry into force",
            publisherComment: null,
            parsedByAuthority: "https://lex.internal.example/authority/eu-legislation-summary-bundle-test/v1",
            sourceObservationId: "obs:bundle-date-axiom");

        var caseLawLink = EuCaseLawLinkBinding.Create(
            source: new OfficialIdentitySet(
                PublisherId.EuEurLex,
                [
                    new OfficialIdentifier(FactsIdentifierFamily.Celex, "62018CJ0311"),
                    new OfficialIdentifier(FactsIdentifierFamily.Ecli, "ECLI:EU:C:2020:559"),
                ]),
            target: Gdpr(),
            predicateUri: EuCaseLawPredicateVocabulary.CaseLawInterpretesResourceLegalPredicateUri,
            targetBodyScope: TargetBodyScope.BodyInScopeHeld,
            qualifiedAxioms: [],
            sourceObservationId: "obs:bundle-case-law");

        var bundle = EuFactsEvidenceBundle.Create(
        [
            EuFactsEvidenceBundleItem.OfDateAxiom(dateAxiom),
            EuFactsEvidenceBundleItem.OfCaseLawLink(caseLawLink),
        ]);

        Assert.AreEqual(2, bundle.Items.Count);
    }

    [TestMethod]
    public void ANullItemInTheBundleIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            EuFactsEvidenceBundle.Create([null!]));
    }

    [TestMethod]
    public void ANullItemsListIsRefused()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EuFactsEvidenceBundle.Create(null!));
    }

    /// <summary>
    /// The negative half of the proof, and the literal attempt SCOPE_RULING precision three asks
    /// for. <c>EuFactsEvidenceBundleItem.OfLegislationSummary(GdprSummary())</c> does not exist, so
    /// that call does not compile if written. Proven here instead by walking every constructor and
    /// every static method <see cref="EuFactsEvidenceBundleItem"/> and its two nested variants
    /// declare: none accepts a <see cref="EuLegislationSummary"/> parameter, so no call through this
    /// type's own surface could ever produce an item wrapping one.
    /// </summary>
    [TestMethod]
    public void NoMemberOfEuFactsEvidenceBundleItemCanProduceOneFromALegislationSummary()
    {
        var members = typeof(EuFactsEvidenceBundleItem)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Append(typeof(EuFactsEvidenceBundleItem))
            .SelectMany(type => type
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Cast<MethodBase>()
                .Concat(type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                    | BindingFlags.DeclaredOnly)))
            .ToArray();

        // Sanity: the walk actually found the two known factories and the two known nested
        // constructors, so an empty or broken walk cannot pass this test by accident.
        Assert.IsTrue(members.Length >= 4);
        Assert.IsTrue(members.Any(m => m.Name == nameof(EuFactsEvidenceBundleItem.OfDateAxiom)));
        Assert.IsTrue(members.Any(m => m.Name == nameof(EuFactsEvidenceBundleItem.OfCaseLawLink)));

        Assert.IsFalse(members.Any(member =>
            member.GetParameters().Any(p => p.ParameterType == typeof(EuLegislationSummary))));
    }

    [TestMethod]
    public void EuFactsEvidenceBundleItemHasExactlyTheTwoNamedFactoriesAndNoOtherProducerInTheAssembly()
    {
        // Transcribed from ConstructionSurface.Of's actual output, per this project's
        // print-then-transcribe technique. The bundle types live in this test assembly (see the
        // remarks on EuFactsEvidenceBundleItem below for why), so their own reflected names carry
        // the test namespace while the binding parameter types they wrap keep the production one.
        const string N = "Lex.V3.Tests.Contracts.Source.Europe.";
        const string ContractsN = "Lex.V3.Contracts.Source.Europe.";
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor internal instance " + N
                + "EuFactsEvidenceBundleItem+CaseLawItem::.ctor(" + ContractsN
                + "EuCaseLawLinkBinding) -> " + N + "EuFactsEvidenceBundleItem+CaseLawItem",
                "constructor internal instance " + N
                + "EuFactsEvidenceBundleItem+DateAxiomItem::.ctor(" + ContractsN
                + "EuDateAxiomBinding) -> " + N + "EuFactsEvidenceBundleItem+DateAxiomItem",
                "constructor private instance " + N + "EuFactsEvidenceBundleItem::.ctor() -> "
                + N + "EuFactsEvidenceBundleItem",
                "method public static " + N + "EuFactsEvidenceBundleItem::OfCaseLawLink(" + ContractsN
                + "EuCaseLawLinkBinding) -> " + N + "EuFactsEvidenceBundleItem",
                "method public static " + N + "EuFactsEvidenceBundleItem::OfDateAxiom(" + ContractsN
                + "EuDateAxiomBinding) -> " + N + "EuFactsEvidenceBundleItem",
            },
            ConstructionSurface.Of(typeof(EuFactsEvidenceBundleItem)).ToArray());

        // The only other place in this assembly that can hand back an already-built item is
        // EuFactsEvidenceBundle's own Items property (and its compiler-generated backing field):
        // reading a list back out, never manufacturing a new item from anything, let alone from a
        // EuLegislationSummary. Transcribed from ConstructionSurface.ProducersIn's actual output.
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N
                + "EuFactsEvidenceBundle::<Items>k__BackingField -> "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuFactsEvidenceBundleItem>",
                "property public instance " + N + "EuFactsEvidenceBundle::Items() -> "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuFactsEvidenceBundleItem>",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuFactsEvidenceBundleItem).Assembly, typeof(EuFactsEvidenceBundleItem), true)
                .ToArray());
    }

    /// <summary>
    /// This test also proves the bundle types cannot be seen from E1's or E6's own production-assembly
    /// construction-surface guards: their scans are assembly-scoped, and the bundle lives in this
    /// test assembly, never in <c>Lex.V3.Contracts.dll</c>.
    /// </summary>
    [TestMethod]
    public void TheBundleTypesAreInvisibleToTheProductionAssemblysOwnBindingGuards()
    {
        Assert.AreNotEqual(
            typeof(EuCaseLawLinkBinding).Assembly, typeof(EuFactsEvidenceBundleItem).Assembly);

        Assert.IsFalse(ConstructionSurface
            .ProducersIn(typeof(EuCaseLawLinkBinding).Assembly, typeof(EuCaseLawLinkBinding), true)
            .Any(entry => entry.Contains("EuFactsEvidenceBundle", StringComparison.Ordinal)));
        Assert.IsFalse(ConstructionSurface
            .ProducersIn(typeof(EuDateAxiomBinding).Assembly, typeof(EuDateAxiomBinding), true)
            .Any(entry => entry.Contains("EuFactsEvidenceBundle", StringComparison.Ordinal)));
    }
}

/// <summary>
/// The closed set of EU Facts-layer bindings admissible into one bundle: E1's own date-axiom
/// evidence and E6's own case-law evidence, and nothing else. Minted for one purpose -- SCOPE_RULING
/// precision three, "the explanatory, not law type is proven by a bundle construction that cannot
/// carry a summary record" -- rather than found already built: see the remarks on
/// <see cref="EuLegislationSummary"/> for why no pre-existing bundle already spans both.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OfDateAxiom"/> and <see cref="OfCaseLawLink"/> are the only two ways to produce an
/// instance. The constructor is <c>private</c>, not <c>private protected</c>: it is reachable only
/// from the two nested variant classes declared inside this type, never from any other type in this
/// assembly, so no third variant -- wrapping <see cref="EuLegislationSummary"/> or anything else --
/// can ever be added without editing this file.
/// <see cref="EuLegislationSummaryTests.EuFactsEvidenceBundleItemHasExactlyTheTwoNamedFactoriesAndNoOtherProducerInTheAssembly"/>
/// pins this reflectively: the only public static methods this type declares are the two named
/// above, and neither accepts a <see cref="EuLegislationSummary"/>.
/// </para>
/// <para>
/// <b>Why this lives in the test project, not in <c>Lex.V3.Contracts</c>.</b> See the remarks on
/// <see cref="EuLegislationSummary"/> itself: a production-assembly version of this type held
/// <see cref="EuDateAxiomBinding"/>/<see cref="EuCaseLawLinkBinding"/> directly and became a new
/// producer of both under <c>Lex.V3.Contracts.dll</c>'s own reflection scope, breaking E1's and E6's
/// own already-merged "no other type in the assembly holds or produces a binding" tests. Declaring
/// it here instead proves the identical exclusion -- E7's own record still cannot enter -- without
/// that collateral touch, because <see cref="Lex.V3.TestSupport.ConstructionSurface.ProducersIn"/>
/// scans one assembly at a time and this type's own assembly is <c>Lex.V3.Tests.dll</c>, never
/// <c>Lex.V3.Contracts.dll</c>.
/// </para>
/// </remarks>
public abstract class EuFactsEvidenceBundleItem
{
    private EuFactsEvidenceBundleItem()
    {
    }

    /// <summary>Admits one of E1's own date-axiom bindings.</summary>
    public static EuFactsEvidenceBundleItem OfDateAxiom(EuDateAxiomBinding binding) =>
        new DateAxiomItem(binding);

    /// <summary>Admits one of E6's own case-law-link bindings.</summary>
    public static EuFactsEvidenceBundleItem OfCaseLawLink(EuCaseLawLinkBinding binding) =>
        new CaseLawItem(binding);

    private sealed class DateAxiomItem : EuFactsEvidenceBundleItem
    {
        internal DateAxiomItem(EuDateAxiomBinding binding)
        {
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        }

        internal EuDateAxiomBinding Binding { get; }
    }

    private sealed class CaseLawItem : EuFactsEvidenceBundleItem
    {
        internal CaseLawItem(EuCaseLawLinkBinding binding)
        {
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        }

        internal EuCaseLawLinkBinding Binding { get; }
    }
}

/// <summary>
/// A small, closed bundle of <see cref="EuFactsEvidenceBundleItem"/> members. See the remarks on
/// <see cref="EuFactsEvidenceBundleItem"/> and on <see cref="EuLegislationSummary"/> for why this
/// exists, what it proves, and why it lives here rather than in <c>Lex.V3.Contracts</c>.
/// </summary>
public sealed class EuFactsEvidenceBundle
{
    private EuFactsEvidenceBundle(IReadOnlyList<EuFactsEvidenceBundleItem> items)
    {
        Items = items;
    }

    public IReadOnlyList<EuFactsEvidenceBundleItem> Items { get; }

    /// <summary>
    /// The only path that mints a bundle. <paramref name="items"/>'s own compile-time type already
    /// closes the admitted set to <see cref="EuFactsEvidenceBundleItem"/>'s two named variants; a
    /// null-element check is the only thing left for this door to do.
    /// </summary>
    public static EuFactsEvidenceBundle Create(IReadOnlyList<EuFactsEvidenceBundleItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var copy = items.ToArray();
        if (Array.IndexOf(copy, null) >= 0)
        {
            throw new ArgumentException(
                "An EU facts evidence bundle item cannot be null.", nameof(items));
        }

        return new EuFactsEvidenceBundle(Array.AsReadOnly(copy));
    }
}
