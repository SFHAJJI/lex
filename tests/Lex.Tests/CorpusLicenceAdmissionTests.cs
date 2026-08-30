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

        Assert.Equal(PublicTextAdmission.LicenceConflict, result.Outcome);
        Assert.Null(result.BasisUri);
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

        Assert.Equal(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(null).Outcome);
        Assert.Equal(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(awaiting).Outcome);
        Assert.Equal(PublicTextAdmission.LicenceUnresolved,
            LicencePublicAdmission.Assess(invalid).Outcome);
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

        Assert.Equal(PublicTextAdmission.LicenceUnsupported,
            LicencePublicAdmission.Assess(scl).Outcome);
        Assert.Equal(PublicTextAdmission.LicenceUnsupported,
            LicencePublicAdmission.Assess(ambiguous).Outcome);
    }

    private static LicenceClaim Claim(string value, string uri) =>
        new("uri", value, uri);

    private static ManifestationLicenceEvidence Evidence(
        LicenceChannelEvidence sparql,
        LicenceChannelEvidence file) =>
        ManifestationLicenceEvidence.AwaitingFile(
                "https://publisher.example/m", "https://publisher.example/file", sparql)
            .WithFile(file);
}
