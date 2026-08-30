using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace Lex.V3.Contracts;

public static class PreviewSchemaExporter
{
    private static readonly ReadOnlyDictionary<string, Type> SchemaTypes =
        new(new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [V3SchemaIds.PreviewArtifact] = typeof(PreviewArtifactManifest),
            [V3SchemaIds.PreviewPayload] = typeof(PreviewPayload),
            [V3SchemaIds.PreviewEnvelope] = typeof(PreviewEnvelope),
            [V3SchemaIds.PreviewObjectSet] = typeof(PreviewObjectSet),
            [V3SchemaIds.PreviewOperationCatalog] = typeof(PreviewOperationCatalog),
            [V3SchemaIds.PreviewRefusalRegistry] = typeof(PreviewRefusalRegistry),
        });

    private static readonly ReadOnlyDictionary<string, string> SchemaFiles =
        new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [V3SchemaIds.PreviewArtifact] = "preview-artifact.schema.json",
            [V3SchemaIds.PreviewPayload] = "preview-payload.schema.json",
            [V3SchemaIds.PreviewEnvelope] = "preview-envelope.schema.json",
            [V3SchemaIds.PreviewObjectSet] = "preview-object-set.schema.json",
            [V3SchemaIds.PreviewOperationCatalog] = "preview-operation-catalog.schema.json",
            [V3SchemaIds.PreviewRefusalRegistry] = "preview-refusal-registry.schema.json",
        });

    public static byte[] ExportUtf8(string schemaId)
    {
        var root = CreateHardenedSchemaNode(schemaId);
        if (string.Equals(schemaId, V3SchemaIds.PreviewPayload, StringComparison.Ordinal))
        {
            AppendPayloadResources(root);
        }

        var outputOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Default,
            WriteIndented = true,
        };
        var json = root.ToJsonString(outputOptions).Replace("\r\n", "\n", StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(json.TrimEnd('\r', '\n') + "\n");
    }

    private static JsonObject CreateHardenedSchemaNode(string schemaId)
    {
        if (!SchemaTypes.TryGetValue(schemaId, out var type))
        {
            throw new ArgumentException("Unknown preview schema identity.", nameof(schemaId));
        }

        var serializerOptions = ContractJson.CreateSchemaOptions();
        var root = serializerOptions.GetJsonSchemaAsNode(
            type,
            new JsonSchemaExporterOptions
            {
                TreatNullObliviousAsNonNullable = true,
            }) as JsonObject
            ?? throw new InvalidOperationException("The exported schema root must be an object.");

        root["$id"] = V3SchemaResourceIds.ForWireSchema(schemaId);
        root["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        PreviewSchemaHardener.Apply(schemaId, root);
        return root;
    }

    private static void AppendPayloadResources(JsonObject payload)
    {
        payload["$defs"] = new JsonObject
        {
            ["operation_catalog"] = CreateHardenedSchemaNode(V3SchemaIds.PreviewOperationCatalog),
            ["refusal_registry"] = CreateHardenedSchemaNode(V3SchemaIds.PreviewRefusalRegistry),
            ["object_set"] = CreateHardenedSchemaNode(V3SchemaIds.PreviewObjectSet),
            ["envelope"] = CreateHardenedSchemaNode(V3SchemaIds.PreviewEnvelope),
        };
    }

    public static string FileNameFor(string schemaId) =>
        SchemaFiles.TryGetValue(schemaId, out var fileName)
            ? fileName
            : throw new ArgumentException("Unknown preview schema identity.", nameof(schemaId));

    public static PreviewContractSet ExportContractSet() => new(
        ExportReference(V3SchemaIds.PreviewEnvelope),
        ExportReference(V3SchemaIds.PreviewObjectSet),
        ExportReference(V3SchemaIds.PreviewOperationCatalog),
        ExportReference(V3SchemaIds.PreviewRefusalRegistry));

    public static string ComputeSha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    public static byte[] GetDocumentCanonicalBytes(PreviewOperationCatalog value) =>
        PreviewDocumentCanonicalizer.Canonicalize(value);

    public static byte[] GetDocumentCanonicalBytes(PreviewRefusalRegistry value) =>
        PreviewDocumentCanonicalizer.Canonicalize(value);

    public static byte[] GetDocumentCanonicalBytes(PreviewObjectSet value) =>
        PreviewDocumentCanonicalizer.Canonicalize(value);

    public static string ComputeDocumentSha256(PreviewOperationCatalog value) =>
        ComputeSha256(GetDocumentCanonicalBytes(value));

    public static string ComputeDocumentSha256(PreviewRefusalRegistry value) =>
        ComputeSha256(GetDocumentCanonicalBytes(value));

    public static string ComputeDocumentSha256(PreviewObjectSet value) =>
        ComputeSha256(GetDocumentCanonicalBytes(value));

    private static PreviewTrackedSchemaReference ExportReference(string schemaId) =>
        new(
            schemaId,
            V3SchemaResourceIds.ForWireSchema(schemaId),
            ComputeSha256(ExportUtf8(schemaId)));
}

