namespace Lex.Index;

/// <summary>Maps reviewed and held citation aliases to local work identifiers.</summary>
public sealed class CitationTargetResolver
{
    private readonly HashSet<string> _heldWorks;
    private readonly IReadOnlyDictionary<string, string> _reviewedAliases;
    private readonly string _collection;

    public CitationTargetResolver(
        string collection,
        IEnumerable<string> heldWorks,
        IEnumerable<ReviewedCitationAliasRow>? reviewedAliases = null)
    {
        _collection = collection;
        _heldWorks = heldWorks.ToHashSet(StringComparer.Ordinal);
        _reviewedAliases = (reviewedAliases ?? [])
            .ToDictionary(alias => alias.Alias, alias => alias.Work, StringComparer.Ordinal);
        foreach (var alias in _reviewedAliases)
            if (!_heldWorks.Contains(alias.Value))
                throw new InvalidDataException(
                    $"Reviewed citation alias '{alias.Key}' targets unheld work '{alias.Value}'.");
    }

    public string? CanonicalWork(string? derivedSlug)
    {
        if (derivedSlug is not null
            && _reviewedAliases.TryGetValue(derivedSlug, out var reviewed))
            return $"{_collection}:{reviewed}";
        if (derivedSlug is not null)
            foreach (var alias in _reviewedAliases
                         .OrderByDescending(item => item.Key.Length)
                         .ThenBy(item => item.Key, StringComparer.Ordinal))
                if (derivedSlug.StartsWith(alias.Key + "-", StringComparison.Ordinal))
                    return $"{_collection}:{alias.Value}";
        return derivedSlug is not null && _heldWorks.Contains(derivedSlug)
            ? $"{_collection}:{derivedSlug}"
            : null;
    }
}
