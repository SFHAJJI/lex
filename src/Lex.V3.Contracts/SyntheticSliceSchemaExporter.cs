using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace Lex.V3.Contracts;

public static class SyntheticSliceSchemaExporter
{
    private static readonly ReadOnlyDictionary<string, Type> SchemaTypes = new(
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [V3SchemaIds.SyntheticSliceArtifact] = typeof(SyntheticSliceArtifactManifest),
            [V3SchemaIds.SyntheticSliceControl] = typeof(SyntheticSliceControl),
            [V3SchemaIds.SyntheticResolveEnvelope] = typeof(SyntheticResolveEnvelope),
        });

    private static readonly ReadOnlyDictionary<string, string> SchemaFiles = new(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [V3SchemaIds.SyntheticSliceArtifact] = "synthetic-slice-artifact.schema.json",
            [V3SchemaIds.SyntheticSliceControl] = "synthetic-slice-control.schema.json",
            [V3SchemaIds.SyntheticResolveEnvelope] = "synthetic-resolve-envelope.schema.json",
        });

    public static byte[] ExportUtf8(string schemaId) =>
        ExportUtf8(schemaId, SyntheticSliceContractLimits.MaximumSchemaBytes);

    internal static byte[] ExportUtf8(string schemaId, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (!SchemaTypes.TryGetValue(schemaId, out var type))
        {
            throw new ArgumentException("Unknown synthetic-slice schema identity.", nameof(schemaId));
        }

        var root = ContractJson.CreateSchemaOptions().GetJsonSchemaAsNode(
            type,
            new JsonSchemaExporterOptions
            {
                TreatNullObliviousAsNonNullable = true,
            }) as JsonObject
            ?? throw new InvalidOperationException("The exported schema root must be an object.");

        root["$id"] = V3SchemaResourceIds.ForWireSchema(schemaId);
        root["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        SyntheticSliceSchemaHardener.Apply(schemaId, root);

        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Default,
            WriteIndented = true,
        };
        var json = root.ToJsonString(options).Replace("\r\n", "\n", StringComparison.Ordinal);
        var normalized = json.TrimEnd('\r', '\n') + "\n";
        var byteCount = Encoding.UTF8.GetByteCount(normalized);
        if (byteCount > maximumBytes)
        {
            throw new InvalidDataException("The synthetic schema exceeds its remaining byte budget.");
        }

        var bytes = GC.AllocateUninitializedArray<byte>(byteCount);
        Encoding.UTF8.GetBytes(normalized, bytes);
        return bytes;
    }

    public static string FileNameFor(string schemaId) =>
        SchemaFiles.TryGetValue(schemaId, out var fileName)
            ? fileName
            : throw new ArgumentException("Unknown synthetic-slice schema identity.", nameof(schemaId));

    public static SyntheticSliceSchemaTable ExportSchemaTable() =>
        ExportSchemaTable(SyntheticSliceContractLimits.MaximumTrackedSchemaBytes);

    internal static SyntheticSliceSchemaTable ExportSchemaTable(int maximumTrackedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTrackedBytes);
        var remaining = maximumTrackedBytes;
        var members = new List<SyntheticSliceSchemaMember>(SyntheticSliceSchemaGraph.SchemaIds.Count);
        foreach (var schemaId in SyntheticSliceSchemaGraph.SchemaIds)
        {
            if (remaining <= 0)
            {
                throw new InvalidDataException("The synthetic schema table exhausted its byte budget.");
            }

            var memberBudget = Math.Min(remaining, SyntheticSliceContractLimits.MaximumSchemaBytes);
            var bytes = SyntheticSliceSchemaGraph.OwnedSchemaIds.Contains(schemaId)
                ? ExportUtf8(schemaId, memberBudget)
                : PreviewSchemaExporter.ExportUtf8(schemaId, memberBudget);
            remaining -= bytes.Length;
            members.Add(new SyntheticSliceSchemaMember(
                schemaId,
                V3SchemaResourceIds.ForWireSchema(schemaId),
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                bytes.LongLength));
        }

        return new SyntheticSliceSchemaTable(members);
    }
}

internal static class SyntheticSliceSchemaHardener
{
    private const string Sha256Pattern = "^[0-9a-f]{64}$";
    private const string RequestReferencePattern = "^req_[0-9a-f]{32}$";
    private const string SignaturePattern = "^[A-Za-z0-9_-]{86}$";

