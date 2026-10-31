using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Lex.V3.Ingest.Europe;

/// <summary>The kind of an Official Journal reference a Formex package states (EU index /7).</summary>
public static class EuOfficialJournalReferenceKinds
{
    /// <summary>
    /// The act's own publication: the <c>DOCUMENT.REF</c> of an original act's main unit (<c>ACT</c>, or the Charter's
    /// <c>GENERAL</c>), with the publication date of the issue its document record (<c>DOC</c>) names.
    /// </summary>
    public const string ActPublication = "act_publication";

    /// <summary>
    /// The publication of the act a consolidated version consolidates: the <c>DOCUMENT.REF.CONS</c> of the consolidated
    /// package's base act (its <c>FAM.COMP</c>'s first <c>BIB.DATA</c>), with the date that bibliography gives it. A
    /// consolidated text is not published in the Official Journal; this names where the act it consolidates was.
    /// </summary>
    public const string ConsolidatedActPublication = "consolidated_act_publication";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { ActPublication, ConsolidatedActPublication });
}

/// <summary>How an Official Journal reference numbers its issue or act (EU index /7).</summary>
public static class EuOfficialJournalNumberForms
{
    /// <summary>A series, an issue number (<c>NO.OJ</c>), a year and a first page: "L 119 … p. 1", the Official Journal before October 2023.</summary>
    public const string SeriesNumber = "series_number";

    /// <summary>A series and the act's own number (<c>NO.DOC</c>: year and number), "L, 2024/1689": the act-by-act Official Journal since October 2023.</summary>
    public const string ActNumber = "act_number";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { SeriesNumber, ActNumber });
}

/// <summary>
/// One Official Journal reference a Formex package states, read from its bibliographic markup and nothing else: the series
/// (<c>COLL</c>), the issue number as served (<c>NO.OJ</c>) or the act's number (<c>NO.DOC</c>'s year and number), the year,
/// the first page as served, and the publication date when the package states one that agrees with the reference. The raw
/// forms are the elements it was read from, as this reader re-serialises them (<see cref="ReferenceXml"/>,
/// <see cref="PublicationXml"/>), so the index can read every field again from them.
/// </summary>
public sealed record EuFormexOfficialJournalReference(
    string Kind,
    string NumberForm,
    string Series,
    string? OjNumber,
    string? ActNumber,
    string Year,
    string? FirstPage,
    string? PublicationDate,
    string ReferenceXml,
    string? PublicationXml);

/// <summary>
/// What an admitted main body's package states of the act's publication (EU index /7, Release 3's item 1): the main unit's
/// <c>TITLE</c> (its markup re-serialised, and its words with the markup and the notes removed) and the Official Journal
/// reference. Read beside the articles and never part of their identities or of the outcome's: a package whose markup gives
/// neither is read exactly as before.
/// </summary>
public sealed record EuFormexPublication(
    string PackageEntry,
    string? TitleXml,
    string? TitleText,
    EuFormexOfficialJournalReference? Reference);

/// <summary>
/// Reads a Formex main unit's title and Official Journal reference (<see cref="EuFormexPublication"/>), and reads them again
/// from their stored raw forms for the index's check. Never throws on a shape it does not know: a field it cannot read is
/// absent, and a reference without a series and a number is no reference.
/// </summary>
public static class EuFormexPublicationReader
{
    /// <summary>The rule a title's words are read by, said where an answer serves them.</summary>
    public const string TitleTextRule =
        "the words of each paragraph (P) of the title, in order, joined by one space, with the notes (NOTE) and every other piece of " +
        "markup removed and each run of white space written as one space; the markup is kept in the index as the package states it";

