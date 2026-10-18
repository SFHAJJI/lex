// The release pipeline's image steps, rehearsed with no production credential: one command builds the
// one-server OCI image (the API, the live web pages and a corpus mount), verifies what it holds,
// signs it with a rehearsal identity, verifies the signature, and removes it (the launch contract's
// release path; production signing, credentials and deployment stay with the owner).
//
//   node scripts/image-rehearsal.mjs --mount <a v3-corpus directory with its build-report.json, or the
//     journey's fixture mount with its journey-mount.json> [--keep] [--no-reproduce] [--no-probe | --no-browser] [--platform-card]
//
// The image is built without a container daemon, by the .NET SDK (`dotnet publish -t:PublishContainer`,
// the base image pinned by digest in `Lex.V3.Api.csproj`), as an OCI image layout archive. Then:
// - every blob the index names (manifest, config, layers) must have the digest and size it is named by;
// - the app layer must hold the API, every file of the live pages under `app/v3-web/` and every file of
//   the mount under `app/v3-corpus/`, byte for byte, and each file the mount's build report lists with
//   its recorded digest;
// - the image must run as a non-root user, start the API, and name its base image by digest;
// - the synthetic preview and its key, kept for tests, must be absent from the app layer, and (with the probes) the
//   server must answer its readiness route 204 with its mount and the preview's route as an unknown route (plan 0.7);
// - V2, the retired product, must be absent: no Lex assembly that is not V3's in any layer, no V2
//   library among the API's dependencies, and (with the probes) each of V2's routes answering 404.
// The signature is the rehearsal's own: an ECDSA P-256 key made for the run and never kept, over a
// signing payload in the shape container signatures use, naming the manifest digest and saying it is a
// rehearsal. Verifying it checks the signature, the digest and that rehearsal label.
// With the owner's signing key (`--signing-key <PEM file, or - to read it from standard input> --signer
// <identity>`, the release signer), the image and the release are signed by that key instead, naming that
// identity and not the rehearsal's, and the release is published as a release (`lex-v3-release/1`, version
// `v3-...`), not a rehearsal. The key is ECDSA P-256; it is read once, held only in this process, and never
// written, logged or kept; no error repeats it. Reading the key from standard input
// (`az keyvault secret show ... --query value -o tsv | node scripts/image-rehearsal.mjs ... --signing-key -`)
// puts it on no disk at all.
// The evaluation card is the machine gates run over the mount the image carries (ruling 2), derived from the
// mount by `V3MountedGatesTests` (`--platform-card` takes the platform's fixture card instead).
// Last, the release assets are published into a versioned directory (the image, its signature, the
// evaluation card the image serves at its stable route `/evaluation-card.json`, which the probes fetch, the mount's report, and a signed release manifest naming each by
// hash), read back and verified (`release-assets.mjs`).
// The image is reproducible: each build restores and compiles every project afresh in its own directory,
// the SDK's time stamps are replaced by the source date (`image-reproducible.mjs`), and a second build
// from scratch must give the same manifest digest (`--no-reproduce` skips it). The image then runs in
// WSL as a hardened container would, and the zero-traffic probes run against it (`image-run.mjs`, `probeImage`;
// `--no-probe` skips them): every probe that needs no browser on one start of the image (its API's journeys, the
// deployment probe's checks of its pages, card and headers, V2's routes, its readiness, and what it records), then the
// browser's walk of its pages. `--no-browser` keeps every probe but the walk, for a machine where no browser may run
// (the driver's, since 2026-10-16: headless Chrome runs there locked the owner's Windows account): no browser is looked
// for or started, and the report says `browserProbes: "skipped: --no-browser"` and names each page step not walked
// (`stepsNotWalked`). With it the command installs the browser trap before anything else (`browser-trap.mjs`), so no
// later step can find or start a browser: a touch fails the run with 97. The probes take `browser` with no default.
// Nothing is pushed anywhere, and the work directory (both builds, their
// archives), the build's artifacts directory and the container's run directory are removed at the end,
// which the report records.

