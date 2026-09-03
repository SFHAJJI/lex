using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Scope;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The binding of the Union's already-merged scope dispositions onto the one shared
/// <c>scope/1</c> manifest family (<see cref="ScopeManifest"/>). Item 5 of
/// <c>STAGE1-AUTHORITY-AND-QUEUE-2026-09-03.md</c>.
/// </summary>
[TestClass]
public sealed class EuScopeProfileTests
{
    private const string Digest =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [TestMethod]
    public void TheBindingCoversAllFourAxesOnceEachAndCarriesEuOwnIdentityNotLuxembourgs()
    {
        var binding = EuScopeProfile.BuildBinding();

        CollectionAssert.AreEquivalent(
            Enum.GetValues<ScopeAxis>(),
            binding.OrderedRules.Select(static rule => rule.Axis).ToArray());
        Assert.HasCount(4, binding.OrderedRules);
        Assert.HasCount(7, binding.OrderedSelectorMemberOrdinals);

        // Every ordinal in the table must be distinct and in range; ScopeProfileBinding's own
        // constructor already enforces this, so a successful BuildBinding() call is itself
        // evidence, but the axis-coverage and selector-count checks above are the ones this test
        // exists to pin.
        Assert.IsTrue(binding.OrderedMembers.Count >= 8);

        // The Union's identity must differ from Luxembourg's already-merged one (pinned literally,
        // from VerifiedLuxembourgSourceProfile's own ProfileResourceId and SelectorTableResourceId
        // constants, rather than constructing a Luxembourg profile in a Europe test): the whole
        // point of "mapped, not renamed" is that a produced manifest is traceable to the publisher
        // whose reviewed policy produced it, not folded into a shared, provenance-less identity.
        Assert.AreNotEqual(
            "urn:uuid:19191414-0517-46fb-b4e0-bc6231601c88",
            binding.SourceProfileRef.ResourceId);
        Assert.AreNotEqual(
            "urn:uuid:72fdaf8b-e367-43c5-8b34-e22a99bfdbe7",
            binding.SelectorTableRef.ResourceId);
        foreach (var member in binding.OrderedMembers)
        {
            StringAssert.StartsWith(member.MemberKey, "eu_");
        }
    }

    [TestMethod]
    public void BuildBindingIsDeterministicAcrossCalls()
    {
        var first = EuScopeProfile.BuildBinding();
        var second = EuScopeProfile.BuildBinding();

        Assert.AreEqual(first.SourceProfileRef, second.SourceProfileRef);
        Assert.AreEqual(first.SelectorTableRef, second.SelectorTableRef);
        Assert.AreEqual(first.BodyCandidateRoleMemberOrdinal, second.BodyCandidateRoleMemberOrdinal);
        CollectionAssert.AreEqual(
            first.OrderedMembers.Select(static m => (m.RegistryRef, m.MemberKey)).ToArray(),
            second.OrderedMembers.Select(static m => (m.RegistryRef, m.MemberKey)).ToArray());
        CollectionAssert.AreEqual(
            first.OrderedRules.Select(static r => (r.Axis, r.RuleMemberOrdinal, r.Ordinal)).ToArray(),
            second.OrderedRules.Select(static r => (r.Axis, r.RuleMemberOrdinal, r.Ordinal)).ToArray());
    }

    [TestMethod]
    public void AnAcceptedBodyCandidateReducesToAcceptedOnRecordBodyAndRelation()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = new EuScopeObjectDispositions(
            ObjectRef("32016R0679"),
            EuActForm.Regulation,
            Artifact("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            Channel(EuChannel.CellarSparqlEndpoint),
            Language(EuOfficialLanguage.English, EuLanguageBodyState.BodyCandidate),
            Format(EuManifestationFormat.Formex4, EuFormatBodyAdmission.BodyAdmitted),
            Rights(EuContentClass.OriginalLegalText),
            Artifact("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
            [Relation(EuRelationFamily.Amends, EuRelationAcquisitionState.Complete)],
            Artifact("cccccccc-cccc-4ccc-8ccc-cccccccccccc"),
            null,
            Artifact("dddddddd-dddd-4ddd-8ddd-dddddddddddd"));

        var verified = ReduceOne(profile, dispositions);

        var reduction = ScopeReducer.ReduceRequest(
            verified,
            dispositions.ObjectRef,
            Enum.GetValues<ScopeAxis>());
        AssertAxis(reduction, ScopeAxis.Record, ScopeDisposition.AcceptedSelected);
        AssertAxis(reduction, ScopeAxis.Body, ScopeDisposition.AcceptedSelected);
        AssertAxis(reduction, ScopeAxis.Relation, ScopeDisposition.AcceptedSelected);
        AssertAxis(reduction, ScopeAxis.SupportingDocument, ScopeDisposition.Point);
        CollectionAssert.AreEqual(new[] { 0 }, verified.Manifest.BodyCandidateOrdinals.ToArray());
    }

    [TestMethod]
    public void AnExcludedAcquisitionChannelCapsTheBodyAxisAtPointEvenWithAGoodFormat()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "portal-only",
            channel: Channel(EuChannel.EurLexPortal),
            language: Language(EuOfficialLanguage.English, EuLanguageBodyState.BodyCandidate),
            format: Format(EuManifestationFormat.Formex4, EuFormatBodyAdmission.BodyAdmitted),
            rights: Rights(EuContentClass.OriginalLegalText));

