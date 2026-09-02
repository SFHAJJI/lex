using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

/// <summary>
/// The Union scope vocabularies, measured rather than transcribed.
///
/// Every cardinality below is a literal taken from the measurement recorded on issue #331, not
/// computed from the enum under test. Asserting <c>Enum.GetValues().Length ==
/// EuScopeVocabulary.X.Count</c> would pass for any pair of equal wrong numbers, which is the
/// self-consistent fixture this repository keeps finding.
///
/// Token resolution is exercised through <see cref="ContractJson"/> rather than through a helper
/// of this slice's own, because that is the path the contracts actually serialise on. A private
/// lookup tested against itself would prove that two of my own functions agree.
/// </summary>
[TestClass]
public sealed class EuScopeDimensionsTests
{
    [TestMethod]
    public void EveryClosedSetInThisFileHasItsMeasuredCardinality()
    {
        Assert.AreEqual(2, EuScopeVocabulary.Hierarchies.Count);
        Assert.AreEqual(3, EuScopeVocabulary.BindingStatuses.Count);
        Assert.AreEqual(2, EuScopeVocabulary.ConsolidationStatuses.Count);
        Assert.AreEqual(4, EuScopeVocabulary.ExtractionProfiles.Count);
        Assert.AreEqual(12, EuScopeVocabulary.ActForms.Count);
        Assert.AreEqual(13, EuScopeVocabulary.RelationFamilies.Count);
        Assert.AreEqual(3, EuScopeVocabulary.Channels.Count);
        Assert.AreEqual(13, EuScopeVocabulary.CdmPredicates.Count);
        Assert.AreEqual(24, EuScopeVocabulary.OfficialLanguages.Count);
    }

    [TestMethod]
    public void EveryMemberOfEveryClosedSetSerialisesToItsExactPublisherToken()
    {
        // Every member, not a representative sample, and every token written out by hand rather
        // than derived. A sample leaves the unpinned members free to be renamed, and the
        // round-trip test below cannot catch that because it takes its expectation from the same
        // production enum it is checking: rename a member and its token together and the round
        // trip is perfect while the wire value other systems key on has silently changed.
        AssertTokens<EuHierarchy>("primary_eu_law", "secondary_eu_law");
        AssertTokens<EuBindingStatus>("in_force", "not_in_force", "unknown");
        AssertTokens<EuConsolidationStatus>("published", "original_official_expression");
        AssertTokens<EuExtractionProfile>(
            "fmx4-eu/1", "xhtml-eu/1", "xhtml-eu-xlink-context/1", "html-eu-tolerant/1");
        AssertTokens<EuActForm>(
            "DIR", "REG", "REG_DEL", "REG_IMPL", "TREATY", "CORRIGENDUM",
            "DIR_DEL", "DEC_IMPL", "DEC", "DEC_ENTSCHEID", "DIR_IMPL", "DEC_DEL");
        AssertTokens<EuRelationFamily>(
            "resource_legal_amends_resource_legal",
            "resource_legal_amended_by_resource_legal",
            "resource_legal_corrects_resource_legal",
            "resource_legal_based_on_resource_legal",
            "resource_legal_repeals_resource_legal",
            "resource_legal_implicitly_repeals_resource_legal",
            "resource_legal_proposes_to_amend_resource_legal",
            "act_consolidated_based_on_resource_legal",
            "act_consolidated_consolidates_resource_legal",
            "case-law_interpretes_resource_legal",
            "case-law_declares_void_by_preliminary_ruling_resource_legal",
            "communication_case_new_submits_preliminary_question_resource_legal",
            "communication_case_new_requests_annulment_of_resource_legal");
        AssertTokens<EuRelationAuthority>(
            "publisher_asserted", "ontology_authorized_inverse", "local_inbound_view");
        AssertTokens<EuRelationAcquisitionState>(
            "unacquired", "incomplete", "uncertain", "complete");
        AssertTokens<EuChannel>(
            "cellar_sparql_endpoint", "publications_rest_resource", "eurlex_portal");
        AssertTokens<EuChannelAdmission>("admitted", "excluded");
        AssertTokens<EuCdmPredicate>(
            "resource_legal_id_celex", "expression_belongs_to_work", "resource_legal_type",
            "work_has_resource-type", "work_date_document", "act_consolidated_date",
            "date_creation_legacy", "resource_legal_in-force", "expression_uses_language",
            "expression_title", "expression_title_short", "work_is_about_concept_eurovoc",
            "resource_legal_is_about_concept_directory-code");
        AssertTokens<EuOfficialLanguage>(
            "BUL", "CES", "DAN", "DEU", "ELL", "ENG", "EST", "FIN", "FRA", "GLE", "HRV", "HUN",
            "ITA", "LAV", "LIT", "MLT", "NLD", "POL", "POR", "RON", "SLK", "SLV", "SPA", "SWE");
    }

