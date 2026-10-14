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

import { INTERFACE_LANGUAGES, pageForSitePath, redirectFor, sitePageFor } from "./permalinks.mjs";
import { createServer, request as httpRequest } from "node:http";
import { readFile, stat } from "node:fs/promises";
import { extname, join, relative as pathRelative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import vm from "node:vm";

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

async function serveStatic(response, root, pathname, method) {
  let relative;
  try {
    // An interface language's root is its home page; a law's address under a language is served the page that reads it
    // (`permalinks.mjs`).
    const home = INTERFACE_LANGUAGES.find((language) => pathname === `/${language}/`);
    relative = home !== undefined ? `${home}/index.html` : (pageForSitePath(pathname) ?? decodeURIComponent(pathname.slice(1)));
  } catch {
    problem(response, 404, "not_found");
    return;
  }
  const file = resolve(root, relative);
  // The server renderers are run here, never served: the first segment is compared without case, as Windows' and macOS's
  // file systems compare names (review of #959: /SSR/en.js was served).
  if ((file !== root && !file.startsWith(root + sep)) || pathRelative(root, file).split(sep)[0].toLowerCase() === "ssr") {
    problem(response, 404, "not_found");
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
    problem(response, 404, "not_found");
    return;
  }
  const bytes = await readFile(file);
  response.writeHead(200, { ...BASE_HEADERS, "content-type": type, "content-length": bytes.length });
  response.end(method === "HEAD" ? undefined : bytes);
}

/**
 * The server. `root` is the built live directory (`dist-live/`), `apiOrigin` the origin of a
 * running `Lex.V3.Api` (for example `http://127.0.0.1:5075`). It is returned unstarted.
 */
export function createLiveServer({ root, apiOrigin, apiDeadlineMs = API_DEADLINE_MS }) {
  const rootPath = resolve(root instanceof URL ? fileURLToPath(root) : root);
  const rendererFor = serverRenderers(rootPath);
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
      // The root, and a law's address with no interface language, are sent to the reader's language (R61), as the API
      // sends them (V3WebRoot).
      const location = redirectFor(url.pathname, url.search, request.headers["accept-language"]);
      if (location !== null) {
        response.writeHead(302, { ...BASE_HEADERS, location, vary: "Accept-Language", "content-length": 0 });
        response.end();
        return;
      }
      // A law's address under a language: the page rendered from the API's answer (R67), else the page as built.
      const page = sitePageFor(url.pathname);
      if (page !== null) {
        let rendered = null;
        try {
          rendered = await renderSitePage(page, url.pathname, apiOrigin, await rendererFor(page.language), apiDeadlineMs);
        } catch {
          rendered = null;
        }
        if (rendered !== null) {
          const bytes = Buffer.from(rendered.html, "utf8");
          response.writeHead(rendered.status, { ...BASE_HEADERS, "content-type": CONTENT_TYPES[".html"], "content-length": bytes.length });
          response.end(request.method === "HEAD" ? undefined : bytes);
          return;
        }
      }
      await serveStatic(response, rootPath, url.pathname, request.method);
    } catch {
      if (!response.headersSent) problem(response, 500, "server_error");
      else response.destroy();
    }
  });
}
