using Json.Schema;
using Lex.V3.Contracts.Facts;

namespace Lex.V3.Tests.Facts;

/// <summary>
/// One built instance per schema identity, shared by every Facts test class.
/// </summary>
/// <remarks>
/// <c>JsonSchema.FromText</c> registers the document by its <c>$id</c> in a process-wide registry
/// that refuses to overwrite. Two test classes each holding their own cache therefore threw on
/// whichever ran second, which looked like a schema defect and was a test-harness one.
/// </remarks>
internal static class FactsSchemas
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, JsonSchema>
        Built = new(StringComparer.Ordinal);

    internal static JsonSchema For(string schemaId) => Built.GetOrAdd(
        schemaId,
        static id => JsonSchema.FromText(
            System.Text.Encoding.UTF8.GetString(FactsSchemaExporter.ExportUtf8(id))));

    internal static EvaluationOptions Options() => new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true,
    };
}
