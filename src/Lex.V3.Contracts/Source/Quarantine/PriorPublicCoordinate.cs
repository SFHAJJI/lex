using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Contracts.Source.Quarantine;

/// <summary>
/// One coordinate a retired V2 index used to resolve a public permalink: the opaque work key
/// (V2's <c>lex_id</c>), language, dated-version validity start and, for a provision-level
/// permalink, the publisher-minted anchor fragment. These four fields are exactly what V2's own
/// <c>src/Lex.Index/Rows.cs</c> uses to key a row: a version <c>Rid</c> is
/// <c>"key|language|valid_from"</c> and a provision <c>ProvisionId</c> is
/// <c>"lex_id#anchor"</c>. A version-level coordinate carries no anchor; a provision-level one
/// does.
/// </summary>
/// <remarks>
/// <para>
/// Structural boundary (Decision 71 and the standing V2-eradication rule): this record has no
/// field that can carry law text, a byte payload, a stream, or a filesystem path -- only four
/// bounded strings naming a location, never its content. A permalink coordinate and the provision
/// text that used to live behind it are different things; V2's own <c>Rows.cs</c> draws the
/// identical line for <c>DocRow.Body</c> ("reconstructed from provisions on demand (never stored
/// in lex-index/2)"). Nothing here can be pointed at <c>works/</c>, a law
/// <c>*.xml</c>/<c>*.html</c>/<c>*.json</c> file, or any byte content: there is no constructor
/// parameter, property, or producer anywhere in this type whose type is <see cref="byte"/>[],
/// <see cref="System.IO.Stream"/>, or a path. That absence, not the bounded-ASCII rejection in
/// <see cref="QuarantineCoordinateValidation"/>, is what makes it structurally impossible; the
/// rejection is defence in depth on top of it, not the guarantee itself.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PriorPublicCoordinate
{
    [JsonConstructor]
    public PriorPublicCoordinate(string workKey, string language, string validFrom, string? anchor)
    {
        WorkKey = QuarantineCoordinateValidation.RequireOpaqueKey(workKey, nameof(workKey));
        Language = RoutedHttpValidation.RequireLanguage(language, nameof(language));
        ValidFrom = QuarantineCoordinateValidation.RequireIsoDate(validFrom, nameof(validFrom));
        Anchor = anchor is null
            ? null
            : QuarantineCoordinateValidation.RequireOpaqueKey(anchor, nameof(anchor));
    }

    /// <summary>The opaque V2 <c>lex_id</c> this coordinate names. Never a filesystem path.</summary>
    public string WorkKey { get; }

    public string Language { get; }

    /// <summary>The dated version's validity start, exact <c>yyyy-MM-dd</c>.</summary>
    public string ValidFrom { get; }

    /// <summary>The publisher-minted anchor fragment for a provision-level coordinate; null for a version-level one.</summary>
    public string? Anchor { get; }
}

internal static class QuarantineCoordinateValidation
{
    private const int MaximumKeyLength = 512;

    private static readonly string[] ForbiddenLawContentExtensions =
        [".xml", ".html", ".htm", ".json"];

    /// <summary>
    /// A bounded, opaque, printable-ASCII token. Rejects anything shaped like a URI, a path
    /// traversal sequence, a <c>works/</c> path segment, or a law-content file name, so a caller
    /// cannot smuggle a real file path or a network locator through a coordinate field even though
    /// the type has no dedicated path parameter to reject in the first place.
    /// </summary>
    public static string RequireOpaqueKey(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaximumKeyLength ||
            value.Any(static character => character is < '!' or > '~') ||
            value.Contains("..", StringComparison.Ordinal) ||
            value.Contains("://", StringComparison.Ordinal) ||
            value.StartsWith("works/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("/works/", StringComparison.OrdinalIgnoreCase) ||
            ForbiddenLawContentExtensions.Any(extension =>
                value.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "A prior coordinate key must be a bounded opaque token, never a URI, a path "
                + "traversal sequence, a works/ path segment or a law-content file name.",
                parameterName);
        }

        return value;
    }

    public static string RequireIsoDate(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!DateOnly.TryParseExact(
                value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ArgumentException(
                "A prior coordinate's valid-from date must be an exact yyyy-MM-dd calendar date.",
                parameterName);
        }

        return value;
    }
}

/// <summary>
/// Deterministic, order-independent encoding of a coordinate list, used to derive a reproduction's
/// digest from its own content and nothing else.
/// </summary>
/// <remarks>
/// Coordinates are sorted ordinally by (work key, language, valid-from, anchor presence, anchor)
/// before encoding, so two independent walks of the same true V2 index that happened to enumerate
/// rows in different orders normalize to identical bytes here, while two walks that disagree on
/// the underlying content do not. Each field is length-prefixed so no pair of adjacent fields can
/// be re-cut into a different pair that hashes the same way. There is no separate anchor-presence
/// flag byte: <see cref="PriorPublicCoordinate.Anchor"/> is either null or a non-empty bounded
/// token (its own constructor rejects a blank anchor), so the anchor field's own length prefix
/// already distinguishes a version-level coordinate ("0:|", zero bytes) from a provision-level one
/// ("N:value|", N &gt;= 1) without a redundant extra byte encoding the same fact twice.
/// </remarks>
public static class PriorPublicCoordinateSet
{
    /// <summary>
    /// The deterministic (work key, language, valid-from, anchor) ordering both
    /// <see cref="CanonicalBytes"/> and the inventory-level signable form in
    /// <c>QuarantineInventoryCanonicalizer</c> iterate coordinates in, exposed so both places sort
    /// exactly once, the same way, rather than risking two independently written orderings drifting
    /// apart.
    /// </summary>
    internal static IReadOnlyList<PriorPublicCoordinate> Ordered(
        IReadOnlyList<PriorPublicCoordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        return coordinates
            .OrderBy(static coordinate => coordinate.WorkKey, StringComparer.Ordinal)
            .ThenBy(static coordinate => coordinate.Language, StringComparer.Ordinal)
            .ThenBy(static coordinate => coordinate.ValidFrom, StringComparer.Ordinal)
            .ThenBy(static coordinate => coordinate.Anchor is null ? 0 : 1)
            .ThenBy(static coordinate => coordinate.Anchor ?? string.Empty, StringComparer.Ordinal)
            .ToArray();
    }

    public static byte[] CanonicalBytes(IReadOnlyList<PriorPublicCoordinate> coordinates)
    {
        var builder = new StringBuilder();
        foreach (var coordinate in Ordered(coordinates))
        {
            AppendField(builder, coordinate.WorkKey);
            AppendField(builder, coordinate.Language);
            AppendField(builder, coordinate.ValidFrom);
            AppendField(builder, coordinate.Anchor ?? string.Empty);
            builder.Append('\n');
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static string CanonicalSha256Hex(IReadOnlyList<PriorPublicCoordinate> coordinates) =>
        Convert.ToHexString(SHA256.HashData(CanonicalBytes(coordinates))).ToLowerInvariant();

    private static void AppendField(StringBuilder builder, string value)
    {
        var length = Encoding.UTF8.GetByteCount(value);
        builder.Append(length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('|');
    }
}
