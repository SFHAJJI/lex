using System.Globalization;
using System.Text;
using Lex.V3.Contracts.Index;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Index;

/// <summary>
/// Lex's one folding of searched text (<see cref="TextFolding"/>, the plan's item 4.4): capitals and accents never decide a
/// match, every other character does, and a match found in the fold is said on the text's own characters. Every test but the
/// last holds in any process, with the operating system's language library or without it; the last holds the fold of every
/// letter to the library's canonical decomposition wherever there is one.
/// </summary>
[TestClass]
public sealed class TextFoldingTests
{
    [TestMethod]
    [DataRow("Congé parental")]
    [DataRow("congé parental")]
    [DataRow("conge parental")]
    [DataRow("CONGÉ PARENTAL")]
    [DataRow("congé parental")]
    public void EveryFormOfCongeParentalFoldsAlike(string value)
    {
        Assert.AreEqual("conge parental", TextFolding.Fold(value));
    }

    [TestMethod]
    public void TheLigaturesAreWrittenAsTwoLettersInEitherCase()
    {
        Assert.AreEqual("oeuvre oeuvre aeternam caesar", TextFolding.Fold("œuvre ŒUVRE Æternam Cæsar"));
        Assert.AreEqual("ae", TextFolding.Fold("ǽ"), "a ligature with an accent loses the accent, then is written as two letters");
    }

    [TestMethod]
    public void EveryOtherCharacterIsKeptAsItIs()
    {
        // Apostrophes, quotation marks, dashes, no-break spaces, punctuation and symbols are not folded: unlike a title's fold,
        // nothing separates words, and a symbol is not decomposed (U+2260 would otherwise lose its stroke and read as "=").
        const string Kept = "l’article l'article « x » a–b 6 km § 2, ≠ = €";
        Assert.AreEqual(Kept, TextFolding.Fold(Kept));
        Assert.AreSame(Kept, TextFolding.Fold(Kept), "a text nothing in which folds is returned as it is");
    }

    [TestMethod]
    public void AMatchIsSaidOnTheTextsOwnCharacters()
    {
        // A decomposed accent is taken with its letter, a ligature whole, and the original's own spelling is what is marked.
        var text = TextFolding.Folded("Le congé et l’Œuvre, puis CONGÉ.");
        CollectionAssert.AreEqual(new[] { "congé", "CONGÉ" },
            text.Occurrences("conge").Select(span => text.Original[span.Start..span.End]).ToArray());
        CollectionAssert.AreEqual(new[] { "Œu" }, text.Occurrences("oeu").Select(span => text.Original[span.Start..span.End]).ToArray());
        CollectionAssert.AreEqual(new[] { "Œ" }, text.Occurrences("e").Where(span => span.Start == text.Original.IndexOf('Œ')).Select(span => text.Original[span.Start..span.End]).ToArray(),
            "half a ligature marks the whole ligature");
        Assert.IsTrue(text.Contains("l’oeuvre"));
        Assert.IsFalse(text.Contains("l'oeuvre"), "the typographic apostrophe is not the ASCII one");
    }

    [TestMethod]
    public void ANeedleThatFoldsToNothingOrToALoneSurrogateMatchesNothing()
    {
        Assert.IsFalse(TextFolding.CanMatch(TextFolding.Fold("́")));
        Assert.IsFalse(TextFolding.CanMatch(TextFolding.Fold(" ́ ")));
        Assert.IsFalse(TextFolding.CanMatch("\uD83D"), "a lone surrogate");
        Assert.IsTrue(TextFolding.CanMatch("😀"), "a whole pair");
        var text = TextFolding.Folded("a 😀 b");
        Assert.HasCount(0, text.Occurrences("\uDE00"));
        Assert.HasCount(1, text.Occurrences("😀"));
    }

    /// <summary>
    /// Every letter or digit of the Basic Multilingual Plane but the Hangul syllables folds to its canonical decomposition without
    /// its non-spacing marks, lowered, with œ and æ written as two letters, as the operating system's library makes it; run only
    /// where there is one.
    /// </summary>
    [TestMethod]
    public void EveryLetterFoldsAsTheLibrarysCanonicalDecompositionLowered()
    {
        if (!LibraryHolds())
        {
            Assert.Inconclusive("This process runs without the operating system's language library.");
        }

        var differences = new List<string>();
        for (var code = 0; code <= char.MaxValue; code++)
        {
            var character = (char)code;
            // A Hangul syllable keeps its composed form, as the title folding's table, which this fold reads, keeps it (the
            // library composed it again): its decomposition is its jamo, which no accent is among.
            if (char.IsSurrogate(character) || !char.IsLetterOrDigit(character) || code is 0xFFFE or (>= 0xAC00 and <= 0xD7A3))
            {
                continue;
            }

            string decomposed;
            try
            {
                decomposed = character.ToString().Normalize(NormalizationForm.FormD);
            }
            catch (ArgumentException)
            {
                continue;
            }

            var expected = new StringBuilder();
            foreach (var part in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(part) != UnicodeCategory.NonSpacingMark)
                {
                    expected.Append(char.ToLowerInvariant(part));
                }
            }

            var library = expected.ToString()
                .Replace("œ", "oe", StringComparison.Ordinal).Replace("æ", "ae", StringComparison.Ordinal);
            var folded = TextFolding.Fold(character.ToString());
            if (!string.Equals(library, folded, StringComparison.Ordinal))
            {
                differences.Add($"U+{code:X4}: {folded} where the library gives {library}");
            }
        }

        Assert.IsEmpty(differences, string.Join(Environment.NewLine, differences.Take(40)));
    }

    private static bool LibraryHolds()
    {
        try
        {
            return string.Equals("é".Normalize(NormalizationForm.FormC), "é", StringComparison.Ordinal);
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }
}
