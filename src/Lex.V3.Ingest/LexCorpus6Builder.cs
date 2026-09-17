using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Ingest;

public enum LexCorpus6BuildRefusal
{
    [JsonStringEnumMemberName("none")] None = 0,
    [JsonStringEnumMemberName("evidence_incomplete")] EvidenceIncomplete = 1,
    [JsonStringEnumMemberName("eu_rights_binding_missing")] EuropeRightsBindingMissing = 2,
    [JsonStringEnumMemberName("luxembourg_rights_binding_missing")] LuxembourgRightsBindingMissing = 3,
    [JsonStringEnumMemberName("luxembourg_rights_evidence_incomplete")] LuxembourgRightsEvidenceIncomplete = 4,
    [JsonStringEnumMemberName("population_mismatch")] PopulationMismatch = 5,
    [JsonStringEnumMemberName("eu_rights_evidence_missing")] EuropeRightsEvidenceMissing = 6,
}

public enum LexCorpus6OutcomeKind
{
    [JsonStringEnumMemberName("acquired")] Acquired = 1,
    [JsonStringEnumMemberName("unavailable")] Unavailable = 2,
    [JsonStringEnumMemberName("refused")] Refused = 3,
    [JsonStringEnumMemberName("rights_withheld")] RightsWithheld = 4,
}

public enum LexCorpus6Stage3OutcomeDomain
{
    [JsonStringEnumMemberName("luxembourg_akn_legal_content")]
    LuxembourgAknLegalContent = 1,
    [JsonStringEnumMemberName("luxembourg_publisher_pdf_act_scope")]
    LuxembourgPublisherPdfActScope = 2,
    [JsonStringEnumMemberName("europe_annex_body")]
    EuropeAnnexBody = 3,
    [JsonStringEnumMemberName("europe_formex_main_body")]
    EuropeFormexMainBody = 4,
}

