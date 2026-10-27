using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// What in-file rights reading /2 found in one retained publisher file (LU-DATA-DESIGN v2 §1.1). Closed. The first five say
/// what the file states for the fetched manifestation; the last four are rejections, each for a reason the reader names, and a
/// rejection outranks anything the SPARQL channel carries (resolution /2, rule 3).
/// </summary>
public enum LuxembourgInFileRightsReadingV2Status
{
    /// <summary>One <c>scl:JOLUXManifestation</c> of the document's own identification names the fetched manifestation and states a licence.</summary>
    [JsonStringEnumMemberName("bound")]
    Bound = 1,

    /// <summary>The document's own block names the fetched manifestation and states no licence.</summary>
    [JsonStringEnumMemberName("own_block_empty")]
    OwnBlockEmpty = 2,

    /// <summary>No block names the fetched manifestation, and the file states a licence somewhere (for another identity).</summary>
    [JsonStringEnumMemberName("unbound")]
    Unbound = 3,

    /// <summary>No block names the fetched manifestation, and the file states no licence anywhere.</summary>
    [JsonStringEnumMemberName("silent")]
    Silent = 4,

    /// <summary>The representation is not one this channel can read (a PDF): not a defect of the file.</summary>
    [JsonStringEnumMemberName("not_applicable")]
    NotApplicable = 5,

    [JsonStringEnumMemberName("malformed_xml")]
    MalformedXml = 6,

    /// <summary>The root is not Akoma Ntoso, or it does not hold exactly one document element (a leading <c>scl:postit</c> aside), or the document holds more than one <c>meta</c> or identification.</summary>
    [JsonStringEnumMemberName("root_rejected")]
    RootRejected = 7,

    /// <summary>More than one block of the document's own identification names the fetched manifestation, or a block naming it names another identity too.</summary>
    [JsonStringEnumMemberName("ambiguous_binding")]
    AmbiguousBinding = 8,

    /// <summary>A licence declaration carries markup, or a value that is not one exact absolute IRI.</summary>
    [JsonStringEnumMemberName("invalid_licence_iri")]
    InvalidLicenceIri = 9,
}

/// <summary>One identity a <c>scl:JOLUXManifestation</c> block of the file names (its <c>uriThis</c>, verbatim), with that block's licence values.</summary>
public sealed record LuxembourgInFileNamedIdentity
{
    public LuxembourgInFileNamedIdentity(string identity, IReadOnlyList<string> licenceIris)
    {
        Identity = LuxembourgSourceValidation.RequireScalarString(identity, nameof(identity));
        LicenceIris = LuxembourgInFileRightsReadingV2.SortedLicences(licenceIris, nameof(licenceIris));
    }

    /// <summary>The block's <c>uriThis</c> as the file writes it; never normalized.</summary>
    public string Identity { get; }

    /// <summary>The block's licence values, sorted and distinct.</summary>
    public IReadOnlyList<string> LicenceIris { get; }
}

