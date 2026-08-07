using Lex.Index;
using Lex.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Standalone MCP host for local clients. Legal tools and protocol bindings live in the
// transport-neutral Lex.Mcp library; the public HTTP host composes that same library in Lex.Web.
var indexDir = Environment.GetEnvironmentVariable("LEX_INDEX_DIR") ?? "indexes";
var readers = new Dictionary<string, LexIndexReader>(StringComparer.Ordinal);
if (Directory.Exists(indexDir))
    foreach (var db in Directory.EnumerateFiles(indexDir, "index-*.db"))
    {
        var reader = LexIndexReader.Open(db);
        readers[reader.Collection] = reader;
    }
Console.Error.WriteLine($"[lex-mcp] mounted {readers.Count} index(es) from {indexDir}");

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(new McpCore(readers));
builder.Services.AddMcpServer(McpSdkBridge.Configure)
    .WithStdioServerTransport()
    .WithLexTools();
// Stdout is the MCP transport. A single host log line there corrupts the protocol stream.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

try
{
    await builder.Build().RunAsync();
}
finally
{
    foreach (var reader in readers.Values) reader.Dispose();
}