    public static void Apply(string schemaId, JsonObject root)
    {
        BoundCollections(root);
        HardenHashes(root);

        switch (schemaId)
        {
            case V3SchemaIds.SyntheticSliceArtifact:
                HardenArtifact(root);
                break;
            case V3SchemaIds.SyntheticSliceControl:
                HardenControl(root);
                break;
            case V3SchemaIds.SyntheticResolveEnvelope:
                HardenEnvelope(root);
                break;
            default:
                throw new ArgumentException("Unknown synthetic-slice schema identity.", nameof(schemaId));
        }
    }

    private static void HardenArtifact(JsonObject root)
    {
        SetConst(Property(root, "schema"), V3SchemaIds.SyntheticSliceArtifact);
        SetConst(Property(root, "schema_resource"), V3SchemaResourceIds.SyntheticSliceArtifact);
        SetConst(Property(root, "evidence_class"), "synthetic_preview");
        SetConst(Property(root, "synthetic"), true);
        SetConst(Property(root, "source_kind"), "synthetic_test");
        root["x_max_stream_bytes"] = SyntheticSliceContractLimits.MaximumManifestBytes;
        root["x_runtime_invariants"] = new JsonArray(
            "schema table is the exact six-member verifier-owned graph",
            "manifest and control schema digests equal their schema-table members",
            "signature verifies against the separately pinned preview public key");

        var environment = Property(root, "environment");
        SetConst(Property(environment, "class"), "preview");
        Property(environment, "binding")["maxLength"] = 2_048;
        SetConst(Property(Property(root, "issuer"), "role"), "preview_attestor");

        var schemaTable = Property(root, "schema_table");
        var members = Property(schemaTable, "members");
        members["minItems"] = SyntheticSliceSchemaGraph.SchemaIds.Count;
        members["maxItems"] = SyntheticSliceSchemaGraph.SchemaIds.Count;
        members["prefixItems"] = new JsonArray(
            SyntheticSliceSchemaGraph.SchemaIds
                .Select(static schemaId => (JsonNode)new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["properties"] = new JsonObject
                    {
                        ["schema"] = new JsonObject { ["const"] = schemaId },
                        ["schema_resource"] = new JsonObject
                        {
                            ["const"] = V3SchemaResourceIds.ForWireSchema(schemaId),
                        },
                        ["sha256"] = HashSchema(),
                        ["bytes"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["minimum"] = 1,
                            ["maximum"] = SyntheticSliceContractLimits.MaximumSchemaBytes,
                        },
                    },
                    ["required"] = new JsonArray("schema", "schema_resource", "sha256", "bytes"),
                })
                .ToArray());
        members["items"] = false;

        var control = Property(root, "control");
        SetConst(Property(control, "schema"), V3SchemaIds.SyntheticSliceControl);
        SetConst(Property(control, "schema_resource"), V3SchemaResourceIds.SyntheticSliceControl);
        SetConst(Property(control, "media_type"), "application/json");
        SetRange(Property(control, "bytes"), 1, SyntheticSliceContractLimits.MaximumControlBytes);

