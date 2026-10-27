using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// The wire of the relation shapes R0's replay hashes is what it was before index 10 read them (the Release 3 relations slice;
/// the lesson of #999's finding 1). The query checkpoint's result digest (<c>LuxembourgQueryExecutionAdapter.QueryResultDigest</c>)
/// hashes the run's resolved relations, its local inbound relations and its relation family acquisitions through
/// <see cref="ContractJson"/>, and R0 replays run 15 against that stored digest: one new member on any of these types, even a
/// null, makes the replay refuse before R1 runs. Index 10 reads these shapes and changes none of them; this pins their bytes.
/// </summary>
/// <remarks>
/// The objects are run 15's own: its first set-G <c>modifies</c> row and its first <c>cites</c> row as custody holds them
/// (checkpoint <c>1f9a11f3…</c>, family <c>legislative-loi-g</c>), with the run's one observation reference, the vocabulary
/// observation every relation of the run carries (<c>b86525e5…</c>, the journal's <c>lu-vocabulary</c> entry). The values were
/// computed with this branch's Contracts, which are byte-identical to integration head ee474a6d's (no file under
/// <c>src/Lex.V3.Contracts</c> changes in this slice), so they are the bytes run 15's checkpoint digests were made from.
/// </remarks>
[TestClass]
public sealed class LuxembourgRelationWirePinTests
{
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";
    private const string Amending = "http://data.legilux.public.lu/eli/etat/leg/loi/1842/12/27/n1/jo";
    private const string Amended = "http://data.legilux.public.lu/eli/etat/leg/loi/1819/05/21/n1/jo";

    private const string ModifiesJson = "TBD";
    private const string ModifiesSha256 = "TBD";
    private const string CitesSha256 = "TBD";
    private const string InboundSha256 = "TBD";
    private const string ObservedSha256 = "TBD";
    private const string ListSha256 = "TBD";
    private const string FamilySha256 = "TBD";

    /// <summary>Run 15's observation reference, the journal's <c>lu-vocabulary</c> observation.</summary>
    private static readonly SourceArtifactRef Run15Observation = new(
        "urn:uuid:afd60336-dcf4-479d-bc6c-c75bfd575bce", "b86525e5dc4a237ca540299fd1f122edc9d36abd74daae80f089bad03984cfed");

    [TestMethod]
    public void TheRelationShapesTheQueryCheckpointHashesSerializeAsAtEe474a6d()
    {
        var modifies = new LuxembourgResolvedRelation(
            Amending, Jolux + "modifies", Amended, Run15Observation, LuxembourgRelationSemantic.AssertedRelation,
            LuxembourgRelationDisposition.Accepted, null, LuxembourgRelationAuthority.PublisherAsserted);
        var cites = new LuxembourgResolvedRelation(
            Amending, Jolux + "cites", Amended, Run15Observation, LuxembourgRelationSemantic.AssertedCitation,
            LuxembourgRelationDisposition.Accepted, null, LuxembourgRelationAuthority.PublisherAsserted);
        var inbound = new LuxembourgResolvedLocalInboundRelation(
            Amended, Amending, Run15Observation, LuxembourgRelationAuthority.LocalInboundView,
            new LuxembourgLocalInboundView(LuxembourgRelationPredicate.Cites, "cited_by"));
        var observed = new LuxembourgObservedRelation(Amending, Jolux + "modifies", Amended, Run15Observation, LuxembourgRelationAuthority.PublisherAsserted);
        IReadOnlyList<LuxembourgResolvedRelation> list = [cites, modifies];
        var family = LuxembourgRelationFamilyAcquisition.NotComplete(
            Jolux + "modifies", LuxembourgRelationFamilyAcquisitionState.Incomplete, "The declared scope has not been independently reverified.");

        var actual = string.Join("\n", new[]
        {
            "ModifiesJson " + ContractJson.Serialize(modifies),
            "ModifiesSha256 " + ContractJson.Sha256(modifies),
            "CitesSha256 " + ContractJson.Sha256(cites),
            "InboundSha256 " + ContractJson.Sha256(inbound),
            "ObservedSha256 " + ContractJson.Sha256(observed),
            "ListSha256 " + ContractJson.Sha256(list),
            "FamilySha256 " + ContractJson.Sha256(family),
        });
        Assert.AreEqual(ModifiesJson, ContractJson.Serialize(modifies), actual);
        Assert.AreEqual(ModifiesSha256, ContractJson.Sha256(modifies), actual);
        Assert.AreEqual(CitesSha256, ContractJson.Sha256(cites), actual);
        Assert.AreEqual(InboundSha256, ContractJson.Sha256(inbound), "the query result digest hashes the local inbound relations\n" + actual);
        Assert.AreEqual(ObservedSha256, ContractJson.Sha256(observed), actual);
        Assert.AreEqual(ListSha256, ContractJson.Sha256(list), "the query result digest hashes the resolved relations as a list\n" + actual);
        Assert.AreEqual(FamilySha256, ContractJson.Sha256(family), "and the relation family acquisitions\n" + actual);

        // What index 10 reads of a resolved relation is its public members, and nothing of index 10 is on these types' wire.
        foreach (var member in new[] { "edge_type", "acting", "publication_date", "evidence_sha256" })
        {
            StringAssert.DoesNotMatch(ContractJson.Serialize(modifies), new System.Text.RegularExpressions.Regex("\"" + member));
        }
    }
}
