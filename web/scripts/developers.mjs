// The Developers page's facts (the specification of record's R55: "Developers at /developers, generated from the registry";
// R46: free and anonymous, "the limits enforced and the guarantees actually supported"; the plan's item 8.4), generated from
// the contracts this release holds, never written by hand:
// - the operations, the refusal codes with the fields each payload must carry, the verdicts and the registry's digest: the
//   census's `contract` block (`schemas/v3-platform/envelope-samples.json`), which `V3EnvelopeSamplesTests` reads from the
//   reviewed registry itself (`V3OperationRegistry.Reviewed`) and holds byte for byte;
// - which operations this server serves, and the data that would serve each one it does not: the census's coverage answer,
//   whose operations block the platform computes from its routes (`V3RestRouteBinding.Served`, `V3UnservedOperations`);
// - each operation's parameters and the envelope's members: the request schemas and the envelope schema, the very files the
//   API embeds and checks against the registry's digests when it loads them (`V3PlatformSchemaDocuments`);
// - the events log's delivery, in the platform's own words: the census of answers (`answer-samples.json`).
//
// A few facts live only in the API's code: the MCP protocol version and methods, the request body's ceiling and the
// transport failures below the envelope. They are named here once, and `reference-pages.test.mjs` holds each to the C# it
// comes from, so a change there fails the web suite until this page says it.
//
// The schemas are published beside the pages, byte for byte, under `/schemas/v3-platform/`, each named by its file with
// `.schema.json` shortened to `.json` (both servers serve one extension per file: `V3WebRoot`'s and `serve-live.mjs`'s
// path rules), so the page links each one and prints its SHA-256, which the registry names.
//
// This module is data and pure functions only: the page's bundle carries it, so it reads nothing itself
// (`developers-contracts.mjs` reads the files, for the build and the tests).

/** Where REST operations and the MCP endpoint are, on this site's own origin. */
export const REST_PREFIX = "/api/v3/";
export const MCP_ENDPOINT = "/mcp";

/** The MCP protocol version the endpoint names in every answer (`V3McpJsonRpc.ProtocolVersion`). */
export const MCP_PROTOCOL_VERSION = "2025-06-18";

/** The JSON-RPC methods the MCP endpoint answers (`V3McpJsonRpc.HandleAsync`); any other is "Method not found". */
export const MCP_METHODS = Object.freeze(["initialize", "tools/list", "tools/call"]);

/** The largest request body the API reads, in bytes (`V3PlatformHost.MaximumRequestBytes`); a larger one answers request_too_large. */
export const MAXIMUM_REQUEST_BYTES = 1024 * 1024;

/**
 * The per-client limit (R109, R46; `V3ClientLimiter`): API requests a minute and at once for one client (REST and MCP
 * together), the status REST answers `rate_limited` with (`V3ResolveRestRoute.HandleRateLimitedAsync`), and the pages the
 * server renders itself a minute, past which a page is sent as built.
 */
export const CLIENT_LIMIT = Object.freeze({ apiPerMinute: 60, apiInFlight: 4, status: 429, rendersPerMinute: 300 });

/** Where the published key set stands (R116; `V3KeySet.RawTarget`), served by the API, not a file of the pages. */
export const KEY_SET_PATH = "/.well-known/lex-keys.json";

/** The schema of the problem document a request that never reaches an envelope answers (`V3TransportResponse.Schema`). */
export const TRANSPORT_PROBLEM_SCHEMA = "lex-v3-transport-problem/1";

/**
 * The failures below the envelope, as `V3TransportResponse` sends them: each problem's code, its title (the platform's
 * English, shown as sent) and its HTTP status, in the C#'s order.
 */
