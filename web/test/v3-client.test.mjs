import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

import { askV3, SERVED_OPERATIONS } from "../scripts/v3-client.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const answers = JSON.parse(await readFile(new URL("../../schemas/v3-platform/answer-samples.json", import.meta.url), "utf8"));
const { contract } = census;
const coverageEnvelope = census.envelopes.find((entry) => entry.operation === "coverage" && entry.envelope.result !== null).envelope;

function respond(status, contentType, body) {
  return {
    status,
    headers: { get: (name) => (name.toLowerCase() === "content-type" ? contentType : null) },
    json: async () => (typeof body === "string" ? JSON.parse(body) : body),
  };
}

function recordingFetch(answer) {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ url, init });
    return typeof answer === "function" ? answer() : answer;
  };
  return { calls, fetchImpl };
}

async function silently(action) {
  const logged = [];
  const saved = {};
  for (const name of ["log", "info", "warn", "error", "debug"]) {
    saved[name] = console[name];
    console[name] = (...values) => logged.push([name, values]);
  }
  try {
    return { value: await action(), logged };
  } finally {
    Object.assign(console, saved);
  }
}

test("the operations asked are exactly the ones the platform says it serves", () => {
  const coverage = answers.sampled.find((entry) => entry.operation === "coverage");
  assert.deepEqual([...SERVED_OPERATIONS].sort(), [...coverage.answer.operations.served_operations].sort());
});

test("a question is one same-origin POST whose parameters travel in the body alone", async () => {
  const { calls, fetchImpl } = recordingFetch(respond(200, "application/json; charset=utf-8", coverageEnvelope));
  const { value, logged } = await silently(() => askV3("coverage", {}, { contract, objectType: "coverage_report", fetchImpl }));
  assert.equal(value.state, "success");
  assert.equal(value.envelope.result.object_type, "coverage_report");
  assert.equal(calls.length, 1, "asked once");
  const [{ url, init }] = calls;
  assert.equal(url, "/api/v3/coverage", "relative, same origin, and nothing in the query string");
  assert.equal(init.method, "POST");
  assert.deepEqual(init.headers, { "content-type": "application/json" }, "no header but the body's media type");
  assert.equal(init.body, JSON.stringify({ operation_id: "coverage", parameters: {} }));
  assert.equal(init.credentials, "omit");
  assert.equal(init.cache, "no-store");
  assert.equal(init.referrerPolicy, "no-referrer");
  assert.equal(init.redirect, "error");
  assert.deepEqual(logged, [], "nothing about the question or the answer is logged");

  const search = recordingFetch(respond(200, "application/json", coverageEnvelope));
  await askV3("search", { query: "garantie locative", language: "fra" }, { contract, fetchImpl: search.fetchImpl });
  assert.equal(search.calls[0].url, "/api/v3/search", "the query text is not in the URL");
  assert.ok(!search.calls[0].url.includes("garantie"));
});

test("a refusal is a state to render, not an error", async () => {
  const refusal = census.envelopes.find((entry) => entry.operation === "coverage" && entry.envelope.refusal !== null).envelope;
  const { fetchImpl } = recordingFetch(respond(200, "application/json", refusal));
  const answer = await askV3("coverage", {}, { contract, fetchImpl });
  assert.equal(answer.state, "refusal");
  assert.equal(answer.envelope.refusal.code, "no_corpus_mounted");
});

test("what is not an envelope is named for what it is, and never retried", async () => {
  const problem = recordingFetch(respond(404, "application/problem+json", { code: "operation_not_served" }));
  assert.deepEqual(await askV3("coverage", {}, { contract, fetchImpl: problem.fetchImpl }),
    { state: "transport_failure", code: "operation_not_served", status: 404 });
  assert.equal(problem.calls.length, 1);

  const network = recordingFetch(() => { throw new TypeError("Failed to fetch"); });
  assert.deepEqual(await askV3("coverage", {}, { contract, fetchImpl: network.fetchImpl }),
    { state: "transport_failure", code: "network_error" });
  assert.equal(network.calls.length, 1, "a failure is reported once, not retried");

  const html = recordingFetch(respond(200, "text/html", "<p>"));
  const page = await askV3("coverage", {}, { contract, fetchImpl: html.fetchImpl });
  assert.equal(page.state, "invalid_envelope");
  assert.match(page.reason, /text\/html/);

  const tampered = structuredClone(coverageEnvelope);
  tampered.registry_sha256 = "0".repeat(64);
  const forged = recordingFetch(respond(200, "application/json", tampered));
  const read = await askV3("coverage", {}, { contract, fetchImpl: forged.fetchImpl });
  assert.equal(read.state, "invalid_envelope");
  assert.match(read.reason, /reviewed registry/);

  const wrongAnswer = recordingFetch(respond(200, "application/json", coverageEnvelope));
  const other = await askV3("search", { query: "x", language: "fra" }, { contract, fetchImpl: wrongAnswer.fetchImpl });
  assert.equal(other.state, "invalid_envelope", "an answer to another operation is not this question's answer");
});

test("an operation the API does not serve is refused before anything is sent", async () => {
  const { calls, fetchImpl } = recordingFetch(respond(200, "application/json", coverageEnvelope));
  await assert.rejects(() => askV3("concepts", {}, { contract, fetchImpl }), /not an operation the API serves/);
  await assert.rejects(() => askV3("coverage", "x", { contract, fetchImpl }), /parameters are an object/);
  assert.equal(calls.length, 0);
});

test("a cancelled question stays cancelled", async () => {
  const aborted = recordingFetch(() => { const error = new Error("aborted"); error.name = "AbortError"; throw error; });
  await assert.rejects(() => askV3("coverage", {}, { contract, fetchImpl: aborted.fetchImpl }), { name: "AbortError" });
});
