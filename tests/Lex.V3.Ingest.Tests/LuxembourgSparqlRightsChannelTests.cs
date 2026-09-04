using System.Security.Cryptography;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// D1-06c-LU-2 repair, the rights blocker. RULING
/// lex-event-20260904T201756388Z-897fb21258b14e088f0495121479c9f4: <c>jolux:license</c> is an
/// admitted assertion predicate of the same family this run already proves and re-verifies, so
/// channel one is held evidence rather than a new query, and Decision 21's second channel, which
/// reads the licence out of the document, becomes a typed pending state rather than a refusal.
/// </summary>
/// <remarks>
/// The adapter used to pass an empty list for BOTH channels, so every body candidate failed the
/// rights blocker for want of a channel nobody had asked for. That is the defect these tests pin.
/// </remarks>
[TestClass]
public sealed class LuxembourgSparqlRightsChannelTests
{
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";
    private const string Manifestation =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2021/09/09/a676/jo/fr/xml";
    private const string OtherManifestation =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2021/09/09/a676/jo/fr/pdfa";
    private const string CcBy = "http://creativecommons.org/licenses/by/4.0/";
    private const string Scl = "http://data.legilux.public.lu/resource/authority/license/licenceSCL";

    private static readonly SourceArtifactRef ObservationRef = new(
        "urn:uuid:0f3b9c41-7d52-4a86-9e0b-2c5f81a4d637",
        Convert.ToHexStringLower(SHA256.HashData("lu-rights-observation"u8.ToArray())));

    /// <summary>
    /// The constants the adapter reads jolux:license and its two ruled licences by are the
    /// profile's own, not a second transcription beside them. This is the cross-check rule this
    /// lane adopted after a hand-written token list hid <c>doc</c>: a vocabulary is checked against
    /// every other list of the same thing in the tree before it is called closed.
    /// </summary>
    [TestMethod]
    public void TheRightsChannelPredicateAndLicencesMatchTheProfilesOwnVocabulary()
    {
        // Compared as arrays rather than as constants: a constant-to-constant Assert.AreEqual is
        // folded by the compiler and the analyzer refuses it as always true, which would make this
        // pin look like a check while proving nothing.
        CollectionAssert.AreEqual(
            new[]
            {
                VerifiedLuxembourgSourceProfile.JoluxPrefix + "license",
                VerifiedLuxembourgSourceProfile.AdmittingLicence,
                VerifiedLuxembourgSourceProfile.NonAdmittingLicenceScl,
            },
            new[]
            {
                LuxembourgQueryExecutionAdapter.JoluxLicense,
                LuxembourgQueryExecutionAdapter.RuledAdmittingLicence,
                LuxembourgQueryExecutionAdapter.RuledNonAdmittingLicenceScl,
            },
            "the adapter reads the profile's own vocabulary, never a second transcription of it.");
    }

    /// <summary>
    /// One row per manifestation that declares a licence, keyed by that manifestation, built from
    /// the assertions the proven family delivered and nothing else.
    /// </summary>
    [TestMethod]
    public void TheChannelIsPopulatedFromTheProvenFamilysOwnLicenceAssertions()
    {
        var rows = LuxembourgQueryExecutionAdapter.BuildSparqlRightsRows(
            [
                Iri(Manifestation, Jolux + "userFormat", "http://data.legilux.public.lu/resource/authority/user-format/xml-akomantoso"),
                Iri(Manifestation, LuxembourgQueryExecutionAdapter.JoluxLicense, CcBy),
                Iri(OtherManifestation, LuxembourgQueryExecutionAdapter.JoluxLicense, Scl),
            ],
            ObservationRef);

        Assert.HasCount(2, rows);
        Assert.AreEqual(OtherManifestation, rows[0].ManifestationIri, "rows are canonically ordered.");
        Assert.AreEqual(Manifestation, rows[1].ManifestationIri);
        CollectionAssert.AreEqual(new[] { CcBy }, rows[1].LicenceIris.ToArray());
        CollectionAssert.AreEqual(new[] { Scl }, rows[0].LicenceIris.ToArray());
    }

    /// <summary>
    /// A manifestation whose licence IRI the profile does not rule gets NO row, and the reason is a
    /// real hazard rather than tidiness: <c>LuxembourgScopeResolver.ValidateObservation</c> refuses
    /// the WHOLE RUN with UnknownVocabularyDrift for an unruled licence on a rights channel, so
    /// carrying one would let a single odd licence anywhere in the store kill every run. No row
    /// means rights stay unproven and the body stays unselected, which is the conservative answer.
    /// Giving an unruled licence its own typed quarantine is named residue (D1-04f), not a silent
    /// drop.
    /// </summary>
    [TestMethod]
    public void AnUnruledLicenceGetsNoChannelRowRatherThanRefusingTheWholeRun()
    {
        var rows = LuxembourgQueryExecutionAdapter.BuildSparqlRightsRows(
            [Iri(Manifestation, LuxembourgQueryExecutionAdapter.JoluxLicense, "http://example.invalid/licence")],
            ObservationRef);

        Assert.IsEmpty(rows);
    }

