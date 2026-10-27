// The zero-traffic probe of a deployed revision: the deployment kit's check before the owner promotes anything
// (`deploy/deploy.ps1`; STATUS, the deployment kit). It is asked of the candidate revision's own URL, never of the
// app's public traffic, and of the release the revision was deployed from:
// - the release must read back first, under the signing identity's public key the owner names (`releaseFailures`:
//   the manifest's signature, every asset by size and hash, the image blob by blob and its signature);
// - health and the page: `GET /` sends a reader who names no language to `/fr/` (R61), with `Vary: Accept-Language`,
//   and `GET /fr/` answers 200 with the release image's own home page (`homePageFailures`: byte for byte as built, or
//   as the server renders it from its coverage answer: everything outside the root's content, the facts block and the
//   freshness strip and banner the built page's, byte for byte, facts a home page's, and the whole page what the release's own renderer renders from
//   the probe's coverage answer on the facts' day), carrying the security headers the image run probes (CSP with
//   `frame-ancestors 'none'`, HSTS, `Referrer-Policy: no-referrer`, `nosniff`);
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
// - the legal pages (the plan's item 7.1; `legal-pages.mjs`): each of the ten the release image holds answers 200 with the
//   image's own bytes, the page's CSP and no cookie; while one shows what the owner's decision of 2026-10-19 keeps off the
//   site (an e-mail address, a mail or telephone link, a placeholder a template left, a postcode: `SHOWN_NOWHERE`), the
//   probe says so beside its result, and with `--opening` (every `-Public` deployment) it fails, as it does when the image
//   holds none of them. `--opening` with no `--release` is the opening's own check (`deploy.ps1 -Open`): the site's served
//   legal pages, each 200, without a cookie and showing none of those, or the site is not opened (`openingFailures`);
// - no cookie: neither the home page nor a legal page sets one (the cookies page says no page does);
// - the home page's example searches (R47): each one a home page lists, asked of the revision as the search page asks it,
//   opens the article the page says it opens (`homeExampleFailures`);
// - the browser probes (`--browser`): the journey's real-mount steps through a browser against the revision, each
//   page held to what the API answers its request, its hydration, console, paint and security headers, and every
//   citation verified. Privacy cannot be observed on a remote revision (its process and files are not ours to watch),
//   so this probe states that rather than passing it: only the release's own image run observes it, and only when the
//   release was probed (its report's `privacy`; a release made with `--no-probe` was not).
//   Without `--browser` no browser is looked for or started (`findBrowser` is never called), and the probe says that no
//   page of the revision was walked in a browser (`NOT_WALKED_NOTE`); `deploy.ps1` runs it so unless `-BrowserProbe`.
// - the release's maintenance revision (`--maintenance`, `deploy.ps1 -Maintenance`; R60, R108, R115; the plan's item 10.14),
//   instead of all the above: the release read back as above, then the revision's readiness 204, each page asked
//   (`MAINTENANCE_PROBES`, the root among them) 503 with Retry-After, a page's headers, no cookie and the release image's own
//   maintenance page in its language, byte for byte, and the API's coverage 503 with the problem `service_in_maintenance`
//   (`maintenanceFailures`). HTTP only: it never walks a page in a browser.
// It sends no request anywhere but the origin it is given, and writes nothing but a temporary copy of the release's
// mount report, removed when it ends.
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import vm from "node:vm";
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
import { HOME_FACTS_ID, readHomeFactsValue } from "./live-home.mjs";
import { INDEXED, KEPT_OUT, KEPT_OUT_META, NEVER_INDEXED_META } from "./search-engines.mjs";
import { LEGAL_PAGE_PATHS, builtLegalPageFailures, openingFailures } from "./legal-pages.mjs";
import { MAINTENANCE_DIRECTORY } from "./not-found.mjs";
import { INTERFACE_LANGUAGES } from "./permalinks.mjs";
import { luxembourgDate, luxembourgOffset, luxembourgSummerTimeEnds, luxembourgSummerTimeStarts } from "./temporal.mjs";

const LIVE_PREFIX = "app/v3-web/";

