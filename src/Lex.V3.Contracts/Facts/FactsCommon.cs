using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// A reference to durable transport bytes, addressed by content and nothing else.
/// </summary>
/// <remarks>
/// There is deliberately no account, container, bucket, region, URL or path field here. A
/// physical storage provider cannot be hard-coded into a contract that has nowhere to put one.
/// Resolving a digest to bytes is a provider concern that lives outside these contracts.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransportByteReference
{
    [JsonConstructor]
    public TransportByteReference(string contentSha256, long byteLength)
    {
        if (!FactsValidation.IsLowercaseSha256(contentSha256))
        {
            throw new ArgumentException(
                "Transport bytes must be referenced by a lowercase 64 character SHA-256.",
                nameof(contentSha256));
        }

        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteLength),
                "Transport byte length cannot be negative.");
        }

        ContentSha256 = contentSha256;
        ByteLength = byteLength;
    }

    public string ContentSha256 { get; }

    public long ByteLength { get; }
}

/// <summary>
/// A reference to the source observation that witnessed a fact.
/// </summary>
/// <remarks>
/// Provider-neutral in the same way as <see cref="TransportByteReference"/>: an opaque
/// observation identity plus the transport bytes that observation captured. Dropping either one
/// severs a fact from its evidence, which is the mutation the round-trip tests exist to catch.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SourceObservationReference
{
    [JsonConstructor]
    public SourceObservationReference(
        string observationId,
        DateTimeOffset observedAt,
        TransportByteReference transportBytes)
    {
        if (!FactsValidation.IsOpaqueIdentity(observationId))
        {
            throw new ArgumentException(
                "An observation identity must be 1 to 200 printable ASCII characters.",
                nameof(observationId));
        }

        if (observedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "An observation timestamp must be expressed in UTC.",
                nameof(observedAt));
        }

        ObservationId = observationId;
        ObservedAt = observedAt;
        TransportBytes = transportBytes ?? throw new ArgumentNullException(nameof(transportBytes));
    }

    public string ObservationId { get; }

    public DateTimeOffset ObservedAt { get; }

    public TransportByteReference TransportBytes { get; }
}

/// <summary>
/// An identity exactly as the publisher states it. The raw value is never normalized, because a
/// normalized identifier is a different claim from the one the publisher made.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OfficialIdentity
{
    [JsonConstructor]
    public OfficialIdentity(PublisherId publisher, IdentifierFamily family, string rawValue)
    {
        if (!FactsValidation.IsOpaqueIdentity(rawValue))
        {
            throw new ArgumentException(
                "An official identifier must be 1 to 200 printable ASCII characters.",
                nameof(rawValue));
        }

        Publisher = publisher;
        Family = family;
        RawValue = rawValue;
    }

    public PublisherId Publisher { get; }

    public IdentifierFamily Family { get; }

    public string RawValue { get; }
}

/// <summary>
/// One qualifier on a publisher axiom, kept as the publisher expressed it.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AxiomQualifier
{
    [JsonConstructor]
    public AxiomQualifier(string predicateUri, string rawValue)
    {
        if (!FactsValidation.IsAbsoluteUri(predicateUri))
        {
            throw new ArgumentException(
                "A qualifier predicate must be an absolute URI.",
                nameof(predicateUri));
        }

        ArgumentNullException.ThrowIfNull(rawValue);
        PredicateUri = predicateUri;
        RawValue = rawValue;
    }

    public string PredicateUri { get; }

    public string RawValue { get; }
}

/// <summary>
/// One qualified axiom, with the complete ordered list of qualifiers the publisher attached.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Qualifiers"/> is a list and not a dictionary, and that is the whole point. A
/// publisher may attach the same qualifier predicate more than once with different values, and a
/// dictionary keyed by predicate silently keeps one of them. Order is preserved for the same
/// reason: it is evidence about what was served, not a presentation choice.
/// </para>
/// <para>
/// Two axioms may also share a <see cref="RemoteAxiomId"/>. That is a real publisher condition,
/// so axiom lists are never keyed or deduplicated by remote id either.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QualifiedAxiom
{
    [JsonConstructor]
    public QualifiedAxiom(string remoteAxiomId, IReadOnlyList<AxiomQualifier> qualifiers)
    {
        if (!FactsValidation.IsOpaqueIdentity(remoteAxiomId))
        {
            throw new ArgumentException(
                "A remote axiom identity must be 1 to 200 printable ASCII characters.",
                nameof(remoteAxiomId));
        }

        ArgumentNullException.ThrowIfNull(qualifiers);
        var copied = qualifiers.ToArray();
        if (Array.IndexOf(copied, null) >= 0)
        {
            throw new ArgumentException("A qualifier entry cannot be null.", nameof(qualifiers));
        }

        RemoteAxiomId = remoteAxiomId;
        Qualifiers = Array.AsReadOnly(copied);
    }

    public string RemoteAxiomId { get; }

    public IReadOnlyList<AxiomQualifier> Qualifiers { get; }
}

internal static class FactsValidation
{
    internal static bool IsLowercaseSha256(string? value)
    {
        if (value is not { Length: 64 })
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsOpaqueIdentity(string? value)
    {
        if (value is null || value.Length is 0 or > 200)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is < ' ' or > '~')
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsAbsoluteUri(string? value) =>
        value is not null &&
        value.Length is > 0 and <= 2000 &&
        Uri.TryCreate(value, UriKind.Absolute, out _);
}
