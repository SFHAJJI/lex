using System.Reflection;
using Lex.V3.Contracts.Source.Quarantine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Quarantine;

/// <summary>
/// Proves, structurally rather than by inspection, the CLAUDE.md hard rule this package must
/// never violate: nothing in the quarantine contracts can carry law body content
/// (<c>works/</c>, <c>*.xml</c>, <c>*.html</c>, law <c>*.json</c>) or open it. A caller could pass
/// a string that happens to spell a path -- <see cref="PriorPublicCoordinateTests"/> covers that
/// with a denylist, which is defence in depth, never the guarantee -- but no member anywhere in
/// this namespace has a type through which actual bytes, a stream, or a filesystem handle could
/// travel in the first place. This sweep is what makes that a checked property of the assembly
/// rather than a claim in a doc comment.
/// </summary>
[TestClass]
public sealed class NoLawContentCapabilityTests
{
    private const string RootNamespace = "Lex.V3.Contracts.Source.Quarantine";

    /// <summary>
    /// The swept type count today. Pinned as a literal, not a floor: a floor let this sweep
    /// silently stop finding (and therefore stop checking) new types without the test ever going
    /// red. Any type added to or removed from this namespace must show up here as a deliberate
    /// diff, assigned to whichever of the two lists below it belongs on.
    /// </summary>
    /// <remarks>
    /// <c>Assembly.GetTypes()</c> returns compiler-generated types too, not only the ones this
    /// file's authors wrote by hand: a <c>static</c> lambda used inside a swept type (for example
    /// the <c>character =&gt; ...</c> predicates in <see cref="PriorPublicCoordinate"/>'s and
    /// <see cref="QuarantineInventoryCanonicalizer"/>'s validation/decoding methods) compiles to a
    /// nested cached-delegate class (<c>+&lt;&gt;c</c>) in the SAME namespace as its containing
    /// type, and a lambda that captures a local compiles to a numbered <c>+&lt;&gt;c__DisplayClassN_M</c>
    /// nested class. Confirmed by probe (transcribed, not guessed, per this codebase's working
    /// discipline for guarded/reflected surfaces): of the 19, 12 are hand-authored (three refusal
    /// enums, one internal coordinate-validation helper, two canonicalization utilities, and six
    /// DTO/gate types) and 7 are compiler-generated closure-cache classes for those hand-authored
    /// types' own lambdas.
    /// </remarks>
    private const int ExpectedSweptTypeCount = 19;

    private static readonly Type[] ForbiddenContentTypes =
    [
        typeof(ReadOnlyMemory<byte>),
        typeof(Memory<byte>),
        typeof(Stream),
        typeof(FileStream),
        typeof(FileInfo),
        typeof(DirectoryInfo),
        typeof(StreamReader),
        typeof(StreamWriter),
        typeof(Uri),
    ];

    /// <summary>
    /// The two pure canonicalization utilities in this namespace, excluded from the main member
    /// sweep below by name -- not by any structural rule the sweep applies to everything else.
    /// Both are static classes with no instance and no public constructor:
    /// <see cref="PriorPublicCoordinateSet"/> and <see cref="QuarantineInventoryCanonicalizer"/>
    /// exist only to turn already-validated, already-bounded-ASCII coordinate and evidence fields
    /// into a SHA-256 digest or a signable byte sequence -- a computed OUTPUT, never a
    /// byte[]/Stream/path INPUT a caller could use to smuggle real content through.
    /// <see cref="TheExcludedCanonicalizationUtilitiesAcceptNoForbiddenParameter"/> below is what
    /// keeps this exclusion from becoming the same kind of loophole objection 3 found in the old
    /// predicate: it independently sweeps exactly these two types' constructors and method
    /// PARAMETERS (never their return types) through the identical <see cref="IsForbidden"/>
    /// predicate, so an input-side violation on either type still fails loudly.
    /// </summary>
    private static readonly Type[] ExcludedCanonicalizationUtilities =
    [
        typeof(PriorPublicCoordinateSet),
        typeof(QuarantineInventoryCanonicalizer),
    ];