/** The home page's root, which the server renders from the coverage answer (`live-home-page.jsx`). */
const HOME_ROOT_OPEN = '<div id="live-home-root">';
const FACTS_OPEN = `<script type="application/json" id="${HOME_FACTS_ID}">`;

/**
 * A home page in its parts: `before`, everything up to the root's content; `root`, that content; `facts`, the facts block's
 * text when the page carries one (rendered) or null (as built); and `after`, the root's closing tag and everything after the
 * root and the facts block. Null when the page has no single root followed by the facts block or by its script.
 */
export function homePageParts(text) {
  const open = text.indexOf(HOME_ROOT_OPEN);
  if (open < 0 || text.indexOf(HOME_ROOT_OPEN, open + 1) >= 0) return null;
  const start = open + HOME_ROOT_OPEN.length;
  const factsAt = text.indexOf(FACTS_OPEN, start);
  // The root is followed by the facts block (rendered) or by the script that hydrates it (as built).
  const next = factsAt >= 0 ? factsAt : text.indexOf('<script src="', start);
  if (next < 0) return null;
  const close = text.lastIndexOf("</div>", next);
  if (close < start || close + "</div>".length !== next) return null;
  if (factsAt < 0) return { before: text.slice(0, start), root: text.slice(start, close), facts: null, after: text.slice(close) };
  const end = text.indexOf("</script>", factsAt);
  if (end < 0 || text.indexOf(FACTS_OPEN, end) >= 0) return null;
  return {
    before: text.slice(0, start),
    root: text.slice(start, close),
    facts: text.slice(factsAt + FACTS_OPEN.length, end),
    after: `</div>${text.slice(end + "</script>".length)}`,
  };
}

/**
 * The home page the release's own server renderer (`ssr/{language}.js`, bytes) renders from `coverage`, the coverage
 * envelope, on `today`: the envelope answered at midday UTC that day (the same calendar day in Luxembourg, either season).
 */
export function renderedHomePage(renderer, coverage, today, language = "fr") {
  const context = vm.createContext({ TextEncoder });
  vm.runInContext(renderer.toString("utf8"), context, { timeout: 10_000 });
  const envelope = structuredClone(coverage);
  envelope.context = { ...envelope.context, freshness: { ...envelope.context?.freshness, observed_at: `${today}T12:00:00.0000000Z` } };
  return context.lexSsr.render("home", JSON.stringify(envelope), `/${language}/`);
}

/**
 * Why `served` is not the release image's own home page `built`, as failures (empty when it is): the page as built, byte
 * for byte; or that page as the server renders it from its coverage answer (`live-home.mjs`, R67), which is held to
 * everything the probe can hold it to:
 * - everything outside the root's content and the facts block is the built page's, byte for byte (the head, the
 *   heading, the footer: nothing there depends on the answer), but its robots element, which follows the facts (a home
 *   page that counts the mounted law carries the kept-out marker, which a server opened to search engines removes; the
 *   page as built never is: `search-engines.mjs`, review of #976, F2), and the footer's freshness strip and the staleness
 *   banner's container, which say the answer's freshness and commits (R87, R23; `withoutFreshness`) and are held by the
 *   whole page's rendering below;
 * - the facts block is a home page's facts (`readHomeFactsValue`), today and a coverage line of served counts;
 * - the whole page is what the release's own renderer (`renderer`, the bytes of `ssr/fr.js`) renders from the probe's
 *   own coverage answer (`coverage`) on the facts' day, byte for byte, so the root says that answer's counts and nothing
 *   typed in their place. With no renderer or no answer to render from, the rendered page cannot be held, and fails.
 */
/** A page's head with its robots element left out, either one (`search-engines.mjs`). */
const withoutRobots = (text) => text.replace(KEPT_OUT_META, "").replace(NEVER_INDEXED_META, "");

// The footer's freshness strip and the staleness banner's container (`Freshness.jsx`; R87, R23): said from the answer the
// page was rendered from, so they are held by the whole page's rendering below, and set aside where the page is held to the
// page as built. The strip holds no section inside it and the banner no aside, so each ends at its first closing tag.
const FRESHNESS_SLOT = /<div id="site-freshness"[^>]*><section class="freshness-strip"[\s\S]*?<\/section><\/div>/g;
const STALENESS_SLOT = /<div id="site-staleness"[^>]*>(?:<aside class="staleness-banner"[\s\S]*?<\/aside>)?<\/div>/g;