    /// <summary>
    /// THE WIRING, not the builder. An observation really carries the channel it built, and the
    /// second channel really is empty. Driving BuildSparqlRightsRows alone proved the builder and
    /// said nothing about the call site, and an unfiltered mutation putting the empty list back at
    /// that call site passed 285 of 285 and 2108 of 2108. This is the assertion that kills it.
    /// </summary>
    [TestMethod]
    public void TheObservationCarriesTheChannelItBuiltAndAnEmptySecondChannel()
    {
        var observation = LuxembourgQueryExecutionAdapter.BuildResourceObservation(
            "http://data.legilux.public.lu/eli/etat/leg/loi/2021/09/09/a676/jo",
            [Iri(Manifestation, LuxembourgQueryExecutionAdapter.JoluxLicense, CcBy)],
            ObservationRef,
            ObservationRef);

        Assert.HasCount(
            1,
            observation.SparqlRightsObservations.Observations,
            "the observation must carry the licence row its own assertions declare.");
        Assert.AreEqual(
            Manifestation, observation.SparqlRightsObservations.Observations[0].ManifestationIri);
        Assert.IsEmpty(
            observation.InFileRightsObservations.Observations,
            "channel two is genuinely empty, never a fabricated row.");
    }

    /// <summary>
    /// Channel one admitting alone is the typed pending state, and it is deliberately NOT
    /// agreement: two channels have not agreed, and saying they had is exactly the fabrication the
    /// disjointness rule guards against.
    /// </summary>
    [TestMethod]
    public void ChannelOneAdmittingAloneIsPendingAndNeverAgreement()
    {
        var resolution = Resolve(CcBy);

        Assert.AreEqual(LuxembourgRightsChannelDisposition.SecondChannelPending, resolution.Disposition);
        Assert.IsTrue(resolution.SparqlChannelAdmitsWithSecondChannelPending);
        Assert.IsFalse(
            resolution.ChannelsAgreeOnAdmittingLicence,
            "one channel cannot agree with itself.");
        Assert.IsNull(resolution.InFileObservation, "and no second observation is fabricated.");
    }

    /// <summary>
    /// The single-channel path applies the same value tail as the two-channel path: a non-admitting
    /// licence, an unruled one, a missing value and a multiple each keep their own named outcome
    /// rather than collapsing into "pending". Without this, pending would be a catch-all that hid a
    /// refusal.
    /// </summary>
    [TestMethod]
    public void TheSingleChannelPathKeepsEveryOtherOutcomeItsOwnName()
    {
        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl, Resolve(Scl).Disposition);
        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.TypedQuarantineUnruledLicence,
            Resolve("http://example.invalid/licence").Disposition);
        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.MissingValue, Resolve().Disposition);
        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.Multiple, Resolve(CcBy, Scl).Disposition);

        foreach (var disposition in new[]
        {
            Resolve(Scl).Disposition,
            Resolve("http://example.invalid/licence").Disposition,
            Resolve().Disposition,
            Resolve(CcBy, Scl).Disposition,
        })
        {
            Assert.AreNotEqual(LuxembourgRightsChannelDisposition.SecondChannelPending, disposition);
        }
    }

    /// <summary>
    /// No channel at all is still ChannelEnumerationUnproven: the pending state means channel one
    /// spoke, never that neither did.
    /// </summary>
    [TestMethod]
    public void NoChannelAtAllStaysUnproven()
    {
        var resolution = LuxembourgRightsChannels.Resolve(
            Manifestation,
            ObservationRef,
            new LuxembourgSparqlRightsChannelObservations(ObservationRef, ObservationRef, []),
            new LuxembourgInFileRightsChannelObservations(ObservationRef, ObservationRef, []));

        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.ChannelEnumerationUnproven, resolution.Disposition);
        Assert.IsFalse(resolution.SparqlChannelAdmitsWithSecondChannelPending);
    }

    private static LuxembourgRightsChannelResolution Resolve(params string[] licenceIris) =>
        LuxembourgRightsChannels.Resolve(
            Manifestation,
            ObservationRef,
            new LuxembourgSparqlRightsChannelObservations(
                ObservationRef,
                ObservationRef,
                [new LuxembourgRightsChannelObservation(
                    Manifestation, ObservationRef, ObservationRef, licenceIris)]),
            new LuxembourgInFileRightsChannelObservations(ObservationRef, ObservationRef, []));

    private static LuxembourgObservedAssertion Iri(string subject, string predicate, string value) =>
        new(subject, predicate, LuxembourgAssertionObjectKind.Iri, value, string.Empty, string.Empty, ObservationRef);
}
