using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests.Census;

/// <summary>
/// Every static token registry in the swept assemblies, with the size of each collection it holds
/// and the name of each string constant. 0 of them when this was written.
/// </summary>
/// <remarks>
/// <para>
/// A vocabulary does not have to be an enum. A static class holding a frozen set of predicate URIs,
/// a schema-id table or a run of wire-token constants is a closed vocabulary with the same failure
/// mode, and until this pin existed most of them had no gate that a new entry would break.
/// </para>
/// <para>
/// Why it is a sweep. The selection is structural: a static class holding at least one static
/// readonly collection or static get-only collection property, or two or more string constants.
/// A registry added tomorrow matches that description without anyone updating a list, so it appears
/// here and fails the pin.
/// </para>
/// <para>
/// What it does not do. It pins each collection's element count, not its elements: pinning the
/// contents would copy a large amount of publisher text into a second place that nobody would think
/// to update, and each registry's own tests already own its contents. So a token added or removed
/// fails this; a token swapped for another of the same kind does not, and the registry's own test
/// is the control for that. A member whose static initializer throws is reported as
/// <c>unreadable</c> rather than dropped, because a member that quietly leaves a sweep is the
/// failure this file exists to prevent.
/// </para>
/// <para>
/// This assembly holds no static token registry at all, so the pin is an empty array.
/// An empty baseline that can only ever be compared with itself would pass forever, so
/// say what makes this one real: the sweep is over the assembly, not over a list, and the
/// first registry added to it becomes an element the empty expectation does not have.
/// </para>
/// <para>
/// When a real change makes this fail, that is the pin working rather than a defect in it, and the
/// fix is not to hand edit the array until it matches. Re-derive it: print the sweep's own output
/// from a throwaway test that writes it somewhere under <c>Path.GetTempPath()</c>, read the diff,
/// and transcribe the printed lines. Never rebuild the expected side from the sweep inside the
/// test. It would then agree with whatever the code happens to say, which is the one thing a pin
/// must not do, and it is how a large array quietly stops being evidence of anything.
/// </para>
/// </remarks>
[TestClass]
public sealed class VocabularyRegistryCensusTests
{
    [TestMethod]
    public void EveryStaticTokenRegistryInTheSweptAssembliesIsPinnedAtItsCurrentSize()
    {
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ClosedSurfaceCensus.VocabularyRegistries(CensusScope.SweptHere).ToArray());
    }
}
