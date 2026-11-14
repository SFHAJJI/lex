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

createServer(async (request, response) => {
  const path = new URL(request.url, "http://preview").pathname;
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
