using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Facts;

/// <summary>
/// The single custody coordinate a Fact carries.
/// </summary>
/// <remarks>
/// Candidate rounds three through seven carried a <c>SourceObservationReference</c> holding an
/// identity and an <c>observed_at</c>. The accepted ruling is that Facts carry exactly
/// <c>source_observation_id</c>, and the timestamp was a second projection of a record this
/// package does not own: <c>http_observation/1</c> holds the authoritative instant, so a Fact
/// repeating it can contradict it, and nothing here could detect the contradiction. Removing the
/// byte reference and keeping the timestamp closed half the hole and left the half that can
/// disagree with the publisher record.
/// </remarks>
public static class SourceObservation
{
    public static string Require(string? sourceObservationId, string parameterName)
    {
        if (!FactsValidation.IsOpaqueIdentity(sourceObservationId))
        {
            throw new ArgumentException(
                "An observation identity must be 1 to 200 printable ASCII characters.",
                parameterName);
        }

        return sourceObservationId!;
    }
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
    public char? CelexSector() =>
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
    public bool ProvesCase() =>
        Family == FactsIdentifierFamily.Ecli ||
        (Family == FactsIdentifierFamily.Celex && RawValue[0] == '6');

    /// <summary>
    /// The admitted profile a value matches, or <c>null</c> where it matches none.
    /// </summary>
    /// <remarks>
    /// Candidate 3 wrote one narrow CELEX shape and refused four identities the accepted V3 scope
    /// requires, including <c>12012E/TXT</c>, which is already in the accepted 82-seed plan. That
    /// is a no-loss violation produced by over-correcting: the previous candidate trusted any
    /// label, so this one refused anything it had not thought of. Both lose publisher facts.
    ///
    /// <b>A value outside every profile is refused, not reported as drift.</b> Candidate 4's
    /// declaration said it was "reported as drift rather than silently refused", and no drift
    /// carrier for identifier profiles exists anywhere in this package: <c>ProfileOf</c> returns
    /// null and the constructor throws. The claim is narrowed to what the code does rather than a
    /// carrier being invented to match a sentence. A typed identifier-profile drift observation
    /// belongs where observations are processed and carry their own evidence, not here.
    /// </remarks>
    public static CelexProfile? ProfileOf(string value)
    {
        if (value is null || value.Length < 7 || value[0] is < '0' or > '9')
        {
            return null;
        }

        for (var index = 1; index <= 4; index++)
        {
            if (value[index] is < '0' or > '9')
            {
                return null;
            }
        }

        var cursor = 5;
        var letters = 0;
        while (cursor < value.Length && value[cursor] is >= 'A' and <= 'Z')
        {
            letters++;
            cursor++;
        }

        if (letters is < 1 or > 3)
        {
            return null;
        }

        var rest = value[cursor..];

        // Treaty parts: the number position is a slash-separated part name, as in 12012E/TXT.
        if (rest.StartsWith('/'))
        {
            var parts = rest[1..].Split('/');
            return parts.Length > 0 && parts.All(IsUpperAlphanumeric)
                ? CelexProfile.TreatyPart
                : null;
        }

        // A consolidated act carries the consolidation date after the number.
        var consolidated = false;
        var dash = rest.IndexOf('-');
        if (dash >= 0)
        {
            // A real calendar date, not eight digits. Candidate 3 checked only the shape and my
            // declaration claimed the date was "parsed and checked", so 02016R0679-20160231
            // was accepted while I said it could not be. The claim was the defect as much as
            // the code: a review that repeats an author's description verifies nothing.
            if (!IsCalendarDate(rest[(dash + 1)..]))
            {
                return null;
            }

            rest = rest[..dash];
            consolidated = true;
        }

        // A corrigendum carries R(nn) after the number.
        var corrigendum = false;
        var open = rest.IndexOf('(');
        if (open >= 0)
        {
            if (!rest.EndsWith(')') || open == 0 || rest[open - 1] != 'R')
            {
                return null;
            }

            var ordinal = rest[(open + 1)..^1];
            if (ordinal.Length == 0 || !ordinal.All(char.IsAsciiDigit))
            {
                return null;
            }

            rest = rest[..(open - 1)];
            corrigendum = true;
        }

        // Sector 7 national implementing measures carry a country code and a national reference
        // after the act number, as in 72019L1937LUX_202303892. The relation and transposition
        // spine needs them, so refusing the tail refuses a required V3 fact.
        var underscore = rest.IndexOf('_');
        if (underscore >= 0)
        {
            if (value[0] != '7' || consolidated || corrigendum)
            {
                return null;
            }

            var head = rest[..underscore];
            var reference = rest[(underscore + 1)..];
            var digits = head.TakeWhile(char.IsAsciiDigit).Count();
            var country = head[digits..];
            return digits > 0 &&
                country.Length == 3 && country.All(c => c is >= 'A' and <= 'Z') &&
                reference.Length > 0 &&
                reference.All(c => c is (>= '0' and <= '9') or (>= 'A' and <= 'Z'))
                    ? CelexProfile.NationalImplementingMeasure
                    : null;
        }

        if (rest.Length == 0 || !rest.All(char.IsAsciiDigit))
        {
            return null;
        }

        return consolidated
            ? CelexProfile.ConsolidatedAct
            : corrigendum
                ? CelexProfile.Corrigendum
                : CelexProfile.BaseAct;
    }

