using System.Reflection;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Tests.Contracts.Source.Http;

/// <summary>
/// D1-06c-LU-2 fold-in three and the latent defect it travelled with: the per-route document-fetch
/// parameter declaration, and the canonical method token that must refuse an unknown member by name
/// rather than guessing.
/// </summary>
[TestClass]
public sealed class DocumentFetchRouteContractTests
{
    /// <summary>
    /// The declaration is per route and the plans read it rather than repeating it. EU declares the
    /// two observed negotiation headers in their observed order; LU declares one parameter that
    /// fills NO header, because that route negotiates nothing and its one carried value is the act's
    /// own ELI page path the robots ruling requires.
    /// </summary>
    [TestMethod]
    public void EachDocumentFetchRouteDeclaresItsOwnOrderedParameters()
    {
        var eu = DocumentFetchParameterContract.For(
            OfficialMachineQuerySourceProfileId.EuropeanUnionDocumentFetch);
        Assert.IsNotNull(eu);
        CollectionAssert.AreEqual(
            new[] { "eu_document_fetch_accept", "eu_document_fetch_accept_language" },
            eu!.Parameters.Select(static parameter => parameter.ParameterName).ToArray());
        CollectionAssert.AreEqual(
            new[] { "accept", "accept-language" },
            eu.Parameters.Select(static parameter => parameter.HeaderName).ToArray());

        var lu = DocumentFetchParameterContract.For(
            OfficialMachineQuerySourceProfileId.LuxembourgDocumentFetch);
        Assert.IsNotNull(lu);
        CollectionAssert.AreEqual(
            new[] { "lu_document_fetch_act_eli_page_path" },
            lu!.Parameters.Select(static parameter => parameter.ParameterName).ToArray());
        Assert.IsNull(
            lu.Parameters[0].HeaderName,
            "the Luxembourg route sends no negotiation header at all.");
    }

