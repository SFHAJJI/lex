// The Developers page's facts (the specification of record's R55: "Developers at /developers, generated from the registry";
// R46: free and anonymous, "the limits enforced and the guarantees actually supported"; the plan's item 8.4), generated from
// the contracts this release holds, never written by hand:
// - the operations, the refusal codes with the fields each payload must carry, the verdicts and the registry's digest: the
//   census's `contract` block (`schemas/v3-platform/envelope-samples.json`), which `V3EnvelopeSamplesTests` reads from the
//   reviewed registry itself (`V3OperationRegistry.Reviewed`) and holds byte for byte;
// - which operations this server serves, and, for each one it serves from no data, the typed refusal every request for it
//   answers inside the envelope and the data that would serve it: the census's coverage answer, whose operations block the
//   platform computes from its routes (`V3RestRouteBinding.Served`, `V3UnservedOperations`);
// - each refusal code's payload schema, and the out-of-scope list (what Lex never holds, with each class's official place
//   and who to ask): the census's `contract` block, from `V3OperationRegistry` and `V3OutOfScope`;
// - each operation's parameters and the envelope's members: the request schemas and the envelope schema, the very files the
//   API embeds and checks against the registry's digests when it loads them (`V3PlatformSchemaDocuments`);
// - the events log's delivery, in the platform's own words: the census of answers (`answer-samples.json`).
//
// A few facts live only in the API's code: the MCP protocol version, methods and instructions, the request body's ceiling,
// the transport failures below the envelope, and the replay guarantees with the tests that hold them. They are named here
// once, and `reference-pages.test.mjs` holds each to the C# it comes from, so a change there fails the web suite until this
// page says it.
//
// The live examples (the plan's item 8.4: "live examples from the release's own data") are not built: both servers render
// this page, as they render the home page, from their own answer to `browse` for the first act they hold (`sitePageFor`,
// `ssr-entry.jsx`), so the page shows what the release serving it answers; the page as built says it carries none.
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
export const MCP_METHODS = Object.freeze(["initialize", "ping", "tools/list", "tools/call"]);

/**
 * What the MCP endpoint tells a client at `initialize`, as its `instructions` (the MCP specification, 2025-06-18; the plan's
 * item 8.5): `V3McpJsonRpc.Instructions`, word for word, in the API's English.
 */
export const MCP_INSTRUCTIONS =
  "Lex answers from the Luxembourg and EU legal texts this server holds, and every tool returns one Lex V3 envelope " +
  "(lex-v3-envelope/1) as structuredContent, the same envelope the REST route POST /api/v3/{operation} returns. " +
  "A refusal is an answer, not an error: its verdict is refuse, isError stays false, and its refusal names one code of the " +
  "registry's closed twenty with a payload saying what was asked, what this server holds and what would answer; read the " +
  "payload rather than retrying another way. A transport or protocol failure is a JSON-RPC error, never an envelope. " +
  "A Luxembourg date is the publisher's applicability date; an EU wording date is never an applicability date; the two are " +
  "never merged. An absence in this server is not evidence that a law does not exist. Every envelope names the registry " +
  "digest it is bound to (registry_sha256) and its context says how current the data is. Lex shows the published text of " +
  "the law; it does not say whether or how it applies to anyone's situation and is not legal advice.";

/**
 * The replay guarantees of `33-product-spec.md`, G1 to G5, in its order, each with the test that holds it on the real
 * handler (`V3ReplayGuaranteesTests`, which `reference-pages.test.mjs` reads); what the page says each one is, as this server
 * supports it, is in the chrome table under `developers.replay.guarantees`.
 */
export const REPLAY_GUARANTEES = Object.freeze([
  Object.freeze({ id: "G1", test: "AReplacedPublisherFileIsAFileReplacedEventAndANewVersionIdOnlyWhenItsTextChanged" }),
  Object.freeze({ id: "G2", test: "EveryServedOperationAnswersTheSameBytesFromTheSameSnapshotFromACopyOfItAndLater" }),
  Object.freeze({ id: "G3", test: "NothingIsHardDeletedAcrossBuildsEveryEventIsCarriedAndEveryStateTheLogNamesStillVerifies" }),
  Object.freeze({ id: "G4", test: "EverySnapshotTheLogNamesAnswersAsObservedWhatThatBuildAnsweredBoundedByItsBuildTime" }),
  Object.freeze({ id: "G5", test: "OneEvidenceBundleAndThePublisherFileAreEnoughToCheckItsTextAndRecomputeItsDerivedDigests" }),
]);

/**
 * How many days the retention rule keeps the last build of each day (`V3GenerationRetention.NightlyDays`, S7-A09), which G3
 * says.
 */
export const RETENTION_NIGHTLY_DAYS = 90;

/**
 * When the registry took its current version, and the version before it (the plan's item 6.10, 2026-10-27): the change
 * policy's history names both.
 */
export const REGISTRY_VERSION_SINCE = "2026-10-27";
export const PREVIOUS_REGISTRY_SCHEMA = "lex-v3-operation-registry/1";

/** Where the replay guarantees' tests are, in the repository. */
export const REPLAY_TESTS = "tests/Lex.V3.Ingest.Tests/V3ReplayGuaranteesTests.cs";

