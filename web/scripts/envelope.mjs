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
/**
 * Resolve a local `$ref` against the document that contains it.
 *
 * Only same-document pointers are resolvable. An external ref, a malformed pointer, an
 * unresolvable path or a cycle all return null after recording a problem, so a `$ref`
 * this reader cannot follow refuses the envelope instead of passing it. Codex proved the
 * previous behaviour: a node carrying only `$ref` matched none of the keyword branches,
 * so `check` fell through and returned clean, and three mutations across a real `$ref`
 * boundary were reported as valid.
 */
export function resolveLocalRef(ref, doc, where, problems, seen, docs = []) {
  if (typeof ref !== "string" || ref.length === 0) {
    problems.push(`${where}: $ref ${JSON.stringify(ref)} is not a reference`);
    return null;
  }
  if (seen.has(ref)) {
    problems.push(`${where}: $ref ${ref} is cyclic`);
    return null;
  }

  // A ref may name this document (`#/...`) or a schema supplied to this reader by its
  // own `$id`. Anything else would have to be fetched, and a reader that fetches is a
  // reader that can be pointed somewhere else, so it is refused.
  let base = doc;
  let pointer = ref;
  if (!ref.startsWith("#")) {
    const hash = ref.indexOf("#");
    const id = hash === -1 ? ref : ref.slice(0, hash);
    const known = docs.find((candidate) => candidate && candidate.$id === id);
    if (!known) {
      problems.push(
        `${where}: $ref ${JSON.stringify(ref)} names no schema supplied to this reader, ` +
          "so it cannot be checked",
      );
      return null;
    }
    base = known;
    pointer = hash === -1 ? "#" : ref.slice(hash);
  }

  const path = pointer.slice(1);
  if (path === "") {
    return base;
  }
  if (!path.startsWith("/")) {
    problems.push(`${where}: $ref ${ref} is not a JSON pointer`);
    return null;
  }
  let node = base;
  for (const raw of path.slice(1).split("/")) {
    const token = raw.replace(/~1/g, "/").replace(/~0/g, "~");
    if (node === null || typeof node !== "object" || !(token in node)) {
      problems.push(`${where}: $ref ${ref} does not resolve in this document`);
      return null;
    }
    node = node[token];
  }
  return node;
}

/**
 * Assertion keywords this reader implements.
 *
 * Anything else is refused rather than ignored. Silently skipping an unimplemented
 * keyword is the defect class behind O1: `prefixItems` and `$ref` were both absent, so
 * every constraint they carried evaluated to "no problems found". A validator that does
 * not understand a constraint must say so, not pass.
 */
const SUPPORTED_KEYWORDS = new Set([
  "$ref", "$id", "$schema", "$defs", "$comment", "title", "description", "examples", "default",
  "const", "enum", "type", "properties", "required", "additionalProperties",
  "items", "prefixItems", "minItems", "maxItems",
  "minProperties", "maxProperties", "uniqueItems",
  "minLength", "maxLength", "pattern", "format",
  "anyOf", "allOf", "if", "then", "else",
]);

/**
 * Extension keywords that carry no assertion, listed one by one.
 *
 * Deliberately not matched by an `x_` prefix. A prefix rule would let a future
 * `x_must_be_hex` be silently ignored, which is the same fail-open this reader was
 * repaired to stop. Adding an annotation here is a decision someone has to make on
 * purpose.
 */
const ANNOTATION_KEYWORDS = new Set(["x_runtime_invariants", "x_max_stream_bytes"]);

/**
 * The two `format` values these schemas actually use, both checked rather than treated
 * as annotations.
 *
 * An unlisted format is refused by `check` below, so adding one to a schema without
 * teaching this reader how to verify it fails loudly instead of passing silently.
 */
const FORMATS = new Map([
  [
    "uri",
    (value) => {
      try {
        return Boolean(new URL(value));
      } catch {
        return false;
      }
    },
  ],
  [
    "date-time",
    (value) =>
      /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/u.test(value) &&
      !Number.isNaN(Date.parse(value)),
  ],
]);

