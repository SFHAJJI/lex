using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The title and Official Journal reference a Formex main unit states (EU index /7, Release 3's item 1), read from three
/// packages the Publications Office served and this repository retains byte for byte: the GDPR as published (a reference of
/// the Official Journal before October 2023: series, issue, year and first page, with the issue's date from the act's document
/// record), an act published act by act in 2026 (the act's own number, "L, 2026/1965"), and the GDPR's consolidated version
/// (the publication of the act it consolidates). A title's words are read with its markup and notes removed, and every stored
/// raw form reads back to the same values.
/// </summary>
[TestClass]
public sealed class EuFormexPublicationTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "EuDocumentFetch", name);

    /// <summary>A package's main unit and its document records' PUBLICATION.REF by entry, as the main-body producer reads them.</summary>
    private static (XElement Root, string Entry, Dictionary<string, XElement> Records) MainUnit(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        XElement? root = null;
        string? entry = null;
        var records = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var item in archive.Entries.Where(static item => item.FullName.EndsWith(".xml", StringComparison.Ordinal)))
        {
            using var stream = item.Open();
            var unit = XDocument.Load(stream, LoadOptions.PreserveWhitespace).Root!;
            var name = unit.Name.LocalName;
            if (name is "ACT" or "CONS.ACT" or "CONS.OTH")
            {
                Assert.IsNull(root, "one main unit");
                (root, entry) = (unit, item.FullName);
            }

            if (name == "DOC" && unit.Elements("PUBLICATION.REF").SingleOrDefault() is { } publication)
                records[item.FullName] = publication;
        }

        return (root ?? throw new AssertFailedException("no main unit"), entry!, records);
    }

    private static EuFormexPublication Read(byte[] package)
    {
        var (root, entry, records) = MainUnit(package);
        var publication = EuFormexPublicationReader.Read(root, entry, records);
        Assert.IsNotNull(publication);
        Assert.AreEqual(entry, publication.PackageEntry);
        return publication;
    }

    private const string GdprWords =
        "Regulation (EU) 2016/679 of the European Parliament and of the Council of 27 April 2016 on the protection of natural persons " +
        "with regard to the processing of personal data and on the free movement of such data, and repealing Directive 95/46/EC " +
        "(General Data Protection Regulation) (Text with EEA relevance)";

    [TestMethod]
    public void TheGdprAsPublishedIsOjL119Of4May2016Page1ReadFromItsDocumentReferenceAndItsRecord()
    {
        var publication = Read(File.ReadAllBytes(Fixture("gdpr-fmx4-200-body.bin")));
        var reference = publication.Reference!;
        Assert.AreEqual(EuOfficialJournalReferenceKinds.ActPublication, reference.Kind);
        Assert.AreEqual(EuOfficialJournalNumberForms.SeriesNumber, reference.NumberForm);
        Assert.AreEqual("L", reference.Series);
        Assert.AreEqual("119", reference.OjNumber);
        Assert.IsNull(reference.ActNumber);
        Assert.AreEqual("2016", reference.Year);
        Assert.AreEqual("1", reference.FirstPage);
        Assert.AreEqual("2016-05-04", reference.PublicationDate, "the issue's date is its document record's, never the act's own 27 April 2016");
        StringAssert.StartsWith(reference.ReferenceXml, "<DOCUMENT.REF FILE=\"L_2016119EN.01000101.doc.xml\">");
        StringAssert.StartsWith(reference.PublicationXml!, "<PUBLICATION.REF ");
        Assert.AreEqual(GdprWords, publication.TitleText, "the title's words with its markup (HT, DATE) removed");
        StringAssert.Contains(publication.TitleXml!, "<HT TYPE=\"UC\">Regulation</HT>", "the markup is kept as the package states it");
        Assert.AreEqual(reference, EuFormexPublicationReader.ReferenceOfXml(reference.Kind, reference.ReferenceXml, reference.PublicationXml),
            "the stored raw forms read back to the same reference");
        Assert.AreEqual(publication.TitleText, EuFormexPublicationReader.TitleTextOfXml(publication.TitleXml!));
    }

    [TestMethod]
    public void AnActPublishedActByActIsNumberedByItsOwnNumberAndDatedByItsIssue()
    {
        var publication = Read(File.ReadAllBytes(Fixture("new-fmx4-200-body.bin")));
        var reference = publication.Reference!;
        Assert.AreEqual(EuOfficialJournalReferenceKinds.ActPublication, reference.Kind);
        Assert.AreEqual(EuOfficialJournalNumberForms.ActNumber, reference.NumberForm);
        Assert.AreEqual("L", reference.Series);
        Assert.IsNull(reference.OjNumber, "an act published act by act has no issue number");
        Assert.AreEqual("2026/1965", reference.ActNumber);
        Assert.AreEqual("2026", reference.Year);
        Assert.AreEqual("2026-08-27", reference.PublicationDate, "published the day after its date of 26 August 2026");
        StringAssert.StartsWith(publication.TitleText!, "Commission Implementing Regulation (EU) 2026/1965 of 26 August 2026 amending for the 360th time");
        Assert.AreEqual(reference, EuFormexPublicationReader.ReferenceOfXml(reference.Kind, reference.ReferenceXml, reference.PublicationXml));
    }

    [TestMethod]
    [DataRow("eng")]
    [DataRow("fra")]
    public async Task AConsolidatedVersionNamesThePublicationOfTheActItConsolidates(string language)
    {
        var publication = Read(await EuFormexMainBodyLegalContentProducerTests.ReadConsolidatedPackage(language));
        var reference = publication.Reference!;
        Assert.AreEqual(EuOfficialJournalReferenceKinds.ConsolidatedActPublication, reference.Kind);
        Assert.AreEqual(EuOfficialJournalNumberForms.SeriesNumber, reference.NumberForm);
        Assert.AreEqual(("L", "119", "2016", "1", "2016-05-04"),
            (reference.Series, reference.OjNumber, reference.Year, reference.FirstPage, reference.PublicationDate),
            "the base act's own reference, never a corrigendum's or an amending act's");
        StringAssert.StartsWith(reference.PublicationXml!, "<DATE ISO=\"20160504\">");
        if (language == "eng") Assert.AreEqual(GdprWords, publication.TitleText);
        Assert.AreEqual(reference, EuFormexPublicationReader.ReferenceOfXml(reference.Kind, reference.ReferenceXml, reference.PublicationXml));
    }

    [TestMethod]
    public void AStoredRawFormIsTheSameBytesOnEverySystemWhateverItsLineEndingsSpacesAndMarks()
    {
        // The repair of 2026-10-31: derive-r3's index, derived on Linux, refused to open on Windows because the reader
        // re-serialised a title with a line feed as "\r\n" (XElement.ToString's newline handling replaces each line feed with
        // the system's line ending). A package's CRLF is one line feed once parsed (XML's own rule), and the stored form keeps it
        // as one line feed on every system; a no-break space, a combining mark and a decomposed accent are kept as the code
        // points the package states, never normalised, and the words are read alike under invariant globalization.
        const string Decomposed = "e\u0301";
        var unit = Encoding.UTF8.GetBytes(
            "<ACT><TITLE><TI>\r\n<P><HT TYPE=\"UC\">Council Directive</HT> 2006/112/EC</P>\r\n<P>of <DATE ISO=\"20061128\">28\u00a0November 2006</DATE></P>" +
            "<P>on the common syst" + Decomposed + "me of value added tax, d\u00e9j\u00e0 r\u00e9vis" + Decomposed + "e</P></TI></TITLE>" +
            "<BIB.INSTANCE><DOCUMENT.REF FILE=\"L_2006347EN.01000101.doc.xml\">\r\n<COLL>L</COLL>\r\n<NO.OJ>347</NO.OJ><YEAR>2006</YEAR><PAGE.FIRST>1</PAGE.FIRST></DOCUMENT.REF></BIB.INSTANCE></ACT>");
        var root = XDocument.Load(new MemoryStream(unit), LoadOptions.PreserveWhitespace).Root!;
        var publication = EuFormexPublicationReader.Read(root, "L_2006347EN.01000101.xml", new Dictionary<string, XElement>(StringComparer.Ordinal))!;

        var expectedXml = "<TITLE><TI>\n<P><HT TYPE=\"UC\">Council Directive</HT> 2006/112/EC</P>\n<P>of <DATE ISO=\"20061128\">28\u00a0November 2006</DATE></P>" +
            "<P>on the common syst" + Decomposed + "me of value added tax, d\u00e9j\u00e0 r\u00e9vis" + Decomposed + "e</P></TI></TITLE>";
        Assert.AreEqual(expectedXml, publication.TitleXml, "one line feed per line ending, the code points as stated, on this system");
        Assert.DoesNotContain('\r', publication.TitleXml!);
        Assert.AreEqual("Council Directive 2006/112/EC of 28 November 2006 on the common syst" + Decomposed + "me of value added tax, d\u00e9j\u00e0 r\u00e9vis" + Decomposed + "e",
            publication.TitleText, "a no-break space is white space between words; the marks are kept as stated");
        Assert.AreEqual(publication.TitleText, EuFormexPublicationReader.TitleTextOfXml(publication.TitleXml!), "the stored form reads back to the same words");
        Assert.AreEqual(publication.TitleXml, EuFormexPublicationReader.Serialise(XElement.Parse(publication.TitleXml!, LoadOptions.PreserveWhitespace)),
            "serialising the stored form again gives the same bytes");
        Assert.IsNull(EuFormexPublicationReader.TitleTextOfXml(publication.TitleXml!.Replace("\n", "\r\n", StringComparison.Ordinal)),
            "a form with the system's line ending is not the reader's own");

        var reference = publication.Reference!;
        Assert.AreEqual("<DOCUMENT.REF FILE=\"L_2006347EN.01000101.doc.xml\">\n<COLL>L</COLL>\n<NO.OJ>347</NO.OJ><YEAR>2006</YEAR><PAGE.FIRST>1</PAGE.FIRST></DOCUMENT.REF>",
            reference.ReferenceXml);
        Assert.AreEqual(("L", "347", "2006", "1", (string?)null), (reference.Series, reference.OjNumber, reference.Year, reference.FirstPage, reference.PublicationDate));
        Assert.AreEqual(reference, EuFormexPublicationReader.ReferenceOfXml(reference.Kind, reference.ReferenceXml, null), "the stored elements read back to the same reference");
        Assert.AreEqual("d\u00e9j\u00e0 r\u00e9vis" + Decomposed + "e", EuropeIndexBuilder.LiteralTitleText(" d\u00e9j\u00e0\u00a0r\u00e9vis" + Decomposed + "e\r\n"),
            "a literal's words: the same rule, no normalisation");
    }

    [TestMethod]
    public void ATitlesWordsLeaveOutItsNotesAndKeepItsQuotationMarks()
    {
        var title = XElement.Parse(
            "<TITLE><TI><P><HT TYPE=\"UC\">Council Regulation</HT> (EC) No 1/2003</P><P>of <DATE ISO=\"20021216\">16 December 2002</DATE></P>" +
            "<P>on the <QUOT.START CODE=\"2018\" ID=\"QS0001\" REF.END=\"QE0001\"/>rules<QUOT.END CODE=\"2019\" ID=\"QE0001\" REF.START=\"QS0001\"/>" +
            "\n   laid down<NOTE NOTE.ID=\"E0001\"><P>Text with EEA relevance</P></NOTE></P></TI><STI><P>(recast)</P></STI></TITLE>",
            LoadOptions.PreserveWhitespace);
        Assert.AreEqual("Council Regulation (EC) No 1/2003 of 16 December 2002 on the ‘rules’ laid down (recast)", EuFormexPublicationReader.TitleTextOf(title));
        Assert.AreEqual("Contents", EuFormexPublicationReader.TitleTextOf(XElement.Parse("<TITLE><TI><NP><TXT>Contents</TXT></NP></TI></TITLE>")),
            "a title with no paragraph is read whole");
        Assert.IsNull(EuFormexPublicationReader.TitleTextOf(XElement.Parse("<TITLE><TI><P><NOTE><P>only a note</P></NOTE></P></TI></TITLE>")));
        Assert.IsNull(EuFormexPublicationReader.TitleTextOf(XElement.Parse("<TITLE><TI><P><QUOT.START CODE=\"ZZZZZZZZ\"/>bad</P></TI></TITLE>")),
            "a quotation mark of no Unicode code gives no title");
        Assert.IsNull(EuFormexPublicationReader.TitleTextOfXml("<TI><P>not a title</P></TI>"));
        Assert.IsNull(EuFormexPublicationReader.TitleTextOfXml("<TITLE ><TI><P>reformatted</P></TI></TITLE>"), "a stored form must be the reader's own");
    }

    [TestMethod]
    public void ADocumentRecordOfAnotherIssueGivesNoDateAndAReferenceWithoutSeriesOrNumberIsNone()
    {
        var reference = XElement.Parse("<DOCUMENT.REF FILE=\"doc.xml\"><COLL>L</COLL><NO.OJ>119</NO.OJ><YEAR>2016</YEAR><PAGE.FIRST>1</PAGE.FIRST></DOCUMENT.REF>");
        var act = new XElement("ACT", new XElement("BIB.INSTANCE", reference), XElement.Parse("<TITLE><TI><P>An act</P></TI></TITLE>"));
        var other = XElement.Parse("<PUBLICATION.REF><COLL>L</COLL><NO.OJ>120</NO.OJ><DATE ISO=\"20160505\">20160505</DATE></PUBLICATION.REF>");
        var read = EuFormexPublicationReader.Read(act, "act.xml", new Dictionary<string, XElement> { ["doc.xml"] = other })!.Reference!;
        Assert.IsNull(read.PublicationDate, "a record of issue 120 does not date issue 119");
        Assert.IsNull(read.PublicationXml);
        Assert.AreEqual("119", read.OjNumber);

        var unnamed = EuFormexPublicationReader.Read(act, "act.xml", new Dictionary<string, XElement>())!.Reference!;
        Assert.IsNull(unnamed.PublicationDate, "no record of the named file, no date");

        reference.Element("COLL")!.Remove();
        var withoutSeries = EuFormexPublicationReader.Read(act, "act.xml", new Dictionary<string, XElement>())!;
        Assert.IsNull(withoutSeries.Reference, "a reference without its series is none");
        Assert.AreEqual("An act", withoutSeries.TitleText, "and the title is still read");
        Assert.IsNull(EuFormexPublicationReader.Read(new XElement("ACT"), "act.xml", new Dictionary<string, XElement>()),
            "a unit stating neither gives no publication");
        Assert.IsNull(EuFormexPublicationReader.ReferenceOfXml(EuOfficialJournalReferenceKinds.ActPublication,
            "<DOCUMENT.REF.CONS><COLL>L</COLL><NO.OJ>1</NO.OJ><YEAR>2016</YEAR></DOCUMENT.REF.CONS>", null),
            "a consolidated bibliography's element is no act's own reference");
    }
}
