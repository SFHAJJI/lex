using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Lex.V3.TestSupport;

/// <summary>
/// Sweeps a whole assembly for the three shapes this repository treats as closed: a vocabulary
/// whose members are the contract, a type whose construction is restricted to a named door, and a
/// static registry of tokens. Each sweep answers "what is there", so a test can pin the answer.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists at all. <see cref="ConstructionSurface"/> guards one type at a time, and it is
/// only as good as somebody having remembered to point it at that type. On 2026-09-04 four closed
/// vocabularies were found carrying no pin of any kind, each by accident while doing something
/// else, which says nothing about whether four is the number. These sweeps make the question
/// answerable: a test pins a whole assembly's closed surface, so a vocabulary added tomorrow
/// arrives inside the pin rather than outside every pin.
/// </para>
/// <para>
/// The one rule every sweep here obeys. A sweep is narrowed only by properties the type itself
/// has: it is an enum; every constructor it declares is non-public; it is a static class holding a
/// collection or a run of string constants. No sweep is narrowed by the list its caller is about to
/// compare it against. That mistake was made in this repository on the same day and it is silent:
/// filter an assembly scan through the names you expect and the scan can only ever return names you
/// expect, so the test passes forever and reads like coverage. Before changing a predicate below,
/// ask what addition to the assembly would flip it. If the answer is none, it is not a sweep.
/// </para>
/// <para>
/// What these sweeps cannot see. They read an assembly's metadata, so a vocabulary that is a set of
/// strings rather than an enum, a guarded type reached through <c>Activator</c> or
/// <c>GetUninitializedObject</c>, and a registry built at run time are all invisible, exactly as
/// they are to <see cref="ConstructionSurface"/>. They also see only the assemblies a caller names,
/// which is why the callers pin that list against what is actually deployed.
/// </para>
/// </remarks>
public static class ClosedSurfaceCensus
{
    private const BindingFlags Everything =
        BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// The simple names of every <c>Lex.V3.*</c> assembly deployed beside <paramref name="tests"/>,
    /// excluding the test assembly itself. This is what a census in that project can reach at all.
    /// </summary>
    public static IReadOnlyList<string> LexAssembliesBeside(Assembly tests)
    {
        ArgumentNullException.ThrowIfNull(tests);
        var self = tests.GetName().Name;
        return Directory.EnumerateFiles(AppContext.BaseDirectory, "Lex.V3.*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && !string.Equals(name, self, StringComparison.Ordinal))
            .Select(static name => name!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Every enum in <paramref name="assemblies"/>, as <c>full name: member, member</c>, ordered by
    /// full name. Nested and non-public enums are included: a closed vocabulary is closed whoever
    /// can see it.
    /// </summary>
    public static IReadOnlyList<string> ClosedVocabularies(params string[] assemblies) =>
        Load(assemblies)
            .SelectMany(AllTypes)
            .Where(static type => type.IsEnum)
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(static type => type.FullName + ": " + string.Join(", ", Enum.GetNames(type)))
            .ToArray();

    /// <summary>
    /// Every type in <paramref name="assemblies"/> whose declared constructors are all non-public,
    /// as <c>full name: hand-out, hand-out</c>, ordered by full name. Each hand-out is
    /// <see cref="ConstructionSurface.HandOuts"/>'s own entry with the parameter list and return
    /// type cut off, plus a count of the compiler-generated ones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A type with no public constructor is a type whose author decided a caller must come through
    /// a named door. That is a property of the type, readable without knowing what the type is for,
    /// which is what makes it sweepable; "is guarded" as a judgement about intent is not.
    /// </para>
    /// <para>
    /// The parameter list is cut off because a census over every such type in an assembly is
    /// otherwise four times the size and stops being read. State plainly what that costs: this
    /// catches a door added, removed, renamed, moved to another type or given a different scope,
    /// and it does not catch an existing door's parameters changing. The exact per-type pins built
    /// on <see cref="ConstructionSurface.Of"/> catch that, for the types that have one. Overloads
    /// survive the cut because the entries are not deduplicated afterwards, so a second
    /// <c>Create</c> is a second line.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> GuardedConstruction(params string[] assemblies) =>
        Load(assemblies)
            .SelectMany(static assembly => AllTypes(assembly)
                .Where(ConstructionIsRestricted)
                .Select(type => (Assembly: assembly, Type: type)))
            .OrderBy(static found => found.Type.FullName, StringComparer.Ordinal)
            .Select(static found => found.Type.FullName + ": " + Doors(found.Assembly, found.Type))
            .ToArray();

    /// <summary>
    /// Every static class in <paramref name="assemblies"/> that holds a token registry, as
    /// <c>full name: member=count, const Name</c>, ordered by full name. A registry is a static
    /// class with at least one static readonly collection or static get-only collection property,
    /// or with two or more string constants.
    /// </summary>
    /// <remarks>
    /// A collection's element count is the pinned part, not its contents: reading the contents of
    /// every registry in an assembly would pin a large amount of publisher text a second time, in a
    /// place nobody would think to update it. A token added to or removed from a registry moves the
    /// count. A token swapped for another does not, and the registry's own tests are the control
    /// for that. A member whose static initializer throws is reported as <c>unreadable</c> rather
    /// than skipped, because a member that quietly leaves a sweep is the failure this file exists
    /// to prevent.
    /// </remarks>
    public static IReadOnlyList<string> VocabularyRegistries(params string[] assemblies) =>
        Load(assemblies)
            .SelectMany(AllTypes)
            .Where(IsRegistry)
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(static type => type.FullName + ": " + string.Join(", ", RegistryMembers(type)))
            .ToArray();

    private static IEnumerable<Assembly> Load(string[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        return assemblies
            .OrderBy(static name => name, StringComparer.Ordinal)
            .Select(static name => Assembly.Load(new AssemblyName(name)));
    }

    private static bool ConstructionIsRestricted(Type type)
    {
        if (type.IsEnum || type.IsInterface || type.IsAbstract)
        {
            return false;
        }

        var declared = type.GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        return declared.Length > 0 && !declared.Any(static constructor => constructor.IsPublic);
    }

    private static string Doors(Assembly assembly, Type guarded)
    {
        var surface = ConstructionSurface.HandOuts(assembly, guarded);
        var doors = surface.Declared.Select(static entry =>
        {
            var cut = entry.IndexOf('(');
            return cut < 0 ? entry : entry[..cut];
        }).ToList();
        if (surface.CompilerGenerated > 0)
        {
            doors.Add(surface.CompilerGenerated + " compiler-generated");
        }

        return string.Join(", ", doors);
    }

    private static bool IsRegistry(Type type)
    {
        if (type.IsEnum || !type.IsAbstract || !type.IsSealed
            || type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
        {
            return false;
        }

        var stringConstants = type.GetFields(Everything)
            .Count(static field => field.IsLiteral && field.FieldType == typeof(string));
        return RegistryCollections(type).Any() || stringConstants >= 2;
    }

    private static IEnumerable<MemberInfo> RegistryCollections(Type type)
    {
        foreach (var field in type.GetFields(Everything)
                     .Where(static field => field.IsStatic && field.IsInitOnly)
                     .Where(static field =>
                         !field.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
                     .OrderBy(static field => field.Name, StringComparer.Ordinal))
        {
            if (Collects(field.FieldType))
            {
                yield return field;
            }
        }

        foreach (var property in type.GetProperties(Everything)
                     .Where(static property => (property.GetMethod ?? property.SetMethod)!.IsStatic)
                     .Where(static property => property.GetMethod is not null)
                     .Where(static property => property.GetIndexParameters().Length == 0)
                     .OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            if (Collects(property.PropertyType))
            {
                yield return property;
            }
        }
    }

    private static bool Collects(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    private static IEnumerable<string> RegistryMembers(Type type)
    {
        foreach (var member in RegistryCollections(type))
        {
            yield return member.Name + "=" + Count(member);
        }

        foreach (var constant in type.GetFields(Everything)
                     .Where(static field => field.IsLiteral && field.FieldType == typeof(string))
                     .OrderBy(static field => field.Name, StringComparer.Ordinal))
        {
            yield return "const " + constant.Name;
        }
    }

    private static string Count(MemberInfo member)
    {
        object? value;
        try
        {
            value = member is FieldInfo field
                ? field.GetValue(null)
                : ((PropertyInfo)member).GetValue(null);
        }
        catch (Exception)
        {
            return "unreadable";
        }

        return value switch
        {
            null => "null",
            ICollection collection => collection.Count.ToString(),
            IEnumerable sequence => sequence.Cast<object>().Count().ToString(),
            _ => "unreadable",
        };
    }

    private static IEnumerable<Type> AllTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            // A type this sweep cannot load is a type it cannot pin, and swallowing that silently
            // would shrink the census without saying so. The loadable ones are still returned, and
            // the pin is over every entry, so a type that disappears fails it rather than passing.
            return exception.Types.Where(static type => type is not null).Select(static type => type!);
        }
    }
}
