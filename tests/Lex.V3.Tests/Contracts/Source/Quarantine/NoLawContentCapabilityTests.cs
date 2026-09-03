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
    private static readonly Type[] ForbiddenContentTypes =
    [
        typeof(byte[]),
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

    [TestMethod]
    public void NoQuarantineTypeHasAMemberThatCanCarryFileContentAStreamOrAUri()
    {
        var assembly = typeof(PriorPublicCoordinate).Assembly;
        var quarantineTypes = assembly.GetTypes()
            .Where(type => type.Namespace == typeof(PriorPublicCoordinate).Namespace)
            .ToArray();

        Assert.IsTrue(quarantineTypes.Length >= 6, "the sweep must actually find the quarantine types, not an empty namespace filter");

        const BindingFlags everything =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        var offenders = new List<string>();
        foreach (var type in quarantineTypes)
        {
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

            foreach (var method in type.GetMethods(everything))
            {
                if (IsForbidden(method.ReturnType))
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

        Assert.AreEqual(0, offenders.Count, "content-capable members found: " + string.Join("; ", offenders));
    }

    private static bool IsForbidden(Type type)
    {
        var unwrapped = type.IsByRef || type.IsPointer ? type.GetElementType()! : type;
        if (unwrapped.IsArray)
        {
            unwrapped = unwrapped.GetElementType()!;
        }

        if (ForbiddenContentTypes.Any(forbidden => forbidden == unwrapped || forbidden.IsAssignableFrom(unwrapped)))
        {
            return true;
        }

        // Generic containers (IReadOnlyList<T>, Task<T>, ...) around a forbidden type.
        if (unwrapped.IsGenericType)
        {
            return unwrapped.GetGenericArguments().Any(IsForbidden);
        }

        return false;
    }
}
