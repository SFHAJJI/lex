using Lex.V3.Ingest.Europe;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The EU index /7's titles and Official Journal references of a derived index, re-read on this machine as the reader re-reads
/// them at open, with the first row that disagrees printed field by field (the repair of 2026-10-31: derive-r3's index, derived
/// on Linux, refused to open on Windows). Never a CI test.
/// </summary>
/// <remarks>
/// OPT-IN AND READ-ONLY. It runs only with <c>LEX_EU_TITLES_OPEN_DIAGNOSTIC</c> naming the index file, opened in place and
/// read-only; nothing is copied or written. It does not hash the articles: the reader's open does that, and the mounted-gates
/// test over the mount is the proof that the whole index opens.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class EuTitlesOpenDiagnostic
{
    private const string IndexVariable = "LEX_EU_TITLES_OPEN_DIAGNOSTIC";

    [TestMethod]
    public void EveryStoredTitleAndReferenceReadsAgainAsItWasStored()
    {
        var path = Environment.GetEnvironmentVariable(IndexVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            Assert.Inconclusive("Set LEX_EU_TITLES_OPEN_DIAGNOSTIC to an EU index /7 file for the read-only re-read of its titles and references.");
        }

        Assert.IsTrue(File.Exists(path), IndexVariable + " names an EU index file.");
        // The lines printed are also written where LEX_EU_TITLES_OPEN_DIAGNOSTIC_OUT asks, since a passing test's console is not shown.
        var lines = new List<string>();
        void Say(string line)
        {
            lines.Add(line);
            Console.WriteLine(line);
        }
        try
        {
            Diagnose(path, Say);
        }
        finally
        {
            if (Environment.GetEnvironmentVariable(IndexVariable + "_OUT") is { Length: > 0 } output)
                File.WriteAllLines(output, lines);
        }
    }

    private static void Diagnose(string path, Action<string> say)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private, Pooling = false,
        }.ToString());
        connection.Open();
        var titles = EuropeIndexReader.ReadTitleRowsOf(connection);
        var references = EuropeIndexReader.ReadReferenceRowsOf(connection);
        var works = new HashSet<string>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT DISTINCT publisher_work_iri FROM states";
            using var reader = command.ExecuteReader();
            while (reader.Read()) works.Add(reader.GetString(0));
        }

        static bool HoldsArticles(string expression) => true;
        var disagreeing = 0;
        foreach (var row in titles)
        {
            if (EuropeIndexBuilder.TitleDisagreement(row, works, HoldsArticles) is not { } field) continue;
            disagreeing++;
            if (disagreeing > 1) continue;
            var text = row.Source == EuropeTitleSources.FormexTitle ? EuFormexPublicationReader.TitleTextOfXml(row.RawValue) : EuropeIndexBuilder.LiteralTitleText(row.RawValue);
            say("FIRST DISAGREEING TITLE");
            say($"  field: {field}");
            say($"  expression: {row.PublisherExpressionIri}");
            say($"  source: {row.Source}, kind: {row.TitleKind}, language: {row.Language}");
            say($"  stored title_text:     {Escaped(row.TitleText)}");
            say($"  recomputed title_text: {Escaped(text)}");
            say($"  stored identity: {row.TitleIdentitySha256}");
            say($"  raw_language_tag: {Escaped(row.RawLanguageTag)}, raw_datatype_iri: {Escaped(row.RawDatatypeIri)}");
            say($"  source_reference: {row.SourceReference}, source_sha256: {row.SourceSha256}");
            say($"  stored raw_value: {Escaped(row.RawValue)}");
            if (row.Source == EuropeTitleSources.FormexTitle)
            {
                var again = ReserialisedOrNull(row.RawValue);
                say($"  raw_value re-serialised here: {Escaped(again)}");
                say($"  same bytes: {string.Equals(again, row.RawValue, StringComparison.Ordinal)}");
            }
        }

        foreach (var row in references)
        {
            if (EuropeIndexBuilder.ReferenceDisagreement(row, HoldsArticles) is not { } field) continue;
            disagreeing++;
            if (disagreeing > 1) continue;
            var read = EuFormexPublicationReader.ReferenceOfXml(row.Kind, row.ReferenceXml, row.PublicationXml);
            say("FIRST DISAGREEING REFERENCE");
            say($"  field: {field}");
            say($"  expression: {row.PublisherExpressionIri}, kind: {row.Kind}, package: {row.PackageSha256} {row.PackageEntry}");
            say($"  stored: {row.NumberForm} {row.Series} {row.OjNumber} {row.ActNumber} {row.Year} {row.FirstPage} {row.PublicationDate}");
            say($"  recomputed: {read?.NumberForm} {read?.Series} {read?.OjNumber} {read?.ActNumber} {read?.Year} {read?.FirstPage} {read?.PublicationDate}");
            say($"  stored reference_xml: {Escaped(row.ReferenceXml)}");
            say($"  reference_xml re-serialised here: {Escaped(ReserialisedOrNull(row.ReferenceXml))}");
            say($"  stored publication_xml: {Escaped(row.PublicationXml)}");
            say($"  publication_xml re-serialised here: {Escaped(row.PublicationXml is null ? null : ReserialisedOrNull(row.PublicationXml))}");
        }

        // The cause of 2026-10-31, shown on whatever this machine is: a stored raw form with a line feed, re-serialised the way
        // XElement.ToString does it (newline handling "replace", the system's line ending) beside the reader's own serialiser.
        if (titles.FirstOrDefault(static row => row.Source == EuropeTitleSources.FormexTitle && row.RawValue.Contains('\n')) is { } lineFed)
        {
            var element = System.Xml.Linq.XElement.Parse(lineFed.RawValue, System.Xml.Linq.LoadOptions.PreserveWhitespace);
            var toString = element.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
            say("FIRST FORMEX TITLE WITH A LINE FEED IN ITS STORED RAW FORM");
            say($"  expression: {lineFed.PublisherExpressionIri}");
            say($"  stored:                                   {Escaped(lineFed.RawValue)}");
            say($"  XElement.ToString(DisableFormatting) here: {Escaped(toString)} (same bytes: {string.Equals(toString, lineFed.RawValue, StringComparison.Ordinal)})");
            say($"  the reader's serialiser here:             {Escaped(EuFormexPublicationReader.Serialise(element))} (same bytes: {string.Equals(EuFormexPublicationReader.Serialise(element), lineFed.RawValue, StringComparison.Ordinal)})");
        }

        say($"titles: {titles.Length}, references: {references.Length}, disagreeing rows: {disagreeing}, " +
            $"titles with a line feed in raw_value: {titles.Count(static row => row.RawValue.Contains('\n'))}, " +
            $"invariant globalization: {Environment.GetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT")}, OS: {Environment.OSVersion}");
        Assert.AreEqual(0, disagreeing, "every stored title and reference reads again as it was stored");
    }

    /// <summary>The raw form parsed and serialised again as the reader does it, or null when it does not parse as one element.</summary>
    private static string? ReserialisedOrNull(string xml)
    {
        try
        {
            return EuFormexPublicationReader.Serialise(System.Xml.Linq.XElement.Parse(xml, System.Xml.Linq.LoadOptions.PreserveWhitespace));
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>A value with its control characters escaped and every character outside ASCII named by its code point.</summary>
    private static string Escaped(string? value)
    {
        if (value is null) return "<null>";
        var builder = new System.Text.StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value == '\r') builder.Append("\\r");
            else if (rune.Value == '\n') builder.Append("\\n");
            else if (rune.Value == '\t') builder.Append("\\t");
            else if (rune.Value < 0x20 || rune.Value > 0x7e) builder.Append($"<U+{rune.Value:X4}>");
            else builder.Append((char)rune.Value);
        }

        return builder.ToString();
    }
}
