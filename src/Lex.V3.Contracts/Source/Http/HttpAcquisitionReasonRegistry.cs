using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Http;

public enum HttpPartialBodyReason
{
    BodyDeadline = 1,
    BodyReadFailure = 2,
    ByteBoundPreventedCompletion = 3,
    CallerCancelledAfterHeaders = 4,
    DeclaredLengthShortRead = 5,
}

public enum HttpCompletionUnprovenReason
{
    MissingCompletionProof = 1,
    TransferCodingConflict = 2,
}

public enum HttpPreHeaderFailureClass
{
    HeaderDeadline = 1,
    TransportBeforeHeaders = 2,
}

public static class HttpAcquisitionReasonRegistry
{
    public const string Schema = "http_acquisition_reason_registry/1";
    public const string ResourceId = "urn:uuid:f9eb3136-c855-44f5-b84f-6c28353b592d";
    public const string Sha256 =
        "7648f04492573e9a748a167d27c42841da0cfba1a8735070d2b322c39a242197";

    private const string CanonicalArtifact =
        "{\"schema\":\"http_acquisition_reason_registry/1\",\"registry_id\":\"urn:uuid:f9eb3136-c855-44f5-b84f-6c28353b592d\",\"members\":[{\"member_key\":\"body_deadline\",\"stage\":\"entity_transfer\"},{\"member_key\":\"body_read_failure\",\"stage\":\"entity_transfer\"},{\"member_key\":\"byte_bound_prevented_completion\",\"stage\":\"entity_transfer\"},{\"member_key\":\"caller_cancelled_after_headers\",\"stage\":\"entity_transfer\"},{\"member_key\":\"declared_length_short_read\",\"stage\":\"entity_transfer\"},{\"member_key\":\"header_deadline\",\"stage\":\"before_response_headers\"},{\"member_key\":\"missing_completion_proof\",\"stage\":\"completion_unproven\"},{\"member_key\":\"transfer_coding_conflict\",\"stage\":\"completion_unproven\"},{\"member_key\":\"transport_before_headers\",\"stage\":\"before_response_headers\"}]}\n";

    private static readonly byte[] CanonicalArtifactBytes = Encoding.UTF8.GetBytes(CanonicalArtifact);

    public static ReadOnlySpan<byte> CanonicalBytes => CanonicalArtifactBytes;

    public static SourceArtifactRef RegistryRef { get; } = new(ResourceId, Sha256);

    public static SourceRegistryMemberRef Member(HttpPartialBodyReason reason) => new(
        RegistryRef,
        SourceCoreValidation.RequireDefined(reason, nameof(reason)) switch
        {
            HttpPartialBodyReason.BodyDeadline => "body_deadline",
            HttpPartialBodyReason.BodyReadFailure => "body_read_failure",
            HttpPartialBodyReason.ByteBoundPreventedCompletion => "byte_bound_prevented_completion",
            HttpPartialBodyReason.CallerCancelledAfterHeaders => "caller_cancelled_after_headers",
            HttpPartialBodyReason.DeclaredLengthShortRead => "declared_length_short_read",
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        });

    public static SourceRegistryMemberRef Member(HttpCompletionUnprovenReason reason) => new(
        RegistryRef,
        SourceCoreValidation.RequireDefined(reason, nameof(reason)) switch
        {
            HttpCompletionUnprovenReason.MissingCompletionProof => "missing_completion_proof",
            HttpCompletionUnprovenReason.TransferCodingConflict => "transfer_coding_conflict",
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        });

    public static SourceRegistryMemberRef Member(HttpPreHeaderFailureClass failureClass) => new(
        RegistryRef,
        SourceCoreValidation.RequireDefined(failureClass, nameof(failureClass)) switch
        {
            HttpPreHeaderFailureClass.HeaderDeadline => "header_deadline",
            HttpPreHeaderFailureClass.TransportBeforeHeaders => "transport_before_headers",
            _ => throw new ArgumentOutOfRangeException(nameof(failureClass)),
        });

    public static HttpPartialBodyReason RequirePartial(SourceRegistryMemberRef member)
    {
        RequireRegistry(member);
        return member.MemberKey switch
        {
            "body_deadline" => HttpPartialBodyReason.BodyDeadline,
            "body_read_failure" => HttpPartialBodyReason.BodyReadFailure,
            "byte_bound_prevented_completion" => HttpPartialBodyReason.ByteBoundPreventedCompletion,
            "caller_cancelled_after_headers" => HttpPartialBodyReason.CallerCancelledAfterHeaders,
            "declared_length_short_read" => HttpPartialBodyReason.DeclaredLengthShortRead,
            _ => throw new ArgumentException(
                "The acquisition reason is not a partial-body member.",
                nameof(member)),
        };
    }

    public static HttpCompletionUnprovenReason RequireCompletionUnproven(
        SourceRegistryMemberRef member)
    {
        RequireRegistry(member);
        return member.MemberKey switch
        {
            "missing_completion_proof" =>
                HttpCompletionUnprovenReason.MissingCompletionProof,
            "transfer_coding_conflict" =>
                HttpCompletionUnprovenReason.TransferCodingConflict,
            _ => throw new ArgumentException(
                "The acquisition reason is not a completion-unproven member.",
                nameof(member)),
        };
    }

    public static HttpPreHeaderFailureClass RequireBeforeHeaders(SourceRegistryMemberRef member)
    {
        RequireRegistry(member);
        return member.MemberKey switch
        {
            "header_deadline" => HttpPreHeaderFailureClass.HeaderDeadline,
            "transport_before_headers" => HttpPreHeaderFailureClass.TransportBeforeHeaders,
            _ => throw new ArgumentException(
                "The acquisition reason is not a before-response-headers member.",
                nameof(member)),
        };
    }

    private static void RequireRegistry(SourceRegistryMemberRef member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (member.RegistryRef != RegistryRef)
        {
            throw new ArgumentException(
                "The acquisition reason must bind the pinned reason registry.",
                nameof(member));
        }
    }
}