internal static class PreviewSchemaHardener
{
    private const string Sha256Pattern = "^[0-9a-f]{64}(?![\\s\\S])";
    private const string SignaturePattern = "^[A-Za-z0-9_-]{86}(?![\\s\\S])";
    private static readonly HashSet<string> IdentifierPropertyNames = new(StringComparer.Ordinal)
    {
        "schema",
        "index_format",
        "deterministic_order",
        "capability_requirement",
        "rest_projection",
        "mcp_projection",
        "html_projection",
        "jurisdiction",
        "identifier",
    };

    public static void Apply(string schemaId, JsonObject root)
    {
        HardenGenericPayloadBounds(root);
        HardenIdentifierProperties(root);

        switch (schemaId)
        {
            case V3SchemaIds.PreviewArtifact:
                HardenArtifact(root);
                break;
            case V3SchemaIds.PreviewPayload:
                HardenPayload(root);
                break;
            case V3SchemaIds.PreviewEnvelope:
                HardenEnvelope(root);
                break;
            case V3SchemaIds.PreviewObjectSet:
                HardenObjectSet(root);
                break;
            case V3SchemaIds.PreviewOperationCatalog:
                HardenOperationCatalog(root);
                break;
            case V3SchemaIds.PreviewRefusalRegistry:
                HardenRefusalRegistry(root);
                break;
            default:
                throw new ArgumentException("Unknown preview schema identity.", nameof(schemaId));
        }

        HardenSha256Properties(root);
    }

