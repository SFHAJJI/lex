namespace Lex.Ask;

/// <summary>
/// What a running server actually mounts, and what it can actually serve from it.
///
/// A publisher filter the planner invented ("EU", "Luxembourg") used to select zero readers and
/// come back as a bare empty result, which the answer layer then reported as a fact about the
/// holdings. The mounted set is the only authority on what those two arguments may say, so it is
/// carried here and used twice: to constrain the planner schema, and to canonicalise a value the
/// model spelled with different case before the plan is frozen.
///
/// Mounting a corpus and being able to search it every advertised way are two different facts, so
/// hybrid readiness travels beside the mounted set rather than being inferred from it. Both are
/// read from the running server; neither is configured.
/// </summary>
public sealed class CorpusVocabulary
{
    /// <summary>A build with no mounted corpus, or a caller that has none to declare. Constrains
    /// nothing: an empty JSON Schema <c>enum</c> is invalid and would break planning outright.
    /// Nothing mounted also serves no semantic retrieval, which is why the servability answer here
    /// is the negative one.</summary>
    public static CorpusVocabulary Unconstrained { get; } = new([], []);

    public CorpusVocabulary(
        IEnumerable<string> publishers,
        IEnumerable<string> jurisdictions,
        bool hybridRetrievalServable = false)
    {
        ArgumentNullException.ThrowIfNull(publishers);
        ArgumentNullException.ThrowIfNull(jurisdictions);
        // Ordinal order keeps every schema generated from this byte-identical between processes.
        Publishers = publishers.Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Jurisdictions = jurisdictions.Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        HybridRetrievalServable = hybridRetrievalServable;
    }

    public IReadOnlyList<string> Publishers { get; }

    public IReadOnlyList<string> Jurisdictions { get; }

    /// <summary>Whether every mounted publisher can serve <c>retrieval_mode=hybrid</c> right now.
    ///
    /// <para>It defaults to false, and that direction is deliberate: a caller who does not know
    /// gets the retrieval mode every mounted corpus can always serve. The cost of being wrong that
    /// way is a keyword ranking instead of a semantic one, which answers the same question from
    /// the same law; the cost of being wrong the other way is an operation with no execution and a
    /// not_available where the corpus held the answer all along.</para>
    ///
    /// <para>Deliberately not read by the planner schema, which keeps advertising both modes. The
    /// schema is a contract shared with the MCP tool definitions and pinned against them, and the
    /// tool still accepts hybrid and still refuses it honestly per publisher. This answers a
    /// different question: not what may be asked for, but what this server can deliver today.</para></summary>
    public bool HybridRetrievalServable { get; }

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
