// A preview of a branch's site for the driver's design review (2026-11-14), on GitHub's runner only: the live pages as built
// from this branch (`dist-live/`), served by the Node live server (`serve-live.mjs`), whose API is a public origin reached
// through a small local relay (the live server forwards over plain HTTP; the public API is HTTPS); and the pages only the API
// renders from its coverage answer (the home page, the assistant's), rendered here by the built server renderer as the API
// renders them (`ssr-entry.jsx`). Nothing is recorded.
// Usage: node web/scripts/preview-server.mjs <port> <api origin>
import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import vm from "node:vm";
import { createLiveServer } from "./serve-live.mjs";

const [port = "8080", apiOrigin = "https://law.soufien.lu"] = process.argv.slice(2);
const relayPort = Number(port) + 1;
const root = new URL("../dist-live/", import.meta.url);

// The relay: POST /api/v3/{operation} to the public API, its answer back as it came.
createServer(async (request, response) => {
  const chunks = [];
  for await (const chunk of request) chunks.push(chunk);
  try {
    const answer = await fetch(`${apiOrigin}${request.url}`, { method: request.method, headers: { "content-type": request.headers["content-type"] ?? "application/json" }, body: request.method === "POST" ? Buffer.concat(chunks) : undefined });
    response.writeHead(answer.status, { "content-type": answer.headers.get("content-type") ?? "application/json" });
    response.end(Buffer.from(await answer.arrayBuffer()));
  } catch (error) {
    response.writeHead(502, { "content-type": "text/plain" });
    response.end(String(error));
  }
}).listen(relayPort, "127.0.0.1");

const live = createLiveServer({ root, apiOrigin: `http://127.0.0.1:${relayPort}`, apiDeadlineMs: 60_000 });
const handle = live.listeners("request")[0];

/** A page the API renders from its coverage answer (the home page, the assistant's), rendered as the API renders it. */
async function coveragePage(kind, language) {
  const response = await fetch(`${apiOrigin}/api/v3/coverage`, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ operation_id: "coverage", parameters: {} }) });
  const envelope = await response.text();
  const context = vm.createContext({ TextEncoder });
  vm.runInContext(await readFile(fileURLToPath(new URL(`ssr/${language}.js`, root)), "utf8"), context, { timeout: 10_000 });
  return context.lexSsr.render(kind, envelope, `/${language}/${kind === "home" ? "" : kind}`);
}

// The pages builder's preview (21/11/2026): the new engine's addresses (`/api/next/*`), which the Node live server does not
// forward, answered here: from the fixtures the pages are checked against when one matches (an answer in the engine's
// contract for a question it was asked, a replay token, the server's configuration, the coverage file, the results in the
// new schema, a text's table of contents, an article's context), otherwise from the API origin as it answers.
const FIXTURES = new URL("../test/next-fixtures/", import.meta.url);
const readJson = async (name) => JSON.parse(await readFile(new URL(name, FIXTURES), "utf8"));
async function nextFixture(method, url, body) {
  const { pathname, searchParams } = new URL(url, "http://preview");
  const tryRead = async (name) => { try { return await readJson(name); } catch { return null; } };
  // dossier-page (22/11/2026): with LEX_PREVIEW_LIVE_GETS=1 every GET of the engine's read-only API is the live site's.
  if (method === "GET" && process.env.LEX_PREVIEW_LIVE_GETS === "1") return null;
  // repair-pages (22/11/2026): the answers this tree's server gives (test/next-fixtures/preview/, saved from it locally), and the
  // results in lex-results/2 as the evaluation will write them (results-v2.json), so the pages are photographed as they will read.
  if (pathname === "/api/next/config") return readJson("preview/config.json");
  if (pathname === "/api/next/coverage") return readJson("preview/coverage.json");
  if (pathname === "/api/next/results") return readJson("results-v2.json");
  if (pathname === "/api/next/dictionary") return readJson("preview/dictionary.json");
  if (pathname === "/api/next/versions") return readJson("preview/versions.json");
  if (pathname === "/api/next/sources") {
    const saved = (await tryRead("preview/sources.json")) ?? {};
    return Object.fromEntries(searchParams.getAll("version").map((id) => [id, saved[id] ?? (id === "17295" ? { identity_from: "publisher-pdf", ocr: false, pdf: true, language: "fra" } : null)]).filter(([, value]) => value !== null));
  }
  if (pathname === "/api/next/shapes") return readJson("preview/shapes.json");
  if (pathname === "/api/next/text") return tryRead(`preview/text-${searchParams.get("work")}.json`).then((value) => value ?? tryRead({ "code-securite-sociale": "text-css.json", "32009L0138": "text-solvency.json" }[searchParams.get("work")] ?? "none.json"));
  if (pathname === "/api/next/text-outline") return tryRead(`preview/outline-${searchParams.get("work")}.json`).then((value) => value ?? tryRead(`contexts/outline-${searchParams.get("work")}.json`));
  if (pathname === "/api/next/context") return tryRead(`contexts/${Number(searchParams.get("article_pk"))}.json`);
  if (method === "POST" && (pathname === "/api/next/ask" || pathname === "/api/next/replay")) {
    let asked = {};
    try { asked = JSON.parse(body.toString("utf8") || "{}"); } catch { asked = {}; }
    const { readdir } = await import("node:fs/promises");
    for (const folder of ["funnel-real/", "contract/"]) {
      for (const name of (await readdir(new URL(folder, FIXTURES))).filter((file) => file.endsWith(".json") && !file.startsWith("context-"))) {
        const answer = await readJson(folder + name);
        if (pathname.endsWith("/ask") && answer.question === asked.question) return answer;
        if (pathname.endsWith("/replay") && answer.meta?.replay === asked.token) return answer;
      }
    }
  }
  return null;
}

