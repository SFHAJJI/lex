// Build the preview surface.
//
// The static shell is copied, then all five state pages are generated. The two
// data-bearing pages render captured envelopes produced by the merged C# production
// path, never fabricated ones: two of the envelope's digests are computed at request
// time over a canonical serialisation of a typed object graph, and a JavaScript
// reimplementation differing by one byte would still yield a well-formed hex string that
// validates. A plausible wrong digest is worse than a missing one.
//
// Every captured envelope is decoded and validated before it is rendered. The wire
// format encodes closed vocabularies as integer indices, so an undecoded envelope would
// put bare numbers where a reader expects machine codes.

import { cp, mkdir, readFile, rm, writeFile } from "node:fs/promises";

import { loadCaptured } from "./captured-envelopes.mjs";
import { decodeEnvelope, validateEnvelope } from "./envelope.mjs";
import {
  renderLoading,
  renderTransportFailure,
  renderInvalidEnvelope,
  renderSuccess,
  renderRefusal,
} from "./render.mjs";

const source = new URL("../src/", import.meta.url);
const destination = new URL("../dist/", import.meta.url);

await rm(destination, { force: true, recursive: true });
await mkdir(destination, { recursive: true });
await cp(source, destination, { recursive: true });

// Each page states one thing that is true and nothing about any law. The reasons are
// concrete rather than generic, because "an error occurred" teaches a reader nothing
// and invites them to guess.
const pages = [
  ["state-loading.html", renderLoading()],
  [
    "state-transport-failure.html",
    renderTransportFailure({
      reason: "the connection closed before any response header arrived",
    }),
  ],
  [
    "state-invalid-envelope.html",
    renderInvalidEnvelope({
      problems: [
        'branch "sideways" is not one of ["success","refusal"]',
        "context.operation.operation_id: not in the closed set",
      ],
    }),
  ],
];

// The schemas that describe the captured envelopes. `result` declares its own schema
// identity, and the vocabularies for the object fields live there, so the object-set
// schema is registered rather than duplicated into the envelope schema.
const json = async (url) => JSON.parse(await readFile(url, "utf8"));

const envelopeSchema = await json(
  new URL("../../schemas/v3-synthetic-preview/synthetic-resolve-envelope.schema.json", import.meta.url),
);
const registry = new Map([
  [
    "lex-v3-preview-object-set/1",
    await json(new URL("../../schemas/v3-preview/preview-object-set.schema.json", import.meta.url)),
  ],
]);

for (const [name, file, render] of [
  ["state-success.html", "success.json", renderSuccess],
  ["state-refusal.html", "refusal.json", renderRefusal],
]) {
  const raw = loadCaptured(file);
  const { decoded, problems } = decodeEnvelope(envelopeSchema, raw, registry);
  const invalid = problems.concat(decoded ? validateEnvelope(envelopeSchema, decoded) : []);
  if (invalid.length > 0) {
    throw new Error(`captured ${file} did not decode and validate: ${invalid.join("; ")}`);
  }
  pages.push([name, render({ envelope: decoded })]);
}

for (const [name, html] of pages) {
  await writeFile(new URL(name, destination), html, "utf8");
}

console.log(`generated ${pages.length} state pages`);
