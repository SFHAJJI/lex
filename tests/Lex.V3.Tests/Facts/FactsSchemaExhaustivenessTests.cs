using System.Reflection;
using System.Text.Json;
using Lex.V3.Contracts.Facts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Facts;

/// <summary>
/// The exporter and the Facts contracts are a bijection: every schema identity the package
/// declares is exported, and every exported identity names a contract type that exists.
/// </summary>
/// <remarks>
/// <para>
/// This closes a hole found while checking whether lane C's new-files-only constraint was
/// satisfiable at all (ruling
/// <c>lex-event-20260904T174138711Z-88eebc66d3be4283832d54b9d72a9e71</c>).
/// <c>FactsSchemaExporter</c> maps identities to types by hand and nothing enforced that the map
/// was complete, so a new Facts contract added as new files only would have shipped with no
/// exported schema and no test would have said a word. The failure is silent by construction: the
/// existing suite iterates <c>AllSchemaIds</c>, so a type missing from that list is a type no
/// existing test ever looks at.
/// </para>
/// <para>
/// The checks run entirely through the exporter's public surface plus reflection over the
/// contracts assembly, so they do not depend on the private dictionary's shape and keep working if
/// its internals are rewritten.
/// </para>
/// <para>
/// <b>Proven to bite.</b> Adding a schema identity constant with no exporter entry makes
/// <see cref="EverySchemaIdentityConstantIsExported"/> fail, naming the unexported constant. That
/// mutation was applied, watched fail, and reverted before this file was committed.
/// </para>
/// </remarks>
[TestClass]
public sealed class FactsSchemaExhaustivenessTests
{
    /// <summary>Every <c>public const string</c> declared on <see cref="FactsSchemaIds"/>.</summary>
    private static IReadOnlyList<(string Name, string Value)> DeclaredSchemaIdConstants() =>
        typeof(FactsSchemaIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (field.Name, Value: (string)field.GetRawConstantValue()!))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Every type in the Facts contracts namespace declaring a <c>public const string Identity</c>,
    /// which is how a Facts contract states it has a wire schema.
    /// </summary>
    private static IReadOnlyList<(Type Type, string Identity)> ContractTypesDeclaringASchemaIdentity() =>
        typeof(FactsSchemaIds).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "Lex.V3.Contracts.Facts")
            .Select(type => (
                Type: type,
                Field: type.GetField("Identity", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)))
            .Where(entry => entry.Field is { IsLiteral: true } field && field.FieldType == typeof(string))
            .Select(entry => (entry.Type, Identity: (string)entry.Field!.GetRawConstantValue()!))
            .OrderBy(entry => entry.Type.FullName, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The guard is not vacuous: both reflective sweeps must actually find the contracts that are
    /// there. A sweep returning nothing would make every assertion below pass forever.
    /// </summary>
    [TestMethod]
    public void BothReflectiveSweepsFindTheContractsThatExist()
    {
        var constants = DeclaredSchemaIdConstants();
        var contractTypes = ContractTypesDeclaringASchemaIdentity();

        Assert.IsGreaterThanOrEqualTo(8, constants.Count);
        Assert.IsGreaterThanOrEqualTo(7, contractTypes.Count);

        // Named anchors, so a sweep that silently starts matching nothing is caught here rather
        // than passing as an empty universe.
        CollectionAssert.Contains(
            constants.Select(entry => entry.Value).ToArray(),
            FactsSchemaIds.RelationFact);
        CollectionAssert.Contains(
            contractTypes.Select(entry => entry.Type).ToArray(),
            typeof(RelationFact));
    }

    /// <summary>
    /// Every declared schema identity constant is exported. This is the direction that catches a
    /// new Facts contract shipping with no schema.
    /// </summary>
    [TestMethod]
    public void EverySchemaIdentityConstantIsExported()
    {
        var exported = FactsSchemaExporter.AllSchemaIds.ToHashSet(StringComparer.Ordinal);

        var unexported = DeclaredSchemaIdConstants()
            .Where(entry => !exported.Contains(entry.Value))
            .Select(entry => $"{entry.Name} (\"{entry.Value}\")")
            .ToArray();

        Assert.IsEmpty(
            unexported,
            "FactsSchemaIds declares schema identities that FactsSchemaExporter does not export, "
                + "so a contract carrying one would ship with no schema: "
                + string.Join(", ", unexported));
    }

    /// <summary>Every exported identity is a declared constant, so none is a loose string.</summary>
    [TestMethod]
    public void EveryExportedSchemaIdentityIsADeclaredConstant()
    {
        var declared = DeclaredSchemaIdConstants()
            .Select(entry => entry.Value)
            .ToHashSet(StringComparer.Ordinal);

        var undeclared = FactsSchemaExporter.AllSchemaIds
            .Where(schemaId => !declared.Contains(schemaId))
            .ToArray();

        Assert.IsEmpty(undeclared, string.Join(", ", undeclared));
    }

    /// <summary>
    /// Every contract type declaring a schema identity is exported, and the schema the exporter
    /// produces for that identity pins that same identity. This is the half that proves the
    /// exporter's entry names the right type and not merely some type.
    /// </summary>
    [TestMethod]
    public void EveryContractTypeWithASchemaIdentityIsExportedUnderThatIdentity()
    {
        foreach (var (type, identity) in ContractTypesDeclaringASchemaIdentity())
        {
            CollectionAssert.Contains(
                FactsSchemaExporter.AllSchemaIds.ToArray(),
                identity,
                $"{type.Name} declares schema identity \"{identity}\" and the exporter omits it.");

            var bytes = FactsSchemaExporter.ExportUtf8(identity);
            using var document = JsonDocument.Parse(bytes);
            Assert.AreEqual(
                identity,
                document.RootElement
                    .GetProperty("properties")
                    .GetProperty("schema")
                    .GetProperty("const")
                    .GetString(),
                $"The schema exported for {type.Name} must pin {type.Name}'s own identity.");
        }
    }

    /// <summary>
    /// Every exported identity names an existing contract type, except the shared definitions
    /// document, which is a bag of value objects and deliberately has no root contract.
    /// </summary>
    [TestMethod]
    public void EveryExportedSchemaIdentityNamesExactlyOneExistingContractType()
    {
        var byIdentity = ContractTypesDeclaringASchemaIdentity()
            .GroupBy(entry => entry.Identity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var schemaId in FactsSchemaExporter.AllSchemaIds)
        {
            if (string.Equals(schemaId, FactsSchemaIds.FactsCommon, StringComparison.Ordinal))
            {
                Assert.IsFalse(
                    byIdentity.ContainsKey(schemaId),
                    "The common definitions document has no root contract type.");
                continue;
            }

            Assert.IsTrue(
                byIdentity.TryGetValue(schemaId, out var owners),
                $"The exporter exports \"{schemaId}\" and no Facts contract type declares it.");
            Assert.HasCount(
                1,
                owners!,
                $"\"{schemaId}\" is declared by more than one contract type: "
                    + string.Join(", ", owners!.Select(entry => entry.Type.Name)));
        }
    }

    /// <summary>
    /// Every exported identity also resolves a file name and a resource identity, so registration
    /// cannot be half done in one of the three tables.
    /// </summary>
    [TestMethod]
    public void EveryExportedSchemaIdentityResolvesAFileNameAndAResourceIdentity()
    {
        foreach (var schemaId in FactsSchemaExporter.AllSchemaIds)
        {
            Assert.IsFalse(string.IsNullOrEmpty(FactsSchemaExporter.FileNameFor(schemaId)), schemaId);
            Assert.IsFalse(
                string.IsNullOrEmpty(FactsSchemaResourceIds.ForWireSchema(schemaId)),
                schemaId);
        }
    }
}
