// The zero-traffic probe of a deployed revision: the deployment kit's check before the owner promotes anything
// (`deploy/deploy.ps1`; STATUS, the deployment kit). It is asked of the candidate revision's own URL, never of the
// app's public traffic, and of the release the revision was deployed from:
// - the release must read back first, under the signing identity's public key the owner names (`releaseFailures`:
//   the manifest's signature, every asset by size and hash, the image blob by blob and its signature);
// - health and the page: `GET /` answers 200 with the release image's own page, byte for byte, carrying the
//   security headers the image run probes (CSP with `frame-ancestors 'none'`, HSTS, `Referrer-Policy: no-referrer`,
//   `nosniff`);
// - the card: the evaluation card at its stable route is the release's card, byte for byte, as JSON;
// - the API: `coverage` answers from the mount the release names, by its corpus digest;
// - V2 absent: every route V2 served answers 404, except where V3 serves the same path from its own pages;
// - the browser probes (`--browser`): the journey's real-mount steps through a browser against the revision, each
//   page held to what the API answers its request, its hydration, console, paint and security headers, and every
//   citation verified. Privacy cannot be observed on a remote revision (its process and files are not ours to watch),
//   so this probe states that rather than passing it: the image run's privacy probe held the same image digest.
// It sends no request anywhere but the origin it is given, and writes nothing but a temporary copy of the release's
// mount report, removed when it ends.
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { findBrowser } from "./browser-evidence.mjs";
import { cspValue } from "./csp.mjs";
import { CARD_ROUTE } from "./evaluation-card.mjs";
import { layerTar, readLayout, readOciImage, readTar, servedPaths, v2RouteFailures } from "./image-rehearsal.mjs";
import { invokedDirectly } from "./invoked-directly.mjs";
import { realMountRuns } from "./journey.mjs";
import { ASSETS, RELEASE_MANIFEST, releaseFailures } from "./release-assets.mjs";

const LIVE_PREFIX = "app/v3-web/";

/** The live page files a release image serves, as `[path, bytes]` from under `app/v3-web/`, the last layer's copy winning. */
export function liveFilesOf(archive) {
  const image = readOciImage(readLayout(archive));
  const files = new Map();
  for (const layer of image.layers) {
    for (const entry of readTar(layerTar(layer))) {
      if (entry.type === "file" && entry.path.startsWith(LIVE_PREFIX)) files.set(entry.path.slice(LIVE_PREFIX.length), entry.bytes);
    }
  }
  return [...files.entries()];
}

/**
 * What a deployed revision must answer at `origin`, as failures (empty when it holds): `web` the release image's live
 * page files, `card` the release's evaluation card, `corpusSha256` the corpus the release names.
 */
export async function revisionFailures(origin, { web, card, corpusSha256 }) {
  const failures = [];
  const served = servedPaths(web);
  if (!served.has("/")) return ["the release image holds no live page at /, so the revision has nothing to be held to"];

  const page = await fetch(`${origin}/`, { redirect: "manual" });
  const body = Buffer.from(await page.arrayBuffer());
  if (page.status !== 200) {
    failures.push(`GET / answered ${page.status}, not 200`);
  } else if (!body.equals(served.get("/"))) {
    failures.push("GET / answered a page that is not the release image's own, byte for byte");
  }
  const header = (name) => page.headers.get(name);
  const policy = `${cspValue()}; frame-ancestors 'none'`;
  if (header("content-security-policy") !== policy) failures.push(`GET / carries the CSP ${header("content-security-policy")}, not ${policy}`);
  if (!/^max-age=\d+/.test(header("strict-transport-security") ?? "")) failures.push("GET / carries no HSTS");
  if (header("referrer-policy") !== "no-referrer") failures.push(`GET / carries Referrer-Policy ${header("referrer-policy")}, not no-referrer`);
  if (header("x-content-type-options") !== "nosniff") failures.push("GET / carries no nosniff");

  const servedCard = await fetch(`${origin}${CARD_ROUTE}`, { redirect: "manual" });
  const cardBytes = Buffer.from(await servedCard.arrayBuffer());
  if (servedCard.status !== 200 || !(servedCard.headers.get("content-type") ?? "").startsWith("application/json")) {
    failures.push(`GET ${CARD_ROUTE} answered ${servedCard.status} ${servedCard.headers.get("content-type")}, not 200 application/json`);
  } else if (!cardBytes.equals(card)) {
    failures.push(`GET ${CARD_ROUTE} is not the release's evaluation card, byte for byte`);
  }

  const coverage = await fetch(`${origin}/api/v3/coverage`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ operation_id: "coverage", parameters: {} }),
    redirect: "manual",
  });
  let envelope = null;
  try {
    envelope = coverage.status === 200 ? await coverage.json() : null;
  } catch {
    envelope = null;
  }
  if (envelope === null) {
    failures.push(`coverage answered ${coverage.status}, not a 200 envelope`);
  } else if (envelope.refusal !== null && envelope.refusal !== undefined) {
    failures.push(`coverage refused ${envelope.refusal.code}: the revision serves no mount`);
  } else if (envelope.result?.value?.mounted?.corpus_sha256 !== corpusSha256) {
    failures.push(`coverage answers from corpus ${envelope.result?.value?.mounted?.corpus_sha256}, not the release's ${corpusSha256}`);
  }

  failures.push(...await v2RouteFailures(origin, { v3Files: served }));
  return failures;
}