    private static void HardenArtifact(JsonObject root)
    {
        HardenManifestStrings(root);
        SetConst(Property(root, "schema"), V3SchemaIds.PreviewArtifact);
        SetConst(Property(root, "schema_resource"), V3SchemaResourceIds.PreviewArtifact);
        SetConst(Property(root, "evidence_class"), "synthetic_preview");
        SetConst(Property(root, "synthetic"), true);
        SetConst(Property(root, "source_kind"), "synthetic_test");
        root["x_runtime_invariants"] = new JsonArray(
            "all six schema digests equal the verifier-pinned generated tracked definitions",
            "contract_set equals the expected fixed preview graph",
            "issuer and key come from the configured trust store and signature is canonical P1363 over exact signing bytes",
            "declared payload size and digest are checked before payload parsing");
        root["x_max_stream_bytes"] = PreviewContractLimits.MaximumManifestBytes;
        root["x_max_json_depth"] = PreviewContractLimits.MaximumManifestDepth;
        root["x_max_total_property_names"] = PreviewContractLimits.MaximumManifestProperties;
        root["x_max_property_name_utf8_bytes"] = PreviewContractLimits.MaximumManifestPropertyNameBytes;
        root["x_max_string_value_utf8_bytes"] = PreviewContractLimits.MaximumManifestStringBytes;

        var environment = Property(root, "environment");
        SetConst(Property(environment, "class"), "preview");
        Property(environment, "binding")["maxLength"] = 2_048;

        var issuer = Property(root, "issuer");
        SetConst(Property(issuer, "role"), "preview_attestor");
        SetIdentifierConstraints(Property(issuer, "issuer_id"));
        SetIdentifierConstraints(Property(issuer, "key_id"));

        var contractSet = Property(root, "contract_set");
        BindContractReference(Property(contractSet, "envelope"), V3SchemaIds.PreviewEnvelope);
        BindContractReference(Property(contractSet, "object_set"), V3SchemaIds.PreviewObjectSet);
        BindContractReference(
            Property(contractSet, "operation_catalog"),
            V3SchemaIds.PreviewOperationCatalog);
        BindContractReference(
            Property(contractSet, "refusal_registry"),
            V3SchemaIds.PreviewRefusalRegistry);

        var payload = Property(root, "payload");
        SetConst(Property(payload, "schema"), V3SchemaIds.PreviewPayload);
        SetConst(Property(payload, "schema_resource"), V3SchemaResourceIds.PreviewPayload);
        SetConst(Property(payload, "media_type"), "application/json");
        var payloadBytes = Property(payload, "bytes");
        payloadBytes["minimum"] = 0;
        payloadBytes["maximum"] = PreviewContractLimits.MaximumPayloadBytes;

        var attestation = Property(root, "attestation");
        SetConst(Property(attestation, "purpose"), "preview_mechanics_only");
        SetConst(Property(attestation, "algorithm"), "ECDSA-P256-SHA256");
        SetConst(Property(attestation, "signature_format"), "ieee-p1363");
        var signature = Property(attestation, "signature");
        signature["minLength"] = 86;
        signature["maxLength"] = 86;
        signature["pattern"] = SignaturePattern;
    }

    private static void HardenPayload(JsonObject root)
    {
        SetConst(Property(root, "schema"), V3SchemaIds.PreviewPayload);
        root["x_runtime_invariants"] = new JsonArray(
            "each envelope references an active operation and binds the embedded catalog id and canonical digest",
            "each envelope binds the embedded refusal-registry id, schema, and canonical digest",
            "each success envelope binds the embedded object-set id and canonical digest",
            "each refusal code is allowed by its active operation descriptor",
            "all embedded collections reject null members");
        root["x_max_stream_bytes"] = PreviewContractLimits.MaximumPayloadBytes;
        root["x_max_json_depth"] = PreviewContractLimits.MaximumPayloadDepth;
        root["x_max_json_tokens"] = PreviewContractLimits.MaximumPayloadTokens;
        root["x_max_object_members"] = PreviewContractLimits.MaximumObjectMembers;
        root["x_max_array_items"] = PreviewContractLimits.MaximumArrayItems;
        root["x_max_property_name_utf8_bytes"] = PreviewContractLimits.MaximumPayloadPropertyNameBytes;
        root["x_max_string_value_utf8_bytes"] = PreviewContractLimits.MaximumPayloadStringBytes;

        ReplacePropertyWithReference(
            root,
            "operation_catalog",
            V3SchemaResourceIds.PreviewOperationCatalog);
        ReplacePropertyWithReference(
            root,
            "refusal_registry",
            V3SchemaResourceIds.PreviewRefusalRegistry);
        ReplacePropertyWithReference(root, "object_set", V3SchemaResourceIds.PreviewObjectSet);

        var envelopes = Property(root, "envelopes");
        envelopes["maxItems"] = PreviewContractLimits.MaximumEnvelopes;
        envelopes["items"] = new JsonObject { ["$ref"] = V3SchemaResourceIds.PreviewEnvelope };
    }

    private static void HardenEnvelope(JsonObject root)
    {
        root["x_runtime_invariants"] = new JsonArray(
            "runtime and builder component identifiers are distinct",
            "refusal status exactly equals its payload refusal code");
        HardenEnvelopeBranches(root);
    }