        var verified = ReduceOne(profile, dispositions);
        var body = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Body])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.Point, body.Disposition);
        Assert.IsEmpty(verified.Manifest.BodyCandidateOrdinals);
    }

    [TestMethod]
    public void ALanguageWhoseBodyIsNotHeldCapsTheBodyAxisAtPoint()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "german-only",
            channel: Channel(EuChannel.CellarSparqlEndpoint),
            language: Language(EuOfficialLanguage.German, EuLanguageBodyState.BodyNotHeldPoint),
            format: Format(EuManifestationFormat.Formex4, EuFormatBodyAdmission.BodyAdmitted),
            rights: Rights(EuContentClass.OriginalLegalText));

        var verified = ReduceOne(profile, dispositions);
        var body = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Body])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.Point, body.Disposition);
    }

    [TestMethod]
    public void PrintIsTheOnlyFormatThatDeniesTheBodyAxisOutright()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "print-only",
            channel: Channel(EuChannel.CellarSparqlEndpoint),
            language: Language(EuOfficialLanguage.French, EuLanguageBodyState.BodyCandidate),
            format: Format(EuManifestationFormat.Print, EuFormatBodyAdmission.BodyNotAdmitted),
            rights: Rights(EuContentClass.OriginalLegalText));

        var verified = ReduceOne(profile, dispositions);
        var reduction = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Body]);
        var body = reduction.AllAxisResults.Single(r => r.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.NeverIngest, body.Disposition);
        Assert.AreEqual(ScopeRuleEffect.ExactDenial, body.Effect);
    }

    [TestMethod]
    public void ABodyNotAdmittedNonPrintFormatIsATypedGapNotAnExclusion()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "html-gap",
            channel: Channel(EuChannel.CellarSparqlEndpoint),
            language: Language(EuOfficialLanguage.French, EuLanguageBodyState.BodyCandidate),
            format: Format(EuManifestationFormat.Html, EuFormatBodyAdmission.BodyNotAdmitted),
            rights: Rights(EuContentClass.OriginalLegalText));

        var verified = ReduceOne(profile, dispositions);
        var body = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Body])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.TypedQuarantine, body.Disposition);
    }

    [TestMethod]
    public void NoRightsDispositionYetLeavesAnOtherwiseReadyBodyQuarantined()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "rights-pending",
            channel: Channel(EuChannel.CellarSparqlEndpoint),
            language: Language(EuOfficialLanguage.English, EuLanguageBodyState.BodyCandidate),
            format: Format(EuManifestationFormat.Formex4, EuFormatBodyAdmission.BodyAdmitted),
            rights: null);

        var verified = ReduceOne(profile, dispositions);
        var body = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Body])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.TypedQuarantine, body.Disposition);
    }

    [TestMethod]
    public void AnIncompleteRelationFamilyQuarantinesTheWholeRelationAxis()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = new EuScopeObjectDispositions(
            ObjectRef("incomplete-relation"),
            EuActForm.Directive,
            Artifact("11111111-1111-4111-8111-111111111111"),
            Channel(EuChannel.CellarSparqlEndpoint),
            null,
            null,
            null,
            Artifact("22222222-2222-4222-8222-222222222222"),
            [Relation(EuRelationFamily.BasedOn, EuRelationAcquisitionState.Incomplete)],
            Artifact("33333333-3333-4333-8333-333333333333"),
            null,
            Artifact("44444444-4444-4444-8444-444444444444"));

        var verified = ReduceOne(profile, dispositions);
        var relation = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Relation])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Relation);
        Assert.AreEqual(ScopeDisposition.TypedQuarantine, relation.Disposition);
    }

    [TestMethod]
    public void NoRelationsAtAllIsNotApplicableRatherThanQuarantined()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = new EuScopeObjectDispositions(
            ObjectRef("no-relations"),
            EuActForm.Decision,
            Artifact("55555555-5555-4555-8555-555555555555"),
            Channel(EuChannel.CellarSparqlEndpoint),
            null,
            null,
            null,
            Artifact("66666666-6666-4666-8666-666666666666"),
            [],
            Artifact("77777777-7777-4777-8777-777777777777"),
            null,
            Artifact("88888888-8888-4888-8888-888888888888"));

        var verified = ReduceOne(profile, dispositions);
        var relation = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Relation])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Relation);
        Assert.AreEqual(ScopeDisposition.Point, relation.Disposition);
    }

    [TestMethod]
    public void ASummaryIsAnAcceptedSupportingDocumentButMetadataIsAnUnclassifiedShape()
    {
        var profile = EuScopeProfile.BuildBinding();
        var summary = new EuScopeObjectDispositions(
            ObjectRef("summary"),
            EuActForm.Regulation,
            Artifact("99999999-9999-4999-8999-999999999999"),
            Channel(EuChannel.CellarSparqlEndpoint),
            null,
            null,
            null,
            Artifact("aaaaaaaa-1111-4aaa-8aaa-aaaaaaaaaaaa"),
            [],
            Artifact("bbbbbbbb-1111-4bbb-8bbb-bbbbbbbbbbbb"),
            EuContentClass.Summary,
            Artifact("cccccccc-1111-4ccc-8ccc-cccccccccccc"));
        var summaryVerified = ReduceOne(profile, summary);
        var summarySupport = ScopeReducer.ReduceRequest(
            summaryVerified,
            summary.ObjectRef,
            [ScopeAxis.SupportingDocument])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.SupportingDocument);
        Assert.AreEqual(ScopeDisposition.AcceptedSelected, summarySupport.Disposition);

        var metadata = new EuScopeObjectDispositions(
            ObjectRef("metadata-shape"),
            EuActForm.Regulation,
            Artifact("dddddddd-1111-4ddd-8ddd-dddddddddddd"),
            Channel(EuChannel.CellarSparqlEndpoint),
            null,
            null,
            null,
            Artifact("eeeeeeee-1111-4eee-8eee-eeeeeeeeeeee"),
            [],
            Artifact("ffffffff-1111-4fff-8fff-ffffffffffff"),
            EuContentClass.Metadata,
            Artifact("11111111-2222-4111-8111-111111111111"));
        var metadataVerified = ReduceOne(profile, metadata);
        var metadataSupport = ScopeReducer.ReduceRequest(
            metadataVerified,
            metadata.ObjectRef,
            [ScopeAxis.SupportingDocument])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.SupportingDocument);
        Assert.AreEqual(ScopeDisposition.TypedQuarantine, metadataSupport.Disposition);
    }

    [TestMethod]
    public void DispositionsRefuseADuplicateRelationFamily()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EuScopeObjectDispositions(
            ObjectRef("dup"),
            EuActForm.Regulation,
            Artifact("12121212-1212-4212-8212-121212121212"),
            Channel(EuChannel.CellarSparqlEndpoint),
            null,
            null,
            null,
            Artifact("13131313-1313-4313-8313-131313131313"),
            [
                Relation(EuRelationFamily.Amends, EuRelationAcquisitionState.Complete),
                Relation(EuRelationFamily.Amends, EuRelationAcquisitionState.Complete),
            ],
            Artifact("14141414-1414-4414-8414-141414141414"),
            null,
            Artifact("15151515-1515-4515-8515-151515151515")));
    }

    private static EuScopeObjectDispositions Baseline(
        ScopeProfileBinding profile,
        string key,
        EuChannelDisposition channel,
        EuLanguageBodyDisposition? language,
        EuFormatDisposition? format,
        EuRightsDisposition? rights)
    {
        _ = profile;
        return new EuScopeObjectDispositions(
            ObjectRef(key),
            EuActForm.Regulation,
            Artifact("a1a1a1a1-0000-4000-8000-000000000001"),
            channel,
            language,
            format,
            rights,
            Artifact("a1a1a1a1-0000-4000-8000-000000000002"),
            [Relation(EuRelationFamily.Amends, EuRelationAcquisitionState.Complete)],
            Artifact("a1a1a1a1-0000-4000-8000-000000000003"),
            null,
            Artifact("a1a1a1a1-0000-4000-8000-000000000004"));
    }

    private static void AssertAxis(
        ScopeRequestReduction reduction,
        ScopeAxis axis,
        ScopeDisposition expected)
    {
        var actual = reduction.AllAxisResults.Single(r => r.Axis == axis);
        Assert.AreEqual(expected, actual.Disposition, $"axis {axis}");
    }

    private static VerifiedScopeManifest ReduceOne(
        ScopeProfileBinding profile,
        EuScopeObjectDispositions dispositions)
    {
        // The evidence-artifact table must contain exactly the artifacts a selector actually
        // references (ScopeReducer.VerifyAndOpen enforces this), and which of the four candidate
        // refs are used depends on which selectors came back "not applicable" rather than
        // "present" -- a caller decision this test does not want to duplicate. So build once
        // against the full candidate set to learn which ordinals were actually used, then rebuild
        // the table and the input against exactly that subset.
        var candidateRefs = new[]
            {
                dispositions.RecordEvidenceRef,
                dispositions.BodyEvidenceRef,
                dispositions.RelationEvidenceRef,
                dispositions.SupportingEvidenceRef,
            }
            .Distinct()
            .OrderBy(static value => value.ResourceId, StringComparer.Ordinal)
            .ThenBy(static value => value.Sha256, StringComparer.Ordinal)
            .ToArray();
        var candidateOrdinals = candidateRefs
            .Select(static (value, ordinal) => (value, ordinal))
            .ToDictionary(static value => value.value, static value => value.ordinal);
        var probe = EuScopeProfile.BuildScopeInput(profile, dispositions, candidateOrdinals);
        var usedRefs = probe.Selectors
            .Where(static selector => selector.EvidenceArtifactOrdinal is not null)
            .Select(selector => candidateRefs[selector.EvidenceArtifactOrdinal!.Value])
            .Distinct()
            .OrderBy(static value => value.ResourceId, StringComparer.Ordinal)
            .ThenBy(static value => value.Sha256, StringComparer.Ordinal)
            .ToArray();
        var evidenceOrdinals = usedRefs
            .Select(static (value, ordinal) => (value, ordinal))
            .ToDictionary(static value => value.value, static value => value.ordinal);

        var input = EuScopeProfile.BuildScopeInput(profile, dispositions, evidenceOrdinals);
        var resolver = ExactResolver.For(profile, usedRefs, [input]);
        return ScopeReducer.Reduce(
            profile,
            usedRefs,
            [dispositions.ObjectRef],
            [input],
            resolver);
    }

    private static EuChannelDisposition Channel(EuChannel channel) => new(
        channel,
        EuChannelDisposition.PolicyFor(channel),
        "test_channel_reason",
        "test_channel_rule",
        Artifact("f0f0f0f0-0000-4000-8000-000000000001"));

    private static EuLanguageBodyDisposition Language(
        EuOfficialLanguage language,
        EuLanguageBodyState state) => new(
        language,
        state,
        "test_language_reason",
        "test_language_rule",
        Artifact("f0f0f0f0-0000-4000-8000-000000000002"));

    private static EuFormatDisposition Format(
        EuManifestationFormat format,
        EuFormatBodyAdmission admission) => new(
        format,
        admission,
        "test_format_reason",
        Artifact("f0f0f0f0-0000-4000-8000-000000000003"));

    private static EuRightsDisposition Rights(EuContentClass contentClass) => new(
        contentClass,
        EuRightsDisposition.BasisFor(contentClass),
        Artifact("f0f0f0f0-0000-4000-8000-000000000004"));

    private static EuRelationFamilyDisposition Relation(
        EuRelationFamily family,
        EuRelationAcquisitionState acquisition) => new(
        family,
        EuRelationAuthority.PublisherAsserted,
        acquisition,
        acquisition == EuRelationAcquisitionState.Complete
            ? Artifact("f0f0f0f0-0000-4000-8000-000000000005")
            : null,
        null);

    private static SourceObjectRef ObjectRef(string key) => new(
        SourceCoreSchemaIds.SourceObjectRef,
        SourceAuthority.Cellar,
        new SourceRegistryMemberRef(
            Artifact("44aa505f-d55f-4d6c-aef0-21ddcb46633d"),
            "work"),
        $"http://publications.europa.eu/resource/cellar/{key}",
        $"cellar:work:{key}",
        Sha256($"cellar:work:{key}"),
        Artifact("08ca1acc-142a-4807-8cc0-d84e412e1d07"),
        null);

    private static SourceArtifactRef Artifact(string id) => new($"urn:uuid:{id}", Digest);

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class ExactResolver : IScopeReductionEvidenceResolver
    {
        private readonly HashSet<ScopeSelectorObservationBinding> _bindings;
        private readonly HashSet<ScopeSelectorNotApplicableBinding> _notApplicableBindings;
        private readonly HashSet<ScopeRuleEvaluationBinding> _ruleBindings;
        private readonly ScopeCompleteEnumerationBinding _completeEnumerationBinding;

        private ExactResolver(
            IEnumerable<ScopeSelectorObservationBinding> bindings,
            IEnumerable<ScopeSelectorNotApplicableBinding> notApplicableBindings,
            IEnumerable<ScopeRuleEvaluationBinding> ruleBindings,
            ScopeCompleteEnumerationBinding completeEnumerationBinding)
        {
            _bindings = bindings.ToHashSet();
            _notApplicableBindings = notApplicableBindings.ToHashSet();
            _ruleBindings = ruleBindings.ToHashSet();
            _completeEnumerationBinding = completeEnumerationBinding;
        }

        public SourceArtifactRef CompleteEnumerationRef =>
            _completeEnumerationBinding.CompleteEnumerationRef;

        public static ExactResolver For(
            ScopeProfileBinding profile,
            IReadOnlyList<SourceArtifactRef> evidence,
            IReadOnlyList<ScopeObjectReductionInput> inputs)
        {
            var bindings = new List<ScopeSelectorObservationBinding>();
            var notApplicableBindings = new List<ScopeSelectorNotApplicableBinding>();
            var ruleBindings = new List<ScopeRuleEvaluationBinding>();
            foreach (var input in inputs)
            {
                var objectDigest = ScopeManifestCanonicalWriter.ComputeObjectRefSha256(input.ObjectRef);
                for (var selectorOrdinal = 0; selectorOrdinal < input.Selectors.Count; selectorOrdinal++)
                {
                    var selector = input.Selectors[selectorOrdinal];
                    if (selector.EvidenceArtifactOrdinal is not { } evidenceOrdinal ||
                        selector.EvidenceKind is null)
                    {
                        if (selector.RuleOrdinal is { } ruleOrdinal)
                        {
                            var rule = profile.OrderedRules[ruleOrdinal];
                            notApplicableBindings.Add(new ScopeSelectorNotApplicableBinding(
                                objectDigest,
                                selectorOrdinal,
                                profile.OrderedMembers[
                                    profile.OrderedSelectorMemberOrdinals[selectorOrdinal]],
                                profile.SourceProfileRef,
                                profile.SelectorTableRef,
                                ruleOrdinal,
                                profile.OrderedMembers[rule.RuleMemberOrdinal]));
                        }

                        continue;
                    }

                    bindings.Add(new ScopeSelectorObservationBinding(
                        selector.EvidenceKind.Value,
                        objectDigest,
                        selectorOrdinal,
                        profile.OrderedMembers[
                            profile.OrderedSelectorMemberOrdinals[selectorOrdinal]],
                        profile.SourceProfileRef,
                        profile.SelectorTableRef,
                        evidence[evidenceOrdinal],
                        ScopeManifestCanonicalWriter.ComputeSelectorEvidenceSha256(
                            profile,
                            evidence,
                            selectorOrdinal,
                            selector)));
                }

                var selectorSetSha256 = ScopeManifestCanonicalWriter.ComputeSelectorSetSha256(
                    profile,
                    evidence,
                    input.Selectors);
                foreach (var evaluation in input.RuleEvaluations)
                {
                    var rule = profile.OrderedRules[evaluation.RuleOrdinal];
                    ruleBindings.Add(new ScopeRuleEvaluationBinding(
                        objectDigest,
                        selectorSetSha256,
                        evaluation.RuleOrdinal,
                        profile.OrderedMembers[rule.RuleMemberOrdinal],
                        profile.SourceProfileRef,
                        profile.SelectorTableRef,
                        ScopeManifestCanonicalWriter.ComputeRuleEvaluationSha256(profile, evaluation)));
                }
            }

            var observed = inputs
                .Select(input => new ScopeObservedObjectEntry(
                    input.ObjectRef,
                    ScopeManifestCanonicalWriter.ComputeObjectRefSha256(input.ObjectRef)))
                .OrderBy(static entry => entry, ScopeObservedObjectComparer.Instance)
                .ToArray();
            var enumerationBinding = new ScopeCompleteEnumerationBinding(
                Artifact("f0f0f0f0-0000-4000-8000-0000000000ee"),
                profile.SourceProfileRef,
                profile.SelectorTableRef,
                observed.Length,
                ScopeManifestCanonicalWriter.ComputeObservedObjectSequenceSha256(observed));
            return new ExactResolver(bindings, notApplicableBindings, ruleBindings, enumerationBinding);
        }

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) =>
            _bindings.Contains(binding);

        public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding) =>
            _notApplicableBindings.Contains(binding);

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) =>
            _ruleBindings.Contains(binding);

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) =>
            _completeEnumerationBinding == binding;
    }
}
