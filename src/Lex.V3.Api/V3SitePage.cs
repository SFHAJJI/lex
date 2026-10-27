using System.Text;
using System.Text.Json;

namespace Lex.V3.Api;

/// <summary>
/// A law's address under an interface language, or that language's home page or Developers page, as the server renders it
/// (the specification of record's R67): its language, the page (<c>reading</c>, <c>dossier</c>, <c>home</c>,
/// <c>developers</c>), and the operation and parameters the page itself would ask, or for the Developers page the request its
/// live examples show (<see cref="V3WebRoot.SitePageFor"/>): strings, and whole numbers (a reading's page number, the plan's
/// item 3.7; the Developers page's one row).
/// </summary>
internal sealed record V3SitePage(string Language, string Kind, string OperationId, IReadOnlyDictionary<string, object> Parameters)
{
    /// <summary>The request the page would send, as its body: <c>{"operation_id": ..., "parameters": {...}}</c>.</summary>
    internal byte[] RequestUtf8 =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["operation_id"] = OperationId,
            ["parameters"] = Parameters,
        }));
}
