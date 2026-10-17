using System.Text.Json;
using Lex.V3.Api;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.References;

/// <summary>
/// The fixed table of article numbers (the plan's item 4.2: "1 = 1er; L. 121-6 = L.121-6; 42 is never 42ter"), held to every
/// article id the population's two indexes hold (<c>Fixtures/ArticleNumbers/held-article-ids.json</c>, read read-only from
/// <c>derive-c</c> of run 15, with the AKN fixtures' ids the population does not hold), to its own examples, and to code-shaped
/// ids no index holds yet (codes come with Release 1).
/// </summary>
[TestClass]
public sealed class ArticleNumberTableTests
{
    private static JsonDocument Held() =>
        JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "ArticleNumbers", "held-article-ids.json")));

    private static string[] Opened(string cited, params string[] held) =>
        ArticleNumbers.Opened(ArticleNumbers.FromCitation(cited) ?? throw new AssertFailedException($"{cited} is no number"),
            held, ArticleNumbers.FromLuxembourgId).Ids.ToArray();

    [TestMethod]
    public void EveryArticleIdThePopulationAndTheFixturesHoldIsReadByTheTable()
    {
        using var held = Held();
        var root = held.RootElement;
        var luxembourg = root.GetProperty("luxembourg").GetProperty("ids").EnumerateArray()
            .Select(static entry => entry[0].GetString()!)
            .Concat(root.GetProperty("fixtures").GetProperty("ids").EnumerateArray().Select(static entry => entry[0].GetString()!))
            .ToArray();
        var europe = root.GetProperty("europe").GetProperty("ids").EnumerateArray().Select(static entry => entry[0].GetString()!).ToArray();
        Assert.AreEqual(207, root.GetProperty("luxembourg").GetProperty("ids").GetArrayLength(), "the population's distinct Luxembourg ids");
        Assert.AreEqual(1141, europe.Length, "the population's distinct EU provision ids");

        var unread = luxembourg.Where(static id => ArticleNumbers.FromLuxembourgId(id) is null).ToArray();
        Assert.IsEmpty(unread, "Luxembourg ids the table does not read: " + string.Join(", ", unread));
        var unreadEurope = europe.Where(static id => ArticleNumbers.FromEuropeId(id) is null).ToArray();
        Assert.IsEmpty(unreadEurope, "EU ids the table does not read: " + string.Join(", ", unreadEurope));

        // What the population holds, by reading: the counts are of distinct ids.
        var readings = luxembourg.GroupBy(static id => ArticleNumbers.FromLuxembourgId(id)!.Reading)
            .ToDictionary(static group => group.Key, static group => group.Count());
        Assert.AreEqual(20, readings[ArticleNumberReading.Quoted], "quoted articles of amended acts (art_n)_art_m, art__art_n)");
        Assert.AreEqual(10, readings[ArticleNumberReading.Range], "the fixtures' ranges (art_141_à_144 and the Code de commerce's)");
        Assert.AreEqual(3, readings[ArticleNumberReading.Group], "art_559_560, art_632_et_633, art_636_et_637");

        // Two ids of one key in the population would be two articles a citation could not tell apart: none share one within
        // an expression, which the index's own check holds; across the whole population many do, which is why a citation
        // names its act.
        Assert.AreEqual("1", ArticleNumbers.FromLuxembourgId("art_1er")!.Key);
        Assert.AreEqual("1", ArticleNumbers.FromLuxembourgId("art_1")!.Key);
    }

    [TestMethod]
    public void TheTablesOwnExamplesReadToTheKeysItStates()
    {
        var table = ArticleNumbers.TableDocument;
        var shapes = 0;
        foreach (var shape in table.GetProperty("luxembourg_id_shapes").EnumerateArray())
        {
            shapes++;
            var reading = shape.GetProperty("reading").GetString();
            foreach (var example in shape.GetProperty("examples").EnumerateObject())
            {
                var read = ArticleNumbers.FromLuxembourgId(example.Name);
                Assert.IsNotNull(read, example.Name);
                if (example.Value.ValueKind == JsonValueKind.Null)
                {
                    Assert.AreEqual(ArticleNumberReading.Quoted, read.Reading, example.Name);
                    Assert.AreEqual("quoted", reading, example.Name);
                }
                else
                {
                    Assert.AreEqual(example.Value.GetString(), read.Key, example.Name);
                    Assert.AreEqual(reading, read.Reading.ToString().ToLowerInvariant(), example.Name);
                }
            }
        }

        Assert.AreEqual(11, shapes, "every shape the indexes hold, each with its examples");
        foreach (var example in table.GetProperty("europe_id_shape").GetProperty("examples").EnumerateObject())
        {
            Assert.AreEqual(example.Value.GetString(), ArticleNumbers.FromEuropeId(example.Name)?.Key, example.Name);
        }

        foreach (var example in table.GetProperty("citation_examples").EnumerateObject())
        {
            Assert.AreEqual(example.Value.GetString(), ArticleNumbers.FromCitation(example.Name)?.Key, example.Name);
        }

        foreach (var pair in table.GetProperty("never_equal").EnumerateArray())
        {
            var left = ArticleNumbers.FromCitation(pair[0].GetString()!);
            var right = ArticleNumbers.FromCitation(pair[1].GetString()!);
            Assert.IsNotNull(left, pair[0].GetString());
            Assert.IsNotNull(right, pair[1].GetString());
            Assert.AreNotEqual(left.Key, right.Key, $"{pair[0]} and {pair[1]} are two numbers");
        }
    }

    [TestMethod]
    public void OneIsFirstAndL121SixIsL121SixAndFortyTwoIsNeverFortyTwoTer()
    {
        // 1 = 1er = premier, whichever the act holds.
        foreach (var cited in new[] { "1", "1er", "premier", "Premier", "1ER" })
        {
            CollectionAssert.AreEqual(new[] { "art_1er" }, Opened(cited, "art_1er", "art_2", "art_10", "art_11"), cited);
            CollectionAssert.AreEqual(new[] { "art_1" }, Opened(cited, "art_1", "art_10", "art_1bis"), cited);
            CollectionAssert.AreEqual(new[] { "art1er" }, Opened(cited, "art1er", "art12"), cited);
        }

        // L. 121-6 = L.121-6 = L 121-6, whichever way the code's id writes it; never L. 121-6-1, L. 121, R. 121-6 or 121-6.
        var code = new[] { "art_l_121-5", "art_l_121-6", "art_l_121-6-1", "art_l_121-60", "art_r_121-6", "art_121-6" };
        foreach (var cited in new[] { "L. 121-6", "L.121-6", "L 121-6", "l. 121-6", "L121-6", "L. 121 - 6" })
        {
            CollectionAssert.AreEqual(new[] { "art_l_121-6" }, Opened(cited, code), cited);
        }

        foreach (var written in new[] { "art_L.121-6", "art_L._121-6", "art_l121-6", "art_L_121-6", "art_L121-6" })
        {
            CollectionAssert.AreEqual(new[] { written }, Opened("L. 121-6", written, "art_L.121-7"), written);
        }

        CollectionAssert.AreEqual(new[] { "art_l_121-6-1" }, Opened("L. 121-6-1", code));
        CollectionAssert.AreEqual(new[] { "art_r_121-6" }, Opened("R. 121-6", code));
        CollectionAssert.AreEqual(new[] { "art_121-6" }, Opened("121-6", code));
        Assert.IsEmpty(Opened("L. 121", code));

        // 42 is never 42ter, 42bis, 42-1, 420 or XLII; 42ter is never 42 or 42bis.
        var suffixed = new[] { "art_42ter", "art_42bis", "art_42-1", "art_420", "art_xlii" };
        Assert.IsEmpty(Opened("42", suffixed));
        CollectionAssert.AreEqual(new[] { "art_42" }, Opened("42", ["art_42", .. suffixed]));
        CollectionAssert.AreEqual(new[] { "art_42ter" }, Opened("42ter", ["art_42", .. suffixed]));
        CollectionAssert.AreEqual(new[] { "art_42ter" }, Opened("42 ter", ["art_42", .. suffixed]));
        CollectionAssert.AreEqual(new[] { "art_42bis" }, Opened("42-bis", ["art_42", .. suffixed]));
        CollectionAssert.AreEqual(new[] { "art_42-1" }, Opened("42-1", ["art_42", .. suffixed]));
        CollectionAssert.AreEqual(new[] { "art_xlii" }, Opened("XLII", ["art_42", .. suffixed]));
        Assert.IsEmpty(Opened("3", "art_iii", "art_iv"), "3 is not III");
        CollectionAssert.AreEqual(new[] { "art_ier" }, Opened("Ier", "art_ier", "art_ii"), "Ier is I");
        Assert.IsEmpty(Opened("1er", "art_ier", "art_ii"), "1er is not Ier");
    }

    [TestMethod]
    public void AGroupedArticleOpensOnlyWhenNoArticleHoldsTheNumberItselfAndAQuotedArticleNever()
    {
        // The Code de commerce writes repealed runs as one element.
        CollectionAssert.AreEqual(new[] { "art_141_à_144" }, Opened("142", "art_140", "art_141_à_144", "art_145"));
        CollectionAssert.AreEqual(new[] { "art_141_à_144" }, Opened("141 à 144", "art_140", "art_141_à_144", "art_145"));
        CollectionAssert.AreEqual(new[] { "art_632_et_633" }, Opened("633", "art_631", "art_632_et_633"));
        CollectionAssert.AreEqual(new[] { "art_559_560" }, Opened("559", "art_558", "art_559_560"));
        CollectionAssert.AreEqual(new[] { "art_142" }, Opened("142", "art_141_à_144", "art_142"), "the article itself first");
        Assert.IsEmpty(Opened("146", "art_141_à_144", "art_145"));
        Assert.IsEmpty(Opened("142bis", "art_141_à_144"), "a suffixed number is never inside a range");

        // An amending act's own articles are I, II, III; its points quote the amended act's articles, which no citation opens.
        var amending = new[] { "art_ier", "art_ii", "art_iii", "art_iv", "art_4)_art_13", "art_1)_art_12", "art_2)_art_33-1_1" };
        Assert.IsEmpty(Opened("13", amending));
        Assert.IsEmpty(Opened("33-1", amending));
        CollectionAssert.AreEqual(new[] { "art_ii" }, Opened("II", amending));
        Assert.IsEmpty(Opened("3bis", "art__art_3bis", "art_6"), "an article quoted inside another is not the act's");

        // A dated id and a heading after the number are the number's.
        CollectionAssert.AreEqual(new[] { "art_37-1_20240201" }, Opened("37-1", "art_37", "art_37-1_20240201"));
        CollectionAssert.AreEqual(new[] { "art_2_champ_d'application" }, Opened("2", "art_1er_objet", "art_2_champ_d'application"));
        CollectionAssert.AreEqual(new[] { "art_1er_objet" }, Opened("premier", "art_1er_objet", "art_2_champ_d'application"));

        // Two ids of one number are both opened: the caller lists them, and picks neither.
        CollectionAssert.AreEquivalent(new[] { "art_1", "art_1er" }, Opened("1", "art_1", "art_1er", "art_2"));
    }

    [TestMethod]
    public void AnEuProvisionIdReadsToItsArticleNumberAndItsLettersStayPartOfIt()
    {
        string[] Europe(string cited, params string[] held) =>
            ArticleNumbers.Opened(ArticleNumbers.FromCitation(cited)!, held, ArticleNumbers.FromEuropeId).Ids.ToArray();

        CollectionAssert.AreEqual(new[] { "006" }, Europe("6", "005", "006", "060", "006A"));
        CollectionAssert.AreEqual(new[] { "006A" }, Europe("6a", "005", "006", "060", "006A"));
        CollectionAssert.AreEqual(new[] { "038L" }, Europe("38l", "038", "038L", "038LA"));
        CollectionAssert.AreEqual(new[] { "001" }, Europe("1er", "001", "002"));
        Assert.IsEmpty(Europe("38", "038L", "038A"));
    }

    [TestMethod]
    public void NoTextThatIsNoNumberIsReadAsOne()
    {
        foreach (var text in new[] { "", " ", "de", "la loi", "42ter bis", "1er bis 2", "L. 121-6 du", "12 ans", "5 a)", "foo" })
        {
            Assert.IsNull(ArticleNumbers.FromCitation(text), text);
        }

        foreach (var id in new[] { "", "art_", "paragraph_1", "art_foo", "chp_1", "sec_2" })
        {
            Assert.IsNull(ArticleNumbers.FromLuxembourgId(id), id);
        }

        foreach (var id in new[] { "", "6", "06", "006a", "ART6", "0x6" })
        {
            Assert.IsNull(ArticleNumbers.FromEuropeId(id), id);
        }
    }
}