export function decodeEnvelope(schema, envelope, registry = new Map()) {
  const { arm, problems } = selectArm(schema, envelope);
  if (!arm) {
    return { decoded: null, problems };
  }

  // Every closed vocabulary the schema and its registered sub-schemas declare, collected
  // once. These are the only index spaces a value is allowed to be decoded through.
  const vocabularies = [];
  const collect = (node, seen) => {
    if (!node || typeof node !== "object" || seen.has(node)) return;
    seen.add(node);
    if (Array.isArray(node.enum) && node.enum.every((m) => typeof m === "string")) {
      vocabularies.push(node.enum);
    }
    for (const child of Object.values(node)) {
      if (Array.isArray(child)) {
        child.forEach((member) => collect(member, seen));
      } else {
        collect(child, seen);
      }
    }
  };
  const seen = new Set();
  collect(schema, seen);
  for (const sub of registry.values()) {
    collect(sub, seen);
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

  // Every schema this reader was handed: the root plus each registered sub-schema. A
  // `$ref` may name one of them by `$id`, and nothing outside this set is resolvable.
  const knownDocs = [schema, ...registry.values()];

  const decode = (rawNode, value, path, doc = schema, seen = new Set()) => {
    const where = path || "(root)";
    // A `$ref` is followed before anything else, and against the document that contains
    // it: a ref inside a registered sub-schema resolves within that sub-schema.
    if (rawNode && typeof rawNode === "object" && rawNode.$ref !== undefined) {
      const target = resolveLocalRef(rawNode.$ref, doc, where, problems, seen, knownDocs);
      if (!target) {
        return value;
      }
      return decode(target, value, path, doc, new Set([...seen, rawNode.$ref]));
    }
    const entered = enter(rawNode, value, where);
    const nextDoc = entered === rawNode ? doc : entered;
    const node = resolve(entered, value, where);
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

    // A position pinned only by `const` still arrives as an index, and the const names
    // the expected member without naming the vocabulary it indexes. Rather than guess,
    // find the schema's own vocabularies that place this const at exactly this index.
    // One match decodes; zero or several refuse, because decoding through the wrong
    // vocabulary produces a confident wrong label.
    if (typeof node.const === "string" && typeof value === "number") {
      // Deduplicated by content: the publisher vocabulary is declared identically in
      // several places, and the same list declared twice is one vocabulary, not two
      // candidates. Only genuinely different member orders are ambiguous.
      const candidates = [
        ...new Set(
          vocabularies
            .filter((members) => Number.isInteger(value) && members[value] === node.const)
            .map((members) => JSON.stringify(members)),
        ),
      ];
      if (candidates.length === 1) {
        return node.const;
      }
      problems.push(
        `${where}: index ${JSON.stringify(value)} is pinned to ${JSON.stringify(node.const)} ` +
          `but ${candidates.length} declared vocabularies place it there, so the ` +
          "vocabulary to decode against is ambiguous",
      );
      return value;
    }
    if (Array.isArray(value)) {
      // A tuple schema describes each position separately with `prefixItems`, and closes
      // the tail with `items: false`. Reading only `items` skipped those positions
      // entirely, so `publisher_contexts_checked: [0]` reached the page as a literal 0
      // where a reader expects `lu-legilux`. The length is enforced here rather than
      // left to validation, because an extra element would otherwise decode against no
      // schema at all and pass through raw.
      const prefix = Array.isArray(node.prefixItems) ? node.prefixItems : null;
      if (prefix) {
        const max = node.maxItems ?? (node.items === false ? prefix.length : Infinity);
        const min = node.minItems ?? 0;
        if (value.length > max || value.length < min) {
          problems.push(
            `${where}: ${value.length} members, but the schema admits ` +
              `${min === max ? min : `${min} to ${max}`}`,
          );
        }
        return value.map((item, index) => {
          if (index >= prefix.length) {
            if (node.items === false) {
              problems.push(`${where}[${index}]: beyond the closed tuple, no schema describes it`);
              return item;
            }
            return decode(node.items, item, `${where}[${index}]`, nextDoc, seen);
          }
          return decode(prefix[index], item, `${where}[${index}]`, nextDoc, seen);
        });
      }
      return value.map((item, index) => decode(node.items, item, `${where}[${index}]`, nextDoc, seen));
    }
    if (value !== null && typeof value === "object") {
      const out = {};
      for (const [key, member] of Object.entries(value)) {
        out[key] = decode(
          node.properties?.[key],
          member,
          path ? `${path}.${key}` : key,
          nextDoc,
          seen,
        );
      }
      return out;
    }
    return value;
  };

  return { decoded: decode(arm, envelope, ""), problems };
}

export function validateEnvelope(schema, envelope, registry = new Map()) {
  const { arm, problems } = selectArm(schema, envelope);
  if (!arm) {
    return problems;
  }
  check(arm, envelope, "", problems, schema, new Set(), [schema, ...registry.values()]);
  return problems;
}

function check(node, value, path, problems, doc, seen, docs = []) {
  const where = path || "(root)";

  if (node === null || typeof node !== "object") {
    problems.push(`${where}: no schema describes this position`);
    return;
  }

  // Follow a local `$ref` before any other keyword, against its own document.
  if (node.$ref !== undefined) {
    const target = resolveLocalRef(node.$ref, doc, where, problems, seen, docs);
    if (target) {
      // A ref into a registered schema makes that schema the document for anything
      // nested beneath it, so its own local pointers resolve where they were written.
      const nextDoc = node.$ref.startsWith("#") ? doc : target;
      check(target, value, path, problems, nextDoc, new Set([...seen, node.$ref]), docs);
    }
    return;
  }

  // Refuse what this reader cannot check, rather than reporting it clean.
  for (const keyword of Object.keys(node)) {
    if (!SUPPORTED_KEYWORDS.has(keyword) && !ANNOTATION_KEYWORDS.has(keyword)) {
      problems.push(
        `${where}: schema keyword ${JSON.stringify(keyword)} is not implemented by this ` +
          "reader, so the position cannot be validated",
      );
      return;
    }
  }

  // Every `allOf` arm applies, in addition to this node's own keywords. Skipping them,
  // as this reader did, discarded whole constraint sets without a word.
  for (const sub of node.allOf ?? []) {
    check(sub, value, path, problems, doc, seen, docs);
  }

  // `if` selects between `then` and `else`. Its own failures are not problems: they are
  // the question being asked. So it is evaluated against a scratch list that is thrown
  // away, and only the selected branch reports.
  if (node.if !== undefined) {
    const scratch = [];
    check(node.if, value, path, scratch, doc, seen, docs);
    const branch = scratch.length === 0 ? node.then : node.else;
    if (branch !== undefined) {
      check(branch, value, path, problems, doc, seen, docs);
    }
  }

  if (node.const !== undefined && value !== node.const) {
    problems.push(`${where}: expected const ${JSON.stringify(node.const)}, got ${JSON.stringify(value)}`);
    return;
  }

  if (node.enum !== undefined && !node.enum.includes(value)) {
    problems.push(`${where}: ${JSON.stringify(value)} is not in the closed set`);
    return;
  }

  if (node.type === "string" && typeof value !== "string") {
    problems.push(`${where}: expected string, got ${typeof value}`);
    return;
  }

  // String constraints apply to any string, not only where the same node also declares
  // `type`. Gating them on `type` meant a composed arm carrying only `minLength` or only
  // `pattern` asserted nothing at all.
  if (typeof value === "string") {
    if (node.minLength !== undefined && value.length < node.minLength) {
      problems.push(`${where}: shorter than ${node.minLength}`);
    }
    if (node.maxLength !== undefined && value.length > node.maxLength) {
      problems.push(`${where}: longer than ${node.maxLength}`);
    }
    if (node.pattern !== undefined && !new RegExp(node.pattern, "u").test(value)) {
      problems.push(`${where}: does not match the published pattern`);
    }
    if (node.format !== undefined) {
      const verify = FORMATS.get(node.format);
      if (!verify) {
        problems.push(
          `${where}: format ${JSON.stringify(node.format)} is not implemented by this ` +
            "reader, so the position cannot be validated",
        );
      } else if (!verify(value)) {
        problems.push(`${where}: is not a valid ${node.format}`);
      }
    }
    // Only a node that declares `type: "string"` is finished here. Returning for every
    // string value skipped the type checks below, so a node declaring `type: "boolean"`
    // accepted a string.
    if (node.type === "string") {
      return;
    }
  }

  if (node.type === "boolean" && typeof value !== "boolean") {
    problems.push(`${where}: expected boolean`);
    return;
  }

  if (node.type === "array" || Array.isArray(node.prefixItems)) {
    if (!Array.isArray(value)) {
      problems.push(`${where}: expected array`);
      return;
    }
    const prefix = Array.isArray(node.prefixItems) ? node.prefixItems : null;
    if (node.minItems !== undefined && value.length < node.minItems) {
      problems.push(`${where}: ${value.length} members, fewer than ${node.minItems}`);
    }
    if (node.maxItems !== undefined && value.length > node.maxItems) {
      problems.push(`${where}: ${value.length} members, more than ${node.maxItems}`);
    }
    if (node.uniqueItems === true) {
      const seenMembers = new Set(value.map((item) => JSON.stringify(item)));
      if (seenMembers.size !== value.length) {
        problems.push(`${where}: members must be unique`);
      }
    }
    if (prefix) {
      // Each declared position is checked against its own sub-schema. Reading only
      // `items` left every tuple position unvalidated, which is how a tuple pinned to
      // `const: "lu-legilux"` accepted ["not-a-publisher"].
      if (value.length < prefix.length) {
        problems.push(`${where}: ${value.length} members, but ${prefix.length} positions are declared`);
      }
      prefix.forEach((sub, index) => {
        if (index < value.length) {
          check(sub, value[index], `${where}[${index}]`, problems, doc, seen, docs);
        }
      });
      if (node.items === false) {
        if (value.length > prefix.length) {
          problems.push(
            `${where}: ${value.length} members, but the tuple closes after ${prefix.length}`,
          );
        }
      } else if (node.items && node.items !== true) {
        value.slice(prefix.length).forEach((item, offset) => {
          check(node.items, item, `${where}[${prefix.length + offset}]`, problems, doc, seen, docs);
        });
      }
      return;
    }
    if (node.items && node.items !== true) {
      value.forEach((item, index) =>
        check(node.items, item, `${where}[${index}]`, problems, doc, seen, docs),
      );
    }
    return;
  }

  if (node.type === "object" || node.properties) {
    if (value === null || typeof value !== "object" || Array.isArray(value)) {
      problems.push(`${where}: expected object`);
      return;
    }
    const memberCount = Object.keys(value).length;
    if (node.minProperties !== undefined && memberCount < node.minProperties) {
      problems.push(`${where}: ${memberCount} members, fewer than ${node.minProperties}`);
    }
    if (node.maxProperties !== undefined && memberCount > node.maxProperties) {
      problems.push(`${where}: ${memberCount} members, more than ${node.maxProperties}`);
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
        check(subSchema, value[key], path ? `${path}.${key}` : key, problems, doc, seen, docs);
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
