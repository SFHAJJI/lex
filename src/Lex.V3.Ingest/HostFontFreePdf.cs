using System.Collections;
using System.Reflection;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Fonts.SystemFonts;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Tokens;

namespace Lex.V3.Ingest;

/// <summary>
/// The one door through which this product opens a PDF with PdfPig, so that nothing it reads from a PDF depends on the
/// fonts installed on the machine that reads it.
/// </summary>
/// <remarks>
/// <para>
/// PdfPig 0.1.11 lays out a TrueType font the PDF does not embed from an installed font of the same name:
/// <c>PdfDocumentFactory</c> hands each document's <c>TrueTypeFontHandler</c> the process-wide
/// <see cref="SystemFontFinder.Instance"/>, which reads the Windows font folder, <c>/usr/share/fonts</c> and
/// <c>~/.fonts</c>. A glyph's box then came from that host font's outline, and, when the font dictionary has no
/// <c>/Encoding</c>, its text from that font's cmap. The same bytes read on Windows, which has Arial, and on the Linux
/// release image, which has none, gave other boxes: other layout artefacts, so another corpus.
/// </para>
/// <para>
/// PdfPig has no option for it (<see cref="ParsingOptions"/> has none in 0.1.11, nor in 0.1.16): the finder is a
/// private field of the document's own handler. This door opens the document as PdfPig does, then, before any page loads
/// a font, sets that field of this document's handler to a finder that finds nothing. A non-embedded TrueType font is
/// then read exactly as on a host without it: its advances from the PDF's <c>/Widths</c>, each glyph's box the
/// <c>/FontBBox</c> of its <c>/FontDescriptor</c> at the glyph's origin, and, for a dictionary that names a Standard 14
/// font without those, the Adobe metrics PdfPig bundles. Type 1, CID and Type 3 fonts never read the host.
/// </para>
/// <para>
/// The fields are reached by reflection, on the exact PdfPig the project pins (<c>[0.1.11]</c>). Each step is checked,
/// and one that does not hold (another PdfPig, a renamed field, a font loaded before the finder was set) throws
/// <see cref="PdfHostFontIsolationException"/>, which every caller lets through: a PDF is never recorded as unreadable,
/// nor read with the host's fonts, because the door did not close.
/// </para>
/// </remarks>
internal static class HostFontFreePdf
{
    /// <summary>The PdfPig assembly version whose private fields this door names.</summary>
    internal static readonly Version PinnedPdfPigVersion = new(0, 1, 11, 0);

    /// <summary>Opens the PDF so that no font is read from the host.</summary>
    internal static PdfDocument Open(byte[] bytes) => Open(bytes, NoHostFonts.Instance);

