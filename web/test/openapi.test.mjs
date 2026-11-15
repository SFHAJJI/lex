// The API's OpenAPI 3.1 description (the owner's order of 2026-11-15): generated from the same contracts as the Developers
// page, one route per registered operation, every referenced schema one the site publishes, the transport problems the
// server's own, and written by the live build at the site's root.

import assert from "node:assert/strict";
import test from "node:test";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

import { publishedSchemaFiles, readDevelopersContracts } from "../scripts/developers-contracts.mjs";
import { CLIENT_LIMIT, MCP_ENDPOINT, REST_PREFIX, TRANSPORT_PROBLEMS } from "../scripts/developers.mjs";
import { OPENAPI_FILE, OPENAPI_ORIGIN, openApiDocument } from "../scripts/openapi.mjs";
import { buildLive } from "../scripts/build-live.mjs";

const model = await readDevelopersContracts();

test("the OpenAPI document names every registered operation once, as a POST route, served or not, and the MCP endpoint", () => {
  const doc = openApiDocument(model);
  assert.equal(doc.openapi, "3.1.0");
  assert.deepEqual(doc.servers, [{ url: OPENAPI_ORIGIN }]);
  assert.deepEqual(Object.keys(doc.paths).filter((path) => path !== MCP_ENDPOINT).sort(), model.operations.map((operation) => `${REST_PREFIX}${operation.id}`).sort());
  for (const operation of model.operations) {
    const post = doc.paths[`${REST_PREFIX}${operation.id}`].post;
    assert.equal(post.operationId, operation.id);
    assert.equal(post["x-lex-served"], operation.served, operation.id);
    assert.equal(post["x-lex-mcp-tool"], operation.tool);
    if (!operation.served) assert.ok(post.description.includes(operation.refusalCode), `${operation.id} names its refusal`);
  }
  assert.ok(doc.paths[MCP_ENDPOINT].post.description.includes("tools/call"));
  assert.equal(doc["x-lex-registry"].sha256, model.registry.sha256);
});

test("every schema it references is one the site publishes, and every status is the server's own", async () => {
  const doc = openApiDocument(model);
  const published = new Set((await publishedSchemaFiles()).map(([path]) => `${OPENAPI_ORIGIN}/${path}`));
  const refs = [];
  (function walk(node) {
    if (Array.isArray(node)) node.forEach(walk);
    else if (node && typeof node === "object") {
      if (typeof node.$ref === "string") refs.push(node.$ref);
      Object.values(node).forEach(walk);
    }
  })(doc.paths);
  assert.ok(refs.length > 2 * model.operations.length, `the routes reference their schemas (${refs.length})`);
  for (const ref of refs) assert.ok(ref === "#/components/schemas/TransportProblem" || published.has(ref), `${ref} is published`);
  const statuses = new Set(Object.keys(doc.paths[`${REST_PREFIX}ask`].post.responses).map(Number));
  for (const problem of TRANSPORT_PROBLEMS) assert.ok(statuses.has(problem.status), `${problem.code}: ${problem.status}`);
  assert.ok(statuses.has(CLIENT_LIMIT.status) && statuses.has(200));
  assert.deepEqual(doc.components.schemas.TransportProblem.properties.code.enum, TRANSPORT_PROBLEMS.map((problem) => problem.code));
});

test("the live build writes it at the site's root, as the generator writes it", async () => {
  const destination = await mkdtemp(join(tmpdir(), "lex-openapi-"));
  try {
    await buildLive(pathToFileURL(`${destination}/`), { buildTag: "openapi-test" });
    const written = JSON.parse(await readFile(join(destination, OPENAPI_FILE), "utf8"));
    assert.deepEqual(written, openApiDocument(model));
  } finally {
    await rm(destination, { recursive: true, force: true });
  }
});