/** A page with its freshness strip and its staleness banner's container each set aside as one mark. */
export const withoutFreshness = (text) => text.replace(FRESHNESS_SLOT, "<!--freshness-->").replace(STALENESS_SLOT, "<!--staleness-->");

/** What a page is held to outside its root and its facts: its head without its robots element, and without its freshness. */
const outsideAnswer = (text) => withoutFreshness(withoutRobots(text));

export function homePageFailures(served, built, { renderer = null, coverage = null } = {}) {
  if (served.equals(built)) return [];
  const notOwn = "GET /fr/ answered a page that is not the release image's own home page, as built or rendered from its coverage answer";
  const own = homePageParts(built.toString("utf8"));
  if (own === null || own.facts !== null) return [`${notOwn}: the release image's home page as built has no root to hold it to`];
  const page = served.toString("utf8");
  const parts = homePageParts(page);
  if (parts === null || parts.facts === null) return [`${notOwn}: it carries no root followed by the facts it was rendered from`];
  const failures = [];
  if (outsideAnswer(parts.before) !== outsideAnswer(own.before) || withoutFreshness(parts.after) !== withoutFreshness(own.after)) {
    failures.push(`${notOwn}: outside its root and its facts it is not the built page, byte for byte`);
  }
  let facts = null;
  try {
    facts = readHomeFactsValue(JSON.parse(parts.facts));
  } catch {
    facts = null;
  }
  if (facts === null) return [...failures, `${notOwn}: its facts are not a home page's facts`];
  if (renderer === null) return [...failures, `${notOwn}: the release image holds no server renderer (ssr/fr.js) to render it again`];
  if (coverage === null) return [...failures, `${notOwn}: the probe has no coverage answer to render it again from`];
  if (facts.today === null) return [...failures, `${notOwn}: its facts name no today to render it again on`];
  let again = null;
  try {
    again = renderedHomePage(renderer, coverage, facts.today);
  } catch (error) {
    return [...failures, `${notOwn}: the release's renderer could not render it again (${error?.message ?? error})`];
  }
  // The server opened to search engines serves the render without its kept-out marker (`servedPage`).
  if (again !== page && again.replace(KEPT_OUT_META, "") !== page) {
    failures.push(`${notOwn}: it is not what the release's renderer renders from this revision's coverage answer on ${facts.today}, byte for byte`);
  }
  return failures;
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
 * clock, `ask` how an operation is asked of the revision. Its pages, card and API (`servedPageFailures`), then the
 * server's own answers (`serverFailures`) and V2's routes.
 */
export async function revisionFailures(origin, options) {
  const failures = await servedPageFailures(origin, options);
  const served = servedPaths(options.web);
  if (!served.has("/fr/")) return failures;
  failures.push(...await serverFailures(origin));
  // robots.txt and the sitemap are held to the deployment's own switch (deploy.ps1 -SearchEngines): a revision opened to
  // search engines serves V3's indexed robots.txt and its sitemap index, one kept out neither (the probe of 27 October).
  failures.push(...await v2RouteFailures(origin, { v3Files: served, searchEngines: options.searchEngines ?? KEPT_OUT }));
  return failures;
}

/**
 * Everything `revisionFailures` holds a revision to but the server's own answers and V2's routes, which the image
 * rehearsal asks for itself and reports apart (`image-rehearsal.mjs`, `probeImage`): the root's redirect, the home page
 * (as built, or as the release's renderer renders the revision's coverage answer) and its security headers, the 404 page,
 * the card at its stable route, coverage from the release's corpus, the clock and an accented title. Every one is an HTTP
 * request; none needs a browser.
 */
export async function servedPageFailures(origin, { web, card, corpusSha256, notes = [], promotion = [], now = Date.now, ask = askOperation, opening = false, homeExamples = undefined }) {
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
  // The page itself is held once the probe has its own coverage answer to render it again from (below).
  if (page.status !== 200) failures.push(`GET /fr/ answered ${page.status}, not 200`);
  const header = (name) => page.headers.get(name);
  const policy = `${cspValue()}; frame-ancestors 'none'`;
  if (header("content-security-policy") !== policy) failures.push(`GET /fr/ carries the CSP ${header("content-security-policy")}, not ${policy}`);
  if (!/^max-age=\d+/.test(header("strict-transport-security") ?? "")) failures.push("GET /fr/ carries no HSTS");
  if (header("referrer-policy") !== "no-referrer") failures.push(`GET /fr/ carries Referrer-Policy ${header("referrer-policy")}, not no-referrer`);
  if (header("x-content-type-options") !== "nosniff") failures.push("GET /fr/ carries no nosniff");
  if (header("set-cookie") !== null) failures.push("GET /fr/ sets a cookie, which the cookies page says no page does");

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
    // What the coverage answer says of the data's age decides whether promotion may go ahead (R87, O11), apart from
    // whether the revision answers as the release holds.
    promotion.push(...dataAgeFailures(envelope));
    const observedAt = envelope.context?.freshness?.observed_at;
    const clock = clockFailures(observedAt, { asked, answered });
    failures.push(...clock);
    if (clock.length === 0) notes.push(luxembourgTodayNote(observedAt));
  }
  if (envelope !== null && (envelope.refusal === null || envelope.refusal === undefined)) {
    failures.push(...await accentedTitleFailures(origin, { ask, notes }));
  }
  // The home page (R67): the release's own, as built or as its renderer renders this revision's coverage answer.
  if (page.status === 200) {
    failures.push(...homePageFailures(body, served.get("/fr/"), { renderer: new Map(web).get("ssr/fr.js") ?? null, coverage: envelope }));
  }
  // The home page's example searches (R47; `home-examples-run.mjs`): each one a home page lists, asked of this revision as
  // the search page asks it, must open the article the page says it opens; with the record the build carried
  // (`homeExamples`, the release rehearsal's), the page must list exactly its examples. Each home page the image holds.
  const { homeExampleFailures } = await import("./home-examples-run.mjs");
  failures.push(...await homeExampleFailures(origin, { expected: homeExamples, languages: ["fr", "en"].filter((language) => served.has(`/${language}/`)) }));
  failures.push(...await legalPageFailures(origin, web, { policy, notes, opening }));
  return failures;
}

