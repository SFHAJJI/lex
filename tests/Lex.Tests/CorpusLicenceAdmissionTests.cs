using Lex.Ingest;
using Lex.Law;
using Lex.Sources.Legilux;

namespace Lex.Tests;

public sealed class CorpusLicenceAdmissionTests
{
    private const string CreativeCommonsBy40 =
        "http://creativecommons.org/licenses/by/4.0/";
    private const string LicenceScl =
        "http://data.legilux.public.lu/resource/authority/license/licenceSCL";

    [Fact]
    public void Default_admission_value_denies_public_text()
    {
        Assert.Equal(PublicTextAdmission.LicenceUnresolved,
            default(PublicTextAdmission));
        Assert.NotEqual(PublicTextAdmission.Admitted,
            default(PublicTextAdmission));
    }

    [Fact]
    public void Unknown_comparison_value_is_unresolved()
    {
        var sparqlCcBy = LicenceChannelEvidence.Present([
            UriClaim(CreativeCommonsBy40),
        ]);
        var fileCcBy = LicenceChannelEvidence.Present([
            FileClaim("CC-BY-4.0", CreativeCommonsBy40),
        ]);
        var evidence = EvidenceWithComparison(
            sparqlCcBy, fileCcBy, (LicenceComparison)int.MaxValue);

        Assert.Equal((LicenceComparison)int.MaxValue, evidence.Comparison);
        AssertDenied(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(evidence));
    }

    [Fact]
    public void Agreed_marker_cannot_override_an_invalid_channel()
    {
        var invalid = LicenceChannelEvidence.Invalid([
            new LicenceClaim("literal", "unknown", null),
        ]);
        var fileCcBy = LicenceChannelEvidence.Present([
            FileClaim("CC-BY-4.0", CreativeCommonsBy40),
        ]);
        var evidence = EvidenceWithComparison(
            invalid, fileCcBy, LicenceComparison.Agreed);

        AssertDenied(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(evidence));
    }

    [Fact]
    public void Conflict_marker_cannot_turn_invalid_data_into_observed_disagreement()
    {
        var invalid = LicenceChannelEvidence.Invalid([
            new LicenceClaim("literal", "unknown", null),
        ]);
        var evidence = EvidenceWithComparison(
            invalid,
            LicenceChannelEvidence.Present([
                FileClaim("CC-BY-4.0", CreativeCommonsBy40),
            ]),
            LicenceComparison.LicenceConflict);

        AssertDenied(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(evidence));
    }

    [Fact]
    public void Agreed_marker_cannot_override_channel_disagreement()
    {
        var evidence = EvidenceWithComparison(
            LicenceChannelEvidence.Present([
                UriClaim(CreativeCommonsBy40),
            ]),
            LicenceChannelEvidence.Present([
                FileClaim("licenceSCL", LicenceScl),
            ]),
            LicenceComparison.Agreed);

        AssertDenied(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(evidence));
    }

    [Fact]
    public void Literal_claims_with_exact_uri_are_not_agreed()
    {
        var literal = LicenceChannelEvidence.Present([
            new LicenceClaim("literal", CreativeCommonsBy40, CreativeCommonsBy40),
        ]);

        Assert.Equal(LicenceComparison.LicenceUnresolved,
            LegiluxLicenceContract.Compare(literal, literal, out var agreedUris));
        Assert.Empty(agreedUris);
    }

    [Fact]
    public void Agreed_marker_cannot_admit_literal_claims_with_exact_uri()
    {
        var literal = LicenceChannelEvidence.Present([
            new LicenceClaim("literal", CreativeCommonsBy40, CreativeCommonsBy40),
        ]);
        var evidence = EvidenceWithComparison(
            literal, literal, LicenceComparison.Agreed);

        AssertDenied(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(evidence));
    }

    [Theory]
    [InlineData(true, "uri", "http://publisher.example/not-the-licence", CreativeCommonsBy40)]
    [InlineData(true, "uri", "ftp://creativecommons.org/licenses/by/4.0/",
        "ftp://creativecommons.org/licenses/by/4.0/")]
    [InlineData(false, "token", "CC-BY-4.0", LicenceScl)]
    [InlineData(false, "unknown", "CC-BY-4.0", CreativeCommonsBy40)]
    public void Channel_claim_integrity_mismatches_are_unresolved(
        bool corruptSparql,
        string termType,
        string value,
        string licenceUri)
    {
        var validSparql = LicenceChannelEvidence.Present([
            UriClaim(CreativeCommonsBy40),
        ]);
        var validFile = LicenceChannelEvidence.Present([
            FileClaim("CC-BY-4.0", CreativeCommonsBy40),
        ]);
        var corrupt = LicenceChannelEvidence.Present([
            new LicenceClaim(termType, value, licenceUri),
        ]);
        var sparql = corruptSparql ? corrupt : validSparql;
        var file = corruptSparql ? validFile : corrupt;

        Assert.Equal(LicenceComparison.LicenceUnresolved,
            LegiluxLicenceContract.Compare(sparql, file, out var agreedUris));
        Assert.Empty(agreedUris);
        AssertDenied(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(EvidenceWithComparison(
                sparql, file, LicenceComparison.Agreed)));
    }

