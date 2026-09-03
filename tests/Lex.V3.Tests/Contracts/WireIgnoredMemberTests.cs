using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Tests.Contracts.Source.Europe;
using Lex.V3.Tests.Facts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

/// <summary>
/// A computed convenience must be a method, never a property marked <see cref="JsonIgnore"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>ContractJson</c> sets <c>UnmappedMemberHandling.Disallow</c> and every emitted schema sets
/// <c>additionalProperties: false</c>. Both read as a closed wire and neither closes an ignored
/// name, because System.Text.Json treats such a name as mapped and ignored rather than unmapped.
/// A document carrying it was accepted, its value silently discarded, and the object then computed
/// the opposite of what the document asserted: <c>proves_case: true</c> deserialized to an object
/// answering False, and <c>celex_sector: "9"</c> to one answering 3.
/// </para>
/// <para>
/// Three losses in one. A document carrying a false claim was accepted. A store indexing the raw
/// JSON and a store indexing the deserialized object disagreed about the same bytes. And the reader
/// admitted a set its own schema refused, which is the divergence the Cellar families and the
/// persistent identifier were each repaired for, here across the whole contract surface.
/// </para>
/// <para>
/// The rule is structural rather than a list of names, because a list would have to be extended by
/// whoever adds the next computed member, which is the person least likely to know why. A method is
/// invisible to the serializer, so the closed-wire rule that already exists does the work instead of
/// a second mechanism that does not.
/// </para>
/// </remarks>
[TestClass]
public sealed class WireIgnoredMemberTests
{
    /// <summary>
    /// One vector per former wire name, exercised through the runtime reader and, where the type
    /// has one, through the emitted schema.
    /// </summary>
    /// <remarks>
    /// Keyed by declaring type as well as name, because a name is not the unit. Two of these,
    /// <c>durable_blob_ref</c> and <c>transport_byte_sha256</c>, are computed on three observation
    /// types each, and <c>received_encoded_entity_byte_count</c> is computed on the complete
    /// observation while remaining a real wire input on the partial one. A per-name rule would have
    /// refused a legitimate field; the compiler caught that during the repair.
    /// </remarks>
    private sealed record Vector(
        string DeclaringType,
        string Member,
        string WireName,
        string InjectedValue,
        Func<object> Instance,
        string? SchemaFile);

    private static readonly string Digest = new('a', 64);

    private static Vector[] Corpus() =>
    [
        new("OfficialIdentifier", "CelexSector", "celex_sector", "\"9\"",
            static () => new OfficialIdentifier(FactsIdentifierFamily.Celex, "32016R0679"),
            "v3-facts/facts-common.schema.json"),
        new("OfficialIdentifier", "ProvesCase", "proves_case", "true",
            static () => new OfficialIdentifier(FactsIdentifierFamily.Celex, "32016R0679"),
            "v3-facts/facts-common.schema.json"),
        new("OfficialIdentitySet", "IsCase", "is_case", "true",
            static () => FactsFixtures.LuWork(),
            "v3-facts/facts-common.schema.json"),
        new("RelationFact", "CarriedTarget", "carried_target", "{}",
            static () => FactsFixtures.AssertedFact(),
            "v3-facts/relation-fact.schema.json"),
        new("RelationFact", "TargetEcli", "target_ecli", "\"ECLI:EU:C:2019:1\"",
            static () => FactsFixtures.AssertedFact(),
            "v3-facts/relation-fact.schema.json"),
        new("ScopeAccountingSet", "Count", "count", "99",
            static () => new ScopeAccountingSet(ScopeAxis.Record, ScopeDisposition.AcceptedSelected, [1, 2]),
            "v3-source/source-scope-manifest.schema.json"),
        new("EuChannelDisposition", "MayGraduate", "may_graduate", "true",
            static () => new EuChannelDisposition(
                EuChannel.CellarSparqlEndpoint,
                EuChannelAdmission.Admitted,
                "chosen_absent_published_guidance",
                "rule-1",
                Evidence()),
            null),
        new("EuLanguageBodyDisposition", "CarriesBody", "carries_body", "true",
            static () => new EuLanguageBodyDisposition(
                EuOfficialLanguage.French,
                EuLanguageBodyState.BodyCandidate,
                "chosen_absent_published_guidance",
                "rule-1",
                Evidence()),
            null),
        new("EuAcquisitionProfile", "AdmittedChannels", "admitted_channels",
            "[\"cellar_sparql_endpoint\"]",
            static () => EuAcquisitionProfileTests.ProbeProfile(),
            null),
        new("DurableBlobWriteReceipt", "VerifiedAt", "verified_at",
            "\"2026-09-02T00:00:00+00:00\"",
            static () => WriteReceipt(),
            null),
        new("SyntheticSliceScope", "CanonicalDescriptor", "canonical_descriptor", "\"x\"",
            static () => SyntheticSliceScope.CompleteLu,
            "v3-synthetic-preview/synthetic-slice-control.schema.json"),
        new("SyntheticResolveRequestContract", "CanonicalDescriptor", "canonical_descriptor",
            "\"x\"",
            static () => SyntheticResolveRequestContract.V1,
            "v3-synthetic-preview/synthetic-slice-control.schema.json"),
    ];