    /// <summary>Eight digits that name a day that exists.</summary>
    private static bool IsCalendarDate(string value)
    {
        if (value.Length != 8 || !value.All(char.IsAsciiDigit))
        {
            return false;
        }

        var year = int.Parse(value[..4]);
        var month = int.Parse(value.Substring(4, 2));
        var day = int.Parse(value.Substring(6, 2));
        return year >= 1 && month is >= 1 and <= 12 &&
            day >= 1 && day <= DateTime.DaysInMonth(year, month);
    }

    private static bool IsUpperAlphanumeric(string text) =>
        text.Length > 0 && text.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9'));

    /// <summary>The grammar each family declares, checked rather than assumed.</summary>
    public static bool IsWellFormed(FactsIdentifierFamily family, string value) => family switch
    {
        FactsIdentifierFamily.Celex => ProfileOf(value) is not null,

        // ECLI:country:court:year:ordinal, five colon-separated parts.
        FactsIdentifierFamily.Ecli => IsEcli(value),

        // The two Cellar families are distinguished by WEMI level, not by the caller's label.
        FactsIdentifierFamily.CellarWorkUri => IsCellarUri(value, work: true),
        FactsIdentifierFamily.CellarResourceUri => IsCellarUri(value, work: false),

        // ELI is minted by both publishers in different lexical shapes, so the family alone is
        // not the check. `EliMintedBy` decides which publisher a given shape belongs to, and the
        // identity set requires that to be the publisher carrying it.
        FactsIdentifierFamily.Eli => EliMintedBy(value) is not null,

        FactsIdentifierFamily.CellarPsiUri => IsCellarPsi(value),

        FactsIdentifierFamily.Memorial or FactsIdentifierFamily.HistoricalLegalId =>
            value.Length > 0 && !value.Contains(' ', StringComparison.Ordinal),

        _ => false,
    };

    /// <summary>
    /// The raw value is a URI spelled exactly, with no character a URI may not carry literally.
    /// </summary>
    /// <remarks>
    /// .NET percent-encodes a literal space before <c>AbsolutePath</c> is read, so
    /// <c>.../cellar/&lt;uuid&gt;/DOC 1</c> parsed to a clean path and the reader accepted a
    /// spelling the schema refuses. Parsing normalises; a publisher identifier is not a thing to
    /// normalise, it is a thing to record as stated or refuse. The check is on the raw string,
    /// before any parser has a chance to be helpful.
    /// </remarks>
    private static bool IsExactUriSpelling(string value) =>
        value.Length > 0 && value.All(c => c is > ' ' and <= '~');

