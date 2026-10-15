using System.Globalization;
using System.Text;
using Lex.V3.Contracts.Index;
using Lex.V3.Tests.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Index;

/// <summary>
/// Lex's own title folding (<see cref="TitleFolding"/>, plan 0.8). Every test but the last holds in any process, with
/// the operating system's language library or without it, as the image-conditions lane runs them; the last holds the
/// table to the library wherever there is one.
/// </summary>
[TestClass]
public sealed class TitleFoldingTests
{
    // The real bounded first mount's one title (tests/Lex.V3.Ingest.Tests/Fixtures/LegacySchema6Mount), as the publisher
    // wrote it and as its index stores it, folded on a machine with the library. The literal "\n" is the publisher's.
    private const string LegacyTitle =
        "Loi du 14 mars 2017 portant modification de la loi modifi\u00E9e du 7 octobre 1993 ayant pour objet\\n1. la " +
        "cr\u00E9ation d\u2019un Service de Coordination de la Recherche et de l\u2019Innovation p\u00E9dagogiques et " +
        "technologiques ;\\n2. la cr\u00E9ation d\u2019un \u00AB\u00A0Centre de Gestion Informatique de " +
        "l\u2019\u00C9ducation\u00A0\u00BB ;\\n3. l\u2019institution d\u2019un Conseil scientifique.";

    private const string LegacyTitleStored =
        "loi du 14 mars 2017 portant modification de la loi modifiee du 7 octobre 1993 ayant pour objet n1 la creation d " +
        "un service de coordination de la recherche et de l innovation pedagogiques et technologiques n2 la creation d un " +
        "centre de gestion informatique de l education n3 l institution d un conseil scientifique";

    [TestMethod]
    public void CongeParentalFoldsToTheFormTheIndexesHold()
    {
        Assert.AreEqual("conge parental", TitleFolding.NormalizeTitle("Cong\u00E9 parental"));
    }

    [TestMethod]
    public void AnAccentedCapitalFoldsToItsPlainSmallLetter()
    {
        Assert.AreEqual("conge parental", TitleFolding.NormalizeTitle("CONG\u00C9 PARENTAL"));
    }

    [TestMethod]
    public void ADecomposedAccentFoldsAsItsComposedLetterDoes()
    {
        Assert.AreEqual("conge parental", TitleFolding.NormalizeTitle("conge\u0301 parental"));
    }

    [TestMethod]
    public void ALetterTheLibraryDidNotDecomposeKeepsItsLetter()
    {
        Assert.AreEqual(
            "\u0153uvre \u00E6ther stra\u00DFe \u00F8re \u0142odz \u0111",
            TitleFolding.NormalizeTitle("\u0152uvre \u00C6ther Stra\u00DFe \u00D8re \u0141\u00F3d\u017A \u0110"));
    }

    [TestMethod]
    public void QuotesApostrophesAndNoBreakSpacesSeparateWords()
    {
        Assert.AreEqual(
            "l education centre n 3",
            TitleFolding.NormalizeTitle(" l\u2019\u00C9ducation\u00A0: \u00AB\u00A0Centre\u00A0\u00BB n\u00B0\u00A03 "));
    }

    [TestMethod]
    public void ALoneSurrogateAndUFFFESeparateWords()
    {
        Assert.AreEqual("a b c", TitleFolding.NormalizeTitle("a\uD800b\uFFFEc"));
    }

    [TestMethod]
    public void TheFirstMountsTitleFoldsToWhatItsIndexStores()
    {
        Assert.AreEqual(LegacyTitleStored, TitleFolding.NormalizeTitle(LegacyTitle));
    }

    /// <summary>
    /// The table against the library it replaces: every character of the Basic Multilingual Plane, between two letters,
    /// folds as the library's fold (the code the indexes were built with, below) folds it, but where the library threw
    /// (U+FFFE), which separates words here. A difference names the entry the table needs.
    /// </summary>
    [TestMethod]
    public void EveryCharacterOfThePlaneFoldsAsTheLanguageLibraryFoldedIt()
    {
        ImageConditions.RequireLanguageLibrary("the library's own fold, to compare the table with");
        var differing = new List<string>();
        for (var code = 0; code <= 0xFFFF; code++)
        {
            if (code is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            var text = "a" + (char)code + "b";
            string expected;
            try
            {
                expected = LibraryFold(text);
            }
            catch (ArgumentException)
            {
                expected = "a b";
            }

            var actual = TitleFolding.NormalizeTitle(text);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                var fold = expected.Length == 3 ? expected[1] : ' ';
                differing.Add($"U+{code:X4} folds to '{actual}', the library to '{expected}': entry {code:X4}>{(int)fold:X4}");
            }
        }

        Assert.IsEmpty(differing, $"{differing.Count} characters: {string.Join("; ", differing.Take(40))}");
    }

    // The fold every index was built with until plan 0.8 (LuxembourgIndexBuilder.NormalizeTitle at e5d0bd88), which asks
    // the operating system's language library.
    private static string LibraryFold(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length != 0)
                {
                    builder.Append(' ');
                }
                builder.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