    private static SourceArtifactRef Evidence() =>
        new("urn:uuid:00000000-0000-4000-8000-000000000011", new string('b', 64));

    [TestMethod]
    public void NoContractPropertyIsMarkedJsonIgnore()
    {
        var offenders = typeof(ContractJson).Assembly
            .GetTypes()
            .SelectMany(static type => type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(static property => property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
                .Select(property => type.Name + "." + property.Name))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.IsEmpty(
            offenders,
            "A JsonIgnore property leaves its own name open on the wire while the emitted schema " +
            "refuses it. Make each of these a method: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The corpus names every member the repair converted, and each is really a method.
    /// </summary>
    /// <remarks>
    /// Without this, deleting a vector would silently reduce coverage and every remaining test
    /// would still pass. The attribute that used to enumerate these is gone by design, so the list
    /// is the record, and it has to be pinned rather than derived from the thing it describes.
    /// </remarks>
    [TestMethod]
    public void TheCorpusCoversEveryConvertedMember()
    {
        var corpus = Corpus();

        foreach (var vector in corpus)
        {
            var type = vector.Instance().GetType();
            Assert.IsNotNull(
                type.GetMethod(vector.Member, Type.EmptyTypes),
                $"{vector.DeclaringType}.{vector.Member} is not a no-argument method");
            Assert.IsNull(
                type.GetProperty(vector.Member),
                $"{vector.DeclaringType}.{vector.Member} is a property again");
        }

        CollectionAssert.AreEqual(
            new[]
            {
                "DurableBlobWriteReceipt.VerifiedAt",
                "EuAcquisitionProfile.AdmittedChannels",
                "EuChannelDisposition.MayGraduate",
                "EuLanguageBodyDisposition.CarriesBody",
                "OfficialIdentifier.CelexSector",
                "OfficialIdentifier.ProvesCase",
                "OfficialIdentitySet.IsCase",
                "RelationFact.CarriedTarget",
                "RelationFact.TargetEcli",
                "ScopeAccountingSet.Count",
                "SyntheticResolveRequestContract.CanonicalDescriptor",
                "SyntheticSliceScope.CanonicalDescriptor",
            },
            corpus
                .Select(static vector => vector.DeclaringType + "." + vector.Member)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray(),
            "the corpus no longer covers exactly the surviving converted members");
    }

    /// <summary>
    /// Every former wire name, refused by the reader on a document that is otherwise valid.
    /// </summary>
    /// <remarks>
    /// Each vector asserts the round trip first. Without that, a refusal would prove only that the
    /// document was rejected, not that it was rejected for the injected name.
    /// </remarks>
    [TestMethod]
    public void TheReaderRefusesEveryFormerWireName()
    {
        foreach (var vector in Corpus())
        {
            var instance = vector.Instance();
            var type = instance.GetType();
            var json = ContractJson.Serialize(instance);

            Assert.IsNotNull(
                Deserialize(type, json),
                $"{vector.DeclaringType} does not round trip, so its vector proves nothing");

            Assert.IsFalse(
                JsonNode.Parse(json)!.AsObject().ContainsKey(vector.WireName),
                $"{vector.DeclaringType}.{vector.Member} is still emitted as {vector.WireName}");

            // Appended rather than inserted at the head. These contracts include polymorphic
            // unions whose type discriminator must remain the first member, so a leading injection
            // would be refused for the discriminator's sake and prove nothing about the name.
            var hostile = json.Insert(
                json.Length - 1, $",\"{vector.WireName}\":{vector.InjectedValue}");
            var refused = false;
            try
            {
                Deserialize(type, hostile);
            }
            catch (JsonException)
            {
                refused = true;
            }

            Assert.IsTrue(
                refused,
                $"{vector.DeclaringType} accepted a document carrying {vector.WireName}, so its " +
                "claim was taken in and silently discarded");
        }
    }

    /// <summary>
    /// The same names, refused by the emitted schema, so reader and schema admit one set.
    /// </summary>
    /// <remarks>
    /// Types with no emitted schema are listed rather than skipped. An absent schema is not a proof
    /// of anything, and letting it pass silently is the shape this whole slice exists to remove.
    /// </remarks>
    [TestMethod]
    public void EverySchemaRefusesTheSameNames()
    {
        // Positive controls first. Both assertions below are satisfied by every schema today, so
        // without these the test would pass just as well with detectors that always answered
        // "absent" and "closed". These prove each detector can still say the other thing.
        var facts = JsonNode.Parse(
            File.ReadAllText(Path.Combine(FindSchemasRoot(), "v3-facts", "facts-common.schema.json")))!;
        Assert.IsTrue(
            MentionsProperty(facts, "raw_value"),
            "the property detector cannot find a property the schema plainly declares");
        Assert.IsFalse(
            MentionsProperty(facts, "celex_sector"),
            "the property detector reports a name no schema declares");

        var openSchema = JsonNode.Parse(
            """{"type":"object","properties":{"a":{"type":"string"}}}""")!;
        Assert.IsFalse(
            EveryObjectIsClosed(openSchema),
            "the closure detector calls an object without additionalProperties false closed");
        Assert.IsTrue(
            EveryObjectIsClosed(JsonNode.Parse(
                """{"type":"object","additionalProperties":false,"properties":{"a":{"type":"string"}}}""")!),
            "the closure detector calls a closed object open");

        var unschemaed = new List<string>();

        foreach (var vector in Corpus())
        {
            if (vector.SchemaFile is null)
            {
                unschemaed.Add(vector.DeclaringType + "." + vector.Member);
                continue;
            }

            var path = Path.Combine(FindSchemasRoot(), vector.SchemaFile.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(path), path);
            var schema = JsonNode.Parse(File.ReadAllText(path))!;

            Assert.IsFalse(
                MentionsProperty(schema, vector.WireName),
                $"{vector.SchemaFile} declares {vector.WireName}, which the reader now refuses");

            Assert.IsTrue(
                EveryObjectIsClosed(schema),
                $"{vector.SchemaFile} has an object without additionalProperties false, so it " +
                $"cannot be said to refuse {vector.WireName}");
        }

        CollectionAssert.AreEquivalent(
            new[]
            {
                "EuChannelDisposition.MayGraduate",
                "EuLanguageBodyDisposition.CarriesBody",
                "EuAcquisitionProfile.AdmittedChannels",
                "DurableBlobWriteReceipt.VerifiedAt",
            },
            unschemaed.ToArray(),
            "the set of members with no emitted schema changed, so the reader is now the only " +
            "proof for a different set than this test records");
    }

    private static DurableBlobWriteReceipt WriteReceipt()
    {
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef,
            Digest,
            1,
            CustodyClass.NightlyFloor90d);
        var policy = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            CustodyVerificationProfile.FileSystemUnenforced1,
            policyKey: null,
            CustodyProtection.NotEnforced,
            new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero),
            protectedUntil: null);
        return new DurableBlobWriteReceipt(
            CustodySchemaIds.DurableBlobWriteReceipt,
            reference,
            policy);
    }

