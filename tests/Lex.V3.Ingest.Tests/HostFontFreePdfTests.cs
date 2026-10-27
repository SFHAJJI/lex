using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Luxembourg;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Fonts.SystemFonts;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;
using UglyToad.PdfPig.Tokens;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// A PDF is laid out the same on every host: no glyph's box or text comes from a font installed on the machine.
/// </summary>
/// <remarks>
/// PdfPig 0.1.11 reads a TrueType font the PDF does not embed from an installed font of the same name. Before
/// <see cref="HostFontFreePdf"/>, <see cref="ANonEmbeddedTrueTypeFontIsLaidOutFromThePdfsOwnMetricsOnEveryHost"/> failed on
/// Windows, which has Arial Bold (the "L" read from 20.77 to 25.81, its outline, not the PDF's font box from 13.72 to 40),
/// and identically in Ubuntu 24.04 with Windows' Arial Bold in <c>~/.fonts</c>; it passed only where the host has no
/// such font, as the Linux release image. <see cref="AHostThatHasTheFontWouldHaveLaidItOutOtherwise"/> shows the same on
/// any host, with a finder that stands for one that has the font.
/// </remarks>
[TestClass]
public sealed class HostFontFreePdfTests
{
    private const string SourceIdentity = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    /// <summary>The synthetic PDF's layout artifact, the same on Windows and on Linux.</summary>
    private const string ExpectedLayoutSha256 = "9c7f09385e3d860efd15f6d39765b873436e603e0e4fb974bb9ef5104f484587";

    [TestMethod]
    public void ANonEmbeddedTrueTypeFontIsLaidOutFromThePdfsOwnMetricsOnEveryHost()
    {
        var pdf = NonEmbeddedTrueTypePdf();

        var artifact = LuxembourgPdfLayoutEvidenceProducer.ReadLayout(
            pdf, SourceIdentity, CustodyBounds.MaxObjectBytes, CancellationToken.None);

        Assert.IsTrue(artifact.Valid);
        var document = LuxembourgPdfLayoutEvidenceArtifactCodec.Decode(
            artifact.Bytes.Span, LuxembourgPdfLayoutEvidenceProducer.RuleProfileSha256, SourceIdentity,
            artifact.PageCount, artifact.GlyphCount);
        CollectionAssert.AreEqual(new[] { "L", "U", "X" }, document.Glyphs.Select(static glyph => glyph.Text).ToArray());

        // Each glyph's box is the descriptor's /FontBBox [-628 -376 2000 1018] at 10 points, at its origin; each origin
        // advances by the PDF's own /Widths (611, 722). No installed Arial is consulted.
        var origins = new[] { 20d, 26.11d, 33.33d };
        for (var index = 0; index < origins.Length; index++)
        {
            var glyph = document.Glyphs[index];
            Assert.AreEqual("Arial,Bold", glyph.FontName);
            Assert.AreEqual(origins[index] - 6.28d, glyph.Left, 1e-9, $"{glyph.Text} left");
            Assert.AreEqual(origins[index] + 20d, glyph.Right, 1e-9, $"{glyph.Text} right");
            Assert.AreEqual(46.24d, glyph.Bottom, 1e-9, $"{glyph.Text} bottom");
            Assert.AreEqual(60.18d, glyph.Top, 1e-9, $"{glyph.Text} top");
        }

        Assert.AreEqual(ExpectedLayoutSha256, Convert.ToHexStringLower(SHA256.HashData(artifact.Bytes.Span)),
            "The layout artifact's bytes must be these on every host; a host font changes them.");
    }

    [TestMethod]
    public void AHostThatHasTheFontWouldHaveLaidItOutOtherwise()
    {
        var pdf = NonEmbeddedTrueTypePdf();
        var empty = new RecordingFinder(null);
        var installed = new RecordingFinder(EmbeddedTrueTypeProgram());

        var door = Boxes(HostFontFreePdf.Open(pdf));
        var emptyHost = Boxes(HostFontFreePdf.Open(pdf, empty));
        var fontHost = Boxes(HostFontFreePdf.Open(pdf, installed));

        // The document asks the finder the door gives it, and only that one, for the font it does not embed.
        CollectionAssert.Contains(empty.Requests, "Arial,Bold");
        CollectionAssert.Contains(installed.Requests, "Arial,Bold");
        CollectionAssert.AreEqual(emptyHost, door,
            $"The door reads as a host without the font does.\nno font: {string.Join(" | ", emptyHost)}\n"
            + $"door:    {string.Join(" | ", door)}");
        CollectionAssert.AreNotEqual(door, fontHost,
            "A host that has the font would read other boxes: what the door keeps out.");
    }

