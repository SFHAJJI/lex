// The captured envelopes, embedded verbatim.
//
// These are the bytes Codex captured from the merged S0-05 production C# path. They
// lived as .json files until the V3 tree verifier rejected web/fixtures/: the
// structural allowlist admits only web/scripts/*.mjs, web/src/*.{css,html,svg} and
// web/test/*.test.mjs, and widening it belongs to #335, which is queued behind #334.
//
// They are stored as escaped string literals rather than inline object literals so the
// exact bytes survive. A template literal would reinterpret the backslash-n escapes
// inside the object body text and silently change the digest, which is the one
// property that makes a captured fixture worth having.

import { createHash } from "node:crypto";

export const CAPTURE_PROVENANCE = Object.freeze({
  source_commit: "246ad5377bb382321fd42b57ec5f4dbdf9caa819",
  merge_commit: "6d299e2f39f1a3806126987c917d1f25b47efc14",
  api_assembly_sha256: "9dfe24b39b2e2d4d40f712d3234fb67e81238280794940d7e0d3ef46681a7f30",
});

const CAPTURES = Object.freeze({
  "success.json": {
    text: "{\"branch\":\"success\",\"matched_identifier_family\":0,\"matched_coordinate\":\"eli/synthetic-preview\",\"result\":{\"schema\":\"lex-v3-preview-object-set/1\",\"object_set_id\":\"s0-05-sql-object-set\",\"objects\":[{\"object_type\":\"preview_coordinate\",\"synthetic\":true,\"work_id\":\"preview:eli/synthetic-preview\",\"version_key\":\"preview:synthetic-v1\",\"anchor\":\"preview:article-1\",\"body_holding_state\":0,\"body_holding_disposition\":0,\"body\":\"LEX V3 SYNTHETIC PREVIEW\\nArticle 1\\nThis text is synthetic and has no legal authority.\\n\",\"body_sha256\":\"5512d26f4fcdf962273e5f4ac59b893401b380a128a737ba718d3326cba0ed7e\",\"object_id\":\"preview:eli/synthetic-preview#article-1\"}]},\"schema\":\"lex-v3-synthetic-resolve-envelope/1\",\"synthetic\":true,\"object_type\":\"envelope\",\"status\":\"ok\",\"context\":{\"request_ref\":\"req_00000000000000000000000000000000\",\"operation\":{\"operation_id\":\"resolve\",\"catalog_id\":\"s0-05-resolve-only\",\"catalog_sha256\":\"1f13453ab4cf3d8f6f3bf49ea45f14a9151adba9dd4ac8973c130089b1d07c81\"},\"refusal_registry\":{\"registry_id\":\"s0-04-identifier-boundary\",\"schema\":\"lex-v3-preview-refusal-registry/1\",\"sha256\":\"999ecb1f0c36fa8b2961de55b7312c230f7ddc3ed7904febdaf99ba43e9edb9a\"},\"snapshot\":{\"snapshot_id\":\"s0-05-snapshot\",\"snapshot_sha256\":\"865cdf1fba5eae99c79bec8e911aa08b8fef38d12f9fd1f74c925b763729f98f\"},\"artifact\":{\"sha256\":\"e26f803cfc3020b8c9f3de35ab23f08ecada6033a1dc066c54d6634acf44d76d\"},\"index\":{\"schema\":\"lex-v3-synthetic-sqlite/1\",\"sha256\":\"b588a1eeebc5a30c789d8d2162099ca9484a72014e99b8da2f3ce092c0113e91\",\"build_id\":\"865cdf1fba5eae99c79bec8e911aa08b8fef38d12f9fd1f74c925b763729f98f\"},\"runtime\":{\"component_id\":\"s0-05-runtime\",\"source_sha256\":\"9dfe24b39b2e2d4d40f712d3234fb67e81238280794940d7e0d3ef46681a7f30\"},\"builder\":{\"component_id\":\"s0-05-builder\",\"source_sha256\":\"3abecefc9186dbf6180fe4149e633f932418e80c4297fade4d3a680dd6ca418a\"}}}",
    sha256: "dedbcbe0f53f5e2b41fd98551d5913b0ed56525ec35f7b26a6c0fa9eaad4ba3c",
    bytes: 1824,
  },
  "refusal.json": {
    text: "{\"branch\":\"refusal\",\"refusal\":{\"code\":0,\"checked_identifier_family\":3,\"requested_coordinate\":\"historical_legal_id:synthetic-preview\",\"publisher_contexts_checked\":[0],\"possible_held_records\":[{\"identifier_family\":0,\"coordinate\":\"eli/synthetic-preview\",\"publisher\":0}],\"official_search_actions\":[{\"kind\":\"publisher_search\",\"publisher\":0,\"uri\":\"https://legilux.public.lu/search\"}],\"what_would_answer\":[0],\"asserts_absence_of_law\":false},\"schema\":\"lex-v3-synthetic-resolve-envelope/1\",\"synthetic\":true,\"object_type\":\"envelope\",\"status\":\"identifier_unknown\",\"context\":{\"request_ref\":\"req_01010101010101010101010101010101\",\"operation\":{\"operation_id\":\"resolve\",\"catalog_id\":\"s0-05-resolve-only\",\"catalog_sha256\":\"1f13453ab4cf3d8f6f3bf49ea45f14a9151adba9dd4ac8973c130089b1d07c81\"},\"refusal_registry\":{\"registry_id\":\"s0-04-identifier-boundary\",\"schema\":\"lex-v3-preview-refusal-registry/1\",\"sha256\":\"999ecb1f0c36fa8b2961de55b7312c230f7ddc3ed7904febdaf99ba43e9edb9a\"},\"snapshot\":{\"snapshot_id\":\"s0-05-snapshot\",\"snapshot_sha256\":\"865cdf1fba5eae99c79bec8e911aa08b8fef38d12f9fd1f74c925b763729f98f\"},\"artifact\":{\"sha256\":\"e26f803cfc3020b8c9f3de35ab23f08ecada6033a1dc066c54d6634acf44d76d\"},\"index\":{\"schema\":\"lex-v3-synthetic-sqlite/1\",\"sha256\":\"b588a1eeebc5a30c789d8d2162099ca9484a72014e99b8da2f3ce092c0113e91\",\"build_id\":\"865cdf1fba5eae99c79bec8e911aa08b8fef38d12f9fd1f74c925b763729f98f\"},\"runtime\":{\"component_id\":\"s0-05-runtime\",\"source_sha256\":\"9dfe24b39b2e2d4d40f712d3234fb67e81238280794940d7e0d3ef46681a7f30\"},\"builder\":{\"component_id\":\"s0-05-builder\",\"source_sha256\":\"3abecefc9186dbf6180fe4149e633f932418e80c4297fade4d3a680dd6ca418a\"}}}",
    sha256: "cfc9fe90f4f020e99f8da43c8d9e5f74c570eced2ad5d303c6dee7b485eb0212",
    bytes: 1630,
  },
});

/**
 * Return one captured envelope, refusing if its bytes are not the ones captured.
 *
 * The digest is recomputed on every call rather than trusted from the constant beside
 * it. A fixture that changed silently is indistinguishable from one that was
 * fabricated, and that distinction is the entire reason these bytes came from a
 * production run instead of from me.
 */
export function loadCaptured(name) {
  const entry = CAPTURES[name];
  if (!entry) {
    throw new Error(`no captured envelope named ${name}`);
  }
  const bytes = Buffer.from(entry.text, "utf8");
  const digest = createHash("sha256").update(bytes).digest("hex");
  if (digest !== entry.sha256 || bytes.length !== entry.bytes) {
    throw new Error(
      `captured ${name} does not match its recorded identity: ` +
        `${bytes.length} bytes ${digest}, expected ${entry.bytes} bytes ${entry.sha256}`,
    );
  }
  return JSON.parse(entry.text);
}

export const CAPTURED_NAMES = Object.freeze(Object.keys(CAPTURES));
