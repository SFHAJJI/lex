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
import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { resolve as resolvePath } from "node:path";
import { extname, join as joinPath } from "node:path";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

/**
 * Where a real Chromium lives, per platform.
 *
 * This list was Windows-only, so `findBrowser` threw on the Ubuntu runner and the required
 * `web` check failed after 63 tests passed. It looked for `C:/Program Files/...` on Linux,
 * which is the shape of assuming your own machine is the only one: it passed locally for
 * exactly as long as nobody ran it anywhere else.
 *
 * `LEX_BROWSER` wins when set, so a host with Chromium somewhere unusual is configurable
 * rather than unsupported.
 */
const BROWSERS_BY_PLATFORM = {
  win32: [
    "C:/Program Files/Google/Chrome/Application/chrome.exe",
    "C:/Program Files (x86)/Google/Chrome/Application/chrome.exe",
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  ],
  linux: [
    "/usr/bin/google-chrome-stable",
    "/usr/bin/google-chrome",
    "/usr/bin/chromium-browser",
    "/usr/bin/chromium",
    "/usr/bin/microsoft-edge-stable",
    "/snap/bin/chromium",
  ],
  darwin: [
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "/Applications/Chromium.app/Contents/MacOS/Chromium",
    "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
  ],
};

/** The candidates for a platform, most preferred first. Exported so a test can check them. */
export function browserCandidates(platform = process.platform) {
  const configured = process.env.LEX_BROWSER;
  const known = BROWSERS_BY_PLATFORM[platform] ?? [];
  return configured ? [configured, ...known] : known;
}


// Narrow, tablet, desktop. The narrow width is a real small phone rather than a
// convenient round number, because layouts tend to be tuned to round numbers.
const WIDTHS = [
  { label: "narrow", width: 320, height: 640 },
  { label: "tablet", width: 768, height: 1024 },
  { label: "desktop", width: 1440, height: 900 },
  // Browser zoom shrinks the CSS viewport rather than scaling pixels, so 200% zoom at a
  // 1440 window is a 720 CSS viewport and WCAG 1.4.10's 400% at 1280 is 320. Naming them
  // as zoom levels keeps the evidence honest about what was actually exercised.
  { label: "zoom200", width: 720, height: 450 },
  { label: "zoom400", width: 320, height: 256 },
];

const PAGES = [
  "state-loading.html",
  "state-transport-failure.html",
  "state-invalid-envelope.html",
  "state-success.html",
  "state-refusal.html",
  // The trust surface. Added because the run reported "all combinations clean" while this
  // page existed and was not in the list, which is evidence about five pages presented as
  // evidence about six.
  "trust-surface.html",
];

/**
 * Ports WHATWG Fetch refuses to connect to, so a debugger listening on one is unreachable.
 *
 * The two harnesses drew from `9222 + random*500` and `9800 + random*300`, and the second range
 * contains **10080**, which is on this list. Chrome launched fine and `fetch` was then forbidden
 * from asking it anything, so the run waited twenty seconds and reported that the debugger never
 * answered. Identical trees went green or red depending on a dice roll, which is the worst kind
 * of failure: the evidence looked flaky and the cause was deterministic.
 *
 * Only the entries that can fall inside a debugger range are listed; the full WHATWG set is
 * mostly low ports no allocator here would reach.
 * https://fetch.spec.whatwg.org/#port-blocking
 */
export const FETCH_BLOCKED_PORTS = Object.freeze(new Set([
  6000, 6566, 6665, 6666, 6667, 6668, 6669, 6697, 10080,
]));

/**
 * A debugger port drawn from a range, with blocked ports excluded by construction.
 *
 * Rejecting after the draw would leave the bug reachable through an unlucky retry, so the range
 * is filtered first and the draw is over what remains.
 */
export function allocateDebuggerPort(start, count, random = Math.random) {
  const usable = [];
  for (let port = start; port < start + count; port += 1) {
    if (!FETCH_BLOCKED_PORTS.has(port)) {
      usable.push(port);
    }
  }

  if (usable.length === 0) {
    throw new Error(`every port in ${start}..${start + count - 1} is blocked by Fetch`);
  }

  return usable[Math.min(usable.length - 1, Math.floor(random() * usable.length))];
}

