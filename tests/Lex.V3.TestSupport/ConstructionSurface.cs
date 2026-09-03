using System.Reflection;

namespace Lex.V3.TestSupport;

/// <summary>
/// Enumerates every way an instance of a guarded type can come into existence or be handed out,
/// so a test pins the whole surface rather than the doors its author thought of.
/// </summary>
/// <remarks>
/// <para>
/// A producer is any member that can yield the guarded type or a subtype of it: a constructor of
/// the type, of a subtype, or a non-private constructor of a base type that a new subtype could
/// reach; a method whose return type carries the guarded type, or whose parameter list carries it
/// by reference (<c>out</c> or <c>ref</c>); a field or property whose type carries it; a
/// conversion operator. "Carries" looks through by-ref, arrays, pointers and generic arguments at
/// any depth, so <c>Task&lt;T&gt;</c>, <c>(T, int)</c>, <c>IEnumerable&lt;T&gt;</c>,
/// <c>Lazy&lt;T&gt;</c>, <c>Func&lt;T&gt;</c> and <c>T[]</c> all count.
/// </para>
/// <para>
/// Every scan uses the full binding flags, walks nested types transitively, and walks the base
/// chain, because a guard that omits one scope has already been wrong four times in this
/// repository. Each entry is pinned as kind, scope, static or instance, declaring type, name,
/// parameter types and return type, never as a bare name.
/// </para>
/// <para>
/// What this guard cannot see, so that signature coverage is not mistaken for total coverage: it
/// reads signatures only. A method declared as returning <c>object</c>, or an interface the
/// guarded type does not implement, that happens to return a guarded instance is invisible to it,
/// as are <c>RuntimeHelpers.GetUninitializedObject</c> and <c>Activator</c> against a private
/// constructor. Closing those needs a scan of method bodies for <c>newobj</c> and call tokens
/// against the producers, which is a different tool with a different failure mode and is not part
/// of this one.
/// </para>
/// </remarks>
public static class ConstructionSurface
{
    private const BindingFlags Everything =
        BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Every producer declared on the guarded type itself, on its nested types (transitively),
    /// and on its base types. Sorted ordinally so the list can be pinned.
    /// </summary>
    public static IReadOnlyList<string> Of(Type guarded)
    {
        ArgumentNullException.ThrowIfNull(guarded);
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in SelfNestedAndBases(guarded))
        {
            var compilerGenerated = IsCompilerGenerated(type);
            foreach (var producer in ProducersDeclaredOn(type, guarded, includeBaseConstructors: true))
            {
                // Same rule as the assembly sweep, and for the same measured reason: a field on a
                // compiler-generated type nested here is the compiler's storage for a captured
                // local, it exists only when that local is live across an await, and Debug hoists
                // them all. CompilerGeneratedHolders asserts them collectively. Methods and
                // constructors on those types stay exact, because a lambda returning the guarded
                // type is a real door.
                if (compilerGenerated && producer.Entry.StartsWith("field ", StringComparison.Ordinal))
                {
                    continue;
                }

                found.Add(producer.Entry);
            }
        }

