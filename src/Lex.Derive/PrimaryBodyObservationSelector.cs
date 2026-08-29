namespace Lex.Derive;

internal readonly record struct PrimaryBodyObservationShape(
    string? File,
    string? Format,
    bool HasHttp,
    string? AttemptOutcome);

/// <summary>
/// Selects the exact publisher body that derivation may consume and indexing may bind.
/// </summary>
internal static class PrimaryBodyObservationSelector
{
    internal static T? Select<T>(
        IEnumerable<T> observations,
        Func<T, PrimaryBodyObservationShape> shape)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(shape);

        return observations.LastOrDefault(observation =>
        {
            var candidate = shape(observation);
            return candidate.Format is null
                && IsPortableLeaf(candidate.File)
                && Path.GetExtension(candidate.File) is ".xml" or ".html" or ".body"
                && (!candidate.HasHttp
                    || string.Equals(candidate.AttemptOutcome, "retrieved",
                        StringComparison.Ordinal));
        });
    }

    private static bool IsPortableLeaf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.EndsWith(' ') || value.EndsWith('.')
            || value.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0
            || !string.Equals(Path.GetFileName(value), value,
                StringComparison.Ordinal)
            || value.Any(character => character < ' '))
            return false;

        var stem = Path.GetFileNameWithoutExtension(value).ToUpperInvariant();
        return stem is not (
            "CON" or "PRN" or "AUX" or "NUL"
            or "COM1" or "COM2" or "COM3" or "COM4" or "COM5"
            or "COM6" or "COM7" or "COM8" or "COM9"
            or "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5"
            or "LPT6" or "LPT7" or "LPT8" or "LPT9");
    }
}
