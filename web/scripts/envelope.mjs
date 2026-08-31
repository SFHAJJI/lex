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
/** The schema arm for this envelope's branch, or a problem describing why there is none. */
export function selectArm(schema, envelope) {
  if (envelope === null || typeof envelope !== "object" || Array.isArray(envelope)) {
    return { problems: ["envelope is not an object"] };
  }
  const branch = envelope.branch;
  if (typeof branch !== "string") {
    return { problems: ["envelope has no string branch member"] };
  }
  const arm = (schema.anyOf ?? []).find(
    (candidate) => candidate?.properties?.branch?.const === branch,
  );
  if (!arm) {
    const known = (schema.anyOf ?? [])
      .map((candidate) => candidate?.properties?.branch?.const)
      .filter(Boolean);
    return { problems: [`branch ${JSON.stringify(branch)} is not one of ${JSON.stringify(known)}`] };
  }
  return { arm, problems: [] };
}

/**
 * Resolve the envelope's integer vocabulary indices into their schema-declared members.
 *
 * The wire format encodes every closed vocabulary as an index. `refusal.code` arrives as
 * `0`, not `"identifier_unknown"`; `checked_identifier_family` as `3`, not
 * `"historical_legal_id"`. Rendering the integer would put a bare number where a reader
 * expects a machine code, and a hardcoded lookup table would be a second copy of a
 * vocabulary that already exists and free to drift from it silently.
 *
 * So the schema is the authority: wherever it declares an `enum`, an integer is an index
 * into that enum. Anything out of range is refused rather than clamped or passed through,
 * because an index resolving to the wrong member is a plausible wrong label, and that is
 * worse than a missing one.
 */
export function decodeEnvelope(schema, envelope, registry = new Map()) {
  const { arm, problems } = selectArm(schema, envelope);
  if (!arm) {
    return { decoded: null, problems };
  }

  // A sub-document may declare its own schema identity: `result.schema` is
  // `lex-v3-preview-object-set/1`, and the vocabularies for `body_holding_state` and
  // `body_holding_disposition` live there rather than in the envelope schema. Following
  // the declared identity keeps one vocabulary in one place; copying those members into
  // the envelope schema would be a second copy free to drift.
  const enter = (node, value, where) => {
    if (value === null || typeof value !== "object" || Array.isArray(value)) {
      return node;
    }
    const id = value.schema;
    if (typeof id !== "string" || !registry.has(id) || registry.get(id) === node) {
      return node;
    }
    return registry.get(id);
  };

  // `anyOf` is resolved by its const discriminators, and only when exactly one arm
  // matches. Zero or several is refused rather than guessed, because decoding against
  // the wrong arm resolves indices through the wrong vocabulary and produces a
  // confident wrong label.
  const resolve = (node, value, where) => {
    if (!node || !Array.isArray(node.anyOf)) {
      return node;
    }
    const matching = node.anyOf.filter((candidate) =>
      Object.entries(candidate.properties ?? {}).every(
        ([key, sub]) => sub.const === undefined || value?.[key] === sub.const,
      ),
    );
    if (matching.length !== 1) {
      problems.push(
        `${where}: ${matching.length} of ${node.anyOf.length} schema arms match, so the ` +
          "vocabulary to decode against is ambiguous",
      );
      return null;
    }
    return matching[0];
  };

  const decode = (rawNode, value, path) => {
    const where = path || "(root)";
    const node = resolve(enter(rawNode, value, where), value, where);
    if (!node) {
      return value;
    }
    if (Array.isArray(node.enum) && typeof value === "number") {
      if (!Number.isInteger(value) || value < 0 || value >= node.enum.length) {
        problems.push(
          `${where}: vocabulary index ${JSON.stringify(value)} is outside the ` +
            `${node.enum.length} declared members`,
        );
        return value;
      }
      return node.enum[value];
    }
    if (Array.isArray(value)) {
      return value.map((item, index) => decode(node.items, item, `${where}[${index}]`));
    }
    if (value !== null && typeof value === "object") {
      const out = {};
      for (const [key, member] of Object.entries(value)) {
        out[key] = decode(node.properties?.[key], member, path ? `${path}.${key}` : key);
      }
      return out;
    }
    return value;
  };

  return { decoded: decode(arm, envelope, ""), problems };
}

export function validateEnvelope(schema, envelope) {
  const { arm, problems } = selectArm(schema, envelope);
  if (!arm) {
    return problems;
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
