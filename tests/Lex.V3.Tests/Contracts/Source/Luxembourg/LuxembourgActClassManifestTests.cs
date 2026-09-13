using System.Reflection;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// #419 slice 4: the LOI/RGD class manifest. E10 counts exactly LOI and RGD; the manifest classifies
/// the whole recognized legal-type vocabulary, names every other recognized code out of scope, and
/// fails closed on an unrecognized one.
/// </summary>
[TestClass]
public sealed class LuxembourgActClassManifestTests
{
    private const string ResourceTypeAuthority =
        "http://data.legilux.public.lu/resource/authority/resource-type/";

    /// <summary>LOI and RGD are in scope, under the publisher's real resource-type authority.</summary>
    /// <remarks>
    /// The IRIs are pinned against the literal authority rather than only against the manifest's own
    /// constants, so a change to the wrong authority (the frame's test fixtures use a
    /// <c>legal-type/</c> stand-in that the publisher does not actually emit) fails here.
    /// </remarks>
    [TestMethod]
    public void LoiAndRgdAreInScopeUnderThePublishersResourceTypeAuthority()
    {
        Assert.AreEqual(ResourceTypeAuthority + "LOI", LuxembourgActClassManifest.LoiClassIri);
        Assert.AreEqual(ResourceTypeAuthority + "RGD", LuxembourgActClassManifest.RgdClassIri);
        Assert.AreEqual(
            LuxembourgActClassScope.InScopeLoi,
            LuxembourgActClassManifest.Classify(LuxembourgActClassManifest.LoiClassIri));
        Assert.AreEqual(
            LuxembourgActClassScope.InScopeRgd,
            LuxembourgActClassManifest.Classify(LuxembourgActClassManifest.RgdClassIri));
    }

