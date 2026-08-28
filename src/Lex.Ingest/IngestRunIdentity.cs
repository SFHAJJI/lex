using System.Globalization;

namespace Lex.Ingest;

internal static class IngestRunIdentity
{
    public const int MaximumLength = 128;

    public static string Require(string? value, string description)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > MaximumLength
            || !IsAsciiAlphaNumeric(value[0])
            || value.Any(character => !IsAllowed(character)))
            throw new InvalidDataException(
                $"{description} must be 1 to {MaximumLength} bounded ASCII identity characters.");

        if (value.Length >= 20
            && value[4] == '-' && value[7] == '-' && value[10] == 'T'
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _))
            throw new InvalidDataException(
                $"{description} must identify the completed source run, not its timestamp.");

        return value;
    }

    private static bool IsAllowed(char value) => IsAsciiAlphaNumeric(value)
        || value is '.' or '_' or ':' or '/' or '-' or '@';

    private static bool IsAsciiAlphaNumeric(char value) =>
        value is >= 'a' and <= 'z'
        or >= 'A' and <= 'Z'
        or >= '0' and <= '9';
}
