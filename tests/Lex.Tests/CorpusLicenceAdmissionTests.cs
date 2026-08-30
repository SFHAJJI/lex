using Lex.Ingest;
using Lex.Law;

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
        var ccBy = LicenceChannelEvidence.Present([
            Claim("CC-BY-4.0", CreativeCommonsBy40),
        ]);
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
        var evidence = (ManifestationLicenceEvidence)constructor.Invoke([
            "https://publisher.example/m",
            "https://publisher.example/file",
            ccBy,
            ccBy,
            (LicenceComparison)int.MaxValue,
        ]);

        Assert.Equal((LicenceComparison)int.MaxValue, evidence.Comparison);
        AssertDenied(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(evidence));
    }

    [Fact]
    public void Exact_agreed_cc_by_is_the_only_initially_admitted_basis()
    {
        var evidence = Evidence(
            LicenceChannelEvidence.Present([Claim("CC-BY-4.0", CreativeCommonsBy40)]),
            LicenceChannelEvidence.Present([Claim("CC-BY-4.0", CreativeCommonsBy40)]));

        var result = LicencePublicAdmission.Assess(evidence);

        Assert.Equal(PublicTextAdmission.Admitted, result.Outcome);
        Assert.Equal(CreativeCommonsBy40, result.BasisUri);
    }

    [Fact]
    public void Channel_disagreement_is_a_conflict_and_has_no_basis()
    {
        var evidence = Evidence(
            LicenceChannelEvidence.Present([Claim("CC-BY-4.0", CreativeCommonsBy40)]),
            LicenceChannelEvidence.Present([Claim("licenceSCL", LicenceScl)]));

        var result = LicencePublicAdmission.Assess(evidence);

        AssertDenied(PublicTextAdmission.LicenceConflict, result);
    }

    [Fact]
    public void Missing_invalid_or_unobserved_channel_is_unresolved()
    {
        var awaiting = ManifestationLicenceEvidence.AwaitingFile(
            "https://publisher.example/m", "https://publisher.example/file",
            LicenceChannelEvidence.Present([Claim("CC-BY-4.0", CreativeCommonsBy40)]));
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
    public void Absent_channel_never_admits_public_text()
    {
        var ccBy = LicenceChannelEvidence.Present([
            Claim("CC-BY-4.0", CreativeCommonsBy40),
        ]);

        AssertDenied(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(Evidence(
                LicenceChannelEvidence.Absent, LicenceChannelEvidence.Absent)));
        AssertDenied(PublicTextAdmission.LicenceConflict,
            LicencePublicAdmission.Assess(Evidence(
                LicenceChannelEvidence.Absent, ccBy)));
        AssertDenied(PublicTextAdmission.LicenceConflict,
            LicencePublicAdmission.Assess(Evidence(
                ccBy, LicenceChannelEvidence.Absent)));
    }

    [Fact]
    public void Agreed_unreviewed_or_ambiguous_basis_is_unsupported()
    {
        var scl = Evidence(
            LicenceChannelEvidence.Present([Claim("licenceSCL", LicenceScl)]),
            LicenceChannelEvidence.Present([Claim("licenceSCL", LicenceScl)]));
        var ambiguous = Evidence(
            LicenceChannelEvidence.Present([
                Claim("CC-BY-4.0", CreativeCommonsBy40),
                Claim("licenceSCL", LicenceScl),
            ]),
            LicenceChannelEvidence.Present([
                Claim("CC-BY-4.0", CreativeCommonsBy40),
                Claim("licenceSCL", LicenceScl),
            ]));

        AssertDenied(PublicTextAdmission.LicenceUnsupported,
            LicencePublicAdmission.Assess(scl));
        AssertDenied(PublicTextAdmission.LicenceUnsupported,
            LicencePublicAdmission.Assess(ambiguous));
    }

    [Theory]
    [InlineData("https://creativecommons.org/licenses/by/4.0/")]
    [InlineData("http://creativecommons.org/licenses/by/4.0")]
    [InlineData("http://creativecommons.org/licenses/by/3.0/")]
    [InlineData("http://creativecommons.org/licenses/by-sa/4.0/")]
    [InlineData("http://CREATIVECOMMONS.ORG/licenses/by/4.0/")]
    public void Agreed_cc_by_uri_near_misses_are_unsupported(string licenceUri)
    {
        var evidence = Evidence(
            LicenceChannelEvidence.Present([Claim("licence", licenceUri)]),
            LicenceChannelEvidence.Present([Claim("licence", licenceUri)]));

        AssertDenied(PublicTextAdmission.LicenceUnsupported,
            LicencePublicAdmission.Assess(evidence));
    }

    private static LicenceClaim Claim(string value, string uri) =>
        new("uri", value, uri);

    private static ManifestationLicenceEvidence Evidence(
        LicenceChannelEvidence sparql,
        LicenceChannelEvidence file) =>
        ManifestationLicenceEvidence.AwaitingFile(
                "https://publisher.example/m", "https://publisher.example/file", sparql)
            .WithFile(file);

    private static void AssertDenied(
        PublicTextAdmission expectedOutcome,
        LicenceAdmissionResult result)
    {
        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Null(result.BasisUri);
    }
}
