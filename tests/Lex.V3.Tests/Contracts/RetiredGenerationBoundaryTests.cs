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

    /// <summary>
    /// One constant string value that carries the retired-generation marker for a reason that is not
    /// the retired generation, named exactly rather than pattern-matched.
    /// </summary>
    /// <param name="Value">
    /// The exact value. Not a prefix and not a contains: a later edit that widens the string stops
    /// matching this row and is refused, which is the difference between an exemption and a hole.
    /// </param>
    private readonly record struct RetiredValueExemption(
        string AssemblyName, string TypeFullName, string FieldName, string Value, string Reason);

    /// <summary>
    /// Every constant value allowed to carry the marker, with the reason a person keeps true.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both rows below mean the SECOND VERSION OF A V3 ARTIFACT, not the retired generation this
    /// clause forbids. The collision is real and it has already cost something: the identifier half
    /// of this guard forced a pin named <c>PlanV2Sha256</c> to be renamed <c>CurrentPlanSha256</c>,
    /// which is a weaker name for a value whose whole job is to be exact, and "current" will be
    /// ambiguous the day a third plan generation exists.
    /// </para>
    /// <para>
    /// The identifier rule stays blunt on purpose -- a member NAMED for the retired generation is
    /// refused with no exemption available. What this list buys is that a V3-internal generation
    /// token in a VALUE is a reviewed row carrying its reason, rather than a rename that loses
    /// precision or a sweep narrowed until it stops seeing anything.
    /// </para>
    /// </remarks>
    private static readonly RetiredValueExemption[] AllowedRetiredValues =
    [
        new(
            "Lex.V3.Contracts",
            "Lex.V3.Contracts.Source.Europe.EuFormexManifestationDiscoveryPlan",
            "PartitionKeyPrefix",
            "eu-formex-manifestations-by-expression-v2-",
            "the second generation of a V3 Formex manifestation discovery plan; the token versions "
                + "this repository's own partition key, and the plan's identity schema reads "
                + "plan/2. Pinned by EuFormexManifestationDiscoveryPlanTests."),
        new(
            "Lex.V3.Ingest.Tests",
            "Lex.V3.Ingest.Tests.EuFormexManifestationCanary",
            "EnableVariable",
            "LEX_FORMEX_MANIFESTATION_CANARY_V2",
            "the activation gate for that same second-generation plan's canary. The fresh suffix is "
                + "what stops the spent first-generation authorization from activating a changed "
                + "publisher question, so the token is load-bearing rather than incidental."),
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

    /// <summary>
    /// The value half of the same clause: no constant string carries the retired-generation marker
    /// except the named exemptions.
    /// </summary>
    /// <remarks>
    /// Deliberately a separate test from the identifier sweep rather than another condition folded
    /// into it. One assertion proving two rules cannot say which of them a failure broke, and a
    /// rule added to an existing composite is a rule nobody can delete a clause of and notice.
    /// </remarks>
    [TestMethod]
    public void NoRetiredConstantValueEntersTheLineExceptNamedExemptions()
    {
        var allowed = AllowedRetiredValues
            .Select(static exemption => Key(
                exemption.AssemblyName, exemption.TypeFullName, exemption.FieldName, exemption.Value))
            .ToHashSet(StringComparer.Ordinal);

        var offenders = LoadSweptAssemblies()
            .SelectMany(CollectRetiredConstantValues)
            .Where(found => !allowed.Contains(found))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.IsEmpty(offenders,
            "a constant naming the retired generation entered the compiled V3 line: "
                + string.Join(" | ", offenders));
    }

    /// <summary>
    /// Every exemption still names something that exists, and the sweep that would find it actually
    /// reaches constant values at all.
    /// </summary>
    /// <remarks>
    /// This is the reach control and the staleness control in one, and without it the test above is
    /// worthless: a sweep that silently reached zero fields would report zero offenders and pass. It
    /// also refuses an exemption left behind for something long deleted, which is how an allowlist
    /// quietly becomes a hole nobody re-reads.
    /// </remarks>
    [TestMethod]
    public void EveryNamedValueExemptionIsStillPresentInTheCompiledLine()
    {
        var found = LoadSweptAssemblies()
            .SelectMany(CollectRetiredConstantValues)
            .ToHashSet(StringComparer.Ordinal);

        var stale = AllowedRetiredValues
            .Where(exemption => !found.Contains(Key(
                exemption.AssemblyName, exemption.TypeFullName, exemption.FieldName, exemption.Value)))
            .Select(static exemption => $"{exemption.TypeFullName}.{exemption.FieldName}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.IsEmpty(stale,
            "an exemption names a constant this line no longer carries, so nothing it says is "
                + "still being checked: " + string.Join(" | ", stale));
        Assert.AreEqual(2, AllowedRetiredValues.Length,
            "the exemptions are pinned by count as well as by row, so one added without review "
                + "fails here rather than passing as one more line in a list.");
    }

    /// <summary>
    /// An exemption covers one exact value, so widening the constant later stops matching it.
    /// </summary>
    /// <remarks>
    /// The rule this states cannot be driven by a real constant, because a hostile one would have to
    /// be committed to a production assembly to be swept, and committing it is the thing the guard
    /// exists to prevent. So it is stated against the key the sweep and the exemptions both use: a
    /// key that dropped the value would let an exemption cover any future value of that same field,
    /// which is precisely the hole an allowlist is supposed not to be.
    /// </remarks>
    [TestMethod]
    public void AnExemptionDoesNotCoverADifferentValueOfTheSameConstant()
    {
        var allowed = AllowedRetiredValues
            .Select(static exemption => Key(
                exemption.AssemblyName, exemption.TypeFullName, exemption.FieldName, exemption.Value))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var exemption in AllowedRetiredValues)
        {
            Assert.IsTrue(
                allowed.Contains(Key(
                    exemption.AssemblyName,
                    exemption.TypeFullName,
                    exemption.FieldName,
                    exemption.Value)),
                $"{exemption.FieldName} must be covered for the value it actually carries.");
            Assert.IsFalse(
                allowed.Contains(Key(
                    exemption.AssemblyName,
                    exemption.TypeFullName,
                    exemption.FieldName,
                    exemption.Value + "-and-something-else")),
                $"{exemption.FieldName} must NOT be covered for a value it does not carry.");
        }
    }

    /// <summary>
    /// Every constant string field in <paramref name="assembly"/> whose VALUE carries the marker,
    /// keyed the same way the exemptions are.
    /// </summary>
    private static IEnumerable<string> CollectRetiredConstantValues(Assembly assembly)
    {
        var assemblyName = assembly.GetName().Name!;
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var type in assembly.GetTypes())
        {
            foreach (var field in type.GetFields(declared))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string))
                {
                    continue;
                }

                if (field.GetRawConstantValue() is string value && ContainsRetiredMarker(value))
                {
                    yield return Key(assemblyName, type.FullName!, field.Name, value);
                }
            }
        }
    }

    private static string Key(string assemblyName, string typeFullName, string fieldName, string value) =>
        $"{assemblyName}:{typeFullName}.{fieldName} = {value}";

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
