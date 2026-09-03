using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// Mints the one <see cref="SourceProfileTopology"/> value Luxembourg's design ruling requires:
/// R3.2 of the D1-01 candidate ("D1-01 owns that topology honesty in the source profile through
/// the required field source_profile_topology/1. The current LU value is exactly
/// single_publisher_store") and the D1-04 design-synthesis ruling
/// (lex-event-20260903T192615392Z-b13dee192bd84cea970b71cd8ffd4b89, "Mint SourceProfileTopology
/// with the single member single_publisher_store bound to the LU profile identity, so the
/// executor's tripwire flips and every absence, completion and release consumer can refuse a
/// missing value").
/// </summary>
/// <remarks>
/// <para>
/// Before this type existed, nothing in <c>src</c> outside <c>Source.Core</c>'s own tests
/// constructed a <see cref="SourceProfileTopology"/> for Luxembourg. That gap is what
/// <c>LuxembourgWitnessIndependenceTests.NothingInTheDeliveryProfileRecordsWitnessIndependenceYet</c>
/// pinned as a residue: the two-pass reconciliation the repeated-enumeration executor performs is
/// not a second independent witness (both passes read the one Virtuoso store), so R3.2 requires the
/// profile to say so explicitly rather than let a downstream absence, completion or release
/// consumer assume independent corroboration that was never observed.
/// </para>
/// <para>
/// The registry this type mints is frozen and has exactly one member,
/// <see cref="SinglePublisherStoreMemberKey"/>, which is R3.2's exact vocabulary token. It is a
/// distinct artifact from the LU source profile's own identity: <see cref="SourceProfileTopology"/>
/// separates <c>IdentityProfileRef</c> (whose topology this is: the LU profile) from
/// <c>Topology</c> (a <see cref="SourceRegistryMemberRef"/>, a member of some registry). Binding
/// both to the same profile artifact would collapse "whose identity" and "which registry" into one
/// object and make a future second topology value (an independent witness, if one is ever observed)
/// indistinguishable from this one by registry alone.
/// </para>
/// </remarks>
public static class LuxembourgSourceProfileTopology
{
    /// <summary>
    /// R3.2's exact vocabulary token: "the receipt therefore records
    /// witness_independence = single_publisher_store".
    /// </summary>
    public const string SinglePublisherStoreMemberKey = "single_publisher_store";

    private const string RegistryResourceId = "urn:uuid:b709709a-2ce9-4090-a030-44241490c7d5";
    private const string RegistryDomain = "lex-v3-luxembourg-source-profile-topology-registry/1";

    /// <summary>
    /// The frozen registry artifact identity. Content-addressed over the registry's own domain tag
    /// and its one member key, so a future second member changes this digest rather than silently
    /// growing an unversioned list.
    /// </summary>
    public static SourceArtifactRef RegistryRef { get; } = new(RegistryResourceId, ComputeRegistrySha256());

    /// <summary>
    /// Mints the LU topology: the frozen single-member registry, bound to the exact profile whose
    /// topology this is. There is no other constructor path to a Luxembourg
    /// <see cref="SourceProfileTopology"/> in this codebase; <see cref="VerifiedLuxembourgSourceProfile"/>
    /// does not expose one because topology is a Core-wire concern (<see cref="SourceCoreSchemaIds.SourceProfileTopology"/>),
    /// not a scope-resolution concern, and belongs beside the profile that is bound to it rather than
    /// buried inside a resolution result a caller might not request.
    /// </summary>
    public static SourceProfileTopology Mint(VerifiedLuxembourgSourceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var member = new SourceRegistryMemberRef(RegistryRef, SinglePublisherStoreMemberKey);
        return new SourceProfileTopology(
            SourceCoreSchemaIds.SourceProfileTopology,
            profile.ScopeBinding.SourceProfileRef,
            member);
    }

    private static string ComputeRegistrySha256()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, RegistryDomain);
        Append(hash, SinglePublisherStoreMemberKey);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
