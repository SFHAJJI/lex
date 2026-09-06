using System.Text.Json;
using Azure;
using Azure.Identity;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.Custody.Probe;

/// <summary>
/// Operator-only failure evidence. A policy exception is not proof of a particular retention
/// condition. Only fixed categories, bounded HTTP statuses and, in version 2, allowlisted
/// configuration-guard attribution leave this boundary.
/// </summary>
internal static class ProbeFailureDiagnostic
{
    // A private key tags only our exact InvalidOperationException instances. Never enumerate
    // metadata or read Data on an external subtype; it may override that property.
    private static readonly object ConfigurationGuardKey = new();

    internal static InvalidOperationException ConfigurationFailure(
        ProbeConfigurationGuard guard, string message, string? setting = null)
    {
        var exception = new InvalidOperationException(message);
        exception.Data[ConfigurationGuardKey] = new ConfigurationGuard(guard, setting);
        return exception;
    }

    internal static string Serialize(
        Exception exception,
        bool includeConfiguration = false,
        bool includeCustody = false)
    {
        includeConfiguration |= includeCustody;
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
            var boundedStatus = status is >= 100 and <= 599 ? status : null;
            if (includeCustody)
            {
                var guard = current.GetType() == typeof(InvalidOperationException)
                    ? current.Data[ConfigurationGuardKey] as ConfigurationGuard : null;
                causes.Add(new
                {
                    kind,
                    http_status = boundedStatus,
                    configuration_guard = guard?.ToDiagnostic(),
                    custody_guard = CustodyGuard(current),
                });
            }
            else if (includeConfiguration)
            {
                var guard = current.GetType() == typeof(InvalidOperationException)
                    ? current.Data[ConfigurationGuardKey] as ConfigurationGuard : null;
                causes.Add(new
                {
                    kind,
                    http_status = boundedStatus,
                    configuration_guard = guard?.ToDiagnostic(),
                });
            }
            else
            {
                causes.Add(new { kind, http_status = boundedStatus });
            }
            current = current.InnerException;
        }

        return JsonSerializer.Serialize(new
        {
            schema = includeCustody
                ? "lex-v3-custody-probe-diagnostic/3"
                : includeConfiguration
                    ? "lex-v3-custody-probe-diagnostic/2"
                    : "lex-v3-custody-probe-diagnostic/1",
            @event = "custody_probe_failed",
            causes,
            truncated = current is not null,
        });
    }

    private static string? CustodyGuard(Exception exception)
    {
        // CustodyPolicyException is sealed. Read its message only after the exact-type check,
        // admit two product-owned literals, and publish only the fixed token. Provider text,
        // arbitrary exception messages and lookalikes remain outside the diagnostic boundary.
        if (exception.GetType() != typeof(CustodyPolicyException))
        {
            return null;
        }

        return exception.Message switch
        {
            "The final Azure policy reread was refused." => "final_policy_reread",
            "The final Azure object did not prove the protection required by its custody lane."
                => "final_object_protection",
            _ => null,
        };
    }

    private sealed record ConfigurationGuard(ProbeConfigurationGuard Guard, string? Setting)
    {
        internal object ToDiagnostic() => new
        {
            kind = Guard switch
            {
                ProbeConfigurationGuard.SecretCredential => "secret_credential",
                ProbeConfigurationGuard.AlternateIdentitySource => "alternate_identity_source",
                ProbeConfigurationGuard.MissingSetting => "missing_setting",
                ProbeConfigurationGuard.InvalidIdentitySource => "invalid_identity_source",
                ProbeConfigurationGuard.InvalidGuid => "invalid_guid",
                _ => "unknown",
            },
            setting = Setting?.ToUpperInvariant() switch
            {
                "AZURE_CLIENT_SECRET" or "AZURE_STORAGE_ACCOUNT_KEY"
                    or "AZURE_STORAGE_CONNECTION_STRING" or "AZURE_STORAGE_KEY"
                    or "LEX_V3_CUSTODY_ACCOUNT_KEY" or "LEX_V3_CUSTODY_CLIENT_SECRET"
                    or "LEX_V3_CUSTODY_CONNECTION_STRING"
                    or "MSI_ENDPOINT" or "MSI_SECRET" or "IMDS_ENDPOINT"
                    or "IDENTITY_SERVER_THUMBPRINT" or "AZURE_FEDERATED_TOKEN_FILE"
                    or "IDENTITY_ENDPOINT" or "IDENTITY_HEADER"
                    or "LEX_V3_CUSTODY_SERVICE_URI" or "LEX_V3_CUSTODY_STAGING_CONTAINER"
                    or "LEX_V3_CUSTODY_NIGHTLY_CONTAINER" or "LEX_V3_CUSTODY_LEGAL_HOLD_CONTAINER"
                    or "LEX_V3_CUSTODY_MANAGED_IDENTITY_CLIENT_ID" or "LEX_V3_CUSTODY_NIGHTLY_POLICY_KEY"
                    or "LEX_V3_CUSTODY_LEGAL_HOLD_POLICY_KEY" or "LEX_V3_CUSTODY_SUBSCRIPTION_ID"
                    or "LEX_V3_CUSTODY_RESOURCE_GROUP" => Setting.ToUpperInvariant(),
                _ => null,
            },
        };
    }
}

internal enum ProbeConfigurationGuard
{
    SecretCredential,
    AlternateIdentitySource,
    MissingSetting,
    InvalidIdentitySource,
    InvalidGuid,
}
