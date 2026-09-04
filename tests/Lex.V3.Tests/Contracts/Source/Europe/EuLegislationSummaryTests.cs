using System.Linq;
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
    public void ThePublicPropertySurfaceIsExactlyTheseTen()
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

    // --- The exclusion proof: SCOPE_RULING precision three, structurally enforced ------------------

    /// <summary>
    /// SCOPE_RULING precision three's real exclusion, proven by reflection over the whole
    /// <c>Lex.V3.Contracts</c> assembly rather than by a hand-built example bundle:
    /// <see cref="IEuFactsEvidenceCarrier"/>'s implementers are exactly E1's own
    /// <see cref="EuDateAxiomBinding"/>, E6's own <see cref="EuCaseLawLinkBinding"/> and E4's own
    /// <see cref="EuPublisherRelationEdge"/>, and this record is not assignable to the marker. A
    /// further implementer added later would change the expected array here too, so the closed set
    /// cannot silently widen; see the type remarks on
    /// <see cref="EuLegislationSummary"/> and on <see cref="IEuFactsEvidenceCarrier"/> for why a
    /// marker-typed bundle member trips neither E1's nor E6's own construction-surface guard.
    /// </summary>
    /// <remarks>
    /// E4 widened this pin from two implementers to three, ruled at
    /// <c>lex-event-20260904T190136614Z-26f124d9e6d246348b54b6719e22a63a</c>: a publisher-asserted
    /// EU relation edge is evidence. E4's derived inverse
    /// (<c>EuDerivedInverseRelationEdge</c>) is deliberately absent from this list and must stay
    /// absent, because REL-002 excludes derived edges from evidence bundles.
    /// </remarks>
    [TestMethod]
    public void TheEvidenceCarrierMarkerIsImplementedByExactlyE1sE6sAndE4sBindingsAndNotByThisRecord()
    {
        var implementers = typeof(EuLegislationSummary).Assembly.GetTypes()
            .Where(type => type != typeof(IEuFactsEvidenceCarrier) &&
                           typeof(IEuFactsEvidenceCarrier).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                typeof(EuCaseLawLinkBinding),
                typeof(EuDateAxiomBinding),
                typeof(EuPublisherRelationEdge),
            },
            implementers);

        Assert.IsFalse(typeof(IEuFactsEvidenceCarrier).IsAssignableFrom(typeof(EuLegislationSummary)));
    }
}
