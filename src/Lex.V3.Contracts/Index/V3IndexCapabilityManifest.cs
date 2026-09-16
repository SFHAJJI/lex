using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Index;

public enum V3IndexCapabilityManifestRefusal
{
    [JsonStringEnumMemberName("none")]
    None,

    [JsonStringEnumMemberName("unknown_publisher")]
    UnknownPublisher,

    [JsonStringEnumMemberName("publisher_mismatch")]
    PublisherMismatch,

    [JsonStringEnumMemberName("index_mismatch")]
    IndexMismatch,

    [JsonStringEnumMemberName("malformed_cell")]
    MalformedCell,

    [JsonStringEnumMemberName("duplicate_cell")]
    DuplicateCell,

    [JsonStringEnumMemberName("overlapping_period")]
    OverlappingPeriod,
}

public enum V3IndexCapabilityLookupOutcome
{
    [JsonStringEnumMemberName("supported")]
    Supported,

    [JsonStringEnumMemberName("filter_not_supported_by_index")]
    FilterNotSupportedByIndex,
}

/// <summary>
/// One measured, non-empty capability of one content-addressed publisher index over an inclusive
/// civil-date period.
/// </summary>
public sealed record V3IndexCapabilityCell
{
    public V3IndexCapabilityCell(
        PublisherId publisher,
        string indexSha256,
        string operation,
        string column,
        string field,
        string language,
        DateOnly periodFrom,
        DateOnly periodTo,
        long population)
    {
        if (!Enum.IsDefined(publisher))
        {
            throw new ArgumentOutOfRangeException(nameof(publisher));
        }

        operation = ContractValidation.RequireIdentifier(operation, nameof(operation));
        if (!V3ContractVocabulary.OperationIds.Contains(operation, StringComparer.Ordinal))
        {
            throw new ArgumentException("The operation is outside the closed V3 operation vocabulary.", nameof(operation));
        }

        if (periodFrom > periodTo)
        {
            throw new ArgumentOutOfRangeException(nameof(periodFrom), "The inclusive period cannot end before it begins.");
        }

        if (population <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(population), "An advertised capability must have measured support.");
        }

        Publisher = publisher;
        IndexSha256 = ContractValidation.RequireSha256(indexSha256, nameof(indexSha256));
        Operation = operation;
        Column = ContractValidation.RequireIdentifier(column, nameof(column));
        Field = ContractValidation.RequireIdentifier(field, nameof(field));
        Language = ContractValidation.RequireIdentifier(language, nameof(language));
        PeriodFrom = periodFrom;
        PeriodTo = periodTo;
        Population = population;
    }

    public PublisherId Publisher { get; }

    public string IndexSha256 { get; }

    public string Operation { get; }

    public string Column { get; }

    public string Field { get; }

    public string Language { get; }

    public DateOnly PeriodFrom { get; }

    public DateOnly PeriodTo { get; }

    public long Population { get; }
}

/// <summary>
/// The admitted capability cells for exactly one content-addressed publisher index.
/// </summary>
/// <remarks>
/// Periods have inclusive, day-level coverage. This contract does not establish that its inputs are
/// complete or measured honestly. A later index builder must derive the digest and cells from its
/// own complete accepted inputs before it may emit the immutable publisher index.
/// </remarks>
public sealed class V3IndexCapabilityManifest
{
    public static string PeriodGranularity => "day";

    private V3IndexCapabilityManifest(
        PublisherId publisher,
        string indexSha256,
        V3IndexCapabilityCell[] cells)
    {
        Publisher = publisher;
        IndexSha256 = indexSha256;
        Cells = Array.AsReadOnly(cells);
    }

    public PublisherId Publisher { get; }

    public string IndexSha256 { get; }

    public IReadOnlyList<V3IndexCapabilityCell> Cells { get; }

