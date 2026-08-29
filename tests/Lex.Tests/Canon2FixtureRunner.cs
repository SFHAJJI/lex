using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Lex.Derive;

namespace Lex.Tests;

internal static class Canon2FixtureRunner
{
    public const string Canon1ReviewedManifestSha256 =
        "6655f4c0d5e8c970fe3ab99e82896105da60b7b0ac24c84b1e13c519ad729beb";
    public const string MarkerGapNote = "1. loi modifiee du 9 juillet 2099";
    public const string MarkerGapOutputPath = "cases/akn-lu-marker-gap_3/output.json";

    private const string ManifestName = "manifest.tsv";
    private const string OrdinaryLexId = "lu-legilux:synthetic-ordinary-citation:2099-07-09";
    private const string MarkerLexId = "lu-legilux:synthetic-marker-gap:2099-07-09";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static IReadOnlyList<string> ProfileIds { get; } = Array.AsReadOnly(
        Canon1FixtureRunner.ProfileIds
            .Append(AknLuProfileV3.ProfileId)
            .Order(StringComparer.Ordinal)
            .ToArray());

    public static void Generate(string outputRoot, string canon1Directory) =>
        Canon1FixtureRunner.RunWithInvariantCulture(() =>
            GenerateInvariant(outputRoot, canon1Directory));

    public static void Verify(
        string generatedRoot,
        string reviewedManifestPath,
        string canon1Directory) =>
        Canon1FixtureRunner.RunWithInvariantCulture(() =>
            VerifyInvariant(generatedRoot, reviewedManifestPath, canon1Directory));