    /// <summary>Exactly LOI and RGD are counted; nothing else the manifest classifies is.</summary>
    [TestMethod]
    public void ExactlyLoiAndRgdAreCounted()
    {
        var counted = LuxembourgActClassManifest.RecognizedClassIris
            .Where(iri => LuxembourgActClassManifest.IsCounted(
                LuxembourgActClassManifest.Classify(iri)))
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { LuxembourgActClassManifest.LoiClassIri, LuxembourgActClassManifest.RgdClassIri }
                .OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            counted,
            "E10's counted population is exactly LOI and RGD.");
    }

    /// <summary>
    /// Every other recognized legal-type is named out of scope — not counted, not unrecognized.
    /// </summary>
    [TestMethod]
    public void EveryOtherRecognizedLegalTypeIsNamedOutOfScope()
    {
        var others = LuxembourgActClassManifest.RecognizedClassIris
            .Where(iri => iri != LuxembourgActClassManifest.LoiClassIri
                && iri != LuxembourgActClassManifest.RgdClassIri)
            .ToArray();

        Assert.IsGreaterThan(0, others.Length, "the manifest classifies more than the two counted codes.");
        foreach (var iri in others)
        {
            Assert.AreEqual(
                LuxembourgActClassScope.RecognizedOutOfScope,
                LuxembourgActClassManifest.Classify(iri),
                $"{iri} is recognized and out of scope, not counted.");
            Assert.IsFalse(
                LuxembourgActClassManifest.IsCounted(LuxembourgActClassManifest.Classify(iri)),
                $"{iri} is not counted.");
        }
    }

    /// <summary>
    /// An unrecognized code fails closed — it is not silently treated as out of scope.
    /// </summary>
    /// <remarks>
    /// Both a code that does not exist under the real authority and one under a stand-in authority
    /// (the frame fixtures' <c>legal-type/</c>) are unrecognized. The distinction the owner's ruling
    /// draws is between "recognized and out of scope" and "unrecognized"; a count may keep spending
    /// on the former and must stop on the latter, so they cannot collapse into one answer.
    /// </remarks>
    [TestMethod]
    public void AnUnrecognizedCodeFailsClosedRatherThanBeingTreatedAsOutOfScope()
    {
        foreach (var unrecognized in new[]
        {
            ResourceTypeAuthority + "NOT_A_REAL_CODE",
            "http://data.legilux.public.lu/resource/authority/legal-type/LOI",
            "http://data.legilux.public.lu/resource/authority/resource-type/",
        })
        {
            Assert.IsFalse(
                LuxembourgActClassManifest.TryClassify(unrecognized, out _),
                $"{unrecognized} is unrecognized.");
            var thrown = Assert.ThrowsExactly<ArgumentException>(
                () => LuxembourgActClassManifest.Classify(unrecognized));
            Assert.AreEqual("publisherClassIri", thrown.ParamName);
            StringAssert.Contains(thrown.Message, "fails closed");
        }
    }

    /// <summary>
    /// The counting boundary refuses an undefined scope, so a TryClassify miss cannot be read as
    /// "not counted".
    /// </summary>
    /// <remarks>
    /// Codex's round-1 finding: TryClassify leaves scope at default(0) on a miss, and IsCounted
    /// returned false for it - the same answer as RecognizedOutOfScope. A consumer that ignored the
    /// bool would silently exclude an unrecognized legal-type from the claimed complete population.
    /// IsCounted now fails closed on the undefined value.
    /// </remarks>
    [TestMethod]
    public void TheCountingBoundaryRefusesAnUndefinedScope()
    {
        var undefined = default(LuxembourgActClassScope);
        Assert.IsFalse(Enum.IsDefined(undefined), "the premise: default(0) is not a named member.");

        LuxembourgActClassManifest.TryClassify("http://data.legilux.public.lu/x", out var missScope);
        Assert.AreEqual(undefined, missScope, "a miss leaves scope at the undefined default.");

        var thrown = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => LuxembourgActClassManifest.IsCounted(undefined));
        Assert.AreEqual("scope", thrown.ParamName);
    }

    [TestMethod]
    public void ClassifyingANullIriIsACallerContractViolation() =>
        Assert.ThrowsExactly<ArgumentNullException>(
            () => LuxembourgActClassManifest.TryClassify(null!, out _));

    /// <summary>
    /// The manifest classifies EXACTLY the resource-type codes this build's resolver recognizes —
    /// no more, no fewer.
    /// </summary>
    /// <remarks>
    /// The manifest states the recognized vocabulary rather than reading the resolver's private sets
    /// at runtime, so this guard reflects over those sets and requires the two to agree. If the
    /// publisher mints a new legal-type and the resolver learns it, the manifest must classify it too
    /// or this fails; and the manifest may not recognize a code the resolver does not. That is what
    /// keeps "recognized" one vocabulary across the build rather than two that drift.
    /// </remarks>
    [TestMethod]
    public void TheManifestClassifiesExactlyTheResolversRecognizedResourceTypeCodes()
    {
        var resolverResourceTypeCodes = typeof(LuxembourgScopeResolver)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => typeof(IEnumerable<string>).IsAssignableFrom(field.FieldType))
            .SelectMany(field => (IEnumerable<string>)field.GetValue(null)!)
            .Where(value => value.StartsWith(ResourceTypeAuthority, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        Assert.IsGreaterThan(
            0, resolverResourceTypeCodes.Count,
            "the reflection must actually find the resolver's resource-type buckets.");
        CollectionAssert.AreEquivalent(
            resolverResourceTypeCodes.ToArray(),
            LuxembourgActClassManifest.RecognizedClassIris.ToArray(),
            "the manifest and the resolver must recognize exactly the same resource-type vocabulary.");
    }

    /// <summary>Every scope member has its exact wire token and round-trips.</summary>
    [TestMethod]
    public void EveryScopeMemberHasItsExactWireToken() =>
        CollectionAssert.AreEqual(
            new[] { "\"in_scope_loi\"", "\"in_scope_rgd\"", "\"recognized_out_of_scope\"" },
            Enum.GetValues<LuxembourgActClassScope>()
                .Select(member => ContractJson.Serialize(member))
                .ToArray());
}