    private static object? Deserialize(Type type, string json)
    {
        var method = typeof(ContractJson)
            .GetMethod(nameof(ContractJson.Deserialize))!
            .MakeGenericMethod(type);
        try
        {
            return method.Invoke(null, [json]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    private static bool MentionsProperty(JsonNode node, string name)
    {
        if (node is JsonObject obj)
        {
            if (obj["properties"] is JsonObject properties && properties.ContainsKey(name))
            {
                return true;
            }

            return obj.Any(pair => pair.Value is not null && MentionsProperty(pair.Value, name));
        }

        return node is JsonArray array &&
            array.Any(item => item is not null && MentionsProperty(item, name));
    }

    private static bool EveryObjectIsClosed(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            // Only a node that declares an object type is required to close. An "if" or "then"
            // subschema inside an "allOf" carries "properties" to express a conditional
            // constraint, and closing those would refuse documents the contract admits.
            // Read both defensively. "type" may be an array such as ["object", "null"], and
            // "additionalProperties" may be a subschema object rather than a boolean, so a
            // direct GetValue throws rather than answering.
            if (obj["properties"] is JsonObject &&
                obj["type"] is JsonValue typeValue &&
                typeValue.TryGetValue<string>(out var declaredType) &&
                declaredType == "object" &&
                (obj["additionalProperties"] is not JsonValue closedValue ||
                    !closedValue.TryGetValue<bool>(out var closed) ||
                    closed))
            {
                return false;
            }

            return obj.All(pair => pair.Value is null || EveryObjectIsClosed(pair.Value));
        }

        return node is not JsonArray array ||
            array.All(item => item is null || EveryObjectIsClosed(item));
    }

    private static string FindSchemasRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            var candidate = Path.Combine(directory, "schemas");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new InvalidOperationException("schemas directory not found");
    }
}
