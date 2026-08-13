using System.Security.Cryptography;
using System.Text;
using Lex.Derive;
using Xunit;

namespace Lex.Tests;

/// <summary>
/// Which Memorial PDF profile gets the document. The fallback to the second profile used to fire
/// only when the first found zero provisions, which asks about structure when the question is
/// wording: the 2003 consolidation of the financial-sector law produced 145 provisions with text
/// for 40, passed the zero check, and published 105 empty-string hashes. The second profile never
/// saw the document.
/// </summary>
public sealed class MemorialFallbackTests
{
    private static Extraction WithCounts(int provisions, int withText)
    {
        var rows = Enumerable.Range(0, provisions).Select(i =>
        {
            var text = i < withText ? $"Wording of article {i}." : "";
            return new Provision($"art_{i}", null, "article", $"Art. {i}.", null, [], null,
                text, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
                0, 0, []);
        }).ToList();
        return new Extraction(rows, "", []);
    }

    // The exact shape that motivated the change, pinned by its real numbers.
    [Fact]
    public void The_financial_sector_law_shape_now_reaches_the_second_profile()
    {
        Assert.True(DeriveWriter.RecoveredLittleText(WithCounts(145, 40)));
    }

    [Fact]
    public void A_document_with_no_provisions_still_falls_back()
    {
        Assert.True(DeriveWriter.RecoveredLittleText(WithCounts(0, 0)));
    }

    // A mostly-recovered document stays with the first profile: the fallback exists for failed
    // extractions, not for swapping profiles over a scattered gap.
    [Fact]
    public void A_document_whose_text_mostly_extracted_keeps_the_first_profile()
    {
        Assert.False(DeriveWriter.RecoveredLittleText(WithCounts(145, 120)));
    }

    // The second profile has to earn the document by recovering strictly more wording. If it does
    // worse or only equals the first, the first stands: switching profiles re-keys every
    // text_sha, and that must never happen without a text gain to show for it.
    [Fact]
    public void The_second_profile_wins_only_by_recovering_more_wording()
    {
        Assert.True(DeriveWriter.RecoversMoreText(WithCounts(145, 130), WithCounts(145, 40)));
        Assert.False(DeriveWriter.RecoversMoreText(WithCounts(145, 40), WithCounts(145, 40)));
        Assert.False(DeriveWriter.RecoversMoreText(WithCounts(12, 8), WithCounts(145, 40)));
    }

    // The case the original zero-provision fallback existed for: first profile finds nothing at
    // all, second finds structure but the document genuinely holds no extractable wording. The
    // second still wins, because structure with honest gaps beats nothing.
    [Fact]
    public void Equal_wording_still_switches_when_the_first_found_no_structure()
    {
        Assert.True(DeriveWriter.RecoversMoreText(WithCounts(30, 0), WithCounts(0, 0)));
    }
}
