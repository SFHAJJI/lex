// The zero-traffic probe of a deployed revision: the deployment kit's check before the owner promotes anything
// (`deploy/deploy.ps1`; STATUS, the deployment kit). It is asked of the candidate revision's own URL, never of the
// app's public traffic, and of the release the revision was deployed from:
// - the release must read back first, under the signing identity's public key the owner names (`releaseFailures`:
//   the manifest's signature, every asset by size and hash, the image blob by blob and its signature);
// - health and the page: `GET /` sends a reader who names no language to `/fr/` (R61), with `Vary: Accept-Language`,
//   and `GET /fr/` answers 200 with the release image's own home page (`isOwnHomePage`: byte for byte as built, or as
//   the server renders it from its coverage answer, the built page's head byte for byte and the facts it was rendered
//   from), carrying the security headers the image run probes (CSP with `frame-ancestors 'none'`, HSTS,
//   `Referrer-Policy: no-referrer`, `nosniff`);
// - the 404 page (R60): an address nothing claims answers 404 with the release image's own French 404 page, byte for
//   byte, carrying the same security headers, for a reader who names no language;
// - the card: the evaluation card at its stable route is the release's card, byte for byte, as JSON;
// - the API: `coverage` answers from the mount the release names, by its corpus digest;
// - the server: its readiness route answers 204 (it holds the law), and the synthetic preview's route is unknown to it;
// - the clock (plan 0.8): the instant `coverage` was answered at is within a minute of this probe's own, so the date the
//   revision calls today in Luxembourg is today's; the probe says that date by the coded rule, and whether the instant
//   fell in the hour whose date the season's offset decides. A revision answers only at its own instant, so the clock
//   changes themselves are held where an instant can be chosen: the shared table both rules meet in CI;
// - an accented title (plan 0.8): a title the revision holds, asked for with its accents, in capitals and decomposed,
//   finds its work as it does without them (`accentedTitleFailures`; the image carries no language library);
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
import { layerTar, readLayout, readOciImage, readTar, servedPaths, serverFailures, v2RouteFailures } from "./image-rehearsal.mjs";
import { invokedDirectly } from "./invoked-directly.mjs";
import { askOperation, heldLuxembourgWorkFromAnswers, realMountRuns } from "./journey.mjs";
import { ASSETS, RELEASE_MANIFEST, releaseFailures } from "./release-assets.mjs";
import { readLargeFile } from "./large-bytes.mjs";
import { HOME_FACTS_ID } from "./live-home.mjs";
import { luxembourgDate, luxembourgOffset, luxembourgSummerTimeEnds, luxembourgSummerTimeStarts } from "./temporal.mjs";

const LIVE_PREFIX = "app/v3-web/";

/**
 * Whether `served` is the release image's own home page `built`: that page byte for byte (served as built), or that page
 * as the server renders it from its coverage answer (`live-home.mjs`, R67): the built page's head, byte for byte (its title,
 * its policy, its links: nothing in it depends on the answer), its script, and the facts it was rendered from.
 */
export function isOwnHomePage(served, built) {
  if (served.equals(built)) return true;
  const page = served.toString("utf8");
  const own = built.toString("utf8");
  const head = (text) => (text.includes("</head>") ? text.slice(0, text.indexOf("</head>")) : null);
  const script = /<script src="[^"]+" defer=""><\/script>/.exec(own)?.[0] ?? null;
  return head(own) !== null && head(page) === head(own) && script !== null && page.includes(script) && page.includes(`<script type="application/json" id="${HOME_FACTS_ID}">`);
}

