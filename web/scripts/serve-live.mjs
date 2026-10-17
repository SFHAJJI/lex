// Serve the live page and the API on one origin.
//
// The page's CSP allows `connect-src 'self'` and the API sends no CORS headers, so a live page must
// come from the origin its requests go to. Until the owner rules on hosting (the API serving the
// bundle, or an ingress in front of both), this is the smallest server that makes that true for a
// journey on one machine: static files from `dist-live/`, and exactly one kind of request forwarded
// to a running `Lex.V3.Api`.
//
// What is forwarded is `POST /api/v3/{operation}` and nothing else: no query string, no case
// variant, no deeper path, no other method. The forwarded request carries the body and its media
// type and nothing about the caller: no cookie, no user agent, no referrer, no address. The API
// records no query text, IP or user agent (S4-A11), and a proxy that passed them along would be
// where that rule quietly stopped holding. A body over the API's own 1 MiB ceiling is refused here
// before it is read in full, and an API that cannot be reached is a transport problem the client
// names, never a page.
//
// A law's address under an interface language is rendered here as the API renders it (the specification of record's
// R67): the page asks the API, as itself would, through the same forwarder, and the built server renderer
// (`ssr/{language}.js`, `app/ssr-entry.jsx`) renders the page from that answer, in a context of its own with nothing
// of this process in it. When anything in that fails, the page is served as built and asks for itself.
//
// What it says to search engines is the release's switch (`search-engines.mjs`, R67), as the API says it
// (`V3WebRoot.SearchEngines.cs`): `/robots.txt` in the switch's state, the sitemap (`/sitemap.xml` and its parts) only with
// the site indexed, and `X-Robots-Tag: noindex` on the pages, files and 404 pages it serves, but a page the switch opened
// (whose kept-out marker it removed), and on a redirect while the site is kept out; a problem it answers in JSON (a method
// refused, a path forwarded to nothing) names none, as the API's own problems name none. The switch is
// `LEX_V3_SEARCH_ENGINES` when the server is made, unless the caller names one; the indexing record (R92's outcome) is a file
// the caller names, none by default. With the site indexed, the server asks its API once, when a page first needs it, the
// mounted index's build time and which of the record's works it holds (`searchEngineFacts`), and builds the sitemap's bytes
// then, once (review of #976, F4 and F5); an API it cannot reach is asked again on the next request, and meanwhile nothing
// is opened.

