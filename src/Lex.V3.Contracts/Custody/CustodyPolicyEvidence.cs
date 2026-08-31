using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Custody;

/// <summary>
/// Provider-neutral evidence of the protection observed on one exact held object.
/// </summary>
/// <remarks>
/// <para>
/// A configured duration is not object evidence. The only time-based claim carried here is the
/// exact first instant at which protection is no longer claimed. A legal hold is reported only as
/// active at <see cref="ObservedAt"/> because a privileged operator can later clear it.
/// </para>
/// <para>
/// <see cref="PolicyKey"/> is a random, non-locating identifier for joining this observation to a
/// separately retained configuration receipt. It is never derived from an account, container,
/// endpoint, path or other storage coordinate.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustodyPolicyEvidence
{
    private static readonly TimeSpan NightlyFloor = TimeSpan.FromDays(90);

    [JsonConstructor]
    public CustodyPolicyEvidence(
        string schema,
        DurableBlobRef reference,
        CustodyVerificationProfile verificationProfile,
        Guid? policyKey,
        CustodyProtection protection,
        DateTimeOffset observedAt,
        DateTimeOffset? protectedUntil)
    {
        if (!string.Equals(schema, CustodySchemaIds.CustodyPolicyEvidence, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Custody policy evidence must declare {CustodySchemaIds.CustodyPolicyEvidence}.",
                nameof(schema));
        }

        ArgumentNullException.ThrowIfNull(reference);

        if (!Enum.IsDefined(verificationProfile))
        {
            throw new ArgumentOutOfRangeException(
                nameof(verificationProfile),
                verificationProfile,
                "That is not an admitted custody verification profile.");
        }

        if (!Enum.IsDefined(protection))
        {
            throw new ArgumentOutOfRangeException(
                nameof(protection), protection, "That is not an admitted custody protection.");
        }

        RequireUtc(observedAt, nameof(observedAt));
        if (protectedUntil is not null)
        {
            RequireUtc(protectedUntil.Value, nameof(protectedUntil));
        }

        switch (verificationProfile)
        {
            case CustodyVerificationProfile.FileSystemUnenforced1:
                if (policyKey is not null
                    || protection != CustodyProtection.NotEnforced
                    || protectedUntil is not null)
                {
                    throw new ArgumentException(
                        "The filesystem profile can report only unprotected local custody.",
                        nameof(verificationProfile));
                }

                break;

            case CustodyVerificationProfile.ImmutableObject1:
                if (policyKey is null || policyKey == Guid.Empty)
                {
                    throw new ArgumentException(
                        "Immutable-object evidence requires a nonempty policy key.",
                        nameof(policyKey));
                }

                ValidateImmutableProtection(reference, protection, observedAt, protectedUntil);
                break;
        }

        Schema = schema;
        Reference = reference;
        VerificationProfile = verificationProfile;
        PolicyKey = policyKey;
        Protection = protection;
        ObservedAt = observedAt;
        ProtectedUntil = protectedUntil;
    }

    public string Schema { get; }

    public DurableBlobRef Reference { get; }

    public CustodyVerificationProfile VerificationProfile { get; }

    public Guid? PolicyKey { get; }

    public CustodyProtection Protection { get; }

    public DateTimeOffset ObservedAt { get; }

    /// <summary>
    /// The first instant at which locked-time protection is no longer claimed, or null when the
    /// observation is not a fixed-duration protection claim.
    /// </summary>
    public DateTimeOffset? ProtectedUntil { get; }

    private static void ValidateImmutableProtection(
        DurableBlobRef reference,
        CustodyProtection protection,
        DateTimeOffset observedAt,
        DateTimeOffset? protectedUntil)
    {
        switch (reference.CustodyClass, protection)
        {
            case (CustodyClass.NightlyFloor90d, CustodyProtection.LockedTime):
                if (protectedUntil is null || protectedUntil.Value - observedAt < NightlyFloor)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(protectedUntil),
                        "A nightly receipt requires at least 90 days of protection remaining.");
                }

                break;

            case (CustodyClass.LegalHoldEvidence, CustodyProtection.ActiveLegalHold):
                if (protectedUntil is not null)
                {
                    throw new ArgumentException(
                        "An active legal hold has no fixed future expiry claim.",
                        nameof(protectedUntil));
                }

                break;

            default:
                throw new ArgumentException(
                    "The observed immutable protection does not satisfy the requested custody lane.",
                    nameof(protection));
        }
    }

    private static void RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A custody-policy instant must be expressed in UTC.", parameterName);
        }
    }
}
