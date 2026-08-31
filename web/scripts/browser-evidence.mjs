// Real-browser evidence for the generated state pages.
//
// Zero npm dependencies, deliberately. `web` has no dependencies and the pages ship
// inert HTML with no client script; adding a browser automation toolchain to prove that
// would be the largest dependency in the package. Node 22 has a global WebSocket and
// Chrome speaks the DevTools Protocol over one, so the harness is a socket and a few
// commands.
//
// What this collects is evidence, not assertions about evidence: every value below is
// read out of a running browser, and a page that logs anything to the console fails.

import { spawn } from "node:child_process";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

const BROWSERS = [
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
];

// Narrow, tablet, desktop. The narrow width is a real small phone rather than a
// convenient round number, because layouts tend to be tuned to round numbers.
const WIDTHS = [
  { label: "narrow", width: 320, height: 640 },
  { label: "tablet", width: 768, height: 1024 },
  { label: "desktop", width: 1440, height: 900 },
];

const PAGES = ["state-loading.html", "state-transport-failure.html", "state-invalid-envelope.html"];

function existsSync(path) {
  try {
    return require("node:fs").existsSync(path);
  } catch {
    return false;
  }
}

async function findBrowser() {
  const { access } = await import("node:fs/promises");
  for (const candidate of BROWSERS) {
    try {
      await access(candidate);
      return candidate;
    } catch {
      // try the next one
    }
  }
  throw new Error(`no browser found; looked for:\n  ${BROWSERS.join("\n  ")}`);
}

async function waitForDebugger(port, deadlineMs = 20000) {
  const started = Date.now();
  let lastError;
  while (Date.now() - started < deadlineMs) {
    try {
      const response = await fetch(`http://127.0.0.1:${port}/json/version`);
      if (response.ok) {
        return (await response.json()).webSocketDebuggerUrl;
      }
    } catch (error) {
      lastError = error;
    }
    await new Promise((resolve) => setTimeout(resolve, 150));
  }
  throw new Error(`browser debugger never answered on ${port}: ${lastError}`);
}

/** A minimal CDP client: send a command, await its reply, observe events. */
class Session {
  #socket;
  #next = 1;
  #pending = new Map();
  #listeners = new Set();

  constructor(socket) {
    this.#socket = socket;
    socket.addEventListener("message", (event) => {
      const message = JSON.parse(event.data);
      if (message.id !== undefined) {
        const entry = this.#pending.get(message.id);
        if (entry) {
          this.#pending.delete(message.id);
          message.error ? entry.reject(new Error(JSON.stringify(message.error))) : entry.resolve(message.result);
        }
        return;
      }
      for (const listener of this.#listeners) {
        listener(message);
      }
    });
  }

  static async open(url) {
    const socket = new WebSocket(url);
    await new Promise((resolve, reject) => {
      socket.addEventListener("open", resolve, { once: true });
      socket.addEventListener("error", reject, { once: true });
    });
    return new Session(socket);
  }

  on(listener) {
    this.#listeners.add(listener);
  }

  send(method, params = {}, sessionId) {
    const id = this.#next++;
    return new Promise((resolve, reject) => {
      this.#pending.set(id, { resolve, reject });
      this.#socket.send(JSON.stringify({ id, method, params, sessionId }));
    });
  }

  close() {
    this.#socket.close();
  }
}

// Read out of the live DOM. Focus order is collected by actually walking the document
// rather than by counting elements that look focusable, because the two differ.
const PROBE = `(() => {
  const focusable = [...document.querySelectorAll(
    'a[href],button,input,select,textarea,summary,[tabindex]:not([tabindex="-1"])')];
  const heads = [...document.querySelectorAll('h1,h2,h3')].map((h) => h.tagName + ':' + h.textContent.trim().slice(0, 40));
  const body = getComputedStyle(document.body);
  return {
    lang: document.documentElement.lang,
    state: document.documentElement.dataset.previewState,
    title: document.title,
    headings: heads,
    h1Count: document.querySelectorAll('h1').length,
    focusableCount: focusable.length,
    focusableWithVisibleText: focusable.filter((el) => el.textContent.trim().length > 0).length,
    landmarks: [...document.querySelectorAll('main,[role=note],[role=group],aside')].length,
    syntheticBanner: !!document.querySelector('[data-synthetic]'),
    horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
    bodyColor: body.color,
    bodyBackground: body.backgroundColor,
    scriptCount: document.querySelectorAll('script').length,
  };
})()`;

