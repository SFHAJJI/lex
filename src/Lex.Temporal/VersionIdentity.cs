using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lex.Temporal;

/// <summary>
/// Defines the persisted identity of one publisher version. The publisher identifier, not the
/// number of versions currently known for a date, makes the coordinate stable as the set grows.
/// </summary>
public static class VersionIdentity
{
    public const int HashLength = 64;

    public static string Create(DateOnly validFrom, string publisherVersionIdentifier)
    {
        if (string.IsNullOrWhiteSpace(publisherVersionIdentifier))
            throw new InvalidDataException("publisher_version_identifier is required");
        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(publisherVersionIdentifier)));
        return validFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + $"--{hash}";
    }

    public static string RequireCanonical(
        string directoryKey,
        string validFrom,
        string? publisherVersionIdentifier,
        string lexId,
        string expectedLexIdPrefix)
    {
        if (!DateOnly.TryParseExact(validFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            throw new InvalidDataException($"valid_from '{validFrom}' is not an ISO date");
        var expectedKey = Create(date, publisherVersionIdentifier ?? "");
        if (!string.Equals(directoryKey, expectedKey, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"version directory '{directoryKey}' does not match canonical key '{expectedKey}'");
        var expectedLexId = $"{expectedLexIdPrefix}:{expectedKey}";
        if (!string.Equals(lexId, expectedLexId, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"lex_id '{lexId}' does not match canonical identity '{expectedLexId}'");
        return expectedKey;
    }

    public static string DateOf(string canonicalKey)
    {
        if (canonicalKey.Length != 12 + HashLength
            || canonicalKey[10..12] != "--"
            || !DateOnly.TryParseExact(canonicalKey[..10], "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            || canonicalKey[12..].Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException($"version key '{canonicalKey}' is not canonical");
        return canonicalKey[..10];
    }
}
