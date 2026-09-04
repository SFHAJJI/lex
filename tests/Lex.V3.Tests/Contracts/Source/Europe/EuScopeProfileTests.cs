using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.TestSupport;
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

    private const string FakeCandidateDigestA =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string FakeCandidateDigestB =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [TestMethod]
    public void TheBindingCoversAllFourAxesOnceEachAndCarriesEuOwnIdentityNotLuxembourgs()
    {
        var binding = EuScopeProfile.BuildBinding();

        CollectionAssert.AreEquivalent(
            Enum.GetValues<ScopeAxis>(),
            binding.OrderedRules.Select(static rule => rule.Axis).ToArray());
        Assert.HasCount(4, binding.OrderedRules);
        Assert.HasCount(7, binding.OrderedSelectorMemberOrdinals);

        // Twelve exactly: seven selector keys plus four projection-rule keys plus the one
        // body-candidate role key. Not a loose lower bound: the whole member table is pinned
        // exactly, by key, registry ref and ordinal, in MemberTableIsPinnedExactlyByKeyRefAndOrdinal
        // below.
        Assert.HasCount(12, binding.OrderedMembers);

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

        // The Union's own two identities, pinned literally rather than only proven distinct from
        // Luxembourg's: distinctness alone would still pass if these constants were accidentally
        // swapped with each other, or edited to some other UUID nobody reviewed.
        Assert.AreEqual(
            "urn:uuid:49fe8a39-4d46-4c94-b82c-12e6c8a639ef",
            binding.SourceProfileRef.ResourceId);
        Assert.AreEqual(
            "urn:uuid:57e32290-68a8-4a34-b7a8-226886bc11a2",
            binding.SelectorTableRef.ResourceId);
        foreach (var member in binding.OrderedMembers)
        {
            StringAssert.StartsWith(member.MemberKey, "eu_");
        }
    }

    [TestMethod]
    public void ScopeDispositionsNumberingMatchesTheWorstWinsPrecedenceThisFileDependsOn()
    {
        // Worst() in EuScopeProfile.cs is a plain "left > right" comparison over ScopeDisposition's
        // declared enum values, not a table this file controls: ScopeManifest.cs owns that numbering,
        // outside this file's path claim, and a future renumbering there would silently change which
        // of two contributions "wins" a body-join tie without touching a single line here. Read back
        // through reflection (Enum.GetNames/GetValues) rather than a direct literal-to-cast
        // comparison, so the compiler cannot fold this into a tautology the way
        // "Assert.AreEqual(1, (int)ScopeDisposition.AcceptedSelected)" would.
        var pairs = Enum.GetNames<ScopeDisposition>()
            .Zip(
                Enum.GetValues<ScopeDisposition>().Cast<int>(),
                static (name, value) => $"{name}={value}")
            .ToArray();
        CollectionAssert.AreEqual(
            new[] { "AcceptedSelected=1", "TypedQuarantine=2", "Point=3", "NeverIngest=4" },
            pairs);
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

    // --- Objection 1: real digests over the closed vocabulary, folding in the accepted Candidate 4
    // digest, never a literal placeholder. ---------------------------------------------------------

    [TestMethod]
    public void ProfileAndSelectorTableDigestsArePinnedLiterally()
    {
        var binding = EuScopeProfile.BuildBinding();

        // Print-actual-then-transcribe: these are the exact SHA-256 values BuildBinding() computes
        // today over the closed EU vocabularies and the selector/projection table. A change to
        // either input is expected to change these literals, which is exactly what the sensitivity
        // tests below drive on purpose.
        Assert.AreEqual(
            "0438e3d2ec9d99c0b1190c20b1b93d500508a1a3e4bb91c068a95f8d6fee0e0d",
            binding.SourceProfileRef.Sha256);
        Assert.AreEqual(
            "b4fb1a17408b4fab4b6ab5080f34b847813460230327f4770f3a3febaf4e41a5",
            binding.SelectorTableRef.Sha256);
    }

    [TestMethod]
    public void ProfileDigestChangesWhenTheFoldedInCandidateDigestChanges()
    {
        var withA = EuScopeProfile.ComputeProfileSha256(
            EuScopeVocabulary.ActForms,
            EuScopeVocabulary.Channels,
            EuScopeVocabulary.OfficialLanguages,
            Enum.GetValues<EuManifestationFormat>(),
            Enum.GetValues<EuContentClass>(),
            EuScopeVocabulary.RelationFamilies,
            FakeCandidateDigestA);
        var withB = EuScopeProfile.ComputeProfileSha256(
            EuScopeVocabulary.ActForms,
            EuScopeVocabulary.Channels,
            EuScopeVocabulary.OfficialLanguages,
            Enum.GetValues<EuManifestationFormat>(),
            Enum.GetValues<EuContentClass>(),
            EuScopeVocabulary.RelationFamilies,
            FakeCandidateDigestB);

        Assert.AreNotEqual(withA, withB);
        Assert.AreNotEqual(EuScopeProfile.BuildBinding().SourceProfileRef.Sha256, withA);
        Assert.AreNotEqual(EuScopeProfile.BuildBinding().SourceProfileRef.Sha256, withB);
    }

    [TestMethod]
    public void ProfileDigestChangesWhenAnyClosedVocabularyShrinks()
    {
        var full = EuScopeProfile.ComputeProfileSha256(
            EuScopeVocabulary.ActForms,
            EuScopeVocabulary.Channels,
            EuScopeVocabulary.OfficialLanguages,
            Enum.GetValues<EuManifestationFormat>(),
            Enum.GetValues<EuContentClass>(),
            EuScopeVocabulary.RelationFamilies,
            FakeCandidateDigestA);

        var missingActForm = EuScopeProfile.ComputeProfileSha256(
            EuScopeVocabulary.ActForms.Take(EuScopeVocabulary.ActForms.Count - 1).ToArray(),
            EuScopeVocabulary.Channels,
            EuScopeVocabulary.OfficialLanguages,
            Enum.GetValues<EuManifestationFormat>(),
            Enum.GetValues<EuContentClass>(),
            EuScopeVocabulary.RelationFamilies,
            FakeCandidateDigestA);
        var missingLanguage = EuScopeProfile.ComputeProfileSha256(
            EuScopeVocabulary.ActForms,
            EuScopeVocabulary.Channels,
            EuScopeVocabulary.OfficialLanguages
                .Take(EuScopeVocabulary.OfficialLanguages.Count - 1).ToArray(),
            Enum.GetValues<EuManifestationFormat>(),
            Enum.GetValues<EuContentClass>(),
            EuScopeVocabulary.RelationFamilies,
            FakeCandidateDigestA);
        var missingRelationFamily = EuScopeProfile.ComputeProfileSha256(
            EuScopeVocabulary.ActForms,
            EuScopeVocabulary.Channels,
            EuScopeVocabulary.OfficialLanguages,
            Enum.GetValues<EuManifestationFormat>(),
            Enum.GetValues<EuContentClass>(),
            EuScopeVocabulary.RelationFamilies
                .Take(EuScopeVocabulary.RelationFamilies.Count - 1).ToArray(),
            FakeCandidateDigestA);

        Assert.AreNotEqual(full, missingActForm);
        Assert.AreNotEqual(full, missingLanguage);
        Assert.AreNotEqual(full, missingRelationFamily);
    }

    [TestMethod]
    public void SelectorTableDigestChangesWhenASelectorKeyOrProjectionRuleChanges()
    {
        var baseKeys = new[] { "eu_selector.a", "eu_selector.b" };
        var mutatedKeys = new[] { "eu_selector.a", "eu_selector.c" };
        var rules = new[] { (ScopeAxis.Record, "eu_projection.record") };
        var mutatedRules = new[] { (ScopeAxis.Body, "eu_projection.record") };

        var baseline = EuScopeProfile.ComputeSelectorTableSha256(baseKeys, rules);
        var withDifferentKey = EuScopeProfile.ComputeSelectorTableSha256(mutatedKeys, rules);
        var withDifferentAxis = EuScopeProfile.ComputeSelectorTableSha256(baseKeys, mutatedRules);

        Assert.AreNotEqual(baseline, withDifferentKey);
        Assert.AreNotEqual(baseline, withDifferentAxis);
    }

    // --- Fold-in: pin the twelve member keys with registry refs and ordinals as literals, and
    // assert the token strings each closed-vocabulary switch produces. -----------------------------

    [TestMethod]
    public void MemberTableIsPinnedExactlyByKeyRegistryOwnerAndOrdinal()
    {
        var binding = EuScopeProfile.BuildBinding();

        // Print-actual-then-transcribe. ScopeProfileBinding's own constructor requires this exact
        // canonical sort (registry-ref resource id, then sha256, then member key) and throws on
        // anything else, so this order is not BuildBinding()'s free choice; which UUID sorts first
        // is, and that fact is what
        // ProfileResourceSortsBeforeSelectorTableResourceUnderTheSharedCanonicalOrder pins below.
        CollectionAssert.AreEqual(
            new[]
            {
                "0|eu_role.body_candidate|profile",
                "1|eu_projection.body|table",
                "2|eu_projection.record|table",
                "3|eu_projection.relation|table",
                "4|eu_projection.supporting_document|table",
                "5|eu_selector.channel|table",
                "6|eu_selector.format|table",
                "7|eu_selector.language_body|table",
                "8|eu_selector.record_form|table",
                "9|eu_selector.relation_family|table",
                "10|eu_selector.rights|table",
                "11|eu_selector.supporting_content_class|table",
            },
            binding.OrderedMembers
                .Select((member, ordinal) => $"{ordinal}|{member.MemberKey}|" +
                    (member.RegistryRef == binding.SourceProfileRef ? "profile" : "table"))
                .ToArray());
    }

    [TestMethod]
    public void ProfileResourceSortsBeforeSelectorTableResourceUnderTheSharedCanonicalOrder()
    {
        var binding = EuScopeProfile.BuildBinding();

        Assert.IsTrue(
            string.CompareOrdinal(
                binding.SourceProfileRef.ResourceId,
                binding.SelectorTableRef.ResourceId) < 0,
            "The profile resource id must sort before the selector-table resource id under the " +
            "shared scope/1 canonical member order; this is pinned so a future UUID change cannot " +
            "silently reorder the member table without a failing test.");
        Assert.AreEqual(binding.SourceProfileRef, binding.OrderedMembers[0].RegistryRef);
        foreach (var member in binding.OrderedMembers.Skip(1))
        {
            Assert.AreEqual(binding.SelectorTableRef, member.RegistryRef);
        }
    }

    [TestMethod]
    public void TokenSwitchesProduceTheExpectedWireTokenForEveryClosedVocabularyMember()
    {
        var profile = EuScopeProfile.BuildBinding();

        // Every EuActForm, driven through the record selector via the record axis.
        var actFormTokens = new Dictionary<EuActForm, string>
        {
            [EuActForm.Directive] = "DIR",
            [EuActForm.Regulation] = "REG",
            [EuActForm.DelegatedRegulation] = "REG_DEL",
            [EuActForm.ImplementingRegulation] = "REG_IMPL",
            [EuActForm.Treaty] = "TREATY",
            [EuActForm.Corrigendum] = "CORRIGENDUM",
            [EuActForm.DelegatedDirective] = "DIR_DEL",
            [EuActForm.ImplementingDecision] = "DEC_IMPL",
            [EuActForm.Decision] = "DEC",
            [EuActForm.DecisionEntscheid] = "DEC_ENTSCHEID",
            [EuActForm.ImplementingDirective] = "DIR_IMPL",
            [EuActForm.DelegatedDecision] = "DEC_DEL",
        };
        CollectionAssert.AreEquivalent(EuScopeVocabulary.ActForms.ToArray(), actFormTokens.Keys.ToArray());
        foreach (var (form, expectedToken) in actFormTokens)
        {
            var dispositions = Baseline(profile, "form-" + form, Channel(EuChannel.CellarSparqlEndpoint),
                null, null, null, recordForm: form);
            var verified = ReduceOne(profile, dispositions);
            var recordSelector = verified.Manifest.Rows.Single().Selectors[0];
            CollectionAssert.AreEqual(new[] { expectedToken }, recordSelector.CanonicalValues.ToArray());
        }
    }

    [TestMethod]
    public void EveryChannelLanguageFormatContentClassAndRelationFamilyProducesItsExactToken()
    {
        var profile = EuScopeProfile.BuildBinding();

        foreach (var channel in EuScopeVocabulary.Channels)
        {
            var expected = channel switch
            {
                EuChannel.CellarSparqlEndpoint => "cellar_sparql_endpoint",
                EuChannel.PublicationsRestResource => "publications_rest_resource",
                EuChannel.EurLexPortal => "eurlex_portal",
                _ => throw new ArgumentOutOfRangeException(nameof(channel)),
            };
            var dispositions = Baseline(profile, "chan-" + channel, Channel(channel), null, null, null);
            var verified = ReduceOne(profile, dispositions);
            var selector = verified.Manifest.Rows.Single().Selectors[1];
            CollectionAssert.AreEqual(new[] { expected }, selector.CanonicalValues.ToArray());
        }

        foreach (var language in EuScopeVocabulary.OfficialLanguages)
        {
            var state = EuLanguageBodyDisposition.BodyCandidateLanguages.Contains(language)
                ? EuLanguageBodyState.BodyCandidate
                : EuLanguageBodyState.BodyNotHeldPoint;
            var expected = language switch
            {
                EuOfficialLanguage.Bulgarian => "BUL",
                EuOfficialLanguage.Czech => "CES",
                EuOfficialLanguage.Danish => "DAN",
                EuOfficialLanguage.German => "DEU",
                EuOfficialLanguage.Greek => "ELL",
                EuOfficialLanguage.English => "ENG",
                EuOfficialLanguage.Estonian => "EST",
                EuOfficialLanguage.Finnish => "FIN",
                EuOfficialLanguage.French => "FRA",
                EuOfficialLanguage.Irish => "GLE",
                EuOfficialLanguage.Croatian => "HRV",
                EuOfficialLanguage.Hungarian => "HUN",
                EuOfficialLanguage.Italian => "ITA",
                EuOfficialLanguage.Latvian => "LAV",
                EuOfficialLanguage.Lithuanian => "LIT",
                EuOfficialLanguage.Maltese => "MLT",
                EuOfficialLanguage.Dutch => "NLD",
                EuOfficialLanguage.Polish => "POL",
                EuOfficialLanguage.Portuguese => "POR",
                EuOfficialLanguage.Romanian => "RON",
                EuOfficialLanguage.Slovak => "SLK",
                EuOfficialLanguage.Slovenian => "SLV",
                EuOfficialLanguage.Spanish => "SPA",
                EuOfficialLanguage.Swedish => "SWE",
                _ => throw new ArgumentOutOfRangeException(nameof(language)),
            };
            var dispositions = Baseline(
                profile, "lang-" + language, Channel(EuChannel.CellarSparqlEndpoint),
                Language(language, state), null, null);
            var verified = ReduceOne(profile, dispositions);
            var selector = verified.Manifest.Rows.Single().Selectors[2];
            CollectionAssert.AreEqual(new[] { expected }, selector.CanonicalValues.ToArray());
        }

        foreach (var format in Enum.GetValues<EuManifestationFormat>())
        {
            var admission = format == EuManifestationFormat.Print
                ? EuFormatBodyAdmission.BodyNotAdmitted
                : EuFormatBodyAdmission.BodyAdmitted;
            var expected = format switch
            {
                EuManifestationFormat.Formex4 => "fmx4",
                EuManifestationFormat.Xhtml => "xhtml",
                EuManifestationFormat.Xhtml5 => "xhtml5",
                EuManifestationFormat.Html => "html",
                EuManifestationFormat.Pdf => "pdf",
                EuManifestationFormat.PdfA1a => "pdfa1a",
                EuManifestationFormat.PdfA1b => "pdfa1b",
                EuManifestationFormat.PdfA2a => "pdfa2a",
                EuManifestationFormat.Print => "print",
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            };
            var dispositions = Baseline(
                profile, "fmt-" + format, Channel(EuChannel.CellarSparqlEndpoint),
                null, Format(format, admission), null);
            var verified = ReduceOne(profile, dispositions);
            var selector = verified.Manifest.Rows.Single().Selectors[3];
            CollectionAssert.AreEqual(new[] { expected }, selector.CanonicalValues.ToArray());
        }

        foreach (var contentClass in Enum.GetValues<EuContentClass>())
        {
            var expected = contentClass switch
            {
                EuContentClass.Metadata => "metadata",
                EuContentClass.Consolidation => "consolidation",
                EuContentClass.Summary => "summary",
                EuContentClass.OriginalLegalText => "original_legal_text",
                EuContentClass.EditorialContent => "editorial_content",
                _ => throw new ArgumentOutOfRangeException(nameof(contentClass)),
            };
            var dispositions = Baseline(
                profile, "rights-" + contentClass, Channel(EuChannel.CellarSparqlEndpoint),
                null, null, Rights(contentClass));
            var verified = ReduceOne(profile, dispositions);
            var selector = verified.Manifest.Rows.Single().Selectors[4];
            CollectionAssert.AreEqual(new[] { expected }, selector.CanonicalValues.ToArray());
        }

        foreach (var family in EuScopeVocabulary.RelationFamilies)
        {
            var expected = family switch
            {
                EuRelationFamily.Amends => "resource_legal_amends_resource_legal",
                EuRelationFamily.AmendedBy => "resource_legal_amended_by_resource_legal",
                EuRelationFamily.Corrects => "resource_legal_corrects_resource_legal",
                EuRelationFamily.BasedOn => "resource_legal_based_on_resource_legal",
                EuRelationFamily.Repeals => "resource_legal_repeals_resource_legal",
                EuRelationFamily.ImplicitlyRepeals =>
                    "resource_legal_implicitly_repeals_resource_legal",
                EuRelationFamily.ProposesToAmend => "resource_legal_proposes_to_amend_resource_legal",
                EuRelationFamily.ConsolidatedBasedOn => "act_consolidated_based_on_resource_legal",
                EuRelationFamily.ConsolidatedConsolidates =>
                    "act_consolidated_consolidates_resource_legal",
                EuRelationFamily.CaseLawInterpretes => "case-law_interpretes_resource_legal",
                EuRelationFamily.CaseLawDeclaresVoid =>
                    "case-law_declares_void_by_preliminary_ruling_resource_legal",
                EuRelationFamily.SubmitsPreliminaryQuestion =>
                    "communication_case_new_submits_preliminary_question_resource_legal",
                EuRelationFamily.RequestsAnnulment =>
                    "communication_case_new_requests_annulment_of_resource_legal",
                _ => throw new ArgumentOutOfRangeException(nameof(family)),
            };
            var dispositions = new EuScopeObjectDispositions(
                ObjectRef("rel-" + family),
                EuActForm.Regulation,
                Artifact("f1f1f1f1-0000-4000-8000-000000000001"),
                Channel(EuChannel.CellarSparqlEndpoint),
                null,
                null,
                null,
                [Relation(family, EuRelationAcquisitionState.Complete)],
                Artifact("f1f1f1f1-0000-4000-8000-000000000002"),
                null,
                Artifact("f1f1f1f1-0000-4000-8000-000000000003"));
            var verified = ReduceOne(profile, dispositions);
            var selector = verified.Manifest.Rows.Single().Selectors[5];
            CollectionAssert.AreEqual(new[] { expected }, selector.CanonicalValues.ToArray());
        }
    }

    // --- Objection 2: the record axis is a rule over the closed act-form vocabulary, never a
    // constant. -------------------------------------------------------------------------------------

    [TestMethod]
    public void RecordAxisAcceptsEveryMemberOfTheClosedActFormVocabulary()
    {
        var profile = EuScopeProfile.BuildBinding();

        foreach (var form in EuScopeVocabulary.ActForms)
        {
            var dispositions = Baseline(
                profile, "record-" + form, Channel(EuChannel.CellarSparqlEndpoint),
                null, null, null, recordForm: form);
            var verified = ReduceOne(profile, dispositions);
            var reduction = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Record]);
            var record = reduction.AllAxisResults.Single(r => r.Axis == ScopeAxis.Record);
            Assert.AreEqual(ScopeDisposition.AcceptedSelected, record.Disposition, $"form {form}");
        }
    }

    // --- Objection 3: the body axis is a worst-wins join, driven for every competing pair of
    // contributing dispositions that can disagree. THE CONFIRMED BUG is the first test below. -------

    [TestMethod]
    public void AnExcludedChannelDoesNotHideAPrintFormatsNeverIngestTheConfirmedBug()
    {
        // Before this file's refreeze, ReduceBody was an ordered early-return chain that checked
        // the channel first, so an excluded channel returned Point before a Print format's
        // NeverIngest could ever be reached: the stronger exclusion was silently hidden behind the
        // weaker one. This is that exact combination, and it must resolve to NeverIngest.
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "excluded-channel-print-format",
            channel: Channel(EuChannel.EurLexPortal),
            language: Language(EuOfficialLanguage.English, EuLanguageBodyState.BodyCandidate),
            format: Format(EuManifestationFormat.Print, EuFormatBodyAdmission.BodyNotAdmitted),
            rights: Rights(EuContentClass.OriginalLegalText));

        var verified = ReduceOne(profile, dispositions);
        var body = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Body])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.NeverIngest, body.Disposition);
        Assert.AreEqual(ScopeRuleEffect.ExactDenial, body.Effect);
    }

    [TestMethod]
    public void AnExcludedChannelStillCapsAtPointAgainstAMerelyQuarantinedMissingFormat()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "excluded-channel-missing-format",
            channel: Channel(EuChannel.EurLexPortal),
            language: Language(EuOfficialLanguage.English, EuLanguageBodyState.BodyCandidate),
            format: null,
            rights: Rights(EuContentClass.OriginalLegalText));

        var verified = ReduceOne(profile, dispositions);
        var body = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Body])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.Point, body.Disposition);
    }

    [TestMethod]
    public void AnExcludedChannelStillCapsAtPointAgainstMissingRights()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "excluded-channel-missing-rights",
            channel: Channel(EuChannel.EurLexPortal),
            language: Language(EuOfficialLanguage.English, EuLanguageBodyState.BodyCandidate),
            format: Format(EuManifestationFormat.Formex4, EuFormatBodyAdmission.BodyAdmitted),
            rights: null);

        var verified = ReduceOne(profile, dispositions);
        var body = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Body])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.Point, body.Disposition);
    }

    [TestMethod]
    public void ALanguageWhoseBodyIsNotHeldDoesNotHideAPrintFormatsNeverIngest()
    {
        // Same defect shape as the confirmed bug, via language instead of channel: the old chain
        // checked the language before the format too.
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "body-not-held-print-format",
            channel: Channel(EuChannel.CellarSparqlEndpoint),
            language: Language(EuOfficialLanguage.German, EuLanguageBodyState.BodyNotHeldPoint),
            format: Format(EuManifestationFormat.Print, EuFormatBodyAdmission.BodyNotAdmitted),
            rights: Rights(EuContentClass.OriginalLegalText));

        var verified = ReduceOne(profile, dispositions);
        var body = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Body])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.NeverIngest, body.Disposition);
        Assert.AreEqual(ScopeRuleEffect.ExactDenial, body.Effect);
    }

    [TestMethod]
    public void ALanguageWhoseBodyIsNotHeldStillCapsAtPointAgainstAMerelyQuarantinedMissingFormat()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "body-not-held-missing-format",
            channel: Channel(EuChannel.CellarSparqlEndpoint),
            language: Language(EuOfficialLanguage.German, EuLanguageBodyState.BodyNotHeldPoint),
            format: null,
            rights: Rights(EuContentClass.OriginalLegalText));

        var verified = ReduceOne(profile, dispositions);
        var body = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Body])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.Point, body.Disposition);
    }

    [TestMethod]
    public void ALanguageWhoseBodyIsNotHeldStillCapsAtPointAgainstMissingRights()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "body-not-held-missing-rights",
            channel: Channel(EuChannel.CellarSparqlEndpoint),
            language: Language(EuOfficialLanguage.German, EuLanguageBodyState.BodyNotHeldPoint),
            format: Format(EuManifestationFormat.Formex4, EuFormatBodyAdmission.BodyAdmitted),
            rights: null);

        var verified = ReduceOne(profile, dispositions);
        var body = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Body])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.Point, body.Disposition);
    }

    [TestMethod]
    public void APrintFormatsNeverIngestBeatsMissingRightsTypedQuarantine()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "print-format-missing-rights",
            channel: Channel(EuChannel.CellarSparqlEndpoint),
            language: Language(EuOfficialLanguage.French, EuLanguageBodyState.BodyCandidate),
            format: Format(EuManifestationFormat.Print, EuFormatBodyAdmission.BodyNotAdmitted),
            rights: null);

        var verified = ReduceOne(profile, dispositions);
        var body = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Body])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.NeverIngest, body.Disposition);
        Assert.AreEqual(ScopeRuleEffect.ExactDenial, body.Effect);
    }

    // The "channel and language cannot disagree" claim this comment used to make stopped being
    // true once a missing LanguageDisposition was split out as its own TypedQuarantine contribution
    // (distinct from an observed expression whose body is not held, which stays Point): channel
    // contributes only AcceptedSelected or Point, but language can now also contribute
    // TypedQuarantine, so the two can disagree. The three tests below drive the previously undriven
    // competing pairs, including that exact one.

    [TestMethod]
    public void AnExcludedChannelBeatsAMissingLanguageExpressionsTypedQuarantine()
    {
        // The pair the stale comment above used to claim could never disagree: an excluded channel
        // contributes Point, a missing language expression contributes TypedQuarantine, and Point
        // is the higher-precedence (worse) of the two, so the join must pick Point.
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "excluded-channel-missing-expression",
            channel: Channel(EuChannel.EurLexPortal),
            language: null,
            format: Format(EuManifestationFormat.Formex4, EuFormatBodyAdmission.BodyAdmitted),
            rights: Rights(EuContentClass.OriginalLegalText));

        var verified = ReduceOne(profile, dispositions);
        var body = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Body])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.Point, body.Disposition);
    }

    [TestMethod]
    public void AMissingLanguageExpressionDoesNotHideAPrintFormatsNeverIngest()
    {
        // Same defect shape as the confirmed bug and its language/BodyNotHeldPoint sibling above,
        // now for a missing expression: TypedQuarantine must not hide a Print format's NeverIngest.
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "missing-expression-print-format",
            channel: Channel(EuChannel.CellarSparqlEndpoint),
            language: null,
            format: Format(EuManifestationFormat.Print, EuFormatBodyAdmission.BodyNotAdmitted),
            rights: Rights(EuContentClass.OriginalLegalText));

        var verified = ReduceOne(profile, dispositions);
        var body = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Body])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.NeverIngest, body.Disposition);
        Assert.AreEqual(ScopeRuleEffect.ExactDenial, body.Effect);
    }

    [TestMethod]
    public void AMissingFormatAloneQuarantinesAnOtherwiseAcceptedBody()
    {
        // The mirror of NoRightsDispositionYetLeavesAnOtherwiseReadyBodyQuarantined below: there,
        // a missing rights basis is the sole reason the join lands on TypedQuarantine; here, an
        // otherwise-accepted channel, language and rights leave a missing format as the one
        // contribution that actually decides the axis, rather than merely riding along behind a
        // channel exclusion the way AnExcludedChannelStillCapsAtPointAgainstAMerelyQuarantinedMissingFormat
        // exercises above.
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "missing-format-alone",
            channel: Channel(EuChannel.CellarSparqlEndpoint),
            language: Language(EuOfficialLanguage.English, EuLanguageBodyState.BodyCandidate),
            format: null,
            rights: Rights(EuContentClass.OriginalLegalText));

        var verified = ReduceOne(profile, dispositions);
        var body = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Body])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.TypedQuarantine, body.Disposition);
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

    // --- Newly folded-in objection: a missing language Expression
    // (ScopeSelectorState.PublisherValueAbsent per R1) must not collapse into the same Point outcome
    // as an observed Expression whose body this scope does not hold. -------------------------------

    [TestMethod]
    public void AMissingLanguageExpressionIsTypedQuarantineNotPointAtTheBodyAxis()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "missing-expression",
            channel: Channel(EuChannel.CellarSparqlEndpoint),
            language: null,
            format: Format(EuManifestationFormat.Formex4, EuFormatBodyAdmission.BodyAdmitted),
            rights: Rights(EuContentClass.OriginalLegalText));

        var verified = ReduceOne(profile, dispositions);
        var body = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Body])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Body);
        Assert.AreEqual(ScopeDisposition.TypedQuarantine, body.Disposition);
    }

    [TestMethod]
    public void AMissingExpressionIsDistinctFromAnObservedExpressionWhoseBodyIsNotHeld()
    {
        // The mutation this guards against: collapsing EuScopeObjectDispositions.LanguageDisposition
        // being null into the same branch as it being non-null-but-not-carrying-body. If that
        // collapse ever comes back, missing and notHeld below become equal and this test fails.
        var profile = EuScopeProfile.BuildBinding();
        var missingDispositions = Baseline(
            profile,
            "missing-vs-not-held-a",
            channel: Channel(EuChannel.CellarSparqlEndpoint),
            language: null,
            format: Format(EuManifestationFormat.Formex4, EuFormatBodyAdmission.BodyAdmitted),
            rights: Rights(EuContentClass.OriginalLegalText));
        var notHeldDispositions = Baseline(
            profile,
            "missing-vs-not-held-b",
            channel: Channel(EuChannel.CellarSparqlEndpoint),
            language: Language(EuOfficialLanguage.German, EuLanguageBodyState.BodyNotHeldPoint),
            format: Format(EuManifestationFormat.Formex4, EuFormatBodyAdmission.BodyAdmitted),
            rights: Rights(EuContentClass.OriginalLegalText));

        var missing = ScopeReducer.ReduceRequest(
                ReduceOne(profile, missingDispositions), missingDispositions.ObjectRef, [ScopeAxis.Body])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Body).Disposition;
        var notHeld = ScopeReducer.ReduceRequest(
                ReduceOne(profile, notHeldDispositions), notHeldDispositions.ObjectRef, [ScopeAxis.Body])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Body).Disposition;

        Assert.AreEqual(ScopeDisposition.TypedQuarantine, missing);
        Assert.AreEqual(ScopeDisposition.Point, notHeld);
        Assert.AreNotEqual(missing, notHeld);
    }

    [TestMethod]
    public void AMissingLanguageExpressionPublishesPublisherValueAbsentUnlikeAMissingFormatOrRights()
    {
        // THE WIRE-VOCABULARY BUG: before this fold-in, a missing language expression, a missing
        // format and a missing rights basis all published the identical
        // ScopeSelectorState.SelectorNotApplicable entry for ScopeAxis.Body, so R1's "no Expression
        // was observed at all" distinction lived only on which axis a reader happened to be looking
        // at, never on the selector's own state. Driving all three absences on one object proves the
        // language selector alone now differs.
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "all-three-absent",
            channel: Channel(EuChannel.CellarSparqlEndpoint),
            language: null,
            format: null,
            rights: null);

        var verified = ReduceOne(profile, dispositions);
        var selectors = verified.Manifest.Rows.Single().Selectors;
        var languageSelector = selectors[2];
        var formatSelector = selectors[3];
        var rightsSelector = selectors[4];

        Assert.AreEqual(ScopeSelectorState.PublisherValueAbsent, languageSelector.State);
        Assert.AreEqual(
            ScopeSelectorEvidenceKind.CompleteObservationAbsence,
            languageSelector.EvidenceKind);
        Assert.IsEmpty(languageSelector.CanonicalValues);
        Assert.IsNotNull(languageSelector.EvidenceArtifactOrdinal);
        Assert.IsNull(languageSelector.RuleOrdinal);

        Assert.AreEqual(ScopeSelectorState.SelectorNotApplicable, formatSelector.State);
        Assert.AreEqual(ScopeSelectorState.SelectorNotApplicable, rightsSelector.State);
        Assert.AreNotEqual(languageSelector.State, formatSelector.State);
        Assert.AreNotEqual(languageSelector.State, rightsSelector.State);
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
    public void AnUnacquiredRelationFamilyQuarantinesTheWholeRelationAxis()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = new EuScopeObjectDispositions(
            ObjectRef("unacquired-relation"),
            EuActForm.Directive,
            Artifact("11111111-1111-4111-8111-111111111112"),
            Channel(EuChannel.CellarSparqlEndpoint),
            null,
            null,
            null,
            [Relation(EuRelationFamily.BasedOn, EuRelationAcquisitionState.Unacquired)],
            Artifact("33333333-3333-4333-8333-333333333334"),
            null,
            Artifact("44444444-4444-4444-8444-444444444445"));

        var verified = ReduceOne(profile, dispositions);
        var relation = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Relation])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Relation);
        Assert.AreEqual(ScopeDisposition.TypedQuarantine, relation.Disposition);
    }

    [TestMethod]
    public void AnUncertainRelationFamilyQuarantinesTheWholeRelationAxis()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = new EuScopeObjectDispositions(
            ObjectRef("uncertain-relation"),
            EuActForm.Directive,
            Artifact("11111111-1111-4111-8111-111111111113"),
            Channel(EuChannel.CellarSparqlEndpoint),
            null,
            null,
            null,
            [Relation(EuRelationFamily.BasedOn, EuRelationAcquisitionState.Uncertain)],
            Artifact("33333333-3333-4333-8333-333333333335"),
            null,
            Artifact("44444444-4444-4444-8444-444444444446"));

        var verified = ReduceOne(profile, dispositions);
        var relation = ScopeReducer.ReduceRequest(verified, dispositions.ObjectRef, [ScopeAxis.Relation])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.Relation);
        Assert.AreEqual(ScopeDisposition.TypedQuarantine, relation.Disposition);
    }

    [TestMethod]
    public void NoRelationsAtAllIsPointRatherThanQuarantined()
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
    public void EditorialContentIsAlsoAnAcceptedSupportingDocument()
    {
        var profile = EuScopeProfile.BuildBinding();
        var editorial = new EuScopeObjectDispositions(
            ObjectRef("editorial"),
            EuActForm.Regulation,
            Artifact("21212121-1111-4111-8111-111111111111"),
            Channel(EuChannel.CellarSparqlEndpoint),
            null,
            null,
            null,
            [],
            Artifact("21212121-1111-4111-8111-111111111112"),
            EuContentClass.EditorialContent,
            Artifact("21212121-1111-4111-8111-111111111113"));

        var verified = ReduceOne(profile, editorial);
        var support = ScopeReducer.ReduceRequest(
            verified,
            editorial.ObjectRef,
            [ScopeAxis.SupportingDocument])
            .AllAxisResults.Single(r => r.Axis == ScopeAxis.SupportingDocument);
        Assert.AreEqual(ScopeDisposition.AcceptedSelected, support.Disposition);
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
            [
                Relation(EuRelationFamily.Amends, EuRelationAcquisitionState.Complete),
                Relation(EuRelationFamily.Amends, EuRelationAcquisitionState.Complete),
            ],
            Artifact("14141414-1414-4414-8414-141414141414"),
            null,
            Artifact("15151515-1515-4515-8515-151515151515")));
    }

    // --- Fold-in: each present selector's evidence ordinal must resolve to its own disposition's
    // distinct resource id, not merely to some ref the code happened to use. ReduceOne's ExactResolver
    // harness above builds its evidence table by asking BuildScopeInput which refs it used and
    // keeping only those, so a bug that swapped two selectors' evidence refs (say, the channel
    // selector citing the format disposition's own ref) would still resolve to *some* member of the
    // same used-ref table and none of the tests above would notice. This test fixes the full
    // candidate table up front, independently of what the code emits. --------------------------------

    [TestMethod]
    public void EachPresentSelectorsEvidenceOrdinalResolvesToItsOwnDispositionsDistinctResourceId()
    {
        var profile = EuScopeProfile.BuildBinding();
        var recordEvidence = Artifact("e0e0e0e0-0000-4000-8000-000000000001");
        var channelEvidence = Artifact("e0e0e0e0-0000-4000-8000-000000000002");
        var languageEvidence = Artifact("e0e0e0e0-0000-4000-8000-000000000003");
        var formatEvidence = Artifact("e0e0e0e0-0000-4000-8000-000000000004");
        var rightsEvidence = Artifact("e0e0e0e0-0000-4000-8000-000000000005");
        var relationEvidence = Artifact("e0e0e0e0-0000-4000-8000-000000000006");
        var supportingEvidence = Artifact("e0e0e0e0-0000-4000-8000-000000000007");
        var completionEvidence = Artifact("e0e0e0e0-0000-4000-8000-000000000008");

        var dispositions = new EuScopeObjectDispositions(
            ObjectRef("distinct-evidence"),
            EuActForm.Regulation,
            recordEvidence,
            new EuChannelDisposition(
                EuChannel.CellarSparqlEndpoint,
                EuChannelDisposition.PolicyFor(EuChannel.CellarSparqlEndpoint),
                "test_channel_reason",
                "test_channel_rule",
                channelEvidence),
            new EuLanguageBodyDisposition(
                EuOfficialLanguage.English,
                EuLanguageBodyState.BodyCandidate,
                "test_language_reason",
                "test_language_rule",
                languageEvidence),
            new EuFormatDisposition(
                EuManifestationFormat.Formex4,
                EuFormatBodyAdmission.BodyAdmitted,
                "test_format_reason",
                formatEvidence),
            new EuRightsDisposition(
                EuContentClass.OriginalLegalText,
                EuRightsDisposition.BasisFor(EuContentClass.OriginalLegalText),
                rightsEvidence),
            [
                new EuRelationFamilyDisposition(
                    EuRelationFamily.Amends,
                    EuRelationAuthority.PublisherAsserted,
                    EuRelationAcquisitionState.Complete,
                    completionEvidence,
                    null),
            ],
            relationEvidence,
            EuContentClass.Summary,
            supportingEvidence);

        // The full candidate table, fixed up front and independent of anything BuildScopeInput
        // decides to use -- the opposite of ReduceOne's adaptive probe-then-narrow harness above.
        var allRefs = new[]
        {
            recordEvidence, channelEvidence, languageEvidence, formatEvidence,
            rightsEvidence, relationEvidence, supportingEvidence, completionEvidence,
        }
        .OrderBy(static value => value.ResourceId, StringComparer.Ordinal)
        .ThenBy(static value => value.Sha256, StringComparer.Ordinal)
        .ToArray();
        Assert.HasCount(8, allRefs.Distinct().ToArray(), "the eight seed artifacts must be pairwise distinct.");
        var ordinals = allRefs
            .Select(static (value, ordinal) => (value, ordinal))
            .ToDictionary(static value => value.value, static value => value.ordinal);

        var input = EuScopeProfile.BuildScopeInput(profile, dispositions, ordinals);

        Assert.AreEqual(recordEvidence, allRefs[input.Selectors[0].EvidenceArtifactOrdinal!.Value]);
        Assert.AreEqual(channelEvidence, allRefs[input.Selectors[1].EvidenceArtifactOrdinal!.Value]);
        Assert.AreEqual(languageEvidence, allRefs[input.Selectors[2].EvidenceArtifactOrdinal!.Value]);
        Assert.AreEqual(formatEvidence, allRefs[input.Selectors[3].EvidenceArtifactOrdinal!.Value]);
        Assert.AreEqual(rightsEvidence, allRefs[input.Selectors[4].EvidenceArtifactOrdinal!.Value]);
        Assert.AreEqual(relationEvidence, allRefs[input.Selectors[5].EvidenceArtifactOrdinal!.Value]);
        Assert.AreEqual(supportingEvidence, allRefs[input.Selectors[6].EvidenceArtifactOrdinal!.Value]);

        // Not just each individually correct: pairwise distinct, so no two selectors that are
        // supposed to cite different dispositions' evidence could have been silently swapped for a
        // pair that happens to still look individually plausible.
        var resolvedRefs = input.Selectors
            .Select(selector => allRefs[selector.EvidenceArtifactOrdinal!.Value])
            .ToArray();
        Assert.HasCount(7, resolvedRefs.Distinct().ToArray());
    }

    // --- Fold-in: pin EuScopeObjectDispositions' own construction surface, and that EuScopeProfile's
    // two producer methods are exactly the recognised external producers they claim to be. -----------

    [TestMethod]
    public void EuScopeObjectDispositionsHasExactlyOneConstructionPath()
    {
        const string N = "Lex.V3.Contracts.Source.Europe.";
        const string R = "Lex.V3.Contracts.";
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor public instance " + N + "EuScopeObjectDispositions::.ctor("
                + "Lex.V3.Contracts.Source.Core.SourceObjectRef, "
                + R + "EuActForm, "
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, "
                + R + "EuChannelDisposition, "
                + R + "EuLanguageBodyDisposition, "
                + N + "EuFormatDisposition, "
                + N + "EuRightsDisposition, "
                + "System.Collections.Generic.IReadOnlyList<" + R + "EuRelationFamilyDisposition>, "
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, "
                + "System.Nullable<" + N + "EuContentClass>, "
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef) -> " + N
                + "EuScopeObjectDispositions",
            },
            ConstructionSurface.Of(typeof(EuScopeObjectDispositions)).ToArray());
    }

    [TestMethod]
    public void BuildBindingAndBuildScopeInputAreRecognisedProducersOfTheSharedScopeTypes()
    {
        // Exact set, not membership: a Contains check only proves EuScopeProfile's own two doors
        // exist and says nothing about every other door into these two shared types across the
        // whole Contracts assembly (Luxembourg's own binding and input producers, and the shared
        // ScopeManifest/ScopeManifestCanonicalWriter/OpenCanonicalScopePass holders and openers
        // among them). Print-actual-then-transcribe: this is the exact, sorted list
        // ConstructionSurface.ProducersIn(assembly, ..., includeNonPublic: true) returns today for
        // each guarded type, so a new, unreviewed door into either shared type -- in this file or
        // anywhere else in the assembly -- fails this test rather than passing silently because it
        // happened to also contain the one string a Contains check looked for.
        const string N = "Lex.V3.Contracts.Source.Europe.";
        const string Lu = "Lex.V3.Contracts.Source.Luxembourg.";
        const string Sc = "Lex.V3.Contracts.Source.Scope.";
        const string Co = "Lex.V3.Contracts.Source.Core.";
        var assembly = typeof(EuScopeProfile).Assembly;

        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + Lu + "VerifiedLuxembourgSourceProfile::<ScopeBinding>k__BackingField -> "
                    + Sc + "ScopeProfileBinding",
                "field private instance " + Sc + "ScopeManifest::<Profile>k__BackingField -> "
                    + Sc + "ScopeProfileBinding",
                "method private static " + Lu + "VerifiedLuxembourgSourceProfile::BuildScopeBinding("
                    + Co + "SourceArtifactRef, " + Co + "SourceArtifactRef) -> System.ValueTuple<"
                    + Sc + "ScopeProfileBinding, System.Collections.Generic.IReadOnlyDictionary<"
                    + "System.String, System.Int32>>",
                "method public static " + N + "EuScopeProfile::BuildBinding() -> " + Sc + "ScopeProfileBinding",
                "property public instance " + Lu + "VerifiedLuxembourgSourceProfile::ScopeBinding() -> "
                    + Sc + "ScopeProfileBinding",
                "property public instance " + Sc + "ScopeManifest::Profile() -> " + Sc + "ScopeProfileBinding",
            },
            ConstructionSurface.ProducersIn(assembly, typeof(ScopeProfileBinding), includeNonPublic: true)
                .ToArray(),
            "the exact set of producers of ScopeProfileBinding across Lex.V3.Contracts.");

        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + Lu + "LuxembourgProfileResolution+Resolved::<ScopeInputs>k__BackingField -> "
                    + "System.Collections.Generic.IReadOnlyList<" + Sc + "ScopeObjectReductionInput>",
                "method private static " + Lu + "LuxembourgScopeResolver::BuildScopeInput("
                    + Lu + "VerifiedLuxembourgSourceProfile, " + Lu + "LuxembourgResourceObservation, "
                    + "Lex.V3.Contracts.LuScopeDimensions, System.Collections.Generic.IReadOnlyList<"
                    + Lu + "LuxembourgResolvedRelation>, " + Lu + "LuxembourgWemiTopologyResolution, "
                    + Lu + "LuxembourgBodyJoinResolution, System.Collections.Generic.IReadOnlyDictionary<"
                    + Co + "SourceArtifactRef, System.Int32>) -> " + Sc + "ScopeObjectReductionInput",
                "method private static " + Sc + "ScopeManifestCanonicalWriter::OpenSnapshot("
                    + Sc + "OpenCanonicalScopePass, System.Threading.CancellationToken) -> "
                    + "System.Collections.Generic.IEnumerator<" + Sc + "ScopeObjectReductionInput>",
                "method public instance " + Sc + "OpenCanonicalScopePass::EndInvoke(System.IAsyncResult) -> "
                    + "System.Collections.Generic.IEnumerable<" + Sc + "ScopeObjectReductionInput>",
                "method public instance " + Sc + "OpenCanonicalScopePass::Invoke(System.Threading.CancellationToken) -> "
                    + "System.Collections.Generic.IEnumerable<" + Sc + "ScopeObjectReductionInput>",
                "method public static " + N + "EuScopeProfile::BuildScopeInput(" + Sc + "ScopeProfileBinding, "
                    + N + "EuScopeObjectDispositions, System.Collections.Generic.IReadOnlyDictionary<"
                    + Co + "SourceArtifactRef, System.Int32>) -> " + Sc + "ScopeObjectReductionInput",
                "property public instance " + Lu + "LuxembourgProfileResolution+Resolved::ScopeInputs() -> "
                    + "System.Collections.Generic.IReadOnlyList<" + Sc + "ScopeObjectReductionInput>",
            },
            ConstructionSurface.ProducersIn(assembly, typeof(ScopeObjectReductionInput), includeNonPublic: true)
                .ToArray(),
            "the exact set of producers of ScopeObjectReductionInput across Lex.V3.Contracts.");
    }

    // --- Fold-in: EuScopeSnapshotReduction.Reduce is an unpinned producer of
    // EuScopeObjectDispositions. There is no pre-existing ProducersIn pin over that type anywhere in
    // the tree to extend (item 5's own exact ProducersIn sets above cover ScopeProfileBinding and
    // ScopeObjectReductionInput, never EuScopeObjectDispositions), so this is a new pin, built here
    // because EuScopeObjectDispositions is declared in this file's own path claim. Print-actual-then-
    // transcribe: this is the exact, sorted list ConstructionSurface.ProducersIn returns today. ------

    [TestMethod]
    public void EuScopeSnapshotReductionReduceIsTheOnlyRecognisedExternalProducerOfEuScopeObjectDispositions()
    {
        const string N = "Lex.V3.Contracts.Source.Europe.";
        var assembly = typeof(EuScopeProfile).Assembly;

        CollectionAssert.AreEqual(
            new[]
            {
                "method public static " + N + "EuScopeSnapshotReduction::Reduce(" + N
                    + "EuCellarObjectSnapshot) -> " + N + "EuScopeObjectDispositions",
            },
            ConstructionSurface.ProducersIn(assembly, typeof(EuScopeObjectDispositions), includeNonPublic: true)
                .ToArray(),
            "the exact set of external producers of EuScopeObjectDispositions across Lex.V3.Contracts; " +
            "EuScopeObjectDispositions' own constructor is pinned separately by " +
            "EuScopeObjectDispositionsHasExactlyOneConstructionPath above.");
    }

    // --- Fold-in: execute TryOpenAsEuManifest once with a manifest produced by the real production
    // path -- EuScopeProfile's own binding and reduction, ScopeReducer, and the canonical writer --
    // rather than only against a hand-built ScopeProfileBinding the way EuScopeManifestBindingProofTests
    // isolates CheckProfileIdentity. ------------------------------------------------------------------

    [TestMethod]
    public void TryOpenAsEuManifestOpensARealManifestBuiltThroughTheFullProductionPath()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "end-to-end-real-manifest",
            channel: Channel(EuChannel.CellarSparqlEndpoint),
            language: Language(EuOfficialLanguage.English, EuLanguageBodyState.BodyCandidate),
            format: Format(EuManifestationFormat.Formex4, EuFormatBodyAdmission.BodyAdmitted),
            rights: Rights(EuContentClass.OriginalLegalText));

        var (verified, _, resolver) = ReduceOneForReopening(profile, dispositions);

        using var canonical = new MemoryStream();
        var manifestSha256 = ScopeManifestCanonicalWriter.Write(canonical, verified);
        var artifactRef = new SourceArtifactRef(
            "urn:uuid:6b1a5a2e-0000-4000-8000-0000000000ab", manifestSha256);

        var manifest = EuScopeManifestBindingProof.TryOpenAsEuManifest(
            artifactRef, canonical.ToArray(), resolver, out var refusal);

        Assert.IsNotNull(manifest);
        Assert.AreEqual(EuScopeManifestBindingProofRefusal.None, refusal);
        Assert.AreEqual(profile.SourceProfileRef, manifest!.Profile.SourceProfileRef);
        Assert.AreEqual(profile.SelectorTableRef, manifest.Profile.SelectorTableRef);
        Assert.AreEqual(dispositions.ObjectRef, manifest.ObservedObjects.Single().ObjectRef);
    }

    // --- Fold-in: the evidence gate can actually refuse -- proven with a fixed admitted set that is
    // independent of the input, rather than one self-derived from it. The ExactResolver below (used
    // by every happy-path test above) intentionally derives its admitted set from the same
    // computation under test, because those tests exist to exercise the shape of a correct pipeline
    // end to end; that self-derivation is exactly why none of them can ever demonstrate a refusal.
    // This test fixes the resolver's admitted set independently of the input (the fixed empty set)
    // to prove the gate is not a rubber stamp. -----------------------------------------------------

    [TestMethod]
    public void AnEvidenceResolverThatAdmitsNothingRefusesRatherThanReduces()
    {
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "refusal-probe",
            channel: Channel(EuChannel.CellarSparqlEndpoint),
            language: Language(EuOfficialLanguage.English, EuLanguageBodyState.BodyCandidate),
            format: Format(EuManifestationFormat.Formex4, EuFormatBodyAdmission.BodyAdmitted),
            rights: Rights(EuContentClass.OriginalLegalText));
        var evidenceRefs = new[]
        {
            dispositions.RecordEvidenceRef,
            dispositions.ChannelDisposition.EvidenceRef,
            dispositions.LanguageDisposition!.EvidenceRef,
            dispositions.FormatDisposition!.EvidenceRef,
            dispositions.RightsDisposition!.EvidenceRef,
            dispositions.RelationEvidenceRef,
            dispositions.SupportingEvidenceRef,
        }
        .Distinct()
        .OrderBy(static value => value.ResourceId, StringComparer.Ordinal)
        .ThenBy(static value => value.Sha256, StringComparer.Ordinal)
        .ToArray();
        var evidenceOrdinals = evidenceRefs
            .Select(static (value, ordinal) => (value, ordinal))
            .ToDictionary(static value => value.value, static value => value.ordinal);
        var input = EuScopeProfile.BuildScopeInput(profile, dispositions, evidenceOrdinals);

        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.Reduce(
            profile,
            evidenceRefs,
            [dispositions.ObjectRef],
            [input],
            new AllRefusedResolver()));
    }

    [TestMethod]
    public void AResolverWithAFixedNonEmptyButIncompleteAdmittedSetStillRefuses()
    {
        // Neither of the two resolvers above can show this shape: AllRefusedResolver admits
        // nothing at all, so it can never distinguish "the gate checks every binding" from "the
        // gate just checks whether anything was admitted"; ExactResolver self-derives a complete
        // admitted set from the exact reduction under test, so it can never demonstrate a refusal.
        // This resolver's admitted set is fixed, non-empty, and genuinely incomplete: it admits
        // exactly one real, correctly computed selector-observation binding (the record selector's)
        // and nothing else, independent of what the rest of the reduction needs. The gate must
        // still refuse, because the channel/language/format/rights/relation/supporting selectors,
        // every rule evaluation, and the complete-enumeration binding are all outside that fixed
        // set.
        var profile = EuScopeProfile.BuildBinding();
        var dispositions = Baseline(
            profile,
            "fixed-partial-admission",
            channel: Channel(EuChannel.CellarSparqlEndpoint),
            language: Language(EuOfficialLanguage.English, EuLanguageBodyState.BodyCandidate),
            format: Format(EuManifestationFormat.Formex4, EuFormatBodyAdmission.BodyAdmitted),
            rights: Rights(EuContentClass.OriginalLegalText));
        var evidenceRefs = new[]
        {
            dispositions.RecordEvidenceRef,
            dispositions.ChannelDisposition.EvidenceRef,
            dispositions.LanguageDisposition!.EvidenceRef,
            dispositions.FormatDisposition!.EvidenceRef,
            dispositions.RightsDisposition!.EvidenceRef,
            dispositions.RelationEvidenceRef,
            dispositions.SupportingEvidenceRef,
        }
        .Distinct()
        .OrderBy(static value => value.ResourceId, StringComparer.Ordinal)
        .ThenBy(static value => value.Sha256, StringComparer.Ordinal)
        .ToArray();
        var evidenceOrdinals = evidenceRefs
            .Select(static (value, ordinal) => (value, ordinal))
            .ToDictionary(static value => value.value, static value => value.ordinal);
        var input = EuScopeProfile.BuildScopeInput(profile, dispositions, evidenceOrdinals);

        const int recordSelectorOrdinal = 0;
        var recordSelector = input.Selectors[recordSelectorOrdinal];
        var admitted = new ScopeSelectorObservationBinding(
            recordSelector.EvidenceKind!.Value,
            ScopeManifestCanonicalWriter.ComputeObjectRefSha256(input.ObjectRef),
            recordSelectorOrdinal,
            profile.OrderedMembers[profile.OrderedSelectorMemberOrdinals[recordSelectorOrdinal]],
            profile.SourceProfileRef,
            profile.SelectorTableRef,
            evidenceRefs[recordSelector.EvidenceArtifactOrdinal!.Value],
            ScopeManifestCanonicalWriter.ComputeSelectorEvidenceSha256(
                profile, evidenceRefs, recordSelectorOrdinal, recordSelector));

        Assert.ThrowsExactly<InvalidOperationException>(() => ScopeReducer.Reduce(
            profile,
            evidenceRefs,
            [dispositions.ObjectRef],
            [input],
            new PartiallyAdmittedResolver(admitted)));
    }

    private static EuScopeObjectDispositions Baseline(
        ScopeProfileBinding profile,
        string key,
        EuChannelDisposition channel,
        EuLanguageBodyDisposition? language,
        EuFormatDisposition? format,
        EuRightsDisposition? rights,
        EuActForm recordForm = EuActForm.Regulation)
    {
        _ = profile;
        return new EuScopeObjectDispositions(
            ObjectRef(key),
            recordForm,
            Artifact("a1a1a1a1-0000-4000-8000-000000000001"),
            channel,
            language,
            format,
            rights,
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
        EuScopeObjectDispositions dispositions) =>
        ReduceOneForReopening(profile, dispositions).Verified;

    /// <summary>
    /// The same reduction <see cref="ReduceOne"/> performs, but also returns the evidence table and
    /// resolver it built along the way. <see cref="ReduceOne"/>'s callers only ever need the verified
    /// manifest; a caller that means to reopen the manifest's own canonical bytes afterwards (see
    /// <c>TryOpenAsEuManifestOpensARealManifestBuiltThroughTheFullProductionPath</c> below) needs the
    /// same resolver again, because <see cref="VerifiedScopeManifest.ParseAndVerify"/> re-verifies
    /// every binding against it.
    /// </summary>
    private static (
        VerifiedScopeManifest Verified,
        IReadOnlyList<SourceArtifactRef> UsedRefs,
        ExactResolver Resolver) ReduceOneForReopening(
        ScopeProfileBinding profile,
        EuScopeObjectDispositions dispositions)
    {
        // The evidence-artifact table must contain exactly the artifacts a selector actually
        // references (ScopeReducer.VerifyAndOpen enforces this), and which of the candidate refs
        // are used depends on which selectors came back "not applicable" rather than "present" -- a
        // caller decision this test does not want to duplicate. So build once against the full
        // candidate set (every disposition's own evidence ref, never a shared stand-in) to learn
        // which ordinals were actually used, then rebuild the table and the input against exactly
        // that subset.
        var candidateRefs = new[]
            {
                dispositions.RecordEvidenceRef,
                dispositions.ChannelDisposition.EvidenceRef,
                dispositions.LanguageDisposition?.EvidenceRef,
                dispositions.FormatDisposition?.EvidenceRef,
                dispositions.RightsDisposition?.EvidenceRef,
                dispositions.RelationEvidenceRef,
                dispositions.SupportingEvidenceRef,
            }
            .Where(static value => value is not null)
            .Select(static value => value!)
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
        var verified = ScopeReducer.Reduce(
            profile,
            usedRefs,
            [dispositions.ObjectRef],
            [input],
            resolver);
        return (verified, usedRefs, resolver);
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

    /// <summary>
    /// A fixed evidence resolver that admits nothing, independent of any input it is asked about.
    /// Used to prove the reduction gate can actually refuse, unlike <see cref="ExactResolver"/>
    /// below, which deliberately self-derives its admitted set from the same input under test so the
    /// happy-path tests above can exercise a correct pipeline end to end.
    /// </summary>
    private sealed class AllRefusedResolver : IScopeReductionEvidenceResolver
    {
        public SourceArtifactRef CompleteEnumerationRef { get; } =
            new("urn:uuid:f0f0f0f0-0000-4000-8000-0000000000ff", Digest);

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) => false;

        public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding) => false;

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) => false;

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) => false;
    }

    /// <summary>
    /// A resolver whose admitted set is a fixed, non-empty, deliberately incomplete literal: exactly
    /// one real selector-observation binding, computed once by the caller and never re-derived from
    /// whatever a reduction under test happens to need. Distinct from <see cref="AllRefusedResolver"/>
    /// (admits nothing) and <see cref="ExactResolver"/> (self-derives a complete admitted set from
    /// the same computation under test), so it can show what neither of those can: that the gate
    /// refuses a reduction whose bindings are only partially covered, not merely one with zero
    /// coverage.
    /// </summary>
    private sealed class PartiallyAdmittedResolver : IScopeReductionEvidenceResolver
    {
        private readonly ScopeSelectorObservationBinding _admitted;

        public PartiallyAdmittedResolver(ScopeSelectorObservationBinding admitted)
        {
            _admitted = admitted;
        }

        public SourceArtifactRef CompleteEnumerationRef { get; } =
            new("urn:uuid:f0f0f0f0-0000-4000-8000-0000000000fe", Digest);

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) =>
            binding == _admitted;

        public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding) => false;

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) => false;

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) => false;
    }

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