public enum LexCorpus6Stage3Disposition
{
    [JsonStringEnumMemberName("akn_admitted")] AknAdmitted = 1,
    [JsonStringEnumMemberName("akn_upstream_not_inventoried")] AknUpstreamNotInventoried = 2,
    [JsonStringEnumMemberName("akn_retained_bytes_unavailable")] AknRetainedBytesUnavailable = 3,
    [JsonStringEnumMemberName("akn_xml_rejected")] AknXmlRejected = 4,
    [JsonStringEnumMemberName("akn_article_coordinates_mismatch")] AknArticleCoordinatesMismatch = 5,
    [JsonStringEnumMemberName("akn_unsupported_content_shape")] AknUnsupportedContentShape = 6,
    [JsonStringEnumMemberName("pdf_not_applicable")] PdfNotApplicable = 7,
    [JsonStringEnumMemberName("pdf_gazette_issue_scope")] PdfGazetteIssueScope = 8,
    [JsonStringEnumMemberName("pdf_upstream_text_layer_gap")] PdfUpstreamTextLayerGap = 9,
    [JsonStringEnumMemberName("pdf_act_scope_unproven")] PdfActScopeUnproven = 10,
    [JsonStringEnumMemberName("annex_text_not_available")] AnnexTextNotAvailable = 11,
    [JsonStringEnumMemberName("annex_mapping_unresolved")] AnnexMappingUnresolved = 12,
    [JsonStringEnumMemberName("annex_body_contains_text")] AnnexBodyContainsText = 13,
    [JsonStringEnumMemberName("annex_body_contains_no_image")] AnnexBodyContainsNoImage = 14,
    [JsonStringEnumMemberName("annex_mapped_page_outside_document")]
    AnnexMappedPageOutsideDocument = 15,
    [JsonStringEnumMemberName("formex_main_body_admitted")] FormexMainBodyAdmitted = 16,
    [JsonStringEnumMemberName("formex_main_body_not_eligible")] FormexMainBodyNotEligible = 17,
    [JsonStringEnumMemberName("formex_main_body_package_unavailable")] FormexMainBodyPackageUnavailable = 18,
    [JsonStringEnumMemberName("formex_main_body_package_refused")] FormexMainBodyPackageRefused = 19,
    [JsonStringEnumMemberName("formex_main_body_retained_bytes_unavailable")] FormexMainBodyRetainedBytesUnavailable = 20,
    [JsonStringEnumMemberName("formex_main_body_package_unreadable")] FormexMainBodyPackageUnreadable = 21,
    [JsonStringEnumMemberName("formex_main_body_xml_rejected")] FormexMainBodyXmlRejected = 22,
    [JsonStringEnumMemberName("formex_main_body_missing")] FormexMainBodyMissing = 23,
    [JsonStringEnumMemberName("formex_main_body_unsupported_content_shape")] FormexMainBodyUnsupportedContentShape = 24,
    [JsonStringEnumMemberName("akn_marker_only_evidence")] AknMarkerOnlyEvidence = 25,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LexCorpus6Stage3Outcome(
    LexCorpus6Stage3OutcomeDomain Domain,
    string SemanticIdentitySha256,
    LexCorpus6Stage3Disposition Disposition)
{
    public LexCorpus6Stage3Outcome Validate()
    {
        if (!Enum.IsDefined(Domain) || !Enum.IsDefined(Disposition) || !Compatible(Domain, Disposition))
        {
            throw new ArgumentException("The Stage 3 outcome domain and disposition are not one closed pair.");
        }
        LexCorpus6Member.RequireSha256(SemanticIdentitySha256, nameof(SemanticIdentitySha256));
        return this;
    }

    private static bool Compatible(
        LexCorpus6Stage3OutcomeDomain domain,
        LexCorpus6Stage3Disposition disposition) => domain switch
        {
            LexCorpus6Stage3OutcomeDomain.LuxembourgAknLegalContent => disposition is >=
                LexCorpus6Stage3Disposition.AknAdmitted and <=
                LexCorpus6Stage3Disposition.AknUnsupportedContentShape
                or LexCorpus6Stage3Disposition.AknMarkerOnlyEvidence,
            LexCorpus6Stage3OutcomeDomain.LuxembourgPublisherPdfActScope => disposition is >=
                LexCorpus6Stage3Disposition.PdfNotApplicable and <=
                LexCorpus6Stage3Disposition.PdfActScopeUnproven,
            LexCorpus6Stage3OutcomeDomain.EuropeAnnexBody => disposition is >=
                LexCorpus6Stage3Disposition.AnnexTextNotAvailable and <=
                LexCorpus6Stage3Disposition.AnnexMappedPageOutsideDocument,
            LexCorpus6Stage3OutcomeDomain.EuropeFormexMainBody => disposition is >=
                LexCorpus6Stage3Disposition.FormexMainBodyAdmitted and <=
                LexCorpus6Stage3Disposition.FormexMainBodyUnsupportedContentShape,
            _ => false,
        };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LexCorpus6EuropeRightsMatrix(
    SourceArtifactRef LegalNoticeEvidenceRef,
    string LegalNoticeEvidenceBase64,
    string RoutedEvidenceSha256,
    string ResponseBodySha256,
    ulong ResponseByteLength,
    string DurableWriteReceiptSha256,
    string CapturedAt,
    IReadOnlyList<EuRightsDisposition> ContentClasses,
    IReadOnlyList<EuRightsExceptionDisposition> ExceptionChannels)
{
    public LexCorpus6EuropeRightsMatrix Validate()
    {
        ArgumentNullException.ThrowIfNull(LegalNoticeEvidenceRef);
        byte[] legalNoticeBytes;
        try
        {
            legalNoticeBytes = Convert.FromBase64String(LegalNoticeEvidenceBase64);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The embedded EU legal-notice evidence is not base64.", nameof(LegalNoticeEvidenceBase64), exception);
        }
        if (!string.Equals(Convert.ToBase64String(legalNoticeBytes), LegalNoticeEvidenceBase64, StringComparison.Ordinal))
        {
            throw new ArgumentException("The embedded EU legal-notice evidence is not canonical base64.", nameof(LegalNoticeEvidenceBase64));
        }
        var notice = EuLegalNoticeEvidence.ParseAndVerify(legalNoticeBytes);
        if (!string.Equals(notice.CanonicalSha256, LegalNoticeEvidenceRef.Sha256, StringComparison.Ordinal) ||
            !string.Equals(notice.RoutedEvidenceSha256, RoutedEvidenceSha256, StringComparison.Ordinal) ||
            !string.Equals(notice.Sha256, ResponseBodySha256, StringComparison.Ordinal) ||
            notice.ByteLength != ResponseByteLength ||
            !string.Equals(notice.DurableWriteReceiptSha256, DurableWriteReceiptSha256, StringComparison.Ordinal) ||
            !string.Equals(notice.CapturedAt, CapturedAt, StringComparison.Ordinal))
        {
            throw new ArgumentException("The EU policy provenance does not match its strict-reopened legal-notice evidence.");
        }
        LexCorpus6Member.RequireSha256(RoutedEvidenceSha256, nameof(RoutedEvidenceSha256));
        LexCorpus6Member.RequireSha256(ResponseBodySha256, nameof(ResponseBodySha256));
        LexCorpus6Member.RequireSha256(DurableWriteReceiptSha256, nameof(DurableWriteReceiptSha256));
        if (ResponseByteLength == 0 || string.IsNullOrWhiteSpace(CapturedAt))
        {
            throw new ArgumentException("The EU notice policy must retain response custody and capture time.");
        }
        ArgumentNullException.ThrowIfNull(ContentClasses);
        ArgumentNullException.ThrowIfNull(ExceptionChannels);
        var expectedClasses = Enum.GetValues<EuContentClass>();
        var expectedChannels = Enum.GetValues<EuRightsExceptionChannel>();
        if (ContentClasses.Count != expectedClasses.Length ||
            ContentClasses.Where((value, index) =>
                value is null || value.ContentClass != expectedClasses[index] ||
                value.Basis != EuRightsDisposition.BasisFor(expectedClasses[index]) ||
                value.EvidenceRef != LegalNoticeEvidenceRef).Any())
        {
            throw new ArgumentException(
                "The EU rights matrix must carry every content class exactly once in closed order.",
                nameof(ContentClasses));
        }

        if (ExceptionChannels.Count != expectedChannels.Length ||
            ExceptionChannels.Where((value, index) =>
                value is null || value.Channel != expectedChannels[index] ||
                value.EvidenceRef != LegalNoticeEvidenceRef).Any())
        {
            throw new ArgumentException(
                "The EU rights matrix must carry every exception channel exactly once in closed order.",
                nameof(ExceptionChannels));
        }

        return this;
    }

    public static LexCorpus6EuropeRightsMatrix From(EuLegalNoticeEvidence notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var evidenceRef = notice.ToArtifactRef(LexCorpus6Builder.ResourceIdOf(notice.CanonicalSha256));
        return new LexCorpus6EuropeRightsMatrix(
            evidenceRef,
            Convert.ToBase64String(notice.CopyCanonicalBytes()),
            notice.RoutedEvidenceSha256,
            notice.Sha256,
            notice.ByteLength,
            notice.DurableWriteReceiptSha256,
            notice.CapturedAt,
            Enum.GetValues<EuContentClass>().Select(value =>
                new EuRightsDisposition(value, EuRightsDisposition.BasisFor(value), evidenceRef)).ToArray(),
            Enum.GetValues<EuRightsExceptionChannel>().Select(value =>
                new EuRightsExceptionDisposition(value, evidenceRef)).ToArray()).Validate();
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LexCorpus6LuxembourgWemiBinding(
    string RootIri,
    string ExpressionIri,
    string ManifestationIri,
    string ItemIri,
    string LanguageIri,
    string FormatIri,
    SourceArtifactRef ObservationRef,
    string IdentitySha256)
{
    public LexCorpus6LuxembourgWemiBinding Validate()
    {
        RequireExactAbsoluteIri(RootIri, nameof(RootIri));
        RequireExactAbsoluteIri(ExpressionIri, nameof(ExpressionIri));
        RequireExactAbsoluteIri(ManifestationIri, nameof(ManifestationIri));
        RequireExactAbsoluteIri(ItemIri, nameof(ItemIri));
        RequireExactAbsoluteIri(LanguageIri, nameof(LanguageIri));
        RequireExactAbsoluteIri(FormatIri, nameof(FormatIri));
        ArgumentNullException.ThrowIfNull(ObservationRef);
        if (!string.Equals(IdentitySha256, ComputeIdentitySha256(
                RootIri, ExpressionIri, ManifestationIri, ItemIri,
                LanguageIri, FormatIri, ObservationRef), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The selected WEMI identity does not match its six coordinates and observation evidence.",
                nameof(IdentitySha256));
        }

        return this;
    }

    public static LexCorpus6LuxembourgWemiBinding From(LuxembourgWemiCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new LexCorpus6LuxembourgWemiBinding(
            candidate.RootIri,
            candidate.ExpressionIri,
            candidate.ManifestationIri,
            candidate.ItemIri,
            candidate.LanguageIri,
            candidate.FormatIri,
            candidate.ObservationRef,
            ComputeIdentitySha256(
                candidate.RootIri, candidate.ExpressionIri, candidate.ManifestationIri,
                candidate.ItemIri, candidate.LanguageIri, candidate.FormatIri,
                candidate.ObservationRef)).Validate();
    }

    private static string ComputeIdentitySha256(
        string rootIri,
        string expressionIri,
        string manifestationIri,
        string itemIri,
        string languageIri,
        string formatIri,
        SourceArtifactRef observationRef)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var value in new[]
        {
            "lex-corpus/6/luxembourg-selected-wemi/1",
            rootIri,
            expressionIri,
            manifestationIri,
            itemIri,
            languageIri,
            formatIri,
            observationRef.ResourceId,
            observationRef.Sha256,
        })
        {
            hash.AppendData(Encoding.UTF8.GetBytes(value));
            hash.AppendData([(byte)'\n']);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void RequireExactAbsoluteIri(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            !string.Equals(parsed.AbsoluteUri, value, StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment))
        {
            throw new ArgumentException("A selected WEMI coordinate must be one exact absolute IRI.", name);
        }
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LexCorpus6LuxembourgRights(
    LexCorpus6LuxembourgWemiBinding SelectedWemi,
    SourceArtifactRef BoundRunIdentity,
    string BindingSha256,
    LuxembourgRightsChannelDisposition Disposition,
    IReadOnlyList<SourceArtifactRef> EvidenceRefs)
{
    public LexCorpus6LuxembourgRights Validate(SourceArtifactRef memberRunIdentity)
    {
        ArgumentNullException.ThrowIfNull(SelectedWemi);
        SelectedWemi.Validate();

        ArgumentNullException.ThrowIfNull(BoundRunIdentity);
        ArgumentNullException.ThrowIfNull(memberRunIdentity);
        if (!string.Equals(
                BindingSha256,
                ComputeBindingSha256(memberRunIdentity, BoundRunIdentity, SelectedWemi.IdentitySha256),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Luxembourg rights binding does not match its acquisition run, rights run and selected WEMI.",
                nameof(BindingSha256));
        }

        if (!IsTerminal(Disposition))
        {
            throw new ArgumentException(
                "The Luxembourg rights resolution is not a terminal post-acquisition disposition.",
                nameof(Disposition));
        }

        ArgumentNullException.ThrowIfNull(EvidenceRefs);
        if (EvidenceRefs.Count < 2)
        {
            throw new ArgumentException(
                "Both Luxembourg rights-channel enumerations must be evidenced.",
                nameof(EvidenceRefs));
        }

        LexCorpus6Member.RequireSortedArtifacts(EvidenceRefs, nameof(EvidenceRefs));
        return this;
    }

    public static string ComputeBindingSha256(
        SourceArtifactRef memberRunIdentity,
        SourceArtifactRef rightsRunIdentity,
        string selectedWemiIdentitySha256)
    {
        ArgumentNullException.ThrowIfNull(memberRunIdentity);
        ArgumentNullException.ThrowIfNull(rightsRunIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedWemiIdentitySha256);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var value in new[]
        {
            "lex-corpus/6/luxembourg-rights-binding/1",
            memberRunIdentity.ResourceId,
            memberRunIdentity.Sha256,
            rightsRunIdentity.ResourceId,
            rightsRunIdentity.Sha256,
            selectedWemiIdentitySha256,
        })
        {
            hash.AppendData(Encoding.UTF8.GetBytes(value));
            hash.AppendData([(byte)'\n']);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static bool IsTerminal(LuxembourgRightsChannelDisposition disposition) =>
        disposition is LuxembourgRightsChannelDisposition.MissingValue
            or LuxembourgRightsChannelDisposition.AgreedSameRunCcBy
            or LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl
            or LuxembourgRightsChannelDisposition.TypedQuarantineUnruledLicence
            or LuxembourgRightsChannelDisposition.TypedQuarantineUnrepresentableLicenceShape
            or LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LexCorpus6Member(
    PublisherId Publisher,
    string ObjectRefSha256,
    int SourceOrdinal,
    SourceArtifactRef SourceManifestRef,
    SourceArtifactRef RunIdentity,
    ScopeDisposition BodyDisposition,
    LexCorpus6OutcomeKind Outcome,
    string? BodySha256,
    long? BodyByteLength,
    string? BodyReceiptSha256,
    EuContentClassObservation? EuropeContentClass,
    LexCorpus6LuxembourgRights? LuxembourgRights,
    IReadOnlyList<LexCorpus6Stage3Outcome> Stage3Outcomes,
    IReadOnlyList<string> Gaps)
{
    public LexCorpus6Member Validate()
    {
        RequireDefined(Publisher, nameof(Publisher));
        RequireSha256(ObjectRefSha256, nameof(ObjectRefSha256));
        ArgumentOutOfRangeException.ThrowIfNegative(SourceOrdinal, nameof(SourceOrdinal));
        ArgumentNullException.ThrowIfNull(SourceManifestRef);
        ArgumentNullException.ThrowIfNull(RunIdentity);
        RequireDefined(BodyDisposition, nameof(BodyDisposition));
        RequireDefined(Outcome, nameof(Outcome));
        ArgumentNullException.ThrowIfNull(Gaps);
        ArgumentNullException.ThrowIfNull(Stage3Outcomes);
        var carriesHeldBytes = Outcome is LexCorpus6OutcomeKind.Acquired or LexCorpus6OutcomeKind.RightsWithheld;
        if (carriesHeldBytes != (BodySha256 is not null))
        {
            throw new ArgumentException(
                "Exactly an acquired or rights-withheld member carries held-body custody.",
                nameof(BodySha256));
        }

        if (!carriesHeldBytes && Stage3Outcomes.Count != 0)
        {
            throw new ArgumentException("Only held source members can carry Stage 3 derivation outcomes.");
        }
        foreach (var stage3Outcome in Stage3Outcomes)
        {
            ArgumentNullException.ThrowIfNull(stage3Outcome, nameof(Stage3Outcomes));
            stage3Outcome.Validate();
        }
        for (var i = 1; i < Stage3Outcomes.Count; i++)
        {
            if (CompareStage3Outcomes(Stage3Outcomes[i - 1], Stage3Outcomes[i]) >= 0)
            {
                throw new ArgumentException("Stage 3 outcomes must be sorted and unique.", nameof(Stage3Outcomes));
            }
        }

        if (BodySha256 is not null)
        {
            RequireSha256(BodySha256, nameof(BodySha256));
            RequireSha256(BodyReceiptSha256!, nameof(BodyReceiptSha256));
            ArgumentOutOfRangeException.ThrowIfNegative(BodyByteLength!.Value, nameof(BodyByteLength));
        }
        else if (BodyByteLength is not null || BodyReceiptSha256 is not null)
        {
            throw new ArgumentException("A member without acquired bytes carries no body custody fields.");
        }

        if (!carriesHeldBytes)
        {
            if (EuropeContentClass is not null || LuxembourgRights is not null)
            {
                throw new ArgumentException("A member without held bytes cannot carry rights bindings.");
            }
        }
        else if (Publisher == PublisherId.EuEurLex)
        {
            ArgumentNullException.ThrowIfNull(EuropeContentClass);
            if (LuxembourgRights is not null || Outcome != LexCorpus6OutcomeKind.Acquired)
            {
                throw new ArgumentException("A held EU member requires only its EU class binding and an acquired outcome.");
            }
            if (Stage3Outcomes.Count(static outcome =>
                    outcome.Domain == LexCorpus6Stage3OutcomeDomain.EuropeFormexMainBody) != 1)
            {
                throw new ArgumentException(
                    "A held EU member requires exactly one Formex main-body outcome.",
                    nameof(Stage3Outcomes));
            }
        }
        else if (Publisher == PublisherId.LuLegilux)
        {
            if (EuropeContentClass is not null)
            {
                throw new ArgumentException("A Luxembourg member cannot carry an EU rights class.");
            }

            ArgumentNullException.ThrowIfNull(LuxembourgRights);
            LuxembourgRights.Validate(RunIdentity);
            var expectedOutcome = LuxembourgRights.Disposition ==
                LuxembourgRightsChannelDisposition.AgreedSameRunCcBy
                    ? LexCorpus6OutcomeKind.Acquired
                    : LexCorpus6OutcomeKind.RightsWithheld;
            if (Outcome != expectedOutcome)
            {
                throw new ArgumentException("The Luxembourg outcome contradicts its closed rights disposition.");
            }
        }

        RequireSortedStrings(Gaps, nameof(Gaps));
        return this;
    }

    internal static int CompareStage3Outcomes(
        LexCorpus6Stage3Outcome left,
        LexCorpus6Stage3Outcome right)
    {
        var domain = left.Domain.CompareTo(right.Domain);
        return domain != 0
            ? domain
            : string.CompareOrdinal(left.SemanticIdentitySha256, right.SemanticIdentitySha256);
    }

    internal static void RequireSortedArtifacts(IReadOnlyList<SourceArtifactRef> values, string name)
    {
        if (values.Any(static value => value is null))
        {
            throw new ArgumentException("Artifact lists cannot contain null.", name);
        }

        for (var i = 1; i < values.Count; i++)
        {
            var left = values[i - 1];
            var right = values[i];
            var order = string.CompareOrdinal(left.ResourceId, right.ResourceId);
            if (order > 0 || order == 0 && string.CompareOrdinal(left.Sha256, right.Sha256) >= 0)
            {
                throw new ArgumentException("Artifact lists must be sorted and unique.", name);
            }
        }
    }

    internal static void RequireSortedStrings(IReadOnlyList<string> values, string name)
    {
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("String lists cannot contain empty values.", name);
        }

        for (var i = 1; i < values.Count; i++)
        {
            if (string.CompareOrdinal(values[i - 1], values[i]) >= 0)
            {
                throw new ArgumentException("String lists must be sorted and unique.", name);
            }
        }
    }

    internal static string RequireSha256(string value, string name)
    {
        if (value is not { Length: 64 } || value.Any(static c => c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f'))))
        {
            throw new ArgumentException("A SHA-256 must be 64 lowercase hexadecimal characters.", name);
        }

        return value;
    }

    private static T RequireDefined<T>(T value, string name) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "The value is outside the closed vocabulary.");
        }

        return value;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LexCorpus6CorrigendumLine(
    string CorrigendumWorkRoot,
    string PublisherExpressionId,
    string LanguageIri,
    EuCorrigendumLanguageReach Reach,
    EuCorrigendumDateState DateState,
    string? PublisherDateRawLexical,
    string? PublisherDateDatatypeIri,
    string ExpressionContentSha256)
{
    public LexCorpus6CorrigendumLine Validate()
    {
        RequireAbsoluteIri(CorrigendumWorkRoot, nameof(CorrigendumWorkRoot));
        RequireAbsoluteIri(PublisherExpressionId, nameof(PublisherExpressionId));
        RequireAbsoluteIri(LanguageIri, nameof(LanguageIri));
        if (!Enum.IsDefined(Reach) || !Enum.IsDefined(DateState) ||
            Reach != EuCorrigendumTripwireSet.ReachOf(LanguageIri))
        {
            throw new ArgumentException("The corrigendum language reach is not the reviewed reach of its language.");
        }

        var dated = DateState == EuCorrigendumDateState.PublisherDated;
        if (dated != (PublisherDateRawLexical is not null) ||
            dated != (PublisherDateDatatypeIri is not null))
        {
            throw new ArgumentException("Publisher date fields must be present exactly for publisher-dated corrigenda.");
        }
        if (dated)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(PublisherDateRawLexical);
            RequireAbsoluteIri(PublisherDateDatatypeIri!, nameof(PublisherDateDatatypeIri));
        }
        LexCorpus6Member.RequireSha256(ExpressionContentSha256, nameof(ExpressionContentSha256));
        return this;
    }

    internal static int Compare(LexCorpus6CorrigendumLine left, LexCorpus6CorrigendumLine right)
    {
        var work = string.CompareOrdinal(left.CorrigendumWorkRoot, right.CorrigendumWorkRoot);
        return work != 0 ? work : string.CompareOrdinal(left.PublisherExpressionId, right.PublisherExpressionId);
    }

    internal static void RequireAbsoluteIri(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            !string.Equals(parsed.AbsoluteUri, value, StringComparison.Ordinal))
        {
            throw new ArgumentException("The value must be one exact absolute IRI.", name);
        }
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LexCorpus6CorrigendumTripwire(
    string CorrectedWorkRoot,
    string TripwireSha256,
    IReadOnlyList<LexCorpus6CorrigendumLine> Lines,
    IReadOnlyList<string> CorrigendaWithoutDerivedExpressions)
{
    public LexCorpus6CorrigendumTripwire Validate()
    {
        LexCorpus6CorrigendumLine.RequireAbsoluteIri(CorrectedWorkRoot, nameof(CorrectedWorkRoot));
        LexCorpus6Member.RequireSha256(TripwireSha256, nameof(TripwireSha256));
        ArgumentNullException.ThrowIfNull(Lines);
        ArgumentNullException.ThrowIfNull(CorrigendaWithoutDerivedExpressions);
        foreach (var line in Lines) (line ?? throw new ArgumentException("A corrigendum line is null.")).Validate();
        for (var i = 1; i < Lines.Count; i++)
        {
            if (LexCorpus6CorrigendumLine.Compare(Lines[i - 1], Lines[i]) >= 0)
                throw new ArgumentException("Corrigendum lines must be sorted and unique.", nameof(Lines));
        }
        foreach (var root in CorrigendaWithoutDerivedExpressions)
            LexCorpus6CorrigendumLine.RequireAbsoluteIri(root, nameof(CorrigendaWithoutDerivedExpressions));
        LexCorpus6Member.RequireSortedStrings(
            CorrigendaWithoutDerivedExpressions, nameof(CorrigendaWithoutDerivedExpressions));
        var canonical = new EuCorrigendumTripwire.CanonicalTripwireDocument(
            EuCorrigendumTripwire.Schema,
            CorrectedWorkRoot,
            Lines.Select(static line => new EuCorrigendumTripwire.CanonicalLineDocument(
                line.CorrigendumWorkRoot,
                line.PublisherExpressionId,
                line.LanguageIri,
                line.Reach.ToString(),
                line.DateState.ToString(),
                line.PublisherDateRawLexical,
                line.PublisherDateDatatypeIri,
                line.ExpressionContentSha256)).ToArray(),
            CorrigendaWithoutDerivedExpressions);
        if (!string.Equals(
                TripwireSha256,
                EuCorrigendumTripwire.CanonicalSha256Of(canonical),
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The tripwire identity does not match its canonical projection.", nameof(TripwireSha256));
        }
        return this;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LexCorpus6CorrigendumGap(
    string WorkRoot,
    EuCorrigendumTripwireGapReason Reason)
{
    public LexCorpus6CorrigendumGap Validate()
    {
        LexCorpus6CorrigendumLine.RequireAbsoluteIri(WorkRoot, nameof(WorkRoot));
        if (!Enum.IsDefined(Reason)) throw new ArgumentOutOfRangeException(nameof(Reason));
        return this;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LexCorpus6CorrigendumProduction(
    string FamilyKey,
    string CanonicalSha256,
    string LineageSha256,
    string TripwireReceiptSha256,
    string TripwireLineageReceiptSha256,
    IReadOnlyList<LexCorpus6CorrigendumTripwire> Tripwires,
    IReadOnlyList<LexCorpus6CorrigendumGap> UnresolvedGaps)
{
    public LexCorpus6CorrigendumProduction Validate()
    {
        const string familyPrefix = "eu-object-facts-batch-";
        if (!FamilyKey.StartsWith(familyPrefix, StringComparison.Ordinal) ||
            FamilyKey.Length != familyPrefix.Length + 24 ||
            !FamilyKey.AsSpan(familyPrefix.Length).ToArray().All(static value => char.IsAsciiHexDigitLower(value)))
        {
            throw new ArgumentException("The corrigendum family key is not a canonical EU object-facts batch key.", nameof(FamilyKey));
        }
        LexCorpus6Member.RequireSha256(CanonicalSha256, nameof(CanonicalSha256));
        LexCorpus6Member.RequireSha256(LineageSha256, nameof(LineageSha256));
        LexCorpus6Member.RequireSha256(TripwireReceiptSha256, nameof(TripwireReceiptSha256));
        LexCorpus6Member.RequireSha256(TripwireLineageReceiptSha256, nameof(TripwireLineageReceiptSha256));
        ArgumentNullException.ThrowIfNull(Tripwires);
        ArgumentNullException.ThrowIfNull(UnresolvedGaps);
        foreach (var tripwire in Tripwires)
            (tripwire ?? throw new ArgumentException("A corrigendum tripwire is null.")).Validate();
        for (var i = 1; i < Tripwires.Count; i++)
        {
            if (string.CompareOrdinal(Tripwires[i - 1].CorrectedWorkRoot, Tripwires[i].CorrectedWorkRoot) >= 0)
                throw new ArgumentException("Corrigendum tripwires must be sorted and unique.", nameof(Tripwires));
        }
        foreach (var gap in UnresolvedGaps)
            (gap ?? throw new ArgumentException("A corrigendum gap is null.")).Validate();
        for (var i = 1; i < UnresolvedGaps.Count; i++)
        {
            if (string.CompareOrdinal(UnresolvedGaps[i - 1].WorkRoot, UnresolvedGaps[i].WorkRoot) >= 0)
                throw new ArgumentException("Corrigendum gaps must be sorted and unique.", nameof(UnresolvedGaps));
        }
        var canonical = new EuCorrigendumTripwireSet.CanonicalSetDocument(
            EuCorrigendumTripwireSet.Schema,
            Tripwires.Select(static tripwire => new EuCorrigendumTripwire.CanonicalTripwireDocument(
                EuCorrigendumTripwire.Schema,
                tripwire.CorrectedWorkRoot,
                tripwire.Lines.Select(static line => new EuCorrigendumTripwire.CanonicalLineDocument(
                    line.CorrigendumWorkRoot,
                    line.PublisherExpressionId,
                    line.LanguageIri,
                    line.Reach.ToString(),
                    line.DateState.ToString(),
                    line.PublisherDateRawLexical,
                    line.PublisherDateDatatypeIri,
                    line.ExpressionContentSha256)).ToArray(),
                tripwire.CorrigendaWithoutDerivedExpressions)).ToArray(),
            UnresolvedGaps.Select(static gap =>
                new EuCorrigendumTripwireSet.CanonicalGapDocument(
                    gap.WorkRoot,
                    gap.Reason.ToString())).ToArray());
        if (!string.Equals(
                CanonicalSha256,
                EuCorrigendumTripwireSet.CanonicalSha256Of(canonical),
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The corrigendum set identity does not match its canonical projection.", nameof(CanonicalSha256));
        }
        return this;
    }

    public static LexCorpus6CorrigendumProduction From(
        string familyKey,
        Europe.EuCorrigendumTripwireProductionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var set = result.TripwireSet ?? throw new ArgumentException("A delivered production requires a tripwire set.", nameof(result));
        var production = new LexCorpus6CorrigendumProduction(
            familyKey,
            set.CanonicalSha256,
            set.LineageSha256,
            DurableBlobWriteReceiptDigest.Of(result.RetainedTripwire!),
            DurableBlobWriteReceiptDigest.Of(result.RetainedTripwireLineage!),
            set.Tripwires.Select(static tripwire => new LexCorpus6CorrigendumTripwire(
                tripwire.CorrectedWorkRoot,
                tripwire.TripwireSha256,
                tripwire.Lines.Select(static line => new LexCorpus6CorrigendumLine(
                    line.CorrigendumWorkRoot,
                    line.PublisherExpressionId,
                    line.LanguageIri,
                    line.Reach,
                    line.DateState,
                    line.PublisherCorrigendumDate?.RawLexical,
                    line.PublisherCorrigendumDate?.DatatypeIri,
                    line.ExpressionContentSha256)).ToArray(),
                tripwire.CorrigendaWithoutDerivedExpressions
                    .Select(static value => value.CorrigendumWorkRoot).ToArray())).ToArray(),
            set.UnresolvedGaps.Select(static gap =>
                new LexCorpus6CorrigendumGap(gap.WorkRoot, gap.Reason)).ToArray());
        return production;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LexCorpus6ManifestSet(
    string Schema,
    SourceArtifactRef EuropeSourceSetRef,
    SourceArtifactRef LuxembourgSourceSetRef,
    LexCorpus6EuropeRightsMatrix EuropeRightsMatrix,
    IReadOnlyList<string> ProfileIdentities,
    IReadOnlyList<string> CorrigendumEvidenceReceiptSha256,
    IReadOnlyList<LexCorpus6CorrigendumProduction> CorrigendumProductions,
    IReadOnlyList<Stage3FidelityPreservationObligation> UnresolvedFidelityObligations,
    IReadOnlyList<LexCorpus6Member> Members)
{
    public LexCorpus6ManifestSet Validate()
    {
        if (!string.Equals(Schema, LexCorpus6Builder.Schema, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected corpus manifest-set schema.", nameof(Schema));
        }

        ArgumentNullException.ThrowIfNull(EuropeSourceSetRef);
        ArgumentNullException.ThrowIfNull(LuxembourgSourceSetRef);
        ArgumentNullException.ThrowIfNull(EuropeRightsMatrix);
        EuropeRightsMatrix.Validate();
        ArgumentNullException.ThrowIfNull(ProfileIdentities);
        ArgumentNullException.ThrowIfNull(CorrigendumEvidenceReceiptSha256);
        ArgumentNullException.ThrowIfNull(CorrigendumProductions);
        ArgumentNullException.ThrowIfNull(UnresolvedFidelityObligations);
        ArgumentNullException.ThrowIfNull(Members);
        foreach (var value in ProfileIdentities.Concat(CorrigendumEvidenceReceiptSha256))
        {
            LexCorpus6Member.RequireSha256(value, nameof(ProfileIdentities));
        }

        LexCorpus6Member.RequireSortedStrings(ProfileIdentities, nameof(ProfileIdentities));
        LexCorpus6Member.RequireSortedStrings(CorrigendumEvidenceReceiptSha256, nameof(CorrigendumEvidenceReceiptSha256));
        if (CorrigendumProductions.Count == 0)
            throw new ArgumentException("The corpus must carry each completed corrigendum production.", nameof(CorrigendumProductions));
        foreach (var production in CorrigendumProductions)
            (production ?? throw new ArgumentException("A corrigendum production is null.")).Validate();
        for (var i = 1; i < CorrigendumProductions.Count; i++)
        {
            if (string.CompareOrdinal(CorrigendumProductions[i - 1].FamilyKey, CorrigendumProductions[i].FamilyKey) >= 0)
                throw new ArgumentException("Corrigendum productions must be sorted and unique.", nameof(CorrigendumProductions));
        }
        if (UnresolvedFidelityObligations.Count != 0)
            throw new ArgumentException("The unresolved fidelity obligations do not match the accepted fidelity proof boundary.", nameof(UnresolvedFidelityObligations));
        if (Members.Count == 0 || Members.Any(static member => member is null))
        {
            throw new ArgumentException("A corpus manifest set requires members.", nameof(Members));
        }

        foreach (var member in Members)
        {
            member.Validate();
        }

        for (var i = 1; i < Members.Count; i++)
        {
            if (CompareMembers(Members[i - 1], Members[i]) >= 0)
            {
                throw new ArgumentException("Corpus members must be sorted and unique.", nameof(Members));
            }
        }

        return this;
    }

    internal static int CompareMembers(LexCorpus6Member left, LexCorpus6Member right)
    {
        var publisher = left.Publisher.CompareTo(right.Publisher);
        return publisher != 0 ? publisher : string.CompareOrdinal(left.ObjectRefSha256, right.ObjectRefSha256);
    }
}

public static class LexCorpus6Builder
{
    public const string Schema = "lex-corpus/6";
    private static readonly byte[] Domain = Encoding.ASCII.GetBytes(Schema + "\n");

    public static LexCorpus6BuildResult? TryBuild(
        Stage3DerivationProfileEnvelope profileEnvelope,
        out LexCorpus6BuildRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(profileEnvelope);
        refusal = LexCorpus6BuildRefusal.None;
        detail = null;
        var composition = profileEnvelope.BodyComposition;
        var evidence = composition.Envelope;
        var eu = evidence.Europe;
        var lu = evidence.Luxembourg;
        if (eu.CorpusRecordSetRef is null || eu.CorpusRecordSet is null ||
            lu.CorpusRecordSetRef is null || lu.CorpusRecordSet is null || eu.CorrigendumTripwires is null ||
            evidence.FormexMainBodyLegalContent is null)
        {
            refusal = LexCorpus6BuildRefusal.EvidenceIncomplete;
            detail = "A source record set, corrigendum completion or Formex main-body population is missing.";
            return null;
        }

        var formexMainBody = evidence.FormexMainBodyLegalContent;
        if (!ReferenceEquals(formexMainBody.Formex, evidence.Formex) ||
            formexMainBody.Outcomes.Count != evidence.Formex.Outcomes.Count ||
            formexMainBody.Outcomes.Where((value, index) =>
                !ReferenceEquals(value.Source, evidence.Formex.Outcomes[index])).Any())
        {
            refusal = LexCorpus6BuildRefusal.PopulationMismatch;
            detail = "The Formex main-body population is missing, reordered, extra or unbound.";
            return null;
        }

        if (eu.HeldBodyContentClasses is null)
        {
            refusal = LexCorpus6BuildRefusal.EuropeRightsBindingMissing;
            detail = "The EU adapter did not retain its held-body content-class population.";
            return null;
        }

        var legalNotice = evidence.EuropeLegalNoticeEvidence;
        if (legalNotice is null)
        {
            refusal = LexCorpus6BuildRefusal.EuropeRightsEvidenceMissing;
            detail = "The Stage 3 envelope carries no strict-reopened retained EU legal-notice evidence.";
            return null;
        }

        var euHeldRecords = eu.CorpusRecordSet.Set.Records
            .Where(static record => record.Body.Kind == CorpusBodyRecordKind.Held)
            .Select(static record => record.ObjectRef)
            .ToHashSet();
        if (eu.HeldBodyContentClasses.Count != euHeldRecords.Count ||
            eu.HeldBodyContentClasses.Keys.Any(key => !euHeldRecords.Contains(key)))
        {
            refusal = LexCorpus6BuildRefusal.PopulationMismatch;
            detail = "The EU held-body content-class population is missing, duplicated or extra.";
            return null;
        }

        var formexMainBodySources = new Dictionary<
            Europe.EuFormexMainBodyLegalContentOutcome,
            SourceObjectRef>();
        var boundFormexRecords = new HashSet<SourceObjectRef>();
        foreach (var outcome in formexMainBody.Outcomes)
        {
            var matches = eu.CorpusRecordSet.Set.Records.Where(record =>
                    record.Body.Kind == CorpusBodyRecordKind.Held &&
                    FormexMainBodyBelongsTo(outcome.Source.Expression, record.ObjectRef))
                .Take(2)
                .ToArray();
            if (matches.Length == 0 &&
                outcome.Source.Kind != Europe.EuFormexPackageOutcomeKind.Acquired)
            {
                continue;
            }
            if (matches.Length == 0 &&
                IsUnboundAlternateLanguage(
                    outcome.Source.Expression,
                    eu.CorpusRecordSet.Set.Records
                        .Where(static record => record.Body.Kind == CorpusBodyRecordKind.Held)
                        .Select(static record => record.ObjectRef)))
            {
                continue;
            }
            if (matches.Length != 1)
            {
                refusal = LexCorpus6BuildRefusal.PopulationMismatch;
                detail = $"A Formex main-body outcome does not bind by publisher identity to exactly one held EU corpus body: " +
                    $"work={outcome.Source.ExpressionIdentity.PublisherWorkId}; " +
                    $"expression={outcome.Source.ExpressionIdentity.PublisherExpressionId}; " +
                    $"language={outcome.Source.Expression.OfficialLanguage}; " +
                    $"held={string.Join(',', eu.CorpusRecordSet.Set.Records.Where(static record => record.Body.Kind == CorpusBodyRecordKind.Held).Select(static record => record.ObjectRef.PublisherUri))}.";
                return null;
            }
            if (!boundFormexRecords.Add(matches[0].ObjectRef))
            {
                refusal = LexCorpus6BuildRefusal.PopulationMismatch;
                detail = "A held EU corpus body is bound to more than one Formex main-body outcome.";
                return null;
            }
            formexMainBodySources.Add(outcome, matches[0].ObjectRef);
        }
        if (boundFormexRecords.Count != euHeldRecords.Count ||
            euHeldRecords.Any(record => !boundFormexRecords.Contains(record)))
        {
            refusal = LexCorpus6BuildRefusal.PopulationMismatch;
            detail = "The held EU corpus population does not carry exactly one Formex main-body outcome per member.";
            return null;
        }

        var luInputs = new Dictionary<SourceObjectRef, Luxembourg.LuxembourgHeldBodyDerivationInput>();
        foreach (var input in composition.LuxembourgDerivationPopulation.Inputs)
        {
            if (!luInputs.TryAdd(input.CorpusRecord.ObjectRef, input))
            {
                refusal = LexCorpus6BuildRefusal.PopulationMismatch;
                detail = "The Luxembourg held-body derivation population contains a duplicate object.";
                return null;
            }
        }

        var luHeldRecords = lu.CorpusRecordSet.Set.Records
            .Where(static record => record.Body.Kind == CorpusBodyRecordKind.Held)
            .Select(static record => record.ObjectRef)
            .ToHashSet();
        if (luInputs.Count != luHeldRecords.Count || luInputs.Keys.Any(key => !luHeldRecords.Contains(key)))
        {
            refusal = LexCorpus6BuildRefusal.PopulationMismatch;
            detail = "The Luxembourg derivation population is missing or extra relative to held corpus records.";
            return null;
        }
        if (luInputs.Values.Any(static input => input.RightsResolution is null))
        {
            refusal = LexCorpus6BuildRefusal.LuxembourgRightsBindingMissing;
            detail = "A held Luxembourg body has no final two-channel rights resolution.";
            return null;
        }

        var stage3Outcomes = Stage3OutcomesByObjectRef(profileEnvelope, formexMainBodySources);
        var members = new List<LexCorpus6Member>();
        foreach (var record in eu.CorpusRecordSet.Set.Records)
        {
            EuContentClassObservation? observedClass = null;
            if (record.Body.Kind == CorpusBodyRecordKind.Held)
            {
                if (!eu.HeldBodyContentClasses.TryGetValue(record.ObjectRef, out observedClass))
                {
                    refusal = LexCorpus6BuildRefusal.EuropeRightsBindingMissing;
                    detail = record.ObjectRef.PublisherUri;
                    return null;
                }

            }

            members.Add(MemberFromRecord(
                PublisherId.EuEurLex,
                record,
                record.Body.Kind == CorpusBodyRecordKind.Held ? LexCorpus6OutcomeKind.Acquired : OutcomeOf(record),
                observedClass,
                null,
                Stage3OutcomesFor(stage3Outcomes, record.ObjectRef),
                GapOf(record)));
        }

        foreach (var record in lu.CorpusRecordSet.Set.Records)
        {
            if (record.Body.Kind != CorpusBodyRecordKind.Held)
            {
                members.Add(MemberFromRecord(PublisherId.LuLegilux, record, OutcomeOf(record), null, null,
                    Stage3OutcomesFor(stage3Outcomes, record.ObjectRef), GapOf(record)));
                continue;
            }

            if (!luInputs.TryGetValue(record.ObjectRef, out var input) || input.RightsResolution is null)
            {
                refusal = LexCorpus6BuildRefusal.LuxembourgRightsBindingMissing;
                detail = record.ObjectRef.PublisherUri;
                return null;
            }

            var rights = input.RightsResolution;
            if (!string.Equals(
                    rights.SelectedManifestationIri,
                    input.SelectedWemiCandidate.ManifestationIri,
                    StringComparison.Ordinal))
            {
                refusal = LexCorpus6BuildRefusal.LuxembourgRightsBindingMissing;
                detail = $"{record.ObjectRef.PublisherUri}: rights manifestation {rights.SelectedManifestationIri} vs selected {input.SelectedWemiCandidate.ManifestationIri}";
                return null;
            }
            if (!LexCorpus6LuxembourgRights.IsTerminal(rights.Disposition))
            {
                refusal = LexCorpus6BuildRefusal.LuxembourgRightsEvidenceIncomplete;
                detail = $"{record.ObjectRef.PublisherUri}: {rights.ReasonCode}";
                return null;
            }

            var admitted = rights.ChannelsAgreeOnAdmittingLicence;
            var rightsEvidence = new List<SourceArtifactRef>
            {
                rights.SparqlObservations.EnumerationRef,
                rights.InFileObservations.EnumerationRef,
            };
            if (rights.SparqlObservation is not null) rightsEvidence.Add(rights.SparqlObservation.EvidenceRef);
            if (rights.InFileObservation is not null) rightsEvidence.Add(rights.InFileObservation.EvidenceRef);
            var selectedWemi = LexCorpus6LuxembourgWemiBinding.From(input.SelectedWemiCandidate);
            members.Add(MemberFromRecord(
                PublisherId.LuLegilux,
                record,
                admitted ? LexCorpus6OutcomeKind.Acquired : LexCorpus6OutcomeKind.RightsWithheld,
                null,
                new LexCorpus6LuxembourgRights(
                    selectedWemi,
                    rights.BoundRunIdentity,
                    LexCorpus6LuxembourgRights.ComputeBindingSha256(
                        record.RunIdentity,
                        rights.BoundRunIdentity,
                        selectedWemi.IdentitySha256),
                    rights.Disposition,
                    SortArtifacts(rightsEvidence)),
                Stage3OutcomesFor(stage3Outcomes, record.ObjectRef),
                admitted ? [] : [rights.ReasonCode]));
        }

        var profileIdentities = ProfileIdentities(profileEnvelope);
        var set = new LexCorpus6ManifestSet(
            Schema,
            eu.CorpusRecordSetRef,
            lu.CorpusRecordSetRef,
            LexCorpus6EuropeRightsMatrix.From(legalNotice),
            profileIdentities,
            CorrigendumReceipts(eu.CorrigendumTripwires),
            CorrigendumProductions(eu.CorrigendumTripwires),
            TerminalUnresolvedFidelityObligations(evidence),
            members.OrderBy(static member => member, Comparer<LexCorpus6Member>.Create(LexCorpus6ManifestSet.CompareMembers)).ToArray()).Validate();
        var bytes = Write(set);
        var digest = ComputeSha256(bytes);
        var artifactRef = new SourceArtifactRef(ResourceIdOf(digest), digest);
        var verified = VerifiedLexCorpus6ManifestSet.ParseAndVerify(
            artifactRef,
            eu.CorpusRecordSetRef,
            lu.CorpusRecordSetRef,
            bytes);
        return new LexCorpus6BuildResult(artifactRef, bytes, verified);
    }

    internal static byte[] Write(LexCorpus6ManifestSet set)
    {
        set.Validate();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.Default,
            Indented = false,
            SkipValidation = false,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            WriteArtifact(writer, "europe_source_set_ref", set.EuropeSourceSetRef);
            WriteArtifact(writer, "luxembourg_source_set_ref", set.LuxembourgSourceSetRef);
            WriteEuropeRightsMatrix(writer, set.EuropeRightsMatrix);
            WriteStrings(writer, "profile_identities", set.ProfileIdentities);
            WriteStrings(writer, "corrigendum_evidence_receipt_sha256", set.CorrigendumEvidenceReceiptSha256);
            writer.WriteStartArray("corrigendum_productions");
            foreach (var production in set.CorrigendumProductions)
            {
                writer.WriteStartObject();
                writer.WriteString("family_key", production.FamilyKey);
                writer.WriteString("canonical_sha256", production.CanonicalSha256);
                writer.WriteString("lineage_sha256", production.LineageSha256);
                writer.WriteString("tripwire_receipt_sha256", production.TripwireReceiptSha256);
                writer.WriteString("tripwire_lineage_receipt_sha256", production.TripwireLineageReceiptSha256);
                writer.WriteStartArray("tripwires");
                foreach (var tripwire in production.Tripwires)
                {
                    writer.WriteStartObject();
                    writer.WriteString("corrected_work_root", tripwire.CorrectedWorkRoot);
                    writer.WriteString("tripwire_sha256", tripwire.TripwireSha256);
                    writer.WriteStartArray("lines");
                    foreach (var line in tripwire.Lines)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("corrigendum_work_root", line.CorrigendumWorkRoot);
                        writer.WriteString("publisher_expression_id", line.PublisherExpressionId);
                        writer.WriteString("language_iri", line.LanguageIri);
                        writer.WriteString("reach", ContractWire.NameOf(line.Reach));
                        writer.WriteString("date_state", ContractWire.NameOf(line.DateState));
                        if (line.PublisherDateRawLexical is null)
                        {
                            writer.WriteNull("publisher_date_raw_lexical");
                            writer.WriteNull("publisher_date_datatype_iri");
                        }
                        else
                        {
                            writer.WriteString("publisher_date_raw_lexical", line.PublisherDateRawLexical);
                            writer.WriteString("publisher_date_datatype_iri", line.PublisherDateDatatypeIri);
                        }
                        writer.WriteString("expression_content_sha256", line.ExpressionContentSha256);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    WriteStrings(writer, "corrigenda_without_derived_expressions", tripwire.CorrigendaWithoutDerivedExpressions);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteStartArray("unresolved_gaps");
                foreach (var gap in production.UnresolvedGaps)
                {
                    writer.WriteStartObject();
                    writer.WriteString("work_root", gap.WorkRoot);
                    writer.WriteString("reason", ContractWire.NameOf(gap.Reason));
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("unresolved_fidelity_obligations");
            foreach (var obligation in set.UnresolvedFidelityObligations)
                writer.WriteStringValue(ContractWire.NameOf(obligation));
            writer.WriteEndArray();
            writer.WriteStartArray("members");
            foreach (var member in set.Members)
            {
                writer.WriteStartObject();
                writer.WriteString("publisher", ContractWire.NameOf(member.Publisher));
                writer.WriteString("object_ref_sha256", member.ObjectRefSha256);
                writer.WriteNumber("source_ordinal", member.SourceOrdinal);
                WriteArtifact(writer, "source_manifest_ref", member.SourceManifestRef);
                WriteArtifact(writer, "run_identity", member.RunIdentity);
                writer.WriteString("body_disposition", ContractWire.NameOf(member.BodyDisposition));
                writer.WriteString("outcome", ContractWire.NameOf(member.Outcome));
                if (member.BodySha256 is null)
                {
                    writer.WriteNull("body_sha256"); writer.WriteNull("body_byte_length"); writer.WriteNull("body_receipt_sha256");
                }
                else
                {
                    writer.WriteString("body_sha256", member.BodySha256);
                    writer.WriteNumber("body_byte_length", member.BodyByteLength!.Value);
                    writer.WriteString("body_receipt_sha256", member.BodyReceiptSha256);
                }

                if (member.EuropeContentClass is null)
                {
                    writer.WriteNull("europe_content_class");
                }
                else
                {
                    writer.WriteStartObject("europe_content_class");
                    writer.WriteString("content_class", ContractWire.NameOf(member.EuropeContentClass.ContentClass));
                    WriteArtifact(writer, "evidence_ref", member.EuropeContentClass.EvidenceRef);
                    writer.WriteEndObject();
                }

                if (member.LuxembourgRights is null)
                {
                    writer.WriteNull("luxembourg_rights");
                }
                else
                {
                    writer.WriteStartObject("luxembourg_rights");
                    WriteLuxembourgWemiBinding(writer, member.LuxembourgRights.SelectedWemi);
                    WriteArtifact(writer, "bound_run_identity", member.LuxembourgRights.BoundRunIdentity);
                    writer.WriteString("binding_sha256", member.LuxembourgRights.BindingSha256);
                    writer.WriteString("disposition", ContractWire.NameOf(member.LuxembourgRights.Disposition));
                    writer.WriteStartArray("evidence_refs");
                    foreach (var artifact in member.LuxembourgRights.EvidenceRefs) WriteArtifactValue(writer, artifact);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteStartArray("stage3_outcomes");
                foreach (var outcome in member.Stage3Outcomes)
                {
                    writer.WriteStartObject();
                    writer.WriteString("domain", ContractWire.NameOf(outcome.Domain));
                    writer.WriteString("semantic_identity_sha256", outcome.SemanticIdentitySha256);
                    writer.WriteString("disposition", ContractWire.NameOf(outcome.Disposition));
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                WriteStrings(writer, "gaps", member.Gaps);
                writer.WriteEndObject();
            }

            writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    internal static string ComputeSha256(ReadOnlySpan<byte> bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Domain); hash.AppendData(bytes);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        hash.GetHashAndReset(digest);
        return Convert.ToHexStringLower(digest);
    }

    internal static string ResourceIdOf(string digest)
    {
        var uuid = digest[..32].ToCharArray();
        uuid[12] = '5';
        uuid[16] = '8';
        return $"urn:uuid:{new string(uuid, 0, 8)}-{new string(uuid, 8, 4)}-" +
            $"{new string(uuid, 12, 4)}-{new string(uuid, 16, 4)}-{new string(uuid, 20, 12)}";
    }

    private static LexCorpus6Member MemberFromRecord(
        PublisherId publisher, CorpusRecord record, LexCorpus6OutcomeKind outcome,
        EuContentClassObservation? europeContentClass,
        LexCorpus6LuxembourgRights? luxembourgRights,
        IReadOnlyList<LexCorpus6Stage3Outcome> stage3Outcomes,
        IReadOnlyList<string> gaps)
    {
        var acquired = outcome is LexCorpus6OutcomeKind.Acquired or LexCorpus6OutcomeKind.RightsWithheld
            ? record.Body.Receipt
            : null;
        return new LexCorpus6Member(
            publisher, ScopeManifestCanonicalWriter.ComputeObjectRefSha256(record.ObjectRef),
            record.ObjectOrdinal, record.ManifestRef, record.RunIdentity, record.BodyDisposition, outcome,
            acquired?.Reference.ContentSha256, acquired?.Reference.ByteLength,
            acquired is null ? null : DurableBlobWriteReceiptDigest.Of(acquired),
            europeContentClass, luxembourgRights, stage3Outcomes, gaps).Validate();
    }

    private static IReadOnlyList<LexCorpus6Stage3Outcome> Stage3OutcomesFor(
        IReadOnlyDictionary<SourceObjectRef, IReadOnlyList<LexCorpus6Stage3Outcome>> outcomes,
        SourceObjectRef objectRef) => outcomes.TryGetValue(objectRef, out var found) ? found : [];

    internal static bool FormexMainBodyBelongsTo(
        Lex.V3.Contracts.Derivation.LanguageScopedExpression expression,
        SourceObjectRef corpusObject)
    {
        if (string.Equals(
                expression.Identity.PublisherExpressionId,
                corpusObject.PublisherUri,
                StringComparison.Ordinal))
        {
            return true;
        }

        return IsEnglish(expression.OfficialLanguage) &&
            string.Equals(
                expression.Identity.PublisherWorkId,
                corpusObject.PublisherUri,
                StringComparison.Ordinal);
    }

    internal static bool IsUnboundAlternateLanguage(
        Lex.V3.Contracts.Derivation.LanguageScopedExpression expression,
        IEnumerable<SourceObjectRef> heldCorpusObjects)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(heldCorpusObjects);
        if (IsEnglish(expression.OfficialLanguage)) return false;

        return heldCorpusObjects
            .Where(value => string.Equals(
                expression.Identity.PublisherWorkId,
                value.PublisherUri,
                StringComparison.Ordinal))
            .Take(2)
            .Count() == 1;
    }

    private static bool IsEnglish(string value) => value is "EN" or "ENG" ||
        string.Equals(
            value,
            "http://publications.europa.eu/resource/authority/language/ENG",
            StringComparison.Ordinal);

    private static IReadOnlyDictionary<SourceObjectRef, IReadOnlyList<LexCorpus6Stage3Outcome>>
        Stage3OutcomesByObjectRef(
            Stage3DerivationProfileEnvelope envelope,
            IReadOnlyDictionary<Europe.EuFormexMainBodyLegalContentOutcome, SourceObjectRef>
                formexMainBodySources)
    {
        var collected = new Dictionary<SourceObjectRef, List<LexCorpus6Stage3Outcome>>();
        void Add(SourceObjectRef objectRef, LexCorpus6Stage3Outcome outcome)
        {
            if (!collected.TryGetValue(objectRef, out var values))
            {
                values = [];
                collected.Add(objectRef, values);
            }
            values.Add(outcome.Validate());
        }

        foreach (var outcome in envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation.Outcomes)
        {
            Add(outcome.SourceInventoryOutcome.Input.CorpusRecord.ObjectRef,
                new LexCorpus6Stage3Outcome(
                    LexCorpus6Stage3OutcomeDomain.LuxembourgAknLegalContent,
                    outcome.SemanticIdentitySha256,
                    AknDisposition(outcome.Disposition)));
        }

        foreach (var outcome in envelope.PublisherPdfActScope.Outcomes)
        {
            Add(outcome.SourceTextLayer.SourceLayoutEvidence.SourceEligibility.Input.CorpusRecord.ObjectRef,
                new LexCorpus6Stage3Outcome(
                    LexCorpus6Stage3OutcomeDomain.LuxembourgPublisherPdfActScope,
                    outcome.SemanticIdentitySha256,
                    PdfDisposition(outcome)));
        }

        foreach (var classification in envelope.BodyComposition.Envelope.FormexAnnexClassifications.Classifications)
        {
            foreach (var member in classification.Members)
            {
                Add(classification.Binding.PdfSource.ObjectRef,
                    Stage3Outcome(member));
            }
        }

        foreach (var pair in formexMainBodySources)
        {
            var outcome = pair.Key;
            Add(pair.Value,
                new LexCorpus6Stage3Outcome(
                    LexCorpus6Stage3OutcomeDomain.EuropeFormexMainBody,
                    outcome.SemanticIdentitySha256,
                    FormexMainBodyDisposition(outcome.Disposition)));
        }

        return collected.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<LexCorpus6Stage3Outcome>)pair.Value
                .OrderBy(static value => value,
                    Comparer<LexCorpus6Stage3Outcome>.Create(LexCorpus6Member.CompareStage3Outcomes))
                .ToArray());
    }

    internal static LexCorpus6Stage3Disposition AknDisposition(
        Luxembourg.LuxembourgAknLegalContentDisposition disposition) => disposition switch
        {
            Luxembourg.LuxembourgAknLegalContentDisposition.Admitted => LexCorpus6Stage3Disposition.AknAdmitted,
            Luxembourg.LuxembourgAknLegalContentDisposition.UpstreamNotInventoried => LexCorpus6Stage3Disposition.AknUpstreamNotInventoried,
            Luxembourg.LuxembourgAknLegalContentDisposition.RetainedBytesUnavailable => LexCorpus6Stage3Disposition.AknRetainedBytesUnavailable,
            Luxembourg.LuxembourgAknLegalContentDisposition.XmlRejected => LexCorpus6Stage3Disposition.AknXmlRejected,
            Luxembourg.LuxembourgAknLegalContentDisposition.ArticleCoordinatesMismatch => LexCorpus6Stage3Disposition.AknArticleCoordinatesMismatch,
            Luxembourg.LuxembourgAknLegalContentDisposition.UnsupportedContentShape => LexCorpus6Stage3Disposition.AknUnsupportedContentShape,
            Luxembourg.LuxembourgAknLegalContentDisposition.MarkerOnlyEvidence => LexCorpus6Stage3Disposition.AknMarkerOnlyEvidence,
            _ => throw new InvalidOperationException("Unknown AKN legal-content disposition."),
        };

    internal static LexCorpus6Stage3Disposition PdfDisposition(
        Luxembourg.LuxembourgPublisherPdfActScopeOutcome outcome) => outcome.Disposition switch
        {
            Luxembourg.LuxembourgPublisherPdfActScopeDisposition.NotApplicable => LexCorpus6Stage3Disposition.PdfNotApplicable,
            Luxembourg.LuxembourgPublisherPdfActScopeDisposition.GazetteIssueScope => LexCorpus6Stage3Disposition.PdfGazetteIssueScope,
            Luxembourg.LuxembourgPublisherPdfActScopeDisposition.TypedGap when outcome.GapReason ==
                Luxembourg.LuxembourgPublisherPdfActScopeGapReason.UpstreamTextLayerGap =>
                    LexCorpus6Stage3Disposition.PdfUpstreamTextLayerGap,
            Luxembourg.LuxembourgPublisherPdfActScopeDisposition.TypedGap when outcome.GapReason ==
                Luxembourg.LuxembourgPublisherPdfActScopeGapReason.ActScopeUnproven =>
                    LexCorpus6Stage3Disposition.PdfActScopeUnproven,
            _ => throw new InvalidOperationException("Unknown publisher-PDF act-scope disposition."),
        };

    internal static LexCorpus6Stage3Outcome Stage3Outcome(
        Europe.EuBoundAnnexBodyMemberClassification member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var disposition = AnnexDisposition(member);
        return new LexCorpus6Stage3Outcome(
            LexCorpus6Stage3OutcomeDomain.EuropeAnnexBody,
            member.SemanticIdentitySha256,
            disposition).Validate();
    }

    private static LexCorpus6Stage3Disposition AnnexDisposition(
        Europe.EuBoundAnnexBodyMemberClassification member)
    {
        if (member.Outcome == Lex.V3.Contracts.Derivation.EuAnnexBodyDispositionOutcome.TextNotAvailable)
        {
            return LexCorpus6Stage3Disposition.AnnexTextNotAvailable;
        }
        return member.Gap switch
        {
            Europe.EuBoundAnnexBodyClassificationGap.MappingUnresolved => LexCorpus6Stage3Disposition.AnnexMappingUnresolved,
            Europe.EuBoundAnnexBodyClassificationGap.BodyContainsText => LexCorpus6Stage3Disposition.AnnexBodyContainsText,
            Europe.EuBoundAnnexBodyClassificationGap.BodyContainsNoImage => LexCorpus6Stage3Disposition.AnnexBodyContainsNoImage,
            Europe.EuBoundAnnexBodyClassificationGap.MappedPageOutsideDocument => LexCorpus6Stage3Disposition.AnnexMappedPageOutsideDocument,
            _ => throw new InvalidOperationException("Unknown EU annex body disposition."),
        };
    }

    internal static LexCorpus6Stage3Disposition FormexMainBodyDisposition(
        Europe.EuFormexMainBodyLegalContentDisposition disposition) => disposition switch
        {
            Europe.EuFormexMainBodyLegalContentDisposition.Admitted => LexCorpus6Stage3Disposition.FormexMainBodyAdmitted,
            Europe.EuFormexMainBodyLegalContentDisposition.NotEligible => LexCorpus6Stage3Disposition.FormexMainBodyNotEligible,
            Europe.EuFormexMainBodyLegalContentDisposition.PackageUnavailable => LexCorpus6Stage3Disposition.FormexMainBodyPackageUnavailable,
            Europe.EuFormexMainBodyLegalContentDisposition.PackageRefused => LexCorpus6Stage3Disposition.FormexMainBodyPackageRefused,
            Europe.EuFormexMainBodyLegalContentDisposition.RetainedBytesUnavailable => LexCorpus6Stage3Disposition.FormexMainBodyRetainedBytesUnavailable,
            Europe.EuFormexMainBodyLegalContentDisposition.PackageUnreadable => LexCorpus6Stage3Disposition.FormexMainBodyPackageUnreadable,
            Europe.EuFormexMainBodyLegalContentDisposition.XmlRejected => LexCorpus6Stage3Disposition.FormexMainBodyXmlRejected,
            Europe.EuFormexMainBodyLegalContentDisposition.MainBodyMissing => LexCorpus6Stage3Disposition.FormexMainBodyMissing,
            Europe.EuFormexMainBodyLegalContentDisposition.UnsupportedContentShape => LexCorpus6Stage3Disposition.FormexMainBodyUnsupportedContentShape,
            _ => throw new InvalidOperationException("Unknown EU Formex main-body disposition."),
        };

    private static LexCorpus6OutcomeKind OutcomeOf(CorpusRecord record) => record.Body.Kind switch
    {
        CorpusBodyRecordKind.NotHeld => LexCorpus6OutcomeKind.Unavailable,
        CorpusBodyRecordKind.PendingAcquisition when record.Body.PendingAcquisitionReason!.Kind == CorpusBodyPendingAcquisitionReasonKind.AcquisitionRefused => LexCorpus6OutcomeKind.Refused,
        CorpusBodyRecordKind.PendingAcquisition => LexCorpus6OutcomeKind.Unavailable,
        CorpusBodyRecordKind.Held => LexCorpus6OutcomeKind.Acquired,
        _ => throw new InvalidOperationException("Unknown corpus body outcome."),
    };

    private static IReadOnlyList<string> GapOf(CorpusRecord record) => record.Body.Kind switch
    {
        CorpusBodyRecordKind.NotHeld => [ContractWire.NameOf(record.Body.NotHeldReason!.Value)],
        CorpusBodyRecordKind.PendingAcquisition => [ContractWire.NameOf(record.Body.PendingAcquisitionReason!.Kind)],
        _ => [],
    };

    private static IReadOnlyList<SourceArtifactRef> SortArtifacts(IEnumerable<SourceArtifactRef> values) => values
        .Distinct().OrderBy(static value => value.ResourceId, StringComparer.Ordinal)
        .ThenBy(static value => value.Sha256, StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<string> ProfileIdentities(Stage3DerivationProfileEnvelope envelope)
    {
        if (Lex.V3.Contracts.Derivation.DerivationProfileComparison.Compare(
                Europe.EuFormexMainBodyLegalContentProducer.ProfileSha256,
                Luxembourg.LuxembourgAknLegalContentProfileProducer.RuleProfileSha256) !=
            Lex.V3.Contracts.Derivation.DerivationProfileComparisonOutcome.ProfilesDiffer)
        {
            throw new InvalidDataException(
                "EU Formex and Luxembourg AKN legal content must remain bound to distinct profiles.");
        }
        return new[]
        {
            Europe.EuFormexMainBodyLegalContentProducer.ProfileSha256,
            Luxembourg.LuxembourgAknLegalContentProfileProducer.RuleProfileSha256,
            envelope.BodyComposition.Envelope.LuxembourgAknArticleInventoryPopulation.IdentitySha256,
            envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation.IdentitySha256,
            envelope.PdfEligibility.IdentitySha256,
            envelope.PdfLayoutEvidence.IdentitySha256,
            envelope.PublisherPdfTextLayer.IdentitySha256,
            envelope.PublisherPdfActScope.IdentitySha256,
        }.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> CorrigendumReceipts(Europe.EuCorrigendumTripwireCompletion completion) =>
        completion.ProductionsByFamilyKey.Values
            .SelectMany(static result => new[]
            {
                result.Expressions!.RetainedDerivation, result.Expressions.RetainedEpisode,
                result.RetainedTripwire!, result.RetainedTripwireLineage!,
            })
            .Select(static receipt => DurableBlobWriteReceiptDigest.Of(receipt!))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<LexCorpus6CorrigendumProduction> CorrigendumProductions(
        Europe.EuCorrigendumTripwireCompletion completion) => completion.ProductionsByFamilyKey
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => LexCorpus6CorrigendumProduction.From(pair.Key, pair.Value))
            .ToArray();

    private static void WriteArtifact(Utf8JsonWriter writer, string name, SourceArtifactRef artifact)
    {
        writer.WritePropertyName(name); WriteArtifactValue(writer, artifact);
    }

    private static void WriteArtifactValue(Utf8JsonWriter writer, SourceArtifactRef artifact)
    {
        writer.WriteStartObject(); writer.WriteString("resource_id", artifact.ResourceId);
        writer.WriteString("sha256", artifact.Sha256); writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name); foreach (var value in values) writer.WriteStringValue(value); writer.WriteEndArray();
    }

    private static void WriteEuropeRightsMatrix(
        Utf8JsonWriter writer,
        LexCorpus6EuropeRightsMatrix matrix)
    {
        matrix.Validate();
        writer.WriteStartObject("europe_rights_matrix");
        WriteArtifact(writer, "legal_notice_evidence_ref", matrix.LegalNoticeEvidenceRef);
        writer.WriteString("legal_notice_evidence_base64", matrix.LegalNoticeEvidenceBase64);
        writer.WriteString("routed_evidence_sha256", matrix.RoutedEvidenceSha256);
        writer.WriteString("response_body_sha256", matrix.ResponseBodySha256);
        writer.WriteNumber("response_byte_length", matrix.ResponseByteLength);
        writer.WriteString("durable_write_receipt_sha256", matrix.DurableWriteReceiptSha256);
        writer.WriteString("captured_at", matrix.CapturedAt);
        writer.WriteStartArray("content_classes");
        foreach (var disposition in matrix.ContentClasses)
        {
            writer.WriteStartObject();
            writer.WriteString("content_class", ContractWire.NameOf(disposition.ContentClass));
            writer.WriteString("basis", ContractWire.NameOf(disposition.Basis));
            WriteArtifact(writer, "evidence_ref", disposition.EvidenceRef);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("exception_channels");
        foreach (var disposition in matrix.ExceptionChannels)
        {
            writer.WriteStartObject();
            writer.WriteString("channel", ContractWire.NameOf(disposition.Channel));
            WriteArtifact(writer, "evidence_ref", disposition.EvidenceRef);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteLuxembourgWemiBinding(
        Utf8JsonWriter writer,
        LexCorpus6LuxembourgWemiBinding binding)
    {
        binding.Validate();
        writer.WriteStartObject("selected_wemi");
        writer.WriteString("root_iri", binding.RootIri);
        writer.WriteString("expression_iri", binding.ExpressionIri);
        writer.WriteString("manifestation_iri", binding.ManifestationIri);
        writer.WriteString("item_iri", binding.ItemIri);
        writer.WriteString("language_iri", binding.LanguageIri);
        writer.WriteString("format_iri", binding.FormatIri);
        WriteArtifact(writer, "observation_ref", binding.ObservationRef);
        writer.WriteString("identity_sha256", binding.IdentitySha256);
        writer.WriteEndObject();
    }

    private static IReadOnlyList<Stage3FidelityPreservationObligation>
        TerminalUnresolvedFidelityObligations(Stage3EvidenceEnvelope evidence)
    {
        var aknOutcomes = evidence.LuxembourgAknLegalContentPopulation.Outcomes;
        var hasMarkerOnlyEvidence = aknOutcomes.Any(static outcome =>
            outcome.Disposition == Luxembourg.LuxembourgAknLegalContentDisposition.MarkerOnlyEvidence);
        var noteBodies = aknOutcomes
            .Select(static outcome => outcome.Article)
            .Where(static article => article is not null)
            .SelectMany(static article => article!.Tokens)
            .Where(static token => token.NoteBody is not null)
            .SelectMany(static token => token.NoteBody!)
            .ToArray();
        var hasFootnoteEvidence = noteBodies.Length != 0;
        var hasCitationEvidence = noteBodies.Any(static token =>
            token.Kind == Luxembourg.LuxembourgAknLegalContentTokenKind.Reference &&
            !string.IsNullOrWhiteSpace(token.Target));
        var retired = new HashSet<Stage3FidelityPreservationObligation>();
        if (hasMarkerOnlyEvidence)
        {
            retired.Add(Stage3FidelityPreservationObligation.MarkerOnlyRuleUnsupported);
        }
        if (hasFootnoteEvidence)
        {
            retired.Add(Stage3FidelityPreservationObligation.FootnotePreservationUnproven);
        }
        if (hasCitationEvidence)
        {
            retired.Add(Stage3FidelityPreservationObligation.CitationPreservationUnproven);
        }

        return evidence.FidelityPreservation.UnresolvedObligations
            .Where(value => !retired.Contains(value))
            .OrderBy(static value => value)
            .ToArray();
    }
}

public sealed record LexCorpus6BuildResult(
    SourceArtifactRef ArtifactRef,
    ReadOnlyMemory<byte> CanonicalBytes,
    VerifiedLexCorpus6ManifestSet VerifiedSet);

public sealed class VerifiedLexCorpus6ManifestSet
{
    private VerifiedLexCorpus6ManifestSet(LexCorpus6ManifestSet set) => Set = set;
    public LexCorpus6ManifestSet Set { get; }

    public static VerifiedLexCorpus6ManifestSet ParseAndVerify(
        SourceArtifactRef artifactRef,
        SourceArtifactRef expectedEuropeSourceSetRef,
        SourceArtifactRef expectedLuxembourgSourceSetRef,
        ReadOnlySpan<byte> canonicalBytes)
    {
        ArgumentNullException.ThrowIfNull(artifactRef);
        ArgumentNullException.ThrowIfNull(expectedEuropeSourceSetRef);
        ArgumentNullException.ThrowIfNull(expectedLuxembourgSourceSetRef);
        if (!string.Equals(LexCorpus6Builder.ComputeSha256(canonicalBytes), artifactRef.Sha256, StringComparison.Ordinal))
        {
            throw new ArgumentException("The corpus manifest-set bytes do not match their artifact reference.", nameof(canonicalBytes));
        }

        if (!string.Equals(
                LexCorpus6Builder.ResourceIdOf(artifactRef.Sha256),
                artifactRef.ResourceId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The corpus manifest-set resource identity does not derive from its canonical digest.",
                nameof(artifactRef));
        }

        LexCorpus6ManifestSet set;
        try
        {
            set = ContractJson.Deserialize<LexCorpus6ManifestSet>(new UTF8Encoding(false, true).GetString(canonicalBytes)).Validate();
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or ArgumentException)
        {
            throw new ArgumentException("The corpus manifest-set bytes are not one valid typed document.", nameof(canonicalBytes), exception);
        }

        if (!canonicalBytes.SequenceEqual(LexCorpus6Builder.Write(set)))
        {
            throw new ArgumentException("The corpus manifest set is not its exact canonical representation.", nameof(canonicalBytes));
        }

        if (set.EuropeSourceSetRef != expectedEuropeSourceSetRef ||
            set.LuxembourgSourceSetRef != expectedLuxembourgSourceSetRef)
        {
            throw new ArgumentException(
                "The corpus manifest set does not bind the expected source record sets.",
                nameof(canonicalBytes));
        }

        return new VerifiedLexCorpus6ManifestSet(set);
    }
}
