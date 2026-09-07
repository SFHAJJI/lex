using System.Reflection;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

/// <summary>
/// S1-A07: no V2 answer or refusal envelope reader, serializer, shim, alias or fixture enters the
/// V3 line.
/// </summary>
/// <remarks>
/// <para>
/// This clause had no executable evidence. Stage 1 closes only when "every clause above have
/// openable evidence", and A07 was held by doc comments alone — the KEEP/IMPROVE/REFUSE provenance
/// notes and boundary statements such as "V3 has no V2 index reader, must never grow one". The rule
/// was true and unenforced, which is the state in which a prohibition is eventually broken by
/// someone who never read the comment.
/// </para>
/// <para>
/// The sweep is proved to detect rather than assumed to. <see cref="TheSweepFindsARealV2NamedTypeInARealAssembly"/>
/// runs the identical instrument over this test assembly, which declares a deliberately named
/// probe, and requires it to be found — because an empty result is also what a broken sweep
/// returns.
/// </para>
/// </remarks>
[TestClass]
public sealed class NoV2CompatibilitySurfaceTests
{
    /// <summary>
    /// The production assemblies this test project can see, pinned as a literal list of names and
    /// loaded by name rather than through an anchor type. A missing entry must be a deliberate
    /// diff: silently sweeping five assemblies instead of six is exactly how a prohibition stops
    /// being checked without any test going red.
    /// </summary>
    private static readonly string[] SweptProductionAssemblyNames =
    [
        "Lex.V3.Api",
        "Lex.V3.Artifacts",
        "Lex.V3.Contracts",
        "Lex.V3.Custody.Azure",
        "Lex.V3.Custody.Probe",
        "Lex.V3.Preview",
    ];

    [TestMethod]
    public void NoProductionAssemblyDeclaresV2CompatibilitySurface()
    {
        foreach (var name in SweptProductionAssemblyNames)
        {
            var offenders = V2CompatibilitySurface.Offenders(Load(name));
            Assert.IsEmpty(
                offenders,
                $"{name} declares V2 compatibility surface, which S1-A07 forbids entering the V3 "
                    + $"line: {string.Join("; ", offenders)}");
        }
    }

    [TestMethod]
    public void EverySweptAssemblyLoadsAndActuallyYieldedTypes()
    {
        // A sweep that finds no types passes vacuously. This keeps "no offenders" from meaning
        // "no look", and fails loudly if an assembly stops being copied beside the tests.
        foreach (var name in SweptProductionAssemblyNames)
        {
            Assert.IsTrue(
                V2CompatibilitySurface.SweptTypeCount(Load(name)) > 0,
                $"the sweep of {name} looked at no types at all, so its clean result states nothing.");
        }
    }

    [TestMethod]
    public void TheSweptAssemblyNamesAreDistinct()
    {
        // A duplicated entry would read as six assemblies while covering five.
        Assert.AreEqual(
            SweptProductionAssemblyNames.Length,
            SweptProductionAssemblyNames.Distinct(StringComparer.Ordinal).Count(),
            "the pinned production assembly list must name each assembly once.");
    }

    [TestMethod]
    public void TheSweepFindsARealV2NamedTypeInARealAssembly()
    {
        // The positive control on the SWEEP, not merely on the predicate.
        var offenders = V2CompatibilitySurface.Offenders(typeof(V2AnswerEnvelopeReaderProbe).Assembly);

        Assert.IsTrue(
            offenders.Contains(typeof(V2AnswerEnvelopeReaderProbe).FullName!, StringComparer.Ordinal),
            "the sweep must find a V2-named type that really exists in an assembly it is given, or "
                + "its empty result over the production assemblies would prove nothing.");
    }

    [TestMethod]
    public void TheSweepFindsAV2NamedMemberOnAnOrdinarilyNamedType()
    {
        // A shim need not be a type, so the member limb is pinned separately.
        var offenders = V2CompatibilitySurface.Offenders(typeof(OrdinarilyNamedHost).Assembly);

        Assert.IsTrue(
            offenders.Contains(
                $"{typeof(OrdinarilyNamedHost).FullName}.{nameof(OrdinarilyNamedHost.ReadV2Envelope)}",
                StringComparer.Ordinal),
            "a V2-named member on an innocently named type must be found.");
    }

    [TestMethod]
    public void ThePredicateFlagsEverySpellingTheClauseNames()
    {
        foreach (var name in new[]
        {
            "V2AnswerEnvelopeReader", "V2RefusalEnvelopeSerializer", "LexV2Shim",
            "V2Alias", "V2Fixture", "V2CompatibilityFixtures", "v2reader", "ReadV2",
        })
        {
            Assert.IsTrue(
                V2CompatibilitySurface.NamesV2CompatibilitySurface(name),
                $"{name} spells V2 surface and must be flagged.");
        }
    }

    [TestMethod]
    public void ThePredicateLeavesOrdinaryV3NamesAlone()
    {
        foreach (var name in new string?[]
        {
            "Lex.V3.Contracts", "CorpusRecord", "EuActForm", "CutReleaseGate",
            "AzureBlobCustodyStore", "Level2Cache", "Sha256Digest", null,
        })
        {
            Assert.IsFalse(
                V2CompatibilitySurface.NamesV2CompatibilitySurface(name),
                $"{name ?? "<null>"} is not V2 surface and must not be flagged.");
        }
    }

    [TestMethod]
    public void ThePermittedNamesListIsEmpty()
    {
        // An eradication rule with a populated exception list is a different rule. If this ever
        // needs an entry, that is a review conversation rather than a test edit.
        Assert.IsEmpty(
            V2CompatibilitySurface.PermittedNames,
            "S1-A07 admits no V2 surface, so the permitted list must stay empty.");
    }

    private static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));

    /// <summary>Exists only so the sweep has a real type to find in a real assembly.</summary>
    private sealed class V2AnswerEnvelopeReaderProbe;

    /// <summary>Exists only so the member limb of the sweep has a real member to find.</summary>
    private sealed class OrdinarilyNamedHost
    {
        public static void ReadV2Envelope()
        {
        }
    }
}