        var attestation = Property(root, "attestation");
        SetConst(Property(attestation, "purpose"), "preview_mechanics_only");
        SetConst(Property(attestation, "algorithm"), "ECDSA-P256-SHA256");
        SetConst(Property(attestation, "signature_format"), "ieee-p1363");
        var signature = Property(attestation, "signature");
        signature["minLength"] = 86;
        signature["maxLength"] = 86;
        signature["pattern"] = SignaturePattern;
    }

    private static void HardenControl(JsonObject root)
    {
        SetConst(Property(root, "schema"), V3SchemaIds.SyntheticSliceControl);
        SetConst(Property(root, "schema_resource"), V3SchemaResourceIds.SyntheticSliceControl);
        root["x_max_stream_bytes"] = SyntheticSliceContractLimits.MaximumControlBytes;
        root["x_max_candidate_bytes"] = SyntheticSliceContractLimits.MaximumCandidateBytes;
        root["x_max_tracked_schema_bytes"] = SyntheticSliceContractLimits.MaximumTrackedSchemaBytes;
        root["x_runtime_invariants"] = new JsonArray(
            "operation catalog and refusal registry equal the active embedded S0-05 instances",
            "object-set schema digest equals the tracked reused schema",
            "index scope digest equals the complete synthetic scope digest",
            "blobs occur exactly once in source, derived, SQLite order");

        HardenRequestContract(Property(root, "resolve_request_contract"));
        HardenNormalizationProfile(Property(root, "normalization_profile"));
        HardenScope(Property(root, "scope"));

        ReplacePropertyWithReference(root, "operation_catalog", V3SchemaResourceIds.PreviewOperationCatalog);
        ReplacePropertyWithReference(root, "refusal_registry", V3SchemaResourceIds.PreviewRefusalRegistry);

        var objectSetSchema = Property(root, "object_set_schema");
        SetConst(Property(objectSetSchema, "schema"), V3SchemaIds.PreviewObjectSet);
        SetConst(Property(objectSetSchema, "schema_resource"), V3SchemaResourceIds.PreviewObjectSet);

        var indexStamp = Property(root, "index_stamp");
        SetConst(Property(indexStamp, "schema"), SyntheticSliceIndexStamp.SchemaIdentity);
        var definitions = root["$defs"] as JsonObject ?? new JsonObject();
        definitions["index_stamp"] = indexStamp.DeepClone();
        root["$defs"] = definitions;
        ReplacePropertyWithReference(root, "index_stamp", "#/$defs/index_stamp");

        var blobs = Property(root, "blobs");
        blobs["minItems"] = 3;
        blobs["maxItems"] = 3;
        blobs["prefixItems"] = new JsonArray(
            BlobSchema("source_transport", "application/octet-stream", SyntheticSliceContractLimits.MaximumSourceBytes),
            BlobSchema("derived_text", "text/plain;charset=utf-8", SyntheticSliceContractLimits.MaximumDerivedBytes),
            BlobSchema("sqlite_index", "application/vnd.sqlite3", SyntheticSliceContractLimits.MaximumSqliteBytes));
        blobs["items"] = false;
    }

    private static void HardenRequestContract(JsonObject request)
    {
        SetConst(Property(request, "contract_id"), SyntheticResolveRequestContract.Identity);
        SetConst(Property(request, "method"), "GET");
        SetConst(
            Property(request, "maximum_application_raw_target_bytes"),
            SyntheticResolveRequestContract.MaximumApplicationRawTargetByteCount);
        var targets = Property(request, "product_raw_targets");
        targets["minItems"] = 2;
        targets["maxItems"] = 2;
        targets["prefixItems"] = new JsonArray(
            new JsonObject { ["const"] = SyntheticResolveRequestContract.HeldRawTarget },
            new JsonObject { ["const"] = SyntheticResolveRequestContract.CandidateRawTarget });
        targets["items"] = false;
        SetConst(Property(request, "readiness_method"), "GET");
        SetConst(Property(request, "readiness_target"), SyntheticResolveRequestContract.ReadyRawTarget);
        SetConst(Property(request, "sha256"), SyntheticResolveRequestContract.V1.Sha256);
    }

    private static void HardenNormalizationProfile(JsonObject profile)
    {
        SetConst(Property(profile, "profile_id"), SyntheticNormalizationProfile.Identity);
        SetConst(Property(profile, "descriptor"), SyntheticNormalizationProfile.CanonicalDescriptor);
        SetConst(Property(profile, "sha256"), SyntheticNormalizationProfile.PlainV1.Sha256);
    }

    private static void HardenScope(JsonObject scope)
    {
        SetConst(Property(scope, "publisher"), "lu-legilux");
        SetConst(Property(scope, "complete"), true);
        SetConst(Property(scope, "upstream_health"), "not_applicable_synthetic");
        var members = Property(scope, "enumerated_members");
        members["minItems"] = 1;
        members["maxItems"] = 1;
        members["prefixItems"] = new JsonArray(
            new JsonObject { ["const"] = ContractValidation.SyntheticEliCoordinate });
        members["items"] = false;
        SetConst(Property(scope, "sha256"), SyntheticSliceScope.CompleteLu.Sha256);
    }

    private static void HardenEnvelope(JsonObject root)
    {
        if (root["anyOf"] is not JsonArray branches || branches.Count != 2)
        {
            throw new InvalidOperationException("The synthetic resolve envelope must have two closed branches.");
        }

        root["x_max_stream_bytes"] = SyntheticSliceContractLimits.MaximumResponseBytes;
        root["x_runtime_invariants"] = new JsonArray(
            "response lineage binds the admitted artifact, index, snapshot, runtime, and builder",
            "success carries one public synthetic object projected from the exact held ELI SQL row",
            "identifier_unknown is possible only for the exact complete synthetic scope");

        foreach (var branchNode in branches)
        {
            var branch = branchNode as JsonObject
                ?? throw new InvalidOperationException("An envelope branch must be an object.");
            SetConst(Property(branch, "schema"), V3SchemaIds.SyntheticResolveEnvelope);
            SetConst(Property(branch, "synthetic"), true);
            SetConst(Property(branch, "object_type"), "envelope");

            var branchName = Property(branch, "branch")["const"]?.GetValue<string>()
                ?? throw new InvalidOperationException("An envelope branch must carry its discriminator.");
            SetConst(
                Property(branch, "status"),
                string.Equals(branchName, "success", StringComparison.Ordinal)
                    ? "ok"
                    : "identifier_unknown");
            HardenContext(Property(branch, "context"));

            if (string.Equals(branchName, "success", StringComparison.Ordinal))
            {
                SetConst(Property(branch, "matched_identifier_family"), "eli");
                SetConst(Property(branch, "matched_coordinate"), ContractValidation.SyntheticEliCoordinate);
                HardenSuccessResult(branch);
            }
            else
            {
                HardenRefusal(Property(branch, "refusal"));
            }
        }
    }

    private static void HardenSuccessResult(JsonObject branch)
    {
        if (branch["properties"] is not JsonObject properties || properties["result"] is null)
        {
            throw new InvalidOperationException("Generated success schema is missing its result.");
        }

        properties["result"] = new JsonObject
        {
            ["$ref"] = V3SchemaResourceIds.PreviewObjectSet,
            ["properties"] = new JsonObject
            {
                ["objects"] = new JsonObject
                {
                    ["minItems"] = 1,
                    ["maxItems"] = 1,
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["synthetic"] = new JsonObject { ["const"] = true },
                            ["body_holding_state"] = new JsonObject { ["const"] = "held_public" },
                            ["body_holding_disposition"] = new JsonObject
                            {
                                ["const"] = "synthetic_fixture",
                            },
                            ["body"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["pattern"] = "This text is synthetic and has no legal authority\\.",
                            },
                        },
                        ["required"] = new JsonArray(
                            "synthetic",
                            "body_holding_state",
                            "body_holding_disposition",
                            "body"),
                    },
                },
            },
            ["required"] = new JsonArray("objects"),
        };
    }

    private static void HardenContext(JsonObject context)
    {
        var requestRef = Property(context, "request_ref");
        requestRef["minLength"] = 36;
        requestRef["maxLength"] = 36;
        requestRef["pattern"] = RequestReferencePattern;

        var operation = Property(context, "operation");
        if (operation["$ref"] is null)
        {
            SetConst(Property(operation, "operation_id"), "resolve");
            SetConst(Property(operation, "catalog_id"), SyntheticSliceOperationCatalog.CatalogId);
        }

        var registry = Property(context, "refusal_registry");
        if (registry["$ref"] is null)
        {
            SetConst(Property(registry, "registry_id"), PreviewRefusalRegistry.StageZero.RegistryId);
            SetConst(Property(registry, "schema"), V3SchemaIds.PreviewRefusalRegistry);
        }

        var index = Property(context, "index");
        if (index["$ref"] is null)
        {
            SetConst(Property(index, "schema"), SyntheticSliceIndexStamp.SchemaIdentity);
        }
    }

    private static void HardenRefusal(JsonObject refusal)
    {
        SetConst(Property(refusal, "code"), "identifier_unknown");
        SetConst(Property(refusal, "checked_identifier_family"), "historical_legal_id");
        SetConst(
            Property(refusal, "requested_coordinate"),
            ContractValidation.SyntheticHistoricalLegalIdCoordinate);
        SetConst(Property(refusal, "asserts_absence_of_law"), false);

        var publishers = Property(refusal, "publisher_contexts_checked");
        publishers["minItems"] = 1;
        publishers["maxItems"] = 1;
        publishers["prefixItems"] = new JsonArray(new JsonObject { ["const"] = "lu-legilux" });
        publishers["items"] = false;

        var candidates = Property(refusal, "possible_held_records");
        candidates["minItems"] = 0;
        candidates["maxItems"] = 1;
        if (candidates["items"] is JsonObject candidate)
        {
            SetConst(Property(candidate, "identifier_family"), "eli");
            SetConst(Property(candidate, "coordinate"), ContractValidation.SyntheticEliCoordinate);
            SetConst(Property(candidate, "publisher"), "lu-legilux");
        }

        var actions = Property(refusal, "official_search_actions");
        actions["minItems"] = 1;
        actions["maxItems"] = 1;
        if (actions["items"] is JsonObject action)
        {
            SetConst(Property(action, "kind"), "publisher_search");
            SetConst(Property(action, "publisher"), "lu-legilux");
            SetConst(Property(action, "uri"), PreviewOfficialPublisherLinks.LuSearch);
        }

        var nextSteps = Property(refusal, "what_would_answer");
        nextSteps["minItems"] = 1;
        nextSteps["uniqueItems"] = true;
    }

    private static JsonObject BlobSchema(string kind, string mediaType, int maximumBytes) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["kind"] = new JsonObject { ["const"] = kind },
            ["sha256"] = HashSchema(),
            ["bytes"] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 1,
                ["maximum"] = maximumBytes,
            },
            ["media_type"] = new JsonObject { ["const"] = mediaType },
        },
        ["required"] = new JsonArray("kind", "sha256", "bytes", "media_type"),
    };

    private static JsonObject HashSchema() => new()
    {
        ["type"] = "string",
        ["minLength"] = 64,
        ["maxLength"] = 64,
        ["pattern"] = Sha256Pattern,
    };

    private static void HardenHashes(JsonNode node)
    {
        if (node is JsonObject schema)
        {
            if (schema["properties"] is JsonObject properties)
            {
                foreach (var property in properties)
                {
                    if (property.Value is JsonObject propertySchema &&
                        (string.Equals(property.Key, "sha256", StringComparison.Ordinal) ||
                         property.Key.EndsWith("_sha256", StringComparison.Ordinal) ||
                         string.Equals(property.Key, "build_id", StringComparison.Ordinal)))
                    {
                        propertySchema["minLength"] = 64;
                        propertySchema["maxLength"] = 64;
                        propertySchema["pattern"] = Sha256Pattern;
                    }
                }
            }

            foreach (var child in schema)
            {
                if (child.Value is not null)
                {
                    HardenHashes(child.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (child is not null)
                {
                    HardenHashes(child);
                }
            }
        }
    }

    private static void BoundCollections(JsonNode node)
    {
        if (node is JsonObject schema)
        {
            if (HasType(schema, "array") && schema["maxItems"] is null)
            {
                schema["maxItems"] = 64;
            }

            if (HasType(schema, "object") && schema["maxProperties"] is null)
            {
                schema["maxProperties"] = 64;
            }

            foreach (var child in schema)
            {
                if (child.Value is not null)
                {
                    BoundCollections(child.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (child is not null)
                {
                    BoundCollections(child);
                }
            }
        }
    }

    private static bool HasType(JsonObject schema, string expected) =>
        schema["type"] is JsonValue type &&
        type.TryGetValue<string>(out var value) &&
        string.Equals(value, expected, StringComparison.Ordinal);

    private static JsonObject Property(JsonObject schema, string propertyName)
    {
        if (schema["properties"] is not JsonObject properties ||
            properties[propertyName] is not JsonObject property)
        {
            throw new InvalidOperationException($"Generated schema is missing property '{propertyName}'.");
        }

        return property;
    }

    private static void ReplacePropertyWithReference(
        JsonObject schema,
        string propertyName,
        string resourceId)
    {
        if (schema["properties"] is not JsonObject properties || properties[propertyName] is null)
        {
            throw new InvalidOperationException($"Generated schema is missing property '{propertyName}'.");
        }

        properties[propertyName] = new JsonObject { ["$ref"] = resourceId };
    }

    private static void SetConst(JsonObject schema, string value) => schema["const"] = value;

    private static void SetConst(JsonObject schema, bool value) => schema["const"] = value;

    private static void SetConst(JsonObject schema, int value) => schema["const"] = value;

    private static void SetRange(JsonObject schema, long minimum, long maximum)
    {
        schema["minimum"] = minimum;
        schema["maximum"] = maximum;
    }
}