/** An address nothing claims, asked of a revision for its 404 page (R60): a page request, under no law's address. */
export const NOT_FOUND_PROBE = "/fr/no-such-page";

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
 * page files, `card` the release's evaluation card, `corpusSha256` the corpus the release names. What the probe can say
 * but not hold (the revision's date in Luxembourg, a check with nothing to check) goes to `notes`; `now` is the probe's
 * clock, `ask` how an operation is asked of the revision.
 */
export async function revisionFailures(origin, { web, card, corpusSha256, notes = [], now = Date.now, ask = askOperation }) {
  const failures = [];
  const served = servedPaths(web);
  if (!served.has("/fr/")) return ["the release image holds no live page at /fr/, so the revision has nothing to be held to"];

  // The root sends a reader with no language preference to the French home page (R61), and that page is the image's own.
  const start = await fetch(`${origin}/`, { redirect: "manual" });
  await start.arrayBuffer();
  if (start.status !== 302 || start.headers.get("location") !== "/fr/") {
    failures.push(`GET / answered ${start.status}${start.headers.get("location") ? ` to ${start.headers.get("location")}` : ""}, not a redirect to /fr/`);
  }
  if (start.headers.get("vary") !== "Accept-Language") failures.push(`GET / varies by ${start.headers.get("vary")}, not Accept-Language`);
  const page = await fetch(`${origin}/fr/`, { redirect: "manual" });
  const body = Buffer.from(await page.arrayBuffer());
  if (page.status !== 200) {
    failures.push(`GET /fr/ answered ${page.status}, not 200`);
  } else if (!isOwnHomePage(body, served.get("/fr/"))) {
    failures.push("GET /fr/ answered a page that is not the release image's own home page, as built or rendered from its coverage answer");
  }
  const header = (name) => page.headers.get(name);
  const policy = `${cspValue()}; frame-ancestors 'none'`;
  if (header("content-security-policy") !== policy) failures.push(`GET /fr/ carries the CSP ${header("content-security-policy")}, not ${policy}`);
  if (!/^max-age=\d+/.test(header("strict-transport-security") ?? "")) failures.push("GET /fr/ carries no HSTS");
  if (header("referrer-policy") !== "no-referrer") failures.push(`GET /fr/ carries Referrer-Policy ${header("referrer-policy")}, not no-referrer`);
  if (header("x-content-type-options") !== "nosniff") failures.push("GET /fr/ carries no nosniff");

  // An address nothing claims is the release's own 404 page, in the language its path names, with a page's headers (R60).
  const notFound = new Map(web).get("not-found/fr.html");
  if (notFound === undefined) {
    failures.push("the release image holds no 404 page (not-found/fr.html), so the revision's answer to a page nothing claims cannot be held to it");
  } else {
    const missing = await fetch(`${origin}${NOT_FOUND_PROBE}`, { redirect: "manual" });
    const missingBody = Buffer.from(await missing.arrayBuffer());
    if (missing.status !== 404 || !missingBody.equals(notFound)) {
      failures.push(`GET ${NOT_FOUND_PROBE} answered ${missing.status}${missing.status === 404 ? " with a body that is not the release image's own 404 page" : ""}, not the 404 page`);
    } else if (missing.headers.get("content-security-policy") !== policy) {
      failures.push(`GET ${NOT_FOUND_PROBE} carries the CSP ${missing.headers.get("content-security-policy")}, not ${policy}`);
    }
  }

  const servedCard = await fetch(`${origin}${CARD_ROUTE}`, { redirect: "manual" });
  const cardBytes = Buffer.from(await servedCard.arrayBuffer());
  if (servedCard.status !== 200 || !(servedCard.headers.get("content-type") ?? "").startsWith("application/json")) {
    failures.push(`GET ${CARD_ROUTE} answered ${servedCard.status} ${servedCard.headers.get("content-type")}, not 200 application/json`);
  } else if (!cardBytes.equals(card)) {
    failures.push(`GET ${CARD_ROUTE} is not the release's evaluation card, byte for byte`);
  }

  const asked = now();
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
  const answered = now();
  if (envelope === null) {
    failures.push(`coverage answered ${coverage.status}, not a 200 envelope`);
  } else if (envelope.refusal !== null && envelope.refusal !== undefined) {
    failures.push(`coverage refused ${envelope.refusal.code}: the revision serves no mount`);
  } else if (envelope.result?.value?.mounted?.corpus_sha256 !== corpusSha256) {
    failures.push(`coverage answers from corpus ${envelope.result?.value?.mounted?.corpus_sha256}, not the release's ${corpusSha256}`);
  }
  if (envelope !== null) {
    const observedAt = envelope.context?.freshness?.observed_at;
    const clock = clockFailures(observedAt, { asked, answered });
    failures.push(...clock);
    if (clock.length === 0) notes.push(luxembourgTodayNote(observedAt));
  }
  if (envelope !== null && (envelope.refusal === null || envelope.refusal === undefined)) {
    failures.push(...await accentedTitleFailures(origin, { ask, notes }));
  }

  failures.push(...await serverFailures(origin));
  failures.push(...await v2RouteFailures(origin, { v3Files: served }));
  return failures;
}

/** How far the revision's clock may stand from this probe's, either way, before its date in Luxembourg is not trusted. */
export const CLOCK_TOLERANCE_MS = 60_000;

// The instant an answer was observed at, as the API writes it (V3EnvelopeJson: UTC, seven fractional digits).
const OBSERVED_AT = /^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{7}Z$/;

/**
 * The revision's clock (plan 0.8): the instant an answer says it was observed at, `observedAt`, is the instant the
 * revision will call today from (R13, item 3.4), so it must stand within `CLOCK_TOLERANCE_MS` of the probe's own clock
 * between `asked` and `answered` (milliseconds). Failures, empty when it does.
 */
export function clockFailures(observedAt, { asked, answered }) {
  if (typeof observedAt !== "string" || !OBSERVED_AT.test(observedAt)) {
    return [`coverage was observed at ${JSON.stringify(observedAt)}, not a UTC instant in the API's shape`];
  }
  const at = Date.parse(observedAt.replace(/(\.[0-9]{3})[0-9]{4}Z$/, "$1Z"));
  if (at < asked - CLOCK_TOLERANCE_MS || at > answered + CLOCK_TOLERANCE_MS) {
    return [`the revision's clock read ${observedAt}, more than a minute from this probe's (${new Date(asked).toISOString()} to ${new Date(answered).toISOString()}): the day it calls today in Luxembourg could be another`];
  }
  return [];
}

/**
 * What the probe says of the revision's day (not a check: a revision answers at its own instant only): its date in
 * Luxembourg by the coded rule the API and the pages share, the offset, whether the instant fell in the one hour of the
 * night (22:00 to 23:00 UTC) whose date the season's offset decides, and the next clock change.
 */
export function luxembourgTodayNote(observedAt) {
  const year = Number(observedAt.slice(0, 4));
  const at = Date.parse(observedAt.replace(/(\.[0-9]{3})[0-9]*Z$/, "$1Z"));
  const changes = [luxembourgSummerTimeStarts(year), luxembourgSummerTimeEnds(year), ...(year < 2099 ? [luxembourgSummerTimeStarts(year + 1)] : [])];
  const next = changes.find((change) => Date.parse(change) > at);
  const decided = observedAt.slice(11, 13) === "22" ? "in the hour whose date the season's offset decides" : "outside the hour whose date the season's offset decides (22:00 to 23:00 UTC)";
  return `the revision's clock read ${observedAt}: ${luxembourgDate(observedAt)} in Luxembourg, UTC${luxembourgOffset(observedAt)}, ${decided}; `
    + `${next ? `the next clock change is at ${next}` : "no clock change is left in the rule's range"}, and the changes themselves are held by the table both rules meet in CI`;
}

// The bounds search sets on a query (V3CorpusMount.SearchMaxQueryCharacters, SearchMaxTerms).
const SEARCH_MAX_CHARACTERS = 512;
const SEARCH_MAX_TERMS = 32;
/** How many of the works its search for "loi" lists the probe looks through for an accented title. */
export const TITLE_WORKS_ASKED = 10;

/** A title as a reader without accents types it: decomposed, its combining marks dropped. */
export function withoutAccents(title) {
  return title.normalize("NFD").replace(/\p{Mn}/gu, "");
}

/**
 * A title in capitals, letter by letter, as a reader types it: a letter whose capital is two letters (ß) or is not its
 * own capital back (µ, ı, ſ, whose capitals lower to μ, i, s) is kept, so the capitals name the same title.
 */
function inCapitals(title) {
  return [...title].map((letter) => {
    const capital = letter.toUpperCase();
    return [...capital].length === 1 && capital.toLowerCase() === letter ? capital : letter;
  }).join("");
}

/**
 * The accented titles a dossier names, in its order (titles, then short titles), each with its language and expression:
 * those that change when decomposed (they carry an accent), with no control character, within search's bounds.
 */
function accentedTitlesOf(dossier) {
  const titles = [];
  for (const group of dossier?.result?.value?.titles ?? []) {
    for (const { title } of [...(group.titles ?? []), ...(group.short_titles ?? [])]) {
      if (typeof title !== "string" || title.normalize("NFD") === title || /\p{Cc}/u.test(title)) continue;
      if ([...title].length > SEARCH_MAX_CHARACTERS || new Set(title.split(/\s+/).filter(Boolean)).size > SEARCH_MAX_TERMS) continue;
      titles.push({ title, language: group.language, expression: group.expression_iri });
    }
  }
  return titles;
}

/**
 * An accented title the revision holds, found (plan 0.8). The image carries no language library, and while the title fold
 * asked for one the server stopped removing the accents from what a reader typed, while every index holds its titles
 * without them: "congé parental" found nothing on the deployed revision. The title is the revision's own: of the works its
 * search for "loi" lists (the first `TITLE_WORKS_ASKED`), the first whose dossier names an accented title that, with its
 * accents removed, resolves to that title's work (the control: a title two works share resolves to neither, and the next
 * is tried). Then the title as published, in capitals and decomposed must each resolve to the same work by the same match,
 * and a strict search for it must name that work alone. Failures, empty when it holds; an index holding no titles has
 * nothing to check, which `notes` says.
 */
export async function accentedTitleFailures(origin, { ask = askOperation, notes = [] } = {}) {
  const listed = await ask(origin, "search", { query: "loi", language: "fra" });
  if (listed === null || (listed.refusal !== null && listed.refusal !== undefined)) {
    return [`search for "loi" answered ${listed === null ? "no envelope" : `a refusal, ${listed.refusal.code}`}, so no accented title could be checked`];
  }
  if (listed.result?.value?.work_resolution?.outcome === "no_titles_held") {
    notes.push("an accented title: not checked, the revision's index holds no titles");
    return [];
  }
  const works = [...new Set((listed.result?.value?.hits ?? []).map((hit) => hit.work_key).filter(Boolean))].slice(0, TITLE_WORKS_ASKED);
  for (const work of works) {
    const dossier = await ask(origin, "dossier", { identifier: `/lu-legilux/${work}` });
    for (const { title, language, expression } of accentedTitlesOf(dossier)) {
      const control = await ask(origin, "resolve", { identifier: withoutAccents(title) });
      const found = control !== null && (control.refusal === null || control.refusal === undefined) ? control.result?.value : null;
      if (!found?.expressions?.includes(expression)) continue;

      const failures = [];
      for (const [form, identifier] of [["as published", title], ["in capitals", inCapitals(title)], ["decomposed", title.normalize("NFD")]]) {
        const answer = await ask(origin, "resolve", { identifier });
        const value = answer !== null && (answer.refusal === null || answer.refusal === undefined) ? answer.result?.value : null;
        if (value === null || value === undefined) {
          failures.push(`resolve of "${title}" ${form} answered ${answer === null ? "no envelope" : `a refusal, ${answer.refusal.code}`}, where without its accents it finds ${found.work_identifier}`);
        } else if (value.work_identifier !== found.work_identifier || value.match_reason !== found.match_reason) {
          failures.push(`resolve of "${title}" ${form} finds ${value.work_identifier} by ${value.match_reason}, where without its accents it finds ${found.work_identifier} by ${found.match_reason}`);
        }
      }
      const searched = await ask(origin, "search", { query: title, language, mode: "strict", limit: 1 });
      const resolution = searched?.result?.value?.work_resolution;
      if (resolution?.outcome !== "one_work" || resolution.work?.work_identifier !== found.work_identifier) {
        failures.push(`search for "${title}" names ${resolution?.outcome ?? (searched === null ? "no envelope" : `a refusal, ${searched.refusal?.code}`)}, not ${found.work_identifier} alone`);
      }
      notes.push(`an accented title: "${title}" (${found.work_identifier}), asked as published, in capitals and decomposed`);
      return failures;
    }
  }
  return [`none of the ${works.length} works the revision's search for "loi" lists names an accented title it resolves without its accents, so no accented title could be checked`];
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
 * build report they read, and the Luxembourg work they ask for chosen from the revision's own answers (`held`; the
 * revision's index is not at hand). Failures, each prefixed with its step; empty when every step passes.
 */
export async function browserFailures(origin, releaseDirectory, { browser, runs = realMountRuns, held = heldLuxembourgWorkFromAnswers }) {
  const work = await mkdtemp(join(tmpdir(), "lex-deploy-browser-"));
  try {
    await writeFile(join(work, "build-report.json"), await readFile(join(releaseDirectory, ASSETS.mountReport)));
    const results = await runs(null, work, { servedByApi: true, keyboard: false, startServer: async () => remoteRevision(origin) }, browser, null,
      { held: await held(origin) });
    return results.flatMap(([label, { failures }]) => failures.map((failure) => `${label}: ${failure}`));
  } finally {
    await rm(work, { recursive: true, force: true });
  }
}

/**
 * The probe of a revision deployed from the release at `releaseDirectory`: the release read back under the signing
 * identity's key (`publicKeyPem`), and as signed by the release signer `identity` when one is named, then the revision
 * held to what the release holds. Failures, empty when both hold; what the probe says but does not hold goes to `notes`.
 */
export async function deployedReleaseFailures(origin, releaseDirectory, { publicKeyPem, identity = null, notes = [] }) {
  const release = await releaseFailures(releaseDirectory, { publicKeyPem, identity });
  if (release.length > 0) return release.map((failure) => `the release does not read back: ${failure}`);
  const manifest = JSON.parse(await readFile(join(releaseDirectory, RELEASE_MANIFEST), "utf8"));
  return revisionFailures(origin, {
    web: liveFilesOf(await readLargeFile(join(releaseDirectory, ASSETS.image))),
    card: await readFile(join(releaseDirectory, ASSETS.card)),
    corpusSha256: manifest.corpus.sha256,
    notes,
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
  const identity = value("--signer");
  const withBrowser = argv.includes("--browser");
  if (release === null || publicKey === null || (origin !== null && !/^https?:\/\//.test(origin)) || (argv.includes("--signer") && !identity)) {
    console.error("usage: node scripts/deploy-probe.mjs --release <release directory> --public-key <signing identity's public key, PEM> [--signer <the release signer's identity>] [--origin <the candidate revision's URL> [--browser]]");
    console.error("       with --signer, the release must be signed by that release signer, never a rehearsal");
    console.error("       without --origin, only the release is read back (deploy.ps1 does this before it touches Azure)");
    process.exit(2);
  }
  const publicKeyPem = await readFile(publicKey, "utf8");
  if (origin === null) {
    const failures = (await releaseFailures(release, { publicKeyPem, identity })).map((failure) => `the release does not read back: ${failure}`);
    for (const failure of failures) console.error(`- ${failure}`);
    const manifest = failures.length === 0 ? JSON.parse(await readFile(join(release, RELEASE_MANIFEST), "utf8")) : null;
    console.log(manifest === null ? `${failures.length} release failure(s)`
      : `the release at ${release} reads back under the signing identity's key, ${manifest.rehearsal ? "as a rehearsal" : `as a release signed as ${manifest.signer}`}`);
    process.exit(failures.length === 0 ? 0 : 1);
  }
  const target = origin.replace(/\/+$/, "");
  const notes = [];
  const failures = await deployedReleaseFailures(target, release, { publicKeyPem, identity, notes });
  for (const note of notes) console.log(note);
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
