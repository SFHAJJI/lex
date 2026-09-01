using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

[TestClass]
public sealed class LuxembourgRightsChannelsTests
{
    private const string SelectedManifestation =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo/fr/xml";
    private const string OtherManifestation =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/02/a2/jo/fr/xml";
    private const string CcBy40 = "http://creativecommons.org/licenses/by/4.0/";
    private const string LicenceScl =
        "http://data.legilux.public.lu/resource/authority/license/licenceSCL";
    private const string UnruledLicence = "https://example.invalid/licence/future";

    [TestMethod]
    public void EqualCurrentCcByChannelsAreTheOnlyAdmittedResult()
    {
        var result = Resolve(
            [Observation(SelectedManifestation, Run, SparqlEvidence, [CcBy40])],
            [Observation(SelectedManifestation, Run, InFileEvidence, [CcBy40])]);

        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.AgreedSameRunCcBy,
            result.Disposition);
        Assert.AreEqual("rights_agreed_same_run_dual_channel_cc_by_4_0", result.ReasonCode);
        Assert.IsTrue(result.ChannelsAgreeOnAdmittingLicence);
        Assert.AreEqual(SparqlEnumerationRef, result.SparqlObservations.EnumerationRef);
        Assert.AreEqual(InFileEnumerationRef, result.InFileObservations.EnumerationRef);
    }

    [TestMethod]
    public void MissingRowCannotBecomeAnAbsenceClaimBeforeEnumerationAdmission()
    {
        var result = Resolve(
            [Observation(OtherManifestation, Run, SparqlEvidence, [CcBy40])],
            [
                Observation(
                    SelectedManifestation,
                    Run,
                    InFileEvidence,
                    [CcBy40, LicenceScl]),
            ]);

        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.ChannelEnumerationUnproven,
            result.Disposition);
        Assert.AreEqual("rights_channel_enumeration_unproven", result.ReasonCode);
        Assert.IsNull(result.SparqlObservation);
        Assert.IsNotNull(result.InFileObservation);
        Assert.IsFalse(result.ChannelsAgreeOnAdmittingLicence);
    }

    [TestMethod]
    public void ObservedEmptyChannelIsMissingValueNotMissingChannel()
    {
        var result = Resolve(
            [Observation(SelectedManifestation, Run, SparqlEvidence, [])],
            [Observation(SelectedManifestation, Run, InFileEvidence, [CcBy40, LicenceScl])]);

        Assert.AreEqual(LuxembourgRightsChannelDisposition.MissingValue, result.Disposition);
        Assert.AreEqual("rights_missing_value", result.ReasonCode);
        Assert.IsNotNull(result.SparqlObservation);
        Assert.IsNotNull(result.InFileObservation);
    }

    [TestMethod]
    public void StaleRunOutranksEvidenceAndLicenceDefects()
    {
        var result = LuxembourgRightsChannels.Resolve(
            SelectedManifestation,
            Run,
            new LuxembourgSparqlRightsChannelObservations(
                StaleRun,
                SparqlEnumerationRef,
                [
                    Observation(
                        SelectedManifestation,
                        StaleRun,
                        SharedEvidence,
                        [CcBy40, LicenceScl]),
                ]),
            new LuxembourgInFileRightsChannelObservations(
                Run,
                InFileEnumerationRef,
                [Observation(SelectedManifestation, Run, SharedEvidence, [LicenceScl])]));

        Assert.AreEqual(LuxembourgRightsChannelDisposition.Stale, result.Disposition);
        Assert.AreEqual("rights_stale_run", result.ReasonCode);
    }

    [TestMethod]
    public void IdenticalCrossChannelEvidenceIsRetainedButCannotAuthorize()
    {
        var result = Resolve(
            [Observation(SelectedManifestation, Run, SharedEvidence, [CcBy40])],
            [Observation(SelectedManifestation, Run, SharedEvidence, [CcBy40])]);

        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.EvidenceNotIndependent,
            result.Disposition);
        Assert.AreEqual("rights_evidence_not_independent", result.ReasonCode);
        Assert.AreSame(result.SparqlObservation, result.SparqlObservations.Observations[0]);
        Assert.AreSame(result.InFileObservation, result.InFileObservations.Observations[0]);
        Assert.IsFalse(result.ChannelsAgreeOnAdmittingLicence);
    }

    [TestMethod]
    public void EvidenceIndependenceOutranksMultipleLicenceValues()
    {
        var result = Resolve(
            [Observation(SelectedManifestation, Run, SharedEvidence, [CcBy40, LicenceScl])],
            [Observation(SelectedManifestation, Run, SharedEvidence, [CcBy40, LicenceScl])]);

        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.EvidenceNotIndependent,
            result.Disposition);
    }

    [TestMethod]
    public void MultipleLicenceValuesOutrankSingletonConflict()
    {
        var result = Resolve(
            [Observation(SelectedManifestation, Run, SparqlEvidence, [CcBy40, LicenceScl])],
            [Observation(SelectedManifestation, Run, InFileEvidence, [LicenceScl])]);

        Assert.AreEqual(LuxembourgRightsChannelDisposition.Multiple, result.Disposition);
        Assert.AreEqual("rights_multiple", result.ReasonCode);
    }

    [TestMethod]
    public void UnequalSingletonsAreAConflict()
    {
        var result = Resolve(
            [Observation(SelectedManifestation, Run, SparqlEvidence, [CcBy40])],
            [Observation(SelectedManifestation, Run, InFileEvidence, [LicenceScl])]);

        Assert.AreEqual(LuxembourgRightsChannelDisposition.Conflict, result.Disposition);
        Assert.AreEqual("rights_conflict", result.ReasonCode);
    }

    [TestMethod]
    public void EqualLicenceSclChannelsAreExplicitlyNonAdmitting()
    {
        var result = Resolve(
            [Observation(SelectedManifestation, Run, SparqlEvidence, [LicenceScl])],
            [Observation(SelectedManifestation, Run, InFileEvidence, [LicenceScl])]);

        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl,
            result.Disposition);
        Assert.AreEqual("rights_non_admitting_licence_scl", result.ReasonCode);
        Assert.IsFalse(result.ChannelsAgreeOnAdmittingLicence);
    }

    [TestMethod]
    public void EqualUnruledChannelsAreTypedQuarantine()
    {
        var result = Resolve(
            [Observation(SelectedManifestation, Run, SparqlEvidence, [UnruledLicence])],
            [Observation(SelectedManifestation, Run, InFileEvidence, [UnruledLicence])]);

        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.TypedQuarantineUnruledLicence,
            result.Disposition);
        Assert.AreEqual("rights_typed_quarantine_unruled_licence", result.ReasonCode);
        Assert.IsFalse(result.ChannelsAgreeOnAdmittingLicence);
    }

    [TestMethod]
    public void CrossManifestationRowsAreRetainedAndCannotAuthorizeSelectedManifestation()
    {
        var result = Resolve(
            [Observation(OtherManifestation, Run, SparqlEvidence, [CcBy40])],
            [Observation(OtherManifestation, Run, InFileEvidence, [CcBy40])]);

        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.ChannelEnumerationUnproven,
            result.Disposition);
        Assert.AreEqual(
            OtherManifestation,
            result.SparqlObservations.Observations.Single().ManifestationIri);
        Assert.AreEqual(
            OtherManifestation,
            result.InFileObservations.Observations.Single().ManifestationIri);
        Assert.IsNull(result.SparqlObservation);
        Assert.IsNull(result.InFileObservation);
    }

    [TestMethod]
    public void CrossManifestationRowsDoNotDisturbExactSelectedAdmission()
    {
        var result = Resolve(
            [
                Observation(OtherManifestation, Run, SharedEvidence, [LicenceScl]),
                Observation(SelectedManifestation, Run, SparqlEvidence, [CcBy40]),
            ],
            [
                Observation(SelectedManifestation, Run, InFileEvidence, [CcBy40]),
                Observation(OtherManifestation, Run, SharedEvidence, [CcBy40, LicenceScl]),
            ]);

        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.AgreedSameRunCcBy,
            result.Disposition);
        Assert.AreEqual(2, result.SparqlObservations.Observations.Count);
        Assert.AreEqual(2, result.InFileObservations.Observations.Count);
        Assert.AreEqual(SelectedManifestation, result.SparqlObservation!.ManifestationIri);
        Assert.AreEqual(SelectedManifestation, result.InFileObservation!.ManifestationIri);
    }

    [TestMethod]
    public void ChannelConstructionRejectsDuplicateManifestationKeys()
    {
        var duplicateRows = new[]
        {
            Observation(SelectedManifestation, Run, SparqlEvidence, [CcBy40]),
            Observation(SelectedManifestation, StaleRun, InFileEvidence, [LicenceScl]),
        };

        Assert.ThrowsExactly<ArgumentException>(() =>
            new LuxembourgSparqlRightsChannelObservations(
                Run,
                SparqlEnumerationRef,
                duplicateRows));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new LuxembourgInFileRightsChannelObservations(
                Run,
                InFileEnumerationRef,
                duplicateRows));
    }

    [TestMethod]
    public void ChannelConstructionRejectsRowsFromAnotherRun()
    {
        var stale = Observation(SelectedManifestation, StaleRun, SparqlEvidence, [CcBy40]);

        Assert.ThrowsExactly<ArgumentException>(() =>
            new LuxembourgSparqlRightsChannelObservations(
                Run,
                SparqlEnumerationRef,
                [stale]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new LuxembourgInFileRightsChannelObservations(
                Run,
                InFileEnumerationRef,
                [stale]));
    }

    [TestMethod]
    public void StaleChannelEnumerationOutranksAnUnprovedMissingRow()
    {
        var result = LuxembourgRightsChannels.Resolve(
            SelectedManifestation,
            Run,
            new LuxembourgSparqlRightsChannelObservations(
                StaleRun,
                SparqlEnumerationRef,
                []),
            new LuxembourgInFileRightsChannelObservations(
                Run,
                InFileEnumerationRef,
                []));

        Assert.AreEqual(LuxembourgRightsChannelDisposition.Stale, result.Disposition);
    }

    [TestMethod]
    public void DifferentlyNamedEvidenceWithTheSameBytesIsNotIndependent()
    {
        var sameBytesOtherId = new SourceArtifactRef(
            "urn:uuid:98b2abb1-bce4-4278-b543-50cbd39c7e2d",
            SparqlEvidence.Sha256);
        var result = Resolve(
            [Observation(SelectedManifestation, Run, SparqlEvidence, [CcBy40])],
            [Observation(SelectedManifestation, Run, sameBytesOtherId, [CcBy40])]);

        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.EvidenceNotIndependent,
            result.Disposition);
    }

    [TestMethod]
    public void EvidenceCannotBeSwappedAcrossRowAndEnumerationRoles()
    {
        var result = LuxembourgRightsChannels.Resolve(
            SelectedManifestation,
            Run,
            new LuxembourgSparqlRightsChannelObservations(
                Run,
                InFileEvidence,
                [Observation(SelectedManifestation, Run, SparqlEvidence, [CcBy40])]),
            new LuxembourgInFileRightsChannelObservations(
                Run,
                SparqlEvidence,
                [Observation(SelectedManifestation, Run, InFileEvidence, [CcBy40])]));

        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.EvidenceNotIndependent,
            result.Disposition);
    }

    [TestMethod]
    public void ObservationRequiresSortedUniqueExactLicenceIris()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            Observation(SelectedManifestation, Run, SparqlEvidence, [LicenceScl, CcBy40]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            Observation(SelectedManifestation, Run, SparqlEvidence, [CcBy40, CcBy40]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            Observation(SelectedManifestation, Run, SparqlEvidence, ["relative-licence"]));
    }

    [TestMethod]
    public void InputsAreCopiedAndCanonicalizedWithoutLosingCrossManifestationRows()
    {
        var licences = new[] { CcBy40 };
        var rows = new[]
        {
            Observation(SelectedManifestation, Run, SparqlEvidence, licences),
            Observation(OtherManifestation, Run, SharedEvidence, [LicenceScl]),
        };
        var channel = new LuxembourgSparqlRightsChannelObservations(
            Run,
            SparqlEnumerationRef,
            rows);

        licences[0] = LicenceScl;
        rows[0] = Observation(OtherManifestation, StaleRun, InFileEvidence, []);

        CollectionAssert.AreEqual(
            new[] { SelectedManifestation, OtherManifestation },
            channel.Observations.Select(static row => row.ManifestationIri).ToArray());
        Assert.AreEqual(CcBy40, channel.Observations[0].LicenceIris.Single());
    }

    private static LuxembourgRightsChannelResolution Resolve(
        IReadOnlyList<LuxembourgRightsChannelObservation> sparql,
        IReadOnlyList<LuxembourgRightsChannelObservation> inFile) =>
        LuxembourgRightsChannels.Resolve(
            SelectedManifestation,
            Run,
            new LuxembourgSparqlRightsChannelObservations(
                Run,
                SparqlEnumerationRef,
                sparql),
            new LuxembourgInFileRightsChannelObservations(
                Run,
                InFileEnumerationRef,
                inFile));

    private static LuxembourgRightsChannelObservation Observation(
        string manifestationIri,
        SourceArtifactRef runIdentity,
        SourceArtifactRef evidenceRef,
        IReadOnlyList<string> licenceIris) => new(
        manifestationIri,
        runIdentity,
        evidenceRef,
        licenceIris);

    private static SourceArtifactRef Run { get; } = Artifact(
        "07b972ed-2a90-4f0a-af09-19ba7c86bc26",
        '1');

    private static SourceArtifactRef StaleRun { get; } = Artifact(
        "bd96f7f4-24a8-44d1-b9cc-4ca1716918d0",
        '2');

    private static SourceArtifactRef SparqlEvidence { get; } = Artifact(
        "4a204e37-4b19-4f73-b3e2-101f4a12a861",
        '3');

    private static SourceArtifactRef InFileEvidence { get; } = Artifact(
        "b458bce1-a4df-40a9-981c-037961aacb4f",
        '4');

    private static SourceArtifactRef SharedEvidence { get; } = Artifact(
        "e095c0de-3aa2-457e-ad93-ec4b8f66f39f",
        '5');

    private static SourceArtifactRef SparqlEnumerationRef { get; } = Artifact(
        "0bf0868f-4bf6-4759-8897-4106168d34f4",
        '6');

    private static SourceArtifactRef InFileEnumerationRef { get; } = Artifact(
        "6ae809e6-b8aa-4038-a71a-70b228f71004",
        '7');

    private static SourceArtifactRef Artifact(string id, char digestCharacter) => new(
        "urn:uuid:" + id,
        new string(digestCharacter, 64));
}
