using System.Text;
using Lex.Law;
using Lex.Sources.Legilux;

namespace Lex.Tests;

public sealed class LegiluxLicenceEvidenceTests
{
    private const string Manifestation =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/n1/consolidation/20200101/fra/xml";
    private const string OtherManifestation =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2020/01/01/n1/consolidation/20200101/deu/xml";
    private const string CreativeCommons = "http://creativecommons.org/licenses/by/4.0/";
    private const string Scl =
        "http://data.legilux.public.lu/resource/authority/license/licenceSCL";
    private const string ManifestationFile =
        "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2020/01/01/n1.xml";

    [Fact]
    public void Sparql_results_preserve_term_type_and_exact_decoded_value()
    {
        var json = Encoding.UTF8.GetBytes("""
            {"head":{"vars":["license"]},"results":{"bindings":[
              {"license":{"type":"uri","value":"http://example.test/licence/%C3%A9"}},
              {"license":{"type":"literal","value":"licenceSCL é"}}
            ]}}
            """);

        var rows = SparqlClient.ParseSelectResponse(json);

        Assert.Equal("uri", rows[0]["license"].Type);
        Assert.Equal("http://example.test/licence/%C3%A9", rows[0]["license"].Value);
        Assert.Equal("literal", rows[1]["license"].Type);
        Assert.Equal("licenceSCL é", rows[1]["license"].Value);
    }

    [Fact]
    public void Sparql_channel_rejects_any_non_uri_term_without_erasing_it()
    {
        var channel = LegiluxLicenceEvidence.FromSparqlTerms([
            new SparqlTerm("uri", CreativeCommons),
            new SparqlTerm("literal", Scl),
        ]);

        Assert.Equal(LicenceChannelState.Invalid, channel.State);
        Assert.Collection(channel.Claims,
            claim =>
            {
                Assert.Equal("literal", claim.TermType);
                Assert.Equal(Scl, claim.Value);
                Assert.Null(claim.LicenceUri);
            },
            claim =>
            {
                Assert.Equal("uri", claim.TermType);
                Assert.Equal(CreativeCommons, claim.Value);
                Assert.Equal(CreativeCommons, claim.LicenceUri);
            });
    }

    [Fact]
    public void Completed_sparql_query_with_no_terms_is_absent()
    {
        var channel = LegiluxLicenceEvidence.FromSparqlTerms([]);

        Assert.Equal(LicenceChannelState.Absent, channel.State);
        Assert.Empty(channel.Claims);
    }

    [Fact]
    public void File_channel_maps_closed_tokens_as_a_set_for_the_exact_manifestation()
    {
        var channel = LegiluxLicenceEvidence.FromAkomaNtoso(
            AkomaNtoso(Manifestation, $$"""
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{OtherManifestation}}</scl:jolux>
                  <scl:jolux scl:name="license">licenceSCL</scl:jolux>
                </scl:JOLUXManifestation>
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{Manifestation}}</scl:jolux>
                  <scl:jolux scl:name="license">licenceSCL</scl:jolux>
                  <scl:jolux scl:name="license">CC-BY-4.0</scl:jolux>
                </scl:JOLUXManifestation>
                """), Manifestation);

        Assert.Equal(LicenceChannelState.Present, channel.State);
        Assert.Collection(channel.Claims,
            claim =>
            {
                Assert.Equal("token", claim.TermType);
                Assert.Equal("CC-BY-4.0", claim.Value);
                Assert.Equal(CreativeCommons, claim.LicenceUri);
            },
            claim =>
            {
                Assert.Equal("token", claim.TermType);
                Assert.Equal("licenceSCL", claim.Value);
                Assert.Equal(Scl, claim.LicenceUri);
            });
    }

    [Fact]
    public void File_channel_is_invalid_when_uriThis_does_not_match_the_fetched_manifestation()
    {
        var channel = LegiluxLicenceEvidence.FromAkomaNtoso(
            AkomaNtoso(Manifestation, $$"""
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{OtherManifestation}}</scl:jolux>
                  <scl:jolux scl:name="license">CC-BY-4.0</scl:jolux>
                </scl:JOLUXManifestation>
                """), Manifestation);

        Assert.Equal(LicenceChannelState.Invalid, channel.State);
        Assert.Empty(channel.Claims);
    }

