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
/// One identifier exactly as the publisher states it, parsed rather than merely labelled.
/// </summary>
/// <remarks>
/// The raw value is never normalized, because a normalized identifier is a different claim from
/// the one the publisher made. It is however <b>validated against the grammar its family
/// declares</b>. Candidate 2 trusted the label: any printable string could be tagged `ecli`, and
/// any absolute URI could be tagged as a Cellar URI. A family that asserts nothing about its
/// value is a free-text field wearing a type.
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

        if (!IsWellFormed(family, rawValue))
        {
            throw new ArgumentException(
                $"The value is not a well-formed {family} identifier.",
                nameof(rawValue));
        }

        Family = family;
        RawValue = rawValue;
    }

    public FactsIdentifierFamily Family { get; }

    public string RawValue { get; }

    /// <summary>The CELEX sector digit, or null where this is not a CELEX.</summary>
    [JsonIgnore]
    public char? CelexSector =>
        Family == FactsIdentifierFamily.Celex ? RawValue[0] : null;

    /// <summary>
    /// Whether this single identifier is itself proof of a court decision.
    /// </summary>
    /// <remarks>
    /// Only two things prove it: a well-formed ECLI, or a CELEX in <b>sector 6</b>, which is case
    /// law. Candidate 2 tested <c>celex[4] == 'C'</c>, and in <c>62019CJ0311</c> position 4 is
    /// <c>'9'</c>, so a CELEX-only case was classified as not a case. The sector is position
    /// <b>zero</b>. My own test asserted "a CELEX sector 6 number is a case" and passed anyway,
    /// because the fixture also carried a URI containing <c>/case/</c>, so the assertion was true
    /// for a reason it did not name.
    /// </remarks>
    [JsonIgnore]
    public bool ProvesCase =>
        Family == FactsIdentifierFamily.Ecli ||
        (Family == FactsIdentifierFamily.Celex && RawValue[0] == '6');

    /// <summary>The grammar each family declares, checked rather than assumed.</summary>
    public static bool IsWellFormed(FactsIdentifierFamily family, string value) => family switch
    {
        // sector digit, four-digit year, one or two letter descriptor, four-digit number.
        FactsIdentifierFamily.Celex => IsCelex(value),

        // ECLI:country:court:year:ordinal, five colon-separated parts.
        FactsIdentifierFamily.Ecli => IsEcli(value),

        FactsIdentifierFamily.CellarWorkUri => IsCellarUri(value, "/resource/"),
        FactsIdentifierFamily.CellarResourceUri => IsCellarUri(value, "/resource/"),

        // ELI is a publisher path expression, not a URI, and both publishers mint one.
        FactsIdentifierFamily.Eli => value.StartsWith("eli/", StringComparison.Ordinal) &&
            !value.Contains(' ', StringComparison.Ordinal),

        FactsIdentifierFamily.Memorial or FactsIdentifierFamily.HistoricalLegalId =>
            value.Length > 0 && !value.Contains(' ', StringComparison.Ordinal),

        _ => false,
    };

    private static bool IsCelex(string value)
    {
        if (value.Length is < 8 or > 30 || value[0] is < '0' or > '9')
        {
            return false;
        }

        for (var index = 1; index <= 4; index++)
        {
            if (value[index] is < '0' or > '9')
            {
                return false;
            }
        }

        var descriptor = 0;
        var cursor = 5;
        while (cursor < value.Length && value[cursor] is >= 'A' and <= 'Z')
        {
            descriptor++;
            cursor++;
        }

        if (descriptor is < 1 or > 2 || cursor >= value.Length)
        {
            return false;
        }

        for (; cursor < value.Length; cursor++)
        {
            if (value[cursor] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsEcli(string value)
    {
        var parts = value.Split(':');
        if (parts.Length != 5 || !string.Equals(parts[0], "ECLI", StringComparison.Ordinal))
        {
            return false;
        }

        if (parts[1].Length != 2 || parts[1].Any(c => c is < 'A' or > 'Z'))
        {
            return false;
        }

        if (parts[2].Length == 0 || parts[3].Length != 4 || parts[3].Any(c => c is < '0' or > '9'))
        {
            return false;
        }

        return parts[4].Length > 0 && parts[4].All(c => c is (>= '0' and <= '9') or (>= 'A' and <= 'Z'));
    }

    /// <summary>
    /// A Cellar URI is on the publisher's own host over https, not any URI that happens to
    /// contain a familiar path segment.
    /// </summary>
    private static bool IsCellarUri(string value, string requiredSegment) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" &&
        string.Equals(uri.Host, "publications.europa.eu", StringComparison.Ordinal) &&
        uri.AbsolutePath.Contains(requiredSegment, StringComparison.Ordinal);
}

/// <summary>
/// Everything one publisher says identifies a single thing, kept together.
/// </summary>
/// <remarks>
/// <para>
/// A EUR-Lex case is a Cellar work URI, a CELEX number and an ECLI at once, and one family per
/// endpoint would mean retaining any one of them discards the other two.
/// </para>
/// <para>
/// Each family is bound to the publisher that mints it, no family repeats, and <b>no raw value
/// appears twice under different families</b>: Candidate 2 admitted one URI as both
/// <c>cellar_work_uri</c> and <c>cellar_resource_uri</c> in a single identity, which is two
/// contradictory level claims about one string.
/// </para>
/// <para>
/// The list keeps the publisher's own order, because that order is evidence about what was
/// served. Identity comparison is <b>canonical and order independent</b>, because RDF row order
/// is not stable and Candidate 2 made inverse and inbound validation depend on it: the same three
/// members in reverse order compared unequal.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OfficialIdentitySet
{
    /// <summary>Which publisher may mint each family.</summary>
    private static readonly IReadOnlyDictionary<FactsIdentifierFamily, PublisherId?> MintedBy =
        new Dictionary<FactsIdentifierFamily, PublisherId?>
        {
            [FactsIdentifierFamily.Celex] = PublisherId.EuEurLex,
            [FactsIdentifierFamily.Ecli] = PublisherId.EuEurLex,
            [FactsIdentifierFamily.CellarWorkUri] = PublisherId.EuEurLex,
            [FactsIdentifierFamily.CellarResourceUri] = PublisherId.EuEurLex,
            [FactsIdentifierFamily.Memorial] = PublisherId.LuLegilux,
            [FactsIdentifierFamily.HistoricalLegalId] = PublisherId.LuLegilux,
            // Both publishers mint ELI.
            [FactsIdentifierFamily.Eli] = null,
        };

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
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identifier in copied)
        {
            if (!families.Add(identifier.Family))
            {
                throw new ArgumentException(
                    $"An identity set cannot repeat the {identifier.Family} family.",
                    nameof(identifiers));
            }

            if (!values.Add(identifier.RawValue))
            {
                throw new ArgumentException(
                    "One raw value cannot be claimed under two families in one identity.",
                    nameof(identifiers));
            }

            if (MintedBy[identifier.Family] is { } minter && minter != publisher)
            {
                throw new ArgumentException(
                    $"{publisher} does not mint {identifier.Family} identifiers.",
                    nameof(identifiers));
            }
        }

        Publisher = publisher;
        Identifiers = Array.AsReadOnly(copied);
    }

    public PublisherId Publisher { get; }

    /// <summary>The identifiers in the publisher's own order, which is itself evidence.</summary>
    public IReadOnlyList<OfficialIdentifier> Identifiers { get; }

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
    /// Whether this set identifies a court decision, decided by parsed evidence.
    /// </summary>
    /// <remarks>
    /// A well-formed ECLI or a sector 6 CELEX proves it. A URI containing a familiar path segment
    /// does not, and a caller's choice of family label cannot manufacture it, because the label is
    /// only accepted when the value parses as that family.
    /// </remarks>
    [JsonIgnore]
    public bool IsCase => Identifiers.Any(static identifier => identifier.ProvesCase);

    /// <summary>Canonical, order-independent identity equality.</summary>
    public bool SameIdentity(OfficialIdentitySet? other)
    {
        if (other is null || other.Publisher != Publisher ||
            other.Identifiers.Count != Identifiers.Count)
        {
            return false;
        }

        foreach (var identifier in Identifiers)
        {
            if (!string.Equals(
                    other.Value(identifier.Family),
                    identifier.RawValue,
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

    /// <summary>An absolute http or https URI, which `UriKind.Absolute` alone does not mean.</summary>
    internal static bool IsHttpsUri(string? value) =>
        value is not null &&
        value.Length is > 0 and <= 2000 &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "https";
}