export async function findBrowser(platform = process.platform) {
  const { access } = await import("node:fs/promises");
  const candidates = browserCandidates(platform);
  if (candidates.length === 0) {
    throw new Error(
      `no browser candidates are declared for platform ${platform}; ` +
        "set LEX_BROWSER or add the platform to BROWSERS_BY_PLATFORM",
    );
  }

  for (const candidate of candidates) {
    try {
      await access(candidate);
      return candidate;
    } catch {
      // try the next one
    }
  }

  throw new Error(
    `no browser found on ${platform}; looked for:\n  ${candidates.join("\n  ")}`,
  );
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
  const contrast = [...document.querySelectorAll('h1,h2,h3,h4,h5,h6,p,li,dt,dd,code,strong,summary,span,a')]
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

  // Readable separation. Contrast and overflow both pass on a page whose labels, values
  // and codes are painted flush against each other, and that is exactly what happened: the
  // new component classes had no layout rules, so Chrome rendered the token label and the
  // text it qualifies as one word. For every pair of adjacent inline element siblings,
  // either the markup has whitespace between them or the boxes do.
  const glued = [];
  for (const parent of document.querySelectorAll('body *')) {
    const children = [...parent.children].filter(
      (el) => el.offsetParent !== null && el.textContent.trim().length > 0,
    );
    for (let i = 0; i + 1 < children.length; i += 1) {
      const before = children[i];
      const after = children[i + 1];
      let between = '';
      for (let node = before.nextSibling; node && node !== after; node = node.nextSibling) {
        if (node.nodeType === 3) between += node.nodeValue;
      }
      if (between.length > 0 && between.trim().length !== between.length) continue;
      if (between.trim().length > 0) continue;
      const a = before.getBoundingClientRect();
      const b = after.getBoundingClientRect();
      const separated = b.left - a.right >= 2 || b.top >= a.bottom;
      if (!separated) {
        glued.push(
          parent.className + ' > ' + before.className + ' | ' + after.className,
        );
      }
    }
  }

  const focusable = [...document.querySelectorAll(
    'a[href],button,input,select,textarea,summary,[tabindex]:not([tabindex="-1"])')];
  const headingEls = [...document.querySelectorAll('h1,h2,h3,h4,h5,h6')];
  const heads = headingEls.map((h) => h.tagName + ':' + h.textContent.trim().slice(0, 40));
  const headingLevels = headingEls.map((h) => Number(h.tagName.slice(1)));
  const body = getComputedStyle(document.body);
  return {
    lang: document.documentElement.lang,
    state: document.documentElement.dataset.previewState,
    title: document.title,
    headings: heads,
    headingLevels,
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
    glued: glued.slice(0, 8),
    gluedCount: glued.length,
    // A control with no handler and no form is a promise the page cannot keep. This line
    // ships no script, so any button is inert by construction.
    inertControls: [...document.querySelectorAll('button, a:not([href])')].length,
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
  // Start from a known place. Focus survives a navigation in a reused target, so without
  // this the first Tab can land mid-document and the walk measures the wrong sequence.
  await session.send(
    "Runtime.evaluate",
    { expression: "document.activeElement && document.activeElement.blur(); window.scrollTo(0, 0);" },
    sessionId,
  );

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
          const path = [];
          for (let node = el; node && node.parentElement; node = node.parentElement) {
            path.push(node.tagName + ':' + [...node.parentElement.children].indexOf(node));
          }
          return {
            path: path.join('/'),
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
    // Tab cycles: past the last control the browser returns to the first. Without this
    // the walk counts the wrap as an extra stop, which reads as a phantom focusable
    // element and fails a page that is actually correct.
    if (seen.some((stop) => stop.path === result.value.path)) break;
    seen.push(result.value);
  }
  return seen;
}


const CONTENT_TYPES = new Map([
  [".html", "text/html; charset=utf-8"],
  [".css", "text/css; charset=utf-8"],
  [".svg", "image/svg+xml"],
]);

/**
 * Serve dist/ over HTTP.
 *
 * The harness used file:// and reported console=0 while the pages were missing an icon
 * link. Over file:// a browser makes no favicon request at all, so the 404 that a real
 * visitor sees could not appear. The acceptance criteria ask for the actual network
 * shape, and file:// is not it: no status codes, no default document requests, no
 * content types. This is 30 lines and removes a whole class of thing the harness could
 * not see.
 */
async function serveDist(root) {
  const server = createServer((request, response) => {
    const path = decodeURIComponent(new URL(request.url, "http://127.0.0.1").pathname);
    const file = joinPath(root, path === "/" ? "/index.html" : path);
    readFile(file).then(
      (body) => {
        response.writeHead(200, {
          "content-type": CONTENT_TYPES.get(extname(file)) ?? "application/octet-stream",
        });
        response.end(body);
      },
      () => {
        response.writeHead(404, { "content-type": "text/plain; charset=utf-8" });
        response.end("not found");
      },
    );
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const { port } = server.address();
  return {
    origin: `http://127.0.0.1:${port}`,
    close: () => new Promise((resolve) => server.close(resolve)),
  };
}

/** Kill a process and everything it spawned, then wait for the handles to drop. */
async function killTree(pid) {
  if (!pid) return;
  if (process.platform === "win32") {
    await new Promise((resolve) => {
      const killer = spawn("taskkill", ["/PID", String(pid), "/T", "/F"], { stdio: "ignore" });
      killer.on("exit", resolve);
      killer.on("error", resolve);
    });
  } else {
    try {
      process.kill(-pid, "SIGKILL");
    } catch {
      try {
        process.kill(pid, "SIGKILL");
      } catch {
        // already gone
      }
    }
  }
  // The handles are released asynchronously, so a build immediately afterwards can still
  // meet a locked directory without this.
  await new Promise((resolve) => setTimeout(resolve, 400));
}

async function main() {
  const browser = await findBrowser();
  const port = allocateDebuggerPort(9222, 500);
  const profile = await mkdtemp(join(tmpdir(), "lex-cdp-"));
  // An induced mutation serves a deliberately broken copy so the gates can be shown red.
  // A gate nobody has watched fail is a gate nobody should trust: the heading-order and
  // link-contrast checks below both replace checks that were green on a page that
  // violated them.
  const root = process.env.LEX_EVIDENCE_ROOT
    ? resolvePath(process.env.LEX_EVIDENCE_ROOT)
    : join(process.cwd(), "dist");
  const site = await serveDist(root);
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

    await session.send("Accessibility.enable", {}, sessionId);
    await session.send("Log.enable", {}, sessionId);
    await session.send("Runtime.enable", {}, sessionId);
    await session.send("Page.enable", {}, sessionId);

    for (const page of PAGES) {
      const url = `${site.origin}/${page}`;
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

        // The accessibility tree is what a screen reader actually receives. Reading the
        // DOM and asserting it "should" expose a name is a different claim.
        const { nodes } = await session.send("Accessibility.getFullAXTree", {}, sessionId);
        const named = (node) => (node.name?.value ?? "").trim().length > 0;
        const ignored = (node) => node.ignored === true;
        const axNodes = nodes.filter((node) => !ignored(node));
        const roles = axNodes.map((node) => node.role?.value).filter(Boolean);
        const interactive = axNodes.filter((node) =>
          ["link", "button", "textbox", "checkbox", "combobox", "disclosure triangle"].includes(
            node.role?.value,
          ),
        );
        const unnamedInteractive = interactive.filter((node) => !named(node));
        const headings = axNodes.filter((node) => node.role?.value === "heading");
        const unnamedHeadings = headings.filter((node) => !named(node));
        observed.axNodes = axNodes.length;
        observed.axInteractive = interactive.length;
        observed.axHeadings = headings.length;
        observed.axLandmarks = roles.filter((role) => ["main", "note", "group", "complementary"].includes(role)).length;

        if (axNodes.length === 0) {
          failures.push(`${page} @${viewport.label}/${scheme}: the accessibility tree is empty`);
        }
        if (observed.gluedCount > 0) {
          failures.push(
            `${page} @${viewport.label}/${scheme}: ${observed.gluedCount} adjacent element(s) ` +
              `painted flush against each other: ${observed.glued.join("; ")}`,
          );
        }
        if (observed.inertControls > 0) {
          failures.push(
            `${page} @${viewport.label}/${scheme}: ${observed.inertControls} control(s) with no ` +
              "activation path on a page that loads no script",
          );
        }
        if (unnamedInteractive.length > 0) {
          failures.push(
            `${page} @${viewport.label}/${scheme}: ${unnamedInteractive.length} interactive node(s) ` +
              `with no accessible name: ${unnamedInteractive.map((n) => n.role?.value).join(", ")}`,
          );
        }
        if (unnamedHeadings.length > 0) {
          failures.push(
            `${page} @${viewport.label}/${scheme}: ${unnamedHeadings.length} heading(s) with no accessible name`,
          );
        }
        // Counting landmarks was too weak: replacing <main> with a <div> still left the
        // banner's note and the provenance aside, so the count stayed nonzero and the
        // mutation passed. The main landmark is the one a screen-reader user jumps to,
        // so it is required by role and by count.
        observed.axMain = roles.filter((role) => role === "main").length;
        if (observed.axMain !== 1) {
          failures.push(
            `${page} @${viewport.label}/${scheme}: expected exactly one main landmark, found ${observed.axMain}`,
          );
        }
        if (observed.axLandmarks === 0) {
          failures.push(`${page} @${viewport.label}/${scheme}: no landmark role in the accessibility tree`);
        }
        const row = { page, viewport: viewport.label, scheme, console: logged.length, ...observed };
        rows.push(row);

        if (logged.length > 0) failures.push(`${page} @${viewport.label}: console output ${JSON.stringify(logged)}`);
        if (observed.horizontalOverflow) {
          failures.push(
            `${page} @${viewport.label}: horizontal overflow ${observed.scrollWidth} > ${observed.clientWidth}`,
          );
        }
        if (observed.h1Count !== 1) failures.push(`${page} @${viewport.label}: ${observed.h1Count} h1 elements`);
        // WCAG 1.3.1: a heading level may not be skipped on the way down. The harness
        // collected headings but never looked at their levels, so a refusal page that
        // went h1 then h3 stayed green across all 50 combinations.
        const levels = observed.headingLevels ?? [];
        if (levels.length === 0) {
          failures.push(`${page} @${viewport.label}: no headings were found`);
        }
        if (levels.length > 0 && levels[0] !== 1) {
          failures.push(`${page} @${viewport.label}: the first heading is h${levels[0]}, not h1`);
        }
        for (let index = 1; index < levels.length; index += 1) {
          if (levels[index] > levels[index - 1] + 1) {
            failures.push(
              `${page} @${viewport.label}: heading level jumps from h${levels[index - 1]} ` +
                `to h${levels[index]}`,
            );
          }
        }
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
    // Chrome spawns a process tree, and killing only the process we launched leaves the
    // renderers and the GPU process behind. Repeated runs left 51 of them alive, holding
    // handles on dist/ so the next build failed with EBUSY. Kill the tree, not the parent.
    await killTree(child.pid);
    await site.close();
    await rm(profile, { force: true, recursive: true }).catch(() => {});
  }

  for (const row of rows) {
    console.log(
      `${row.page.replace('state-','').replace('.html','').padEnd(18)} ${row.viewport.padEnd(8)} ${row.scheme.padEnd(6)} ` +
        `console=${row.console} h1=${row.h1Count} overflow=${row.horizontalOverflow} scripts=${row.scriptCount} ` +
        `contrast=${row.contrastChecked} worst=${row.worstContrast} ` +
        `ax=${row.axNodes} named-interactive=${row.axInteractive} landmarks=${row.axLandmarks} main=${row.axMain} glued=${row.gluedCount} inert=${row.inertControls}`,
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
