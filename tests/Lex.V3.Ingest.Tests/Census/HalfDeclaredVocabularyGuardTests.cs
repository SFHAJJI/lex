using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests.Census;

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
/// THE FULLY DECLARED COUNT IS ASSERTED RATHER THAN DESCRIBED, and that is this guard's reach
/// check as well as its scale. Without it the guard has the empty-baseline shape it exists to
/// remove: wrong binding flags or an inverted filter would read every member as undeclared, the
/// half-declared filter would then match nothing, and a guard whose whole job is to see a surface
/// would report success having seen none of it. A count in a remark cannot fail; a count in an
/// assertion can.
/// </para>
/// <para>
/// This file once carried three counts in prose, and they were measured over src PLUS both test
/// assemblies while this guard sweeps only the census scope, which holds neither. The numbers were
/// real and were about a different set. A count is not a number, it is a number ABOUT A SET, and
/// the set has to be the one the sentence claims.
/// </para>
/// <para>
/// Vocabularies declaring nothing at all are deliberately outside the rule. Most enums here are
/// internal state that never reaches a wire; the criterion is that a vocabulary which has BEGUN
/// declaring tokens has to finish, because that is the point at which a reader starts inferring
/// meaning from their presence.
/// </para>
/// </remarks>
[TestClass]
public sealed class HalfDeclaredVocabularyGuardTests
{
    [TestMethod]
    public void NoVocabularyDeclaresWireTokensOnOnlySomeOfItsMembers()
    {
        var census = ClosedSurfaceCensus.DeclarationCensus(CensusScope.SweptHere);

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            census.HalfDeclared.ToArray(),
            "a vocabulary declares a wire token on some members and not others, so a reader cannot "
                + "tell which names are contract: " + string.Join(" | ", census.HalfDeclared));

        Assert.AreEqual(
            11,
            census.FullyDeclared,
            "the vocabularies in this scope that declare a token on every member. This is the reach "
                + "check: if the attribute read broke, this would fall to zero and the emptiness "
                + "above would prove nothing.");
    }
}
