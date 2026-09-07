using System.Reflection;

namespace Lex.V3.TestSupport;

/// <summary>
/// Sweeps a whole assembly for V2 compatibility surface, so S1-A07 is a checked property of the
/// V3 line rather than a claim in a doc comment.
/// </summary>
/// <remarks>
/// <para>
/// S1-A07 reads: "No V2 answer or refusal envelope reader, serializer, shim, alias, fixture or
/// compatibility test enters the V3 line." Today the clause holds — every one of the 41 <c>v2</c>
/// occurrences in <c>src/</c> is a doc comment, either KEEP/IMPROVE/REFUSE provenance against the
/// v2 repository or an explicit boundary statement such as
/// <c>QuarantinedPriorCoordinateInventory</c>'s "V3 has no V2 index reader, must never grow one".
/// Nothing enforced it. A <c>V2AnswerEnvelopeReader</c> added tomorrow would have gone in green,
/// which is the shape a prohibition always fails in: silently, and only once it is expensive.
/// </para>
/// <para>
/// WHY THE PREDICATE IS A NAME TEST, stated plainly because it is the weak point. A V2 reader
/// could be named anything, so this cannot prove the absence of the capability the way
/// <c>NoLawContentCapabilityTests</c> proves the absence of a byte-carrying member — there the
/// forbidden thing has a type, here it does not. What this does prove is that the eradication rule
/// the code already states in comments is now enforced against the obvious spelling, on a line
/// measured clean at the commit that introduced it, so any reintroduction has to be a deliberate
/// rename rather than an ordinary mistake. That is a real reduction in exposure and it is not the
/// whole clause; the limitation belongs in the review, not in a silence.
/// </para>
/// </remarks>
public static class V2CompatibilitySurface
{
    /// <summary>
    /// Names deliberately permitted to carry <c>v2</c>. Empty, and meant to stay empty: an entry
    /// here is an exception to an eradication rule and must be argued in review, not added to make
    /// a red sweep green.
    /// </summary>
    public static readonly IReadOnlyList<string> PermittedNames = [];

    /// <summary>True when a declared name spells V2 surface.</summary>
    /// <remarks>
    /// A plain case-insensitive containment test rather than a token boundary. The baseline this
    /// ships against was measured identifier-clean across all eight production projects, so the
    /// looser test costs nothing today and refuses more tomorrow; <c>v2</c> requires a <c>v</c>
    /// immediately followed by <c>2</c>, which no ordinary word in this codebase produces.
    /// </remarks>
    public static bool NamesV2CompatibilitySurface(string? name) =>
        name is not null
        && name.Contains("v2", StringComparison.OrdinalIgnoreCase)
        && !PermittedNames.Contains(name, StringComparer.Ordinal);

    /// <summary>
    /// Every declared type and member name in <paramref name="assembly"/> that spells V2 surface,
    /// as <c>Type</c> or <c>Type.Member</c>, ordered so a failure message reads the same twice.
    /// </summary>
    public static IReadOnlyList<string> Offenders(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var found = new List<string>();
        foreach (var type in SweptTypes(assembly))
        {
            if (NamesV2CompatibilitySurface(type.Name) || NamesV2CompatibilitySurface(type.FullName))
            {
                found.Add(type.FullName ?? type.Name);
            }

            foreach (var member in type.GetMembers(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (NamesV2CompatibilitySurface(member.Name))
                {
                    found.Add($"{type.FullName ?? type.Name}.{member.Name}");
                }
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>
    /// How many types the sweep actually looked at. A test pins this being non-zero per assembly,
    /// because a sweep that silently stops finding types stops checking without ever going red.
    /// </summary>
    public static int SweptTypeCount(Assembly assembly) => SweptTypes(assembly).Count;

    private static IReadOnlyList<Type> SweptTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            // A partially loadable assembly still gets swept for what did load, because returning
            // nothing here would turn a load failure into a silent pass of a prohibition.
            return [.. exception.Types.Where(static type => type is not null).Select(static type => type!)];
        }
    }
}
