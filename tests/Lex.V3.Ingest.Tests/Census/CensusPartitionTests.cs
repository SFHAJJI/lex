using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests.Census;

/// <summary>
/// The partition: every type the census had to account for is in one of the three pins or in the
/// declined list below, with a reason. 56 candidates when this was written, 56
/// pinned and 0 declined.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists. The three pins are exact about what they hold and say nothing about what they
/// leave out. The residual used to live in a commit message, which meant a static class holding
/// state that is not a token registry moved nothing at all: it was neither pinned nor declined,
/// and the sentence recording that stayed true only until the next such class was written. A
/// residual stated in prose is not a residual, it is a claim about one afternoon.
/// </para>
/// <para>
/// How it is enforced. <see cref="ClosedSurfaceCensus.Candidates"/> sweeps every type that is a
/// closed vocabulary, a construction-restricted type, or a static class holding any state, which is
/// wider than any of the three pins on purpose. The assertion is that this set equals the types the
/// three pins hold plus the declined names below. A type in none of the four fails. The declined
/// list is a literal and its reasons are prose, but its membership is not: a class that stops
/// matching its reason has to be moved or the test goes red.
/// </para>
/// <para>
/// The declined reasons, and why each was declined rather than pinned. A stateful static that is
/// not a token registry is a helper holding a cached serializer or one schema id, and pinning
/// helpers would drown the registry pin in rows nobody reads. A constant table is a set of numeric
/// limits, which is not a vocabulary: its values already have their own tests, and widening the
/// registry rule to swallow it would raise a coverage number without closing a defect. Neither
/// decision is enforced by anything except this list, which is why the list is here rather than in
/// a report.
/// </para>
/// </remarks>
[TestClass]
public sealed class CensusPartitionTests
{
    /// <summary>
    /// Types the census sweeps and has decided not to pin, as <c>full name: reason</c>. Membership
    /// is enforced by <see cref="EveryCensusCandidateIsPinnedOrDeclinedWithAReason"/>; the reasons
    /// are the part a person has to keep true.
    /// </summary>
    private static readonly string[] Declined = [];

    [TestMethod]
    public void EveryCensusCandidateIsPinnedOrDeclinedWithAReason()
    {
        var pinned = ClosedSurfaceCensus.ClosedVocabularies(CensusScope.SweptHere)
            .Concat(ClosedSurfaceCensus.GuardedConstruction(CensusScope.SweptHere))
            .Concat(ClosedSurfaceCensus.VocabularyRegistries(CensusScope.SweptHere))
            .Select(NameOf);

        CollectionAssert.AreEqual(
            ClosedSurfaceCensus.Candidates(CensusScope.SweptHere).ToArray(),
            pinned
                .Concat(Declined.Select(NameOf))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray(),
            "a swept type is in no pin and on no declined list, so nothing records what it is");
    }

    [TestMethod]
    public void ThePartitionTotalsAreExactlyThese()
    {
        Assert.AreEqual(
            58, ClosedSurfaceCensus.Candidates(CensusScope.SweptHere).Count, "candidates");
        Assert.AreEqual(
            24, ClosedSurfaceCensus.ClosedVocabularies(CensusScope.SweptHere).Count, "vocabularies");
        Assert.AreEqual(
            34, ClosedSurfaceCensus.GuardedConstruction(CensusScope.SweptHere).Count, "guarded types");
        Assert.AreEqual(
            0, ClosedSurfaceCensus.VocabularyRegistries(CensusScope.SweptHere).Count, "registries");
        Assert.AreEqual(0, Declined.Length, "declined");
    }

    private static string NameOf(string row) =>
        row[..row.IndexOf(':', StringComparison.Ordinal)];
}
