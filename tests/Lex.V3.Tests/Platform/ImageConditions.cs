using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Platform;

/// <summary>
/// Whether this test process runs the way the release image runs the API (plan 0.8): .NET globalization-invariant,
/// with no language library (ICU), and with no zone database but UTC. CI's <c>image-conditions</c> lane runs these
/// tests inside the image's own base, where both hold; <c>eng/test-fast.ps1 -ImageConditions</c> reproduces them on any
/// machine with the two switches the lane's canary, <c>ImageConditionsTests</c>, holds to them.
/// </summary>
internal static class ImageConditions
{
    /// <summary>The variable the image-conditions lane sets, so its canary asserts the conditions rather than skipping.</summary>
    internal const string LaneVariable = "LEX_IMAGE_CONDITIONS";

    /// <summary>True when .NET has a language library here: "é" decomposes into two characters only with one.</summary>
    internal static bool LanguageLibraryPresent => "\u00E9".Normalize(NormalizationForm.FormD).Length == 2;

    /// <summary>
    /// Ends a test inconclusive where there is no language library, for a test whose premise needs one: a named culture
    /// (tr-TR, fr-FR, ar-SA) to switch to, or Unicode normalization. Neither exists in the image, so the premise does not.
    /// </summary>
    internal static void RequireLanguageLibrary(string premise)
    {
        if (!LanguageLibraryPresent)
        {
            Assert.Inconclusive(
                $"Not measured here: {premise}, which needs the operating system's language library; this process runs " +
                "without one, as the release image does.");
        }
    }

    /// <summary>Europe/Luxembourg from this host's zone database, or null where the host has none.</summary>
    internal static TimeZoneInfo? LuxembourgZoneOrNull()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Luxembourg");
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
    }
}
