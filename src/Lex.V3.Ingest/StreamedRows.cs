using System.Collections;

namespace Lex.V3.Ingest;

/// <summary>
/// An index table's rows, read as they are enumerated, for a hash over the index's logical rows
/// (<c>ContractJson.Sha256OfDefaultJson</c>): the serializer writes a list as it enumerates it, so the table is never held
/// whole. It is enumerated once; neither a count nor an index is served, since what hashes needs neither.
/// </summary>
internal sealed class StreamedRows<T>(IEnumerable<T> rows) : IReadOnlyList<T>
{
    private bool _enumerated;

    public T this[int index] => throw new NotSupportedException("Streamed rows are read once, in order.");

    public int Count => throw new NotSupportedException("Streamed rows are read once, in order.");

    public IEnumerator<T> GetEnumerator()
    {
        if (_enumerated)
        {
            throw new InvalidOperationException("Streamed rows are read once.");
        }

        _enumerated = true;
        return rows.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