    [Fact]
    public void Conflict_marker_disagreeing_with_valid_channels_is_unresolved()
    {
        var sparqlCcBy = LicenceChannelEvidence.Present([
            UriClaim(CreativeCommonsBy40),
        ]);
        var fileCcBy = LicenceChannelEvidence.Present([
            FileClaim("CC-BY-4.0", CreativeCommonsBy40),
        ]);
        var evidence = EvidenceWithComparison(
            sparqlCcBy, fileCcBy, LicenceComparison.LicenceConflict);

        AssertDenied(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(evidence));
    }

    [Fact]
    public void Exact_sparql_uri_and_file_token_cc_by_are_admitted()
    {
        var evidence = Evidence(
            LicenceChannelEvidence.Present([UriClaim(CreativeCommonsBy40)]),
            LicenceChannelEvidence.Present([
                FileClaim("CC-BY-4.0", CreativeCommonsBy40),
            ]));

        var result = LicencePublicAdmission.Assess(evidence);

        Assert.Equal(PublicTextAdmission.Admitted, result.Outcome);
        Assert.Equal(CreativeCommonsBy40, result.BasisUri);
    }

    [Theory]
    [InlineData("http://creativecommons.org/licenses/by/4.0/")]
    [InlineData("https://creativecommons.org/licenses/by/4.0/")]
    public void Sparql_cc_by_forms_map_to_the_canonical_public_basis(string value)
    {
        Assert.Equal(CreativeCommonsBy40,
            LegiluxLicenceContract.MapSparqlTerm("uri", value));
    }

    [Fact]
    public void File_cc_by_token_maps_to_the_canonical_public_basis()
    {
        Assert.Equal(CreativeCommonsBy40,
            LegiluxLicenceContract.MapFileToken("CC-BY-4.0"));
    }

    [Fact]
    public void Https_sparql_uri_and_file_token_cc_by_are_admitted()
    {
        const string httpsCcBy =
            "https://creativecommons.org/licenses/by/4.0/";
        var evidence = Evidence(
            LicenceChannelEvidence.Present([
                new LicenceClaim("uri", httpsCcBy, CreativeCommonsBy40),
            ]),
            LicenceChannelEvidence.Present([
                FileClaim("CC-BY-4.0", CreativeCommonsBy40),
            ]));

        var result = LicencePublicAdmission.Assess(evidence);

        Assert.Equal(PublicTextAdmission.Admitted, result.Outcome);
        Assert.Equal(CreativeCommonsBy40, result.BasisUri);
    }

    [Fact]
    public void Channel_disagreement_is_a_conflict_and_has_no_basis()
    {
        var evidence = Evidence(
            LicenceChannelEvidence.Present([UriClaim(CreativeCommonsBy40)]),
            LicenceChannelEvidence.Present([FileClaim("licenceSCL", LicenceScl)]));

        Assert.Equal(LicenceComparison.LicenceConflict, evidence.Comparison);
        var result = LicencePublicAdmission.Assess(evidence);

        AssertDenied(PublicTextAdmission.LicenceConflict, result);
    }

    [Fact]
    public void Admission_compares_the_complete_validated_channel_sets()
    {
        var evidence = Evidence(
            LicenceChannelEvidence.Present([
                UriClaim(CreativeCommonsBy40),
                UriClaim(LicenceScl),
            ]),
            LicenceChannelEvidence.Present([
                FileClaim("CC-BY-4.0", CreativeCommonsBy40),
            ]));

        AssertDenied(PublicTextAdmission.LicenceConflict,
            LicencePublicAdmission.Assess(evidence));
    }

    [Fact]
    public void Missing_invalid_or_unobserved_channel_is_unresolved()
    {
        var awaiting = ManifestationLicenceEvidence.AwaitingFile(
            "https://publisher.example/m", "https://publisher.example/file",
            LicenceChannelEvidence.Present([UriClaim(CreativeCommonsBy40)]));
        var invalid = Evidence(
            LicenceChannelEvidence.Invalid([new LicenceClaim("literal", "unknown", null)]),
            LicenceChannelEvidence.Invalid([new LicenceClaim("token", "unknown", null)]));

        AssertDenied(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(null));
        AssertDenied(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(awaiting));
        AssertDenied(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(invalid));
    }

