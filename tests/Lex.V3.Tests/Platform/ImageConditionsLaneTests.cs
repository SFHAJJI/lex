using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Platform;

/// <summary>The image-conditions lane's filter for this test app names tests it has (<see cref="ImageConditionsLane"/>).</summary>
[TestClass]
public sealed class ImageConditionsLaneTests
{
    [TestMethod]
    public void EveryTermOfTheLanesFilterNamesATestOfThisApp()
    {
        var unmatched = ImageConditionsLane.TermsNamingNoTest(
            ImageConditionsLane.Filter("lex_v3_tests"), typeof(ImageConditionsLaneTests).Assembly);
        Assert.IsEmpty(unmatched, string.Join(", ", unmatched));
    }
}
