using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// A reference to durable transport bytes, addressed by content and nothing else.
/// </summary>
/// <remarks>
/// There is deliberately no account, container, bucket, region, URL or path field here. A
/// physical storage provider cannot be hard-coded into a contract that has nowhere to put one.
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
/// One identifier exactly as the publisher states it.
/// </summary>
/// <remarks>
/// The raw value is never normalized, because a normalized identifier is a different claim from
/// the one the publisher made. Cellar URI families additionally require an absolute URI, since a
/// URI family whose value is not a URI is not the thing it claims to be.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OfficialIdentifier
{
    [JsonConstructor]
    public OfficialIdentifier(FactsIdentifierFamily family, string rawValue)
    {
        FactsValidation.RequireDefined(family, nameof(family));
        if (!FactsValidation.IsOpaqueIdentity(rawValue))
        {
            throw new ArgumentException(
                "An official identifier must be 1 to 200 printable ASCII characters.",
                nameof(rawValue));
        }

        if (family is FactsIdentifierFamily.CellarWorkUri or FactsIdentifierFamily.CellarResourceUri
            && !FactsValidation.IsAbsoluteUri(rawValue))
        {
            throw new ArgumentException(
                "A Cellar URI family must carry an absolute URI.",
                nameof(rawValue));
        }

        Family = family;
        RawValue = rawValue;
    }

    public FactsIdentifierFamily Family { get; }

    public string RawValue { get; }
}

/// <summary>
/// Everything one publisher says identifies a single thing, kept together.
/// </summary>
/// <remarks>
/// <para>
/// A EUR-Lex case is a Cellar work URI, a CELEX number and an ECLI at once. Candidate 1 carried
/// one family per endpoint, so retaining any one of those meant discarding the other two, and
/// the fixture that claimed to be a Cellar case relation in fact retained CELEX alone. That is
/// exactly the lossless-identity requirement this package exists to satisfy.
/// </para>
/// <para>
/// The list is defensively copied, ordered as the publisher gave it, and may not repeat a
/// family. Repetition is refused rather than kept, because two CELEX numbers for one endpoint is
/// not a richer identity, it is a contradiction that some later reader would have to resolve by
/// guessing.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OfficialIdentitySet
{
    [JsonConstructor]
    public OfficialIdentitySet(PublisherId publisher, IReadOnlyList<OfficialIdentifier> identifiers)
    {
        FactsValidation.RequireDefined(publisher, nameof(publisher));
        ArgumentNullException.ThrowIfNull(identifiers);
        var copied = identifiers.ToArray();
        if (copied.Length == 0)
        {
            throw new ArgumentException(
                "An identity set must carry at least one identifier.",
                nameof(identifiers));
        }

        if (Array.IndexOf(copied, null) >= 0)
        {
            throw new ArgumentException("An identifier cannot be null.", nameof(identifiers));
        }

        var families = new HashSet<FactsIdentifierFamily>();
        foreach (var identifier in copied)
        {
            if (!families.Add(identifier.Family))
            {
                throw new ArgumentException(
                    $"An identity set cannot repeat the {identifier.Family} family.",
                    nameof(identifiers));
            }
        }

        Publisher = publisher;
        Identifiers = Array.AsReadOnly(copied);
    }

    public PublisherId Publisher { get; }

    public IReadOnlyList<OfficialIdentifier> Identifiers { get; }

    /// <summary>The value for a family, or null where the set does not carry that family.</summary>
    public string? Value(FactsIdentifierFamily family)
    {
        foreach (var identifier in Identifiers)
        {
            if (identifier.Family == family)
            {
                return identifier.RawValue;
            }
        }

        return null;
    }

    public bool Has(FactsIdentifierFamily family) => Value(family) is not null;

    /// <summary>
    /// Whether this set identifies a court decision, which is what makes an ECLI applicable.
    /// </summary>
    /// <remarks>
    /// Decided by the identifiers present rather than declared: an ECLI is itself proof, and a
    /// Cellar work URI under the case segment or a CELEX sector 6 number identify a case. A
    /// Luxembourg statute matches none of these and so is correctly not-applicable.
    /// </remarks>
    [JsonIgnore]
    public bool IsCase =>
        Has(FactsIdentifierFamily.Ecli) ||
        Value(FactsIdentifierFamily.Celex) is { Length: > 4 } celex && celex[4] == 'C' ||
        Value(FactsIdentifierFamily.CellarWorkUri)?.Contains("/case/", StringComparison.Ordinal) == true;

    /// <summary>Ordinal equality over publisher and the ordered identifier list.</summary>
    public bool SameIdentity(OfficialIdentitySet? other)
    {
        if (other is null || other.Publisher != Publisher ||
            other.Identifiers.Count != Identifiers.Count)
        {
            return false;
        }

        for (var index = 0; index < Identifiers.Count; index++)
        {
            if (Identifiers[index].Family != other.Identifiers[index].Family ||
                !string.Equals(
                    Identifiers[index].RawValue,
                    other.Identifiers[index].RawValue,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
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
/// <see cref="Qualifiers"/> is a list and not a dictionary, and that is the whole point. A
/// publisher may attach the same qualifier predicate more than once with different values, and a
/// dictionary keyed by predicate silently keeps one of them. Two axioms may also share a
/// <see cref="RemoteAxiomId"/>, so axiom lists are never keyed or deduplicated either.
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
    /// <summary>
    /// Refuse an enum value outside its declared members.
    /// </summary>
    /// <remarks>
    /// The JSON reader already refuses an unknown wire term, but nothing stopped direct
    /// construction with <c>(EcliState)42</c>, so a caller inside the process could build a fact
    /// carrying a state no schema declares.
    /// </remarks>
    internal static void RequireDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentException(
                $"{value} is not a declared {typeof(TEnum).Name} member.",
                parameterName);
        }
    }

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