import { INTERFACE_LANGUAGES, movedFor, pageForSitePath, redirectFor, sitePageFor } from "./permalinks.mjs";
import { luxembourgDate } from "./temporal.mjs";
import {
  ROBOTS_CONTENT_TYPE, ROBOTS_PATH, SEARCH_ENGINES_VARIABLE, SITEMAP_CONTENT_TYPE, SITEMAP_PART, SITEMAP_PATH, INDEXED, KEPT_OUT, X_ROBOTS_TAG,
  readIndexingRecord, robotsTxt, searchEngineFacts, searchEnginesState, servedPage, sitemapFiles,
} from "./search-engines.mjs";
import { createServer, request as httpRequest } from "node:http";
import { readFileSync } from "node:fs";
import { readFile, stat } from "node:fs/promises";
import { extname, join, relative as pathRelative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import vm from "node:vm";
import { UNSERVED_DIRECTORIES, notFoundPageFor } from "./not-found.mjs";

const OPERATION_PATH = /^\/api\/v3\/[a-z_]+$/;
export const MAXIMUM_REQUEST_BYTES = 1024 * 1024;
/** How long an answer from the API may take before the page is told it timed out. */
export const API_DEADLINE_MS = 30_000;

const CONTENT_TYPES = Object.freeze({
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".svg": "image/svg+xml",
  ".woff2": "font/woff2",
  ".json": "application/json; charset=utf-8",
});

const BASE_HEADERS = Object.freeze({ "x-content-type-options": "nosniff", "cache-control": "no-store" });

/** robots.txt in each state of the switch, as bytes, built once. */
const ROBOTS_BYTES = Object.freeze({ [INDEXED]: Buffer.from(robotsTxt(INDEXED), "utf8"), [KEPT_OUT]: Buffer.from(robotsTxt(KEPT_OUT), "utf8") });

function problem(response, status, code) {
  const body = JSON.stringify({ schema: "lex-v3-live-server-problem/1", code });
  response.writeHead(status, { ...BASE_HEADERS, "content-type": "application/problem+json", "content-length": Buffer.byteLength(body) });
  response.end(body);
}

async function readBounded(request) {
  const declared = Number(request.headers["content-length"]);
  if (Number.isFinite(declared) && declared > MAXIMUM_REQUEST_BYTES) return null;
  const chunks = [];
  let total = 0;
  for await (const chunk of request) {
    total += chunk.length;
    if (total > MAXIMUM_REQUEST_BYTES) return null;
    chunks.push(chunk);
  }
  return Buffer.concat(chunks);
}

function forward(apiOrigin, path, body, deadlineMs) {
  const target = new URL(path, apiOrigin);
  return new Promise((resolveAnswer, reject) => {
    const outgoing = httpRequest(target, {
      method: "POST",
      // The body's media type and length, and nothing else about the request or its sender.
      headers: { "content-type": "application/json", "content-length": body.length },
      timeout: deadlineMs,
    }, (answer) => {
      const chunks = [];
      answer.on("data", (chunk) => chunks.push(chunk));
      answer.on("end", () => resolveAnswer({
        status: answer.statusCode,
        contentType: answer.headers["content-type"] ?? null,
        body: Buffer.concat(chunks),
      }));
      answer.on("error", reject);
    });
    outgoing.on("timeout", () => {
      const error = new Error("the API did not answer in time");
      error.code = "api_timeout";
      outgoing.destroy(error);
    });
    outgoing.on("error", reject);
    outgoing.end(body);
  });
}

/**
 * The built server renderer of each interface language (`ssr/{language}.js`), loaded once into a context of its own,
 * holding nothing but what the renderer needs; null for a language whose renderer is absent or does not load.
 */
function serverRenderers(rootPath) {
  const loaded = new Map();
  return (language) => {
    if (!loaded.has(language)) {
      loaded.set(language, (async () => {
        try {
          const context = vm.createContext({ TextEncoder });
          vm.runInContext(await readFile(join(rootPath, "ssr", `${language}.js`), "utf8"), context, { timeout: 10_000 });
          return context.lexSsr ?? null;
        } catch {
          return null;
        }
      })());
    }
    return loaded.get(language);
  };
}

/**
 * The page at a law's address under an interface language, rendered from the API's answer to the request the page itself
 * would send: `{ status, html }` (404 when the API does not know the work), or null when it cannot be rendered.
 */
async function renderSitePage(page, pathname, apiOrigin, renderer, deadlineMs) {
  if (renderer === null) return null;
  const answer = await forward(apiOrigin, `/api/v3/${page.operation}`, Buffer.from(JSON.stringify({ operation_id: page.operation, parameters: page.parameters })), deadlineMs);
  if (answer.status !== 200 || !(answer.contentType ?? "").startsWith("application/json")) return null;
  const envelopeJson = answer.body.toString("utf8");
  const html = renderer.render(page.kind, envelopeJson, pathname);
  if (typeof html !== "string" || !html.startsWith("<!doctype html>")) return null;
  let code = null;
  try {
    code = JSON.parse(envelopeJson)?.refusal?.code ?? null;
  } catch {
    return null;
  }
  return { status: code === "identifier_unknown" ? 404 : 200, html };
}

/**
 * The answer to a request no file and no page here claims: for a page request (a GET or HEAD whose path is not a
 * machine's, `notFoundPageFor`), the built 404 page in the reader's language, with status 404 (the specification of
 * record's R60), as the API answers it (`V3WebRoot.NotFound.cs`); for any other, or a build that holds no such page, the
 * problem `not_found`. The language is the path's (`/fr/…`, `/en/…`), else the one the root's redirect chooses, and then
 * the answer says it depends on Accept-Language.
 */
async function notFound(response, root, pathname, method, acceptLanguage) {
  const chosen = notFoundPageFor(method, pathname, acceptLanguage);
  // No 404 page is indexed, whatever the switch: its page names no marker a server removes.
  let bytes = null;
  try {
    bytes = chosen === null ? null : await readFile(join(root, chosen.page));
  } catch {
    bytes = null;
  }
  if (bytes === null) {
    problem(response, 404, "not_found");
    return;
  }
  response.writeHead(404, {
    ...BASE_HEADERS,
    "x-robots-tag": X_ROBOTS_TAG,
    "content-type": CONTENT_TYPES[".html"],
    "content-length": bytes.length,
    ...(chosen.negotiated ? { vary: "Accept-Language" } : {}),
  });
  response.end(method === "HEAD" ? undefined : bytes);
}

/**
 * A page's bytes as served in the switch's state (`servedPage`), and the headers it adds: `X-Robots-Tag` unless the page was
 * opened. Any other file is served as it is, with the header. `searchEngines` is the server's switch and, with the site
 * indexed, `facts()`, which answers the record of the works the API holds (`searchEngineFacts`).
 */
async function forSearchEngines(bytes, type, pathname, searchEngines) {
  // Kept out, nothing is opened, so a page is never read as text here.
  if (type !== CONTENT_TYPES[".html"] || searchEngines.state !== INDEXED) return { bytes, headers: { "x-robots-tag": X_ROBOTS_TAG } };
  const { record, reached } = await searchEngines.facts();
  // An API the server could not reach opens nothing: no page is told to search engines it did not ask about.
  if (!reached) return { bytes, headers: { "x-robots-tag": X_ROBOTS_TAG } };
  const served = servedPage(bytes.toString("utf8"), { state: searchEngines.state, pathname, record });
  return served.opened ? { bytes: Buffer.from(served.html, "utf8"), headers: {} } : { bytes, headers: { "x-robots-tag": X_ROBOTS_TAG } };
}

/**
 * With the site indexed, what the API says the search engines are told, asked once (`searchEngineFacts`): the record of the
 * works it holds, the mounted index's build time, and the sitemap's files as bytes, built then. Kept for the server's life
 * once the API answered; while it cannot be reached, nothing is opened and it is asked again on the next request.
 */
function searchEngineFactsOf(record, apiOrigin, deadlineMs) {
  let kept = null;
  return async () => {
    if (kept !== null) return kept;
    let reached = true;
    const ask = async (operation, parameters) => {
      try {
        const answer = await forward(apiOrigin, `/api/v3/${operation}`, Buffer.from(JSON.stringify({ operation_id: operation, parameters })), deadlineMs);
        if (answer.status !== 200 || !(answer.contentType ?? "").startsWith("application/json")) return null;
        return JSON.parse(answer.body.toString("utf8"));
      } catch {
        reached = false;
        return null;
      }
    };
    const facts = await searchEngineFacts(record, ask);
    if (!reached) return Object.freeze({ record: null, lastmod: null, sitemap: null, reached: false });
    const sitemap = new Map(Object.entries(sitemapFiles(facts.record, { lastmod: facts.lastmod })).map(([path, text]) => [path, Buffer.from(text, "utf8")]));
    kept = Object.freeze({ ...facts, sitemap, reached: true });
    return kept;
  };
}

/**
 * A file's path as the build names one and the API serves it (`V3WebRoot`'s `SafePath`): lower-case letters, digits and
 * hyphens, a segment at a time, and an extension. Anything else (a final slash, a percent-escape, a capital) is no file of
 * the build, as the API answers it (review of #976).
 */
const FILE_PATH = /^\/[a-z0-9-]+(?:\/[a-z0-9-]+)*\.[a-z0-9]+$/;

async function serveStatic(response, root, pathname, method, acceptLanguage, searchEngines) {
  let relative;
  try {
    // An interface language's root is its home page; a law's address under a language is served the page that reads it
    // (`permalinks.mjs`); any other path names a file only in the build's own form.
    const home = INTERFACE_LANGUAGES.find((language) => pathname === `/${language}/`);
    const page = home !== undefined ? `${home}/index.html` : pageForSitePath(pathname);
    if (page === null && !FILE_PATH.test(pathname)) throw new Error("no file of the build");
    relative = page ?? pathname.slice(1);
  } catch {
    await notFound(response, root, pathname, method, acceptLanguage);
    return;
  }
  const file = resolve(root, relative);
  // The server renderers are run here, never served, and the 404 and maintenance pages are never served at their own
  // address (`UNSERVED_DIRECTORIES`): the first segment is compared without case, as Windows' and macOS's file systems
  // compare names (review of #959: /SSR/en.js was served).
  if ((file !== root && !file.startsWith(root + sep)) || UNSERVED_DIRECTORIES.includes(pathRelative(root, file).split(sep)[0].toLowerCase())) {
    await notFound(response, root, pathname, method, acceptLanguage);
    return;
  }
  const type = CONTENT_TYPES[extname(file)];
  let info;
  try {
    info = await stat(file);
  } catch {
    info = null;
  }
  if (!type || !info?.isFile()) {
    await notFound(response, root, pathname, method, acceptLanguage);
    return;
  }
  const served = await forSearchEngines(await readFile(file), type, pathname, searchEngines);
  response.writeHead(200, { ...BASE_HEADERS, ...served.headers, "content-type": type, "content-length": served.bytes.length });
  response.end(method === "HEAD" ? undefined : served.bytes);
}

/** The indexing record a caller names (a path or file URL), read once, or null when none is named or it is none. */
function indexingRecordAt(location) {
  if (location === null || location === undefined) return null;
  try {
    return readIndexingRecord(readFileSync(location instanceof URL ? location : resolve(String(location)), "utf8"));
  } catch {
    return null;
  }
}

/** Today in Luxembourg (R13) at the instant `now` gives, or null outside the range the rule is held for. */
function luxembourgToday(now) {
  try {
    return luxembourgDate(now());
  } catch {
    return null;
  }
}

/**
 * The server. `root` is the built live directory (`dist-live/`), `apiOrigin` the origin of a
 * running `Lex.V3.Api` (for example `http://127.0.0.1:5075`), and `now` its clock (a UTC instant), which says the date a
 * readable address's page is rendered at (R67: the current version, today in Luxembourg). `searchEngines` is the release's
 * switch (`indexed`, or anything else for kept out; `LEX_V3_SEARCH_ENGINES` by default) and `indexingRecord` the file of
 * the release's indexing record, R92's outcome (`readIndexingRecord`; none by default, so no law's page is opened), whose works
 * the API's `dossier` must answer to be opened or listed. It is returned unstarted.
 */
export function createLiveServer({
  root,
  apiOrigin,
  apiDeadlineMs = API_DEADLINE_MS,
  now = () => new Date().toISOString(),
  searchEngines = process.env[SEARCH_ENGINES_VARIABLE],
  indexingRecord = null,
}) {
  const rootPath = resolve(root instanceof URL ? fileURLToPath(root) : root);
  const rendererFor = serverRenderers(rootPath);
  const state = searchEnginesState(searchEngines);
  const engines = Object.freeze({ state, facts: state === INDEXED ? searchEngineFactsOf(indexingRecordAt(indexingRecord), apiOrigin, apiDeadlineMs) : null });
  // A redirect or a move with the site indexed carries no X-Robots-Tag: it is no page, and the page it leads to says what it
  // says (an x-default alternate is the root's or a law's redirect). Kept out, it keeps the header, as the API answers it.
  const redirectRobots = engines.state === INDEXED ? {} : { "x-robots-tag": X_ROBOTS_TAG };
  return createServer(async (request, response) => {
    try {
      let url;
      try {
        url = new URL(request.url, "http://live.invalid");
      } catch {
        problem(response, 404, "not_found");
        return;
      }
      if (request.url.startsWith("/api/") || request.url.startsWith("/mcp")) {
        if (request.method !== "POST" || url.search !== "" || !OPERATION_PATH.test(request.url)) {
          problem(response, 404, "not_forwarded");
          return;
        }
        const body = await readBounded(request);
        if (body === null) {
          problem(response, 413, "request_too_large");
          return;
        }
        let answer;
        try {
          answer = await forward(apiOrigin, request.url, body, apiDeadlineMs);
        } catch (error) {
          if (error?.code === "api_timeout") problem(response, 504, "api_timeout");
          else problem(response, 502, "api_unreachable");
          return;
        }
        const headers = { ...BASE_HEADERS, "content-length": answer.body.length };
        if (answer.contentType) headers["content-type"] = answer.contentType;
        response.writeHead(answer.status, headers);
        response.end(answer.body);
        return;
      }
      if (request.method !== "GET" && request.method !== "HEAD") {
        problem(response, 405, "method_not_allowed");
        return;
      }
      // A target that begins with two slashes is no path of this site (a URL parser reads it as another host's), as the API
      // answers it: its 404 page (review of #976).
      if (request.url.startsWith("//")) {
        await notFound(response, rootPath, request.url.split("?")[0], request.method, request.headers["accept-language"]);
        return;
      }
      // robots.txt in the switch's state, and the sitemap's index and parts only with the site indexed (R67); kept out, or
      // a part the sitemap has not, is an address this site does not hold.
      const sitemap = engines.state === INDEXED && (url.pathname === SITEMAP_PATH || SITEMAP_PART.test(url.pathname))
        ? (await engines.facts()).sitemap?.get(url.pathname) ?? null
        : null;
      if (url.pathname === ROBOTS_PATH || sitemap !== null) {
        const bytes = sitemap ?? ROBOTS_BYTES[engines.state];
        response.writeHead(200, {
          ...BASE_HEADERS,
          "x-robots-tag": X_ROBOTS_TAG,
          "content-type": sitemap === null ? ROBOTS_CONTENT_TYPE : SITEMAP_CONTENT_TYPE,
          "content-length": bytes.length,
        });
        response.end(request.method === "HEAD" ? undefined : bytes);
        return;
      }
      // An EU permalink of the shape before R64 has moved for good to R64's, under the interface language it had, the same
      // answer for every reader, as the API moves it (V3WebRoot.MovedFor).
      const moved = movedFor(url.pathname, url.search);
      if (moved !== null) {
        response.writeHead(301, { ...BASE_HEADERS, ...redirectRobots, location: moved, "content-length": 0 });
        response.end();
        return;
      }
      // The root, and a law's address with no interface language, are sent to the reader's language (R61), as the API
      // sends them (V3WebRoot).
      const location = redirectFor(url.pathname, url.search, request.headers["accept-language"]);
      if (location !== null) {
        response.writeHead(302, { ...BASE_HEADERS, ...redirectRobots, location, vary: "Accept-Language", "content-length": 0 });
        response.end();
        return;
      }
      // A law's address under a language: the page rendered from the API's answer (R67), else the page as built. A
      // readable address asks for today in Luxembourg (R13), as the API asks it (V3WebRoot.SitePageFor), and a reading the
      // page its query names (the plan's item 3.7).
      const page = sitePageFor(url.pathname, { today: luxembourgToday(now), search: url.search });
      if (page !== null) {
        let rendered = null;
        try {
          rendered = await renderSitePage(page, url.pathname, apiOrigin, await rendererFor(page.language), apiDeadlineMs);
        } catch {
          rendered = null;
        }
        if (rendered !== null) {
          const served = await forSearchEngines(Buffer.from(rendered.html, "utf8"), CONTENT_TYPES[".html"], url.pathname, engines);
          response.writeHead(rendered.status, { ...BASE_HEADERS, ...served.headers, "content-type": CONTENT_TYPES[".html"], "content-length": served.bytes.length });
          response.end(request.method === "HEAD" ? undefined : served.bytes);
          return;
        }
      }
      await serveStatic(response, rootPath, url.pathname, request.method, request.headers["accept-language"], engines);
    } catch {
      if (!response.headersSent) problem(response, 500, "server_error");
      else response.destroy();
    }
  });
}