    [Fact]
    public void File_channel_is_invalid_when_FRBRthis_does_not_match_the_fetched_manifestation()
    {
        var channel = LegiluxLicenceEvidence.FromAkomaNtoso(
            AkomaNtoso(OtherManifestation, $$"""
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{Manifestation}}</scl:jolux>
                  <scl:jolux scl:name="license">CC-BY-4.0</scl:jolux>
                </scl:JOLUXManifestation>
                """), Manifestation);

        Assert.Equal(LicenceChannelState.Invalid, channel.State);
    }

    [Fact]
    public void File_channel_rejects_an_unbound_Akoma_Ntoso_namespace()
    {
        var xml = Encoding.UTF8.GetBytes($$"""
            <akomaNtoso xmlns="https://attacker.invalid/akn"
                         xmlns:scl="http://www.scl.lu">
              <act><meta><identification>
                <FRBRManifestation><FRBRthis value="{{Manifestation}}" /></FRBRManifestation>
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{Manifestation}}</scl:jolux>
                  <scl:jolux scl:name="license">CC-BY-4.0</scl:jolux>
                </scl:JOLUXManifestation>
              </identification></meta></act>
            </akomaNtoso>
            """);

        var channel = LegiluxLicenceEvidence.FromAkomaNtoso(xml, Manifestation);

        Assert.Equal(LicenceChannelState.Invalid, channel.State);
    }

    [Fact]
    public void File_channel_rejects_duplicate_FRBRthis_identity()
    {
        var xml = Encoding.UTF8.GetBytes($$"""
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13"
                         xmlns:scl="http://www.scl.lu">
              <act><meta><identification>
                <FRBRManifestation>
                  <FRBRthis value="{{Manifestation}}" />
                  <FRBRthis value="{{Manifestation}}" />
                </FRBRManifestation>
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{Manifestation}}</scl:jolux>
                  <scl:jolux scl:name="license">CC-BY-4.0</scl:jolux>
                </scl:JOLUXManifestation>
              </identification></meta></act>
            </akomaNtoso>
            """);

        var channel = LegiluxLicenceEvidence.FromAkomaNtoso(xml, Manifestation);

        Assert.Equal(LicenceChannelState.Invalid, channel.State);
    }

    [Fact]
    public void File_channel_rejects_any_ambiguous_manifestation_block()
    {
        var channel = LegiluxLicenceEvidence.FromAkomaNtoso(
            AkomaNtoso(Manifestation, $$"""
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{Manifestation}}</scl:jolux>
                  <scl:jolux scl:name="license">CC-BY-4.0</scl:jolux>
                </scl:JOLUXManifestation>
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{OtherManifestation}}</scl:jolux>
                  <scl:jolux scl:name="uriThis">{{Manifestation}}</scl:jolux>
                  <scl:jolux scl:name="license">CC-BY-4.0</scl:jolux>
                </scl:JOLUXManifestation>
                """), Manifestation);

        Assert.Equal(LicenceChannelState.Invalid, channel.State);
    }

    [Fact]
    public void File_channel_rejects_duplicate_licence_declarations()
    {
        var channel = LegiluxLicenceEvidence.FromAkomaNtoso(
            AkomaNtoso(Manifestation, $$"""
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{Manifestation}}</scl:jolux>
                  <scl:jolux scl:name="license">CC-BY-4.0</scl:jolux>
                  <scl:jolux scl:name="license">CC-BY-4.0</scl:jolux>
                </scl:JOLUXManifestation>
                """), Manifestation);

        Assert.Equal(LicenceChannelState.Invalid, channel.State);
    }

    [Fact]
    public void File_channel_rejects_manifestation_bindings_outside_identification()
    {
        var xml = Encoding.UTF8.GetBytes($$"""
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13"
                         xmlns:scl="http://www.scl.lu">
              <act><meta><identification>
                <FRBRManifestation><FRBRthis value="{{Manifestation}}" /></FRBRManifestation>
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{Manifestation}}</scl:jolux>
                  <scl:jolux scl:name="license">CC-BY-4.0</scl:jolux>
                </scl:JOLUXManifestation>
              </identification></meta>
              <body><scl:JOLUXManifestation>
                <scl:jolux scl:name="uriThis">{{Manifestation}}</scl:jolux>
                <scl:jolux scl:name="license">licenceSCL</scl:jolux>
              </scl:JOLUXManifestation></body>
              </act>
            </akomaNtoso>
            """);

        var channel = LegiluxLicenceEvidence.FromAkomaNtoso(xml, Manifestation);

        Assert.Equal(LicenceChannelState.Invalid, channel.State);
    }

