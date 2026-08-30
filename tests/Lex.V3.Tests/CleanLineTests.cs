using Lex.V3.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests;

[TestClass]
public sealed class CleanLineTests
{
    [TestMethod]
    public void ContractLineIdentifiesOnlyV3()
    {
        Assert.AreEqual("lex-v3/contracts/1", ContractLine.Generation);
    }
}