/**
 * The legal pages a revision serves (the plan's item 7.1), as failures: each of the ten the release image holds
 * (`LEGAL_PAGE_PATHS`), asked of the revision, must answer 200 with the image's own bytes, the page's CSP (`policy`) and no
 * cookie. What keeps the pages from the opening (an image holding none of them, a page showing anything of `SHOWN_NOWHERE`)
 * is said in `notes`, and with `opening` it fails: every `-Public` deployment holds its candidate to it.
 */
export async function legalPageFailures(origin, web, { policy = `${cspValue()}; frame-ancestors 'none'`, notes = [], opening = false } = {}) {
  const failures = [];
  const files = new Map(web);
  const held = LEGAL_PAGE_PATHS.filter((path) => files.has(path.slice(1)));
  for (const path of held) {
    const answer = await fetch(`${origin}${path}`, { redirect: "manual" });
    const bytes = Buffer.from(await answer.arrayBuffer());
    if (answer.status !== 200 || !bytes.equals(files.get(path.slice(1)))) {
      failures.push(`GET ${path} answered ${answer.status}${answer.status === 200 ? " with a body that is not the release image's own legal page" : ""}, not the legal page`);
      continue;
    }
    if (answer.headers.get("content-security-policy") !== policy) failures.push(`GET ${path} carries the CSP ${answer.headers.get("content-security-policy")}, not ${policy}`);
    if (answer.headers.get("set-cookie") !== null) failures.push(`GET ${path} sets a cookie, which the cookies page says no page does`);
  }
  const unready = builtLegalPageFailures(web);
  if (unready.length > 0) {
    if (opening) failures.push(...unready.map((failure) => `the legal pages are not ready to publish, so the site may not open: ${failure}`));
    else notes.push(`the legal pages are not ready to publish, so the site may not open (deploy.ps1 -Open and -Public refuse it): ${unready.join("; ")}`);
  }
  return failures;
}

