// Build the preview surface.
//
// The static shell is copied, then the state pages are generated. Only the three
// envelope-free states are generated here. The two data-bearing states need a real
// envelope, and two of its digests (`context.operation.catalog_sha256` and
// `context.refusal_registry.sha256`) are computed by the C# API at request time over a
// canonical serialisation of a typed object graph. Reproducing that in JavaScript would
// mean matching it byte for byte; one byte out and the digest is still a well-formed
// 64-character hex string, the envelope still validates, and the page renders from
// something no real response could ever produce. So this build refuses to guess and
// stops short of those two pages until a captured fixture exists.

import { cp, mkdir, rm, writeFile } from "node:fs/promises";

import {
  renderLoading,
  renderTransportFailure,
  renderInvalidEnvelope,
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

for (const [name, html] of pages) {
  await writeFile(new URL(name, destination), html, "utf8");
}

console.log(`generated ${pages.length} state pages`);
