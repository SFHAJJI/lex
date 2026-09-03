using System.Security.Cryptography;
using System.Text.Json;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class HttpAcquisitionReasonRegistryTests
{
    private const string ExpectedSha256 =
        "803ed00fc952d30e66984c21e045dc79dd39c2d555f81df159b7045e32dbbc89";

    [TestMethod]
    public void CanonicalArtifactBindsThePinnedIdentityAndClosedVocabulary()
    {
        var bytes = HttpAcquisitionReasonRegistry.CanonicalBytes.ToArray();
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.AreEqual(1112, bytes.Length);
        Assert.AreEqual(ExpectedSha256, digest);
        Assert.AreEqual(ExpectedSha256, HttpAcquisitionReasonRegistry.RegistryRef.Sha256);
        CollectionAssert.AreEqual(
            bytes,
            File.ReadAllBytes(Path.Combine(
                AppContext.BaseDirectory,
                "http-acquisition-reason-registry.json")));

        using var document = JsonDocument.Parse(bytes);
        var members = document.RootElement.GetProperty("members")
            .EnumerateArray()
            .Select(member =>
                $"{member.GetProperty("member_key").GetString()}:" +
                member.GetProperty("stage").GetString())
            .ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "body_deadline:entity_transfer",
                "body_read_failure:entity_transfer",
                "byte_bound_prevented_completion:entity_transfer",
                "caller_cancelled_after_headers:entity_transfer",
                "declared_length_short_read:entity_transfer",
                "header_deadline:before_response_headers",
                "invalid_content_length:completion_unproven",
                "missing_completion_proof:completion_unproven",
                "revalidation_request_not_admitted:response_semantics",
                "status_content_forbidden:response_semantics",
                "status_framing_conflict:response_semantics",
                "transfer_coding_conflict:completion_unproven",
                "transport_before_headers:before_response_headers",
                "unsupported_transfer_coding:completion_unproven",
            },
            members);
    }

    [TestMethod]
    public void TypedMembersRoundTripAndRejectTheOtherAcquisitionStages()
    {
        foreach (var reason in Enum.GetValues<HttpPartialBodyReason>())
        {
            var member = HttpAcquisitionReasonRegistry.Member(reason);
            Assert.AreEqual(reason, HttpAcquisitionReasonRegistry.RequirePartial(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequireCompletionUnproven(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequireResponseSemantics(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequireBeforeHeaders(member));
        }

        foreach (var reason in Enum.GetValues<HttpCompletionUnprovenReason>())
        {
            var member = HttpAcquisitionReasonRegistry.Member(reason);
            Assert.AreEqual(
                reason,
                HttpAcquisitionReasonRegistry.RequireCompletionUnproven(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequirePartial(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequireResponseSemantics(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequireBeforeHeaders(member));
        }

        foreach (var reason in Enum.GetValues<HttpPreHeaderFailureClass>())
        {
            var member = HttpAcquisitionReasonRegistry.Member(reason);
            Assert.AreEqual(reason, HttpAcquisitionReasonRegistry.RequireBeforeHeaders(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequirePartial(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequireCompletionUnproven(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequireResponseSemantics(member));
        }

        foreach (var reason in Enum.GetValues<HttpResponseSemanticsReason>())
        {
            var member = HttpAcquisitionReasonRegistry.Member(reason);
            Assert.AreEqual(
                reason,
                HttpAcquisitionReasonRegistry.RequireResponseSemantics(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequirePartial(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequireCompletionUnproven(member));
            Assert.ThrowsExactly<ArgumentException>(() =>
                HttpAcquisitionReasonRegistry.RequireBeforeHeaders(member));
        }

        var valid = HttpAcquisitionReasonRegistry.Member(HttpPartialBodyReason.BodyDeadline);
        var foreign = new SourceRegistryMemberRef(
            new SourceArtifactRef(valid.RegistryRef.ResourceId, new string('f', 64)),
            valid.MemberKey);
        Assert.ThrowsExactly<ArgumentException>(() =>
            HttpAcquisitionReasonRegistry.RequirePartial(foreign));
        Assert.ThrowsExactly<ArgumentException>(() =>
            HttpAcquisitionReasonRegistry.RequirePartial(
                new SourceRegistryMemberRef(valid.RegistryRef, "unknown_reason")));
    }

    [TestMethod]
    public void EveryReasonKeepsItsPinnedNumericIdentity()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "BodyDeadline=1",
                "BodyReadFailure=2",
                "ByteBoundPreventedCompletion=3",
                "CallerCancelledAfterHeaders=4",
                "DeclaredLengthShortRead=5",
            },
            NumericIdentities<HttpPartialBodyReason>());
        CollectionAssert.AreEqual(
            new[] { "HeaderDeadline=1", "TransportBeforeHeaders=2" },
            NumericIdentities<HttpPreHeaderFailureClass>());
        CollectionAssert.AreEqual(
            new[]
            {
                "InvalidContentLength=3",
                "MissingCompletionProof=1",
                "TransferCodingConflict=2",
                "UnsupportedTransferCoding=4",
            },
            NumericIdentities<HttpCompletionUnprovenReason>());
        CollectionAssert.AreEqual(
            new[]
            {
                "RevalidationRequestNotAdmitted=1",
                "StatusContentForbidden=2",
                "StatusFramingConflict=3",
            },
            NumericIdentities<HttpResponseSemanticsReason>());
    }

    private static string[] NumericIdentities<TEnum>()
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>()
            .Select(static value => $"{value}={Convert.ToInt32(value)}")
            .OrderBy(static identity => identity, StringComparer.Ordinal)
            .ToArray();
}