/** What the browser probes cannot observe on a remote revision, stated with every browser probe run. */
export const REMOTE_PRIVACY_NOTE =
  "privacy is not observed here: a deployed revision's process output and files are not this probe's to watch; the image run's "
  + "privacy probe (nothing written after the first answer, no query text, address or user agent recorded) held the same image digest";

/**
 * A deployed revision as the journey's runner takes a server: its origin, and nothing of its process, which a remote
 * revision does not expose. The runner's privacy checks therefore have nothing to read here and prove nothing about the
 * revision; `REMOTE_PRIVACY_NOTE` says so wherever these probes report.
 */
export function remoteRevision(origin) {
  return {
    origin,
    output: () => "",
    outputAtStart: 0,
    fileWatch: { stop: async () => [] },
    changedFiles: async () => [],
    close: async () => {},
  };
}

/**
 * The browser probes of a revision deployed from the release at `releaseDirectory`: the journey's real-mount steps
 * (`realMountRuns`, or `runs` in a test) through `browser` against `origin`, the release's mount report standing as the
 * build report they read. Failures, each prefixed with its step; empty when every step passes.
 */
export async function browserFailures(origin, releaseDirectory, { browser, runs = realMountRuns }) {
  const work = await mkdtemp(join(tmpdir(), "lex-deploy-browser-"));
  try {
    await writeFile(join(work, "build-report.json"), await readFile(join(releaseDirectory, ASSETS.mountReport)));
    const results = await runs(null, work, { servedByApi: true, keyboard: false, startServer: async () => remoteRevision(origin) }, browser, null);
    return results.flatMap(([label, { failures }]) => failures.map((failure) => `${label}: ${failure}`));
  } finally {
    await rm(work, { recursive: true, force: true });
  }
}

/**
 * The probe of a revision deployed from the release at `releaseDirectory`: the release read back under the signing
 * identity's key (`publicKeyPem`), then the revision held to what the release holds. Failures, empty when both hold.
 */
export async function deployedReleaseFailures(origin, releaseDirectory, { publicKeyPem }) {
  const release = await releaseFailures(releaseDirectory, { publicKeyPem });
  if (release.length > 0) return release.map((failure) => `the release does not read back: ${failure}`);
  const manifest = JSON.parse(await readFile(join(releaseDirectory, RELEASE_MANIFEST), "utf8"));
  return revisionFailures(origin, {
    web: liveFilesOf(await readFile(join(releaseDirectory, ASSETS.image))),
    card: await readFile(join(releaseDirectory, ASSETS.card)),
    corpusSha256: manifest.corpus.sha256,
  });
}

if (invokedDirectly(import.meta.url, process.argv[1])) {
  const argv = process.argv.slice(2);
  const value = (name) => {
    const at = argv.indexOf(name);
    return at === -1 ? null : argv[at + 1] ?? null;
  };
  const origin = value("--origin");
  const release = value("--release");
  const publicKey = value("--public-key");
  const withBrowser = argv.includes("--browser");
  if (release === null || publicKey === null || (origin !== null && !/^https?:\/\//.test(origin))) {
    console.error("usage: node scripts/deploy-probe.mjs --release <release directory> --public-key <signing identity's public key, PEM> [--origin <the candidate revision's URL> [--browser]]");
    console.error("       without --origin, only the release is read back (deploy.ps1 does this before it touches Azure)");
    process.exit(2);
  }
  const publicKeyPem = await readFile(publicKey, "utf8");
  if (origin === null) {
    const failures = (await releaseFailures(release, { publicKeyPem })).map((failure) => `the release does not read back: ${failure}`);
    for (const failure of failures) console.error(`- ${failure}`);
    console.log(failures.length === 0 ? `the release at ${release} reads back under the signing identity's key` : `${failures.length} release failure(s)`);
    process.exit(failures.length === 0 ? 0 : 1);
  }
  const target = origin.replace(/\/+$/, "");
  const failures = await deployedReleaseFailures(target, release, { publicKeyPem });
  if (failures.length === 0 && withBrowser) {
    failures.push(...await browserFailures(target, release, { browser: await findBrowser() }));
    console.log(REMOTE_PRIVACY_NOTE);
  }
  for (const failure of failures) console.error(`- ${failure}`);
  console.log(failures.length === 0
    ? `the revision at ${origin} answers as the release holds${withBrowser ? ", in the browser too" : ""}`
    : `${failures.length} probe failure(s)`);
  process.exit(failures.length === 0 ? 0 : 1);
}
