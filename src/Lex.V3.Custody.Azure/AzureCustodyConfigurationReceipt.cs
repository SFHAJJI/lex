using System.Text.Json.Serialization;
using Lex.V3.Contracts.Custody;

namespace Lex.V3.Custody.Azure;

public static class AzureCustodySchemaIds
{
    public const string ConfigurationReceipt =
        "lex-v3-azure-custody-configuration-receipt/1";
}

/// <summary>
/// Private Azure configuration evidence joined from a portable receipt by policy key, observation
/// instant and custody class. This document must never enter a public corpus or answer envelope.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AzureCustodyConfigurationReceipt
{
    [JsonConstructor]
    public AzureCustodyConfigurationReceipt(
        string schema,
        Guid policyKey,
        CustodyClass custodyClass,
        DateTimeOffset observedAt,
        string armResourceId,
        string armApiVersion,
        string armResourceEtag,
        string armRequestId,
        Guid managedIdentityClientId,
        string publicAccess,
        bool immutableStorageWithVersioningEnabled,
        string? migrationState,
        string? immutabilityPolicyEtag,
        string? immutabilityPolicyState,
        int? retentionDays,
        bool protectedAppendWrites,
        bool protectedAppendWritesAll,
        bool activeLegalHold,
        bool protectedBlockBlobAppends)
    {
        if (!string.Equals(
                schema, AzureCustodySchemaIds.ConfigurationReceipt, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"An Azure custody configuration receipt must declare {AzureCustodySchemaIds.ConfigurationReceipt}.",
                nameof(schema));
        }

        if (policyKey == Guid.Empty)
        {
            throw new ArgumentException("A nonempty policy key is required.", nameof(policyKey));
        }

        if (!Enum.IsDefined(custodyClass))
        {
            throw new ArgumentOutOfRangeException(
                nameof(custodyClass), custodyClass, "Unknown custody class.");
        }

        if (observedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The Azure policy observation must be expressed in UTC.", nameof(observedAt));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(armResourceId);
        if (!armResourceId.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The ARM resource identity has the wrong shape.", nameof(armResourceId));
        }

        if (!string.Equals(armApiVersion, "2025-06-01", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Azure custody reader requires the admitted ARM API version.",
                nameof(armApiVersion));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(armResourceEtag);
        if (!Guid.TryParse(armRequestId, out _))
        {
            throw new ArgumentException(
                "The ARM request identity must be a GUID.", nameof(armRequestId));
        }

        if (managedIdentityClientId == Guid.Empty)
        {
            throw new ArgumentException(
                "The configured managed-identity client ID is required.",
                nameof(managedIdentityClientId));
        }

        if (!string.Equals(publicAccess, "None", StringComparison.Ordinal)
            || immutableStorageWithVersioningEnabled
            || migrationState is not null
            || protectedAppendWrites
            || protectedAppendWritesAll
            || protectedBlockBlobAppends)
        {
            throw new ArgumentException(
                "The Azure configuration does not support an exact private unversioned object claim.");
        }

        switch (custodyClass)
        {
            case CustodyClass.NightlyFloor90d:
                ArgumentException.ThrowIfNullOrWhiteSpace(immutabilityPolicyEtag);
                if (!string.Equals(immutabilityPolicyState, "Locked", StringComparison.Ordinal)
                    || retentionDays is null or < 1 or > 146_000
                    || activeLegalHold)
                {
                    throw new ArgumentException(
                        "The nightly configuration does not prove a locked retention policy.");
                }

                break;

            case CustodyClass.LegalHoldEvidence:
                if (immutabilityPolicyEtag is not null
                    || immutabilityPolicyState is not null
                    || retentionDays is not null
                    || !activeLegalHold)
                {
                    throw new ArgumentException(
                        "The legal-hold configuration does not prove an active hold.");
                }

                break;
        }

        Schema = schema;
        PolicyKey = policyKey;
        CustodyClass = custodyClass;
        ObservedAt = observedAt;
        ArmResourceId = armResourceId;
        ArmApiVersion = armApiVersion;
        ArmResourceEtag = armResourceEtag;
        ArmRequestId = armRequestId;
        ManagedIdentityClientId = managedIdentityClientId;
        PublicAccess = publicAccess;
        ImmutableStorageWithVersioningEnabled = immutableStorageWithVersioningEnabled;
        MigrationState = migrationState;
        ImmutabilityPolicyEtag = immutabilityPolicyEtag;
        ImmutabilityPolicyState = immutabilityPolicyState;
        RetentionDays = retentionDays;
        ProtectedAppendWrites = protectedAppendWrites;
        ProtectedAppendWritesAll = protectedAppendWritesAll;
        ActiveLegalHold = activeLegalHold;
        ProtectedBlockBlobAppends = protectedBlockBlobAppends;
    }

    public string Schema { get; }

    public Guid PolicyKey { get; }

    public CustodyClass CustodyClass { get; }

    public DateTimeOffset ObservedAt { get; }

    public string ArmResourceId { get; }

    public string ArmApiVersion { get; }

    public string ArmResourceEtag { get; }

    public string ArmRequestId { get; }

    public Guid ManagedIdentityClientId { get; }

    public string PublicAccess { get; }

    public bool ImmutableStorageWithVersioningEnabled { get; }

    public string? MigrationState { get; }

    public string? ImmutabilityPolicyEtag { get; }

    public string? ImmutabilityPolicyState { get; }

    public int? RetentionDays { get; }

    public bool ProtectedAppendWrites { get; }

    public bool ProtectedAppendWritesAll { get; }

    public bool ActiveLegalHold { get; }

    public bool ProtectedBlockBlobAppends { get; }
}

/// <summary>
/// Append-only private retention for every Azure observation used by a portable custody receipt.
/// An implementation must retain entries at least as long as any joined portable receipt.
/// </summary>
public interface IAzureCustodyConfigurationReceiptJournal
{
    Task AppendAsync(
        AzureCustodyConfigurationReceipt receipt,
        CancellationToken cancellationToken);
}
