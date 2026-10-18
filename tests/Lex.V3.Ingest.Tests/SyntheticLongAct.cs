using System.Globalization;
using System.Text;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// A long Luxembourg act for the reading's pages and its speed checks (the plan's item 3.7): the fixture act's document
/// (<c>loi-1991-08-10-n3--2024-02-01--fr.bin</c>) with its body replaced by <c>count</c> articles of synthetic text, built at
/// test time and never written to the repository (which is public: no publisher bytes but the fixture's own are committed,
/// and none are added). It is read through the same derivation as the fixture, so the mount it makes is a real mount of a
/// long act: the stand-in for the codes, which the population mount does not hold.
/// </summary>
/// <remarks>
/// Each article is <c>art_{n}</c> (<c>art_1er</c> for the first), titled and dated as the fixture's articles are, with one
/// paragraph of synthetic French of about <c>textBytes</c> bytes (the fixture's articles average 1,300), which names its
/// number so that no two articles share a text. The fixture's head (its identification, its notes, its preface) is kept.
/// </remarks>
internal static class SyntheticLongAct
{
    private const string Eli = "http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3";

    /// <summary>The words the synthetic text is made of: plain French, no publisher's sentence.</summary>
    private static readonly string[] Words =
    [
        "le", "texte", "synthétique", "de", "cet", "article", "sert", "à", "mesurer", "une", "lecture", "longue", "et", "ne",
        "dit", "rien", "du", "droit", "applicable", "chaque", "phrase", "est", "faite", "pour", "le", "test", "seulement",
    ];

    /// <summary>
    /// The fixture's document with <paramref name="count"/> synthetic articles in place of its body; <paramref name="lean"/>
    /// leaves out each article's publisher metadata (its own title and date), so that 3,000 articles fit the body the test
    /// acquisition takes (1.46 MB was taken and 1.86 MB refused, measured).
    /// </summary>
    internal static async Task<byte[]> DocumentAsync(int count, int textBytes = 1200, bool lean = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var fixture = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuAknLegalContent", "loi-1991-08-10-n3--2024-02-01--fr.bin")));
        var bodyAt = fixture.IndexOf("<body>", StringComparison.Ordinal);
        var bodyEnd = fixture.IndexOf("</body>", StringComparison.Ordinal);
        if (bodyAt < 0 || bodyEnd < bodyAt)
        {
            throw new InvalidDataException("The fixture document has no body to replace.");
        }

        var document = new StringBuilder(fixture.Length + (count * (textBytes + 900)));
        document.Append(fixture, 0, bodyAt).Append("<body>\n<chapter id=\"chapitre_i\">\n<num>Chapitre I. </num>\n<heading> Chapitre synthétique</heading>\n");
        for (var number = 1; number <= count; number++)
        {
            var id = number == 1 ? "art_1er" : "art_" + number.ToString(CultureInfo.InvariantCulture);
            var label = number == 1 ? "Art. 1<sup>er</sup>." : $"Art. {number.ToString(CultureInfo.InvariantCulture)}.";
            document.Append("<article id=\"").Append(id).Append("\">\n");
            if (lean)
            {
                document.Append("<num>").Append(label).Append("</num>\n<alinea>\n<content>\n<p>").Append(Text(number, textBytes))
                    .Append("</p>\n</content>\n</alinea>\n</article>\n");
                continue;
            }

            document.Append("<scl:JOLUXWork>\n<scl:JOLUXComplexWork>\n")
                .Append("<scl:jolux scl:name=\"uriThis\">").Append(Eli).Append('/').Append(id).Append("</scl:jolux>\n</scl:JOLUXComplexWork>\n<scl:JOLUXLegalResource>\n")
                .Append("<scl:jolux scl:name=\"uriThis\">").Append(Eli).Append('/').Append(id).Append("/19910830</scl:jolux>\n")
                .Append("<scl:jolux scl:name=\"isMemberOf\">").Append(Eli).Append("</scl:jolux>\n")
                .Append("<scl:jolux scl:lg=\"fra\" scl:name=\"title\">Art. ").Append(number == 1 ? "1er" : number.ToString(CultureInfo.InvariantCulture)).Append(".</scl:jolux>\n")
                .Append("<scl:jolux scl:name=\"dateApplicability\">2021-08-22</scl:jolux>\n")
                .Append("<scl:rdf scl:name=\"type\">http://data.legilux.public.lu/resource/ontology/jolux#Article</scl:rdf>\n")
                .Append("</scl:JOLUXLegalResource>\n</scl:JOLUXWork>\n<num>").Append(label).Append("</num>\n<alinea>\n<content>\n<p>")
                .Append(Text(number, textBytes)).Append("</p>\n</content>\n</alinea>\n</article>\n");
        }

        document.Append("</chapter>\n").Append(fixture, bodyEnd, fixture.Length - bodyEnd);
        return Encoding.UTF8.GetBytes(document.ToString());
    }

    /// <summary>About <paramref name="bytes"/> bytes of synthetic French naming article <paramref name="number"/>.</summary>
    private static string Text(int number, int bytes)
    {
        var text = new StringBuilder($"Article synthétique numéro {number.ToString(CultureInfo.InvariantCulture)} :");
        for (var word = 0; Encoding.UTF8.GetByteCount(text.ToString()) < bytes; word++)
        {
            text.Append(' ').Append(Words[(word + number) % Words.Length]);
            if (word % 17 == 16)
            {
                text.Append('.');
            }
        }

        return text.Append('.').ToString();
    }
}