    /// <summary>
    /// The raw value begins with the exact Cellar authority, checked ordinally before any parser
    /// sees it.
    /// </summary>
    /// <remarks>
    /// <c>System.Uri</c> lowercases the scheme and host, drops an explicit default port, and moves
    /// userinfo out of <c>Host</c>, so <c>HTTPS://PUBLICATIONS.EUROPA.EU/...</c>,
    /// <c>...europa.eu:443/...</c> and <c>...//user@publications.europa.eu/...</c> all reached the
    /// same accepted parse while the anchored schema pattern refused every one of them. Checking
    /// the parsed authority can only ever compare what the parser decided to keep; checking the
    /// raw prefix compares what the publisher actually wrote, which is the thing being recorded.
    /// </remarks>
    private static bool HasExactCellarAuthority(string value) =>
        value.StartsWith("http://publications.europa.eu/resource/", StringComparison.Ordinal) ||
        value.StartsWith("https://publications.europa.eu/resource/", StringComparison.Ordinal);

    /// <summary>
    /// Which publisher mints an ELI of this exact shape, or null where no publisher does.
    /// </summary>
    /// <remarks>
    /// Candidate 4 admitted both shapes for either publisher, so an EU identity could carry the
    /// Luxembourg relative path and a Luxembourg identity could carry the EU absolute URI. The
    /// host allowlist stopped an invented host and did nothing about a cross-publisher one, which
    /// is authority still resting on the caller's choice of shape.
    /// </remarks>
    public static PublisherId? EliMintedBy(string value)
    {
        if (value is null || value.Contains(' ', StringComparison.Ordinal))
        {
            return null;
        }

        // The relative path expression is Legilux's shape.
        if (value.StartsWith("eli/", StringComparison.Ordinal))
        {
            return PublisherId.LuLegilux;
        }

        // An absolute ELI must be on a publisher host. Candidate 3 accepted any host whose path
        // contained /eli/, so https://example.invalid/eli/... was admissible as an official
        // identifier, which is the caller-shaped lookalike the contract says it never retains.
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !uri.AbsolutePath.Contains("/eli/", StringComparison.Ordinal))
        {
            return null;
        }

