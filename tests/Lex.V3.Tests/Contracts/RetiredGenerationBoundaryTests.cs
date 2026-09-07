using System.Reflection;
using System.Text.Json.Serialization;

namespace Lex.V3.Tests.Contracts;

/// <summary>
/// Makes S1-A07 a checked property of the complete V3 line: no V2 answer/refusal envelope reader,
/// serializer, shim, alias, fixture or compatibility test enters any production or test assembly.
/// </summary>
/// <remarks>
/// The sweep uses compiled identifiers rather than source text, so historical discussion in comments
/// and hostile values remain admissible evidence.
/// </remarks>
[TestClass]
public sealed class RetiredGenerationBoundaryTests
{
    private static readonly (string ProjectDirectory, string AssemblyName)[] SweptAssemblies =
    [
        ("src/Lex.V3.Api", "Lex.V3.Api"),
        ("src/Lex.V3.Artifacts", "Lex.V3.Artifacts"),
        ("src/Lex.V3.ContractTool", "Lex.V3.ContractTool"),
        ("src/Lex.V3.Contracts", "Lex.V3.Contracts"),
        ("src/Lex.V3.Custody.Azure", "Lex.V3.Custody.Azure"),
        ("src/Lex.V3.Custody.Probe", "Lex.V3.Custody.Probe"),
        ("src/Lex.V3.Ingest", "Lex.V3.Ingest"),
        ("src/Lex.V3.Preview", "Lex.V3.Preview"),
        ("tests/Lex.V3.Ingest.Tests", "Lex.V3.Ingest.Tests"),
        ("tests/Lex.V3.Tests", "Lex.V3.Tests"),
    ];

    [TestMethod]
    public void NoRetiredAnswerOrRefusalCompatibilitySurfaceEntersTheLine()
    {
        var assemblies = LoadSweptAssemblies();
        CollectionAssert.AreEqual(
            SweptAssemblies.Select(static item => item.AssemblyName).Order().ToArray(),
            assemblies.Select(static assembly => assembly.GetName().Name!).Order().ToArray(),
            "the guard must inspect exactly all eight production and both test assemblies");

        var identifiers = assemblies
            .SelectMany(CollectRetiredIdentifiers)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.IsEmpty(identifiers,
            "a V2 identifier entered the compiled V3 line: " + string.Join(" | ", identifiers));
    }

    [TestMethod]
    public void NoRetiredFixtureOrSourcePathEntersTheLine()
    {
        Assert.IsTrue(ContainsRetiredPathSegment("tests/Fixtures/v2-answer-envelope.json"),
            "the path predicate must detect a representative forbidden fixture");
        Assert.IsFalse(ContainsRetiredPathSegment("tests/Fixtures/current-answer-envelope.json"),
            "the path predicate must not reject an ordinary V3 fixture");

        var root = FindRepositoryRoot();
        var offenders = new[] { "src", "tests" }
            .SelectMany(directory => Directory.EnumerateFiles(
                Path.Combine(root, directory), "*", SearchOption.AllDirectories))
            .Where(path => !HasGeneratedSegment(Path.GetRelativePath(root, path)))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(ContainsRetiredPathSegment)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.IsEmpty(offenders,
            "a V2-named source, fixture or compatibility-test path entered the V3 line: "
                + string.Join(", ", offenders));
    }

    private static Assembly[] LoadSweptAssemblies()
    {
        var root = FindRepositoryRoot();
        var targetDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var targetFramework = targetDirectory.Name;
        var configuration = targetDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Cannot determine the test build configuration.");
        var executing = Assembly.GetExecutingAssembly();

        return SweptAssemblies.Select(item =>
        {
            if (string.Equals(item.AssemblyName, executing.GetName().Name, StringComparison.Ordinal))
            {
                return executing;
            }

            var path = Path.Combine(
                root,
                item.ProjectDirectory.Replace('/', Path.DirectorySeparatorChar),
                "bin",
                configuration,
                targetFramework,
                item.AssemblyName + ".dll");
            Assert.IsTrue(File.Exists(path),
                $"the complete solution must build {item.AssemblyName} before the S1-A07 sweep: {path}");
            return Assembly.LoadFrom(path);
        }).ToArray();
    }

    private static IEnumerable<string> CollectRetiredIdentifiers(Assembly assembly)
    {
        var assemblyName = assembly.GetName().Name!;
        Assert.IsFalse(ContainsRetiredMarker(assemblyName), $"assembly name is a V2 identifier: {assemblyName}");
        Assert.IsGreaterThan(0, assembly.GetTypes().Length,
            $"the reflection sweep reached no types in {assemblyName}");

        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            if (ContainsRetiredMarker(reference.Name))
            {
                yield return $"{assemblyName}:reference:{reference.Name}";
            }
        }

        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var type in assembly.GetTypes())
        {
            if (ContainsRetiredMarker(type.FullName))
            {
                yield return $"{assemblyName}:{type.FullName}";
            }

            foreach (var value in AttributeStrings(type.CustomAttributes))
            {
                if (ContainsRetiredMarker(value))
                {
                    yield return $"{assemblyName}:{type.FullName} attribute:{value}";
                }
            }

            foreach (var member in type.GetMembers(declared))
            {
                if (ContainsRetiredMarker(member.Name))
                {
                    yield return $"{assemblyName}:{type.FullName}.{member.Name}";
                }

                foreach (var value in AttributeStrings(member.CustomAttributes))
                {
                    if (ContainsRetiredMarker(value))
                    {
                        yield return $"{assemblyName}:{type.FullName}.{member.Name} attribute:{value}";
                    }
                }

                if (member is MethodBase method)
                {
                    foreach (var parameter in method.GetParameters())
                    {
                        if (ContainsRetiredMarker(parameter.Name))
                        {
                            yield return $"{assemblyName}:{type.FullName}.{member.Name} parameter:{parameter.Name}";
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<string> AttributeStrings(IEnumerable<CustomAttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeType.Namespace != typeof(JsonPropertyNameAttribute).Namespace)
            {
                continue;
            }

            foreach (var argument in attribute.ConstructorArguments.Concat(
                         attribute.NamedArguments.Select(static value => value.TypedValue)))
            {
                foreach (var value in AttributeStrings(argument))
                {
                    yield return value;
                }
            }
        }
    }

    private static IEnumerable<string> AttributeStrings(CustomAttributeTypedArgument argument)
    {
        if (argument.Value is string text)
        {
            yield return text;
            yield break;
        }

        if (argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> values)
        {
            foreach (var value in values)
            {
                foreach (var attributeText in AttributeStrings(value))
                {
                    yield return attributeText;
                }
            }
        }
    }

    private static bool ContainsRetiredMarker(string? value) =>
        value?.Contains("v2", StringComparison.OrdinalIgnoreCase) == true;

    private static bool ContainsRetiredPathSegment(string path) =>
        path.Split('/', '\\').Any(ContainsRetiredMarker);

    private static bool HasGeneratedSegment(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj" or "TestResults");

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "Lex.V3.slnx")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Cannot locate the V3 repository root.");
    }
}
