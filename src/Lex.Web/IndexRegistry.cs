using Lex.Index;
using Microsoft.Extensions.Options;

namespace Lex.Web;

/// <summary>
/// The mounted indexes, one per publisher (D27), as a service rather than as a local variable
/// captured by twenty-five route lambdas.
///
/// Publishers are independent by design, so one unreadable index must not take the others down
/// with it: a refusal is logged loudly and the rest still mount. <c>/coverage</c> then reports the
/// survivors, which is the honest state of the service rather than a blank page.
/// </summary>
public sealed class IndexRegistry : IDisposable
{
    private readonly Dictionary<string, LexIndexReader> _readers = new(StringComparer.Ordinal);

    public IndexRegistry(IOptions<LexOptions> options, ILogger<IndexRegistry> log)
    {
        var dir = options.Value.IndexDir;
        if (!Directory.Exists(dir))
        {
            log.LogWarning("No index directory at {Dir}; the service will report empty coverage.", dir);
            return;
        }

        foreach (var db in Directory.EnumerateFiles(dir, "index-*.db"))
        {
            try
            {
                var reader = LexIndexReader.Open(db);
                _readers[reader.Collection] = reader;
                log.LogInformation("Mounted {Db} ({Collection}, signature_valid={Valid})",
                    db, reader.Collection, reader.SignatureValid);
            }
            catch (Exception ex)
            {
                // Deliberately swallowed: see the class remarks. The message names the file and
                // the reason, which is what a stale schema needs in order to be diagnosed.
                log.LogError("Refused {Db}: {Reason}", db, ex.Message);
            }
        }

        if (_readers.Count == 0)
            log.LogWarning("No indexes mounted from {Dir}.", dir);
    }

    public IReadOnlyDictionary<string, LexIndexReader> All => _readers;

    public IEnumerable<LexIndexReader> Values => _readers.Values;

    public int Count => _readers.Count;

    public bool TryGet(string collection, out LexIndexReader reader) =>
        _readers.TryGetValue(collection, out reader!);

    /// <summary>The readers a request should ask, narrowed to one publisher when named.</summary>
    public IEnumerable<LexIndexReader> For(string? publisher) =>
        publisher is null ? _readers.Values : _readers.Values.Where(r => r.Collection == publisher);

    public void Dispose()
    {
        foreach (var r in _readers.Values) r.Dispose();
        _readers.Clear();
    }
}