        return EliHosts.TryGetValue(uri.Host, out var minter) ? minter : null;
    }

    /// <summary>Each host that mints an absolute ELI, and the publisher it belongs to.</summary>
    private static readonly IReadOnlyDictionary<string, PublisherId> EliHosts =
        new Dictionary<string, PublisherId>(StringComparer.Ordinal)
        {
            ["data.europa.eu"] = PublisherId.EuEurLex,
            ["eur-lex.europa.eu"] = PublisherId.EuEurLex,
            ["data.legilux.public.lu"] = PublisherId.LuLegilux,
            ["legilux.public.lu"] = PublisherId.LuLegilux,
        };

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

        // The court segment had no character class at all, so it was looser than the schema
        // pattern beside it. A control character is already stopped by the printable-ASCII bound,
        // but a printable oddity was not, and reader and schema must admit the same set.
        if (parts[2].Length == 0 ||
            !parts[2].All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9')) ||
            parts[3].Length != 4 || parts[3].Any(c => c is < '0' or > '9'))
        {
            return false;
        }

        return parts[4].Length > 0 &&
            parts[4].All(c => c is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or '.');
    }

    /// <summary>
    /// A Cellar URI on the publisher's own host, at the level its family claims.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Candidate 3 called the identical check for both families, so the caller's enum tag was the
    /// only claimed level distinction and one URI could be admitted as either. The work is
    /// <c>/resource/cellar/&lt;uuid&gt;</c>, where the publisher's predicates actually live.
    /// Candidate 4 admitted any three-segment <c>/resource/&lt;class&gt;/&lt;id&gt;</c>, so
    /// <c>/resource/celex/32016R0679</c> passed as a work. That URI is a persistent-identifier
    /// alias tied to the work by <c>owl:sameAs</c>; treating it as the work relabels an alias as
    /// the thing itself.
    /// </para>
    /// <para>
    /// The resource family previously required a bare work UUID with a further path segment, so it
    /// rejected the publisher's own dotted expression and manifestation identifiers while its
    /// documentation claimed to cover exactly those levels. Cellar identifies an expression as
    /// <c>{work}.{four digits}</c> and a manifestation as <c>{expression}.{two digits}</c>, and all
    /// four shapes were confirmed live against the official endpoint on 2026-09-02: the bare work,
    /// the dotted expression and the dotted manifestation each answered 200 and redirected to their
    /// own distinct <c>rdf/object/full</c>, and <c>{manifestation}/DOC_1</c> answered 200 directly.
    /// A third dotted level answered 404, so the depth ceiling is the publisher's answer rather
    /// than an inference of ours.
    /// </para>
    /// <para>
    /// The two families stay disjoint by shape rather than by the caller's label: a bare UUID with
    /// no further path is the work and only the work, and the resource family requires either a
    /// dotted suffix or a further path segment.
    /// </para>
    /// </remarks>
    private static bool IsCellarUri(string value, bool work)
    {
        if (!IsExactUriSpelling(value) ||
            !HasExactCellarAuthority(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.Equals(uri.Host, "publications.europa.eu", StringComparison.Ordinal) ||
            uri.Query.Length != 0 ||
            uri.Fragment.Length != 0)
        {
            // Round six fixed this on the persistent identifier and left it on the work and the
            // resource, which is the repair-the-instance habit inside the round that named it.
            // `AbsolutePath` discards a query and a fragment, so `.../cellar/<uuid>?view=1` was a
            // different string naming the same parsed path, and two spellings of one identity are
            // two rows to every store and one thing to a reader.
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 3 ||
            !string.Equals(segments[0], "resource", StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(segments[1], "cellar", StringComparison.Ordinal) ||
            CellarObjectDepth(segments[2]) is not { } depth)
        {
            return false;
        }

        // A bare UUID with nothing after it is the work, and only the work. Anything the resource
        // family admits therefore carries either a dotted suffix or a further path segment, so one
        // URI can never satisfy both families.
        return work
            ? segments.Length == 3 && depth == 0
            : segments.Length > 3 || depth > 0;
    }

    /// <summary>
    /// The WEMI depth a Cellar object identifier carries, or <c>null</c> where it is not one.
    /// </summary>
    /// <remarks>
    /// Zero is a work, one an expression, two a manifestation. The digit widths are the
    /// publisher's, four then two, and the ceiling is the publisher's too: a third dotted level
    /// answers 404, so admitting one would mean carrying an identity Cellar does not mint.
    /// </remarks>
    private static int? CellarObjectDepth(string segment)
    {
        var parts = segment.Split('.');
        if (parts.Length > 3 || !Guid.TryParseExact(parts[0], "D", out _))
        {
            return null;
        }

        if (parts.Length >= 2 && !IsExactDigits(parts[1], 4))
        {
            return null;
        }

        if (parts.Length == 3 && !IsExactDigits(parts[2], 2))
        {
            return null;
        }

        return parts.Length - 1;
    }

    private static bool IsExactDigits(string value, int width) =>
        value.Length == width && value.All(static character => character is >= '0' and <= '9');

    /// <summary>A persistent-identifier alias URI, which is a fact in its own right.</summary>
    /// <summary>
    /// The CELEX persistent identifier, exactly. <c>AbsolutePath</c> discards a query and a
    /// fragment, so <c>.../celex/62019CJ0311?view=1</c> was reader-accepted and schema-refused.
    /// The class is also narrowed to <c>celex</c>: an arbitrary resource class is not authority
    /// merely because the caller labelled it a persistent identifier.
    /// </summary>
    private static bool IsCellarPsi(string value) =>
        IsExactUriSpelling(value) &&
        HasExactCellarAuthority(value) &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" &&
        string.Equals(uri.Host, "publications.europa.eu", StringComparison.Ordinal) &&
        uri.Query.Length == 0 &&
        uri.Fragment.Length == 0 &&
        uri.AbsolutePath.Trim('/').Split('/') is { Length: 3 } segments &&
        string.Equals(segments[0], "resource", StringComparison.Ordinal) &&
        string.Equals(segments[1], "celex", StringComparison.Ordinal) &&
        // The terminal segment is a CELEX number, so it is held to the CELEX grammar rather than
        // to "nonempty". Candidate round six admitted any terminal, including a percent-encoded
        // slash, which made a two-segment path spell itself as a one-segment one.
        ProfileOf(segments[2]) is not null;
}

/// <summary>
/// The CELEX shapes the accepted V3 scope requires. A value outside all of them is refused:
/// <see cref="OfficialIdentifier.ProfileOf"/> returns null and the constructor throws.
/// </summary>
/// <remarks>
/// This sentence previously said such a value "is drift, and drift is reported". No
/// identifier-profile drift carrier exists in this package, so that was the same overclaim the
/// remarks on <see cref="OfficialIdentifier.ProfileOf"/> already retract. Narrowing one site and
/// leaving the other is how a retracted claim survives a review round.
/// </remarks>
public enum CelexProfile
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("base_act")]
    BaseAct,

    [System.Text.Json.Serialization.JsonStringEnumMemberName("consolidated_act")]
    ConsolidatedAct,

    [System.Text.Json.Serialization.JsonStringEnumMemberName("corrigendum")]
    Corrigendum,

    [System.Text.Json.Serialization.JsonStringEnumMemberName("treaty_part")]
    TreatyPart,

    /// <summary>
    /// A sector 7 national implementing measure, carrying a country code and national reference
    /// after the act number. Part of the relation and transposition spine, not a future family.
    /// </summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("national_implementing_measure")]
    NationalImplementingMeasure,
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
            [FactsIdentifierFamily.CellarPsiUri] = PublisherId.EuEurLex,
            [FactsIdentifierFamily.Memorial] = PublisherId.LuLegilux,
            [FactsIdentifierFamily.HistoricalLegalId] = PublisherId.LuLegilux,
            // Both publishers mint ELI.
            [FactsIdentifierFamily.Eli] = null,
        };

    /// <remarks>
    /// Candidate 3 carried an <c>IdentifierEnumeration</c> state and a query digest, and let a
    /// caller declare a set complete by passing an enum member and any 64-hex string. A query
    /// digest names which query text was identified. It does not prove the query ran, completed,
    /// exhausted its continuations, returned the whole identifier family, or corresponds to the
    /// set beside it. The fixture's digest was a hand-written constant, which is the tell.
    ///
    /// So the state and the digest are removed rather than decorated. This type carries what it
    /// can prove: the identifiers it holds. A completeness claim needs the D1 cut and observation
    /// evidence, which lives in a later type, and this contract no longer pretends to it.
    /// </remarks>
    [JsonConstructor]
    public OfficialIdentitySet(
        PublisherId publisher,
        IReadOnlyList<OfficialIdentifier> identifiers)
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

            // ELI is minted by both publishers in different shapes, so the shape decides.
            if (identifier.Family == FactsIdentifierFamily.Eli &&
                OfficialIdentifier.EliMintedBy(identifier.RawValue) != publisher)
            {
                throw new ArgumentException(
                    $"{publisher} does not mint an ELI of that shape.",
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
    public bool IsCase() => Identifiers.Any(static identifier => identifier.ProvesCase());

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

        // An identity that is only spaces, or that carries them at either end, is not an identity
        // a publisher stated. Two spellings that differ only in surrounding whitespace are two
        // keys everywhere they are used and one value to a reader, which is the shape that lets a
        // duplicate hide.
        if (value.Trim().Length != value.Length || value.Trim().Length == 0)
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