    [TestMethod]
    public void AnEmbeddedFontNeverAsksTheHost()
    {
        var pdf = Fixture("EuDocumentFetch", "new-pdfa2a-200-body.bin");
        var finder = new RecordingFinder(null);

        using (var document = HostFontFreePdf.Open(pdf, finder))
        {
            foreach (var page in document.GetPages())
            {
                _ = page.Letters.Count;
            }
        }

        Assert.IsEmpty(finder.Requests, "Every font of the PDF/A is embedded, so no name is looked up.");
    }

    [TestMethod]
    public void TheDoorNamesThePdfPigTheProjectPins()
    {
        Assert.AreEqual(HostFontFreePdf.PinnedPdfPigVersion, typeof(PdfDocument).Assembly.GetName().Version,
            "A PdfPig upgrade must re-read the private fields HostFontFreePdf replaces before it is taken.");
    }

    [TestMethod]
    public void NoSourceOpensAPdfOutsideTheDoor()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        var separator = Path.DirectorySeparatorChar;
        var door = Path.Combine("Lex.V3.Ingest", "HostFontFreePdf.cs");
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}obj{separator}") && !path.Contains($"{separator}bin{separator}"))
            .Where(path => !string.Equals(Path.GetRelativePath(src, path), door, StringComparison.Ordinal))
            .Where(path =>
            {
                var code = File.ReadAllText(path, Encoding.UTF8);
                return code.Contains("PdfDocument.Open", StringComparison.Ordinal)
                    || code.Contains("SystemFontFinder", StringComparison.Ordinal)
                    || code.Contains("ISystemFontFinder", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(src, path).Replace('\\', '/'))
            .ToArray();

        Assert.IsEmpty(offenders, "Every PDF is opened through HostFontFreePdf: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// A one-page PDF whose only font is a TrueType "Arial,Bold" it does not embed: <c>/FirstChar</c>, <c>/Widths</c> and
    /// a <c>/FontDescriptor</c> with a <c>/FontBBox</c>, no <c>/FontFile2</c>; "LUX" at 10 points from (20, 50). Every
    /// byte is written here.
    /// </summary>
    internal static byte[] NonEmbeddedTrueTypePdf()
    {
        const string content = "BT /F1 10 Tf 20 50 Td (LUX) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /TrueType /BaseFont /Arial,Bold /FirstChar 76 /LastChar 88 "
                + "/Widths [611 0 0 0 0 0 0 0 0 722 0 0 667] /Encoding /WinAnsiEncoding /FontDescriptor 6 0 R >>",
            "<< /Type /FontDescriptor /FontName /Arial,Bold /Flags 32 /FontBBox [-628 -376 2000 1018] /ItalicAngle 0 "
                + "/Ascent 905 /Descent -212 /CapHeight 716 /StemV 165 >>",
        };

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture,
            $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    /// <summary>
    /// A TrueType program to stand for an installed font: the first one the committed EU PDF/A fixture embeds, read
    /// through PdfPig's own parser. No font file of this machine is read.
    /// </summary>
    private static TrueTypeFont EmbeddedTrueTypeProgram()
    {
        using var document = PdfDocument.Open(Fixture("EuDocumentFetch", "new-pdfa2a-200-body.bin"));
        foreach (var reference in document.Structure.CrossReferenceTable.ObjectOffsets.Keys.OrderBy(static key => key.ObjectNumber))
        {
            if (document.Structure.GetObject(reference).Data is DictionaryToken descriptor
                && descriptor.TryGet(NameToken.FontFile2, out var file)
                && file is IndirectReferenceToken program
                && document.Structure.GetObject(program.Data).Data is StreamToken stream)
            {
                return TrueTypeFontParser.Parse(new TrueTypeDataBytes(stream.Decode(DefaultFilterProvider.Instance).ToArray()));
            }
        }

        throw new AssertFailedException("The PDF/A fixture embeds no TrueType program.");
    }

    private static string[] Boxes(PdfDocument document)
    {
        using (document)
        {
            return document.GetPages()
                .SelectMany(static page => page.Letters)
                .Select(static letter => string.Create(CultureInfo.InvariantCulture,
                    $"{letter.Value} {letter.GlyphRectangle.Left:R} {letter.GlyphRectangle.Bottom:R} "
                    + $"{letter.GlyphRectangle.Right:R} {letter.GlyphRectangle.Top:R}"))
                .ToArray();
        }
    }

    private static byte[] Fixture(string directory, string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", directory, fileName));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>A host's fonts, as the test states them: the names asked for, and the one font it has, if any.</summary>
    private sealed class RecordingFinder(TrueTypeFont? font) : ISystemFontFinder
    {
        internal List<string> Requests { get; } = [];

        public TrueTypeFont GetTrueTypeFont(string name)
        {
            Requests.Add(name);
            return font!;
        }
    }
}
