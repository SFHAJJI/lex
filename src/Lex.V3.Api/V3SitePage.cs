using System.Text;
using System.Text.Json;

namespace Lex.V3.Api;

/// <summary>
/// A law's address under an interface language, or that language's home page, as the server renders it (the specification
/// of record's R67): its language, the page (<c>reading</c>, <c>dossier</c>, <c>home</c>), and the operation and parameters
/// the page itself would ask (<see cref="V3WebRoot.SitePageFor"/>).
/// </summary>
internal sealed record V3SitePage(string Language, string Kind, string OperationId, IReadOnlyDictionary<string, string> Parameters)
{
    /// <summary>The request the page would send, as its body: <c>{"operation_id": ..., "parameters": {...}}</c>.</summary>
    internal byte[] RequestUtf8 =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["operation_id"] = OperationId,
            ["parameters"] = Parameters,
        }));
}