    private static void HardenEnvelopeBranches(JsonObject envelopeSchema)
    {
        if (envelopeSchema["anyOf"] is not JsonArray branches)
        {
            throw new InvalidOperationException("The preview envelope schema must have closed branches.");
        }

        foreach (var branchNode in branches)
        {
            var branch = branchNode as JsonObject
                ?? throw new InvalidOperationException("The preview envelope branch must be an object.");
            _ = branch["properties"] as JsonObject
                ?? throw new InvalidOperationException("The preview envelope branch must declare properties.");
            SetConst(Property(branch, "schema"), V3SchemaIds.PreviewEnvelope);
            SetConst(Property(branch, "object_type"), "envelope");

            var branchName = Property(branch, "branch")["const"]?.GetValue<string>();
            SetConst(
                Property(branch, "status"),
                string.Equals(branchName, "success", StringComparison.Ordinal)
                    ? "ok"
                    : "identifier_unknown");

            var context = Property(branch, "context");
            var requestReference = Property(context, "request_ref");
            SetConst(requestReference, ContractValidation.SyntheticRequestReference);
            var refusalRegistry = Property(context, "refusal_registry");
            if (refusalRegistry["$ref"] is null)
            {
                SetConst(Property(refusalRegistry, "schema"), V3SchemaIds.PreviewRefusalRegistry);
            }

            var freshness = Property(context, "freshness");
            if (freshness["$ref"] is null)
            {
                SetConst(
                    Property(freshness, "upstream_health"),
                    "not_applicable_synthetic");
            }

            if (string.Equals(branchName, "refusal", StringComparison.Ordinal))
            {
                HardenIdentifierUnknownRefusal(Property(branch, "refusal"));
            }
        }
    }

