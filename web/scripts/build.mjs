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

import { createHash } from "node:crypto";
import { cp, mkdir, readFile, rm, writeFile } from "node:fs/promises";

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

// The captured envelopes and the schemas that describe them. `result` declares its own
// schema identity, and the vocabularies for the object fields live there, so the
// object-set schema is registered rather than duplicated into the envelope schema.
// The captures live in the repository so the build is hermetic and the fixture is
// reviewable as data. Their digests are verified on every build against the values
// recorded when they were captured: a fixture that changed silently would be
// indistinguishable from one that was fabricated, which is the whole thing being
// guarded against.
const CAPTURES = new URL("../fixtures/captured/", import.meta.url);
const json = async (url) => JSON.parse(await readFile(url, "utf8"));

const captureIndex = await json(new URL("INDEX.json", CAPTURES));

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
  const bytes = await readFile(new URL(file, CAPTURES));
  const digest = createHash("sha256").update(bytes).digest("hex");
  const expected = captureIndex.files?.[file];
  if (!expected) {
    throw new Error(`captured ${file} has no recorded digest in INDEX.json`);
  }
  if (digest !== expected.sha256 || bytes.length !== expected.bytes) {
    throw new Error(
      `captured ${file} does not match its recorded identity: ` +
        `${bytes.length} bytes ${digest}, expected ${expected.bytes} bytes ${expected.sha256}`,
    );
  }
  const raw = JSON.parse(bytes.toString("utf8"));
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
