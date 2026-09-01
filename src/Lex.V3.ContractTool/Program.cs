using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.ContractTool;

if (args.Length == 4 &&
    string.Equals(args[0], "probe-source-scope-scale", StringComparison.Ordinal) &&
    string.Equals(args[2], "--output", StringComparison.Ordinal) &&
    int.TryParse(args[1], out var objectCount) &&
    objectCount is > 0 and <= 1_000_000)
{
    Console.WriteLine(ScopeScaleProbe.Run(objectCount, args[3]));
    return 0;
}

if (args.Length != 3 || !string.Equals(args[1], "--output", StringComparison.Ordinal))
{
    Console.Error.WriteLine(
        "Usage: Lex.V3.ContractTool <export-preview-schemas|export-synthetic-preview-schemas|export-source-core-schemas|export-source-scope-schema> --output <directory>\n" +
        "   or: Lex.V3.ContractTool probe-source-scope-scale <1..1000000> --output <directory>");
    return 2;
}

var outputDirectory = Path.GetFullPath(args[2]);
Directory.CreateDirectory(outputDirectory);

if (string.Equals(args[0], "export-preview-schemas", StringComparison.Ordinal))
{
    foreach (var schemaId in PreviewSchemaGraph.SchemaIds)
    {
        WriteSchema(
            schemaId,
            PreviewSchemaExporter.FileNameFor(schemaId),
            PreviewSchemaExporter.ExportUtf8(schemaId));
    }
}
else if (string.Equals(args[0], "export-synthetic-preview-schemas", StringComparison.Ordinal))
{
    foreach (var schemaId in SyntheticSliceSchemaGraph.OwnedSchemaIds)
    {
        WriteSchema(
            schemaId,
            SyntheticSliceSchemaExporter.FileNameFor(schemaId),
            SyntheticSliceSchemaExporter.ExportUtf8(schemaId));
    }
}
else if (string.Equals(args[0], "export-source-core-schemas", StringComparison.Ordinal))
{
    foreach (var schemaId in SourceCoreSchemaExporter.AllSchemaIds)
    {
        WriteSchema(
            schemaId,
            SourceCoreSchemaExporter.FileNameFor(schemaId),
            SourceCoreSchemaExporter.ExportUtf8(schemaId));
    }
}
else if (string.Equals(args[0], "export-source-scope-schema", StringComparison.Ordinal))
{
    WriteSchema(
        ScopeManifestSchemaIds.Manifest,
        ScopeSchemaExporter.FileName,
        ScopeSchemaExporter.ExportUtf8());
}
else
{
    Console.Error.WriteLine(
        "Usage: Lex.V3.ContractTool <export-preview-schemas|export-synthetic-preview-schemas|export-source-core-schemas|export-source-scope-schema> --output <directory>\n" +
        "   or: Lex.V3.ContractTool probe-source-scope-scale <1..1000000> --output <directory>");
    return 2;
}

return 0;

void WriteSchema(string schemaId, string fileName, byte[] bytes)
{
    var outputPath = Path.Combine(outputDirectory, fileName);
    File.WriteAllBytes(outputPath, bytes);
    Console.WriteLine($"{schemaId} -> {outputPath}");
}