    [Fact]
    public void Both_absent_channels_are_unresolved()
    {
        AssertDenied(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(Evidence(
                LicenceChannelEvidence.Absent, LicenceChannelEvidence.Absent)));
    }

    [Fact]
    public void Missing_sparql_channel_is_unresolved_not_conflict()
    {
        var fileCcBy = LicenceChannelEvidence.Present([
            FileClaim("CC-BY-4.0", CreativeCommonsBy40),
        ]);

        Assert.Equal(LicenceComparison.LicenceUnresolved,
            LegiluxLicenceContract.Compare(
                LicenceChannelEvidence.Absent, fileCcBy, out var agreedUris));
        Assert.Empty(agreedUris);
        AssertDenied(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(Evidence(
                LicenceChannelEvidence.Absent, fileCcBy)));
    }

    [Fact]
    public void Missing_file_channel_is_unresolved_not_conflict()
    {
        var sparqlCcBy = LicenceChannelEvidence.Present([
            UriClaim(CreativeCommonsBy40),
        ]);

        Assert.Equal(LicenceComparison.LicenceUnresolved,
            LegiluxLicenceContract.Compare(
                sparqlCcBy, LicenceChannelEvidence.Absent, out var agreedUris));
        Assert.Empty(agreedUris);
        AssertDenied(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(Evidence(
                sparqlCcBy, LicenceChannelEvidence.Absent)));
    }

    [Fact]
    public void Agreed_unreviewed_or_ambiguous_basis_is_unsupported()
    {
        var scl = Evidence(
            LicenceChannelEvidence.Present([UriClaim(LicenceScl)]),
            LicenceChannelEvidence.Present([FileClaim("licenceSCL", LicenceScl)]));
        var ambiguous = Evidence(
            LicenceChannelEvidence.Present([
                UriClaim(CreativeCommonsBy40),
                UriClaim(LicenceScl),
            ]),
            LicenceChannelEvidence.Present([
                FileClaim("CC-BY-4.0", CreativeCommonsBy40),
                FileClaim("licenceSCL", LicenceScl),
            ]));

        AssertDenied(PublicTextAdmission.LicenceUnsupported,
            LicencePublicAdmission.Assess(scl));
        AssertDenied(PublicTextAdmission.LicenceUnsupported,
            LicencePublicAdmission.Assess(ambiguous));
    }

    [Theory]
    [InlineData("http://creativecommons.org/licenses/by/4.0")]
    [InlineData("https://creativecommons.org/licenses/by/4.0")]
    [InlineData("https://creativecommons.org/licenses/by/4.0/?source=other")]
    [InlineData("http://creativecommons.org/licenses/by/3.0/")]
    [InlineData("http://creativecommons.org/licenses/by-sa/4.0/")]
    [InlineData("http://CREATIVECOMMONS.ORG/licenses/by/4.0/")]
    public void Cc_by_uri_near_misses_are_unresolved(string licenceUri)
    {
        var evidence = EvidenceWithComparison(
            LicenceChannelEvidence.Present([UriClaim(licenceUri)]),
            LicenceChannelEvidence.Present([
                FileClaim("CC-BY-4.0", licenceUri),
            ]),
            LicenceComparison.Agreed);

        AssertDenied(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(evidence));
    }

    private static LicenceClaim UriClaim(string uri) =>
        new("uri", uri, uri);

    private static LicenceClaim FileClaim(string token, string uri) =>
        new("token", token, uri);

    private static ManifestationLicenceEvidence Evidence(
        LicenceChannelEvidence sparql,
        LicenceChannelEvidence file) =>
        ManifestationLicenceEvidence.AwaitingFile(
                "https://publisher.example/m", "https://publisher.example/file", sparql)
            .WithFile(file);

    private static ManifestationLicenceEvidence EvidenceWithComparison(
        LicenceChannelEvidence sparql,
        LicenceChannelEvidence file,
        LicenceComparison comparison)
    {
        var constructor = typeof(ManifestationLicenceEvidence)
            .GetConstructor(
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic,
                binder: null,
                [
                    typeof(string),
                    typeof(string),
                    typeof(LicenceChannelEvidence),
                    typeof(LicenceChannelEvidence),
                    typeof(LicenceComparison),
                ],
                modifiers: null);
        Assert.NotNull(constructor);
        return (ManifestationLicenceEvidence)constructor.Invoke([
            "https://publisher.example/m",
            "https://publisher.example/file",
            sparql,
            file,
            comparison,
        ]);
    }

    private static void AssertDenied(
        PublicTextAdmission expectedOutcome,
        LicenceAdmissionResult result)
    {
        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Null(result.BasisUri);
    }
}
