using System.Globalization;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Platform;

/// <summary>
/// The image-conditions lane's canary (plan 0.8): with <c>LEX_IMAGE_CONDITIONS=1</c>, each condition the release image
/// runs the API under must hold in this process, so the lane cannot drift away from the image and still pass. The image
/// (<c>aspnet:10.0-noble-chiseled</c>) carries no language library and no zone database: its own environment sets .NET
/// globalization-invariant, and without zone files .NET knows UTC alone, silently. Elsewhere these are inconclusive.
/// </summary>
[TestClass]
public sealed class ImageConditionsTests
{
    private static void RequireLane()
    {
        if (Environment.GetEnvironmentVariable(ImageConditions.LaneVariable) != "1")
        {
            Assert.Inconclusive($"{ImageConditions.LaneVariable} is not 1: this process is not the image-conditions lane.");
        }
    }

    [TestMethod]
    public void NoNamedCultureExists()
    {
        RequireLane();
        Assert.ThrowsExactly<CultureNotFoundException>(() => CultureInfo.GetCultureInfo("fr-FR"));
    }

    [TestMethod]
    public void UnicodeNormalizationReturnsItsInputUnchanged()
    {
        RequireLane();
        Assert.AreEqual("Cong\u00E9", "Cong\u00E9".Normalize(NormalizationForm.FormD));
    }

    [TestMethod]
    public void NoLuxembourgZoneExists()
    {
        RequireLane();
        Assert.ThrowsExactly<TimeZoneNotFoundException>(() => TimeZoneInfo.FindSystemTimeZoneById("Europe/Luxembourg"));
    }

    [TestMethod]
    public void TheLocalClockIsUtcInSummer()
    {
        RequireLane();
        Assert.AreEqual(TimeSpan.Zero, TimeZoneInfo.Local.GetUtcOffset(new DateTimeOffset(2026, 7, 14, 22, 30, 0, TimeSpan.Zero)));
    }

    [TestMethod]
    public void UtcIsTheOnlyZone()
    {
        RequireLane();
        Assert.AreEqual(1, TimeZoneInfo.GetSystemTimeZones().Count);
    }
}
