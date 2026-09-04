using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Scope;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The ruling's fourth point: the snapshot reduces, as a pure function with no I/O, into
/// <see cref="EuScopeObjectDispositions"/>'s exact constructor shape.
/// </summary>
[TestClass]
public sealed class EuScopeSnapshotReductionTests
{
    private static string SeedA => EuAppendixASeedMap.PackRoots[0];

    private static SourceArtifactRef Artifact(string id) =>
        new($"urn:uuid:{DeterministicGuid(id)}", Digest("evidence:" + id));

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static Guid DeterministicGuid(string label) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("guid:" + label))[..16]);

    private static SourceObjectRef ObjectRef() => new(
        SourceCoreSchemaIds.SourceObjectRef,
        SourceAuthority.Cellar,
        new SourceRegistryMemberRef(Artifact("registry"), "work"),
        SeedA,
        "cellar:work:seed-a",
        Digest("cellar:work:seed-a"),
        Artifact("identity-profile"),
        null);

    private static IReadOnlyList<EuPredicateObservation> AllPredicatesNotObserved() =>
        EuScopeVocabulary.CdmPredicates
            .Select(p => new EuPredicateObservation(
                p, EuPredicateObservationState.NotObserved, [], Artifact("p-" + p)))
            .ToArray();

    private static EuRelationFamilyObservation Unacquired(EuRelationFamily family) =>
        new(family, EuRelationAcquisitionState.Unacquired, [], null);

    private static IReadOnlyList<EuRelationFamilyObservation> AllReadFamiliesUnacquired() =>
        EuScopeVocabulary.ReadRelationFamilies.Select(Unacquired).ToArray();

    private static EuChannelObservation Channel() =>
        new(EuChannel.CellarSparqlEndpoint, "eu_channel.sparql", "rule.channel", Artifact("channel"));

    private static EuCellarObjectSnapshot Snapshot(
        EuLanguageExpressionObservation? language = null,
        EuFormatObservation? format = null,
        EuContentClassObservation? rights = null,
        EuContentClassObservation? supporting = null,
        IReadOnlyList<EuRelationFamilyObservation>? relations = null)
    {
        var snapshot = EuCellarObjectSnapshot.TryObserve(
            ObjectRef(),
            SeedA,
            EuActForm.Regulation,
            Artifact("record"),
            AllPredicatesNotObserved(),
            Channel(),
            language,
            format,
            rights,
            relations ?? AllReadFamiliesUnacquired(),
            Artifact("relation-axis"),
            supporting,
            Artifact("supporting"),
            out var refusal);
        return snapshot ?? throw new InvalidOperationException($"fixture snapshot refused as {refusal}");
    }

    [TestMethod]
    public void AMinimalSnapshotReducesToAllNullOptionalAxesAndUnacquiredRelations()
    {
        var dispositions = EuScopeSnapshotReduction.Reduce(Snapshot());

        Assert.AreEqual(EuActForm.Regulation, dispositions.RecordForm);
        Assert.AreEqual(EuChannel.CellarSparqlEndpoint, dispositions.ChannelDisposition.Channel);
        Assert.AreEqual(EuChannelAdmission.Admitted, dispositions.ChannelDisposition.Admission);
        Assert.IsNull(dispositions.LanguageDisposition);
        Assert.IsNull(dispositions.FormatDisposition);
        Assert.IsNull(dispositions.RightsDisposition);
        Assert.IsNull(dispositions.SupportingContentClass);
        Assert.AreEqual(4, dispositions.RelationDispositions.Count);
        Assert.IsTrue(dispositions.RelationDispositions.All(
            r => r.Acquisition == EuRelationAcquisitionState.Unacquired));
    }

    [TestMethod]
    public void ANotObservedLanguageReducesToNullSoTheSelectorPublishesPublisherValueAbsent()
    {
        var language = new EuLanguageExpressionObservation(
            EuOfficialLanguage.German, EuExpressionObservationState.NotObserved,
            "eu_language.absent", "rule.language", Artifact("lang"));

        var dispositions = EuScopeSnapshotReduction.Reduce(Snapshot(language: language));

        Assert.IsNull(dispositions.LanguageDisposition);
    }

    [TestMethod]
    public void AnObservedBodyHeldLanguageReducesToBodyCandidate()
    {
        var language = new EuLanguageExpressionObservation(
            EuOfficialLanguage.English, EuExpressionObservationState.ExpressionObservedBodyCandidate,
            "eu_language.held", "rule.language", Artifact("lang"));

        var dispositions = EuScopeSnapshotReduction.Reduce(Snapshot(language: language));

        Assert.IsNotNull(dispositions.LanguageDisposition);
        Assert.AreEqual(EuLanguageBodyState.BodyCandidate, dispositions.LanguageDisposition!.BodyState);
        Assert.AreEqual(EuOfficialLanguage.English, dispositions.LanguageDisposition.Language);
    }

    [TestMethod]
    public void AnObservedBodyNotHeldLanguageReducesToPoint()
    {
        var language = new EuLanguageExpressionObservation(
            EuOfficialLanguage.German, EuExpressionObservationState.ExpressionObservedBodyNotHeld,
            "eu_language.point", "rule.language", Artifact("lang"));

        var dispositions = EuScopeSnapshotReduction.Reduce(Snapshot(language: language));

        Assert.IsNotNull(dispositions.LanguageDisposition);
        Assert.AreEqual(EuLanguageBodyState.BodyNotHeldPoint, dispositions.LanguageDisposition!.BodyState);
    }

    [TestMethod]
    public void AnObservedFormatCarriesItsOwnAdmissionThrough()
    {
        var format = new EuFormatObservation(
            EuManifestationFormat.Formex4, EuFormatBodyAdmission.BodyAdmitted,
            "eu_format.fmx4", Artifact("format"));

        var dispositions = EuScopeSnapshotReduction.Reduce(Snapshot(format: format));

        Assert.IsNotNull(dispositions.FormatDisposition);
        Assert.AreEqual(EuManifestationFormat.Formex4, dispositions.FormatDisposition!.Format);
        Assert.AreEqual(EuFormatBodyAdmission.BodyAdmitted, dispositions.FormatDisposition.Admission);
    }

    [TestMethod]
    public void AnObservedRightsContentClassReducesUsingTheReviewedBasis()
    {
        var rights = new EuContentClassObservation(EuContentClass.Metadata, Artifact("rights"));

        var dispositions = EuScopeSnapshotReduction.Reduce(Snapshot(rights: rights));

        Assert.IsNotNull(dispositions.RightsDisposition);
        Assert.AreEqual(EuReuseBasis.Cc0, dispositions.RightsDisposition!.Basis);
    }

    [TestMethod]
    public void ASupportingContentClassPassesThroughAsIs()
    {
        var supporting = new EuContentClassObservation(EuContentClass.Summary, Artifact("supporting"));

        var dispositions = EuScopeSnapshotReduction.Reduce(Snapshot(supporting: supporting));

        Assert.AreEqual(EuContentClass.Summary, dispositions.SupportingContentClass);
    }

    [TestMethod]
    public void ACompleteRelationFamilyWithASinglePublisherAssertedEdgeReducesCleanly()
    {
        var completion = Artifact("completion");
        var edge = new EuRelationEdgeObservation(
            EuRelationFamily.Amends, EuRelationAuthority.PublisherAsserted, SeedA, Artifact("edge"));
        var relations = new[]
        {
            new EuRelationFamilyObservation(
                EuRelationFamily.Amends, EuRelationAcquisitionState.Complete, [edge], completion),
            Unacquired(EuRelationFamily.Corrects),
            Unacquired(EuRelationFamily.BasedOn),
            Unacquired(EuRelationFamily.ConsolidatedBasedOn),
        };

        var dispositions = EuScopeSnapshotReduction.Reduce(Snapshot(relations: relations));

        var amends = dispositions.RelationDispositions.Single(r => r.Family == EuRelationFamily.Amends);
        Assert.AreEqual(EuRelationAcquisitionState.Complete, amends.Acquisition);
        Assert.AreEqual(EuRelationAuthority.PublisherAsserted, amends.Authority);
        Assert.AreEqual(completion, amends.CompletionEvidenceRef);
    }

    [TestMethod]
    public void TwoEdgesInOneFamilyUnderDifferentAuthoritiesThrows()
    {
        var edgeA = new EuRelationEdgeObservation(
            EuRelationFamily.Amends, EuRelationAuthority.PublisherAsserted, SeedA, Artifact("edge-a"));
        var edgeB = new EuRelationEdgeObservation(
            EuRelationFamily.Amends, EuRelationAuthority.LocalInboundView, SeedA, Artifact("edge-b"));
        var relations = new[]
        {
            new EuRelationFamilyObservation(
                EuRelationFamily.Amends, EuRelationAcquisitionState.Incomplete, [edgeA, edgeB], null),
            Unacquired(EuRelationFamily.Corrects),
            Unacquired(EuRelationFamily.BasedOn),
            Unacquired(EuRelationFamily.ConsolidatedBasedOn),
        };

        Assert.ThrowsExactly<InvalidOperationException>(
            () => EuScopeSnapshotReduction.Reduce(Snapshot(relations: relations)));
    }

    [TestMethod]
    public void AnOntologyAuthorizedInverseEdgeIsAnHonestlyDeclaredGap()
    {
        var edge = new EuRelationEdgeObservation(
            EuRelationFamily.Amends, EuRelationAuthority.OntologyAuthorizedInverse, SeedA,
            Artifact("edge"));
        var relations = new[]
        {
            new EuRelationFamilyObservation(
                EuRelationFamily.Amends, EuRelationAcquisitionState.Incomplete, [edge], null),
            Unacquired(EuRelationFamily.Corrects),
            Unacquired(EuRelationFamily.BasedOn),
            Unacquired(EuRelationFamily.ConsolidatedBasedOn),
        };

        Assert.ThrowsExactly<NotSupportedException>(
            () => EuScopeSnapshotReduction.Reduce(Snapshot(relations: relations)));
    }

    // ---- Fold-in: carry a null language disposition all the way through
    // EuScopeProfile.BuildScopeInput, proving the full pipeline integration (snapshot -> reduction ->
    // scope/1 input), not only this file's own reduction step in isolation. --------------------------

    [TestMethod]
    public void ANotObservedLanguageSurvivesReductionAndBuildScopeInputAsPublisherValueAbsent()
    {
        var language = new EuLanguageExpressionObservation(
            EuOfficialLanguage.German, EuExpressionObservationState.NotObserved,
            "eu_language.absent", "rule.language", Artifact("lang"));

        // Starts at the snapshot: a language Expression the closure query never asked about at all.
        var dispositions = EuScopeSnapshotReduction.Reduce(Snapshot(language: language));
        Assert.IsNull(dispositions.LanguageDisposition);

        // Carries through EuScopeProfile.BuildScopeInput, the now-merged item-5 type: the missing
        // language disposition above must publish PublisherValueAbsent on the wire, not
        // SelectorNotApplicable, which is what a missing format or missing rights basis publish
        // instead (EuScopeProfileTests drives that distinction directly against a hand-built
        // EuScopeObjectDispositions; this test drives the same wire fact end to end from a real
        // snapshot reduction, which is the integration no test before this fold-in exercised).
        var profile = EuScopeProfile.BuildBinding();
        // The relation axis is "present" here, not "not applicable": Snapshot()'s default relation
        // observations are every read family marked Unacquired rather than omitted, and
        // EuScopeSnapshotReduction still reduces an unacquired family to a disposition entry (see
        // EuScopeSnapshotReductionTests.AMinimalSnapshotReducesToAllNullOptionalAxesAndUnacquiredRelations),
        // so RelationDispositions is non-empty and its selector cites RelationEvidenceRef.
        var evidenceRefs = new[]
            {
                dispositions.RecordEvidenceRef,
                dispositions.ChannelDisposition.EvidenceRef,
                dispositions.RelationEvidenceRef,
            }
            .Distinct()
            .OrderBy(static value => value.ResourceId, StringComparer.Ordinal)
            .ThenBy(static value => value.Sha256, StringComparer.Ordinal)
            .ToArray();
        var evidenceOrdinals = evidenceRefs
            .Select(static (value, ordinal) => (value, ordinal))
            .ToDictionary(static value => value.value, static value => value.ordinal);

        var input = EuScopeProfile.BuildScopeInput(profile, dispositions, evidenceOrdinals);
        var languageSelector = input.Selectors[2];

        Assert.AreEqual(ScopeSelectorState.PublisherValueAbsent, languageSelector.State);
        Assert.AreEqual(
            ScopeSelectorEvidenceKind.CompleteObservationAbsence,
            languageSelector.EvidenceKind);
        Assert.IsEmpty(languageSelector.CanonicalValues);
        Assert.IsNull(languageSelector.RuleOrdinal);
    }

    [TestMethod]
    public void ReduceIsPureAndDeterministicAcrossRepeatedCalls()
    {
        var snapshot = Snapshot();
        var first = EuScopeSnapshotReduction.Reduce(snapshot);
        var second = EuScopeSnapshotReduction.Reduce(snapshot);
        Assert.AreEqual(first.RecordForm, second.RecordForm);
        Assert.AreEqual(first.ChannelDisposition.Admission, second.ChannelDisposition.Admission);
        Assert.AreEqual(first.RelationDispositions.Count, second.RelationDispositions.Count);
    }
}
