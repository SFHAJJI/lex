using Lex.Law;

namespace Lex.Ingest;

internal static class PublisherMetadataValidation
{
    private static readonly HashSet<string> AllowedKinds = new(StringComparer.Ordinal)
    {
        "publisher_short_title", "eurovoc", "directory", "eurovoc_alt_label",
        "eurovoc_broader", "eurovoc_subdomain", "eurovoc_domain", "legilux_same_as",
        "legilux_subject_level1_theme", "legilux_subject_level1_organisation",
        "legilux_subject_level1_place", "legilux_subject_level1_legal_resource",
        "legilux_subject_level1_country", "legilux_subject_level2_theme",
        "legilux_subject_level2_organisation", "legilux_subject_level2_place",
        "legilux_subject_level2_legal_resource", "legilux_subject_level2_country",
    };

    internal static List<PublisherMetadataRecord> Canonicalize(
        IReadOnlyList<PublisherMetadataRecord>? values)
    {
        var source = values ?? [];
        if (source.Count > 512 || source.Any(value => value is null))
            throw new InvalidDataException(
                "A publisher version exceeds 512 official metadata records or contains a null record.");
        var canonical = source.Distinct()
            .OrderBy(value => value.Kind, StringComparer.Ordinal)
            .ThenBy(value => value.Identifier, StringComparer.Ordinal)
            .ThenBy(value => value.Language, StringComparer.Ordinal)
            .ThenBy(value => value.Label, StringComparer.Ordinal)
            .ThenBy(value => value.SourceUri, StringComparer.Ordinal)
            .ToList();
        foreach (var value in canonical) Validate(value);
        return canonical;
    }

    private static void Validate(PublisherMetadataRecord value)
    {
        if (string.IsNullOrWhiteSpace(value.Kind) || !AllowedKinds.Contains(value.Kind)
            || string.IsNullOrWhiteSpace(value.Identifier) || value.Identifier.Length > 2048
            || string.IsNullOrWhiteSpace(value.SourceUri) || value.SourceUri.Length > 2048
            || string.IsNullOrWhiteSpace(value.Label)
            || value.Label.Length > 4096
            || !HttpUri(value.Identifier) || !HttpUri(value.SourceUri))
            throw new InvalidDataException(
                "Publisher metadata has an unsupported kind or invalid official URI/label.");
        if (value.Kind == "legilux_same_as")
        {
            if (value.Language is not null || value.SourceUri != value.Identifier)
                throw new InvalidDataException(
                    "Legilux same-as metadata must be language-neutral and self-sourced.");
            return;
        }
        if (value.Language is not ("en" or "fr")
            || value.Kind.StartsWith("legilux_subject_", StringComparison.Ordinal)
               && value.Language != "fr"
            || value.Kind != "publisher_short_title" && value.SourceUri != value.Identifier)
            throw new InvalidDataException(
                "Publisher metadata has invalid language or source provenance.");
        if (value.Kind == "publisher_short_title")
        {
            var segments = value.Label.Split(',').Select(segment => segment.Trim()).ToArray();
            if (segments.Length is 0 or > 16
                || segments.Any(segment => segment.Length is 0 or > 256))
                throw new InvalidDataException(
                    "Publisher short title must contain 1 to 16 comma segments of at most 256 characters.");
        }
    }

    private static bool HttpUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";
}