export const TRANSPORT_PROBLEMS = Object.freeze([
  Object.freeze({ code: "malformed_json", title: "Malformed JSON", status: 400 }),
  Object.freeze({ code: "duplicate_json_member", title: "Duplicate JSON member", status: 400 }),
  Object.freeze({ code: "trailing_json_content", title: "Trailing JSON content", status: 400 }),
  Object.freeze({ code: "request_too_large", title: "Request too large", status: 413 }),
  Object.freeze({ code: "request_too_deep", title: "Request too deep", status: 400 }),
  Object.freeze({ code: "parameters_not_object", title: "Parameters must be an object", status: 400 }),
  Object.freeze({ code: "request_schema_invalid", title: "Request schema invalid", status: 400 }),
  Object.freeze({ code: "unknown_operation", title: "Unknown operation", status: 400 }),
  Object.freeze({ code: "method_not_allowed", title: "Method not allowed", status: 405 }),
  Object.freeze({ code: "unknown_route", title: "Unknown route", status: 404 }),
  Object.freeze({ code: "operation_not_served", title: "Operation registered and not served", status: 404 }),
  Object.freeze({ code: "internal_response_invalid", title: "Internal response invalid", status: 500 }),
  Object.freeze({ code: "internal_failure", title: "Internal failure", status: 500 }),
]);

/** The two presentation results, which are not refusals (the specification of record, §3, API and MCP; D91; R61). */
export const PRESENTATION_RESULTS = Object.freeze(["assistant_v3_unavailable", "localization_unavailable"]);

/** Where the published schemas stand on the site. */
export const PUBLISHED_SCHEMA_DIRECTORY = "schemas/v3-platform";

/** The address a schema file of `schemas/v3-platform` is published at: `search-request.schema.json` at `/schemas/v3-platform/search-request.json`. */
export function publishedSchemaPath(file) {
  if (!/^[a-z0-9-]+\.schema\.json$/.test(file)) throw new Error(`${JSON.stringify(file)} is not a schema file of ${PUBLISHED_SCHEMA_DIRECTORY}`);
  return `/${PUBLISHED_SCHEMA_DIRECTORY}/${file.replace(/\.schema\.json$/, ".json")}`;
}

/** A schema's file in `schemas/v3-platform`, by its identity, as `V3PlatformSchemaExporter.FileNameFor` names it. */
export function schemaFileFor(schemaId) {
  if (schemaId === "lex-v3-envelope/1") return "envelope.schema.json";
  if (schemaId === "lex-v3-refusal/1") return "refusal.schema.json";
  const match = /^lex-v3-([a-z_]+)-(request|result)\/1$/.exec(schemaId);
  if (match === null) throw new Error(`${JSON.stringify(schemaId)} names no schema of the platform`);
  return `${match[1].replaceAll("_", "-")}-${match[2]}.schema.json`;
}

function required(value, what) {
  if (value === undefined || value === null) throw new Error(`the Developers page is generated from the contracts, and they hold no ${what}`);
  return value;
}

/**
 * The page's facts from the contracts: `census` the envelope census, `answers` the census of answers, `schemas` every schema
 * file of `schemas/v3-platform` as `{ file, json, sha256 }`. Throws on anything the contracts do not hold or that disagrees
 * between them (an operation with no schema, a served list that is not the registry's), since a page that guessed would
 * describe a server that does not exist.
 */