    [TestMethod]
    public void EveryTokenRoundTripsToItsOwnMember()
    {
        AssertRoundTrip<EuHierarchy>();
        AssertRoundTrip<EuBindingStatus>();
        AssertRoundTrip<EuConsolidationStatus>();
        AssertRoundTrip<EuExtractionProfile>();
        AssertRoundTrip<EuActForm>();
        AssertRoundTrip<EuRelationFamily>();
        AssertRoundTrip<EuChannel>();
        AssertRoundTrip<EuRelationAuthority>();
        AssertRoundTrip<EuRelationAcquisitionState>();
    }

    [TestMethod]
    public void UnknownVocabularyFailsClosedInEveryClosedSet()
    {
        // One hostile unknown per set. Each is a plausible neighbour of a real token rather than
        // obvious nonsense, because the failure worth preventing is a near miss.
        AssertScopeDrift<EuHierarchy>("tertiary_eu_law");
        AssertScopeDrift<EuBindingStatus>("in-force");
        AssertScopeDrift<EuConsolidationStatus>("consolidated");
        AssertScopeDrift<EuExtractionProfile>("xhtml-eu/2");
        AssertScopeDrift<EuActForm>("REG_ENTSCHEID");
        AssertScopeDrift<EuRelationFamily>("resource_legal_annuls_resource_legal");
        // Was "eurlex_portal" until that became a real token in this same repair, at which
        // point the vector stopped being hostile and the test said so.
        AssertScopeDrift<EuChannel>("eurlex_portal_mirror");
        AssertScopeDrift<EuRelationAuthority>("derived");
        AssertScopeDrift<EuRelationAcquisitionState>("partial");
        AssertScopeDrift<EuCdmPredicate>("resource_legal_id_celex_v2");
        AssertScopeDrift<EuOfficialLanguage>("NOR");
        AssertScopeDrift<EuChannelAdmission>("conditional");
    }

    [TestMethod]
    public void TokenMatchingIsCaseSensitive()
    {
        // The publisher's own tokens are case-sensitive. A case-insensitive match would accept a
        // token the publisher never minted, which is scope drift wearing a familiar name.
        AssertScopeDrift<EuActForm>("reg_impl");
        AssertScopeDrift<EuHierarchy>("SECONDARY_EU_LAW");
    }

