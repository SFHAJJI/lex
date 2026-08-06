using Lex.Law;
using Lex.Sources.EurLex;
using Lex.Sources.Legilux;

namespace Lex.Ingest;

/// <summary>
/// The CLI composition root for official publishers. Core ingestion depends only on
/// <see cref="ISourceAdapter"/>; adding a publisher is one registration plus its adapter
/// package, rather than another set of conditionals across commands.
/// </summary>
public sealed class SourceAdapterRegistry
{
    public delegate ISourceAdapter Factory(Func<string, string?> option);
    public delegate Task<object> Preview(
        ISourceAdapter adapter,
        string? previousScope,
        DateTimeOffset observedAt,
        CancellationToken ct);

    private sealed record Registration(Factory Create, Preview? PreviewScope);

    private readonly Dictionary<string, Registration> _registrations = new(StringComparer.Ordinal);

    public static SourceAdapterRegistry CreateDefault() => new SourceAdapterRegistry()
        .Register("lu-legilux", _ => new LegiluxAdapter())
        .Register(
            "eu-eurlex",
            option => new EurLexAdapter(option("--scope"), ParseInt(option("--wave"))),
            (adapter, previous, now, ct) => PreviewEurLex(adapter, previous, now, ct));

    public SourceAdapterRegistry Register(string publisherId, Factory factory, Preview? preview = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherId);
        ArgumentNullException.ThrowIfNull(factory);
        if (!_registrations.TryAdd(publisherId, new Registration(factory, preview)))
            throw new InvalidOperationException($"Publisher '{publisherId}' is already registered.");
        return this;
    }

    public ISourceAdapter Resolve(string publisherId, Func<string, string?> option)
    {
        if (!_registrations.TryGetValue(publisherId, out var registration))
            throw new ArgumentException(
                $"Unknown publisher '{publisherId}'. Registered publishers: "
                + string.Join(", ", _registrations.Keys.Order(StringComparer.Ordinal)));
        return registration.Create(option);
    }

    public Task<object> PreviewScopeAsync(
        string publisherId,
        Func<string, string?> option,
        string? previousScope,
        DateTimeOffset observedAt,
        CancellationToken ct)
    {
        var adapter = Resolve(publisherId, option);
        var preview = _registrations[publisherId].PreviewScope
                      ?? throw new NotSupportedException(
                          $"Publisher '{publisherId}' does not expose a configurable scope preview.");
        return preview(adapter, previousScope, observedAt, ct);
    }

    private static int? ParseInt(string? value) => int.TryParse(value, out var parsed) ? parsed : null;

    private static async Task<object> PreviewEurLex(
        ISourceAdapter adapter,
        string? previousScope,
        DateTimeOffset observedAt,
        CancellationToken ct)
        => await ((EurLexAdapter)adapter).PreviewScopeAsync(previousScope, observedAt, ct);
}