    private static void HardenObjectSet(JsonObject root)
    {
        SetConst(Property(root, "schema"), V3SchemaIds.PreviewObjectSet);
        root["x_runtime_invariants"] = new JsonArray(
            "object identifiers are unique and objects use object-id ordinal order",
            "object collections reject null members");
        var objects = Property(root, "objects");
        objects["maxItems"] = PreviewContractLimits.MaximumObjects;

        if (objects["items"] is JsonObject items && items["anyOf"] is JsonArray branches)
        {
            foreach (var branchNode in branches)
            {
                var branch = branchNode as JsonObject
                    ?? throw new InvalidOperationException("The preview object branch must be an object.");
                SetConst(Property(branch, "synthetic"), true);
                Property(branch, "work_id")["pattern"] =
                    "^preview:[ -~]{1,248}(?![\\s\\S])";
                Property(branch, "version_key")["pattern"] =
                    "^preview:[ -~]{1,248}(?![\\s\\S])";
                Property(branch, "anchor")["pattern"] =
                    "^preview:[ -~]{1,248}(?![\\s\\S])";
                branch["x_runtime_invariants"] = new JsonArray(
                    "body_sha256 is SHA-256 over the exact strict UTF-8 body bytes",
                    "body_holding_state and body_holding_disposition use the closed preview matrix",
                    "held-public body is a valid Unicode scalar sequence with visible non-whitespace content",
                    "held-public body is at most the runtime UTF-8 byte bound");

                branch["allOf"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["if"] = new JsonObject
                        {
                            ["properties"] = new JsonObject
                            {
                                ["body_holding_state"] = new JsonObject
                                {
                                    ["const"] = "held_public",
                                },
                            },
                            ["required"] = new JsonArray("body_holding_state"),
                        },
                        ["then"] = new JsonObject
                        {
                            ["properties"] = new JsonObject
                            {
                                ["body"] = new JsonObject
                                {
                                    ["type"] = "string",
                                    ["minLength"] = 1,
                                },
                                ["body_sha256"] = new JsonObject
                                {
                                    ["type"] = "string",
                                    ["minLength"] = 64,
                                    ["maxLength"] = 64,
                                    ["pattern"] = Sha256Pattern,
                                },
                                ["body_holding_disposition"] = new JsonObject
                                {
                                    ["const"] = "synthetic_fixture",
                                },
                            },
                        },
                    },
                    new JsonObject
                    {
                        ["if"] = new JsonObject
                        {
                            ["properties"] = new JsonObject
                            {
                                ["body_holding_state"] = new JsonObject
                                {
                                    ["const"] = "held_withheld",
                                },
                            },
                            ["required"] = new JsonArray("body_holding_state"),
                        },
                        ["then"] = new JsonObject
                        {
                            ["properties"] = new JsonObject
                            {
                                ["body"] = new JsonObject { ["const"] = null },
                                ["body_sha256"] = new JsonObject
                                {
                                    ["type"] = "string",
                                    ["minLength"] = 64,
                                    ["maxLength"] = 64,
                                    ["pattern"] = Sha256Pattern,
                                },
                                ["body_holding_disposition"] = new JsonObject
                                {
                                    ["const"] = "synthetic_fixture_withheld",
                                },
                            },
                        },
                    },
                    new JsonObject
                    {
                        ["if"] = new JsonObject
                        {
                            ["properties"] = new JsonObject
                            {
                                ["body_holding_state"] = new JsonObject
                                {
                                    ["const"] = "not_held",
                                },
                            },
                            ["required"] = new JsonArray("body_holding_state"),
                        },
                        ["then"] = new JsonObject
                        {
                            ["properties"] = new JsonObject
                            {
                                ["body"] = new JsonObject { ["const"] = null },
                                ["body_sha256"] = new JsonObject { ["const"] = null },
                                ["body_holding_disposition"] = new JsonObject
                                {
                                    ["const"] = "unknown_pending_evidence",
                                },
                            },
                        },
                    },
                };
            }
        }
    }

    private static void HardenOperationCatalog(JsonObject root)
    {
        SetConst(Property(root, "schema"), V3SchemaIds.PreviewOperationCatalog);
        root["x_runtime_invariants"] = new JsonArray(
            "operation identifiers are unique and entries use immutable operation-inventory order",
            "allowed refusal codes are unique, defined, and use declared enum order",
            "catalog collections reject null members");
    }

    private static void HardenRefusalRegistry(JsonObject root)
    {
        SetConst(Property(root, "schema"), V3SchemaIds.PreviewRefusalRegistry);
        root["x_runtime_invariants"] = new JsonArray(
            "the registry contains identifier_unknown exactly once",
            "mandatory_fields exactly equals the frozen ordered field set",
            "registry collections reject null members");

        var entries = Property(root, "entries");
        if (entries["items"] is JsonObject definition)
        {
            var mandatoryFields = Property(definition, "mandatory_fields");
            mandatoryFields["minItems"] = PreviewRefusalDefinition.IdentifierUnknownMandatoryFields.Count;
            mandatoryFields["maxItems"] = PreviewRefusalDefinition.IdentifierUnknownMandatoryFields.Count;
            mandatoryFields["prefixItems"] = new JsonArray(
                PreviewRefusalDefinition.IdentifierUnknownMandatoryFields
                    .Select(static field => (JsonNode)new JsonObject { ["const"] = field })
                    .ToArray());
            mandatoryFields["items"] = false;
        }

        entries["minItems"] = 1;
        entries["maxItems"] = 1;
    }

    private static void HardenIdentifierUnknownRefusal(JsonObject refusal)
    {
        SetConst(Property(refusal, "code"), "identifier_unknown");
        SetConst(Property(refusal, "asserts_absence_of_law"), false);

        var requestedCoordinate = Property(refusal, "requested_coordinate");
        requestedCoordinate["enum"] = new JsonArray(
            ContractValidation.SyntheticEliCoordinate,
            ContractValidation.SyntheticCelexCoordinate,
            ContractValidation.SyntheticMemorialCoordinate,
            ContractValidation.SyntheticHistoricalLegalIdCoordinate);
        refusal["allOf"] = new JsonArray(
            CoordinateFamilyRule("eli", ContractValidation.SyntheticEliCoordinate),
            CoordinateFamilyRule("celex", ContractValidation.SyntheticCelexCoordinate),
            CoordinateFamilyRule("memorial", ContractValidation.SyntheticMemorialCoordinate),
            CoordinateFamilyRule(
                "historical_legal_id",
                ContractValidation.SyntheticHistoricalLegalIdCoordinate));

        var checkedPublishers = Property(refusal, "publisher_contexts_checked");
        checkedPublishers["minItems"] = 1;
        checkedPublishers["maxItems"] = 2;
        checkedPublishers["uniqueItems"] = true;

        Property(refusal, "official_search_actions")["minItems"] = 1;
        Property(refusal, "what_would_answer")["minItems"] = 1;
        refusal["x_runtime_invariants"] = new JsonArray(
            "official search actions exactly cover publisher_contexts_checked in canonical order",
            "possible held records are unique by publisher and identifier, publisher-ordered, and limited to publisher_contexts_checked",
            "publisher_contexts_checked is unique and uses canonical LU then EU order",
            "what_would_answer is non-empty, unique, defined, and uses declared enum order");

        var possibleHeldRecords = Property(refusal, "possible_held_records");
        if (possibleHeldRecords["items"] is JsonObject heldRecord)
        {
            heldRecord["allOf"] = new JsonArray(
                PublisherIdentifierRule(
                    "lu-legilux",
                    ContractValidation.SyntheticLuHeldRecordIdentifier),
                PublisherIdentifierRule(
                    "eu-eurlex",
                    ContractValidation.SyntheticEuHeldRecordIdentifier));
        }

        var officialActions = Property(refusal, "official_search_actions");
        if (officialActions["items"] is JsonObject action)
        {
            SetConst(Property(action, "kind"), "publisher_search");
            action["allOf"] = new JsonArray(
                PublisherActionRule("lu-legilux", PreviewOfficialPublisherLinks.LuSearch),
                PublisherActionRule(
                    "eu-eurlex",
                    PreviewOfficialPublisherLinks.EuSearch));
        }
    }

    private static JsonObject CoordinateFamilyRule(string family, string coordinate) => new()
    {
        ["if"] = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["checked_identifier_family"] = new JsonObject { ["const"] = family },
            },
            ["required"] = new JsonArray("checked_identifier_family"),
        },
        ["then"] = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["requested_coordinate"] = new JsonObject { ["const"] = coordinate },
            },
        },
    };

    private static JsonObject PublisherActionRule(string publisher, string uri) => new()
    {
        ["if"] = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["publisher"] = new JsonObject { ["const"] = publisher },
            },
            ["required"] = new JsonArray("publisher"),
        },
        ["then"] = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["uri"] = new JsonObject { ["const"] = uri },
            },
        },
    };

    private static JsonObject PublisherIdentifierRule(string publisher, string identifier) => new()
    {
        ["if"] = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["publisher"] = new JsonObject { ["const"] = publisher },
            },
            ["required"] = new JsonArray("publisher"),
        },
        ["then"] = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["identifier"] = new JsonObject { ["const"] = identifier },
            },
        },
    };

    private static void BindContractReference(JsonObject reference, string schemaId)
    {
        SetConst(Property(reference, "schema"), schemaId);
        SetConst(
            Property(reference, "schema_resource"),
            V3SchemaResourceIds.ForWireSchema(schemaId));
    }

    private static void ReplacePropertyWithReference(
        JsonObject schema,
        string propertyName,
        string schemaId)
    {
        if (schema["properties"] is not JsonObject properties || properties[propertyName] is null)
        {
            throw new InvalidOperationException($"Generated schema is missing property '{propertyName}'.");
        }

        properties[propertyName] = new JsonObject { ["$ref"] = schemaId };
    }

    private static void HardenSha256Properties(JsonNode node)
    {
        if (node is JsonObject schema)
        {
            if (schema["properties"] is JsonObject properties)
            {
                foreach (var property in properties)
                {
                    if (property.Value is not JsonObject propertySchema)
                    {
                        continue;
                    }

                    if (string.Equals(property.Key, "sha256", StringComparison.Ordinal) ||
                        property.Key.EndsWith("_sha256", StringComparison.Ordinal))
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
                    HardenSha256Properties(child.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (child is not null)
                {
                    HardenSha256Properties(child);
                }
            }
        }
    }

    private static void HardenIdentifierProperties(JsonNode node)
    {
        if (node is JsonObject schema)
        {
            if (schema["properties"] is JsonObject properties)
            {
                foreach (var property in properties)
                {
                    if (property.Value is not JsonObject propertySchema)
                    {
                        continue;
                    }

                    if (property.Key.EndsWith("_id", StringComparison.Ordinal) ||
                        IdentifierPropertyNames.Contains(property.Key))
                    {
                        SetIdentifierConstraints(propertySchema);
                    }

                    if (string.Equals(property.Key, "title", StringComparison.Ordinal))
                    {
                        propertySchema["minLength"] = 1;
                        propertySchema["maxLength"] = ContractValidation.MaximumDisplayTitleScalars;
                        propertySchema["pattern"] =
                            "^(?=[\\s\\S]*[^\\u0009-\\u000D\\u0020\\u0085\\u00A0\\u1680" +
                            "\\u2000-\\u200A\\u2028\\u2029\\u202F\\u205F\\u3000])" +
                            "[^\\u0000-\\u001F\\u007F-\\u009F\\u2028\\u2029]+" +
                            "(?![\\s\\S])";
                        propertySchema["x_runtime_invariants"] = new JsonArray(
                            "valid Unicode scalar sequence",
                            "contains at least one non-whitespace Unicode scalar");
                    }
                }
            }

            foreach (var child in schema)
            {
                if (child.Value is not null)
                {
                    HardenIdentifierProperties(child.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (child is not null)
                {
                    HardenIdentifierProperties(child);
                }
            }
        }
    }

    private static void SetIdentifierConstraints(JsonObject schema)
    {
        schema["minLength"] = 1;
        schema["maxLength"] = ContractValidation.MaximumIdentifierLength;
        schema["pattern"] = ContractValidation.IdentifierPattern;
    }

    private static void HardenManifestStrings(JsonNode node)
    {
        if (node is JsonObject schema)
        {
            if (schema["type"] is JsonValue typeValue &&
                typeValue.TryGetValue<string>(out var type) &&
                string.Equals(type, "string", StringComparison.Ordinal))
            {
                schema["minLength"] = 1;
                schema["maxLength"] = PreviewContractLimits.MaximumManifestStringBytes;
                schema["pattern"] = "^[ -~]+(?![\\s\\S])";
            }

            foreach (var child in schema)
            {
                if (child.Value is not null)
                {
                    HardenManifestStrings(child.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (child is not null)
                {
                    HardenManifestStrings(child);
                }
            }
        }
    }

    private static void HardenGenericPayloadBounds(JsonNode node)
    {
        if (node is JsonObject schema)
        {
            if (schema["properties"] is JsonObject properties &&
                properties["operation_id"] is JsonObject operationId)
            {
                operationId["enum"] = new JsonArray(
                    V3ContractVocabulary.OperationIds
                        .Select(static value => (JsonNode)JsonValue.Create(value)!)
                        .ToArray());
            }

            if (schema["type"] is JsonValue typeValue &&
                typeValue.TryGetValue<string>(out var type))
            {
                if (string.Equals(type, "array", StringComparison.Ordinal) && schema["maxItems"] is null)
                {
                    schema["maxItems"] = PreviewContractLimits.MaximumArrayItems;
                }
                else if (string.Equals(type, "object", StringComparison.Ordinal) &&
                         schema["maxProperties"] is null)
                {
                    schema["maxProperties"] = PreviewContractLimits.MaximumObjectMembers;
                }
            }

            foreach (var child in schema)
            {
                if (child.Value is not null)
                {
                    HardenGenericPayloadBounds(child.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (child is not null)
                {
                    HardenGenericPayloadBounds(child);
                }
            }
        }
    }

    private static JsonObject Property(JsonObject schema, string propertyName)
    {
        if (schema["properties"] is not JsonObject properties ||
            properties[propertyName] is not JsonObject property)
        {
            throw new InvalidOperationException($"Generated schema is missing property '{propertyName}'.");
        }

        return property;
    }

    private static void SetConst(JsonObject schema, string value) => schema["const"] = value;

    private static void SetConst(JsonObject schema, bool value) => schema["const"] = value;
}