    [Fact]
    public void File_channel_is_absent_only_after_the_matching_block_was_observed()
    {
        var channel = LegiluxLicenceEvidence.FromAkomaNtoso(
            AkomaNtoso(Manifestation, $$"""
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{Manifestation}}</scl:jolux>
                </scl:JOLUXManifestation>
                """), Manifestation);

        Assert.Equal(LicenceChannelState.Absent, channel.State);
    }

    [Fact]
    public void File_channel_is_invalid_for_unknown_tokens_and_preserves_them()
    {
        var channel = LegiluxLicenceEvidence.FromAkomaNtoso(
            AkomaNtoso(Manifestation, $$"""
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{Manifestation}}</scl:jolux>
                  <scl:jolux scl:name="license">futureLicence</scl:jolux>
                </scl:JOLUXManifestation>
                """), Manifestation);

        Assert.Equal(LicenceChannelState.Invalid, channel.State);
        var claim = Assert.Single(channel.Claims);
        Assert.Equal("futureLicence", claim.Value);
        Assert.Null(claim.LicenceUri);
    }

    [Fact]
    public void File_channel_prohibits_DTD_processing()
    {
        var xml = Encoding.UTF8.GetBytes($$"""
            <!DOCTYPE akomaNtoso [<!ENTITY licence "CC-BY-4.0">]>
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0"
                         xmlns:scl="http://www.scl.lu">
              <act><meta><identification>
                <FRBRManifestation><FRBRthis value="{{Manifestation}}" /></FRBRManifestation>
                <scl:JOLUXManifestation>
                  <scl:jolux scl:name="uriThis">{{Manifestation}}</scl:jolux>
                  <scl:jolux scl:name="license">&licence;</scl:jolux>
                </scl:JOLUXManifestation>
              </identification></meta></act>
            </akomaNtoso>
            """);

        var channel = LegiluxLicenceEvidence.FromAkomaNtoso(xml, Manifestation);

        Assert.Equal(LicenceChannelState.Invalid, channel.State);
    }

    [Fact]
    public void Comparison_uses_complete_sets_instead_of_first_or_last_claim()
    {
        var sparql = LicenceChannelEvidence.Present([
            new LicenceClaim("uri", CreativeCommons, CreativeCommons),
            new LicenceClaim("uri", Scl, Scl),
        ]);
        var file = LicenceChannelEvidence.Present([
            new LicenceClaim("token", "licenceSCL", Scl),
            new LicenceClaim("token", "CC-BY-4.0", CreativeCommons),
        ]);

        var evidence = ManifestationLicenceEvidence.AwaitingFile(
                Manifestation, ManifestationFile, sparql)
            .WithFile(file);

        Assert.Equal(LicenceComparison.Agreed, evidence.Comparison);
        Assert.Equal(2, evidence.Sparql.Claims.Count);
        Assert.Equal(2, evidence.File.Claims.Count);
    }

    [Fact]
    public void Comparison_types_disagreement_and_uncertainty_separately()
    {
        var present = LicenceChannelEvidence.Present([
            new LicenceClaim("uri", CreativeCommons, CreativeCommons),
        ]);

        Assert.Equal(LicenceComparison.LicenceConflict,
            ManifestationLicenceEvidence.AwaitingFile(
                    Manifestation, ManifestationFile, present)
                .WithFile(LicenceChannelEvidence.Absent).Comparison);
        Assert.Equal(LicenceComparison.LicenceUnresolved,
            ManifestationLicenceEvidence.AwaitingFile(
                    Manifestation, ManifestationFile, LicenceChannelEvidence.Absent)
                .WithFile(LicenceChannelEvidence.Absent).Comparison);
        Assert.Equal(LicenceComparison.LicenceUnresolved,
            ManifestationLicenceEvidence.AwaitingFile(
                Manifestation, ManifestationFile, present).Comparison);
        Assert.Equal(LicenceChannelState.NotObserved,
            ManifestationLicenceEvidence.AwaitingFile(
                Manifestation, ManifestationFile, present).File.State);
        Assert.Equal(LicenceComparison.LicenceUnresolved,
            ManifestationLicenceEvidence.AwaitingFile(
                    Manifestation, ManifestationFile, present)
                .WithFile(LicenceChannelEvidence.Invalid([])).Comparison);
    }