/**
 * Why promotion refuses the data a revision serves, from its answer's freshness header (the specification of record's R87;
 * O11's maximum, 30 days by the owner's answer of 2026-10-19, which the answer states): a publisher channel whose newest completed collection is past the
 * maximum data age at the answer, or one whose completion the mount does not state, so that its age cannot be shown to be
 * within it; an answer with no channel, or no header, states no data age at all. Each failure names the channel and what the
 * answer says. Empty when every channel is within the maximum. Not a probe failure: a revision with old data still answers
 * as its release holds; `deploy.ps1` prints no promotion command while any of these stands.
 */
export function dataAgeFailures(envelope) {
  const freshness = envelope?.context?.freshness;
  if (freshness === null || typeof freshness !== "object" || !Array.isArray(freshness.channels)) {
    return ["the answer carries no freshness header, so the data's age is not known"];
  }
  if (freshness.channels.length === 0) return ["the answer names no publisher channel, so the data's age is not known"];
  const maximum = freshness.maximum_data_age_days;
  const failures = [];
  for (const channel of freshness.channels) {
    if (channel?.newest_completed_observation === null || channel?.newest_completed_observation === undefined) {
      failures.push(`${channel?.publisher}: the mount states no completed collection, so its data's age is not known to be within ${maximum} days`);
    } else if (channel.stale !== false) {
      failures.push(`${channel.publisher}: its newest completed collection ended at ${channel.newest_completed_observation}, ${channel.age_days} days before the answer, past the maximum data age of ${maximum} days`);
    }
  }
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
      // A title that begins with its act's type and date ("Loi du 3 décembre 2015 …") is read by the exact-reference phase
      // first (4.2), which opens that act and searches no word: that is the work named alone too. The title's own fold is
      // held by the three resolves above.
      const searched = await ask(origin, "search", { query: title, language, mode: "strict", limit: 1 });
      const resolution = searched?.result?.value?.work_resolution;
      const reference = searched?.result?.value?.reference;
      const opensWork = reference?.outcome === "opens" && reference.recognised?.act?.work === work;
      if (!opensWork && (resolution?.outcome !== "one_work" || resolution.work?.work_identifier !== found.work_identifier)) {
        // Every answer carries the phase's member; it answered the search only when it opened something or said why not
        // (on the ladder's answers its outcome is "no_reference", and the ladder's own outcome is the one to report).
        const phaseAnswered = reference?.outcome === "opens" || reference?.outcome === "not_opened";
        const said = phaseAnswered
          ? `the reference phase's ${reference.outcome} of ${reference.recognised?.act?.work ?? "no act"}`
          : resolution?.outcome ?? (searched === null ? "no envelope" : `a refusal, ${searched.refusal?.code}`);
        failures.push(`search for "${title}" names ${said}, not ${found.work_identifier} alone`);
      }
      notes.push(`an accented title: "${title}" (${found.work_identifier}), asked as published, in capitals and decomposed`);
      return failures;
    }
  }
  return [`none of the ${works.length} works the revision's search for "loi" lists names an accented title it resolves without its accents, so no accented title could be checked`];
}

/**
 * What a probe without `--browser` says beside its result: its HTTP probes held, and no page was walked in a browser, so the
 * journey's real-mount steps (each page held to its answer, its hydration, console, paint and citations) were not run.
 */
export const NOT_WALKED_NOTE =
  "the browser probes were not run (no --browser): no page of the revision was walked in a browser, so the journey's real-mount "
  + "steps were not held here; only the HTTP probes above were";

