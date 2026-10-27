using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// The wire of a resolution /1, and of the body joins and Gazette dispositions that carry one, is what it was before reading
/// /2 existed (the review of #999, finding 1). The acquisition's checkpoints hash these shapes through
/// <see cref="ContractJson.Sha256"/>: the Gazette checkpoint's input and result digests (body joins, Gazette sets) and the
/// query checkpoint's result digest (Gazette sets). R0 replays run 15 against those stored digests, so one new member on the
/// wire, even a null, makes the replay refuse before R1 runs.
/// </summary>
/// <remarks>
/// Every pinned value was computed with the Contracts of integration head 473a9fae, built in a scratch worktree outside the
/// repository, by an F# script that builds exactly these objects through the same constructors and factories; the branch's
/// Contracts gave the same values once the /2-only members were kept off the wire when null.
/// </remarks>
[TestClass]
public sealed class LuxembourgRightsResolutionWirePinTests
{
    private const string AgreedSha256 = "1ac4c068222312bfe3dc5cab43b89cd5cfe5402dea1a2e8471660e82792f06e9";
    private const string RefusedSha256 = "c2c2aba24906afbc36592205ba03eee90c2e1a727010380db5995601cd57a8da";
    private const string JoinSha256 = "7042ba7d6c1b3405824b20dbefa841a4caadc2a3fa45c8738faa4b28a1551a9d";
    private const string DispositionSha256 = "2d0f8c5057378fba51115ff4a46d148502783a2762cc6bdfec5b744bd97a43f2";
    private const string SetSha256 = "f71383821524691d431ab2c9c0fac87cc142ddd6246538ddbec70cfa0d07483c";

    private const string CcBy = "http://creativecommons.org/licenses/by/4.0/";
    private const string Act = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo";
    private const string Fra = "http://publications.europa.eu/resource/authority/language/FRA";

    [TestMethod]
    public void AResolutionOneAndTheShapesCarryingItSerializeAsAt473a9fae()
    {
        var run = Artifact("cbe6e64c-789a-4c73-854d-19e464728a50", '1');
        var xml = Candidate("xml", "http://data.legilux.public.lu/resource/authority/user-format/xml", run);
        var pdf = Candidate("pdf", "http://data.legilux.public.lu/resource/authority/user-format/pdf", run);
        var topology = new LuxembourgWemiTopologyResolution(
            [xml, pdf], [], [], [Act + "/fr"],
            new[] { xml.ManifestationIri, pdf.ManifestationIri }.Order(StringComparer.Ordinal).ToArray());
        var sparqlEvidence = Artifact("5f4c1a2e-0b7d-4e7f-9c1a-2b3c4d5e6f70", '5');
        var sparql = new LuxembourgSparqlRightsChannelObservations(
            run, Artifact("388b94c5-c812-494a-8414-11659c742d7f", '3'),
            [
                new LuxembourgRightsChannelObservation(xml.ManifestationIri, run, sparqlEvidence, [CcBy]),
                new LuxembourgRightsChannelObservation(pdf.ManifestationIri, run, sparqlEvidence, [CcBy]),
            ]);
        // The in-file channel as the acquisition leaves it: the XML read CC BY, the PDF read and refused.
        var inFile = new LuxembourgInFileRightsChannelObservations(
            run, Artifact("43b2af70-9a13-4a0f-a202-f5bb00199239", '4'),
            [new LuxembourgRightsChannelObservation(xml.ManifestationIri, run, Artifact("6a5b2c3d-1e8f-4a0b-8d2c-3e4f5a6b7c81", '6'), [CcBy])],
            acquisitionCompleted: true,
            rejectedManifestationIris: [pdf.ManifestationIri]);

        var agreed = LuxembourgRightsChannels.Resolve(xml.ManifestationIri, run, sparql, inFile);
        var refused = LuxembourgRightsChannels.Resolve(pdf.ManifestationIri, run, sparql, inFile);
        var join = LuxembourgBodyJoin.Resolve(Act, run, topology, sparql, inFile);
        var disposition = LuxembourgGazetteBodyDisposition.Create(
            join.Candidates.Single(candidate => candidate.WemiCandidate.ManifestationIri == pdf.ManifestationIri), null);
        var set = LuxembourgGazetteBodySet.Create(join, [disposition]);

        Assert.AreEqual(LuxembourgRightsChannelDisposition.AgreedSameRunCcBy, agreed.Disposition);
        Assert.AreEqual(LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected, refused.Disposition);
        Assert.AreEqual(AgreedSha256, ContractJson.Sha256(agreed));
        Assert.AreEqual(RefusedSha256, ContractJson.Sha256(refused));
        Assert.AreEqual(JoinSha256, ContractJson.Sha256(join), "the Gazette input digest hashes body joins");
        Assert.AreEqual(DispositionSha256, ContractJson.Sha256(disposition));
        Assert.AreEqual(SetSha256, ContractJson.Sha256(set), "the Gazette and query result digests hash Gazette sets");

        foreach (var member in new[] { "in_file_reading", "resolution_rule", "licence_page" })
        {
            foreach (var resolution in new[] { agreed, refused })
            {
                StringAssert.DoesNotMatch(ContractJson.Serialize(resolution),
                    new System.Text.RegularExpressions.Regex("\"" + member),
                    "a resolution /1 carries no /2 member on the wire");
            }
        }
    }

    private static LuxembourgWemiCandidate Candidate(string format, string formatIri, SourceArtifactRef run) => new(
        Act,
        Act + "/fr",
        Act + "/fr/" + format,
        "http://data.legilux.public.lu/filestore/eli-etat-leg-loi-2026-01-01-a1-jo-fr." + format,
        Fra,
        formatIri,
        run,
        LuxembourgWemiCandidateDisposition.StructurallyConsistent,
        []);

    private static SourceArtifactRef Artifact(string id, char digestCharacter) => new("urn:uuid:" + id, new string(digestCharacter, 64));
}
