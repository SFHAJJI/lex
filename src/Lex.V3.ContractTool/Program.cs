using Lex.V3.Contracts;

if (args.Length != 3 || !string.Equals(args[1], "--output", StringComparison.Ordinal))
{
    Console.Error.WriteLine(
        "Usage: Lex.V3.ContractTool <export-preview-schemas|export-synthetic-preview-schemas> --output <directory>");
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
else
{
    Console.Error.WriteLine(
        "Usage: Lex.V3.ContractTool <export-preview-schemas|export-synthetic-preview-schemas> --output <directory>");
    return 2;
}

return 0;

void WriteSchema(string schemaId, string fileName, byte[] bytes)
{
    var outputPath = Path.Combine(outputDirectory, fileName);
    File.WriteAllBytes(outputPath, bytes);
    Console.WriteLine($"{schemaId} -> {outputPath}");
}