/// <summary>
/// In-file rights reading /2 of one retained publisher file: the licence the document's own block states for the fetched
/// manifestation, every licence the file declares anywhere, and every identity its blocks name. Reading /1
/// (<c>LuxembourgInFileRightsReader.ReadAsync</c>) stays as the acquisition ran it; this is the later rule a re-derivation
/// runs. The record states what the file says; whether the licence is cleared is resolution /2's question
/// (<see cref="LuxembourgRightsChannels.ResolveV2"/>), and whether that admits is the policy's.
/// </summary>
public sealed record LuxembourgInFileRightsReadingV2
{
    public LuxembourgInFileRightsReadingV2(
        string manifestationIri,
        SourceArtifactRef bodyRef,
        LuxembourgInFileRightsReadingV2Status status,
        IReadOnlyList<string> ownBlockLicenceIris,
        IReadOnlyList<string> declaredLicenceIris,
        IReadOnlyList<LuxembourgInFileNamedIdentity> namedIdentities,
        IReadOnlyList<string> frbrManifestationIdentities)
    {
        ManifestationIri = LuxembourgSourceValidation.RequireExactResourceIri(manifestationIri, nameof(manifestationIri));
        BodyRef = bodyRef ?? throw new ArgumentNullException(nameof(bodyRef));
        Status = LuxembourgSourceValidation.RequireDefined(status, nameof(status));
        OwnBlockLicenceIris = SortedLicences(ownBlockLicenceIris, nameof(ownBlockLicenceIris));
        DeclaredLicenceIris = SortedLicences(declaredLicenceIris, nameof(declaredLicenceIris));
        NamedIdentities = LuxembourgSourceValidation.Copy(namedIdentities, nameof(namedIdentities));
        FrbrManifestationIdentities = Sorted(frbrManifestationIdentities, nameof(frbrManifestationIdentities));
        if (OwnBlockLicenceIris.Any(licence => !DeclaredLicenceIris.Contains(licence, StringComparer.Ordinal)) ||
            NamedIdentities.SelectMany(static identity => identity.LicenceIris)
                .Any(licence => !DeclaredLicenceIris.Contains(licence, StringComparer.Ordinal)))
        {
            throw new ArgumentException("Every licence a block states is one the file declares.", nameof(declaredLicenceIris));
        }

        var consistent = Status switch
        {
            LuxembourgInFileRightsReadingV2Status.Bound => OwnBlockLicenceIris.Count > 0,
            LuxembourgInFileRightsReadingV2Status.OwnBlockEmpty => OwnBlockLicenceIris.Count == 0,
            LuxembourgInFileRightsReadingV2Status.Unbound => OwnBlockLicenceIris.Count == 0 && DeclaredLicenceIris.Count > 0,
            LuxembourgInFileRightsReadingV2Status.Silent => OwnBlockLicenceIris.Count == 0 && DeclaredLicenceIris.Count == 0,
            _ => OwnBlockLicenceIris.Count == 0 && DeclaredLicenceIris.Count == 0 && NamedIdentities.Count == 0,
        };
        if (!consistent)
        {
            throw new ArgumentException($"A reading /2 of status {Status} cannot carry these licence values.", nameof(status));
        }
    }

    /// <summary>The fetched manifestation, the one the custody holds the body of.</summary>
    public string ManifestationIri { get; }

    /// <summary>The retained body read, by digest.</summary>
    public SourceArtifactRef BodyRef { get; }

    public LuxembourgInFileRightsReadingV2Status Status { get; }

    /// <summary>The licence values of the own block: the block of the document's own identification whose <c>uriThis</c> is the fetched manifestation.</summary>
    public IReadOnlyList<string> OwnBlockLicenceIris { get; }

    /// <summary>Every <c>scl:jolux[@scl:name='license']</c> value anywhere in the document, sorted and distinct: what proves no other licence was written.</summary>
    public IReadOnlyList<string> DeclaredLicenceIris { get; }

    /// <summary>Every identity a <c>scl:JOLUXManifestation</c> block anywhere in the document names, with that block's licence values, in document order.</summary>
    public IReadOnlyList<LuxembourgInFileNamedIdentity> NamedIdentities { get; }

    /// <summary>The own identification's <c>FRBRManifestation/FRBRthis</c> values, recorded and never required (spec 34 §5).</summary>
    public IReadOnlyList<string> FrbrManifestationIdentities { get; }

    /// <summary>Whether the reader refused the file (malformed, a rejected root, an ambiguous binding or an invalid licence value).</summary>
    public bool IsRejected => Status is LuxembourgInFileRightsReadingV2Status.MalformedXml
        or LuxembourgInFileRightsReadingV2Status.RootRejected
        or LuxembourgInFileRightsReadingV2Status.AmbiguousBinding
        or LuxembourgInFileRightsReadingV2Status.InvalidLicenceIri;