export function developersModel({ census, answers, schemas }) {
  const contract = required(census?.contract, "contract block in the envelope census");
  const byFile = new Map(schemas.map((schema) => [schema.file, schema]));
  const schemaOf = (schemaId) => {
    const file = schemaFileFor(schemaId);
    const schema = byFile.get(file);
    if (schema === undefined) throw new Error(`the registry names ${schemaId}, and ${PUBLISHED_SCHEMA_DIRECTORY} holds no ${file}`);
    if (schema.json.$id !== schemaId) throw new Error(`${file} is ${schema.json.$id}, not ${schemaId}`);
    return { id: schemaId, file, path: publishedSchemaPath(file), sha256: schema.sha256, dialect: schema.json.$schema };
  };

  const coverage = required(census.envelopes.find((entry) => entry.operation === "coverage" && entry.envelope.result !== null), "coverage answer");
  const operationsBlock = required(coverage.envelope.result.value.operations, "operations block in the coverage answer");
  const served = new Set(operationsBlock.served_operations);
  const notServedData = new Map(operationsBlock.not_served_data.map((row) => [row.operation, row.data_needed]));
  const registered = contract.operations.map((operation) => operation.operation_id);
  const listed = [...operationsBlock.served_operations, ...operationsBlock.not_served_operations].sort();
  if (JSON.stringify(listed) !== JSON.stringify([...registered].sort())) {
    throw new Error(`the coverage answer serves and leaves unserved ${listed.join(", ")}, which is not the registry's ${registered.join(", ")}`);
  }

  const operations = contract.operations.map((operation) => {
    const id = operation.operation_id;
    const request = schemaOf(`lex-v3-${id}-request/1`);
    const result = schemaOf(operation.result_schema);
    const parameters = required(byFile.get(request.file).json.properties?.parameters, `parameters in ${request.file}`);
    const requiredNames = new Set(parameters.required ?? []);
    const isServed = served.has(id);
    if (!isServed && !notServedData.has(id)) throw new Error(`${id} is not served and the coverage answer names no data that would serve it`);
    return {
      id,
      served: isServed,
      rest: `${REST_PREFIX}${id}`,
      tool: isServed ? id : null,
      objectTypes: [...operation.result_object_types],
      parameters: Object.entries(parameters.properties ?? {}).map(([name, schema]) => ({ name, required: requiredNames.has(name), schema })),
      requestSchema: request,
      resultSchema: result,
      dataNeeded: isServed ? null : notServedData.get(id),
    };
  });

  const envelopeSchema = schemaOf(required(contract.envelope_schema, "envelope schema"));
  const envelopeJson = byFile.get(envelopeSchema.file).json;
  const refusalSchema = schemaOf(required(contract.refusal_schema, "refusal schema"));
  const context = required(census.envelopes.find((entry) => entry.envelope.refusal === null)?.envelope.context, "envelope context");

  const events = required(answers?.sampled?.find((entry) => entry.operation === "events")?.answer, "events answer");
  const eventsParameters = byFile.get(schemaFileFor("lex-v3-events-request/1")).json.properties.parameters.properties;

  return {
    registry: { schema: contract.registry_schema, sha256: contract.registry_sha256, version: contract.version },
    operations,
    served: operations.filter((operation) => operation.served).length,
    verdicts: [...contract.verdicts],
    refusals: contract.refusals.map((refusal) => ({ code: refusal.code, fields: [...refusal.mandatory_payload_fields] })),
    envelope: { schema: envelopeSchema, members: [...envelopeJson.required], contextMembers: Object.keys(context).sort() },
    refusalSchema,
    events: {
      names: [...required(eventsParameters.event?.enum, "event names in the events request schema")],
      cursorPattern: required(eventsParameters.after?.pattern, "cursor pattern in the events request schema"),
      delivery: required(events.delivery, "delivery statement in the events answer"),
    },
  };
}

/** A parameter's schema as the page prints it: its JSON, members in their schema's order, on one line. */
export function parameterSchemaText(schema) {
  return JSON.stringify(schema);
}

/** The REST example the page prints: the coverage report, asked with curl at `origin`. */
export function restExample(origin) {
  return `curl -X POST ${origin}${REST_PREFIX}coverage \\\n  -H 'content-type: application/json' \\\n  -d '{"operation_id":"coverage","parameters":{}}'`;
}

/** The MCP example the page prints: the same report, as a tools/call message. */
export function mcpExample() {
  return JSON.stringify({ jsonrpc: "2.0", id: 1, method: "tools/call", params: { name: "coverage", arguments: {} } }, null, 2);
}

/** The MCP client configuration the page prints, for a client that reads its servers from a JSON file. */
export function mcpClientExample(origin) {
  return JSON.stringify({ mcpServers: { lex: { type: "http", url: `${origin}${MCP_ENDPOINT}` } } }, null, 2);
}