/** The request the server renders this page's live examples from (`sitePageFor`): the first act it holds, one row. */
export const LIVE_EXAMPLE_REQUEST = Object.freeze({ operation_id: "browse", parameters: Object.freeze({ limit: 1 }) });

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
  const match = /^lex-v3-([a-z_]+)-(request|result|payload)\/1$/.exec(schemaId);
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
  const notServedData = new Map(operationsBlock.not_served_data.map((row) => [row.operation, row]));
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
    const unserved = isServed ? null : notServedData.get(id);
    if (unserved !== null && (typeof unserved.reason !== "string" || typeof unserved.requested_mode !== "string")) {
      throw new Error(`${id} is not served and the coverage answer names no typed refusal for it`);
    }
    return {
      id,
      served: isServed,
      rest: `${REST_PREFIX}${id}`,
      // Every registered operation is a tool, as every one has a REST route: the served ones, and the ones served from no
      // data, which refuse inside the envelope.
      tool: id,
      objectTypes: [...operation.result_object_types],
      parameters: Object.entries(parameters.properties ?? {}).map(([name, schema]) => ({ name, required: requiredNames.has(name), schema })),
      requestSchema: request,
      resultSchema: result,
      dataNeeded: unserved?.data_needed ?? null,
      refusalCode: unserved?.reason ?? null,
      requestedMode: unserved?.requested_mode ?? null,
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
    refusals: contract.refusals.map((refusal) => {
      const payloadSchema = schemaOf(required(refusal.payload_schema, `payload schema of ${refusal.code}`));
      if (payloadSchema.sha256 !== refusal.payload_schema_sha256) throw new Error(`${payloadSchema.file} is not the payload schema the registry names for ${refusal.code}`);
      const declared = byFile.get(payloadSchema.file).json.required ?? [];
      if (JSON.stringify(declared) !== JSON.stringify(refusal.mandatory_payload_fields)) throw new Error(`${payloadSchema.file} requires ${declared.join(", ")}, not the registry's fields for ${refusal.code}`);
      return { code: refusal.code, fields: [...refusal.mandatory_payload_fields], payloadSchema };
    }),
    outOfScope: outOfScopeOf(required(contract.out_of_scope, "out-of-scope list in the census's contract block")),
    envelope: { schema: envelopeSchema, members: [...envelopeJson.required], contextMembers: Object.keys(context).sort() },
    refusalSchema,
    events: {
      names: [...required(eventsParameters.event?.enum, "event names in the events request schema")],
      cursorPattern: required(eventsParameters.after?.pattern, "cursor pattern in the events request schema"),
      delivery: required(events.delivery, "delivery statement in the events answer"),
    },
  };
}

/** The out-of-scope list as the page lists it: its identity and digest, and each class as the census holds it. */
function outOfScopeOf(list) {
  const classes = required(list.classes, "classes in the out-of-scope list");
  return {
    schema: required(list.schema, "the out-of-scope list's identity"),
    sha256: required(list.sha256, "the out-of-scope list's digest"),
    classes: classes.map((entry) => {
      const who = required(entry.who_to_ask, `who to ask about ${entry.class}`);
      if (!/^https:\/\//.test(entry.official_source) || !/^https:\/\//.test(who.link)) throw new Error(`${entry.class}: its official place and who to ask are links`);
      return {
        id: entry.class, jurisdiction: entry.jurisdiction, instrument: entry.instrument, reason: entry.reason,
        officialSource: entry.official_source, whoToAsk: { name: who.name, link: who.link, role: who.role }, examples: [...entry.examples],
      };
    }),
  };
}

/**
 * The live examples the page shows, from the answer the server rendered it from (`browse` for the first act it holds,
 * `LIVE_EXAMPLE_REQUEST`), or null as built: the envelope it was answered with, whole, whether it refused, and, when the
 * answer names an act, the act, the date of its latest version and its first language, which the next requests ask about.
 */
export function liveExamples(envelope) {
  if (envelope === null || envelope === undefined) return null;
  if (typeof envelope !== "object" || envelope.operation_id !== LIVE_EXAMPLE_REQUEST.operation_id) throw new Error("the Developers page's live examples are drawn from a browse answer");
  const work = envelope.result?.value?.works?.[0] ?? null;
  return {
    envelope,
    refused: envelope.refusal !== null && envelope.refusal !== undefined,
    work: work === null ? null : {
      identifier: required(work.identifier, "the first act's identifier"),
      date: work.latest_applicability_date ?? null,
      language: work.languages?.[0] ?? null,
    },
  };
}

/** The live example's request, with curl at `origin`: `browse` for the first act held. */
export function liveRestExample(origin) {
  return `curl -X POST ${origin}${REST_PREFIX}${LIVE_EXAMPLE_REQUEST.operation_id} \\\n  -H 'content-type: application/json' \\\n  -d '${JSON.stringify(LIVE_EXAMPLE_REQUEST)}'`;
}

/** The answer the live example got, as the page prints it: the envelope, whole, indented. */
export function liveAnswerText(envelope) {
  return JSON.stringify(envelope, null, 2);
}

/** The next requests about the first act held: its dossier over REST, and its text on its latest version's date over MCP. */
export function liveNextExamples(origin, work) {
  const dossier = { operation_id: "dossier", parameters: { identifier: work.identifier } };
  const asOf = { identifier: work.identifier, ...(work.date === null ? {} : { date: work.date }), ...(work.language === null ? {} : { language: work.language }) };
  return {
    rest: `curl -X POST ${origin}${REST_PREFIX}dossier \\\n  -H 'content-type: application/json' \\\n  -d '${JSON.stringify(dossier)}'`,
    mcp: JSON.stringify({ jsonrpc: "2.0", id: 2, method: "tools/call", params: { name: "as_of", arguments: asOf } }, null, 2),
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