import { spawnSync } from "node:child_process";
import { createHash, createPrivateKey, createPublicKey, generateKeyPairSync, sign, verify } from "node:crypto";
import { existsSync, readFileSync } from "node:fs";
import { mkdtemp, readFile, readdir, rm, stat, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { gunzipSync } from "node:zlib";
import { readLargeFile, sha256Hex } from "./large-bytes.mjs";
import { pageForSitePath, redirectFor } from "./permalinks.mjs";
import { INDEXED, KEPT_OUT, ROBOTS_PATH, SITEMAP_INDEX_HEAD, SITEMAP_PATH, robotsTxt } from "./search-engines.mjs";
import { UNSERVED_DIRECTORIES } from "./not-found.mjs";

const repository = resolve(fileURLToPath(new URL("../../", import.meta.url)));
// In pieces: one update over 2 GiB is refused, and the population image's app layer is 2.5 GB (large-bytes.mjs).
const sha256 = (bytes) => sha256Hex(bytes);

/** The rehearsal identity: named as what it is, so no one mistakes it for the release signer. */
export const REHEARSAL_IDENTITY = "lex-v3-rehearsal (not a release identity)";

/**
 * The entries of a tar archive, as `{ path, type, mode, uid, gid, bytes }`: ustar names with their
 * prefix, and the long names pax headers (`x`) and GNU headers (`L`) carry.
 */
export function readTar(buffer) {
  const entries = [];
  let offset = 0;
  let longName = null;
  const text = (start, length) => buffer.subarray(start, start + length).toString("utf8").replace(/\0.*$/s, "");
  const number = (start, length) => parseInt(text(start, length).trim() || "0", 8);
  while (offset + 512 <= buffer.length) {
    const header = buffer.subarray(offset, offset + 512);
    if (header.every((byte) => byte === 0)) break;
    const name = text(offset, 100);
    const size = number(offset + 124, 12);
    const type = String.fromCharCode(header[156] || 48);
    const prefix = text(offset + 345, 155);
    const [mode, uid, gid] = [number(offset + 100, 8), number(offset + 108, 8), number(offset + 116, 8)];
    const bytes = buffer.subarray(offset + 512, offset + 512 + size);
    offset += 512 + Math.ceil(size / 512) * 512;
    if (type === "x") {
      const records = bytes.toString("utf8");
      const path = records.match(/\d+ path=([^\n]*)\n/);
      if (path) longName = path[1];
      continue;
    }
    if (type === "L") { longName = bytes.toString("utf8").replace(/\0.*$/s, ""); continue; }
    if (type === "g") continue;
    const path = longName ?? (prefix ? `${prefix}/${name}` : name);
    longName = null;
    entries.push({ path: path.replace(/^\.\//, ""), type: type === "5" ? "directory" : type === "0" ? "file" : type, mode, uid, gid, bytes });
  }
  return entries;
}

/** A layer's tar: gunzipped when its media type says gzip (the base image's, the SDK's), else as it is. */
export function layerTar(layer) {
  return /gzip$/.test(layer.mediaType ?? "") ? gunzipSync(layer.bytes) : layer.bytes;
}

/**
 * The image an OCI image layout holds: its manifest, config and layers, each blob checked against the
 * digest and size it is named by. `blobs` maps `sha256:<hex>` to the blob's bytes.
 */
export function readOciImage({ index, blobs }) {
  const blob = (descriptor, what) => {
    const bytes = blobs.get(descriptor.digest);
    if (bytes === undefined) throw new Error(`the image names ${what} ${descriptor.digest}, which the layout does not hold`);
    if (`sha256:${sha256(bytes)}` !== descriptor.digest) throw new Error(`${what} ${descriptor.digest} does not have that digest: its bytes are sha256:${sha256(bytes)}`);
    if (bytes.length !== descriptor.size) throw new Error(`${what} ${descriptor.digest} is ${bytes.length} bytes, not the ${descriptor.size} it is named with`);
    return bytes;
  };
  if (!Array.isArray(index.manifests) || index.manifests.length !== 1) throw new Error("the image layout must name exactly one manifest");
  const manifestDescriptor = index.manifests[0];
  const manifest = JSON.parse(blob(manifestDescriptor, "the manifest").toString("utf8"));
  const config = JSON.parse(blob(manifest.config, "the config").toString("utf8"));
  const layers = manifest.layers.map((layer, position) => ({ ...layer, bytes: blob(layer, `layer ${position + 1}`) }));
  return { manifestDigest: manifestDescriptor.digest, manifest, config, layers };
}

/** Every file under a directory, as `[relative path with forward slashes, bytes]`. */
async function filesUnder(root) {
  const found = [];
  for (const entry of await readdir(root, { recursive: true, withFileTypes: true })) {
    if (!entry.isFile()) continue;
    const path = join(entry.parentPath ?? entry.path, entry.name);
    found.push([relative(root, path).split(sep).join("/"), await readLargeFile(path)]);
  }
  return found;
}

/**
 * What the image must hold and how it must run, as failures (empty when it holds). `web` and `mount`
 * are the directories the image was built from, as `[path, bytes]` lists; `report` is the mount's
 * build report.
 */
export function imageFailures(image, { web, mount, report }) {
  const failures = [];
  const { config } = image;
  if (config.os !== "linux" || config.architecture !== "amd64") failures.push(`the image is ${config.os}/${config.architecture}, not linux/amd64`);
  // A user is `user[:group]`, by name or number; root is the name `root` or the number 0, whatever group
  // follows (review of #821: `0:1654` and `root:1654` run as root).
  const user = config.config?.User ?? "";
  const who = user.split(":")[0];
  if (who === "" || who === "root" || (/^\d+$/.test(who) && Number(who) === 0)) {
    failures.push(`the image runs as ${user === "" ? "root (no user set)" : user}, not a non-root user`);
  }
  if (JSON.stringify(config.config?.Entrypoint) !== JSON.stringify(["dotnet", "/app/Lex.V3.Api.dll"])) failures.push(`the image starts ${JSON.stringify(config.config?.Entrypoint)}, not the API`);
  const base = config.config?.Labels?.["org.opencontainers.image.base.name"] ?? "";
  if (!/@sha256:[0-9a-f]{64}$/.test(base)) failures.push(`the base image is not named by digest: ${JSON.stringify(base)}`);
  const app = image.layers.at(-1);
  if (!app) return [...failures, "the image has no layer"];
  const files = new Map(readTar(layerTar(app)).filter((entry) => entry.type === "file").map((entry) => [entry.path, entry.bytes]));
  if (!files.has("app/Lex.V3.Api.dll")) failures.push("the app layer holds no app/Lex.V3.Api.dll");
  for (const [label, prefix, list] of [["live page file", "app/v3-web/", web], ["mount file", "app/v3-corpus/", mount]]) {
    if (list.length === 0) failures.push(`no ${label} was given to check`);
    for (const [path, bytes] of list) {
      const held = files.get(`${prefix}${path}`);
      if (held === undefined) failures.push(`the image holds no ${prefix}${path}`);
      else if (!held.equals(bytes)) failures.push(`${prefix}${path} in the image differs from the ${label} it was built from`);
    }
  }
  for (const file of report.files ?? []) {
    const held = files.get(`app/v3-corpus/${file.Name}`);
    if (held === undefined) failures.push(`the image holds no ${file.Name}, which the build report lists`);
    else if (sha256(held) !== file.Sha256) failures.push(`${file.Name} in the image is sha256:${sha256(held)}, not the ${file.Sha256} the build report records`);
  }
  if ((report.files ?? []).length === 0) failures.push("the build report lists no file");
  // The synthetic preview and its key are for tests only: the release image ships neither (plan 0.7).
  const preview = [...files.keys()].filter((path) => PREVIEW_FILE.test(path));
  if (preview.length > 0) failures.push(`the image ships the synthetic preview: ${preview.slice(0, 5).join(", ")}${preview.length > 5 ? ` and ${preview.length - 5} more` : ""}`);
  return failures;
}

/** A file of the synthetic preview or of its key, where a test build of the API keeps them. */
const PREVIEW_FILE = /^app\/preview-(?:graph|trust)\//;

/** The synthetic preview's own route, asked of a coordinate its graph holds: a release server must not know it. */
export const PREVIEW_PROBE = "/api/v3-preview/resolve?family=eli&coordinate=eli%2Fsynthetic-preview";

/**
 * How a release server answers for itself, as failures (empty when it holds): its readiness route is 204, uncached, while
 * it holds the law (the platform's readiness probe and monitoring ask it; plan 0.7), and the synthetic preview's route is
 * an unknown route like any other, naming nothing synthetic.
 */
export async function serverFailures(origin) {
  const failures = [];
  const ready = await fetch(`${origin}/health/ready`, { redirect: "manual" });
  const readyBody = Buffer.from(await ready.arrayBuffer());
  if (ready.status !== 204 || readyBody.length !== 0) failures.push(`GET /health/ready answered ${ready.status} (${readyBody.length} bytes), not 204: the server says it does not hold the law`);
  if (ready.headers.get("cache-control") !== "no-store") failures.push(`GET /health/ready may be cached (${ready.headers.get("cache-control")})`);
  const preview = await fetch(`${origin}${PREVIEW_PROBE}`, { redirect: "manual" });
  const previewBody = Buffer.from(await preview.arrayBuffer()).toString("utf8");
  if (preview.status !== 404 || /synthetic|preview/i.test(previewBody)) {
    failures.push(`GET ${PREVIEW_PROBE} answered ${preview.status}${/synthetic|preview/i.test(previewBody) ? ", naming the preview" : ""}, not the 404 of a route the server does not know`);
  }
  return failures;
}

/**
 * The retired product, V2 (the `main` line), by what would betray it in an image: its projects (`src/`
 * on `main`, 2026-09-30), and every route its `Lex.Web` served. The routes are every `MapGet` and
 * `MapPost` of `Lex.Web` on `main`, the pages its `/built` table maps, its publisher document routes
 * (`/{publisher}/{work}...` for `lu-legilux` and `eu-eurlex`) and its `/built/diagrams/{name}.svg` with
 * values it held, its `/mcp/{*rest}` fallback, and the static files of its `wwwroot`. The first list
 * left out `/built` and its table (review of #831). The launch contract's machine gates ask "V2
 * absent from the image".
 */
export const V2_PROJECTS = Object.freeze([
  "Lex.Ask", "Lex.Derive", "Lex.Index", "Lex.Ingest", "Lex.Law", "Lex.Mcp", "Lex.Mcp.Stdio",
  "Lex.Sources.EurLex", "Lex.Sources.Legilux", "Lex.Temporal", "Lex.Web",
]);
export const V2_ROUTES = Object.freeze([
  ...[
    // Its pages and documents.
    "/", "/about", "/ai", "/architecture", "/architecture/dossier", "/architecture/next", "/ask", "/attestation.json",
    "/benchmarks", "/benchmarks/cases.json", "/benchmarks/latest.json", "/browse", "/changed", "/coverage", "/decisions",
    "/developers", "/find", "/go-asof?work=loi-1991-08-10-n3&date=2024-02-01", "/healthz", "/how-it-works", "/in-force-on",
    "/provenance/lu-legilux", "/pubkey.pem", "/readyz", "/robots.txt", "/search", "/sitemap.xml", "/stories", "/verify",
    // The /built table, its release status and its diagrams.
    "/built", "/built/model", "/built/data", "/built/retrieval", "/built/assistant", "/built/release", "/built/decisions",
    "/built/incidents", "/built/limits", "/built/repositories", "/built/release/evaluation.json", "/built/diagrams/system.svg",
    // One law: its timeline, its text on a date, a comparison, per publisher.
    "/lu-legilux/loi-1991-08-10-n3", "/lu-legilux/loi-1991-08-10-n3/2024-02-01", "/lu-legilux/loi-1991-08-10-n3/diff/2024-02-01/2024-02-01",
    "/eu-eurlex/32016R0679", "/eu-eurlex/32016R0679/2016-05-04", "/eu-eurlex/32016R0679/diff/2016-05-04/2016-05-04",
    // Its MCP fallback (V3's own MCP endpoint is /mcp itself) and its static files.
    "/mcp/tools", "/.well-known/glama.json", "/dossier.css", "/make-og.py", "/og.png", "/site.js",
    ...["IBMPlexMono-latin-ext", "IBMPlexMono-latin", "IBMPlexSans-latin-ext", "IBMPlexSans-latin", "SourceSerif4-latin-ext", "SourceSerif4-latin"]
      .map((font) => `/fonts/${font}.woff2`),
  ].map((path) => ["GET", path]),
  // Its assistant.
  ...["/api/ask", "/api/ask/stream", "/api/ask/thread/reset", "/api/ask/evaluation/admission"].map((path) => ["POST", path]),
]);

/** A Lex assembly, symbol file or documentation file that is not V3's. */
const NOT_V3 = /^Lex\.(?!V3\.)[^/]*\.(?:dll|exe|pdb|xml)$/;

/**
 * Where V2 shows in the image, as failures (empty when it is absent): in any layer, a Lex assembly,
 * symbol or documentation file that is not `Lex.V3.*`; and in the API's dependency manifest, a Lex
 * library that is not V3's (a V3 assembly that referenced V2 would name it there).
 */
export function v2Failures(image) {
  const failures = [];
  for (const [position, layer] of image.layers.entries()) {
    for (const entry of readTar(layerTar(layer))) {
      const name = entry.path.replace(/\/+$/, "").split("/").at(-1);
      if (NOT_V3.test(name)) failures.push(`layer ${position + 1} holds ${entry.path}, which is not V3`);
    }
  }
  const app = image.layers.at(-1);
  const deps = app ? readTar(layerTar(app)).find((entry) => entry.path === "app/Lex.V3.Api.deps.json") : undefined;
  if (!deps) return [...failures, "the app layer holds no app/Lex.V3.Api.deps.json to read the API's dependencies from"];
  const manifest = JSON.parse(deps.bytes.toString("utf8"));
  const libraries = new Set([
    ...Object.keys(manifest.libraries ?? {}),
    ...Object.values(manifest.targets ?? {}).flatMap((target) => Object.keys(target ?? {})),
  ].map((key) => key.split("/")[0]));
  for (const library of [...libraries].sort()) {
    if (/^Lex\.(?!V3\.)/.test(library)) failures.push(`the API depends on ${library}, which is not V3`);
  }
  return failures;
}

/**
 * The V2 routes a running image answers, as failures (empty when V2 answers none), asked of the image's
 * own origin. Each must answer 404, except where V3 answers the path itself: the root and a law's address
 * with no interface language with V3's own redirect to the reader's language, to the exact address
 * (`redirectFor`, R61), a file of V3's live pages (`v3Files`, URL path to bytes: a font V2 also had)
 * with 200 and that file, byte for byte, and `/robots.txt` with V3's own in the release's switch state (`searchEngines`, kept
 * out unless the release says `indexed`; R67, `search-engines.mjs`), as `/sitemap.xml` with V3's sitemap index once indexed (kept
 * out, V3 holds none, so it is a 404). Redirects are not followed: a V2 redirect is an answer.
 */
export async function v2RouteFailures(origin, { v3Files = new Map(), routes = V2_ROUTES, searchEngines = KEPT_OUT } = {}) {
  const failures = [];
  for (const [method, path] of routes) {
    const response = await fetch(`${origin}${path}`, { method, headers: { "content-type": "application/json" }, body: method === "POST" ? "{}" : undefined, redirect: "manual" });
    const body = Buffer.from(await response.arrayBuffer());
    // V3's own robots.txt, and its sitemap with the site indexed, stand where V2 had its own (R67).
    if (method === "GET" && (path === ROBOTS_PATH || (path === SITEMAP_PATH && searchEngines === INDEXED))) {
      const own = path === ROBOTS_PATH ? body.toString("utf8") === robotsTxt(searchEngines) : body.toString("utf8").startsWith(SITEMAP_INDEX_HEAD);
      if (response.status !== 200 || !own) failures.push(`${method} ${path} answered ${response.status}, not V3's own ${path.slice(1)} (${searchEngines})`);
      continue;
    }
    // V3's own answer at a path V2 also used is V3's, not V2's: the root and a law's address with no interface language
    // (V2 served pages at /, /lu-legilux/{work} and /eu-eurlex/{celex}) are V3's redirect to the reader's language, French
    // for a request naming none (`redirectFor`, R61), to the exact address; a law's address under a language is V3's
    // page that reads it. Anything else must still be V3's own file or a 404.
    const [bare, query = ""] = path.split("?");
    const location = method === "GET" ? redirectFor(bare, query === "" ? "" : `?${query}`, undefined) : null;
    if (location !== null) {
      if (response.status !== 302 || response.headers.get("location") !== location) {
        failures.push(`${method} ${path} answered ${response.status}${response.headers.get("location") ? ` to ${response.headers.get("location")}` : ""}, not V3's redirect to ${location}`);
      }
      continue;
    }
    const page = method === "GET" ? pageForSitePath(bare) : null;
    const own = method === "GET" ? (v3Files.get(bare) ?? (page === null ? undefined : v3Files.get(`/${page}`))) : undefined;
    if (own === undefined) {
      if (response.status !== 404) failures.push(`${method} ${path} answered ${response.status}, not 404`);
    } else if (response.status !== 200 || !body.equals(own)) {
      failures.push(`${method} ${path} answered ${response.status} with bytes that are not V3's own file at that path`);
    }
  }
  return failures;
}

/** The live pages as the image serves them, URL path to bytes: each file at `/<path>`, and each interface language's root (`/en/`, `/fr/`) as its `index.html`. */
export function servedPaths(web) {
  // The pages' server renderer (ssr/) is run by the API, never served (R67), and the 404 and maintenance pages are never
  // served at their own address (R60; `not-found.mjs`).
  const served = new Map(web.filter(([path]) => !UNSERVED_DIRECTORIES.some((directory) => path.startsWith(`${directory}/`)))
    .map(([path, bytes]) => [`/${path}`, bytes]));
  // An interface language's root is its home page (R61); the site's root is a redirect, not a page.
  for (const language of ["en", "fr"]) {
    if (served.has(`/${language}/index.html`)) served.set(`/${language}/`, served.get(`/${language}/index.html`));
  }
  return served;
}

/** A key for one run of the rehearsal, ECDSA P-256, never written anywhere: `{ privateKey, publicKeyPem }`. */
export function rehearsalKey() {
  const { privateKey, publicKey } = generateKeyPairSync("ec", { namedCurve: "P-256" });
  return { privateKey, publicKeyPem: publicKey.export({ type: "spki", format: "pem" }).toString() };
}

/** The rehearsal's signer: its identity, said as the rehearsal's, with a key made for the run. */
export function rehearsalSigner(key = rehearsalKey()) {
  return Object.freeze({ identity: REHEARSAL_IDENTITY, rehearsal: true, privateKey: key.privateKey, publicKeyPem: key.publicKeyPem });
}

/**
 * The release's signer from the owner's signing key: an ECDSA P-256 private key in PEM (the owner's, kept in their key
 * vault and handed to this process only for the run), under the identity the owner names for it. The public key is
 * derived from the private one, so the release names the key it was signed with. The key is never written, logged or
 * kept, and no error repeats any of it. Throws on anything but a P-256 private key, or on an identity that is empty or
 * the rehearsal's.
 */
export function releaseSigner({ privateKeyPem, identity }) {
  if (typeof identity !== "string" || identity.trim().length === 0 || identity !== identity.trim() || identity === REHEARSAL_IDENTITY) {
    throw new Error("the release signer names its identity, and it is not the rehearsal's");
  }
  let privateKey;
  try {
    privateKey = createPrivateKey(privateKeyPem);
  } catch {
    throw new Error("the signing key is not a private key in PEM");
  }
  if (privateKey.asymmetricKeyType !== "ec" || privateKey.asymmetricKeyDetails?.namedCurve !== "prime256v1") {
    throw new Error("the signing key is not an ECDSA P-256 key, which the release's signatures use");
  }
  const publicKeyPem = createPublicKey(privateKey).export({ type: "spki", format: "pem" }).toString();
  return Object.freeze({ identity, rehearsal: false, privateKey, publicKeyPem });
}

/**
 * An image signature over its manifest digest by `signer` (`rehearsalSigner` or `releaseSigner`): a signing payload in
 * the shape container signatures use, naming the signer and whether it is the rehearsal's, signed with ECDSA P-256
 * over SHA-256.
 */
export function signImage({ manifestDigest, reference, signer }) {
  const payload = Buffer.from(JSON.stringify({
    critical: {
      identity: { "docker-reference": reference },
      image: { "docker-manifest-digest": manifestDigest },
      type: "cosign container image signature",
    },
    optional: { signer: signer.identity, rehearsal: signer.rehearsal },
  }), "utf8");
  return {
    payload,
    signature: sign("sha256", payload, signer.privateKey).toString("base64"),
    publicKeyPem: signer.publicKeyPem,
  };
}

/** A rehearsal signature over the image's manifest digest, with a key made for the run unless one is given. */
export function signRehearsal({ manifestDigest, reference, key = rehearsalKey() }) {
  return signImage({ manifestDigest, reference, signer: rehearsalSigner(key) });
}

/**
 * Whether an image signature holds for this manifest digest: the signature under `publicKeyPem`, the digest, and the
 * signer it names, which must be `signer` (`{ identity, rehearsal }`): a rehearsal's signature never passes for a
 * release's, nor one release signer's for another's.
 */
export function imageSignatureFailures({ payload, signature, publicKeyPem, manifestDigest, signer }) {
  const failures = [];
  if (!verify("sha256", payload, createPublicKey(publicKeyPem), Buffer.from(signature, "base64"))) failures.push("the signature does not verify over its payload with this key");
  let parsed = null;
  try { parsed = JSON.parse(payload.toString("utf8")); } catch { failures.push("the signing payload is not JSON"); }
  if (parsed !== null) {
    if (parsed.critical?.image?.["docker-manifest-digest"] !== manifestDigest) failures.push(`the signature names ${parsed.critical?.image?.["docker-manifest-digest"]}, not this image's manifest ${manifestDigest}`);
    if (parsed.critical?.type !== "cosign container image signature") failures.push("the signing payload is not a container image signature");
    if (parsed.optional?.rehearsal !== signer.rehearsal || parsed.optional?.signer !== signer.identity) {
      failures.push(signer.rehearsal ? "the signature does not say it is the rehearsal's" : `the signature does not say it is ${signer.identity}'s`);
    }
  }
  return failures;
}

/** Whether a rehearsal signature holds for this manifest digest: the signature, the digest and the label. */
export function rehearsalSignatureFailures({ payload, signature, publicKeyPem, manifestDigest }) {
  return imageSignatureFailures({ payload, signature, publicKeyPem, manifestDigest, signer: { identity: REHEARSAL_IDENTITY, rehearsal: true } });
}

/** Runs a command, failing with its output if it fails. */
function run(command, args, options = {}) {
  const result = spawnSync(command, args, { encoding: "utf8", maxBuffer: 64 * 1024 * 1024, ...options });
  if (result.status !== 0) throw new Error(`${command} ${args.join(" ")} failed (${result.status}): ${(result.stdout ?? "").slice(-2000)}${(result.stderr ?? "").slice(-2000)}`);
  return result;
}

/** The layout an OCI image layout archive holds, as `{ index, blobs }` with blobs keyed `sha256:<hex>`. */
export function readLayout(archive) {
  const named = new Map(readTar(archive).filter((entry) => entry.type === "file").map((entry) => [entry.path, entry.bytes]));
  const index = JSON.parse(named.get("index.json").toString("utf8"));
  const blobs = new Map([...named].filter(([path]) => path.startsWith("blobs/sha256/")).map(([path, bytes]) => [`sha256:${path.slice("blobs/sha256/".length)}`, bytes]));
  return { index, blobs };
}

/**
 * Where two builds of the image differ, as failures (empty when they are the same image): the manifest,
 * the config's fields, each layer, and in a differing layer the entries that differ.
 */
export function reproductionFailures(first, second) {
  if (first.manifestDigest === second.manifestDigest) return [];
  const failures = [`the second build's manifest is ${second.manifestDigest}, the first's ${first.manifestDigest}`];
  for (const key of new Set([...Object.keys(first.config), ...Object.keys(second.config)])) {
    if (JSON.stringify(first.config[key]) !== JSON.stringify(second.config[key])) failures.push(`the config's ${key} differs`);
  }
  const entries = (layer) => new Map(readTar(layerTar(layer)).map((entry) => [entry.path.replace(/\/+$/, ""), entry]));
  for (let position = 0; position < Math.max(first.layers.length, second.layers.length); position++) {
    const [one, other] = [first.layers[position], second.layers[position]];
    if (one?.digest === other?.digest) continue;
    failures.push(`layer ${position + 1} differs: ${one?.digest ?? "none"} and ${other?.digest ?? "none"}`);
    if (!one || !other) continue;
    const [a, b] = [entries(one), entries(other)];
    const differing = [...new Set([...a.keys(), ...b.keys()])].sort().filter((path) => {
      const [x, y] = [a.get(path), b.get(path)];
      return !x || !y || x.type !== y.type || x.mode !== y.mode || x.uid !== y.uid || x.gid !== y.gid || !x.bytes.equals(y.bytes);
    });
    failures.push(differing.length === 0
      ? `layer ${position + 1} holds the same entries; only how it is written differs`
      : `layer ${position + 1}: ${differing.length} entries differ: ${differing.slice(0, 20).join(", ")}`);
  }
  return failures;
}

/** Where the image is built: inside the checkout, so the compiler maps its paths (see `buildImage`). */
const ARTIFACTS = join(repository, "artifacts", "image-rehearsal");

/**
 * One build of the image, into its own directory: the live pages, then `dotnet publish
 * -t:PublishContainer` into an emptied artifacts directory, so every project is restored and compiled
 * afresh and nothing is taken from the repository's bin or obj, with source paths mapped
 * (ContinuousIntegrationBuild) so the image does not depend on where the checkout lives. The artifacts
 * directory is `artifacts/image-rehearsal` inside the checkout, the same for every build: the compiler
 * writes the paths of its generated sources and symbol files into each assembly, and only paths under
 * the checkout are mapped (a directory per build outside it gave each build its own assemblies). The
 * SDK's image is then made reproducible (`reproducibleImage`). Answers `{ webRoot, archive, image }`.
 */
async function buildImage({ into, mountPath, epoch, card, log }) {
  log("building the live pages");
  const { buildLive } = await import("./build-live.mjs");
  const webRoot = join(into, "v3-web");
  await buildLive(pathToFileURL(`${webRoot}/`), { card, buildTag: "image-rehearsal" });

  log("building the image (dotnet publish -t:PublishContainer)");
  const sdkArchive = join(into, "sdk-image.tar");
  await rm(ARTIFACTS, { recursive: true, force: true });
  run("dotnet", ["publish", join(repository, "src", "Lex.V3.Api", "Lex.V3.Api.csproj"), "-c", "Release", "-r", "linux-x64", "--self-contained", "false",
    "-p:UseArtifactsOutput=true", `-p:ArtifactsPath=${ARTIFACTS}`, "-p:ContinuousIntegrationBuild=true",
    `-p:LexImageWebRoot=${webRoot}`, `-p:LexImageMount=${mountPath}`, `-p:ContainerArchiveOutputPath=${sdkArchive}`,
    "-p:ContainerRepository=lex-v3-rehearsal", "-p:ContainerImageTag=rehearsal", "-t:PublishContainer", "-m:1", "-nodeReuse:false", "-v", "q"]);
  spawnSync("dotnet", ["build-server", "shutdown"], { encoding: "utf8" });
  await rm(ARTIFACTS, { recursive: true, force: true });

  log("making the image reproducible");
  const { reproducibleImage, writeLayout } = await import("./image-reproducible.mjs");
  const reproducible = reproducibleImage(readLayout(await readLargeFile(sdkArchive)), { epoch });
  const archive = join(into, "lex-v3-rehearsal.tar");
  await writeFile(archive, writeLayout(reproducible, { mtime: epoch }));
  await rm(sdkArchive, { force: true });
  return { webRoot, archive, image: readOciImage(readLayout(await readLargeFile(archive))) };
}

/**
 * What a mount says it holds, as `{ kind, report, bytes }` (`bytes`: the file it was read from, which
 * the release carries): a real mount's build report (`build-report.json`),
 * or, for the journey's fixture mount (`journey-mount.json`, written by `V3JourneyMountTests`), what it
 * names in the build report's shape. The fixture names its corpus by the snapshot digest the coverage
 * answer shows, not by its file's bytes, so only its Luxembourg index is held to a file digest; the
 * corpus file is held byte for byte to the mount like every mount file, and its snapshot digest to the
 * coverage probe.
 */
export async function mountReport(mountPath) {
  if (!existsSync(join(mountPath, "journey-mount.json"))) {
    const bytes = await readFile(join(mountPath, "build-report.json"));
    return { kind: "real", report: JSON.parse(bytes.toString("utf8")), bytes };
  }
  const bytes = await readFile(join(mountPath, "journey-mount.json"));
  const fixture = JSON.parse(bytes.toString("utf8"));
  return {
    kind: "fixture",
    bytes,
    report: {
      corpus: { Sha256: fixture.corpus_sha256 },
      luxembourgIndex: { Sha256: fixture.index_sha256 },
      files: [{ Name: "luxembourg-index.sqlite3", Sha256: fixture.index_sha256 }],
    },
  };
}

/**
 * The evaluation card over the mount the image carries (ruling 2: the launch card carries machine gates run
 * over the real mounted corpus): the temporal, refusal and retrieval sets derived from the mount
 * (`V3MountedGatesTests`), run through the real handler and rendered as the card, built in its own artifacts
 * directory. A gate that fails fails the test, and so the rehearsal.
 */
async function mountedCard({ mountPath, into, log }) {
  log("running the machine gates over the mount (V3MountedGatesTests)");
  const output = join(into, "mount-evaluation-card.json");
  run("dotnet", ["test", join(repository, "tests", "Lex.V3.Ingest.Tests", "Lex.V3.Ingest.Tests.csproj"), "-c", "Release",
    "-p:UseArtifactsOutput=true", `-p:ArtifactsPath=${join(into, "artifacts")}`,
    // No -m:1 or -nodeReuse here: the test platform takes them for its own and then runs no test.
    "--filter", "FullyQualifiedName~TheGatesOverTheMountTheReleaseNames", "-v", "q"],
  { env: { ...process.env, V3_EVALUATE_MOUNT: mountPath, V3_EVALUATION_CARD_OUT: output } });
  spawnSync("dotnet", ["build-server", "shutdown"], { encoding: "utf8" });
  if (!existsSync(output)) throw new Error("the machine gates over the mount wrote no card");
  return readFile(output);
}

/** What a report says in place of the browser's walk on a machine where no browser may run (the owner's order of 2026-10-16). */
export const NO_BROWSER = "skipped: --no-browser";

/**
 * Throws unless `browser` says, as a boolean, whether a browser walks the pages: the release's probes take no default, so a
 * value lost on its way (a call that leaves it out) stops the release instead of starting a browser (the review of #979).
 */
function browserNamed(browser, where) {
  if (typeof browser !== "boolean") {
    throw new Error(`${where} needs browser: true (the pages walked in a browser) or false (--no-browser), and was given ${String(browser)}; it has no default, so nothing that leaves it out starts a browser`);
  }
}

/**
 * The page steps a browser walks over the image, by the labels a walk reports them under: the real mount's
 * (`realMountSteps`, the EU steps with them when its build report names an EU index) or the fixture mount's
 * (`fixtureMountExpectations`). What a report without the browser lists as not walked; reading them asks nothing of a
 * browser.
 */
export async function browserSteps(mountPath, mountKind) {
  const { fixtureMountExpectations, realMountSteps } = await import("./journey.mjs");
  if (mountKind === "fixture") {
    const journeyMount = JSON.parse(await readFile(join(mountPath, "journey-mount.json"), "utf8"));
    return fixtureMountExpectations(journeyMount).map(([name]) => `${name}, with the fixture mount`);
  }
  const report = JSON.parse(await readFile(join(mountPath, "build-report.json"), "utf8"));
  return realMountSteps(report).map(([name]) => `${name}, with the real mount`);
}

/** The string values of a JSON request body's parameters (REST) or arguments (MCP), the texts a probe sent. */
function sentTexts(body) {
  let parsed = null;
  try { parsed = typeof body === "string" ? JSON.parse(body) : null; } catch { parsed = null; }
  const texts = [];
  const walk = (value) => {
    if (typeof value === "string") texts.push(value);
    else if (value !== null && typeof value === "object") Object.values(value).forEach(walk);
  };
  walk(parsed?.parameters ?? parsed?.params?.arguments ?? null);
  return texts;
}

/**
 * Runs `during` and answers `{ value, record }`: what it answered, and every request this process sent meanwhile, as
 * `record`, `{ requests, otherOrigins, cookies, texts }`: how many, the origins other than `origin` asked, the answers that
 * set a cookie (`METHOD path`), and the texts the requests sent (at least eight characters: identifiers, dates, phrases).
 * The probes all ask through the global `fetch`, which is wrapped for that while and put back after.
 */
export async function recordedRequests(origin, during) {
  const record = { requests: 0, otherOrigins: new Set(), cookies: [], texts: new Set() };
  const original = globalThis.fetch;
  globalThis.fetch = async (input, init = {}) => {
    const url = new URL(typeof input === "string" || input instanceof URL ? input : input.url);
    const method = init.method ?? (typeof input === "object" && "method" in input ? input.method : "GET");
    record.requests += 1;
    if (url.origin !== origin) record.otherOrigins.add(url.origin);
    for (const text of sentTexts(init.body)) if (text.length >= 8) record.texts.add(text);
    const response = await original(input, init);
    if (response.headers.get("set-cookie") !== null) record.cookies.push(`${method} ${url.pathname}`);
    return response;
  };
  try {
    const value = await during();
    return { value, record: { requests: record.requests, otherOrigins: [...record.otherOrigins], cookies: record.cookies, texts: [...record.texts] } };
  } finally {
    globalThis.fetch = original;
  }
}

/**
 * The probes of a running image (`startServer` starts it and answers the journey's server shape: `{ origin, output(),
 * outputAtStart, fileWatch, changedFiles(), close() }`), as a report. First every probe that needs no browser, against one
 * start of the image:
 * - journeys J6, J7 and J8 at its API (`apiJourneyRuns`): the contained assistant's card, every page's request and ask,
 *   events and answer drift through REST and MCP alike, and the event log polled (on a real mount its first
 *   `REAL_MOUNT_EVENTS_VERIFIED` permalinks verified, on the fixture's every one);
 * - what the deployment probe holds a candidate to (`servedPageFailures`, `deploy-probe.mjs`): the root's redirect to
 *   `/fr/`, the home page as built or as the release's renderer renders the image's own coverage answer, its security
 *   headers (CSP with `frame-ancestors 'none'`, HSTS, `Referrer-Policy`, `nosniff`), the 404 page byte for byte, the card
 *   at its stable route, coverage from the mount's corpus, the clock, and an accented title found as typed;
 * - V2's routes (`v2RouteFailures`), the server's readiness and no synthetic preview (`serverFailures`), and the card's
 *   bytes at its route;
 * - privacy, as far as it shows: what the container recorded once it first answered (nothing on its output, no file on its
 *   /tmp: `processRecordFailures`), and over HTTP, no answer setting a cookie and no request to another origin.
 * Then, with `browser`, the browser's walk: the real mount's steps (`realMountRuns`, its API journeys already asked above)
 * or the fixture mount's (`fixtureMountRuns`), each step its own start. Without it no browser is looked for or started
 * (`findBrowser` is never called), the report says `browserProbes: "skipped: --no-browser"`, and `stepsNotWalked` names
 * each page step a walk would have reported, so no report reads as browser-probed when it was not.
 */
export async function probeImage({ startServer, mountPath, mountKind, web, card, corpusSha256, browser, log = () => {} }) {
  browserNamed(browser, "probeImage");
  const { apiJourneyRuns, processRecordFailures, REAL_MOUNT_EVENTS_VERIFIED } = await import("./journey.mjs");
  const { servedPageFailures } = await import("./deploy-probe.mjs");
  const { CARD_ROUTE } = await import("./evaluation-card.mjs");
  const report = { browserProbes: browser ? "walked" : NO_BROWSER };
  log("probing the image over HTTP: its API's journeys, its pages, card and headers, V2's routes, its readiness and what it records");
  const server = await startServer();
  try {
    const { value, record } = await recordedRequests(server.origin, async () => {
      const kept = { ...server, close: async () => {} };
      const journeys = await apiJourneyRuns(null, mountPath, {
        startServer: async () => kept, eventsToVerify: mountKind === "fixture" ? Number.POSITIVE_INFINITY : REAL_MOUNT_EVENTS_VERIFIED,
      });
      const notes = [];
      const servedPages = { failures: await servedPageFailures(server.origin, { web, card, corpusSha256, notes }), notes };
      const v2Routes = { routesAsked: V2_ROUTES.length, routeFailures: await v2RouteFailures(server.origin, { v3Files: servedPaths(web) }) };
      // Ready with its mount, and no synthetic preview (plan 0.7).
      const serverAnswers = { failures: await serverFailures(server.origin) };
      // The card for machines at its stable route: the bytes the release carries and signs.
      const answer = await fetch(`${server.origin}${CARD_ROUTE}`);
      const bytes = Buffer.from(await answer.arrayBuffer());
      const cardRoute = { route: CARD_ROUTE, status: answer.status, contentType: answer.headers.get("content-type"), sha256: sha256(bytes), isTheReleaseCard: bytes.equals(card) };
      cardRoute.failures = answer.status !== 200 || !bytes.equals(card) || !/^application\/json/.test(cardRoute.contentType ?? "")
        ? [`the image does not serve the card at ${CARD_ROUTE}: ${JSON.stringify(cardRoute)}`] : [];
      return { journeys, servedPages, v2Routes, serverAnswers, cardRoute };
    });
    report.probes = value.journeys.map(([label, failures]) => ({ step: `${label}, with the ${mountKind} mount`, state: "api", failures, paintedElements: 0, citationsVerified: 0 }));
    Object.assign(report, { servedPages: value.servedPages, v2Routes: value.v2Routes, server: value.serverAnswers, cardRoute: value.cardRoute });
    // What the container recorded once it first answered, all these probes long.
    const output = server.output();
    const recorded = { startup: output.slice(0, server.outputAtStart), output: output.slice(server.outputAtStart), fileEvents: await server.fileWatch.stop(), changedFiles: await server.changedFiles() };
    report.privacy = {
      requests: record.requests, textsSent: record.texts.length, outputBytes: recorded.output.length, fileEvents: recorded.fileEvents.length,
      cookiesSet: record.cookies.length, otherOrigins: record.otherOrigins,
      failures: [
        ...processRecordFailures(recorded, { typed: record.texts }),
        ...record.cookies.map((request) => `${request} answered with a cookie`),
        ...record.otherOrigins.map((other) => `the probes asked ${other}, not the image's own origin`),
      ],
    };
  } finally {
    await server.close();
  }

  if (!browser) {
    report.stepsNotWalked = await browserSteps(mountPath, mountKind);
    log(`the browser probes are skipped (--no-browser): ${report.stepsNotWalked.length} page steps were not walked`);
    return report;
  }
  // The browser's walk: on a real mount each page held to what the image's API answers; on the fixture mount every page
  // to the texts the fixture's one work gives it, and its citations verified.
  log("walking the image's pages in a browser");
  const { fixtureMountRuns, realMountRuns } = await import("./journey.mjs");
  const { findBrowser } = await import("./browser-evidence.mjs");
  const found = await findBrowser();
  const options = { servedByApi: true, keyboard: false, startServer };
  const runs = mountKind === "fixture"
    ? await fixtureMountRuns(null, mountPath, options, found, null)
    : await realMountRuns(null, mountPath, options, found, null, { apiRuns: async () => [] });
  report.probes = [...runs.map(([label, { observed, failures }]) => ({
    step: label, state: observed.answerState, failures,
    paintedElements: observed.paint?.painted ?? 0, citationsVerified: observed.verifications?.length ?? 0,
  })), ...report.probes];
  return report;
}

/** A probe report's failures (`probeImage`), each prefixed with what it belongs to; empty when every probe held. */
export function probeFailures(report) {
  return [
    ...report.probes.filter((one) => one.failures.length > 0).map((one) => `${one.step}: ${one.failures.join("; ")}`),
    ...report.servedPages.failures.map((failure) => `the served pages: ${failure}`),
    ...report.v2Routes.routeFailures.map((failure) => `V2's routes: ${failure}`),
    ...report.server.failures.map((failure) => `the server: ${failure}`),
    ...report.cardRoute.failures,
    ...report.privacy.failures.map((failure) => `privacy: ${failure}`),
  ];
}

/** The rehearsal, end to end. Returns its report; throws on the first step that fails. */
export async function rehearse({ mount, keep = false, probe = true, browser, reproduce = true, platformCard = false, signer = null, log = () => {} }) {
  browserNamed(browser, "the rehearsal");
  const mountPath = resolve(mount);
  const { kind: mountKind, report, bytes: mountBytes } = await mountReport(mountPath);
  const work = await mkdtemp(join(tmpdir(), "lex-image-rehearsal-"));
  const { sourceDate } = await import("./image-reproducible.mjs");
  const source = sourceDate();
  const result = { mount: mountPath, mountKind, corpusSha256: report.corpus?.Sha256 ?? null, source };
  let container = null;
  try {
    // The evaluation card the Trust and Coverage page carries and the release carries beside the image: the machine
    // gates run over this mount (ruling 2), or with `--platform-card` the platform's fixture card. Inside the try, so
    // a gate that fails still has the work directory removed.
    const cardBytes = platformCard
      ? await readFile(join(repository, "schemas", "v3-platform", "evaluation-card.json"))
      : await mountedCard({ mountPath, into: join(work, "gates"), log });
    const card = JSON.parse(cardBytes.toString("utf8"));
    const { readEvaluationCard } = await import("./evaluation-card.mjs");
    const cardView = readEvaluationCard(card);
    result.card = {
      over: platformCard ? "the platform's fixture card" : "the machine gates over this mount",
      target: card.target,
      sets: cardView.sets.map((set) => ({ set: set.set, arm: set.arm, cases: set.cases, gates: set.gates.map((gate) => `${gate.gate}: ${gate.verdict}`) })),
      controls: cardView.controls.map((control) => `${control.set}, ${control.arm}: ${control.verdict}`),
    };
    let { webRoot, archive, image } = await buildImage({ into: join(work, "first"), mountPath, epoch: source.epoch, card, log });
    result.archiveBytes = (await stat(archive)).size;

    log("verifying the image");
    const web = await filesUnder(webRoot);
    // The card the image serves at its stable route, which the release carries beside it.
    const { CARD_ROUTE } = await import("./evaluation-card.mjs");
    const servedCard = web.find(([path]) => `/${path}` === CARD_ROUTE)?.[1];
    if (!servedCard) throw new Error(`the live pages hold no ${CARD_ROUTE}`);
    if (JSON.stringify(JSON.parse(servedCard.toString("utf8"))) !== JSON.stringify(card)) throw new Error(`${CARD_ROUTE} is not the card the pages were built with`);
    // The mount's files (2.5 GB for the population) are let go as soon as they are checked, and the second image below
    // as soon as it is compared: an async function keeps a dead local reachable across its later awaits, and holding both
    // took the image step to about 11.8 GB resident for the population image (the review of #951).
    let corpus = await filesUnder(mountPath);
    const failures = imageFailures(image, { web, mount: corpus, report });
    const mountFilesVerified = corpus.length;
    corpus = null;
    Object.assign(result, {
      manifestDigest: image.manifestDigest,
      configDigest: image.manifest.config.digest,
      layers: image.layers.map((layer) => layer.digest),
      base: image.config.config.Labels["org.opencontainers.image.base.name"],
      user: image.config.config.User,
      webFilesVerified: web.length,
      mountFilesVerified,
      reportFilesVerified: (report.files ?? []).length,
      imageFailures: failures,
    });
    if (failures.length > 0) throw new Error(`the image does not hold what it must:\n- ${failures.join("\n- ")}`);
    const v2 = v2Failures(image);
    // How many entries the check read, so an empty list of failures cannot hide an empty scan.
    result.v2Absent = { entriesScanned: image.layers.reduce((count, layer) => count + readTar(layerTar(layer)).length, 0), failures: v2 };
    if (v2.length > 0) throw new Error(`V2 is in the image:\n- ${v2.join("\n- ")}`);

    // The release's signer when the owner hands over the signing key (`signer`, from `releaseSigner`), else the
    // rehearsal's, with a key made for this run.
    const by = signer ?? rehearsalSigner();
    log(by.rehearsal ? "signing with the rehearsal identity and verifying" : `signing with the release signer ${by.identity} and verifying`);
    const signed = signImage({ manifestDigest: image.manifestDigest, reference: by.rehearsal ? "lex-v3-rehearsal:rehearsal" : "lex-v3:release", signer: by });
    const signatureFailures = imageSignatureFailures({ ...signed, manifestDigest: image.manifestDigest, signer: by });
    Object.assign(result, { signer: by.identity, rehearsal: by.rehearsal, signatureVerified: signatureFailures.length === 0, publicKeyPem: signed.publicKeyPem });
    if (signatureFailures.length > 0) throw new Error(`the image signature does not hold:\n- ${signatureFailures.join("\n- ")}`);

    if (reproduce) {
      // Reproducible: a second build, from scratch in its own directory, must give the same image.
      log("building the image a second time, from scratch, to check it is reproducible");
      let second = await buildImage({ into: join(work, "second"), mountPath, epoch: source.epoch, card, log });
      const differences = reproductionFailures(image, second.image);
      result.reproduced = { manifestDigest: second.image.manifestDigest, identical: differences.length === 0 };
      second = null;
      await rm(join(work, "second"), { recursive: true, force: true });
      if (differences.length > 0) throw new Error(`the image is not reproducible:\n- ${differences.join("\n- ")}`);
    }

    // From here only the image's digests and config are read (the probes unpack the archive from disk, and the release reads
    // it again), so its bytes (2.6 GB for the population image) are let go before the container starts. (The population
    // release's missed starts were not this: every closed start left its container and its /tmp in WSL's memory, which
    // `endProcesses` now ends; 2026-10-13.)
    image = { manifestDigest: image.manifestDigest, manifest: image.manifest, config: image.config, layers: image.layers.map(({ digest }) => ({ digest })) };

    if (probe) {
      // The zero-traffic probes, against the image itself, running in WSL as a hardened container: every probe that needs
      // no browser on one start of it (`probeImage`), then, unless `--no-browser`, the browser's walk of every live screen
      // the image serves, each step its own start.
      log(browser ? "running the image in WSL and probing it" : "running the image in WSL and probing it without a browser (--no-browser)");
      const { unpackImage, startImage, tmpMebibytesFor, TMP_MARGIN_MEBIBYTES } = await import("./image-run.mjs");
      // Collected here, where `rehearse`'s frame no longer holds the old image (a collection on the line after it was let
      // go still found it reachable; the review of #952), and only where the process allows it (`node --expose-gc`).
      globalThis.gc?.();
      container = unpackImage({ archive, layers: image.layers.map((layer) => layer.digest) });
      // The container's /tmp holds the copies of every index the mount carries (2.3 GB for the combined population).
      const tmpMebibytes = await tmpMebibytesFor(mountPath);
      result.containerTmpMebibytes = tmpMebibytes;
      // A minute and a half, and a minute more per GiB of indexes the container verifies before it listens.
      const deadlineMs = 90_000 + Math.ceil(((tmpMebibytes - TMP_MARGIN_MEBIBYTES) / 1024) * 60_000);
      const { v2Routes, ...probed } = await probeImage({
        startServer: () => startImage({ run: container, config: image.config, tmpMebibytes, deadlineMs }),
        mountPath, mountKind, web, card: servedCard, corpusSha256: report.corpus?.Sha256 ?? null, browser, log,
      });
      Object.assign(result, probed);
      Object.assign(result.v2Absent, v2Routes);
      const failing = probeFailures({ ...probed, v2Routes });
      if (failing.length > 0) throw new Error(`the image failed its probes:\n- ${failing.join("\n- ")}`);
    } else {
      result.browserProbes = "skipped: --no-probe";
    }

    log("publishing the release assets, reading them back and verifying them");
    const { ASSETS, imageSignatureAsset, publishRelease, releaseFailures, releaseVersion } = await import("./release-assets.mjs");
    const version = releaseVersion(source, { rehearsal: by.rehearsal });
    const published = await publishRelease(join(work, "release"), {
      version, source, manifestDigest: image.manifestDigest, corpusSha256: report.corpus?.Sha256 ?? null, signer: by,
      assets: [[ASSETS.image, await readLargeFile(archive)], [ASSETS.imageSignature, imageSignatureAsset(signed)], [ASSETS.card, servedCard], [ASSETS.mountReport, mountBytes]],
    });
    // A release signer's release is read back as that signer's; a rehearsal names no release signer to hold it to.
    const readBack = await releaseFailures(published.directory, { publicKeyPem: by.publicKeyPem, identity: by.rehearsal ? null : by.identity });
    result.release = { version, manifestSha256: published.manifestSha256, assets: (await readdir(published.directory)).sort(), readBackFailures: readBack };
    if (readBack.length > 0) throw new Error(`the release does not read back:\n- ${readBack.join("\n- ")}`);
    return result;
  } finally {
    if (!keep) {
      await rm(work, { recursive: true, force: true });
      await rm(ARTIFACTS, { recursive: true, force: true });
      result.removed = { work: !existsSync(work), artifacts: !existsSync(ARTIFACTS) };
      if (container !== null) {
        const { removeRun } = await import("./image-run.mjs");
        result.removed.container = removeRun(container);
      }
    }
  }
}

/**
 * The release command from custody: the mount derived twice by independent processes and compared file for file
 * (`derive-twice.mjs`), then the rehearsal over the first derivation, with the derivation's evidence in the report.
 */
export async function rehearseFromCustody({ tool, custody, checkpoint, custodyEncoding = "brotli", allowUnboundTool = false, log = () => {}, ...options }) {
  // Asked first: the two derivations take hours, and the rehearsal after them would refuse a browser left unsaid.
  browserNamed(options.browser, "the release from custody");
  const { deriveTwice } = await import("./derive-twice.mjs");
  const binding = await toolBinding(resolve(tool));
  if (!binding.bound && !allowUnboundTool) {
    throw new Error(`the derive tool is not bound to the source the image is built from: ${binding.reason}; ` +
      "use this commit's CI runtime artifact, or pass --allow-unbound-tool for a rehearsal that says so");
  }
  const into = await mkdtemp(join(tmpdir(), "lex-release-derive-"));
  try {
    const derived = await deriveTwice({ runner: ["dotnet", resolve(tool)], custody: resolve(custody), checkpoint: resolve(checkpoint), custodyEncoding, into, log });
    log(`the two derivations agree on ${derived.files.length} files`);
    const result = await rehearse({ mount: derived.mount, log, ...options });
    const { mount: _derivedMount, ...evidence } = derived;
    return {
      derivation: {
        custody: resolve(custody),
        checkpoint: { path: resolve(checkpoint), sha256: sha256(await readFile(resolve(checkpoint))) },
        tool: binding,
        ...evidence,
      },
      ...result,
    };
  } finally {
    await rm(into, { recursive: true, force: true });
  }
}

/**
 * Whether the derive tool is the one this commit's CI built: the CI runtime artifact's layout (`<artifact>/runtime/` beside
 * `<artifact>/source-head.txt`) names the commit it was built from, which must be the checkout the image is built from.
 */
export async function toolBinding(toolPath, checkoutHead = null) {
  const head = checkoutHead ?? run("git", ["-C", repository, "rev-parse", "HEAD"]).stdout.trim();
  const toolSha256 = sha256(await readFile(toolPath));
  const stamp = join(dirname(dirname(toolPath)), "source-head.txt");
  if (!existsSync(stamp)) {
    return { path: toolPath, sha256: toolSha256, checkoutHead: head, bound: false, reason: `no ${stamp} names the commit it was built from` };
  }
  const sourceHead = (await readFile(stamp, "utf8")).trim();
  return sourceHead === head
    ? { path: toolPath, sha256: toolSha256, sourceHead, checkoutHead: head, bound: true }
    : { path: toolPath, sha256: toolSha256, sourceHead, checkoutHead: head, bound: false, reason: `it was built from ${sourceHead}, and the checkout is ${head}` };
}

/**
 * The release command's options from its arguments (`argv`, after the script's path), as `rehearse` takes them; the
 * signer, the log and the custody form's own are the command's. `--no-browser` is `browser: false`, under which the
 * command installs the browser trap before it runs anything (`armBrowserTrap`).
 */
export function commandOptions(argv) {
  return {
    keep: argv.includes("--keep"),
    probe: !argv.includes("--no-probe"),
    browser: !argv.includes("--no-browser"),
    reproduce: !argv.includes("--no-reproduce"),
    platformCard: argv.includes("--platform-card"),
  };
}

/** What the release command says once the browser trap holds for a release without a browser. */
export const TRAP_HOLDS = "--no-browser: the browser trap holds; the launcher this run loads is the trap's, so nothing here can find or start a browser";

/**
 * The browser trap a release without a browser runs under (`--no-browser`, the review of #979): installed by the command
 * itself before any script that can find or start a browser is loaded (`browser-trap.mjs`; a touch fails the run with 97
 * once its clean-up has run), then seen to hold: the launcher this process loads is the trap's. Throws when it does not
 * hold, and the release does not run.
 */
export async function armBrowserTrap(log = () => {}) {
  const { TRAP_EXIT_CODE, installBrowserTrap } = await import("./browser-trap.mjs");
  installBrowserTrap({ exitCode: TRAP_EXIT_CODE });
  const { findBrowser, launchBrowser } = await import("./browser-evidence.mjs");
  if (findBrowser.lexBrowserTrap !== true || launchBrowser.lexBrowserTrap !== true) {
    throw new Error("--no-browser: the browser trap does not hold (the launcher was loaded before it), so the release does not run");
  }
  log(TRAP_HOLDS);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const argv = process.argv.slice(2);
  const value = (name) => { const at = argv.indexOf(name); return at < 0 || at + 1 >= argv.length ? null : argv[at + 1]; };
  const options = { ...commandOptions(argv), log: (line) => console.error(`- ${line}`) };
  // Before anything else: with --no-browser the run cannot reach a browser, whatever a later step asks.
  if (!options.browser) {
    try {
      await armBrowserTrap(options.log);
    } catch (error) {
      console.error(error.message);
      process.exit(2);
    }
  }
  // The release signer: the owner's key and the identity it signs as, both or neither.
  const signingKey = value("--signing-key");
  const signerIdentity = value("--signer");
  if ((signingKey === null) !== (signerIdentity === null)) {
    console.error("--signing-key and --signer go together: the owner's signing key and the identity it signs as");
    process.exit(2);
  }
  if (signingKey !== null) {
    try {
      options.signer = releaseSigner({ privateKeyPem: readFileSync(signingKey === "-" ? 0 : signingKey), identity: signerIdentity });
    } catch (error) {
      console.error(`the release signer cannot be used: ${error.message}`);
      process.exit(2);
    }
  }
  const custody = value("--custody");
  const run = custody !== null
    ? (value("--checkpoint") && value("--tool")
      ? rehearseFromCustody({ tool: value("--tool"), custody, checkpoint: value("--checkpoint"), custodyEncoding: value("--custody-encoding") ?? "brotli", allowUnboundTool: argv.includes("--allow-unbound-tool"), ...options })
      : null)
    : value("--mount") !== null ? rehearse({ mount: value("--mount"), ...options }) : null;
  if (run === null) {
    console.error("usage: node scripts/image-rehearsal.mjs (--mount <v3-corpus directory or journey fixture mount> | --custody <custody directory> --checkpoint <mount-inputs.json> --tool <this commit's CI runtime artifact runtime/Lex.V3.Tool.dll> [--custody-encoding raw|brotli] [--allow-unbound-tool]) [--signing-key <PEM file, or - for standard input> --signer <identity>] [--keep] [--no-reproduce] [--no-probe | --no-browser] [--platform-card]");
    process.exit(2);
  }
  run.then(
    (result) => { console.log(JSON.stringify(result, null, 2)); },
    (error) => { console.error(error.message); process.exit(1); },
  );
}