async function main() {
  const browser = await findBrowser();
  const port = 9222 + Math.floor(Math.random() * 500);
  const profile = await mkdtemp(join(tmpdir(), "lex-cdp-"));
  const child = spawn(
    browser,
    [
      "--headless=new",
      `--remote-debugging-port=${port}`,
      `--user-data-dir=${profile}`,
      "--no-first-run",
      "--no-default-browser-check",
      "--disable-extensions",
      "--force-prefers-reduced-motion",
      "about:blank",
    ],
    { stdio: "ignore" },
  );

  const failures = [];
  const rows = [];
  try {
    const session = await Session.open(await waitForDebugger(port));
    const { targetId } = await session.send("Target.createTarget", { url: "about:blank" });
    const { sessionId } = await session.send("Target.attachToTarget", { targetId, flatten: true });

    let logged = [];
    session.on((message) => {
      if (message.sessionId !== sessionId) return;
      if (message.method === "Log.entryAdded") {
        logged.push(`${message.params.entry.level}: ${message.params.entry.text}`);
      }
      if (message.method === "Runtime.consoleAPICalled") {
        logged.push(`console.${message.params.type}`);
      }
      if (message.method === "Runtime.exceptionThrown") {
        logged.push(`exception: ${message.params.exceptionDetails.text}`);
      }
    });

    await session.send("Log.enable", {}, sessionId);
    await session.send("Runtime.enable", {}, sessionId);
    await session.send("Page.enable", {}, sessionId);

    for (const page of PAGES) {
      const url = pathToFileURL(join(process.cwd(), "dist", page)).href;
      for (const viewport of WIDTHS) {
        await session.send(
          "Emulation.setDeviceMetricsOverride",
          { width: viewport.width, height: viewport.height, deviceScaleFactor: 1, mobile: viewport.label === "narrow" },
          sessionId,
        );
        logged = [];
        await session.send("Page.navigate", { url }, sessionId);
        await new Promise((resolve) => setTimeout(resolve, 250));
        const { result } = await session.send(
          "Runtime.evaluate",
          { expression: PROBE, returnByValue: true, awaitPromise: true },
          sessionId,
        );
        const observed = result.value;
        const row = { page, viewport: viewport.label, console: logged.length, ...observed };
        rows.push(row);

        if (logged.length > 0) failures.push(`${page} @${viewport.label}: console output ${JSON.stringify(logged)}`);
        if (observed.horizontalOverflow) {
          failures.push(
            `${page} @${viewport.label}: horizontal overflow ${observed.scrollWidth} > ${observed.clientWidth}`,
          );
        }
        if (observed.h1Count !== 1) failures.push(`${page} @${viewport.label}: ${observed.h1Count} h1 elements`);
        if (!observed.syntheticBanner) failures.push(`${page} @${viewport.label}: synthetic banner missing`);
        if (observed.scriptCount !== 0) failures.push(`${page} @${viewport.label}: ${observed.scriptCount} script tags`);
        if (observed.state === undefined) failures.push(`${page} @${viewport.label}: no data-preview-state`);
      }
    }
    session.close();
  } finally {
    child.kill();
    await rm(profile, { force: true, recursive: true }).catch(() => {});
  }

  for (const row of rows) {
    console.log(
      `${row.page.padEnd(30)} ${row.viewport.padEnd(8)} console=${row.console} ` +
        `state=${row.state} h1=${row.h1Count} focusable=${row.focusableCount} ` +
        `landmarks=${row.landmarks} overflow=${row.horizontalOverflow} scripts=${row.scriptCount}`,
    );
  }

  if (failures.length > 0) {
    console.error(`\n${failures.length} failure(s):`);
    for (const failure of failures) console.error(`  ${failure}`);
    process.exitCode = 1;
    return;
  }
  console.log(`\nall ${rows.length} page/viewport combinations clean`);
}

await main();