    [Fact]
    public void Comparison_does_not_ignore_middle_claims_in_a_set()
    {
        var first = "https://example.test/licence/a";
        var second = "https://example.test/licence/b";
        var otherSecond = "https://example.test/licence/c";
        var last = "https://example.test/licence/d";
        var sparql = LicenceChannelEvidence.Present([
            new LicenceClaim("uri", first, first),
            new LicenceClaim("uri", second, second),
            new LicenceClaim("uri", last, last),
        ]);
        var file = LicenceChannelEvidence.Present([
            new LicenceClaim("uri", first, first),
            new LicenceClaim("uri", otherSecond, otherSecond),
            new LicenceClaim("uri", last, last),
        ]);

        var evidence = ManifestationLicenceEvidence.AwaitingFile(
                Manifestation, ManifestationFile, sparql)
            .WithFile(file);

        Assert.Equal(LicenceComparison.LicenceConflict, evidence.Comparison);
    }

    [Fact]
    public void Channel_factory_rejects_claim_sets_beyond_the_persistence_bound()
    {
        var claims = Enumerable.Range(0, LicenceChannelEvidence.MaximumClaims + 1)
            .Select(index => new LicenceClaim(
                "uri", $"https://example.test/licence/{index}",
                $"https://example.test/licence/{index}"));

        Assert.Throws<InvalidDataException>(() =>
            LicenceChannelEvidence.Invalid(claims));
    }

    [Fact]
    public void Channel_factory_rejects_values_beyond_the_persistence_bound()
    {
        var claim = new LicenceClaim(
            "token", new string('x', LicenceChannelEvidence.MaximumValueLength + 1), null);

        Assert.Throws<InvalidDataException>(() =>
            LicenceChannelEvidence.Invalid([claim]));
    }

    [Fact]
    public void Present_channel_rejects_an_empty_comparison_identifier()
    {
        Assert.Throws<ArgumentException>(() =>
            LicenceChannelEvidence.Present([
                new LicenceClaim("uri", CreativeCommons, " "),
            ]));
    }

    [Fact]
    public void Manifestation_evidence_rejects_unbounded_identifiers()
    {
        var present = LicenceChannelEvidence.Present([
            new LicenceClaim("uri", CreativeCommons, CreativeCommons),
        ]);

        Assert.Throws<InvalidDataException>(() =>
            ManifestationLicenceEvidence.AwaitingFile(
                new string('x', ManifestationLicenceEvidence.MaximumIdentifierLength + 1),
                ManifestationFile,
                present));
    }

    [Fact]
    public void Manifestation_transport_accepts_only_bound_formats_and_official_files()
    {
        var xml = LegiluxAdapter.OfficialManifestationTransport(
            new SparqlTerm("uri",
                "http://data.legilux.public.lu/resource/authority/user-format/xml"),
            new SparqlTerm("uri", ManifestationFile));

        Assert.Equal("xml", xml.Format);
        Assert.Equal(
            "https://legilux.public.lu/filestore/eli/etat/leg/loi/2020/01/01/n1.xml",
            xml.FetchUri);
        Assert.Throws<InvalidDataException>(() =>
            LegiluxAdapter.OfficialManifestationTransport(
                new SparqlTerm("uri",
                    "http://data.legilux.public.lu/resource/authority/user-format/svg"),
                new SparqlTerm("uri", ManifestationFile)));
        Assert.Throws<InvalidDataException>(() =>
            LegiluxAdapter.OfficialManifestationTransport(
                new SparqlTerm("uri",
                    "http://data.legilux.public.lu/resource/authority/user-format/xml"),
                new SparqlTerm("uri", "https://attacker.invalid/example.xml")));
    }

    [Fact]
    public void Manifestation_response_must_match_the_exact_requested_file()
    {
        const string expected =
            "https://legilux.public.lu/filestore/eli/etat/leg/loi/2020/01/01/n1.xml";

        Assert.Equal(expected,
            LegiluxAdapter.RequireOfficialResponseUri(expected, expected));
        Assert.Throws<InvalidDataException>(() =>
            LegiluxAdapter.RequireOfficialResponseUri(
                "https://legilux.public.lu/filestore/other.xml", expected));
        Assert.Throws<InvalidDataException>(() =>
            LegiluxAdapter.RequireOfficialResponseUri(
                "https://attacker.invalid/filestore/n1.xml", expected));
    }

    private static byte[] AkomaNtoso(string frbrThis, string manifestationBlocks) =>
        Encoding.UTF8.GetBytes($$"""
            <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13"
                         xmlns:scl="http://www.scl.lu">
              <act><meta><identification>
                <FRBRManifestation><FRBRthis value="{{frbrThis}}" /></FRBRManifestation>
                {{manifestationBlocks}}
              </identification></meta></act>
            </akomaNtoso>
            """);
}