        return found.ToArray();
    }

    /// <summary>
    /// Every producer declared on any type in <paramref name="assembly"/> outside the guarded
    /// type's own hierarchy (its nested types and its bases are covered by <see cref="Of"/>).
    /// This is what a scan of the guarded type alone can never see: a factory on an unrelated
    /// type, a subtype declared elsewhere, a static holder. With <paramref name="includeNonPublic"/>
    /// false only producers reachable from outside the assembly remain: a public member whose every
    /// enclosing type is public, since a public method on an internal type is internal in effect.
    /// </summary>
    /// <summary>
    /// True when the declaring type is compiler generated, tested by attribute rather than by
    /// name so that no mangled spelling is ever trusted or skipped.
    /// </summary>
    private static bool IsCompilerGenerated(Type type) =>
        type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false);

    /// <summary>
    /// Fields of the guarded type declared on compiler-generated types: the storage a lambda
    /// closure or an async state machine uses for a captured local or for <c>this</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are asserted collectively rather than pinned by name, and the reason is measured. The
    /// compiler hoists an async local into a state-machine field only when it is live across an
    /// await; Debug hoists them all. So an exact pin of these fields drifts with the build
    /// configuration, and every local receipt in this repository was a Debug run while CI builds
    /// Release. That is how a green suite reached a red CI.
    /// </para>
    /// <para>
    /// Methods and constructors on compiler-generated types are <em>not</em> treated this way and
    /// stay in <see cref="ProducersIn"/> exactly: a lambda in a display class that returns the
    /// guarded type is a real door, and excluding whole compiler-generated types to fix the drift
    /// would have hidden it.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> CompilerGeneratedHolders(Assembly assembly, Type guarded)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(guarded);
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in AllTypes(assembly).Where(IsCompilerGenerated))
        {
            foreach (var field in type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType == guarded)
                {
                    found.Add($"{(field.IsStatic ? "static" : "instance")} {(field.IsPublic ? "public" : "non-public")} {type.FullName}");
                }
            }
        }

        return found.ToArray();
    }

    public static IReadOnlyList<string> ProducersIn(Assembly assembly, Type guarded, bool includeNonPublic)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(guarded);
        var own = SelfNestedAndBases(guarded).ToHashSet();
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in AllTypes(assembly))
        {
            if (own.Contains(type))
            {
                continue;
            }

            var reachable = ReachableFromOutside(type);
            var compilerGenerated = IsCompilerGenerated(type);
            foreach (var producer in ProducersDeclaredOn(type, guarded, includeBaseConstructors: false))
            {
                // A field on a compiler-generated type is storage, not a door, and it drifts with
                // the build configuration. It is asserted collectively by CompilerGeneratedHolders
                // instead. Methods and constructors on the same type stay here exactly.
                if (compilerGenerated && producer.Entry.StartsWith("field ", StringComparison.Ordinal))
                {
                    continue;
                }

                if (includeNonPublic || (producer.PublicMember && reachable))
                {
                    found.Add(producer.Entry);
                }
            }
        }

        return found.ToArray();
    }

    /// <summary>
    /// Every member declared on the type and its nested types, transitively, with the full
    /// binding flags. For guards about parameters or names rather than production.
    /// </summary>
    public static IReadOnlyList<MemberInfo> DeclaredMembersTransitive(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return SelfAndNested(type)
            .SelectMany(static t => t.GetMembers(Everything))
            .ToArray();
    }

    /// <summary>True when a value of <paramref name="type"/> can hold the guarded type.</summary>
    public static bool Carries(Type type, Type guarded)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(guarded);
        if (type == guarded || guarded.IsAssignableFrom(type))
        {
            return true;
        }

        if (type.IsByRef || type.IsArray || type.IsPointer)
        {
            var element = type.GetElementType();
            return element is not null && Carries(element, guarded);
        }

        if (type.IsGenericType)
        {
            return type.GetGenericArguments().Any(argument => Carries(argument, guarded));
        }

        return false;
    }

    private static bool ReachableFromOutside(Type type)
    {
        for (var enclosing = type; enclosing is not null; enclosing = enclosing.DeclaringType)
        {
            if (!enclosing.IsPublic && !enclosing.IsNestedPublic)
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<Producer> ProducersDeclaredOn(Type type, Type guarded, bool includeBaseConstructors)
    {
        var isGuardedOrSubtype = guarded.IsAssignableFrom(type);
        var isBase = !isGuardedOrSubtype && type.IsAssignableFrom(guarded) && type != typeof(object);

        foreach (var constructor in type.GetConstructors(Everything))
        {
            if (isGuardedOrSubtype)
            {
                yield return new(Describe("constructor", constructor, type), constructor.IsPublic);
            }
            else if (isBase && includeBaseConstructors && !constructor.IsPrivate)
            {
                // A base constructor that is not private can be reached by a subtype written
                // tomorrow in any assembly the scope admits, so it is a construction path.
                yield return new(Describe("base-constructor", constructor, type), constructor.IsPublic);
            }
        }

        foreach (var method in type.GetMethods(Everything))
        {
            if (method.IsSpecialName &&
                (method.Name.StartsWith("get_", StringComparison.Ordinal) ||
                 method.Name.StartsWith("set_", StringComparison.Ordinal) ||
                 method.Name.StartsWith("add_", StringComparison.Ordinal) ||
                 method.Name.StartsWith("remove_", StringComparison.Ordinal)))
            {
                // Accessors are reported through their property or event below.
                continue;
            }

            var producesByReturn = Carries(method.ReturnType, guarded);
            var producesByRef = method.GetParameters().Any(parameter =>
                parameter.ParameterType.IsByRef && Carries(parameter.ParameterType, guarded));
            if (producesByReturn || producesByRef)
            {
                var kind = method.IsSpecialName && method.Name.StartsWith("op_", StringComparison.Ordinal)
                    ? "operator"
                    : producesByRef && !producesByReturn ? "by-ref-method" : "method";
                yield return new(Describe(kind, method, method.ReturnType), method.IsPublic);
            }
        }

        foreach (var field in type.GetFields(Everything))
        {
            if (Carries(field.FieldType, guarded))
            {
                yield return new(
                    $"field {Scope(field)} {(field.IsStatic ? "static" : "instance")} {Name(type)}::{field.Name} -> {Name(field.FieldType)}",
                    field.IsPublic);
            }
        }

        foreach (var property in type.GetProperties(Everything))
        {
            if (Carries(property.PropertyType, guarded))
            {
                var accessor = property.GetMethod ?? property.SetMethod;
                var scope = accessor is null ? "unknown" : Scope(accessor);
                var isStatic = accessor?.IsStatic == true;
                var parameters = string.Join(", ", property.GetIndexParameters().Select(static p => Name(p.ParameterType)));
                yield return new(
                    $"property {scope} {(isStatic ? "static" : "instance")} {Name(type)}::{property.Name}({parameters}) -> {Name(property.PropertyType)}",
                    accessor?.IsPublic == true);
            }
        }

        foreach (var evt in type.GetEvents(Everything))
        {
            if (evt.EventHandlerType is not null && Carries(evt.EventHandlerType, guarded))
            {
                yield return new(
                    $"event {Name(type)}::{evt.Name} -> {Name(evt.EventHandlerType)}",
                    evt.AddMethod?.IsPublic == true);
            }
        }
    }

    private readonly record struct Producer(string Entry, bool PublicMember);

    private static string Describe(string kind, MethodBase member, Type produced)
    {
        var parameters = string.Join(", ", member.GetParameters().Select(static p =>
            (p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : string.Empty) + Name(p.ParameterType)));
        var declaring = member.DeclaringType is null ? "?" : Name(member.DeclaringType);
        return $"{kind} {Scope(member)} {(member.IsStatic ? "static" : "instance")} {declaring}::{member.Name}({parameters}) -> {Name(produced)}";
    }

    private static string Scope(MethodBase member) =>
        member.IsPublic ? "public"
        : member.IsPrivate ? "private"
        : member.IsFamilyAndAssembly ? "private-protected"
        : member.IsAssembly ? "internal"
        : member.IsFamily ? "protected"
        : member.IsFamilyOrAssembly ? "protected-internal"
        : "unknown";

    private static string Scope(FieldInfo field) =>
        field.IsPublic ? "public"
        : field.IsPrivate ? "private"
        : field.IsFamilyAndAssembly ? "private-protected"
        : field.IsAssembly ? "internal"
        : field.IsFamily ? "protected"
        : field.IsFamilyOrAssembly ? "protected-internal"
        : "unknown";

    private static string Name(Type type)
    {
        if (type.IsByRef || type.IsArray || type.IsPointer)
        {
            var element = type.GetElementType();
            var suffix = type.IsByRef ? "&" : type.IsArray ? "[]" : "*";
            return (element is null ? type.Name : Name(element)) + suffix;
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition().FullName ?? type.Name;
            var tick = definition.IndexOf('`');
            var head = tick < 0 ? definition : definition[..tick];
            return head + "<" + string.Join(", ", type.GetGenericArguments().Select(Name)) + ">";
        }

        return type.FullName ?? type.Name;
    }

    private static IEnumerable<Type> SelfNestedAndBases(Type guarded)
    {
        foreach (var type in SelfAndNested(guarded))
        {
            yield return type;
        }

        for (var basis = guarded.BaseType; basis is not null && basis != typeof(object); basis = basis.BaseType)
        {
            yield return basis;
        }
    }

    private static IEnumerable<Type> SelfAndNested(Type type)
    {
        yield return type;
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (var deeper in SelfAndNested(nested))
            {
                yield return deeper;
            }
        }
    }

    private static IEnumerable<Type> AllTypes(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types.Where(static t => t is not null).Select(static t => t!).ToArray();
        }

        // GetTypes already includes nested types; the transitive walk is for callers that pass a
        // type rather than an assembly.
        return types;
    }
}
