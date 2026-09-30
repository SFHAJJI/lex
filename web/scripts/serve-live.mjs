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

import { createServer, request as httpRequest } from "node:http";
import { readFile, stat } from "node:fs/promises";
import { extname, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

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

async function serveStatic(response, root, pathname, method) {
  let relative;
  try {
    relative = pathname === "/" ? "index.html" : decodeURIComponent(pathname.slice(1));
  } catch {
    problem(response, 404, "not_found");
    return;
  }
  const file = resolve(root, relative);
  if (file !== root && !file.startsWith(root + sep)) {
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
      await serveStatic(response, rootPath, url.pathname, request.method);
    } catch {
      if (!response.headersSent) problem(response, 500, "server_error");
      else response.destroy();
    }
  });
}
