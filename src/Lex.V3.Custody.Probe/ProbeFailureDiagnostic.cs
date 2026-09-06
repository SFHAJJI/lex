using System.Text.Json;
using Azure;
using Azure.Identity;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.Custody.Probe;

/// <summary>
/// Operator-only failure evidence. A policy exception is not proof of a particular retention
/// condition. Only fixed type categories and bounded HTTP statuses leave this boundary.
/// </summary>
internal static class ProbeFailureDiagnostic
{
    internal static string Serialize(Exception exception)
    {
        var causes = new List<object>();
        Exception? current = exception;
        while (current is not null && causes.Count < 8)
        {
            var kind = current switch
            {
                CustodyPolicyException => "custody_policy",
                CustodyIntegrityException => "custody_integrity",
                CustodyRequiredException => "custody_required",
                CredentialUnavailableException => "identity_unavailable",
                AuthenticationFailedException => "authentication_failed",
                RequestFailedException => "azure_request",
                HttpRequestException => "http_request",
                OperationCanceledException => "operation_canceled",
                TimeoutException => "timeout",
                JsonException => "invalid_json",
                ArgumentException => "invalid_argument",
                InvalidOperationException => "invalid_operation",
                _ => "unknown",
            };
            int? status = current switch
            {
                RequestFailedException request => request.Status,
                HttpRequestException request => (int?)request.StatusCode,
                _ => null,
            };
            causes.Add(new { kind, http_status = status is >= 100 and <= 599 ? status : null });
            current = current.InnerException;
        }

        return JsonSerializer.Serialize(new
        {
            schema = "lex-v3-custody-probe-diagnostic/1",
            @event = "custody_probe_failed",
            causes,
            truncated = current is not null,
        });
    }
}
