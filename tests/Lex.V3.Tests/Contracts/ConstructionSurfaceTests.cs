using Lex.V3.TestSupport;

namespace Lex.V3.Tests.Contracts;

/// <summary>
/// The shared guard's acceptance test: the exact enumeration it produces for a fixture written
/// without sight of it (<see cref="SurfaceGuardLeakyFixture"/>). Every line here is a door the
/// fixture opens; a guard change that drops one fails here before it fails in a real surface test.
/// </summary>
[TestClass]
public sealed class ConstructionSurfaceTests
{
    private const string F = "Lex.V3.Tests.Contracts.SurfaceGuardLeakyFixture+";
    private const string Thing = F + "LeakyThing";
    private const string Record = F + "LeakyRecord";

    [TestMethod]
    public void TheGuardEnumeratesEveryDoorOfTheLeakyThingItself()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance " + F + "LeakyBase::.ctor(System.String) -> " + F + "LeakyBase",
                "by-ref-method public static " + Thing + "::TryOpen(System.String, out " + Thing + "&) -> System.Boolean",
                "constructor private instance " + Thing + "::.ctor(System.String) -> " + Thing,
                "constructor private static " + Thing + "::.cctor() -> " + Thing,
                "field private static " + Thing + "::_slot -> " + Thing,
                "field public static " + Thing + "::Constant -> " + Thing,
                "field public static " + Thing + "::Shared -> " + Thing,
                "method internal static " + Thing + "::Friend(System.String) -> " + Thing,
                "method public instance " + Thing + "::Rebrand(System.String) -> " + Thing,
                "method public static " + Thing + "+Nested::Make(System.String) -> " + Thing,
                "method public static " + Thing + "::Adopt(System.String) -> " + Thing,
                "method public static " + Thing + "::Borrow() -> " + Thing + "&",
                "method public static " + Thing + "::Open(System.String, out System.String&) -> " + Thing,
                "method public static " + Thing + "::OpenAsync(System.String) -> System.Threading.Tasks.Task<" + Thing + ">",
                "method public static " + Thing + "::OpenWithNote(System.String) -> System.ValueTuple<" + Thing + ", System.String>",
                "operator public static " + Thing + "::op_Implicit(System.String) -> " + Thing,
            },
            ConstructionSurface.Of(typeof(SurfaceGuardLeakyFixture.LeakyThing)).ToArray());
    }

    [TestMethod]
    public void TheAssemblySweepFindsTheProducersDeclaredOutsideTheLeakyThing()
    {
        var all = ConstructionSurface.ProducersIn(
            typeof(SurfaceGuardLeakyFixture).Assembly,
            typeof(SurfaceGuardLeakyFixture.LeakyThing),
            includeNonPublic: true);
        CollectionAssert.AreEqual(
            new[]
            {
                "field private static " + F + "Indirect::<Factory>k__BackingField -> System.Func<System.String, " + Thing + ">",
                "method private instance " + F + "ExplicitOpener::Lex.V3.Tests.Contracts.SurfaceGuardLeakyFixture.ILeakyOpener.OpenOne(System.String) -> " + Thing,
                "method public instance " + F + "ILeakyOpener::OpenOne(System.String) -> " + Thing,
                "method public static " + F + "Elsewhere::Produce(System.String) -> " + Thing,
                "property public static " + F + "Indirect::Factory() -> System.Func<System.String, " + Thing + ">",
            },
            all.ToArray());

        var publicOnly = ConstructionSurface.ProducersIn(
            typeof(SurfaceGuardLeakyFixture).Assembly,
            typeof(SurfaceGuardLeakyFixture.LeakyThing),
            includeNonPublic: false);
        CollectionAssert.AreEqual(
            all.Where(static entry => entry.Contains(" public ", StringComparison.Ordinal)).ToArray(),
            publicOnly.ToArray());
        Assert.AreEqual(3, publicOnly.Count);
    }

    [TestMethod]
    public void TheGuardSeesTheDoorsARecordEmitsForItsAuthor()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + Record + "::.ctor(" + Record + ") -> " + Record,
                "constructor private instance " + Record + "::.ctor(System.String) -> " + Record,
                "method public instance " + Record + "::<Clone>$() -> " + Record,
                "method public static " + Record + "::Open(System.String) -> " + Record,
            },
            ConstructionSurface.Of(typeof(SurfaceGuardLeakyFixture.LeakyRecord)).ToArray());
        Assert.AreEqual(
            0,
            ConstructionSurface.ProducersIn(
                typeof(SurfaceGuardLeakyFixture).Assembly,
                typeof(SurfaceGuardLeakyFixture.LeakyRecord),
                includeNonPublic: true).Count);
    }

    [TestMethod]
    public void CarriesLooksThroughEveryWrapperAndNothingElse()
    {
        var thing = typeof(SurfaceGuardLeakyFixture.LeakyThing);
        Assert.IsTrue(ConstructionSurface.Carries(thing, thing));
        Assert.IsTrue(ConstructionSurface.Carries(thing, typeof(SurfaceGuardLeakyFixture.LeakyBase)));
        Assert.IsTrue(ConstructionSurface.Carries(thing.MakeByRefType(), thing));
        Assert.IsTrue(ConstructionSurface.Carries(thing.MakeArrayType(), thing));
        Assert.IsTrue(ConstructionSurface.Carries(typeof(Task<>).MakeGenericType(thing), thing));
        Assert.IsTrue(ConstructionSurface.Carries(typeof(Func<,>).MakeGenericType(typeof(string), thing), thing));
        Assert.IsTrue(ConstructionSurface.Carries(
            typeof(IReadOnlyList<>).MakeGenericType(typeof(Lazy<>).MakeGenericType(thing)),
            thing));
        Assert.IsFalse(ConstructionSurface.Carries(typeof(SurfaceGuardLeakyFixture.LeakyBase), thing));
        Assert.IsFalse(ConstructionSurface.Carries(typeof(object), thing));
        Assert.IsFalse(ConstructionSurface.Carries(typeof(Task<string>), thing));
        Assert.IsFalse(ConstructionSurface.Carries(typeof(SurfaceGuardLeakyFixture.ILeakyOpener), thing));
    }
}