/** What the browser probes cannot observe on a remote revision, stated with every browser probe run. */
export const REMOTE_PRIVACY_NOTE =
  "privacy is not observed here: a deployed revision's process output and files are not this probe's to watch; only the release's "
  + "own image run observes it (nothing written after the first answer, no query text, address or user agent recorded), and only "
  + "when the release was probed: its report's privacy says so, and a release made with --no-probe was not";

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
export async function browserFailures(origin, releaseDirectory, { browser, runs = realMountRuns, held = heldLuxembourgWorkFromAnswers, searchEngines = KEPT_OUT }) {
  const work = await mkdtemp(join(tmpdir(), "lex-deploy-browser-"));
  try {
    await writeFile(join(work, "build-report.json"), await readFile(join(releaseDirectory, ASSETS.mountReport)));
    // The walk holds each page's robots elements and X-Robots-Tag to the deployment's own switch (`journey.mjs`, its
    // `expected.searchEngines`), as the HTTP probe holds robots.txt and the sitemap (the review of #993, its F1).
    const results = await runs(null, work, { servedByApi: true, keyboard: false, searchEngines, startServer: async () => remoteRevision(origin) }, browser, null,
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
export async function deployedReleaseFailures(origin, releaseDirectory, { publicKeyPem, identity = null, notes = [], promotion = [], opening = false, searchEngines = KEPT_OUT }) {
  const release = await releaseFailures(releaseDirectory, { publicKeyPem, identity });
  if (release.length > 0) return release.map((failure) => `the release does not read back: ${failure}`);
  const manifest = JSON.parse(await readFile(join(releaseDirectory, RELEASE_MANIFEST), "utf8"));
  return revisionFailures(origin, {
    web: liveFilesOf(await readLargeFile(join(releaseDirectory, ASSETS.image))),
    card: await readFile(join(releaseDirectory, ASSETS.card)),
    corpusSha256: manifest.corpus.sha256,
    notes,
    promotion,
    opening,
    searchEngines,
  });
}

/**
 * The pages a maintenance revision is asked for (R60), each with the interface language its maintenance page must be in:
 * both languages' homes, another page, and the root for a reader whose browser prefers English, whose answer depends on
 * that preference (`acceptLanguage`, and then `Vary: Accept-Language`).
 */
export const MAINTENANCE_PROBES = Object.freeze([
  Object.freeze({ path: "/fr/", language: "fr", acceptLanguage: null }),
  Object.freeze({ path: "/en/", language: "en", acceptLanguage: null }),
  Object.freeze({ path: "/en/search.html", language: "en", acceptLanguage: null }),
  Object.freeze({ path: "/", language: "en", acceptLanguage: "en" }),
]);

/** A `Retry-After` given in seconds, as the maintenance revision gives it. */
const RETRY_SECONDS = /^[1-9][0-9]*$/;

/**
 * What a maintenance revision must answer at `origin` (R60, R108, R115; `deploy.ps1 -Maintenance`), as failures (empty when
 * it holds); `web` is the release image's live page files, whose `maintenance/{language}.html` each page must be, byte for
 * byte. Its readiness answers 204, never cached (it holds a maintenance page in each interface language); each of
 * `MAINTENANCE_PROBES` answers 503 as HTML with Retry-After in seconds, the page's security headers (the CSP with
 * `frame-ancestors 'none'`, HSTS, `Referrer-Policy: no-referrer`, `nosniff`), `no-store`, `X-Robots-Tag: noindex`, no
 * cookie, and the maintenance page in its language; the API's coverage answers 503 `application/problem+json` with the code
 * `service_in_maintenance` and Retry-After, and no cookie. Every one is an HTTP request; none needs a browser.
 */
export async function maintenanceFailures(origin, { web }) {
  const files = new Map(web);
  const pages = new Map(INTERFACE_LANGUAGES.map((language) => [language, files.get(`${MAINTENANCE_DIRECTORY}/${language}.html`)]));
  const missing = [...pages].filter(([, page]) => page === undefined).map(([language]) => `${MAINTENANCE_DIRECTORY}/${language}.html`);
  if (missing.length > 0) {
    return missing.map((page) => `the release image holds no maintenance page ${page}, so the revision's pages cannot be held to it`);
  }

  const failures = [];
  const cookie = (said, answer) => {
    if (answer.headers.get("set-cookie") !== null) failures.push(`${said} sets a cookie, which the cookies page says no page does`);
  };
  const ready = await fetch(`${origin}/health/ready`, { redirect: "manual" });
  const readyBytes = Buffer.from(await ready.arrayBuffer());
  if (ready.status !== 204) {
    failures.push(`GET /health/ready answered ${ready.status} (${readyBytes.length} bytes), not 204: the revision does not hold a maintenance page in each language`);
  }
  if (ready.headers.get("cache-control") !== "no-store") failures.push(`GET /health/ready may be cached (${ready.headers.get("cache-control")})`);
  cookie("GET /health/ready", ready);

  const policy = `${cspValue()}; frame-ancestors 'none'`;
  for (const { path, language, acceptLanguage } of MAINTENANCE_PROBES) {
    const answer = await fetch(`${origin}${path}`, { redirect: "manual", headers: acceptLanguage === null ? {} : { "accept-language": acceptLanguage } });
    const bytes = Buffer.from(await answer.arrayBuffer());
    const said = `GET ${path}${acceptLanguage === null ? "" : ` (Accept-Language: ${acceptLanguage})`}`;
    const header = (name) => answer.headers.get(name);
    if (answer.status !== 503) failures.push(`${said} answered ${answer.status}, not 503`);
    if (!(header("content-type") ?? "").startsWith("text/html")) failures.push(`${said} answered ${header("content-type")}, not a page`);
    if (!bytes.equals(pages.get(language))) {
      failures.push(`${said} answered a body that is not the release image's own maintenance page (${MAINTENANCE_DIRECTORY}/${language}.html), byte for byte`);
    }
    if (!RETRY_SECONDS.test(header("retry-after") ?? "")) failures.push(`${said} carries Retry-After ${header("retry-after")}, not a number of seconds`);
    if (header("cache-control") !== "no-store") failures.push(`${said} may be cached (${header("cache-control")})`);
    if (header("x-robots-tag") !== "noindex") failures.push(`${said} carries X-Robots-Tag ${header("x-robots-tag")}, not noindex`);
    if (header("content-security-policy") !== policy) failures.push(`${said} carries the CSP ${header("content-security-policy")}, not ${policy}`);
    if (!/^max-age=\d+/.test(header("strict-transport-security") ?? "")) failures.push(`${said} carries no HSTS`);
    if (header("referrer-policy") !== "no-referrer") failures.push(`${said} carries Referrer-Policy ${header("referrer-policy")}, not no-referrer`);
    if (header("x-content-type-options") !== "nosniff") failures.push(`${said} carries no nosniff`);
    if (acceptLanguage !== null && header("vary") !== "Accept-Language") failures.push(`${said} varies by ${header("vary")}, not Accept-Language`);
    cookie(said, answer);
  }

  const coverage = await fetch(`${origin}/api/v3/coverage`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ operation_id: "coverage", parameters: {} }),
    redirect: "manual",
  });
  const type = coverage.headers.get("content-type") ?? "";
  let problem = null;
  try {
    problem = JSON.parse(await coverage.text());
  } catch {
    problem = null;
  }
  if (coverage.status !== 503 || !type.startsWith("application/problem+json") || problem?.code !== "service_in_maintenance") {
    failures.push(`POST /api/v3/coverage answered ${coverage.status} ${type}${typeof problem?.code === "string" ? ` (${problem.code})` : ""}, not 503 application/problem+json with the code service_in_maintenance: the revision is not in maintenance`);
  }
  if (!RETRY_SECONDS.test(coverage.headers.get("retry-after") ?? "")) failures.push(`POST /api/v3/coverage carries Retry-After ${coverage.headers.get("retry-after")}, not a number of seconds`);
  cookie("POST /api/v3/coverage", coverage);
  return failures;
}

