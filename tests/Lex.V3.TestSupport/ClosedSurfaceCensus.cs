using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Lex.V3.TestSupport;

/// <summary>
/// Sweeps whole assemblies for the shapes this repository treats as closed: a vocabulary whose
/// members are the contract, a type whose construction is restricted to a named door, and a static
/// registry of tokens. Each sweep answers "what is there", so a test can pin the answer, and
/// <see cref="Candidates"/> answers "what did the three sweeps have to account for", so a test can
/// pin the partition rather than trusting three array lengths.
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
/// has, and by no clause that its own description does not state. No sweep is narrowed by the list
/// its caller is about to compare it against. Both mistakes have been made here. The self-narrowed
/// sweep is silent: filter an assembly scan through the names you expect and it can only return
/// names you expect. The undocumented clause is worse, because the documentation then reads as
/// coverage it does not have. This file once excluded <c>IsAbstract</c> while saying its rule was
/// "declares constructors, none of them public", and nine abstract closed-union bases sat outside
/// the census and outside its residual at the same time. Before adding a clause below, ask what
/// addition to an assembly it would hide, and whether the summary above the method still describes
/// the code under it.
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
    /// can see it. Members are in <see cref="Enum.GetNames(Type)"/> order, which is by underlying
    /// value, so a renumbering that reorders members moves a row and one that preserves the order
    /// does not.
    /// </summary>
    public static IReadOnlyList<string> ClosedVocabularies(params string[] assemblies) =>
        Load(assemblies)
            .SelectMany(AllTypes)
            .Where(static type => type.IsEnum)
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .Select(static type => type.FullName + ": " + string.Join(", ", Enum.GetNames(type)))
            .ToArray();

    /// <summary>
    /// Every type in <paramref name="assemblies"/> that declares at least one constructor and none
    /// of them public, as <c>full name: hand-out, hand-out</c>, ordered by full name. Each hand-out
    /// is <see cref="ConstructionSurface.HandOuts"/>'s own entry, which already stops before the
    /// parameter list, plus a count of the compiler-generated ones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A type with no public constructor is a type whose author decided a caller must come through
    /// a named door. That is a property of the type, readable without knowing what the type is for,
    /// which is what makes it sweepable; "is guarded" as a judgement about intent is not.
    /// </para>
    /// <para>
    /// Abstract types are in. An abstract base with a private protected constructor is the closed
    /// union shape this repository uses most, and that constructor is the door every subtype comes
    /// through, so leaving it out left the most tightly guarded types in the repository uncounted.
    /// Interfaces and enums declare no instance constructor, so they never match and need no clause
    /// of their own; the clauses that once excluded them made the rule read narrower than it was.
    /// </para>
    /// <para>
    /// State plainly what the entries cost. They carry no parameter list, so this catches a door
    /// added, removed, renamed, moved to another type or given a different scope, and it does not
    /// catch an existing door's parameters changing. The exact per-type pins built on
    /// <see cref="ConstructionSurface.Of"/> catch that, for the types that have one. Overloads
    /// survive, because <see cref="ConstructionSurface.HandOuts"/> deduplicates on the full entry
    /// before the parameters are dropped, so a second <c>Create</c> is a second entry.
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
    /// <c>full name: collection=count, const Name, static readonly Name, static property Name</c>,
    /// ordered by full name. A registry is a static class with at least one static collection
    /// member, or with two or more string tokens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A token is a string a reader sees, and the member carrying it may be <c>const</c>, or
    /// <c>static readonly</c>, or a readable static property, meaning one with a getter whether or
    /// not it also has a setter. Which one is a storage decision, not a contract decision. Rendering only <c>const</c> was a hole with a measured shape: a
    /// <c>public static readonly string</c> added to a schema-id table passed the whole suite,
    /// while the same token declared <c>const</c> failed it.
    /// </para>
    /// <para>
    /// A collection's element count is the pinned part, not its contents: reading the contents of
    /// every registry in an assembly would pin a large amount of publisher text a second time, in a
    /// place nobody would think to update it. A token added to or removed from a collection moves
    /// the count. A token swapped for another does not, and the registry's own tests are the
    /// control for that. Token members are pinned by name, not by value, for the same reason. A
    /// member whose static initializer throws is reported as <c>unreadable</c> rather than skipped,
    /// because a member that quietly leaves a sweep is the failure this file exists to prevent.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> VocabularyRegistries(params string[] assemblies) =>
        Load(assemblies)
            .SelectMany(AllTypes)
            .Select(static type => (Type: type, Shape: RegistryShape(type)))
            .Where(static found => found.Shape.IsRegistry)
            .OrderBy(static found => found.Type.FullName, StringComparer.Ordinal)
            .Select(static found =>
                found.Type.FullName + ": " + string.Join(", ", Render(found.Shape)))
            .ToArray();

    /// <summary>
    /// Every type in <paramref name="assemblies"/> that any of the three sweeps has to account for:
    /// a closed vocabulary, a construction-restricted type, or a static class holding state.
    /// Ordered by full name.
    /// </summary>
    /// <remarks>
    /// This is the denominator, in code rather than in a commit message. A caller pins it against
    /// the three sweeps plus a declared list of types it has decided not to pin, so a type in none
    /// of the four is a failure rather than a silence. Without it, a static class holding state
    /// that is not a token registry moves nothing at all, which is how a residual stated once in
    /// prose stops being true without anybody noticing.
    /// </remarks>
    public static IReadOnlyList<string> Candidates(params string[] assemblies) =>
        Load(assemblies)
            .SelectMany(AllTypes)
            .Where(static type =>
                type.IsEnum || ConstructionIsRestricted(type) || HoldsStaticState(type))
            .Select(static type => type.FullName!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The rows rendered as C# array-element source, wrapped and escaped exactly as the pins are
    /// written, so re-transcribing a pin is pasting a printed block rather than editing by hand.
    /// </summary>
    /// <remarks>
    /// Print this from a throwaway test and paste the result between the braces of the failing
    /// <c>new[]</c>. Never call it from the test that does the comparing: an expected side rendered
    /// from the sweep agrees with the sweep by construction, and that is the one thing a pin must
    /// not do.
    /// </remarks>
    public static string RenderForTranscription(
        IReadOnlyList<string> rows, int indent = 16, int limit = 100)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var pad = new string(' ', indent);
        var continuation = new string(' ', indent + 4);
        var text = new StringBuilder();
        foreach (var row in rows)
        {
            var rest = row.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
            var lines = new List<string>();
            while (rest.Length > 0)
            {
                var prefix = lines.Count == 0 ? pad + "\"" : continuation + "+ \"";
                var budget = limit - prefix.Length - 1;
                if (rest.Length <= budget)
                {
                    lines.Add(prefix + rest + "\"");
                    break;
                }

                var afterComma = rest[..Math.Min(rest.Length, budget + 2)]
                    .LastIndexOf(", ", StringComparison.Ordinal);
                int take;
                if (afterComma > 0)
                {
                    take = afterComma + 2;
                }
                else
                {
                    var space = rest[..Math.Min(rest.Length, budget)].LastIndexOf(' ');
                    take = space > 0 ? space + 1 : budget;
                }

                lines.Add(prefix + rest[..take] + "\"");
                rest = rest[take..];
            }

            lines[^1] += ",";
            foreach (var line in lines)
            {
                text.Append(line).Append('\n');
            }
        }

        return text.ToString();
    }

    private static IEnumerable<Assembly> Load(string[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        return assemblies.Select(static name => Assembly.Load(new AssemblyName(name)));
    }

    /// <summary>
    /// True when the type declares at least one constructor and none of them is public. There is no
    /// other clause: see the class remarks on why a clause the summary does not state is the defect
    /// this file exists to remove.
    /// </summary>
    private static bool ConstructionIsRestricted(Type type)
    {
        var declared = type.GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        return declared.Length > 0 && !declared.Any(static constructor => constructor.IsPublic);
    }

    /// <summary>
    /// True when the type is a static class the compiler did not generate and it declares any
    /// constant, any static field or any static property. Deliberately wider than
    /// <see cref="VocabularyRegistries"/>, because this is the denominator a caller partitions.
    /// </summary>
    private static bool HoldsStaticState(Type type)
    {
        if (!IsStaticClass(type))
        {
            return false;
        }

        // IsStatic alone. A literal field is always static, so an IsLiteral disjunct here would be
        // dead, which is the same shape this file carried in ConstructionIsRestricted one cycle ago.
        return type.GetFields(Everything).Any(static field => field.IsStatic)
            || type.GetProperties(Everything)
                .Any(static property => (property.GetMethod ?? property.SetMethod)!.IsStatic);
    }

    private static bool IsStaticClass(Type type) =>
        !type.IsEnum && type.IsAbstract && type.IsSealed
        && !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private static string Doors(Assembly assembly, Type guarded)
    {
        var surface = ConstructionSurface.HandOuts(assembly, guarded);
        var doors = surface.Declared.ToList();
        if (surface.CompilerGenerated > 0)
        {
            doors.Add(surface.CompilerGenerated + " compiler-generated");
        }

        return string.Join(", ", doors);
    }

    private readonly record struct RegistryShapeOf(
        IReadOnlyList<MemberInfo> Collections,
        IReadOnlyList<(MemberInfo Member, string Kind)> Tokens)
    {
        internal bool IsRegistry => Collections.Count > 0 || Tokens.Count >= 2;
    }

    /// <summary>
    /// The registry members of a type, with the reflection walked once. Collections are static
    /// readonly fields and readable static properties whose type is a non-string sequence; tokens
    /// are string constants, static readonly strings and readable static string properties.
    /// Readable means the property has a getter; one that also has a setter is still admitted,
    /// because a settable static property is mutable state and a registry that can be reassigned at
    /// run time is the more interesting case rather than the less.
    /// </summary>
    private static RegistryShapeOf RegistryShape(Type type)
    {
        if (!IsStaticClass(type))
        {
            return new([], []);
        }

        var collections = new List<MemberInfo>();
        var tokens = new List<(MemberInfo, string)>();

        foreach (var field in type.GetFields(Everything)
                     .Where(static field => !field.IsDefined(
                         typeof(CompilerGeneratedAttribute), inherit: false))
                     .OrderBy(static field => field.Name, StringComparer.Ordinal))
        {
            if (field.FieldType == typeof(string))
            {
                if (field.IsLiteral)
                {
                    tokens.Add((field, "const"));
                }
                else if (field.IsStatic && field.IsInitOnly)
                {
                    tokens.Add((field, "static readonly"));
                }
            }
            else if (field.IsStatic && field.IsInitOnly && Collects(field.FieldType))
            {
                collections.Add(field);
            }
        }

        foreach (var property in type.GetProperties(Everything)
                     .Where(static property => (property.GetMethod ?? property.SetMethod)!.IsStatic)
                     .Where(static property => property.GetMethod is not null)
                     .Where(static property => property.GetIndexParameters().Length == 0)
                     .OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            if (property.PropertyType == typeof(string))
            {
                tokens.Add((property, "static property"));
            }
            else if (Collects(property.PropertyType))
            {
                collections.Add(property);
            }
        }

        return new(collections, tokens);
    }

    private static IEnumerable<string> Render(RegistryShapeOf shape)
    {
        foreach (var member in shape.Collections)
        {
            yield return member.Name + "=" + Count(member);
        }

        foreach (var (member, kind) in shape.Tokens)
        {
            yield return kind + " " + member.Name;
        }
    }

    private static bool Collects(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

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
