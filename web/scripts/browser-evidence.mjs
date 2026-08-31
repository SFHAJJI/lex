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

export async function findBrowser() {
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

export async function waitForDebugger(port, deadlineMs = 20000) {
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
export class Session {
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
  // WCAG 2.2 relative luminance and contrast ratio, computed in the page against the
  // effective background. The background is resolved by walking ancestors until a
  // non-transparent colour is found, because an element that sets only a text colour
  // inherits its contrast from whatever is painted behind it, and that is what a reader
  // actually sees.
  const channel = (v) => { const c = v / 255; return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
  const parse = (value) => (value.match(/[\\d.]+/g) || []).map(Number);
  const luminance = ([r, g, b]) => 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
  const opaqueBackground = (el) => {
    for (let node = el; node; node = node.parentElement) {
      const bg = parse(getComputedStyle(node).backgroundColor);
      if (bg.length >= 3 && (bg.length < 4 || bg[3] > 0)) return bg;
    }
    return [255, 255, 255];
  };
  const ratio = (a, b) => {
    const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
    return (hi + 0.05) / (lo + 0.05);
  };
  const contrast = [...document.querySelectorAll('h1,h2,h3,p,li,dt,dd,code,strong,summary,span')]
    .filter((el) => el.textContent.trim().length > 0 && el.offsetParent !== null)
    .map((el) => {
      const style = getComputedStyle(el);
      const size = parseFloat(style.fontSize);
      const bold = Number(style.fontWeight) >= 700;
      const large = size >= 24 || (size >= 18.66 && bold);
      return {
        tag: el.tagName.toLowerCase(),
        ratio: Math.round(ratio(parse(style.color), opaqueBackground(el)) * 100) / 100,
        required: large ? 3 : 4.5,
      };
    });
  const worst = contrast.reduce((a, b) => (a === null || b.ratio - b.required < a.ratio - a.required ? b : a), null);

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
    contrastChecked: contrast.length,
    worstContrast: worst ? worst.ratio : null,
    worstContrastTag: worst ? worst.tag : null,
    worstContrastRequired: worst ? worst.required : null,
    contrastFailures: contrast.filter((c) => c.ratio < c.required).length,
  };
})()`;

/**
 * Walk the page with real Tab presses and report what actually receives focus.
 *
 * Counting focusable elements is not a keyboard test: it says nothing about order,
 * nothing about whether focus is visible, and nothing about whether a control can be
 * reached at all. This presses Tab and records where focus lands.
 */
export async function keyboardWalk(session, sessionId, expected) {
  const seen = [];
  for (let step = 0; step < expected + 1; step++) {
    for (const type of ["rawKeyDown", "keyUp"]) {
      await session.send(
        "Input.dispatchKeyEvent",
        { type, key: "Tab", code: "Tab", windowsVirtualKeyCode: 9, nativeVirtualKeyCode: 9 },
        sessionId,
      );
    }
    const { result } = await session.send(
      "Runtime.evaluate",
      {
        expression: `(() => {
          const el = document.activeElement;
          if (!el || el === document.body) return null;
          const style = getComputedStyle(el);
          const ring = parseFloat(style.outlineWidth) > 0 && style.outlineStyle !== 'none';
          const shadow = style.boxShadow !== 'none' && style.boxShadow !== '';
          return {
            tag: el.tagName.toLowerCase(),
            text: (el.textContent || '').trim().slice(0, 30),
            focusVisible: ring || shadow,
          };
        })()`,
        returnByValue: true,
      },
      sessionId,
    );
    if (result.value === null) break;
    seen.push(result.value);
  }
  return seen;
}

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
       for (const scheme of ["light", "dark"]) {
        await session.send(
          "Emulation.setEmulatedMedia",
          { features: [{ name: "prefers-color-scheme", value: scheme }, { name: "prefers-reduced-motion", value: "reduce" }] },
          sessionId,
        );
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
        const row = { page, viewport: viewport.label, scheme, console: logged.length, ...observed };
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
        if (observed.contrastChecked === 0) {
          failures.push(`${page} @${viewport.label}/${scheme}: no text was contrast-checked`);
        }
        if (observed.focusableCount > 0) {
          const walk = await keyboardWalk(session, sessionId, observed.focusableCount);
          row.tabStops = walk.length;
          if (walk.length !== observed.focusableCount) {
            failures.push(
              `${page} @${viewport.label}/${scheme}: ${observed.focusableCount} focusable elements but ` +
                `${walk.length} reachable by Tab`,
            );
          }
          const invisible = walk.filter((stop) => !stop.focusVisible);
          if (invisible.length > 0) {
            failures.push(
              `${page} @${viewport.label}/${scheme}: ${invisible.length} focus stop(s) with no visible focus ` +
                `indicator: ${invisible.map((s) => s.tag).join(', ')}`,
            );
          }
        }
        if (observed.contrastFailures > 0) {
          failures.push(
            `${page} @${viewport.label}/${scheme}: ${observed.contrastFailures} element(s) below required contrast, ` +
              `worst ${observed.worstContrast} on <${observed.worstContrastTag}> needing ${observed.worstContrastRequired}`,
          );
        }
       }
      }
    }
    session.close();
  } finally {
    child.kill();
    await rm(profile, { force: true, recursive: true }).catch(() => {});
  }

  for (const row of rows) {
    console.log(
      `${row.page.replace('state-','').replace('.html','').padEnd(18)} ${row.viewport.padEnd(8)} ${row.scheme.padEnd(6)} ` +
        `console=${row.console} h1=${row.h1Count} overflow=${row.horizontalOverflow} scripts=${row.scriptCount} ` +
        `contrast=${row.contrastChecked} worst=${row.worstContrast}`,
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

// Only run when invoked directly, so the keyboard walk can be imported and proven by
// the self-test without launching the whole evidence run.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  await main();
}