    [TestMethod]
    public void NoQuarantineTypeHasAMemberThatCanCarryFileContentAStreamOrAUri()
    {
        var quarantineTypes = SweptTypes();

        Assert.AreEqual(
            ExpectedSweptTypeCount,
            quarantineTypes.Length,
            "the swept type count moved; update ExpectedSweptTypeCount deliberately, or a type was "
            + "silently added to or removed from the sweep");

        var offenders = new List<string>();
        foreach (var type in quarantineTypes.Except(ExcludedCanonicalizationUtilities))
        {
            CollectOffenders(type, includeStateAndReturnTypes: true, offenders);
        }

        Assert.AreEqual(0, offenders.Count, "content-capable members found: " + string.Join("; ", offenders));
    }

    [TestMethod]
    public void TheExcludedCanonicalizationUtilitiesAcceptNoForbiddenParameter()
    {
        var offenders = new List<string>();
        foreach (var type in ExcludedCanonicalizationUtilities)
        {
            CollectOffenders(type, includeStateAndReturnTypes: false, offenders);
        }

        Assert.AreEqual(
            0,
            offenders.Count,
            "a canonicalization utility accepted a forbidden-typed parameter: "
            + string.Join("; ", offenders));
    }

    /// <summary>
    /// Positive control (objection 3): proves <see cref="IsForbidden"/> actually flags the exact
    /// shapes the old predicate let through -- a bare byte[], and the generic containers around
    /// byte the review named explicitly ("byte spans, lists and enumerables"). Without this, the
    /// two tests above could pass for the wrong reason: nothing left to find, rather than nothing
    /// wrong. The real-member half of this control (refreeze fold-in 4) goes further and routes
    /// through <see cref="CollectOffenders"/> itself, against a type that genuinely has a
    /// byte-shaped offending member, rather than calling <see cref="IsForbidden"/> directly on the
    /// member's type -- so the traversal that walks constructors, properties, fields and methods
    /// is what is proven to find the offender, not just the predicate in isolation.
    /// </summary>
    [TestMethod]
    public void ThePredicateFlagsAByteArrayAndEveryOtherByteShapedContainer()
    {
        Assert.IsTrue(IsForbidden(typeof(byte[])), "byte[] must be flagged");
        Assert.IsTrue(IsForbidden(typeof(byte[,])), "a multi-dimensional byte array must be flagged");
        Assert.IsTrue(IsForbidden(typeof(byte[][])), "a jagged byte array must be flagged");
        Assert.IsTrue(IsForbidden(typeof(ReadOnlyMemory<byte>)), "ReadOnlyMemory<byte> must be flagged");
        Assert.IsTrue(IsForbidden(typeof(Memory<byte>)), "Memory<byte> must be flagged");
        Assert.IsTrue(IsForbidden(typeof(Span<byte>)), "Span<byte> must be flagged");
        Assert.IsTrue(IsForbidden(typeof(ReadOnlySpan<byte>)), "ReadOnlySpan<byte> must be flagged");
        Assert.IsTrue(IsForbidden(typeof(List<byte>)), "List<byte> must be flagged");
        Assert.IsTrue(IsForbidden(typeof(IEnumerable<byte>)), "IEnumerable<byte> must be flagged");
        Assert.IsTrue(IsForbidden(typeof(IReadOnlyList<byte>)), "IReadOnlyList<byte> must be flagged");
        Assert.IsTrue(IsForbidden(typeof(Task<byte[]>)), "Task<byte[]> must be flagged");

        // Routed through CollectOffenders itself, not IsForbidden called directly on a bare
        // typeof(...): a prior version of this control called IsForbidden(property.PropertyType)
        // directly, which exercises only the predicate, never the traversal (the constructor,
        // property, field and method sweep) that the two tests above actually depend on. Calling
        // CollectOffenders here against a real type with a genuine offending member proves the
        // traversal itself finds the offender -- precisely the shape the bug let through:
        // PriorPublicCoordinateSet.CanonicalBytes returns byte[] and, before this fix, was never
        // flagged by the sweep despite living in the very namespace it polices.
        var memberOffenders = new List<string>();
        CollectOffenders(typeof(PositiveControlWithByteContent), includeStateAndReturnTypes: true, memberOffenders);
        Assert.IsTrue(
            memberOffenders.Count > 0,
            "CollectOffenders must find at least one offending member on a type with a real byte[] property");
        Assert.IsTrue(
            memberOffenders.Any(offender => offender.Contains(nameof(PositiveControlWithByteContent.RawBytes), StringComparison.Ordinal)),
            "the offender list must name the RawBytes member: " + string.Join("; ", memberOffenders));

        // Negative control: a bare scalar byte (not an array, not a generic argument) carries no
        // content-carrying capability and must not be flagged -- only the element/argument shape
        // the review named is forbidden.
        Assert.IsFalse(IsForbidden(typeof(byte)), "a bare scalar byte is not itself forbidden");
        Assert.IsFalse(IsForbidden(typeof(string)), "string must never be flagged");
        Assert.IsFalse(IsForbidden(typeof(int[])), "an int[] must never be flagged");
    }

