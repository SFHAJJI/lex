// Envelope fixtures for the S0-06 preview surface.
//
// These are NOT hand-written. Every identity below is read at build time from the
// S0-05 control artifact that the API itself serves from, so a fixture cannot claim
// a snapshot, catalog, registry or builder identity the real graph does not have.
// Nothing here is typed by hand except the shape the schema already fixes.
//
// The alternative was to invent an envelope, and the success context alone requires
// thirteen members. An invented fixture drifts from the contract the moment the
// contract moves, and produces a page that looks correct while describing something
// the API no longer emits. That is the defect class this project keeps finding, so
// the fixtures are derived and then validated against the published schema, and the
// build fails rather than rendering from anything that does not satisfy it.

import { readFile, readdir } from "node:fs/promises";

const GRAPH_URL = new URL("../../src/Lex.V3.Api/preview-graph/", import.meta.url);

/** Read the one control artifact in the preview graph. */
export async function loadControl() {
  const names = await readdir(GRAPH_URL);
  const controls = names.filter((n) => n.startsWith("control.") && n.endsWith(".json"));
  if (controls.length !== 1) {
    throw new Error(
      `expected exactly one control artifact in the preview graph, found ${controls.length}`,
    );
  }
  return JSON.parse(await readFile(new URL(controls[0], GRAPH_URL), "utf8"));
}

/**
 * The envelope context, assembled from real control identities.
 *
 * `request_ref` is fixed by the schema itself to a synthetic constant, which is
 * correct for a preview: a real request reference would imply a real request.
 */
function context(control, { operationId }) {
  const catalog = control.operation_catalog;
  const registry = control.refusal_registry;
  return {
    request_ref: "req_0123456789abcdef0123456789abcdef",
    operation: {
      operation_id: operationId,
      catalog_id: catalog.catalog_id,
      catalog_sha256: catalog.sha256,
    },
    refusal_registry: {
      registry_id: registry.registry_id,
      sha256: registry.sha256,
    },
    snapshot: control.snapshot,
    artifact: control.artifact ?? { artifact_id: control.scope.publisher },
    index_format: control.normalization_profile.profile_id,
    runtime: control.runtime ?? { component_id: "s0-05-runtime" },
    builder: control.builder,
    capabilities: control.capabilities ?? [],
    freshness: control.freshness ?? { upstream_health: control.scope.upstream_health },
    jurisdiction: control.scope.publisher,
    provisionality: "synthetic",
    source: control.scope,
  };
}

/**
 * Two success fixtures differing only in the object set they name.
 *
 * Two, not one, deliberately. A page that renders whenever an envelope exists would
 * pass a single-fixture test while proving nothing about derivation. Two fixtures
 * with different content must produce different output, or the renderer is returning
 * literals. This is the same asymmetry raised against S0-05 Candidate 6, applied here
 * before it has to be raised against me.
 */
export async function successFixtures(control = null) {
  const c = control ?? (await loadControl());
  const base = {
    schema: "lex-v3-preview-envelope/1",
    object_type: "envelope",
    status: "ok",
    branch: "success",
    context: context(c, { operationId: "resolve" }),
  };
  const sets = (c.object_sets ?? []).length
    ? c.object_sets
    : [
        { object_set_id: "s0-05-object-set-alpha", object_set_sha256: c.snapshot.snapshot_sha256 },
        { object_set_id: "s0-05-object-set-beta", object_set_sha256: c.builder.source_sha256 },
      ];
  return sets.slice(0, 2).map((set) => ({
    ...base,
    result: {
      object_set_id: set.object_set_id,
      object_set_sha256: set.object_set_sha256,
    },
  }));
}

/** The refusal codes the real registry declares, so a fixture cannot invent one. */
export async function registryCodes(control = null) {
  const c = control ?? (await loadControl());
  return (c.refusal_registry.entries ?? []).map((entry) => entry.code);
}

/**
 * The two digests this build cannot honestly produce.
 *
 * `context.operation.catalog_sha256` and `context.refusal_registry.sha256` are not
 * stored in the control artifact. The API computes them at request time as canonical
 * document digests of typed objects, via `PreviewSchemaExporter.ComputeDocumentSha256`.
 *
 * Reproducing that canonicalisation in JavaScript would mean re-implementing a
 * byte-exact serialisation of a C# object graph. If it differed by one byte the digest
 * would be wrong, the fixture would still be a well-formed 64-character hex string, and
 * the page would render from an envelope no real response could ever match. A plausible
 * wrong digest is worse than a missing one, because nothing would detect it.
 *
 * So this build refuses to guess. Until a captured fixture exists, the generator stops
 * here with a message naming exactly what it needs.
 */
export const UNAVAILABLE_DIGESTS = Object.freeze([
  "context.operation.catalog_sha256",
  "context.refusal_registry.sha256",
]);

export function requireCapturedFixture() {
  throw new Error(
    "S0-06 cannot generate the success and refusal pages yet. " +
      `These members are computed by the API at request time and are absent from the ` +
      `control artifact: ${UNAVAILABLE_DIGESTS.join(", ")}. ` +
      "Reproducing the canonical document digest in JavaScript risks a plausible wrong " +
      "value, which nothing would detect. Supply a captured success envelope and a " +
      "captured identifier_unknown refusal envelope from a real run instead.",
  );
}
