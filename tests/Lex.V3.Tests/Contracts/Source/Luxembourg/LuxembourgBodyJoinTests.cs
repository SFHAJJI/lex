using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

[TestClass]
public sealed class LuxembourgBodyJoinTests
{
    /// <summary>
    /// THIS TEST ASSERTED THE DEFECT. It was named
    /// <c>StructurallyConsistentTupleRetainsEveryCurrentMilestoneBlocker</c> and required that a
    /// structurally consistent tuple whose two rights channels AGREE on the admitting licence was
    /// still withheld, carrying all eight milestone blockers. That is how the Luxembourg body axis
    /// came to have no accepting path at all and every real manifest an accepted fraction of zero.
    /// Under the owner principle
    /// (RULING lex-event-20260904T205636383Z-e92b888b62c24df29fe3f8c1be5016f0) a law that can
    /// legitimately be ingested is ingested, so this candidate is accepted.
    /// </summary>
    [TestMethod]
    public void AStructurallyConsistentTupleWithAgreedRightsIsAccepted()
    {
        var topology = Topology(Candidate(ManifestationFrXml, LanguageFra, FormatXml));
        var result = Join(
            topology,
            Sparql(Observation(ManifestationFrXml, SparqlEvidence, [CcBy40])),
            InFile(Observation(ManifestationFrXml, InFileEvidence, [CcBy40])));

        var candidate = result.Candidates.Single();
        Assert.AreSame(topology.Candidates.Single(), candidate.WemiCandidate);
        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.AgreedSameRunCcBy,
            candidate.RightsResolution.Disposition);
        Assert.AreEqual(
            LuxembourgBodyCandidateDisposition.AcceptedCandidate,
            candidate.Disposition);
        Assert.IsEmpty(candidate.BlockerCodes);
        Assert.HasCount(0, result.RootBlockerCodes);
    }

    /// <summary>
    /// Rights that name another manifestation leave this one's licence UNKNOWN. Unknown is recorded
    /// and never a reason (owner principle), so the body is still accepted and the unresolved state
    /// travels on the rights resolution for the answer layer's Decision 58(a) disclosure. The old
    /// assertion here was that this withheld the body.
    /// </summary>
    [TestMethod]
    public void UnrelatedManifestationRightsLeaveTheLicenceUnknownWithoutWithholdingTheBody()
    {
        var result = Join(
            Topology(Candidate(ManifestationFrXml, LanguageFra, FormatXml)),
            Sparql(Observation(ManifestationDePdf, SparqlEvidence, [CcBy40])),
            InFile(Observation(ManifestationDePdf, InFileEvidence, [CcBy40])));

        var candidate = result.Candidates.Single();
        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.ChannelEnumerationUnproven,
            candidate.RightsResolution.Disposition);
        Assert.IsNull(candidate.RightsResolution.SparqlObservation);
        Assert.IsNull(candidate.RightsResolution.InFileObservation);
        Assert.IsEmpty(
            candidate.BlockerCodes,
            "an unresolved licence is an unknown, and unknown is never a reason to withhold.");
        Assert.AreEqual(LuxembourgBodyCandidateDisposition.AcceptedCandidate, candidate.Disposition);
    }

    [TestMethod]
    public void QuarantinedWemiAndNonAgreedRightsAddDistinctBlockersWithoutErasure()
    {
        var quarantined = new LuxembourgWemiCandidate(
            Root,
            ExpressionFr,
            ManifestationFrXml,
            ItemFrXml,
            LanguageFra,
            FormatXml,
            Run,
            LuxembourgWemiCandidateDisposition.TypedQuarantine,
            [LuxembourgWemiBlockerCode.ManifestationTypeMismatch]);
        var result = Join(
            Topology(quarantined),
            Sparql(Observation(ManifestationFrXml, SparqlEvidence, [CcBy40])),
            InFile(Observation(ManifestationFrXml, InFileEvidence, [LicenceScl])));

        var candidate = result.Candidates.Single();
        Assert.AreEqual(
            LuxembourgRightsChannelDisposition.Conflict,
            candidate.RightsResolution.Disposition);
        // The quarantined tuple withholds; the licence CONFLICT does not, because a conflict
        // between two channels is not the publisher marking the object not reusable, it is an
        // unresolved value, and unknown is never a reason.
        CollectionAssert.AreEqual(
            new[] { LuxembourgBodyBlockerCode.WemiTupleTypedQuarantine },
            candidate.BlockerCodes.ToArray());
        CollectionAssert.Contains(
            candidate.WemiCandidate.BlockerCodes.ToArray(),
            LuxembourgWemiBlockerCode.ManifestationTypeMismatch);
    }

    [TestMethod]
    public void ResultsAreDeterministicReadOnlyAndDoNotReorderInputs()
    {
        var german = Candidate(ManifestationDePdf, LanguageDeu, FormatPdf);
        var french = Candidate(ManifestationFrXml, LanguageFra, FormatXml);
        var topology = Topology(french, german);
        var sparqlRows = new[]
        {
            Observation(ManifestationFrXml, SparqlEvidence, [CcBy40]),
            Observation(ManifestationDePdf, OtherSparqlEvidence, [CcBy40]),
        };
        var inFileRows = new[]
        {
            Observation(ManifestationFrXml, InFileEvidence, [CcBy40]),
            Observation(ManifestationDePdf, OtherInFileEvidence, [CcBy40]),
        };
        var sparql = Sparql(sparqlRows);
        var inFile = InFile(inFileRows);
        var topologyBefore = topology.Candidates.ToArray();
        var sparqlBefore = sparql.Observations.ToArray();

        var first = Join(topology, sparql, inFile);
        sparqlRows[0] = Observation(ManifestationFrXml, InFileEvidence, [LicenceScl]);
        inFileRows[0] = Observation(ManifestationFrXml, SparqlEvidence, [LicenceScl]);
        var second = Join(topology, sparql, inFile);

        CollectionAssert.AreEqual(topologyBefore, topology.Candidates.ToArray());
        CollectionAssert.AreEqual(sparqlBefore, sparql.Observations.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                Signature(german),
                Signature(french),
            },
            first.Candidates.Select(row => Signature(row.WemiCandidate)).ToArray());
        CollectionAssert.AreEqual(
            first.Candidates.Select(CandidateSignature).ToArray(),
            second.Candidates.Select(CandidateSignature).ToArray());
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<LuxembourgBodyCandidateResolution>)first.Candidates).Clear());
    }

    [TestMethod]
    public void SameManifestationItemsBothSurviveAndSortByExactItemIdentity()
    {
        const string earlierItem =
            "http://data.legilux.public.lu/filestore/body-a.xml";
        const string laterItem =
            "http://data.legilux.public.lu/filestore/body-z.xml";
        var earlier = Candidate(ManifestationFrXml, LanguageFra, FormatXml, earlierItem);
        var later = Candidate(ManifestationFrXml, LanguageFra, FormatXml, laterItem);

        var result = Join(Topology(later, earlier), Sparql(), InFile());

        CollectionAssert.AreEqual(
            new[] { earlierItem, laterItem },
            result.Candidates.Select(candidate => candidate.WemiCandidate.ItemIri).ToArray());
    }

    [TestMethod]
    public void NoWemiTupleProducesTheStructuralRootBlocker()
    {
        var upstream = new LuxembourgWemiBlocker(
            LuxembourgWemiBlockerCode.RealizationMissing,
            Root,
            "http://data.legilux.public.lu/resource/ontology/jolux#isRealizedBy",
            string.Empty,
            string.Empty,
            string.Empty);
        var topology = new LuxembourgWemiTopologyResolution([], [upstream], [], [], []);

        var result = Join(topology, Sparql(), InFile());

        Assert.HasCount(0, result.Candidates);
        CollectionAssert.AreEqual(
            new[] { LuxembourgBodyRootBlockerCode.PublisherRealizationPathUnproven },
            result.RootBlockerCodes.ToArray());
        Assert.AreSame(upstream, result.WemiBlockers.Single());
    }

    [TestMethod]
    public void ExactRootAndRunMismatchesRemainCandidateBlockers()
    {
        var otherRootCandidate = new LuxembourgWemiCandidate(
            OtherRoot,
            OtherRoot + "/fr",
            OtherRoot + "/fr/xml",
            "http://data.legilux.public.lu/filestore/other.xml",
            LanguageFra,
            FormatXml,
            OtherRun,
            LuxembourgWemiCandidateDisposition.StructurallyConsistent,
            []);

        var result = Join(Topology(otherRootCandidate), Sparql(), InFile());

        // WemiObservationRunMismatch is gone with its only producer, which compared a candidate's
        // own observation ref against the ref the topology had just been resolved with and so could
        // not fail on any production path. The real binding check is per assertion in
        // LuxembourgWemiTopology (ObservationMismatch) and still fails here.
        CollectionAssert.Contains(
            result.Candidates.Single().BlockerCodes.ToArray(),
            LuxembourgBodyBlockerCode.WemiRootMismatch);
    }

    /// <summary>
    /// The blocker-free invariant still holds in both directions, and the join CAN now reach it.
    /// This test was named <c>EmptyBlockersAreExactlyAcceptedButNoPublicJoinPathCanCreateThem</c>
    /// and its closing assertion required that no public join path could ever produce an accepted
    /// candidate. That was an accurate description of the defect: the Luxembourg body axis had no
    /// accepting path at all. The invariant that an accepted candidate carries no blocker, and a
    /// withheld one carries at least one, is unchanged.
    /// </summary>
    [TestMethod]
    public void EmptyBlockersAreExactlyAcceptedAndTheJoinCanReachThem()
    {
        var wemi = Candidate(ManifestationFrXml, LanguageFra, FormatXml);
        var rights = LuxembourgRightsChannels.Resolve(
            ManifestationFrXml,
            Run,
            Sparql(Observation(ManifestationFrXml, SparqlEvidence, [CcBy40])),
            InFile(Observation(ManifestationFrXml, InFileEvidence, [CcBy40])));

        var accepted = new LuxembourgBodyCandidateResolution(
            wemi,
            rights,
            LuxembourgBodyCandidateDisposition.AcceptedCandidate,
            []);

        Assert.HasCount(0, accepted.BlockerCodes);
        Assert.ThrowsExactly<ArgumentException>(() =>
            new LuxembourgBodyCandidateResolution(
                wemi,
                rights,
                LuxembourgBodyCandidateDisposition.Withheld,
                []));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new LuxembourgBodyCandidateResolution(
                wemi,
                rights,
                LuxembourgBodyCandidateDisposition.AcceptedCandidate,
                [LuxembourgBodyBlockerCode.WemiTupleTypedQuarantine]));
        Assert.HasCount(0, typeof(LuxembourgBodyCandidateResolution).GetConstructors());

        var joined = Join(Topology(wemi), rights.SparqlObservations, rights.InFileObservations);
        Assert.IsTrue(
            joined.Candidates.Any(candidate =>
                candidate.Disposition == LuxembourgBodyCandidateDisposition.AcceptedCandidate),
            "a structurally consistent wording manifestation with agreed rights is exactly what "
            + "the owner principle says must be ingested.");
        Assert.IsFalse(typeof(LuxembourgBodyCandidateResolution)
            .GetProperties()
            .Any(property => property.Name.Contains("Role", StringComparison.OrdinalIgnoreCase)));
    }

    private static LuxembourgBodyJoinResolution Join(
        LuxembourgWemiTopologyResolution topology,
        LuxembourgSparqlRightsChannelObservations sparql,
        LuxembourgInFileRightsChannelObservations inFile) =>
        LuxembourgBodyJoin.Resolve(Root, Run, topology, sparql, inFile);

    private static LuxembourgWemiTopologyResolution Topology(
        params LuxembourgWemiCandidate[] candidates) => new(
        candidates,
        [],
        [],
        candidates.Select(static candidate => candidate.ExpressionIri)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray(),
        candidates.Select(static candidate => candidate.ManifestationIri)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray());

    private static LuxembourgWemiCandidate Candidate(
        string manifestationIri,
        string languageIri,
        string formatIri,
        string? itemIri = null) => new(
        Root,
        manifestationIri[..manifestationIri.LastIndexOf('/')],
        manifestationIri,
        itemIri ?? (manifestationIri == ManifestationFrXml ? ItemFrXml : ItemDePdf),
        languageIri,
        formatIri,
        Run,
        LuxembourgWemiCandidateDisposition.StructurallyConsistent,
        []);

    private static LuxembourgSparqlRightsChannelObservations Sparql(
        params LuxembourgRightsChannelObservation[] observations) => new(
        Run,
        SparqlEnumeration,
        observations);

    private static LuxembourgInFileRightsChannelObservations InFile(
        params LuxembourgRightsChannelObservation[] observations) => new(
        Run,
        InFileEnumeration,
        observations);

    private static LuxembourgRightsChannelObservation Observation(
        string manifestationIri,
        SourceArtifactRef evidenceRef,
        IReadOnlyList<string> licenceIris) => new(
        manifestationIri,
        Run,
        evidenceRef,
        licenceIris);

    private static string Signature(LuxembourgWemiCandidate candidate) =>
        $"{candidate.LanguageIri}|{candidate.FormatIri}|" +
        $"{candidate.ExpressionIri}|{candidate.ManifestationIri}|{candidate.ItemIri}";

    private static string CandidateSignature(LuxembourgBodyCandidateResolution candidate) =>
        $"{Signature(candidate.WemiCandidate)}|{(int)candidate.RightsResolution.Disposition}|" +
        string.Join(',', candidate.BlockerCodes.Select(static code => (int)code));

    private const string Root =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1/jo";
    private const string OtherRoot =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/02/a2/jo";
    private const string ExpressionFr = Root + "/fr";
    private const string ManifestationFrXml = ExpressionFr + "/xml";
    private const string ExpressionDe = Root + "/de";
    private const string ManifestationDePdf = ExpressionDe + "/pdf";
    private const string ItemFrXml =
        "http://data.legilux.public.lu/filestore/body-fr.xml";
    private const string ItemDePdf =
        "http://data.legilux.public.lu/filestore/body-de.pdf";
    private const string LanguageFra =
        "http://publications.europa.eu/resource/authority/language/FRA";
    private const string LanguageDeu =
        "http://publications.europa.eu/resource/authority/language/DEU";
    private const string FormatXml =
        "http://data.legilux.public.lu/resource/authority/user-format/xml";
    private const string FormatPdf =
        "http://data.legilux.public.lu/resource/authority/user-format/pdf";
    private const string CcBy40 = "http://creativecommons.org/licenses/by/4.0/";
    private const string LicenceScl =
        "http://data.legilux.public.lu/resource/authority/license/licenceSCL";

    private static SourceArtifactRef Run { get; } = Artifact(
        "cbe6e64c-789a-4c73-854d-19e464728a50",
        '1');
    private static SourceArtifactRef OtherRun { get; } = Artifact(
        "37530abe-232e-4ad0-a9ab-319d9f0823bb",
        '2');
    private static SourceArtifactRef SparqlEnumeration { get; } = Artifact(
        "388b94c5-c812-494a-8414-11659c742d7f",
        '3');
    private static SourceArtifactRef InFileEnumeration { get; } = Artifact(
        "43b2af70-9a13-4a0f-a202-f5bb00199239",
        '4');
    private static SourceArtifactRef SparqlEvidence { get; } = Artifact(
        "d48c632b-ad60-4972-a7da-1c35d0d5dc2f",
        '5');
    private static SourceArtifactRef InFileEvidence { get; } = Artifact(
        "50580b74-da52-484a-a6fa-82eb7489fd09",
        '6');
    private static SourceArtifactRef OtherSparqlEvidence { get; } = Artifact(
        "62cb9a09-5aa2-4d16-9583-3ad9d958081d",
        '7');
    private static SourceArtifactRef OtherInFileEvidence { get; } = Artifact(
        "1e959fe0-4f72-4273-a191-329ce00ada12",
        '8');

    private static SourceArtifactRef Artifact(string id, char digestCharacter) => new(
        "urn:uuid:" + id,
        new string(digestCharacter, 64));
}