    /// <summary>
    /// The publication a main unit states: <paramref name="root"/> is the unit's root (<c>ACT</c>, <c>GENERAL</c>,
    /// <c>CONS.ACT</c> or <c>CONS.OTH</c>), <paramref name="publicationReferences"/> each document record's
    /// <c>PUBLICATION.REF</c> by its package entry. Null when the unit states neither a title nor a reference.
    /// </summary>
    public static EuFormexPublication? Read(
        XElement root,
        string packageEntry,
        IReadOnlyDictionary<string, XElement> publicationReferences)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageEntry);
        ArgumentNullException.ThrowIfNull(publicationReferences);
        XElement? title;
        EuFormexOfficialJournalReference? reference;
        switch (root.Name.LocalName)
        {
            case "ACT" or "GENERAL":
            {
                title = Only(root, "TITLE");
                var bibliography = Only(root, "BIB.INSTANCE");
                var element = bibliography is null ? null : Only(bibliography, "DOCUMENT.REF");
                XElement? publication = null;
                if (element?.Attribute("FILE")?.Value is { Length: > 0 } file && publicationReferences.TryGetValue(file, out var named))
                    publication = named;
                reference = element is null ? null : ReferenceOf(EuOfficialJournalReferenceKinds.ActPublication, element, publication);
                break;
            }
            case "CONS.ACT" or "CONS.OTH":
            {
                var families = root.Descendants().Where(static value => value.Name.LocalName == "FAM.COMP").ToArray();
                var data = families.Length == 1 ? families[0].Elements().FirstOrDefault(static value => value.Name.LocalName == "BIB.DATA") : null;
                title = data is null ? null : Only(data, "TITLE");
                var bibliography = data is null ? null : Only(data, "BIB.INSTANCE.CONS");
                var element = bibliography is null ? null : Only(bibliography, "DOCUMENT.REF.CONS");
                var date = bibliography is null ? null : Only(bibliography, "DATE");
                reference = element is null ? null : ReferenceOf(EuOfficialJournalReferenceKinds.ConsolidatedActPublication, element, date);
                break;
            }
            default:
                return null;
        }

        var titleText = title is null ? null : TitleTextOf(title);
        var titleXml = titleText is null ? null : Serialise(title!);
        return titleXml is null && reference is null ? null : new EuFormexPublication(packageEntry, titleXml, titleText, reference);
    }

    /// <summary>The words of a title (<see cref="TitleTextRule"/>), or null when it holds none.</summary>
    public static string? TitleTextOf(XElement title)
    {
        ArgumentNullException.ThrowIfNull(title);
        // The title's own paragraphs: a paragraph inside a note is left out with its note, and one inside another paragraph is
        // read with it.
        var paragraphs = title.Descendants()
            .Where(paragraph => paragraph.Name.LocalName == "P" && !paragraph.Ancestors().TakeWhile(parent => parent != title)
                .Any(static parent => parent.Name.LocalName is "NOTE" or "P"))
            .ToArray();
        var builder = new StringBuilder();
        if (!(paragraphs.Length == 0 ? Append(title, builder) : paragraphs.All(paragraph =>
            {
                builder.Append(' ');
                return Append(paragraph, builder);
            })))
            return null;
        var text = string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length == 0 ? null : text;
    }

    /// <summary>A stored title's words read again from its stored markup, or null when the markup is not one title.</summary>
    public static string? TitleTextOfXml(string titleXml)
    {
        ArgumentNullException.ThrowIfNull(titleXml);
        var element = ParseElement(titleXml);
        return element is { Name.LocalName: "TITLE" } && Serialise(element) == titleXml ? TitleTextOf(element) : null;
    }

    /// <summary>
    /// A stored reference read again from its stored raw forms: the same reference when they are the forms a reading made,
    /// else null. <paramref name="kind"/> says what the publication form is (a document record's <c>PUBLICATION.REF</c>, or a
    /// consolidated bibliography's <c>DATE</c>).
    /// </summary>
    public static EuFormexOfficialJournalReference? ReferenceOfXml(string kind, string referenceXml, string? publicationXml)
    {
        ArgumentNullException.ThrowIfNull(referenceXml);
        var element = ParseElement(referenceXml);
        var publication = publicationXml is null ? null : ParseElement(publicationXml);
        if (element is null || (publicationXml is not null && publication is null) ||
            Serialise(element) != referenceXml || (publication is not null && Serialise(publication) != publicationXml))
            return null;
        return ReferenceOf(kind, element, publication);
    }

    private static EuFormexOfficialJournalReference? ReferenceOf(string kind, XElement element, XElement? publication)
    {
        var expected = kind == EuOfficialJournalReferenceKinds.ActPublication ? "DOCUMENT.REF"
            : kind == EuOfficialJournalReferenceKinds.ConsolidatedActPublication ? "DOCUMENT.REF.CONS" : null;
        if (expected is null || element.Name.LocalName != expected) return null;
        var series = OnlyText(element, "COLL");
        if (series is null) return null;
        var firstPage = OnlyText(element, "PAGE.FIRST");
        string numberForm;
        string? ojNumber = null;
        string? actNumber = null;
        string? year;
        if (OnlyText(element, "NO.OJ") is { } issue)
        {
            numberForm = EuOfficialJournalNumberForms.SeriesNumber;
            ojNumber = issue;
            year = OnlyText(element, "YEAR");
        }
        else if (ActNumberOf(element) is { } number)
        {
            numberForm = EuOfficialJournalNumberForms.ActNumber;
            actNumber = number.Number;
            year = number.Year;
        }
        else
        {
            return null;
        }

        if (year is null || year.Length != 4 || !year.All(char.IsAsciiDigit)) return null;

        // The publication date, only when its record agrees with the reference: a document record's PUBLICATION.REF names the same
        // series and number (an act number when it states one); a consolidated bibliography's DATE is its base act's own.
        string? date = null;
        string? publicationXml = null;
        if (publication is not null)
        {
            XElement? dateElement;
            if (kind == EuOfficialJournalReferenceKinds.ActPublication)
            {
                var agrees = publication.Name.LocalName == "PUBLICATION.REF" && OnlyText(publication, "COLL") == series &&
                    (numberForm == EuOfficialJournalNumberForms.SeriesNumber
                        ? OnlyText(publication, "NO.OJ") == ojNumber
                        : OnlyText(publication, "NO.OJ") is null && (ActNumberOf(publication) is not { } stated || stated.Number == actNumber));
                dateElement = agrees ? Only(publication, "DATE") : null;
            }
            else
            {
                dateElement = publication.Name.LocalName == "DATE" ? publication : null;
            }

            if (dateElement?.Attribute("ISO")?.Value is { } iso &&
                DateOnly.TryParseExact(iso, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                date = parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                publicationXml = Serialise(publication);
            }
        }

        return new EuFormexOfficialJournalReference(kind, numberForm, series, ojNumber, actNumber, year, firstPage, date,
            Serialise(element), publicationXml);
    }

    private static (string Year, string Number)? ActNumberOf(XElement element)
    {
        var number = Only(element, "NO.DOC");
        if (number is null) return null;
        var year = OnlyText(number, "YEAR");
        var current = OnlyText(number, "NO.CURRENT");
        return year is null || current is null || !current.All(char.IsAsciiDigit) ? null : (year, $"{year}/{current}");
    }

    /// <summary>The one child element of that name, or null for none or several.</summary>
    private static XElement? Only(XElement parent, string localName)
    {
        XElement? found = null;
        foreach (var child in parent.Elements())
        {
            if (child.Name.LocalName != localName) continue;
            if (found is not null) return null;
            found = child;
        }

        return found;
    }

    /// <summary>The trimmed text of the one child element of that name, or null for none, several or empty text.</summary>
    private static string? OnlyText(XElement parent, string localName) =>
        Only(parent, localName)?.Value.Trim() is { Length: > 0 } text ? text : null;

    /// <summary>Appends an element's words, notes left out and quotation markers written as their characters; false on a bad marker.</summary>
    private static bool Append(XElement element, StringBuilder builder)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    builder.Append(text.Value);
                    break;
                case XElement { Name.LocalName: "NOTE" }:
                    break;
                case XElement marker when marker.Name.LocalName is "QUOT.START" or "QUOT.END":
                    var code = marker.Attribute("CODE")?.Value;
                    if (code is null || !int.TryParse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var scalar) || !Rune.IsValid(scalar))
                        return false;
                    builder.Append(new Rune(scalar).ToString());
                    break;
                case XElement child:
                    if (child.Name.LocalName == "P") builder.Append(' ');
                    if (!Append(child, builder)) return false;
                    if (child.Name.LocalName == "P") builder.Append(' ');
                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// An element as this reader stores it: without formatting, and with every character of its text written as it is, so the
    /// same element is the same text on every system. <c>XElement.ToString</c> writes through a writer whose default newline
    /// handling replaces each line feed in a text node with the system's line ending, so a title stored by a derive on Linux
    /// ("\n") was not the title the reader re-serialised on Windows ("\r\n"), and derive-r3's index refused to open there
    /// (2026-10-31); this writer never writes the system's line ending. A parse has already made every line ending one line
    /// feed (XML's own rule), so the stored forms of an index derived on Linux are what this writes.
    /// </summary>
    internal static string Serialise(XElement element)
    {
        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(builder, SerialisationSettings))
            element.WriteTo(writer);
        return builder.ToString();
    }

    private static readonly XmlWriterSettings SerialisationSettings = new()
    {
        OmitXmlDeclaration = true,
        Indent = false,
        // Entitize, not None: None writes a carriage return in text, and a tab, carriage return or line feed in an attribute
        // value, as the raw character, which the next parse normalises away (the review of #1015), so the form would not reopen
        // to itself; Entitize writes a text line feed raw (every form a Linux derive stored re-serialises to the same bytes) and
        // those characters as references, which survive the parse. It never writes the system's line ending.
        NewLineHandling = NewLineHandling.Entitize,
        ConformanceLevel = ConformanceLevel.Fragment,
        CheckCharacters = false,
    };

    private static XElement? ParseElement(string xml)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
            });
            return XElement.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
