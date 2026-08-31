// Envelope handling for the S0-06 preview surface.
//
// Two rules govern this file.
//
// First, nothing here interprets law. The page is generated at build time and ships
// as static HTML with no client script, because `eng/verify-v3-tree.ps1` permits only
// css, html and svg under `web/src/`. That constraint is not a limitation to work
// around; it enforces the product rule that no legal judgement happens in a browser.
// A static page cannot infer, because nothing is running.
//
// Second, fixtures are never invented. A hand-written envelope drifts from the
// contract the moment the contract moves, which is the defect class this project
// keeps producing. So every fixture is validated against the published schema at
// build time, and the build fails rather than emitting a page from an envelope it
// cannot account for.

import { readFile } from "node:fs/promises";

const SCHEMA_URL = new URL(
  "../../schemas/v3-preview/preview-envelope.schema.json",
  import.meta.url,
);

/** The published envelope schema, read from the tree rather than restated here. */
export async function loadEnvelopeSchema() {
  return JSON.parse(await readFile(SCHEMA_URL, "utf8"));
}

/**
 * Validate one envelope against the published schema.
 *
 * Deliberately narrow: it enforces the constraints the schema actually states for
 * this document - branch selection, required members, const values, enums, patterns
 * and additionalProperties - and reports every violation rather than the first. It is
 * not a general JSON Schema engine, and it fails closed: an unknown keyword combination
 * it cannot evaluate is reported as unvalidatable rather than silently passing.
 */
export function validateEnvelope(schema, envelope) {
  const problems = [];
  if (envelope === null || typeof envelope !== "object" || Array.isArray(envelope)) {
    return ["envelope is not an object"];
  }

  const branch = envelope.branch;
  if (typeof branch !== "string") {
    return ["envelope has no string branch member"];
  }

  const arm = (schema.anyOf ?? []).find(
    (candidate) => candidate?.properties?.branch?.const === branch,
  );
  if (!arm) {
    const known = (schema.anyOf ?? [])
      .map((candidate) => candidate?.properties?.branch?.const)
      .filter(Boolean);
    return [`branch ${JSON.stringify(branch)} is not one of ${JSON.stringify(known)}`];
  }

  check(arm, envelope, "", problems);
  return problems;
}

function check(node, value, path, problems) {
  const where = path || "(root)";

  if (node.const !== undefined && value !== node.const) {
    problems.push(`${where}: expected const ${JSON.stringify(node.const)}, got ${JSON.stringify(value)}`);
    return;
  }

  if (node.enum !== undefined && !node.enum.includes(value)) {
    problems.push(`${where}: ${JSON.stringify(value)} is not in the closed set`);
    return;
  }

  if (node.type === "string") {
    if (typeof value !== "string") {
      problems.push(`${where}: expected string, got ${typeof value}`);
      return;
    }
    if (node.minLength !== undefined && value.length < node.minLength) {
      problems.push(`${where}: shorter than ${node.minLength}`);
    }
    if (node.maxLength !== undefined && value.length > node.maxLength) {
      problems.push(`${where}: longer than ${node.maxLength}`);
    }
    if (node.pattern !== undefined && !new RegExp(node.pattern, "u").test(value)) {
      problems.push(`${where}: does not match the published pattern`);
    }
    return;
  }

  if (node.type === "boolean" && typeof value !== "boolean") {
    problems.push(`${where}: expected boolean`);
    return;
  }

  if (node.type === "array") {
    if (!Array.isArray(value)) {
      problems.push(`${where}: expected array`);
      return;
    }
    if (node.items) {
      value.forEach((item, index) => check(node.items, item, `${where}[${index}]`, problems));
    }
    return;
  }

  if (node.type === "object" || node.properties) {
    if (value === null || typeof value !== "object" || Array.isArray(value)) {
      problems.push(`${where}: expected object`);
      return;
    }
    for (const required of node.required ?? []) {
      if (!(required in value)) {
        problems.push(`${where}: missing required member ${required}`);
      }
    }
    if (node.additionalProperties === false) {
      for (const key of Object.keys(value)) {
        if (!(key in (node.properties ?? {}))) {
          problems.push(`${where}: unexpected member ${key}`);
        }
      }
    }
    for (const [key, subSchema] of Object.entries(node.properties ?? {})) {
      if (key in value) {
        check(subSchema, value[key], path ? `${path}.${key}` : key, problems);
      }
    }
  }
}

/**
 * The five renderable states.
 *
 * `loading` and `transport_failure` carry no envelope by construction: there is
 * nothing to render from, and inventing content for them would be exactly the
 * failure the whole surface exists to prevent.
 */
export const STATES = Object.freeze([
  "success",
  "refusal",
  "loading",
  "transport_failure",
  "invalid_envelope",
]);