    public static bool TryCreate(
        PublisherId publisher,
        string indexSha256,
        IEnumerable<V3IndexCapabilityCell> cells,
        out V3IndexCapabilityManifest? manifest,
        out V3IndexCapabilityManifestRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(cells);
        manifest = null;
        indexSha256 = ContractValidation.RequireSha256(indexSha256, nameof(indexSha256));

        if (!Enum.IsDefined(publisher))
        {
            refusal = V3IndexCapabilityManifestRefusal.UnknownPublisher;
            return false;
        }

        var ordered = cells.ToArray();
        if (ordered.Any(static cell => cell is null))
        {
            refusal = V3IndexCapabilityManifestRefusal.MalformedCell;
            return false;
        }

        if (ordered.Any(cell => cell.Publisher != publisher))
        {
            refusal = V3IndexCapabilityManifestRefusal.PublisherMismatch;
            return false;
        }

        if (ordered.Any(cell => !string.Equals(cell.IndexSha256, indexSha256, StringComparison.Ordinal)))
        {
            refusal = V3IndexCapabilityManifestRefusal.IndexMismatch;
            return false;
        }

        Array.Sort(ordered, CompareCells);
        for (var index = 1; index < ordered.Length; index++)
        {
            var previous = ordered[index - 1];
            var current = ordered[index];
            if (!SameDimensions(previous, current))
            {
                continue;
            }

            if (previous.PeriodFrom == current.PeriodFrom && previous.PeriodTo == current.PeriodTo)
            {
                refusal = V3IndexCapabilityManifestRefusal.DuplicateCell;
                return false;
            }

            if (current.PeriodFrom <= previous.PeriodTo)
            {
                refusal = V3IndexCapabilityManifestRefusal.OverlappingPeriod;
                return false;
            }
        }

        manifest = new V3IndexCapabilityManifest(publisher, indexSha256, ordered);
        refusal = V3IndexCapabilityManifestRefusal.None;
        return true;
    }

    public V3IndexCapabilityLookupOutcome Lookup(
        string operation,
        string column,
        string field,
        string language,
        DateOnly periodFrom,
        DateOnly periodTo,
        out IReadOnlyList<V3IndexCapabilityCell> cells)
    {
        operation = ContractValidation.RequireIdentifier(operation, nameof(operation));
        column = ContractValidation.RequireIdentifier(column, nameof(column));
        field = ContractValidation.RequireIdentifier(field, nameof(field));
        language = ContractValidation.RequireIdentifier(language, nameof(language));
        if (periodFrom > periodTo)
        {
            throw new ArgumentOutOfRangeException(nameof(periodFrom), "The requested period cannot end before it begins.");
        }

        var candidates = Cells.Where(candidate =>
                string.Equals(candidate.Operation, operation, StringComparison.Ordinal) &&
                string.Equals(candidate.Column, column, StringComparison.Ordinal) &&
                string.Equals(candidate.Field, field, StringComparison.Ordinal) &&
                string.Equals(candidate.Language, language, StringComparison.Ordinal) &&
                candidate.PeriodTo >= periodFrom &&
                candidate.PeriodFrom <= periodTo)
            .ToArray();

        var nextDate = periodFrom;
        foreach (var candidate in candidates)
        {
            if (candidate.PeriodFrom > nextDate || candidate.PeriodTo < nextDate)
            {
                cells = Array.Empty<V3IndexCapabilityCell>();
                return V3IndexCapabilityLookupOutcome.FilterNotSupportedByIndex;
            }

            if (candidate.PeriodTo >= periodTo)
            {
                cells = Array.AsReadOnly(candidates);
                return V3IndexCapabilityLookupOutcome.Supported;
            }

            nextDate = candidate.PeriodTo.AddDays(1);
        }

        cells = Array.Empty<V3IndexCapabilityCell>();
        return V3IndexCapabilityLookupOutcome.FilterNotSupportedByIndex;
    }

    private static bool SameDimensions(V3IndexCapabilityCell left, V3IndexCapabilityCell right) =>
        string.Equals(left.Operation, right.Operation, StringComparison.Ordinal) &&
        string.Equals(left.Column, right.Column, StringComparison.Ordinal) &&
        string.Equals(left.Field, right.Field, StringComparison.Ordinal) &&
        string.Equals(left.Language, right.Language, StringComparison.Ordinal);

    private static int CompareCells(V3IndexCapabilityCell left, V3IndexCapabilityCell right)
    {
        var comparison = string.CompareOrdinal(left.Operation, right.Operation);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(left.Column, right.Column);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(left.Field, right.Field);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(left.Language, right.Language);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.PeriodFrom.CompareTo(right.PeriodFrom);
        return comparison != 0 ? comparison : left.PeriodTo.CompareTo(right.PeriodTo);
    }
}