    public static void VerifyCanon1Binding(string canon1Directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canon1Directory);
        var root = Path.GetFullPath(canon1Directory);
        var manifest = Path.Combine(root, ManifestName);
        Canon1FixtureRunner.Verify(root, manifest);
        var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(manifest)));
        if (!string.Equals(digest, Canon1ReviewedManifestSha256, StringComparison.Ordinal))
            throw new InvalidDataException(
                "canon/1 reviewed manifest no longer matches the canon/2 binding");
    }

    internal static StructuredTextExtractor.Result ExtractOrdinaryCitation(bool enableAknLuV3) =>
        StructuredTextExtractor.Extract(
            Text(OrdinaryCitationSource()), OrdinaryLexId, enableAknLuV3);

    internal static byte[] CanonicalResultBytes(StructuredTextExtractor.Result result) =>
        Json(writer => WriteResult(writer, result));

    private static void GenerateInvariant(string outputRoot, string canon1Directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        if (!CultureInfo.CurrentCulture.Equals(CultureInfo.InvariantCulture)
            || !CultureInfo.CurrentUICulture.Equals(CultureInfo.InvariantCulture))
            throw new InvalidDataException("canon/2 generation requires invariant culture");

        VerifyCanon1Binding(canon1Directory);
        var root = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(root);
        if (Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidDataException("canon/2 output root must be empty");

        Write(root, "contract.json", ContractBytes());
        CopyCanon1Cases(root, canon1Directory);
        WriteOrdinaryCitationCase(root);
        WriteMarkerGapCase(root);
        Write(root, ManifestName, ManifestBytes(root));
    }

    private static void CopyCanon1Cases(string outputRoot, string canon1Directory)
    {
        using var generatedCanon1 = new TempDirectory("lex-canon1-for-canon2-");
        Canon1FixtureRunner.Generate(generatedCanon1.Path);
        Canon1FixtureRunner.Verify(
            generatedCanon1.Path,
            Path.Combine(Path.GetFullPath(canon1Directory), ManifestName));

        var casesRoot = Path.Combine(generatedCanon1.Path, "cases");
        foreach (var source in Directory.EnumerateFiles(
                     casesRoot, "*", SearchOption.AllDirectories)
                 .OrderBy(path => Path.GetRelativePath(casesRoot, path), StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(generatedCanon1.Path, source)
                .Replace('\\', '/');
            Write(outputRoot, relative, File.ReadAllBytes(source));
        }
    }

    private static void WriteOrdinaryCitationCase(string root)
    {
        const string caseRoot = "cases/akn-lu-ordinary-citation_2";
        var source = OrdinaryCitationSource();
        var frozen = ExtractOrdinaryCitation(enableAknLuV3: false);
        var candidate = ExtractOrdinaryCitation(enableAknLuV3: true);
        var frozenBytes = CanonicalResultBytes(frozen);
        var candidateBytes = CanonicalResultBytes(candidate);
        if (!string.Equals(frozen.ProfileId, AknLuProfileV2.ProfileId, StringComparison.Ordinal)
            || !string.Equals(candidate.ProfileId, AknLuProfileV2.ProfileId, StringComparison.Ordinal)
            || !frozenBytes.AsSpan().SequenceEqual(candidateBytes))
            throw new InvalidDataException(
                "ordinary citation changed when the akn-lu/3 dispatcher was enabled");

        Write(root, $"{caseRoot}/case.json", CaseBytes(
            AknLuProfileV2.ProfileId, OrdinaryLexId, "input.xml"));
        Write(root, $"{caseRoot}/input.xml", source);
        Write(root, $"{caseRoot}/output.json", candidateBytes);
    }

    private static void WriteMarkerGapCase(string root)
    {
        const string caseRoot = "cases/akn-lu-marker-gap_3";
        var source = MarkerGapSource();
        var result = StructuredTextExtractor.Extract(
            Text(source), MarkerLexId, enableAknLuV3: true);
        var gap = result.Extraction.ProvisionGaps?.SingleOrDefault();
        if (!string.Equals(result.ProfileId, AknLuProfileV3.ProfileId, StringComparison.Ordinal)
            || result.Extraction.Provisions.Count != 0
            || gap is null
            || !string.Equals(gap.TextUnavailableReason,
                ProvisionGapReason.MarkerOnly, StringComparison.Ordinal)
            || result.Extraction.Markdown.Contains(MarkerGapNote, StringComparison.Ordinal))
            throw new InvalidDataException(
                "structural marker fixture did not produce one textless akn-lu/3 gap");

        Write(root, $"{caseRoot}/case.json", CaseBytes(
            AknLuProfileV3.ProfileId, MarkerLexId, "input.xml"));
        Write(root, $"{caseRoot}/input.xml", source);
        Write(root, $"{caseRoot}/output.json", CanonicalResultBytes(result));
    }

    private static void VerifyInvariant(
        string generatedRoot,
        string reviewedManifestPath,
        string canon1Directory)
    {
        VerifyCanon1Binding(canon1Directory);
        var root = Path.GetFullPath(generatedRoot);
        var generatedManifestPath = Path.Combine(root, ManifestName);
        if (!File.Exists(generatedManifestPath))
            throw new InvalidDataException("generated canon/2 root has no manifest.tsv");
        if (!File.ReadAllBytes(Path.Combine(root, "contract.json"))
                .AsSpan().SequenceEqual(ContractBytes()))
            throw new InvalidDataException("canon/2 contract does not match the frozen contract");

        var reviewed = File.ReadAllBytes(reviewedManifestPath);
        var recorded = File.ReadAllBytes(generatedManifestPath);
        var actual = ManifestBytes(root);
        if (!reviewed.AsSpan().SequenceEqual(recorded)
            || !recorded.AsSpan().SequenceEqual(actual))
            throw new InvalidDataException("canon/2 files do not match the reviewed manifest");

        var canon1Cases = Path.Combine(Path.GetFullPath(canon1Directory), "cases");
        foreach (var source in Directory.EnumerateFiles(
                     canon1Cases, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(canon1Directory), source);
            var copied = Path.Combine(root, relative);
            if (!File.Exists(copied)
                || !File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(copied)))
                throw new InvalidDataException(
                    "canon/2 no longer contains the byte-identical canon/1 case tree");
        }
    }

    private static byte[] ContractBytes() => Json(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("schema", "lex-canon-freeze/2");
        writer.WriteString("canon", "canon/2");
        writer.WriteString("canon1_reviewed_manifest_sha256",
            Canon1ReviewedManifestSha256);
        writer.WriteStartArray("profile_ids");
        foreach (var profileId in ProfileIds) writer.WriteStringValue(profileId);
        writer.WriteEndArray();
        writer.WriteString("target_framework", "net10.0");
        writer.WriteString("sdk", "10.0.400");
        writer.WriteStartObject("invariants");
        writer.WriteString("culture", "InvariantCulture");
        writer.WriteString("encoding", "UTF-8 without BOM");
        writer.WriteString("line_endings", "LF");
        writer.WriteString("path_order", "ordinal");
        writer.WriteString("hash", "SHA-256 lowercase hexadecimal");
        writer.WriteEndObject();
        writer.WriteEndObject();
    });

    private static byte[] CaseBytes(string profileId, string lexId, string sourceFile) =>
        Json(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("profile_id", profileId);
            writer.WriteString("lex_id_base", lexId);
            writer.WriteString("source_file", sourceFile);
            writer.WriteEndObject();
        });

    private static void WriteResult(
        Utf8JsonWriter writer,
        StructuredTextExtractor.Result result)
    {
        writer.WriteStartObject();
        writer.WriteString("profile_id", result.ProfileId);
        if (result.Extraction.ProvisionGaps is not null)
            writer.WriteString("text_completeness", result.Extraction.TextCompleteness);
        writer.WriteStartArray("provisions");
        foreach (var provision in result.Extraction.Provisions)
        {
            writer.WriteStartObject();
            if (provision.DocumentOrder is not null)
                writer.WriteNumber("document_order", provision.DocumentOrder.Value);
            writer.WriteString("anchor", provision.Anchor);
            String(writer, "eli", provision.Eli);
            writer.WriteString("type", provision.Type);
            String(writer, "num", provision.Num);
            String(writer, "heading", provision.Heading);
            writer.WriteStartArray("path");
            foreach (var path in provision.Path) writer.WriteStringValue(path);
            writer.WriteEndArray();
            String(writer, "article_valid_from", provision.ArticleValidFrom);
            writer.WriteString("text_md", provision.TextMd);
            writer.WriteString("text_sha256", provision.TextSha256);
            writer.WriteStartObject("md_span");
            writer.WriteNumber("start", provision.MdStart);
            writer.WriteNumber("end", provision.MdEnd);
            writer.WriteEndObject();
            writer.WriteStartArray("citations");
            foreach (var citation in provision.Citations)
            {
                writer.WriteStartObject();
                String(writer, "href", citation.Href);
                writer.WriteString("text", citation.Text);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        if (result.Extraction.ProvisionGaps is not null)
        {
            writer.WriteStartArray("provision_gaps");
            foreach (var gap in result.Extraction.ProvisionGaps)
            {
                writer.WriteStartObject();
                writer.WriteNumber("document_order", gap.DocumentOrder);
                writer.WriteString("anchor", gap.Anchor);
                String(writer, "eli", gap.Eli);
                writer.WriteString("type", gap.Type);
                String(writer, "num", gap.Num);
                String(writer, "heading", gap.Heading);
                writer.WriteStartArray("path");
                foreach (var path in gap.Path) writer.WriteStringValue(path);
                writer.WriteEndArray();
                String(writer, "article_valid_from", gap.ArticleValidFrom);
                writer.WriteString("text_unavailable_reason", gap.TextUnavailableReason);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        writer.WriteString("markdown", result.Extraction.Markdown);
        writer.WriteStartArray("notes");
        foreach (var note in result.Extraction.Notes) writer.WriteStringValue(note);
        writer.WriteEndArray();
        writer.WriteStartArray("publisher_structural_empty_articles");
        foreach (var empty in result.Extraction.PublisherStructuralEmptyArticles ?? [])
        {
            writer.WriteStartObject();
            writer.WriteString("anchor", empty.Anchor);
            writer.WriteString("w_id", empty.WId);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void String(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteString(name, value);
    }

    private static byte[] Json(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            write(writer);
        }
        stream.WriteByte((byte)'\n');
        var bytes = stream.ToArray();
        if (bytes.AsSpan().IndexOf((byte)'\r') >= 0)
            throw new InvalidDataException("canonical JSON must use LF line endings");
        return bytes;
    }

    private static byte[] ManifestBytes(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        var entries = Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = Path.GetRelativePath(fullRoot, path).Replace('\\', '/'),
                FullPath = path,
            })
            .Where(value => !string.Equals(
                value.Path, ManifestName, StringComparison.Ordinal))
            .OrderBy(value => value.Path, StringComparer.Ordinal)
            .ToArray();
        var manifest = new StringBuilder();
        foreach (var entry in entries)
        {
            var bytes = File.ReadAllBytes(entry.FullPath);
            manifest.Append(entry.Path).Append('\t')
                .Append(bytes.LongLength.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(Convert.ToHexStringLower(SHA256.HashData(bytes))).Append('\n');
        }
        return Utf8.GetBytes(manifest.ToString());
    }

    private static void Write(string root, string relativePath, byte[] bytes)
    {
        if (relativePath.Contains('\\', StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"canon path '{relativePath}' is not normalized");
        var fullRoot = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(
            fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
            throw new InvalidDataException($"canon path '{relativePath}' escapes its root");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static string Text(byte[] source) => Utf8.GetString(source);

    private static byte[] Source(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return Utf8.GetBytes(normalized + (normalized.EndsWith('\n') ? "" : "\n"));
    }

    private static byte[] OrdinaryCitationSource() => Source("""
        <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0">
          <act><body><article id="art_ordinary"><num>Art. 8.</num>
            <alinea><content><ol>
              <li><ref href="https://publisher.example/synthetic-act">1. loi du 9 juillet 2099</ref></li>
            </ol></content></alinea>
          </article></body></act>
        </akomaNtoso>
        """);

    private static byte[] MarkerGapSource() => Source($$"""
        <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0">
          <act><body><article id="art_marker"><num>Art. 9.</num>
            <alinea><content><ol><li>
              <ref href="https://publisher.example/synthetic-act">{{MarkerGapNote}}</ref>
              <mod class="source" for="item"/><noteRef href="#M1" marker="1"/>
            </li></ol></content></alinea>
          </article></body></act>
        </akomaNtoso>
        """);

    private sealed class TempDirectory : IDisposable
    {
        private readonly string _prefix;

        public string Path { get; }

        public TempDirectory(string prefix)
        {
            _prefix = prefix;
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"{prefix}{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            var full = System.IO.Path.GetFullPath(Path);
            var tempPrefix = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath())
                .TrimEnd(System.IO.Path.DirectorySeparatorChar)
                + System.IO.Path.DirectorySeparatorChar;
            if (full.StartsWith(tempPrefix, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal)
                && System.IO.Path.GetFileName(full).StartsWith(_prefix, StringComparison.Ordinal)
                && Directory.Exists(full))
                Directory.Delete(full, recursive: true);
        }
    }
}
