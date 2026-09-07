using System.Reflection;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// S1-A07 for <c>Lex.V3.Ingest</c>, which the other test assembly cannot see.
/// </summary>
/// <remarks>
/// The acquisition assembly is the one a V2 reader would most plausibly be reintroduced into, since
/// it is where publisher formats are decoded, so leaving it outside the sweep would have guarded the
/// clause everywhere except the place it matters most.
/// </remarks>
[TestClass]
public sealed class NoV2CompatibilitySurfaceInIngestTests
{
    private static Assembly Ingest => Assembly.Load(new AssemblyName("Lex.V3.Ingest"));

    [TestMethod]
    public void TheIngestAssemblyDeclaresNoV2CompatibilitySurface()
    {
        var offenders = V2CompatibilitySurface.Offenders(Ingest);

        Assert.IsEmpty(
            offenders,
            "Lex.V3.Ingest declares V2 compatibility surface, which S1-A07 forbids entering the "
                + $"V3 line: {string.Join("; ", offenders)}");
    }

    [TestMethod]
    public void TheIngestSweepActuallyYieldedTypes()
    {
        Assert.IsTrue(
            V2CompatibilitySurface.SweptTypeCount(Ingest) > 0,
            "the sweep of Lex.V3.Ingest looked at no types at all, so its clean result states nothing.");
    }
}
