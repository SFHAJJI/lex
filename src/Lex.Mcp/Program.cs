using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.Index;
using Lex.Mcp;

// Lex.Mcp — MCP server over stdio (newline-delimited JSON-RPC).
// The tool logic lives in McpCore, shared with the public HTTP endpoint in Lex.Web.

var indexDir = Environment.GetEnvironmentVariable("LEX_INDEX_DIR") ?? "indexes";
var readers = new Dictionary<string, LexIndexReader>(StringComparer.Ordinal);
if (Directory.Exists(indexDir))
    foreach (var db in Directory.EnumerateFiles(indexDir, "index-*.db"))
    {
        var r = LexIndexReader.Open(db);
        readers[r.Collection] = r;
    }
Console.Error.WriteLine($"[lex-mcp] mounted {readers.Count} index(es) from {indexDir}");

var core = new McpCore(readers);
var jsonOut = new JsonSerializerOptions { WriteIndented = false };
Console.OutputEncoding = Encoding.UTF8;

string? line;
while ((line = Console.ReadLine()) is not null)
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    JsonNode? msg;
    try { msg = JsonNode.Parse(line); } catch { continue; }
    if (msg is null) continue;
    var response = core.HandleMessage(msg);
    if (response is not null)
    {
        Console.WriteLine(response.ToJsonString(jsonOut));
        Console.Out.Flush();
    }
}
