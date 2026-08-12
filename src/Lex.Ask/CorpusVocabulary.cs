namespace Lex.Ask;

/// <summary>
/// The publisher ids and jurisdiction codes a running server actually mounts.
///
/// A publisher filter the planner invented ("EU", "Luxembourg") used to select zero readers and
/// come back as a bare empty result, which the answer layer then reported as a fact about the
/// holdings. The mounted set is the only authority on what those two arguments may say, so it is
/// carried here and used twice: to constrain the planner schema, and to canonicalise a value the
/// model spelled with different case before the plan is frozen.
/// </summary>
public sealed class CorpusVocabulary
{
    /// <summary>A build with no mounted corpus, or a caller that has none to declare. Constrains
    /// nothing: an empty JSON Schema <c>enum</c> is invalid and would break planning outright.</summary>
    public static CorpusVocabulary Unconstrained { get; } = new([], []);

    public CorpusVocabulary(
        IEnumerable<string> publishers,
        IEnumerable<string> jurisdictions)
    {
        ArgumentNullException.ThrowIfNull(publishers);
        ArgumentNullException.ThrowIfNull(jurisdictions);
        // Ordinal order keeps every schema generated from this byte-identical between processes.
        Publishers = publishers.Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Jurisdictions = jurisdictions.Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<string> Publishers { get; }

    public IReadOnlyList<string> Jurisdictions { get; }

    /// <summary>The values this server accepts for one corpus filter, or null when the argument
    /// is not corpus-scoped or nothing is mounted to constrain it with.</summary>
    public IReadOnlyList<string>? AllowedValuesFor(string name) => name switch
    {
        "publisher" when Publishers.Count > 0 => Publishers,
        "jurisdiction" when Jurisdictions.Count > 0 => Jurisdictions,
        _ => null,
    };

    /// <summary>The mounted spelling of a corpus filter, or null when nothing mounted matches.
    /// An unmatched value is deliberately not rewritten and not rejected here: rejecting throws
    /// during plan construction and aborts every operation in the plan, so the value travels to
    /// MCP, which answers for it by name instead of returning an indistinguishable empty set.</summary>
    public string? Canonical(string name, string value) =>
        AllowedValuesFor(name)?.FirstOrDefault(item =>
            string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
}
