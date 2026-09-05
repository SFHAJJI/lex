using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Census;

/// <summary>
/// No vocabulary in the swept assemblies declares a wire token on some members and not others.
/// </summary>
/// <remarks>
/// <para>
/// A half declared vocabulary is a surface a reader cannot judge. Some names are contract and the
/// rest fall through to default serialization, which in this codebase is the exact CLR member name,
/// and nothing marks which is which. R4 found two such enums because a lane happened to look at one
/// of them, and fixing the two would have left the class open.
/// </para>
/// <para>
/// THIS IS A LOCK RATHER THAN A CLEANUP. When it was written the sweep saw 243 enums, 160 fully
/// declared, 81 declaring nothing at all, and exactly 2 half declared, which were the two R4 fixed.
/// So it lands green and costs nothing, and its whole value is the next one it refuses.
/// </para>
/// <para>
/// The 81 that declare nothing are correctly outside the rule. Most enums here are internal state
/// that never reaches a wire; the criterion is that a vocabulary which has BEGUN declaring tokens
/// has to finish, because that is the point at which a reader starts inferring meaning from their
/// presence.
/// </para>
/// </remarks>
[TestClass]
public sealed class HalfDeclaredVocabularyGuardTests
{
    [TestMethod]
    public void NoVocabularyDeclaresWireTokensOnOnlySomeOfItsMembers()
    {
        var half = ClosedSurfaceCensus.HalfDeclaredVocabularies(CensusScope.SweptHere);

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            half.ToArray(),
            "a vocabulary declares a wire token on some members and not others, so a reader cannot "
                + "tell which names are contract: " + string.Join(" | ", half));
    }
}