/**
 * The probe of a maintenance revision deployed from the release at `releaseDirectory`: the release read back under the
 * signing identity's key (`publicKeyPem`), and as signed by the release signer `identity` when one is named, then the
 * revision held to the release image's maintenance pages (`maintenanceFailures`). Failures, empty when both hold.
 */
export async function maintenanceReleaseFailures(origin, releaseDirectory, { publicKeyPem, identity = null }) {
  const release = await releaseFailures(releaseDirectory, { publicKeyPem, identity });
  if (release.length > 0) return release.map((failure) => `the release does not read back: ${failure}`);
  return maintenanceFailures(origin, { web: liveFilesOf(await readLargeFile(join(releaseDirectory, ASSETS.image))) });
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
  const opening = argv.includes("--opening");
  // With --promotion-check, a revision that passes its probe while promotion refuses its data exits 3 (deploy.ps1).
  const promotionCheck = argv.includes("--promotion-check");
  // The release's maintenance revision (deploy.ps1 -Maintenance): its maintenance pages and nothing else, over HTTP only.
  const maintenance = argv.includes("--maintenance");
  // The deployment's search-engine switch (deploy.ps1 -SearchEngines), exactly as the API reads it; kept out unless said.
  const searchEngines = argv.includes("--search-engines") ? value("--search-engines") : KEPT_OUT;
  // The opening's own check (deploy.ps1 -Open): the site's served legal pages, and nothing else; it fails closed.
  if (opening && release === null && publicKey === null && origin !== null && /^https?:\/\//.test(origin) && !withBrowser && !maintenance) {
    const failures = await openingFailures(origin.replace(/\/+$/, ""));
    for (const failure of failures) console.error(`- ${failure}`);
    console.log(failures.length === 0
      ? `the legal pages at ${origin} are ready to publish: each of the ${LEGAL_PAGE_PATHS.length} answers, sets no cookie and shows no e-mail address, mail or telephone link, placeholder or postcode`
      : `${failures.length} reason(s) the site may not open`);
    process.exit(failures.length === 0 ? 0 : 1);
  }
  if (release === null || publicKey === null || (origin !== null && !/^https?:\/\//.test(origin)) || (argv.includes("--signer") && !identity)
    || (maintenance && (origin === null || withBrowser || opening || promotionCheck))
    || (searchEngines !== KEPT_OUT && searchEngines !== INDEXED)) {
    console.error("usage: node scripts/deploy-probe.mjs --release <release directory> --public-key <signing identity's public key, PEM> [--signer <the release signer's identity>] [--origin <the candidate revision's URL> [--browser] [--opening] [--promotion-check] [--search-engines kept-out|indexed]]");
    console.error("       node scripts/deploy-probe.mjs --maintenance --origin <the maintenance revision's URL> --release <release directory> --public-key <PEM> [--signer <identity>]: the release's maintenance revision, over HTTP only");
    console.error("       node scripts/deploy-probe.mjs --opening --origin <the site's URL>: the opening's check of the served legal pages alone");
    console.error("       with --promotion-check, a revision that answers as its release holds while promotion refuses its data's age exits 3");
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
  if (maintenance) {
    const failures = await maintenanceReleaseFailures(target, release, { publicKeyPem, identity });
    for (const failure of failures) console.error(`- ${failure}`);
    console.log(failures.length === 0
      ? `the maintenance revision at ${origin} answers as the release holds: ready, each page asked the release's maintenance page with 503 and Retry-After, and the API the problem service_in_maintenance, over HTTP only (no page walked in a browser)`
      : `${failures.length} probe failure(s)`);
    process.exit(failures.length === 0 ? 0 : 1);
  }
  const notes = [];
  const promotion = [];
  const failures = await deployedReleaseFailures(target, release, { publicKeyPem, identity, notes, promotion, opening, searchEngines });
  for (const note of notes) console.log(note);
  if (failures.length === 0 && withBrowser) {
    failures.push(...await browserFailures(target, release, { browser: await findBrowser(), searchEngines }));
    console.log(REMOTE_PRIVACY_NOTE);
  }
  if (!withBrowser) console.log(NOT_WALKED_NOTE);
  for (const failure of failures) console.error(`- ${failure}`);
  console.log(failures.length === 0
    ? `the revision at ${origin} answers as the release holds${withBrowser ? ", in the browser too" : ", over HTTP only (no page walked in a browser)"}`
    : `${failures.length} probe failure(s)`);
  // The data's age at promotion (R87, O11): said whatever the probe found, and with --promotion-check its exit says it.
  for (const refusal of promotion) console.log(`promotion refuses the data: ${refusal}`);
  if (promotion.length === 0) console.log("the data's age allows promotion: every publisher channel's newest completed collection is within the maximum data age");
  process.exit(failures.length > 0 ? 1 : promotionCheck && promotion.length > 0 ? 3 : 0);
}