    /// <summary>
    /// Neither SPARQL POST channel declares a document-fetch contract, so the session cannot treat
    /// one as a GET route by accident, and an unknown profile id is refused rather than defaulted.
    /// </summary>
    [TestMethod]
    public void OnlyTheTwoDocumentFetchRoutesDeclareAContract()
    {
        Assert.IsNull(DocumentFetchParameterContract.For(
            OfficialMachineQuerySourceProfileId.LuxembourgSparql));
        Assert.IsNull(DocumentFetchParameterContract.For(
            OfficialMachineQuerySourceProfileId.EuropeanUnionSparql));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () =>
            DocumentFetchParameterContract.For((OfficialMachineQuerySourceProfileId)99));
    }

    /// <summary>
    /// Each plan builds its bound input FROM its own declaration, so the names the session verifies
    /// and the names the plan mints cannot drift apart. Read back through the declaration's own
    /// verifier, which is the exact code path the session runs.
    /// </summary>
    [TestMethod]
    public void EachPlanBindsExactlyTheParametersItsRouteDeclares()
    {
        var luAddress = LuxembourgDocumentFetchAddress.Create(
            LuxembourgFileUri.RequireValid(
                "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/"
                + "xml/eli-etat-leg-loi-2017-03-14-a439-jo-fr-xml.xml"),
            LuxembourgUserFormatToken.XmlAkomaNtoso,
            LuxembourgLegalValue.Officiel,
            "/eli/etat/leg/loi/2017/03/14/a439/jo");
        var bound = new LuxembourgDocumentFetchPlan(luAddress).Bind(
            "urn:uuid:00000000-0000-4000-9000-000000000101",
            "urn:uuid:00000000-0000-4000-9000-000000000102",
            RendererSource());

        Assert.IsTrue(
            LuxembourgDocumentFetchPlan.ParameterContract.TryReadDeclaredValues(
                bound.InputArtifact, out var values));
        CollectionAssert.AreEqual(
            new[] { "/eli/etat/leg/loi/2017/03/14/a439/jo" }, values.ToArray());

        // And the EU route's own declaration does not read a Luxembourg input: the verification is
        // by declaration, not by position alone.
        Assert.IsFalse(
            EuDocumentFetchPlan.ParameterContract.TryReadDeclaredValues(bound.InputArtifact, out _));
    }

    /// <summary>
    /// The latent defect this slice inherited: the conflict resolution that merged the two lanes
    /// replaced an exhaustive method-token helper with <c>Method == Get ? "GET" : "POST"</c>, so a
    /// third <see cref="HttpRequestMethod"/> member added later would have been canonicalized
    /// silently as POST and two different methods would have shared one profile digest. Refusal by
    /// name is restored and pinned here.
    /// </summary>
    [TestMethod]
    public void TheCanonicalMethodTokenRefusesAnUnknownMethodByNameRatherThanGuessing()
    {
        var methodToken = typeof(OfficialMachineQuerySourceProfile).GetMethod(
            "MethodToken", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(methodToken, "the canonical method token helper must exist to be pinned.");

        Assert.AreEqual("GET", methodToken!.Invoke(null, [HttpRequestMethod.Get]));
        Assert.AreEqual("POST", methodToken.Invoke(null, [HttpRequestMethod.Post]));

        var thrown = Assert.ThrowsExactly<TargetInvocationException>(() =>
            methodToken.Invoke(null, [(HttpRequestMethod)3]));
        Assert.IsInstanceOfType<ArgumentOutOfRangeException>(thrown.InnerException);
    }

    /// <summary>
    /// The corpus vocabulary's own remark says the five Luxembourg spellings agree with
    /// <see cref="LuxembourgDocumentGetOutcomeKind"/>'s. Asserted rather than left to the comment:
    /// every refusal member of the LU GET vocabulary has a corpus member of the identical name, and
    /// the one member that is not a refusal (Retrieved, the held path) has none.
    /// </summary>
    [TestMethod]
    public void CorpusAcquisitionRefusalReasonMirrorsTheLuxembourgDocumentGetVocabulary()
    {
        foreach (var kind in Enum.GetValues<LuxembourgDocumentGetOutcomeKind>())
        {
            var name = kind.ToString();
            if (kind == LuxembourgDocumentGetOutcomeKind.Retrieved)
            {
                Assert.IsFalse(
                    Enum.IsDefined(typeof(CorpusAcquisitionRefusalReason), name),
                    "a retrieval is not a refusal and must have no refusal member.");
                continue;
            }

            Assert.IsTrue(
                Enum.IsDefined(typeof(CorpusAcquisitionRefusalReason), name),
                $"the corpus vocabulary must carry a member named '{name}'.");
        }

        Assert.AreEqual(6, Enum.GetValues<LuxembourgDocumentGetOutcomeKind>().Length);
    }

    /// <summary>
    /// A minted manifest fetch address may omit the Accept pair, and only as a pair: a route either
    /// negotiates or it does not, and half a negotiation is a defect rather than a shape.
    /// </summary>
    [TestMethod]
    public void AMintedFetchAddressCarriesTheAcceptPairTogetherOrNotAtAll()
    {
        var negotiating = ScopeManifestFetchAddress.Minted(
            "publications.europa.eu", "/resource/cellar/x", "application/xhtml+xml", "eng");
        Assert.AreEqual("application/xhtml+xml", negotiating.AcceptMediaType);

        var plain = ScopeManifestFetchAddress.MintedWithoutNegotiation(
            "legilux.public.lu", "/filestore/x");
        Assert.AreEqual(ScopeManifestFetchAddressStatus.Minted, plain.Status);
        Assert.IsNull(plain.AcceptMediaType);
        Assert.IsNull(plain.AcceptLanguage);

        Assert.ThrowsExactly<ArgumentException>(static () => new ScopeManifestFetchAddress(
            ScopeManifestFetchAddressStatus.Minted, "h", "/p", "application/xml", null, null));
        Assert.ThrowsExactly<ArgumentException>(static () => new ScopeManifestFetchAddress(
            ScopeManifestFetchAddressStatus.Minted, "h", null, null, null, null));
    }

    /// <summary>
    /// The act ELI page path is validated rather than accepted as any string: it is a bounded,
    /// printable, absolute path with no query or fragment, because it is fed straight into a robots
    /// evaluation.
    /// </summary>
    [TestMethod]
    public void AnActEliPagePathIsValidatedBeforeItCanReachARobotsEvaluation()
    {
        var fileUri = LuxembourgFileUri.RequireValid(
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/xml/x.xml");

        foreach (var rejected in new[] { "eli/etat", "/eli/etat?x=1", "/eli/etat#f", "/eli/ etat" })
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => LuxembourgDocumentFetchAddress.Create(
                    fileUri, LuxembourgUserFormatToken.Xml, LuxembourgLegalValue.Officiel, rejected),
                $"'{rejected}' is not an absolute bounded printable path.");
        }

        var accepted = LuxembourgDocumentFetchAddress.Create(
            fileUri, LuxembourgUserFormatToken.Xml, LuxembourgLegalValue.Officiel, "/eli/etat/leg");
        Assert.AreEqual("/eli/etat/leg", accepted.ActEliPagePath);
    }

    /// <summary>
    /// The address's own canonical identity covers every field the route depends on, so two
    /// addresses differing only in the token, the legal value or the act page path are different
    /// artifacts rather than the same one under two readings.
    /// </summary>
    [TestMethod]
    public void TheAddressIdentityCoversEveryFieldTheRouteDependsOn()
    {
        var fileUri = LuxembourgFileUri.RequireValid(
            "http://data.legilux.public.lu/filestore/eli/etat/leg/loi/2017/03/14/a439/jo/fr/xml/x.xml");
        var baseline = LuxembourgDocumentFetchAddress.Create(
            fileUri, LuxembourgUserFormatToken.Xml, LuxembourgLegalValue.Officiel, "/eli/a");

        var digests = new[]
        {
            baseline.ArtifactRef.Sha256,
            LuxembourgDocumentFetchAddress.Create(
                fileUri, LuxembourgUserFormatToken.XmlAkomaNtoso, LuxembourgLegalValue.Officiel, "/eli/a")
                .ArtifactRef.Sha256,
            LuxembourgDocumentFetchAddress.Create(
                fileUri, LuxembourgUserFormatToken.Xml, LuxembourgLegalValue.Definitif, "/eli/a")
                .ArtifactRef.Sha256,
            // Unstated is a distinct third state and must not collapse onto either marker in the
            // canonical bytes: an address for an unmarked manifestation is a different artifact
            // from one the publisher marked, even when everything else matches.
            LuxembourgDocumentFetchAddress.Create(
                fileUri, LuxembourgUserFormatToken.Xml, LuxembourgLegalValue.Unstated, "/eli/a")
                .ArtifactRef.Sha256,
            LuxembourgDocumentFetchAddress.Create(
                fileUri, LuxembourgUserFormatToken.Xml, LuxembourgLegalValue.Officiel, "/eli/b")
                .ArtifactRef.Sha256,
        };

        Assert.AreEqual(digests.Length, digests.Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(
            baseline.ArtifactRef.Sha256,
            LuxembourgDocumentFetchAddress.Create(
                fileUri, LuxembourgUserFormatToken.Xml, LuxembourgLegalValue.Officiel, "/eli/a")
                .ArtifactRef.Sha256,
            "and the identity is a pure function of those fields, not of construction order.");
    }

    private static MachineQueryRendererSource RendererSource()
    {
        var bytes = "document-fetch-route-contract-tests"u8.ToArray();
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef(
                "urn:uuid:00000000-0000-4000-9000-000000000103",
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))),
            bytes);
    }
}
