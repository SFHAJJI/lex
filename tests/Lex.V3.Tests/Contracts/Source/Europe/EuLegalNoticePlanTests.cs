using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The fixed-address plan behind the one legal-notice GET (Decision 88). What these tests pin: the
/// bound request targets exactly the R8 URI and nothing else, resolves to exactly the legal-notice
/// profile, carries exactly the one parameter its route declares, and the plan's own target is the
/// query-less path the binder extends with the pinned query.
/// </summary>
[TestClass]
public sealed class EuLegalNoticePlanTests
{
    [TestMethod]
    public void BindMintsTheExactPinnedGetAndItResolvesToTheLegalNoticeProfile()
    {
        var bound = Bind();
        var identity = MachineQueryBinder.OpenIdentity(bound.Request);

        Assert.AreEqual(EuLegalNoticeEvidence.RequestedUri, identity.RequestedUri);
        Assert.AreEqual(HttpRequestMethod.Get, identity.RenderReceipt.Method);
        Assert.IsNull(identity.RenderReceipt.ContentType);
        Assert.IsNull(identity.RenderReceipt.Charset);

        var profile = OfficialMachineQuerySourceProfiles.ResolveFor(identity);
        Assert.AreEqual(OfficialMachineQuerySourceProfileId.EuropeanUnionLegalNotice, profile.Id);
        Assert.AreEqual(OfficialMachineQuerySourceProfileId.EuropeanUnionLegalNotice,
            OfficialMachineQuerySourceProfiles.ResolveFor(MachineQueryBinder.OpenForSend(bound.Request)).Id);
    }

    [TestMethod]
    public void TheBoundInputCarriesExactlyTheDeclaredLanguageSelectionParameter()
    {
        var bound = Bind();

        Assert.IsTrue(EuLegalNoticePlan.ParameterContract.TryReadDeclaredValues(bound.InputArtifact, out var values));
        CollectionAssert.AreEqual(new[] { EuLegalNoticeEvidence.LanguageSelection }, values.ToArray());
        Assert.AreEqual("eu_legal_notice_language_selection", EuLegalNoticePlan.LanguageSelectionParameterName);
        Assert.IsNull(
            EuLegalNoticePlan.ParameterContract.Parameters[0].HeaderName,
            "the legal-notice route negotiates nothing; its one parameter fills no header.");

        // The two other document-fetch declarations do not read this input: verification is by
        // declaration, never by position alone.
        Assert.IsFalse(EuDocumentFetchPlan.ParameterContract.TryReadDeclaredValues(bound.InputArtifact, out _));
        Assert.IsFalse(DocumentFetchParameterContract.LuxembourgDocumentFetch.TryReadDeclaredValues(
            bound.InputArtifact, out _));
    }

    [TestMethod]
    public void ThePlanTargetIsTheQuerylessPathAndTheRenderedTargetIsBoundToIt()
    {
        var bound = Bind();

        Assert.AreEqual(
            "https://eur-lex.europa.eu/content/legal-notice/legal-notice.html",
            bound.MachinePlan.TargetOriginAndPath);
        Assert.AreEqual(EuLegalNoticePlan.TargetOriginAndPath, bound.MachinePlan.TargetOriginAndPath);
        Assert.IsTrue(MachineQueryValidation.IsTargetBoundToPlan(bound.MachinePlan, EuLegalNoticeEvidence.RequestedUri));
        Assert.IsFalse(MachineQueryValidation.IsTargetBoundToPlan(
            bound.MachinePlan, "https://eur-lex.europa.eu/content/legal-notice/other.html?locale=en"));

        var expectedTargetBytes = Encoding.ASCII.GetBytes("/content/legal-notice/legal-notice.html?locale=en");
        var expectedTargetLength = expectedTargetBytes.LongLength;
        var expectedTargetSha256 = Sha256(expectedTargetBytes);
        Assert.AreEqual(expectedTargetLength, bound.MachinePlan.ExpectedRequestTargetLength);
        Assert.AreEqual(expectedTargetSha256, bound.MachinePlan.ExpectedRequestTargetSha256);
    }

    [TestMethod]
    public void ThePlanIdentityIsAPureFunctionOfThePinnedConstants()
    {
        var expected = Encoding.UTF8.GetBytes(string.Join(
            '\n',
            "eu-legal-notice-plan/1",
            "requested_uri=" + EuLegalNoticeEvidence.RequestedUri,
            "language_selection=" + EuLegalNoticeEvidence.LanguageSelection));

        CollectionAssert.AreEqual(expected, EuLegalNoticePlan.CopyCanonicalIdentityBytes());
        Assert.AreEqual(Sha256(expected), EuLegalNoticePlan.ArtifactRef.Sha256);
        Assert.AreEqual("urn:uuid:70712480-66ee-46c8-8a03-2fb651ce9a45", EuLegalNoticePlan.ArtifactRef.ResourceId);

        var bound = Bind();
        Assert.AreEqual(EuLegalNoticePlan.ArtifactRef, bound.MachinePlan.RendererProfileRef);
        Assert.AreEqual(EuLegalNoticePlan.ArtifactRef, MachineQueryBinder.OpenIdentity(bound.Request).RenderReceipt.RendererProfileRef);
    }

    [TestMethod]
    public void BindRefusesNullOrEmptyIdentifiers()
    {
        var plan = new EuLegalNoticePlan();
        Assert.ThrowsExactly<ArgumentException>(() => plan.Bind(string.Empty, "urn:uuid:00000000-0000-4000-8000-000000000002", RendererSource()));
        Assert.ThrowsExactly<ArgumentException>(() => plan.Bind("urn:uuid:00000000-0000-4000-8000-000000000001", string.Empty, RendererSource()));
        Assert.ThrowsExactly<ArgumentNullException>(() => plan.Bind("urn:uuid:00000000-0000-4000-8000-000000000001", "urn:uuid:00000000-0000-4000-8000-000000000002", null!));
    }

    private static EuDocumentFetchBoundQuery Bind() => new EuLegalNoticePlan().Bind(
        "urn:uuid:00000000-0000-4000-8000-000000000e01",
        "urn:uuid:00000000-0000-4000-8000-000000000e02",
        RendererSource());

    private static MachineQueryRendererSource RendererSource()
    {
        var bytes = "eu-legal-notice-plan-tests"u8.ToArray();
        return MachineQueryRendererSource.Open(
            new SourceArtifactRef("urn:uuid:00000000-0000-4000-8000-000000000e03", Sha256(bytes)),
            bytes);
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