    /// <summary>
    /// Whether <paramref name="value"/> is one exact absolute IRI, as the rights channels require of a licence: no
    /// whitespace, no backslash, no malformed escape. Nothing is trimmed or repaired.
    /// </summary>
    public static bool IsExactLicenceIri(string value)
    {
        try
        {
            LuxembourgSourceValidation.RequireExactAbsoluteIri(value, nameof(value));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static IReadOnlyList<string> SortedLicences(IReadOnlyList<string> values, string parameterName)
    {
        var sorted = Sorted(values, parameterName);
        foreach (var licence in sorted)
        {
            LuxembourgSourceValidation.RequireExactAbsoluteIri(licence, parameterName);
        }

        return sorted;
    }

    private static IReadOnlyList<string> Sorted(IReadOnlyList<string> values, string parameterName) =>
        LuxembourgSourceValidation.CopyStrings(
            LuxembourgSourceValidation.Copy(values, parameterName)
                .Distinct(StringComparer.Ordinal)
                .Order(LuxembourgSourceValidation.UnicodeScalarComparer)
                .ToArray(),
            parameterName);
}

/// <summary>
/// The publisher catalogue's licences per manifestation, from the same SPARQL rights evidence the run observed (its typed
/// <c>jolux:license</c> assertions): what resolution /2 checks every identity a file names against. An identity with no
/// assertion is unknown; an assertion whose object is not one exact IRI is kept as unrepresentable, so the identity never
/// reads as cleanly licensed.
/// </summary>
public sealed class LuxembourgRightsCatalogue
{
    private readonly Dictionary<string, (SortedSet<string> Licences, int Unrepresentable)> _byManifestation;

    private LuxembourgRightsCatalogue(Dictionary<string, (SortedSet<string> Licences, int Unrepresentable)> byManifestation)
    {
        _byManifestation = byManifestation;
    }

    /// <summary>An empty catalogue: every identity is unknown.</summary>
    public static LuxembourgRightsCatalogue Empty { get; } = new(new Dictionary<string, (SortedSet<string>, int)>(StringComparer.Ordinal));

    /// <summary>
    /// The catalogue of <paramref name="assertions"/>: each a manifestation and its licence IRI, or null for a
    /// <c>jolux:license</c> assertion whose object is not an exact IRI.
    /// </summary>
    public static LuxembourgRightsCatalogue From(IEnumerable<(string ManifestationIri, string? LicenceIri)> assertions)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        var map = new Dictionary<string, (SortedSet<string> Licences, int Unrepresentable)>(StringComparer.Ordinal);
        foreach (var (manifestation, licence) in assertions)
        {
            LuxembourgSourceValidation.RequireScalarString(manifestation, nameof(assertions));
            if (!map.TryGetValue(manifestation, out var entry))
            {
                entry = (new SortedSet<string>(StringComparer.Ordinal), 0);
            }

            var exact = licence is not null && IsExactAbsoluteIri(licence);
            if (exact)
            {
                entry.Licences.Add(licence!);
            }

            map[manifestation] = exact ? entry : (entry.Licences, entry.Unrepresentable + 1);
        }

        return new(map);
    }

    /// <summary>How many manifestations the catalogue knows.</summary>
    public int Count => _byManifestation.Count;

    /// <summary>Whether the catalogue holds a licence assertion for <paramref name="identity"/>.</summary>
    public bool Knows(string identity) => _byManifestation.ContainsKey(identity);

    /// <summary>
    /// Whether the catalogue knows <paramref name="identity"/> and says anything other than exactly <paramref name="licence"/>
    /// for it: licenceSCL, a double licence, another IRI, or an assertion it cannot represent.
    /// </summary>
    public bool DisagreesWith(string identity, string licence) =>
        _byManifestation.TryGetValue(identity, out var entry) &&
        (entry.Unrepresentable > 0 || entry.Licences.Count != 1 || !string.Equals(entry.Licences.Min, licence, StringComparison.Ordinal));

    private static bool IsExactAbsoluteIri(string value)
    {
        try
        {
            LuxembourgSourceValidation.RequireExactAbsoluteIri(value, nameof(value));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
