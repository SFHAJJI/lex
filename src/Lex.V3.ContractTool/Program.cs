using Lex.V3.Contracts;

if (args.Length != 3 ||
    !string.Equals(args[0], "export-preview-schemas", StringComparison.Ordinal) ||
    !string.Equals(args[1], "--output", StringComparison.Ordinal))
{
    Console.Error.WriteLine(
        "Usage: Lex.V3.ContractTool export-preview-schemas --output <directory>");
    return 2;
}

var outputDirectory = Path.GetFullPath(args[2]);
Directory.CreateDirectory(outputDirectory);

foreach (var schemaId in PreviewSchemaGraph.SchemaIds)
{
    var fileName = PreviewSchemaExporter.FileNameFor(schemaId);
    var outputPath = Path.Combine(outputDirectory, fileName);
    File.WriteAllBytes(outputPath, PreviewSchemaExporter.ExportUtf8(schemaId));
    Console.WriteLine($"{schemaId} -> {outputPath}");
}

return 0;