    [TestMethod]
    public void ACompletedFamilyWithoutEvidenceCannotBeConstructed()
    {
        // The invalid state is unrepresentable rather than merely documented: a completed family
        // with nothing behind it is an absence claim with no proof.
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new EuRelationFamilyDisposition(
                EuRelationFamily.Repeals,
                EuRelationAuthority.PublisherAsserted,
                EuRelationAcquisitionState.Complete,
                completionEvidenceRef: null,
                ontologyAuthorityRef: null));
    }

    [TestMethod]
    public void EvidenceCannotBeAttachedToAnUnfinishedAcquisition()
    {
        // The mirror. Evidence on an incomplete family would let a reader treat it as closed.
        foreach (var state in new[]
                 {
                     EuRelationAcquisitionState.Unacquired,
                     EuRelationAcquisitionState.Incomplete,
                     EuRelationAcquisitionState.Uncertain,
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new EuRelationFamilyDisposition(
                    EuRelationFamily.Repeals,
                    EuRelationAuthority.PublisherAsserted,
                    state,
                    Evidence("22"),
                    ontologyAuthorityRef: null),
                $"{state} accepted completion evidence");
        }
    }

    [TestMethod]
    public void ALocallyComputedInboundViewIsNeverACompletedPublisherObservation()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new EuRelationFamilyDisposition(
                EuRelationFamily.AmendedBy,
                EuRelationAuthority.LocalInboundView,
                EuRelationAcquisitionState.Complete,
                Evidence("33"),
                ontologyAuthorityRef: null));
    }

    [TestMethod]
    public void AnInputWrongTwiceIsRefusedOnAuthorityRatherThanOnMissingEvidence()
    {
        // A local inbound view claiming completion, with no evidence, is wrong for two independent
        // reasons. The caller has misunderstood what the type means, and separately left a field
        // out. The first is the one worth reporting.
        //
        // This pins the guard order. With the evidence handling first, the authority guard is
        // unreachable on this input and the caller is told about a missing string instead, which is
        // a shadowed guard: every other test here supplies evidence, so every other test reaches
        // the guard and none of them would notice.
        //
        // ThrowsExactly, not Throws. ArgumentNullException derives from ArgumentException, so the
        // loose form passes under either ordering and proves nothing.
        var thrown = Assert.ThrowsExactly<ArgumentException>(
            () => new EuRelationFamilyDisposition(
                EuRelationFamily.AmendedBy,
                EuRelationAuthority.LocalInboundView,
                EuRelationAcquisitionState.Complete,
                completionEvidenceRef: null,
                ontologyAuthorityRef: null));
        StringAssert.Contains(thrown.Message, "locally computed inbound view");
    }

    [TestMethod]
    public void ADispositionRoundTripsAndRefusesAnUnknownFamily()
    {
        var original = new EuRelationFamilyDisposition(
            EuRelationFamily.CaseLawInterpretes,
            EuRelationAuthority.PublisherAsserted,
            EuRelationAcquisitionState.Unacquired,
            completionEvidenceRef: null,
            ontologyAuthorityRef: null);

        var json = ContractJson.Serialize(original);
        StringAssert.Contains(json, "case-law_interpretes_resource_legal");
        StringAssert.Contains(json, "publisher_asserted");
        StringAssert.Contains(json, "unacquired");
        // A computed property must not become a wire field somebody could set. Checked under the
        // serialiser's own snake_case policy, because the camelCase spelling would be absent from
        // this document whether or not the property leaked, and an assertion that cannot fail is
        // the thing this whole file exists to avoid.
        Assert.IsFalse(
            json.Contains("supports_absence_claim", StringComparison.Ordinal),
            "the computed absence-claim flag leaked onto the wire");
        // The smuggling test that used to sit here is gone with the property it guarded. It
        // asserted that a wire value of true could not decide the computed absence flag; there is
        // no such flag on this type any more, because this slice may not decide absence
        // eligibility at all. The guard now lives in the type's absence, and
        // ThisSliceCannotDecideAbsenceEligibility holds it there.

        var restored = ContractJson.Deserialize<EuRelationFamilyDisposition>(json);
        Assert.AreEqual(original, restored);

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<EuRelationFamilyDisposition>(
                """
                {"family":"resource_legal_annuls_resource_legal","authority":"publisher_asserted","acquisition":"unacquired","completion_evidence_ref":null,"ontology_authority_ref":null}
                """));
    }

    [TestMethod]
    public void ATypedInvariantSurvivesDeserialisation()
    {
        // The constructor guards must hold on the wire path too, or a document could carry a state
        // the type refuses to be constructed in.
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<EuRelationFamilyDisposition>(
                """
                {"family":"resource_legal_repeals_resource_legal","authority":"publisher_asserted","acquisition":"complete","completion_evidence_ref":null,"ontology_authority_ref":null}
                """));

        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<EuRelationFamilyDisposition>(
                """
                {"family":"resource_legal_amended_by_resource_legal","authority":"local_inbound_view","acquisition":"complete","completion_evidence_ref":null,"ontology_authority_ref":null}
                """));
    }

    [TestMethod]
    public void AChannelIdentityCarriesNoAdmissionAndAdmissionCarriesItsEvidence()
    {
        // The portal is named so it cannot be reached for later as though nobody had checked, but
        // its name no longer decides whether it may be used: an earlier version encoded exclusion
        // into the identity, so a consumer iterating the channel list had only a spelling to read
        // admission from.
        CollectionAssert.Contains(EuScopeVocabulary.Channels.ToArray(), EuChannel.EurLexPortal);

        var excluded = new EuChannelDisposition(
            EuChannel.EurLexPortal,
            EuChannelAdmission.Excluded,
            "waf_challenge_to_non_browser_clients",
            "eu_channel_admission_1",
            Evidence("44"));
        Assert.IsFalse(excluded.MayGraduate());

        var admitted = new EuChannelDisposition(
            EuChannel.CellarSparqlEndpoint,
            EuChannelAdmission.Admitted,
            "open_unauthenticated_robots_permitted",
            "eu_channel_admission_1",
            Evidence("55"));
        Assert.IsTrue(admitted.MayGraduate());

        // Both outcomes need their reason, rule and evidence. An admission without one is the
        // state a consumer relies on to fetch. Walked over the channels rather than over the two
        // admissions, because admission is no longer a caller's choice: each channel is paired
        // with its reviewed answer, which covers both outcomes and reaches all three routes
        // instead of the one the old loop named twice.
        foreach (var channel in EuScopeVocabulary.Channels)
        {
            var admission = EuChannelDisposition.PolicyFor(channel);
            Assert.ThrowsExactly<ArgumentException>(
                () => new EuChannelDisposition(
                    channel, admission, "  ", "rule", Evidence("77")));
            // ArgumentNullException, not ArgumentException: ThrowsExactly demands the exact type,
            // and a missing reference is a different refusal from a blank reason.
            Assert.ThrowsExactly<ArgumentNullException>(
                () => new EuChannelDisposition(
                    channel, admission, "reason", "rule", null!));
        }

        // The pairing itself is refused, so a row cannot claim an admission the reviewed policy
        // does not give that channel.
        Assert.ThrowsExactly<ArgumentException>(
            () => new EuChannelDisposition(
                EuChannel.EurLexPortal,
                EuChannelAdmission.Admitted,
                "reason",
                "rule",
                Evidence("77")));
    }

    [TestMethod]
    public void MetadataIsAcceptedInAllTwentyFourAndOnlyBodiesAreRestricted()
    {
        // The governing inventory is explicit: metadata for all 24, bodies ENG and FRA, and the
        // other 22 bodies are POINT with language_body_not_held rather than excluded metadata.
        // Modelling those 22 as excluded would erase the corrigendum metadata this corpus does
        // hold in every observed language, including 385 with no ENG or FRA counterpart.
        Assert.AreEqual(24, EuScopeVocabulary.OfficialLanguages.Count);

        var carried = new EuLanguageBodyDisposition(
            EuOfficialLanguage.French,
            EuLanguageBodyState.BodyCandidate,
            "bilingual_body_scope",
            "eu_language_body_1",
            Evidence("66"));
        Assert.IsTrue(carried.CarriesBody());

        var pointOnly = new EuLanguageBodyDisposition(
            EuOfficialLanguage.German,
            EuLanguageBodyState.BodyNotHeldPoint,
            "language_body_not_held",
            "eu_language_body_1",
            Evidence("66"));
        Assert.IsFalse(pointOnly.CarriesBody());
        // Not an exclusion, and the type must not offer a way to say it is: there is no member
        // on this axis that removes the record.
        AssertTokens<EuLanguageBodyState>("body_candidate", "body_not_held_point");

        // Both states need their reason, rule and content-bound evidence. A body-not-held
        // disposition without evidence is the false absence this axis exists to prevent, and a
        // mutation removing this guard survived until this assertion existed.
        // French for the candidate case and German for POINT: German cannot be a candidate at
        // all, and the policy guard fires before the evidence guard, so pairing German with
        // BodyCandidate here would test the policy refusal rather than the evidence one.
        foreach (var (language, state) in new[]
                 {
                     (EuOfficialLanguage.French, EuLanguageBodyState.BodyCandidate),
                     (EuOfficialLanguage.German, EuLanguageBodyState.BodyNotHeldPoint),
                 })
        {
            Assert.ThrowsExactly<ArgumentNullException>(
                () => new EuLanguageBodyDisposition(language, state, "reason", "rule", null!),
                $"{language} {state} was allowed to carry no evidence");
            Assert.ThrowsExactly<ArgumentException>(
                () => new EuLanguageBodyDisposition(language, state, "  ", "rule", Evidence("66")),
                $"{language} {state} was allowed a blank reason");
        }
    }

    [TestMethod]
    public void BodyCandidacyIsClosedToEnglishAndFrenchAcrossAllTwentyFourLanguages()
    {
        // Exhaustive rather than exemplary. The previous test proved one candidate and one POINT
        // and left the other twenty-two untested, so a German body candidate was constructible:
        // the policy was representable but not closed, which is caller-minted policy wearing a
        // different field from the last time.
        CollectionAssert.AreEqual(
            new[] { EuOfficialLanguage.English, EuOfficialLanguage.French },
            EuLanguageBodyDisposition.BodyCandidateLanguages.ToArray());

        var candidates = 0;
        var pointOnly = 0;
        foreach (var language in EuScopeVocabulary.OfficialLanguages)
        {
            var expectCandidate = language is EuOfficialLanguage.English or EuOfficialLanguage.French;

            if (expectCandidate)
            {
                var held = new EuLanguageBodyDisposition(
                    language, EuLanguageBodyState.BodyCandidate,
                    "bilingual_body_scope", "eu_language_body_1", Evidence("66"));
                Assert.IsTrue(held.CarriesBody());
                candidates++;
            }
            else
            {
                Assert.ThrowsExactly<ArgumentException>(
                    () => new EuLanguageBodyDisposition(
                        language, EuLanguageBodyState.BodyCandidate,
                        "bilingual_body_scope", "eu_language_body_1", Evidence("66")),
                    $"{language} was accepted as a body candidate");
            }

            // Every one of the twenty-four may be POINT, including the two candidates: an
            // expression can exist with metadata and no body whatever the language.
            var point = new EuLanguageBodyDisposition(
                language, EuLanguageBodyState.BodyNotHeldPoint,
                "language_body_not_held", "eu_language_body_1", Evidence("66"));
            Assert.IsFalse(point.CarriesBody());
            pointOnly++;
        }

        Assert.AreEqual(2, candidates, "the candidate set is not exactly two");
        Assert.AreEqual(24, pointOnly, "not every official language could be POINT");
    }

    [TestMethod]
    public void ADeserialisedGermanBodyCandidateIsRefusedOnTheWireToo()
    {
        // The constructor guard must hold on the wire path, or a document could carry a policy the
        // type refuses to be constructed in.
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<EuLanguageBodyDisposition>(
                """
                {"language":"DEU","body_state":"body_candidate","reason_code":"r","rule_id":"eu_language_body_1","evidence_ref":{"resource_id":"urn:uuid:00000000-0000-4000-8000-000000000066","sha256":"6666666666666666666666666666666666666666666666666666666666666666"}}
                """));
    }

    [TestMethod]
    public void TheReadSetIsExactlyThirteenPredicatesPlusFourRelationFamilies()
    {
        Assert.AreEqual(13, EuScopeVocabulary.CdmPredicates.Count);
        Assert.AreEqual(4, EuScopeVocabulary.ReadRelationFamilies.Count);
        Assert.AreEqual(
            17,
            EuScopeVocabulary.CdmPredicates.Count + EuScopeVocabulary.ReadRelationFamilies.Count,
            "the measured read set is seventeen predicates");
        CollectionAssert.AreEqual(
            new[]
            {
                EuRelationFamily.Amends,
                EuRelationFamily.Corrects,
                EuRelationFamily.BasedOn,
                EuRelationFamily.ConsolidatedBasedOn,
            },
            EuScopeVocabulary.ReadRelationFamilies.ToArray());
        // The nine unread families stay in the vocabulary: not reading a family is not the same
        // as the family not existing, and nothing here may imply the publisher asserts none.
        Assert.AreEqual(13, EuScopeVocabulary.RelationFamilies.Count);
    }

    [TestMethod]
    public void AnOntologyAuthorizedInverseMustNameWhatAuthorizesIt()
    {
        // Completion evidence proves an observation happened. It says nothing about whether the
        // ontology permits reading this family backwards, so without a separate reference any
        // family could claim the inverse and be indistinguishable from one that genuinely has it.
        var authorized = new EuRelationFamilyDisposition(
            EuRelationFamily.AmendedBy,
            EuRelationAuthority.OntologyAuthorizedInverse,
            EuRelationAcquisitionState.Unacquired,
            completionEvidenceRef: null,
            OntologyMember("amended_by_inverse"));
        Assert.AreEqual("amended_by_inverse", authorized.OntologyAuthorityRef!.MemberKey);

        Assert.ThrowsExactly<ArgumentNullException>(
            () => new EuRelationFamilyDisposition(
                EuRelationFamily.AmendedBy,
                EuRelationAuthority.OntologyAuthorizedInverse,
                EuRelationAcquisitionState.Unacquired,
                completionEvidenceRef: null,
                ontologyAuthorityRef: null));
    }

    [TestMethod]
    public void OnlyAnOntologyAuthorizedInverseCarriesAnOntologyAuthority()
    {
        // The mirror. A publisher assertion or a local view carrying an ontology reference would
        // read as though something authorized it, which is the confusion the three authorities
        // exist to prevent.
        foreach (var authority in new[]
                 {
                     EuRelationAuthority.PublisherAsserted,
                     EuRelationAuthority.LocalInboundView,
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new EuRelationFamilyDisposition(
                    EuRelationFamily.AmendedBy,
                    authority,
                    EuRelationAcquisitionState.Unacquired,
                    completionEvidenceRef: null,
                    OntologyMember("amended_by_inverse")),
                $"{authority} was allowed to carry an ontology authority");
        }
    }

    [TestMethod]
    public void ThisSliceCannotDecideAbsenceEligibility()
    {
        // Recording that the property is gone on purpose. Acquisition state is recorded here;
        // whether an empty edge list may be read as "the publisher asserts none" needs the shared
        // delivery proof and an independently different witness, so only the later completion
        // validator may mint it.
        Assert.IsNull(
            typeof(EuRelationFamilyDisposition).GetProperty("SupportsAbsenceClaim"),
            "this slice minted absence eligibility again");
    }

    /// Assert that the members of a closed set are exactly these tokens, in declaration order.
    /// The count assertion is what makes it exhaustive: without it, adding a member would leave it
    /// unpinned and the test would still pass.

    private static SourceArtifactRef Evidence(string digitPair) =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000" + digitPair, digitPair[0].ToString()[0] == 'x' ? new string('a', 64) : new string(digitPair[0], 64));

    private static SourceRegistryMemberRef OntologyMember(string memberKey) =>
        new(Evidence("11"), memberKey);

    private static void AssertTokens<TEnum>(params string[] expected)
        where TEnum : struct, Enum
    {
        var members = Enum.GetValues<TEnum>();
        Assert.AreEqual(
            expected.Length,
            members.Length,
            $"{typeof(TEnum).Name} has {members.Length} members but {expected.Length} are pinned");
        for (var index = 0; index < members.Length; index++)
        {
            Assert.AreEqual(
                "\"" + expected[index] + "\"",
                ContractJson.Serialize(members[index]),
                $"{typeof(TEnum).Name}.{members[index]} does not carry its pinned token");
        }
    }

    private static void AssertRoundTrip<TEnum>()
        where TEnum : struct, Enum
    {
        foreach (var value in Enum.GetValues<TEnum>())
        {
            var json = ContractJson.Serialize(value);
            Assert.AreEqual(
                value,
                ContractJson.Deserialize<TEnum>(json),
                $"{typeof(TEnum).Name}.{value} did not round-trip through {json}");
        }
    }

    private static void AssertScopeDrift<TEnum>(string hostile)
        where TEnum : struct, Enum
    {
        Assert.ThrowsExactly<JsonException>(
            () => ContractJson.Deserialize<TEnum>(JsonSerializer.Serialize(hostile)),
            $"{typeof(TEnum).Name} accepted the unknown token {hostile}");
    }
}
