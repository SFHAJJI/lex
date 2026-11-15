// The API's OpenAPI 3.1 description (the owner's order of 2026-11-15: "documenter des API Data/IA"), generated from the same
// model the Developers page is (`developersModel`: the operation registry, the coverage answer's served list and every
// published schema), so the two cannot disagree. Each operation is one REST route, `POST /api/v3/{operation}`, whose request
// and result are the published JSON Schemas (draft 2020-12, which OpenAPI 3.1 reads as they are), referenced at the addresses
// the site serves them from; every answer is the envelope; the transport problems are the server's own list. The MCP endpoint
// is described beside them, with the same operations as tools. The build writes it at the site's root, `/openapi.json`.
//
// This module is data and pure functions only.

import {
  CLIENT_LIMIT, MAXIMUM_REQUEST_BYTES, MCP_ENDPOINT, MCP_METHODS, MCP_PROTOCOL_VERSION, REST_PREFIX, TRANSPORT_PROBLEMS, TRANSPORT_PROBLEM_SCHEMA,
} from './developers.mjs';

/** Where the build writes the description, from the site's root. */
export const OPENAPI_FILE = 'openapi.json';

/** The origin the description names as its server, and its schemas' addresses are under. */
export const OPENAPI_ORIGIN = 'https://law.soufien.lu';

/** A transport problem's body, as the server writes it (`TRANSPORT_PROBLEMS`): its schema, code, title and HTTP status. */
function problemSchema() {
  return {
    type: 'object',
    required: ['schema', 'type', 'title', 'status', 'code'],
    properties: {
      schema: { const: TRANSPORT_PROBLEM_SCHEMA },
      type: { type: 'string', pattern: '^urn:lex:v3:transport:' },
      title: { type: 'string' },
      status: { type: 'integer' },
      code: { enum: TRANSPORT_PROBLEMS.map((problem) => problem.code) },
    },
  };
}

/** The responses every REST route may give: the envelope, or one of the transport problems, grouped by status. */
function responsesFor(model, origin, operation) {
  const responses = {
    200: {
      description: `An envelope (${model.envelope.schema.id}): a result of type ${operation.objectTypes.join(' or ')}, or a typed refusal (${model.refusalSchema.id}). Its result is described by ${operation.resultSchema.id}.`,
      content: { 'application/json': { schema: { $ref: `${origin}${model.envelope.schema.path}` } } },
      'x-lex-result-schema': { $ref: `${origin}${operation.resultSchema.path}` },
    },
  };
  const byStatus = new Map();
  for (const problem of TRANSPORT_PROBLEMS) byStatus.set(problem.status, [...(byStatus.get(problem.status) ?? []), problem.code]);
  byStatus.set(CLIENT_LIMIT.status, [...(byStatus.get(CLIENT_LIMIT.status) ?? []), 'rate_limited']);
  for (const [status, codes] of [...byStatus.entries()].sort((a, b) => a[0] - b[0])) {
    responses[status] = {
      description: status === CLIENT_LIMIT.status
        ? `Too many requests from one address: at most ${CLIENT_LIMIT.apiPerMinute} a minute and ${CLIENT_LIMIT.apiInFlight} at once.`
        : `A transport problem: ${codes.join(', ')}.`,
      content: { 'application/json': { schema: { $ref: '#/components/schemas/TransportProblem' } } },
    };
  }
  return responses;
}

/**
 * The OpenAPI 3.1 document for `model` (`developersModel`'s), naming `origin` as its server: one path per registered
 * operation, served or not (one that is not served refuses inside the envelope, and says so), and the MCP endpoint.
 */
export function openApiDocument(model, origin = OPENAPI_ORIGIN) {
  const paths = {};
  for (const operation of model.operations) {
    paths[`${REST_PREFIX}${operation.id}`] = {
      post: {
        operationId: operation.id,
        summary: operation.served ? operation.id : `${operation.id} (not served from this mount)`,
        description: operation.served
          ? `Asks the ${operation.id} operation. The same operation is the MCP tool ${operation.tool}.`
          : `Registered but not served from this mount: it answers the typed refusal ${operation.refusalCode} (it needs ${operation.dataNeeded ?? 'data this mount does not hold'}).`,
        'x-lex-served': operation.served,
        'x-lex-mcp-tool': operation.tool,
        requestBody: {
          required: true,
          description: `${operation.requestSchema.id}: { "operation_id": "${operation.id}", "parameters": { … } }, at most ${MAXIMUM_REQUEST_BYTES} bytes.`,
          content: { 'application/json': { schema: { $ref: `${origin}${operation.requestSchema.path}` } } },
        },
        responses: responsesFor(model, origin, operation),
      },
    };
  }
  paths[MCP_ENDPOINT] = {
    post: {
      operationId: 'mcp',
      summary: 'Model Context Protocol (JSON-RPC 2.0)',
      description: `The same operations as tools, for AI agents: ${MCP_METHODS.join(', ')}; protocol ${MCP_PROTOCOL_VERSION}. tools/list names every operation above; tools/call asks one with its parameters and returns its envelope.`,
      requestBody: {
        required: true,
        content: {
          'application/json': {
            schema: {
              type: 'object',
              required: ['jsonrpc', 'method'],
              properties: { jsonrpc: { const: '2.0' }, id: { type: ['string', 'integer'] }, method: { enum: [...MCP_METHODS] }, params: { type: 'object' } },
            },
          },
        },
      },
      responses: { 200: { description: 'A JSON-RPC 2.0 response; for tools/call, the operation\'s envelope as structured content.' } },
    },
  };
  return {
    openapi: '3.1.0',
    jsonSchemaDialect: 'https://json-schema.org/draft/2020-12/schema',
    info: {
      title: 'Lex V3 API',
      version: String(model.registry.version ?? model.registry.schema),
      summary: 'Luxembourg and EU law as published, on any date: search, read, compare, cite and verify; the assistant\'s answers.',
      description: `Generated from the operation registry (${model.registry.schema}, sha256 ${model.registry.sha256}) and the published JSON Schemas. ${model.served} of ${model.operations.length} operations are served. Every answer is an envelope; no answer is free text.`,
      license: { name: 'Texts: the publishers\' licences; see each answer' },
    },
    servers: [{ url: origin }],
    paths,
    components: { schemas: { TransportProblem: problemSchema() } },
    'x-lex-registry': { schema: model.registry.schema, sha256: model.registry.sha256 },
  };
}