    /// <summary>
    /// Opens the PDF with <paramref name="hostFonts"/> standing for the host's installed fonts. Only tests name a finder:
    /// one that finds a font shows what a host that has it would have read.
    /// </summary>
    internal static PdfDocument Open(byte[] bytes, ISystemFontFinder hostFonts)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(hostFonts);
        RequirePinnedPdfPig();
        var document = PdfDocument.Open(bytes);
        try
        {
            Install(document, hostFonts);
            return document;
        }
        catch (PdfHostFontIsolationException)
        {
            document.Dispose();
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Any other failure of the replacement itself (reflection on a field a later PdfPig typed or named otherwise)
            // is the door's, never the PDF's: it stops the run instead of reading as an unreadable PDF (the review's note).
            document.Dispose();
            throw new PdfHostFontIsolationException($"The font finder could not be replaced: {exception.Message}", exception);
        }
    }

    private static void RequirePinnedPdfPig()
    {
        var version = typeof(PdfDocument).Assembly.GetName().Version;
        if (version != PinnedPdfPigVersion)
        {
            throw new PdfHostFontIsolationException(
                $"PdfPig {version} is not the {PinnedPdfPigVersion} whose font finder this door replaces.");
        }
    }

    private static void Install(PdfDocument document, ISystemFontFinder hostFonts)
    {
        var pages = Read(document, typeof(PdfDocument), "pages", "UglyToad.PdfPig.Content.Pages");
        var pageFactory = Read(pages, pages.GetType(), "defaultPageFactory", "UglyToad.PdfPig.Parser.PageFactory");
        if (pageFactory is not BasePageFactory<Page> { ResourceStore: { } resourceStore })
        {
            throw new PdfHostFontIsolationException("The document's page factory holds no resource store.");
        }

        RequireType(resourceStore, "UglyToad.PdfPig.Content.ResourceStore");
        foreach (var loaded in new[] { "loadedFonts", "loadedDirectFonts" })
        {
            if (Read(resourceStore, resourceStore.GetType(), loaded, null) is not ICollection { Count: 0 })
            {
                throw new PdfHostFontIsolationException(
                    $"The document loaded a font ({loaded}) before its font finder was replaced.");
            }
        }

        var fontFactory = Read(resourceStore, resourceStore.GetType(), "fontFactory", "UglyToad.PdfPig.PdfFonts.FontFactory");
        if (Read(fontFactory, fontFactory.GetType(), "handlers", null) is not IDictionary handlers
            || handlers[NameToken.TrueType] is not { } trueType)
        {
            throw new PdfHostFontIsolationException("The document's font factory has no TrueType handler.");
        }

        RequireType(trueType, "UglyToad.PdfPig.PdfFonts.Parser.Handlers.TrueTypeFontHandler");
        var replaced = 0;
        var trueTypeReplaced = false;
        foreach (var handler in handlers.Values)
        {
            for (var type = handler?.GetType(); type is not null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                             | BindingFlags.DeclaredOnly))
                {
                    if (!typeof(ISystemFontFinder).IsAssignableFrom(field.FieldType))
                    {
                        continue;
                    }

                    field.SetValue(handler, hostFonts);
                    if (!ReferenceEquals(field.GetValue(handler), hostFonts))
                    {
                        throw new PdfHostFontIsolationException($"{type.FullName}.{field.Name} kept its font finder.");
                    }

                    replaced++;
                    trueTypeReplaced |= ReferenceEquals(handler, trueType);
                }
            }
        }

        if (!trueTypeReplaced || replaced == 0)
        {
            throw new PdfHostFontIsolationException("The TrueType handler holds no font finder to replace.");
        }
    }

    private static object Read(object owner, Type declaringType, string fieldName, string? expectedValueType)
    {
        var field = declaringType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new PdfHostFontIsolationException($"{declaringType.FullName} has no field {fieldName}.");
        var value = field.GetValue(owner)
            ?? throw new PdfHostFontIsolationException($"{declaringType.FullName}.{fieldName} is null.");
        if (expectedValueType is not null)
        {
            RequireType(value, expectedValueType);
        }

        return value;
    }

    private static void RequireType(object value, string expectedType)
    {
        if (!string.Equals(value.GetType().FullName, expectedType, StringComparison.Ordinal))
        {
            throw new PdfHostFontIsolationException(
                $"Expected {expectedType} in PdfPig's document graph, found {value.GetType().FullName}.");
        }
    }

    /// <summary>The host this door stands for: one with no font installed, as PdfPig's own finder answers there.</summary>
    private sealed class NoHostFonts : ISystemFontFinder
    {
        internal static readonly NoHostFonts Instance = new();

        // PdfPig's finder answers null for a font the host does not have, and its handlers read null as exactly that.
        public TrueTypeFont GetTrueTypeFont(string name) => null!;
    }
}

/// <summary>
/// PdfPig's document graph is not the one <see cref="HostFontFreePdf"/> names, so the document could not be opened
/// without the host's fonts. Never a property of the PDF: callers let it through rather than record the PDF unreadable.
/// </summary>
internal sealed class PdfHostFontIsolationException(string message, Exception? inner = null) : InvalidOperationException(message, inner);
