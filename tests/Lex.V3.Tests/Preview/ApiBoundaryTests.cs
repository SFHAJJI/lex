using Lex.V3.Api;
using Lex.V3.Contracts;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class ApiBoundaryTests
{
    [TestMethod]
    [DataRow(
        "/api/v3-preview/resolve?family=eli&coordinate=eli%2Fsynthetic-preview",
        "eli",
        "eli/synthetic-preview")]
    [DataRow(
        "/api/v3-preview/resolve?family=historical_legal_id&coordinate=historical_legal_id%3Asynthetic-preview",
        "historical_legal_id",
        "historical_legal_id:synthetic-preview")]
    public void ExactRawTargetIsAccepted(string rawTarget, string expectedFamily, string expectedCoordinate)
    {
        var parsed = SyntheticRawTarget.Parse(rawTarget);

        Assert.IsTrue(parsed.Accepted);
        Assert.AreEqual(expectedFamily, parsed.Family);
        Assert.AreEqual(expectedCoordinate, parsed.Coordinate);
    }

    [TestMethod]
    [DataRow("/api/v3-preview/resolve?coordinate=eli%2Fsynthetic-preview&family=eli")]
    [DataRow("/api/v3-preview/resolve?family=eli&coordinate=eli/synthetic-preview")]
    [DataRow("/api/v3-preview/resolve?family=eli&coordinate=eli%2fsynthetic-preview")]
    [DataRow("/api/v3-preview/resolve?family=ELI&coordinate=eli%2Fsynthetic-preview")]
    [DataRow("/api/v3-preview/resolve?family=eli&coordinate=eli%252Fsynthetic-preview")]
    [DataRow("/api/v3-preview/resolve?family=eli&coordinate=eli%2Fsynthetic-preview&extra=1")]
    [DataRow("/api/v3-preview/resolve?family=eli&family=eli&coordinate=eli%2Fsynthetic-preview")]
    [DataRow("/api/v3-preview/resolve")]
    [DataRow("")]
    public void NonCanonicalRawTargetIsRejected(string rawTarget)
    {
        Assert.IsFalse(SyntheticRawTarget.Parse(rawTarget).Accepted);
    }

    [TestMethod]
    public void ApplicationRawTargetBoundaryIsExplicitAndFailClosed()
    {
        Assert.AreEqual(2048, SyntheticResolveRequestContract.V1.MaximumApplicationRawTargetBytes);
        Assert.IsTrue(SyntheticRawTarget.IsWithinApplicationBoundary(
            SyntheticResolveRequestContract.HeldRawTarget));
        Assert.IsTrue(SyntheticRawTarget.IsWithinApplicationBoundary(
            new string('a', SyntheticResolveRequestContract.MaximumApplicationRawTargetByteCount)));
        Assert.IsFalse(SyntheticRawTarget.IsWithinApplicationBoundary(
            new string('a', SyntheticResolveRequestContract.MaximumApplicationRawTargetByteCount + 1)));
        Assert.IsFalse(SyntheticRawTarget.IsWithinApplicationBoundary("ascii\0suffix"));
        Assert.IsFalse(SyntheticRawTarget.IsWithinApplicationBoundary("non-ascii-\u00e9"));
    }

    [TestMethod]
    public void RequestReferenceUsesExactlySixteenEntropyBytes()
    {
        var entropy = new RecordingEntropySource(
            Enumerable.Range(0, 16).Select(static value => (byte)value).ToArray());

        var requestReference = RequestReferenceFactory.Create(entropy);

        Assert.AreEqual("req_000102030405060708090a0b0c0d0e0f", requestReference);
        Assert.AreEqual(16, entropy.RequestedBytes);
    }

    private sealed class RecordingEntropySource(byte[] bytes) : IRequestEntropySource
    {
        public int RequestedBytes { get; private set; }

        public void Fill(Span<byte> destination)
        {
            RequestedBytes = destination.Length;
            bytes.CopyTo(destination);
        }
    }
}
