using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Lex.Derive;

namespace Lex.Tests;

internal static class Canon1FixtureRunner
{
    private const string ManifestName = "manifest.tsv";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static IReadOnlyList<string> ProfileIds { get; } = Array.AsReadOnly(new[]
    {
        AknLuDocumentProfile.ProfileId,
        AknLuDuplicateSclAttributeProfile.ProfileId,
        AknLuProfile.ProfileId,
        AknLuProfileV2.ProfileId,
        Fmx4EuProfile.ProfileId,
        TolerantHtmlEuProfile.ProfileId,
        PdfLuProfile.ProfileId,
        PdfMemorialLuProfile.ProfileId,
        PdfMemorialLuProfileV2.ProfileId,
        LegacyXlinkEuProfile.ProfileId,
        XhtmlEuProfile.ProfileId,
    });

    public static void Generate(string outputRoot)
        => RunWithInvariantCulture(() => GenerateInvariant(outputRoot));

    internal static void RunWithInvariantCulture(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static void GenerateInvariant(string outputRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        var root = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(root);
        if (Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidDataException("canon/1 output root must be empty");

        if (!ProfileIds.SequenceEqual(DiscoverProductionProfileIds(), StringComparer.Ordinal))
            throw new InvalidDataException(
                "canon/1 registry does not cover every public Lex.Derive profile id");
        var cases = Cases();
        ValidateProfileIds(cases.Select(value => value.ProfileId));
        Write(root, "contract.json", ContractBytes());
        foreach (var value in cases)
        {
            var caseRoot = $"cases/{ProfilePath(value.ProfileId)}";
            var sourceFile = $"input.{value.Extension}";
            var source = value.Source();
            Write(root, $"{caseRoot}/case.json", CaseBytes(value, sourceFile));
            Write(root, $"{caseRoot}/{sourceFile}", source);

            var result = value.Extract(source);
            if (!string.Equals(value.ProfileId, result.ProfileId, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"case {value.ProfileId} selected unexpected profile {result.ProfileId}");
            if (result.Extraction.Provisions.Count == 0
                || !result.Extraction.Provisions.Any(provision =>
                    !string.IsNullOrWhiteSpace(provision.TextMd)))
                throw new InvalidDataException($"case {value.ProfileId} is empty or trivial");
            Write(root, $"{caseRoot}/output.json", ExtractionBytes(result));
        }

        Write(root, ManifestName, ManifestBytes(root));
    }

    public static void Verify(string generatedRoot, string reviewedManifestPath)
    {
        var root = Path.GetFullPath(generatedRoot);
        var generatedManifestPath = Path.Combine(root, ManifestName);
        if (!File.Exists(generatedManifestPath))
            throw new InvalidDataException("generated canon root has no manifest.tsv");
        var reviewed = File.ReadAllBytes(reviewedManifestPath);
        var recorded = File.ReadAllBytes(generatedManifestPath);
        var actual = ManifestBytes(root);
        if (!reviewed.AsSpan().SequenceEqual(recorded)
            || !recorded.AsSpan().SequenceEqual(actual))
            throw new InvalidDataException(
                "canon/1 files do not match the reviewed manifest");
    }

    public static void ValidateProfileIds(IEnumerable<string> profileIds)
    {
        var actual = profileIds.ToArray();
        if (actual.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("canon/1 profile cases cannot be empty");
        if (actual.Distinct(StringComparer.Ordinal).Count() != actual.Length)
            throw new InvalidDataException("canon/1 profile cases cannot be duplicated");
        if (!actual.SequenceEqual(ProfileIds, StringComparer.Ordinal))
            throw new InvalidDataException(
                "canon/1 profile cases must exactly match the frozen registry order");
    }

    public static IReadOnlyList<string> DiscoverProductionProfileIds() =>
        typeof(AknLuProfile).Assembly.GetTypes()
            .SelectMany(type => type.GetFields(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.DeclaredOnly))
            .Where(field => field.Name == "ProfileId"
                && field.IsLiteral
                && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static string ProfilePath(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId)
            || profileId.Any(character => !(character is >= 'a' and <= 'z'
                or >= '0' and <= '9' or '-' or '/'))
            || profileId.Count(character => character == '/') != 1)
            throw new InvalidDataException($"profile id '{profileId}' is not canonical");
        return profileId.Replace('/', '_');
    }

    private static IReadOnlyList<FreezeCase> Cases() =>
    [
        Structured(AknLuDocumentProfile.ProfileId, "xml", "lex:synthetic-akn-document", AknDocument),
        Structured(AknLuDuplicateSclAttributeProfile.ProfileId, "xml",
            "lex:synthetic-akn-duplicate", AknDuplicateAttribute),
        Direct(AknLuProfile.ProfileId, "xml", "lex:synthetic-akn-v1", AknV1,
            source => AknLuProfile.Extract(Text(source), "lex:synthetic-akn-v1")),
        Direct(AknLuProfileV2.ProfileId, "xml", "lex:synthetic-akn-v2", AknV2,
            source => AknLuProfileV2.Extract(Text(source), "lex:synthetic-akn-v2")),
        Direct(Fmx4EuProfile.ProfileId, "xml", "lex:synthetic-fmx", Fmx,
            source => Fmx4EuProfile.Extract(Text(source), "lex:synthetic-fmx")),
        Structured(TolerantHtmlEuProfile.ProfileId, "html", "lex:synthetic-tolerant", TolerantHtml),
        Direct(PdfLuProfile.ProfileId, "pdf", "lex:synthetic-pdf", PdfAct,
            source => PdfLuProfile.Extract(source, "lex:synthetic-pdf")),
        Direct(PdfMemorialLuProfile.ProfileId, "pdf",
            "lu-legilux:loi-2099-02-19-n99:2099-03-30", PdfMemorialV1,
            source => PdfMemorialLuProfile.Extract(source,
                "lu-legilux:loi-2099-02-19-n99:2099-03-30")),
        Direct(PdfMemorialLuProfileV2.ProfileId, "pdf",
            "lu-legilux:loi-2098-06-17-n42:2098-07-01", PdfMemorialV2,
            source => PdfMemorialLuProfileV2.Extract(source,
                "lu-legilux:loi-2098-06-17-n42:2098-07-01",
                "Synthetic law of 17 June 2098 concerning the imaginary laboratory"),
            "Synthetic law of 17 June 2098 concerning the imaginary laboratory"),
        Structured(LegacyXlinkEuProfile.ProfileId, "html", "lex:synthetic-xlink", XlinkHtml),
        Structured(XhtmlEuProfile.ProfileId, "html", "lex:synthetic-xhtml", Xhtml),
    ];

    private static FreezeCase Structured(
        string profileId, string extension, string lexId, Func<byte[]> source) =>
        new(profileId, extension, lexId, null, source, bytes =>
        {
            var result = StructuredTextExtractor.Extract(Text(bytes), lexId);
            return new CanonResult(result.ProfileId, result.Extraction);
        });

    private static FreezeCase Direct(
        string profileId, string extension, string lexId, Func<byte[]> source,
        Func<byte[], Extraction> extract, string? workTitle = null) =>
        new(profileId, extension, lexId, workTitle, source,
            bytes => new CanonResult(profileId, extract(bytes)));

    private static byte[] ContractBytes() => Json(writer =>
    {
        if (!CultureInfo.CurrentCulture.Equals(CultureInfo.InvariantCulture)
            || !CultureInfo.CurrentUICulture.Equals(CultureInfo.InvariantCulture))
            throw new InvalidDataException("canon/1 generation requires invariant culture");
        writer.WriteStartObject();
        writer.WriteString("schema", "lex-canon-freeze/1");
        writer.WriteString("canon", "canon/1");
        writer.WriteString("application_baseline", "addc13b07ea5ce83c2ab1c4c7b5f5d8b4bc43c9f");
        writer.WriteString("lex_derive_tree", "69f0bef039a569f897e7ea81cefa6850d65606db");
        writer.WriteStartArray("profile_ids");
        foreach (var profileId in ProfileIds) writer.WriteStringValue(profileId);
        writer.WriteEndArray();
        writer.WriteString("target_framework", "net10.0");
        writer.WriteString("sdk", "10.0.400");
        writer.WriteStartArray("dependencies");
        Dependency(writer, "HtmlAgilityPack", "1.12.4");
        Dependency(writer, "PdfPig", "0.1.11");
        writer.WriteEndArray();
        writer.WriteStartObject("invariants");
        writer.WriteString("culture", "InvariantCulture");
        writer.WriteString("encoding", "UTF-8 without BOM");
        writer.WriteString("line_endings", "LF");
        writer.WriteString("path_order", "ordinal");
        writer.WriteString("hash", "SHA-256 lowercase hexadecimal");
        writer.WriteEndObject();
        writer.WriteEndObject();
    });

    private static void Dependency(Utf8JsonWriter writer, string name, string version)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteString("version", version);
        writer.WriteEndObject();
    }

    private static byte[] CaseBytes(FreezeCase value, string sourceFile) => Json(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("profile_id", value.ProfileId);
        writer.WriteString("lex_id_base", value.LexId);
        String(writer, "work_title", value.WorkTitle);
        writer.WriteString("source_file", sourceFile);
        writer.WriteEndObject();
    });

    private static byte[] ExtractionBytes(CanonResult result) => Json(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("profile_id", result.ProfileId);
        writer.WriteStartArray("provisions");
        foreach (var provision in result.Extraction.Provisions)
        {
            writer.WriteStartObject();
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
    });

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
            .Where(value => !string.Equals(value.Path, ManifestName, StringComparison.Ordinal))
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
        var path = Path.GetFullPath(Path.Combine(fullRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
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

    private static byte[] AknDocument() => Source("""
        <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0">
          <act><body>
            <alinea><content><p>Synthetic public notice for the blue orchard.</p></content></alinea>
            <alinea><content><table><tr><th>Item</th><th>Value</th></tr><tr><td>Compass</td><td>7</td></tr></table></content></alinea>
          </body></act>
        </akomaNtoso>
        """);

    private static byte[] AknDuplicateAttribute() => Source("""
        <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0" xmlns:scl="http://www.scl.lu">
          <act><body><article id="art_duplicate"><num>Art. 3.</num><heading>Blue table</heading>
            <alinea><content><table scl:cols-nb="5" scl:cols-nb="5"><tr><td>Synthetic duplicate repair text.</td></tr></table></content></alinea>
          </article></body></act>
        </akomaNtoso>
        """);

    private static byte[] AknV1() => Source("""
        <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0" xmlns:scl="http://www.scl.lu">
          <act><body><chapter><heading>Invented chapter</heading>
            <article id="art_one"><num>Art. 1.</num><heading>Compass rule</heading>
              <scl:metadata><scl:jolux name="uriThis">https://example.test/eli/synthetic/art1</scl:jolux><scl:jolux name="dateApplicability">2099-01-02</scl:jolux></scl:metadata>
              <alinea><content><p>The blue compass points to <ref href="https://example.test/reference">the synthetic register</ref>.</p></content></alinea>
            </article>
          </chapter></body></act>
        </akomaNtoso>
        """);

    private static byte[] AknV2() => Source("""
        <akomaNtoso xmlns="http://docs.oasis-open.org/legaldocml/ns/akn/3.0">
          <act><body>
            <article id="art_empty" wId="/eli/etat/leg/loi/2099/01/01/n99/art_empty"><num/><alinea><content><p/></content></alinea></article>
            <article id="art_real" wId="/eli/etat/leg/loi/2099/01/01/n99/art_real"><num>Art. 2.</num><heading>Non-BMP span</heading><alinea><content><p>The synthetic compass 🧭 points north.</p></content></alinea></article>
          </body></act>
        </akomaNtoso>
        """);

    private static byte[] Fmx() => Source("""
        <CONS.ACT><ENACTING.TERMS><DIVISION><TITLE><TI><P>CHAPTER SYNTHETIC</P></TI></TITLE>
          <ARTICLE IDENTIFIER="001"><TI.ART>Article 1</TI.ART>
            <PARAG IDENTIFIER="001.001"><NO.PARAG>1.</NO.PARAG><ALINEA>The invented register contains seven entries.</ALINEA></PARAG>
          </ARTICLE>
        </DIVISION></ENACTING.TERMS></CONS.ACT>
        """);

    private static byte[] TolerantHtml() => Source("""
        <html><head><meta http-equiv=Content-Type content="text/html"></head><body>
          <div><p class=title-article-norm>Article 1</p>
          <p>The blue orchard keeps seven tokens.<br>A second synthetic line follows.</p></div>
        </body></html>
        """);

    private static byte[] XlinkHtml() => Source("""
        <?xml version="1.0" encoding="utf-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml"><body><div>
          <p class="title-article-norm">Article 1</p>
          <p><a xlink:href="https://example.test/xlink">Synthetic xlink citation</a> applies to the blue orchard.</p>
        </div></body></html>
        """);

    private static byte[] Xhtml() => Source("""
        <?xml version="1.0" encoding="utf-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml"><body><div class="eli-container">
          <div id="chapter_blue" class="eli-subdivision"><p class="title-division-1">CHAPTER BLUE</p>
            <div id="art_1" class="eli-subdivision"><p class="title-article-norm">Article 1</p>
              <div class="norm"><span class="no-parag">1.</span><p>The compass 🧭 points to the blue orchard.</p></div>
              <p class="modref"><a href="https://example.test/change" title="Synthetic change record">C1</a></p>
            </div>
          </div>
        </div></body></html>
        """);

    private static byte[] PdfAct() => Pdf(
        "Synthetic consolidated notice",
        "Art. 1.",
        "The blue compass points north.",
        "Art. 2. Invented register",
        "Seven synthetic entries are recorded.");

    private static byte[] PdfMemorialV1() => Pdf(
        "Synthetic Gazette",
        "Sommaire",
        "Texte coordonne de la loi du 19 fevrier 2099 concernant le jardin bleu",
        "Texte coordonne",
        "Art. 1er.",
        "Le jardin bleu contient sept arbres fictifs.",
        "Art. 2.",
        "La boussole synthetique indique le nord.");

    private static byte[] PdfMemorialV2() => Pdf(
        "Synthetic Gazette",
        "Texte coordonne de la loi du 17 juin 2098 concernant le laboratoire imaginaire",
        "Texte coordonne",
        "Art. premier - Objet",
        "Le laboratoire fictif conserve sept jetons.",
        "Article 2 - Controle",
        "La boussole synthetique controle le registre.");

    private static byte[] Pdf(params string[] lines)
    {
        if (lines.Any(line => line.Any(character => character > 0x7f)))
            throw new InvalidDataException("synthetic PDF text must be ASCII");
        var content = new StringBuilder("BT\n/F1 11 Tf\n72 760 Td\n");
        foreach (var (line, index) in lines.Select((line, index) => (line, index)))
        {
            if (index > 0) content.Append("0 -18 Td\n");
            content.Append('(').Append(EscapePdf(line)).Append(") Tj\n");
        }
        content.Append("ET\n");
        var stream = content.ToString();
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };

        using var pdf = new MemoryStream();
        void Put(string value) => pdf.Write(Encoding.ASCII.GetBytes(value));
        Put("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(pdf.Position);
            Put($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = pdf.Position;
        Put($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            Put($"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        Put($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
        return pdf.ToArray();
    }

    private static string EscapePdf(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("(", "\\(", StringComparison.Ordinal)
        .Replace(")", "\\)", StringComparison.Ordinal);

    private sealed record FreezeCase(
        string ProfileId,
        string Extension,
        string LexId,
        string? WorkTitle,
        Func<byte[]> Source,
        Func<byte[], CanonResult> Extract);

    private sealed record CanonResult(string ProfileId, Extraction Extraction);
}
