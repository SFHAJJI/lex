using System.Reflection;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// The Union legal-policy rights matrix.
///
/// Totality is load-bearing in one direction. A spurious entry is already refused by
/// <see cref="EuRightsDisposition"/>, which reads the basis from the notice rather than the caller.
/// A missing entry is what nothing upstream can catch: the matrix reads as a settled legal position
/// while the class it omits has no basis at all, and silence about an exception channel reads as
/// "this cannot occur", which is the one thing a rights position must never say by omission.
/// </summary>
[TestClass]
public sealed class EuRightsMatrixTests
{
    [TestMethod]
    public void ACompleteMatrixAnswersForEveryClassAndEveryChannel()
    {
        var matrix = EuRightsMatrix.TryAdmit(AllClasses(), AllChannels(), out var refusal);
        Assert.IsNotNull(matrix, $"refused as {refusal}");
        Assert.AreEqual(EuRightsMatrixRefusal.None, refusal);

        foreach (var contentClass in Enum.GetValues<EuContentClass>())
        {
            Assert.AreEqual(
                EuRightsDisposition.BasisFor(contentClass),
                matrix.For(contentClass).Basis,
                $"{contentClass} must carry the basis read from the notice");
        }

        foreach (var channel in Enum.GetValues<EuRightsExceptionChannel>())
        {
            Assert.AreEqual(channel, matrix.For(channel).Channel);
        }

        // The distinction the matrix exists to preserve: published law is not CC0, and metadata is.
        Assert.AreEqual(
            EuReuseBasis.EurLexLegalNoticePermission,
            matrix.For(EuContentClass.OriginalLegalText).Basis);
        Assert.AreEqual(EuReuseBasis.Cc0, matrix.For(EuContentClass.Metadata).Basis);
    }

    [TestMethod]
    public void AnyMissingContentClassRefusesTheWholeMatrix()
    {
        foreach (var omitted in Enum.GetValues<EuContentClass>())
        {
            Assert.IsNull(
                EuRightsMatrix.TryAdmit(
                    AllClasses().Where(row => row.ContentClass != omitted).ToArray(),
                    AllChannels(),
                    out var refusal),
                $"a matrix missing {omitted} must not be admitted");
            Assert.AreEqual(EuRightsMatrixRefusal.ContentClassUndecided, refusal);
        }
    }

    [TestMethod]
    public void AnyMissingExceptionChannelRefusesTheWholeMatrix()
    {
        foreach (var omitted in Enum.GetValues<EuRightsExceptionChannel>())
        {
            Assert.IsNull(
                EuRightsMatrix.TryAdmit(
                    AllClasses(),
                    AllChannels().Where(row => row.Channel != omitted).ToArray(),
                    out var refusal),
                $"a matrix missing {omitted} must not be admitted");
            Assert.AreEqual(EuRightsMatrixRefusal.ExceptionChannelUndecided, refusal);
        }
    }

    [TestMethod]
    public void DuplicatesRefuseOnTheirOwnSide()
    {
        Assert.IsNull(EuRightsMatrix.TryAdmit(
            AllClasses().Append(Class(EuContentClass.Metadata)).ToArray(),
            AllChannels(),
            out var classes));
        Assert.AreEqual(EuRightsMatrixRefusal.DuplicateContentClass, classes);

        Assert.IsNull(EuRightsMatrix.TryAdmit(
            AllClasses(),
            AllChannels().Append(Channel(EuRightsExceptionChannel.ThirdPartyMaterial)).ToArray(),
            out var channels));
        Assert.AreEqual(EuRightsMatrixRefusal.DuplicateExceptionChannel, channels);
    }

    [TestMethod]
    public void TotalityTracksTheEnumsRatherThanWrittenCounts()
    {
        var matrix = EuRightsMatrix.TryAdmit(AllClasses(), AllChannels(), out _);
        Assert.IsNotNull(matrix);
        Assert.AreEqual(Enum.GetValues<EuContentClass>().Length, matrix.ContentClasses.Count);
        Assert.AreEqual(
            Enum.GetValues<EuRightsExceptionChannel>().Length, matrix.ExceptionChannels.Count);

        CollectionAssert.AreEqual(
            Enum.GetValues<EuContentClass>(),
            matrix.ContentClasses.Select(row => row.ContentClass).ToArray(),
            "declaration order, so a caller's argument order cannot become the policy order");
    }

    [TestMethod]
    public void TheRefusalVocabularyIsClosedAndSpelledForTheWire()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "\"none\"", "\"duplicate_content_class\"", "\"content_class_undecided\"",
                "\"duplicate_exception_channel\"", "\"exception_channel_undecided\"",
            },
            Enum.GetValues<EuRightsMatrixRefusal>().Select(ContractJson.Serialize).ToArray());
    }

    [TestMethod]
    public void TheMatrixHasExactlyOneConstructionPath()
    {
        var type = typeof(EuRightsMatrix);

        var constructors = type.GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsTrue(constructors.Length > 0);
        Assert.IsTrue(
            constructors.All(constructor => constructor.IsPrivate),
            "a non-private constructor would let a caller mint an incomplete legal position");

        var factories = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.ReturnType == type
                || (method.ReturnType.IsByRef && method.ReturnType.GetElementType() == type)
                || method.GetParameters().Any(parameter =>
                    parameter.ParameterType.IsByRef
                    && parameter.ParameterType.GetElementType() == type))
            .Select(method => $"{(method.IsStatic ? "static" : "instance")} {method.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "static TryAdmit" }, factories);
        Assert.AreEqual(
            0,
            type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Length);
    }

    private static EuRightsDisposition[] AllClasses() =>
        Enum.GetValues<EuContentClass>().Select(Class).ToArray();

    private static EuRightsExceptionDisposition[] AllChannels() =>
        Enum.GetValues<EuRightsExceptionChannel>().Select(Channel).ToArray();

    private static EuRightsDisposition Class(EuContentClass contentClass) =>
        new(contentClass, EuRightsDisposition.BasisFor(contentClass), Notice);

    private static EuRightsExceptionDisposition Channel(EuRightsExceptionChannel channel) =>
        new(channel, Notice);

    private static readonly SourceArtifactRef Notice =
        new("urn:uuid:00000000-0000-4000-8000-0000000000dd", new string('d', 64));
}
