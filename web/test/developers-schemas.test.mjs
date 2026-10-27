// The schemas the Developers page links (the specification of record's R46 and R55; `developers.mjs`), held without a
// browser wherever the page is served from: the built live directory holds every one, byte for byte the repository's
// `schemas/v3-platform/*.schema.json`, and every one it publishes is linked; the live server serves them from that
// directory; journey J7 asks them of the page's own origin (the first CI run of the walk, #997, asked the API's origin, which
// with the pages served by the live server holds no web root, and every schema answered 404); and the image and deploy
// probes hold a revision to the image's own files (`schemaLinkFailures`).

import assert from "node:assert/strict";
import test from "node:test";
import { createServer } from "node:http";
import { mkdtemp, readFile, readdir, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

import { buildLive } from "../scripts/build-live.mjs";
import { createLiveServer } from "../scripts/serve-live.mjs";
import { publishedSchemaFiles } from "../scripts/developers-contracts.mjs";
import { linkedSchemas, schemaLinkFailures } from "../scripts/deploy-probe.mjs";
import { developersPageFailures } from "../scripts/journey.mjs";

const listen = (server) => new Promise((resolve) => server.listen(0, "127.0.0.1", () => resolve(`http://127.0.0.1:${server.address().port}`)));
const close = (server) => new Promise((resolve) => server.close(resolve));

/** Every file under `root`, as `[path, bytes]` with `/` separators, as an image's live page files are read. */
async function filesUnder(root) {
  const files = [];
  for (const entry of await readdir(root, { recursive: true, withFileTypes: true })) {
    if (!entry.isFile()) continue;
    const path = join(entry.parentPath ?? entry.path, entry.name);
    files.push([path.slice(root.length + 1).replaceAll("\\", "/"), await readFile(path)]);
  }
  return files;
}

test("the built live directory holds every schema the Developers page links, byte for byte, and each one it publishes is linked", async (t) => {
  const destination = await mkdtemp(join(tmpdir(), "lex-developers-schemas-"));
  t.after(() => rm(destination, { recursive: true, force: true }));
  await buildLive(pathToFileURL(`${destination}/`), { buildTag: "developers-schemas" });
  const published = new Map((await publishedSchemaFiles()).map(([path, bytes]) => [`/${path}`, bytes]));
  assert.ok(published.size > 0, "the platform publishes its schemas");
  for (const language of ["en", "fr"]) {
    const linked = linkedSchemas(await readFile(join(destination, language, "developers.html"), "utf8"));
    assert.deepEqual([...linked].sort(), [...published.keys()].sort(), `${language}: the page links every published schema, and only those`);
    for (const path of linked) {
      const built = await readFile(join(destination, ...path.slice(1).split("/")));
      assert.ok(built.equals(published.get(path)), `${language}: ${path} is the repository's schema, byte for byte`);
    }
  }

  // The live server, as the journeys run it (pages served apart from the API): every schema at the page's origin.
  const unreachable = createServer((request, response) => { response.writeHead(404, { "content-type": "application/problem+json" }); response.end("{}"); });
  const apiOrigin = await listen(unreachable);
  const live = createLiveServer({ root: pathToFileURL(`${destination}/`), apiOrigin });
  const pageOrigin = await listen(live);
  try {
    const web = await filesUnder(destination);
    assert.deepEqual(await schemaLinkFailures(pageOrigin, web), [], "the live server serves every linked schema as the build wrote it");
    // J7's schema check asks the page's own origin; asked of an origin that holds no web root (the API with the pages
    // served apart, #997), every schema is a failure.
    const html = await readFile(join(destination, "en", "developers.html"), "utf8");
    const post = async () => ({ status: 0, json: null });
    const atPage = (await developersPageFailures(apiOrigin, html, { post, pageOrigin })).filter((failure) => failure.startsWith("/schemas/"));
    assert.deepEqual(atPage, [], "at the page's origin");
    const atApi = (await developersPageFailures(apiOrigin, html, { post })).filter((failure) => failure.startsWith("/schemas/"));
    assert.equal(atApi.length, published.size, "at an origin with no web root, each schema answers 404");
    // The probes' check: an image whose page links a schema it does not hold, or a revision that does not serve it, fails.
    const missing = web.filter(([path]) => path !== "schemas/v3-platform/envelope.json");
    assert.deepEqual(await schemaLinkFailures(pageOrigin, missing), ["the release image's Developers page links /schemas/v3-platform/envelope.json, which the image does not hold"]);
    const notServed = await schemaLinkFailures(apiOrigin, web);
    assert.equal(notServed.length, published.size);
    assert.match(notServed[0], /^GET \/schemas\/v3-platform\/[a-z0-9-]+\.json answered 404 application\/problem\+json: the Developers page's link to it does not work$/);
    // An image with no Developers page links none, and asks nothing.
    assert.deepEqual(await schemaLinkFailures(apiOrigin, web.filter(([path]) => !path.endsWith("developers.html"))), []);
  } finally {
    await close(live);
    await close(unreachable);
  }
});
