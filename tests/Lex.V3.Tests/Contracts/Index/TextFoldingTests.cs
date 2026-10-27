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
    [DataRow("Cong\u00E9 parental")]
    [DataRow("cong\u00E9 parental")]
    [DataRow("conge parental")]
    [DataRow("CONG\u00C9 PARENTAL")]
    [DataRow("conge\u0301 parental")]
    [DataRow("conge\u00A0parental")]
    public void EveryFormOfCongeParentalFoldsAlike(string value)
    {
        Assert.AreEqual("conge parental", TextFolding.Fold(value));
    }

    [TestMethod]
    public void TheLigaturesAreWrittenAsTwoLettersInEitherCase()
    {
        Assert.AreEqual("oeuvre oeuvre aeternam caesar", TextFolding.Fold("\u0153uvre \u0152UVRE \u00C6ternam C\u00E6sar"));
        Assert.AreEqual("ae", TextFolding.Fold("\u01FD"), "a ligature with an accent loses the accent, then is written as two letters");
    }

    [TestMethod]
    public void TheTypographicApostrophesAndTheNoBreakSpacesFoldAsTheirPlainFormsAndTheSoftHyphenIsDropped()
    {
        // The review of #1005 (the driver's decision): one unit for one, or one dropped, so a match's offsets stay exact.
        Assert.AreEqual("l'article l'article l'article l'article", TextFolding.Fold("l\u2019article l\u2018article l\u02BCarticle l'article"));
        Assert.AreEqual("6 septembre 6 septembre 6 km", TextFolding.Fold("6\u00A0septembre 6\u202Fseptembre 6\u2007km"));
        Assert.AreEqual("obligation", TextFolding.Fold("obli\u00ADgation"));
        var text = TextFolding.Folded("une obli\u00ADgation de l\u2019article");
        CollectionAssert.AreEqual(new[] { "obli\u00ADgation", "l\u2019article" },
            new[] { "obligation", "l'article" }.Select(needle => text.Occurrences(needle).Single()).Select(span => text.Original[span.Start..span.End]).ToArray(),
            "a match is marked on the text's own characters, the soft hyphen inside it");
        CollectionAssert.AreEqual(new[] { "obli" }, text.Occurrences("obli").Select(span => text.Original[span.Start..span.End]).ToArray(),
            "a soft hyphen after a match is not taken along, as a combining mark is");
    }

    [TestMethod]
    public void EveryOtherCharacterIsKeptAsItIs()
    {
        // Quotation marks, dashes, hyphens, ordinary and other spaces, punctuation and symbols are not folded: unlike a title's
        // fold, nothing separates words, and a symbol is not decomposed (U+2260 would otherwise lose its stroke and read as "=").
        const string Kept = "l'article \u00AB x \u00BB a\u2013b a-b a\u2011b \u2009x \u00A7 2, \u2260 = \u20AC";
        Assert.AreEqual(Kept, TextFolding.Fold(Kept));
        Assert.AreSame(Kept, TextFolding.Fold(Kept), "a text nothing in which folds is returned as it is");
    }

    [TestMethod]
    public void AMatchIsSaidOnTheTextsOwnCharacters()
    {
        // A decomposed accent is taken with its letter, a ligature whole, and the original's own spelling is what is marked.
        var text = TextFolding.Folded("Le conge\u0301 et l\u2019\u0152uvre, puis CONG\u00C9.");
        CollectionAssert.AreEqual(new[] { "conge\u0301", "CONG\u00C9" },
            text.Occurrences("conge").Select(span => text.Original[span.Start..span.End]).ToArray());
        CollectionAssert.AreEqual(new[] { "\u0152u" }, text.Occurrences("oeu").Select(span => text.Original[span.Start..span.End]).ToArray());
        CollectionAssert.AreEqual(new[] { "\u0152" }, text.Occurrences("e").Where(span => span.Start == text.Original.IndexOf('\u0152')).Select(span => text.Original[span.Start..span.End]).ToArray(),
            "half a ligature marks the whole ligature");
        Assert.IsTrue(text.Contains("l'oeuvre"), "the typographic apostrophe is read as the straight one");
        Assert.IsFalse(text.Contains("l oeuvre"), "and never as a space");
    }

    [TestMethod]
    public void ANeedleThatFoldsToNothingOrToALoneSurrogateMatchesNothing()
    {
        Assert.IsFalse(TextFolding.CanMatch(TextFolding.Fold("\u0301")));
        Assert.IsFalse(TextFolding.CanMatch(TextFolding.Fold(" \u0301 ")));
        Assert.IsFalse(TextFolding.CanMatch(TextFolding.Fold("\u00AD")), "a soft hyphen alone folds to nothing");
        Assert.IsFalse(TextFolding.CanMatch("\uD83D"), "a lone surrogate");
        Assert.IsTrue(TextFolding.CanMatch("\uD83D\uDE00"), "a whole pair");
        var text = TextFolding.Folded("a \uD83D\uDE00 b");
        Assert.HasCount(0, text.Occurrences("\uDE00"));
        Assert.HasCount(1, text.Occurrences("\uD83D\uDE00"));
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
            // U+02BC, the modifier letter apostrophe, is a letter the fold writes as the straight apostrophe (the review of #1005).
            if (char.IsSurrogate(character) || !char.IsLetterOrDigit(character) || code is 0xFFFE or 0x02BC or (>= 0xAC00 and <= 0xD7A3))
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
            return string.Equals("e\u0301".Normalize(NormalizationForm.FormC), "\u00E9", StringComparison.Ordinal);
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }
}