createServer(async (request, response) => {
  const path = new URL(request.url, "http://preview").pathname;
  if (path.startsWith("/api/next/")) {
    const chunks = [];
    for await (const chunk of request) chunks.push(chunk);
    const body = Buffer.concat(chunks);
    try {
      const fixture = await nextFixture(request.method, request.url, body);
      if (fixture !== null) {
        response.writeHead(200, { "content-type": "application/json; charset=utf-8" });
        response.end(JSON.stringify(fixture));
        return;
      }
      const answer = await fetch(`${apiOrigin}${request.url}`, { method: request.method, headers: { "content-type": request.headers["content-type"] ?? "application/json" }, body: request.method === "POST" ? body : undefined });
      response.writeHead(answer.status, { "content-type": answer.headers.get("content-type") ?? "application/json" });
      response.end(Buffer.from(await answer.arrayBuffer()));
    } catch (error) {
      response.writeHead(502, { "content-type": "text/plain" });
      response.end(String(error));
    }
    return;
  }
  // The pages as the new engine's server serves them (`app/site_routes.py`): the built files, `/fr/` its index, `/fr/x` the
  // page `x.html`, a law's address its reading page; every other request (the reading API, POST /api/v3/*) as before.
  if ((request.method === "GET" || request.method === "HEAD") && !path.startsWith("/api/")) {
    const { stat } = await import("node:fs/promises");
    const types = { html: "text/html; charset=utf-8", js: "text/javascript; charset=utf-8", css: "text/css; charset=utf-8", json: "application/json; charset=utf-8", svg: "image/svg+xml", woff2: "font/woff2", woff: "font/woff", png: "image/png" };
    const parts = path.replace(/^\/+|\/+$/gu, "").split("/").filter(Boolean);
    let file = null;
    if (parts.length === 0) {
      response.writeHead(302, { location: "/fr/" });
      response.end();
      return;
    }
    if (parts.length === 1 && (parts[0] === "fr" || parts[0] === "en")) file = `${parts[0]}/index.html`;
    else if ((parts[0] === "fr" || parts[0] === "en") && (parts[1] === "lu-legilux" || parts[1] === "eu-eurlex")) file = `${parts[0]}/${parts.length === 3 ? "dossier" : "reading"}.html`;
    else if (parts.length === 2 && (parts[0] === "fr" || parts[0] === "en") && !parts[1].includes(".")) file = `${parts[0]}/${parts[1]}.html`;
    else file = parts.join("/");
    try {
      const at = new URL(file, root);
      if ((await stat(at)).isFile()) {
        const extension = file.split(".").at(-1);
        response.writeHead(200, { "content-type": types[extension] ?? "application/octet-stream" });
        response.end(request.method === "HEAD" ? undefined : await readFile(at));
        return;
      }
    } catch {
      // Not a built file: the live server answers (its 404 page).
    }
  }
  const assistant = /^\/(fr|en)\/assistant$/.exec(path);
  const home = /^\/(fr|en)\/(?:index\.html)?$/.exec(path);
  const match = assistant ?? home;
  if (request.method === "GET" && match !== null) {
    try {
      const html = await coveragePage(assistant !== null ? "assistant" : "home", match[1]);
      response.writeHead(200, { "content-type": "text/html; charset=utf-8" });
      response.end(html);
    } catch (error) {
      response.writeHead(500, { "content-type": "text/plain; charset=utf-8" });
      response.end(String(error));
    }
    return;
  }
  handle(request, response);
}).listen(Number(port), "127.0.0.1", () => console.log(`preview on http://127.0.0.1:${port}, API ${apiOrigin} through http://127.0.0.1:${relayPort}`));
