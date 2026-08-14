namespace Lex.Index;

/// <summary>Maps official publisher identities to held local work identifiers.</summary>
public sealed class CitationTargetResolver
{
    private readonly HashSet<string> _heldWorks;
    private readonly IReadOnlyDictionary<string, string> _officialAliases;
    private readonly string _collection;

    public CitationTargetResolver(
        string collection,
        IEnumerable<DocRow> documents)
    {
        _collection = collection;
        var docs = documents.ToArray();
        _heldWorks = docs.Select(document => document.GroupKey).ToHashSet(StringComparer.Ordinal);
        var pairs = docs.SelectMany(document => document.PublisherMetadata ?? [],
                (document, metadata) => (document.GroupKey, Metadata: metadata))
            .Where(item => item.Metadata.CitationIdentity)
            .Select(item => (Alias: AliasSlug(item.Metadata.Identifier), Work: item.GroupKey))
            .Distinct().ToArray();
        var collision = pairs.GroupBy(item => item.Alias, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Select(item => item.Work)
                .Distinct(StringComparer.Ordinal).Count() > 1);
        if (collision is not null)
            throw new InvalidDataException(
                $"Official citation identity '{collision.Key}' maps to multiple held works.");
        _officialAliases = pairs.ToDictionary(item => item.Alias, item => item.Work,
            StringComparer.Ordinal);
    }

    public string? CanonicalWork(string? derivedSlug)
    {
        if (derivedSlug is not null
            && _officialAliases.TryGetValue(derivedSlug, out var official))
            return $"{_collection}:{official}";
        if (derivedSlug is not null)
            foreach (var alias in _officialAliases
                         .OrderByDescending(item => item.Key.Length)
                         .ThenBy(item => item.Key, StringComparer.Ordinal))
                if (derivedSlug.StartsWith(alias.Key + "-", StringComparison.Ordinal))
                    return $"{_collection}:{alias.Value}";
        return derivedSlug is not null && _heldWorks.Contains(derivedSlug)
            ? $"{_collection}:{derivedSlug}"
            : null;
    }

    private static string AliasSlug(string identifier)
    {
        if (!Uri.TryCreate(identifier, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            throw new InvalidDataException(
                "Official citation identity must be an absolute HTTP(S) URI.");
        const string marker = "/eli/etat/leg/";
        var index = identifier.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            throw new InvalidDataException(
                $"Official citation identity '{identifier}' is not an ELI legal-resource URI.");
        var tail = identifier[(index + marker.Length)..].Trim('/');
        if (tail.Length == 0)
            throw new InvalidDataException("Official citation identity has no ELI path.");
        return tail.Replace('/', '-');
    }
}
