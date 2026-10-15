using Lex.V3.TestSupport;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The image-conditions lane's filter for this test app, its request-time classes, names tests it has
/// (<see cref="ImageConditionsLane"/>): an include term that matched no test once left the image-only annex control case
/// out of the lane, and two exclusions named no class.
/// </summary>
[TestClass]
public sealed class ImageConditionsLaneTests
{
    [TestMethod]
    public void EveryTermOfTheLanesFilterNamesATestOfThisApp()
    {
        var unmatched = ImageConditionsLane.TermsNamingNoTest(
            ImageConditionsLane.Filter("ingest_request_time"), typeof(ImageConditionsLaneTests).Assembly);
        Assert.IsEmpty(unmatched, string.Join(", ", unmatched));
    }
}