    private sealed class PositiveControlWithByteContent
    {
        public byte[] RawBytes { get; init; } = [];
    }

    private static Type[] SweptTypes()
    {
        var assembly = typeof(PriorPublicCoordinate).Assembly;
        return assembly.GetTypes()
            .Where(static type =>
                type.Namespace == RootNamespace ||
                (type.Namespace is not null &&
                 type.Namespace.StartsWith(RootNamespace + ".", StringComparison.Ordinal)))
            .ToArray();
    }

    private static void CollectOffenders(Type type, bool includeStateAndReturnTypes, List<string> offenders)
    {
        const BindingFlags everything =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        foreach (var constructor in type.GetConstructors(everything))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                if (IsForbidden(parameter.ParameterType))
                {
                    offenders.Add($"{type.FullName} ctor parameter {parameter.Name}: {parameter.ParameterType}");
                }
            }
        }

        if (includeStateAndReturnTypes)
        {
            foreach (var property in type.GetProperties(everything))
            {
                if (IsForbidden(property.PropertyType))
                {
                    offenders.Add($"{type.FullName}.{property.Name}: {property.PropertyType}");
                }
            }

            foreach (var field in type.GetFields(everything))
            {
                if (IsForbidden(field.FieldType))
                {
                    offenders.Add($"{type.FullName}.{field.Name}: {field.FieldType}");
                }
            }
        }

        foreach (var method in type.GetMethods(everything))
        {
            if (includeStateAndReturnTypes && IsForbidden(method.ReturnType))
            {
                offenders.Add($"{type.FullName}.{method.Name} return: {method.ReturnType}");
            }

            foreach (var parameter in method.GetParameters())
            {
                if (IsForbidden(parameter.ParameterType))
                {
                    offenders.Add($"{type.FullName}.{method.Name} parameter {parameter.Name}: {parameter.ParameterType}");
                }
            }
        }
    }

    private static bool IsForbidden(Type type)
    {
        var unwrapped = type.IsByRef || type.IsPointer ? type.GetElementType()! : type;

        if (unwrapped.IsArray)
        {
            // byte[] (and byte[,], byte[][], ...) must be forbidden because their ELEMENT is byte,
            // not because the array type itself matches a name in ForbiddenContentTypes. This is
            // exactly the bug objection 3 found: the old code reduced byte[] to byte here and then
            // checked byte against a list that only names whole types (byte[], Stream, ...), so it
            // silently passed. Recursing through the same element/argument check used for generic
            // containers closes that hole instead of special-casing arrays separately.
            return IsElementOrArgumentForbidden(unwrapped.GetElementType()!);
        }

        if (ForbiddenContentTypes.Any(forbidden => forbidden == unwrapped || forbidden.IsAssignableFrom(unwrapped)))
        {
            return true;
        }

        if (unwrapped.IsGenericType)
        {
            // Generic containers (IReadOnlyList<T>, List<T>, Memory<T>, Span<T>, Task<T>, ...)
            // around a forbidden element -- byte included, so List<byte>, IEnumerable<byte>,
            // Memory<byte> and Span<byte> are all caught through this one rule, the same rule that
            // catches byte[].
            return unwrapped.GetGenericArguments().Any(IsElementOrArgumentForbidden);
        }

        return false;
    }

    /// <summary>
    /// Forbids <c>byte</c> as an element type or generic argument (objection 3's exact wording),
    /// deliberately not as a bare scalar: nothing in this namespace has, or needs, a raw
    /// <c>byte</c>-typed parameter or property, and a single 0-255 value cannot itself carry file
    /// content the way an array, span, list or enumerable of them can.
    /// </summary>
    private static bool IsElementOrArgumentForbidden(Type elementOrArgument) =>
        elementOrArgument == typeof(byte) || IsForbidden(elementOrArgument);
}
