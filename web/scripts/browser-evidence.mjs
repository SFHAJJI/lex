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
import { readdir, readFile, realpath } from "node:fs/promises";
import { existsSync } from "node:fs";
import { resolve as resolvePath } from "node:path";
import { extname, join as joinPath, sep as pathSep } from "node:path";
import { CSP_DIRECTIVES, FORBIDDEN_SOURCES, cspValue } from "./csp.mjs";
import { decodePng, inkMeasure } from "./png-ink.mjs";
import { mkdtemp, rm, stat } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

import { SHELLS, parseObjectUrl } from "./urls.mjs";

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
// Iterating on one truth rule does not need 315 page and viewport combinations to tell you
// whether the rule holds; it needs one. LEX_EVIDENCE_FAST=1 keeps every page and every gate and
// drops the matrix to a single width and scheme, which turns a two-minute wait into seconds.
// The full matrix is what a freeze is measured on and is still the default, because responsive
// and forced-colours defects are exactly the ones a single viewport cannot see.
const FAST = process.env.LEX_EVIDENCE_FAST === "1";

const ALL_WIDTHS = [
  { label: "narrow", width: 320, height: 640 },
  { label: "tablet", width: 768, height: 1024 },
  { label: "desktop", width: 1440, height: 900 },
  // Browser zoom shrinks the CSS viewport rather than scaling pixels, so 200% zoom at a
  // 1440 window is a 720 CSS viewport and WCAG 1.4.10's 400% at 1280 is 320. Naming them
  // as zoom levels keeps the evidence honest about what was actually exercised.
  { label: "zoom200", width: 720, height: 450 },
  { label: "zoom400", width: 320, height: 256 },
];

const WIDTHS = FAST ? ALL_WIDTHS.slice(2, 3) : ALL_WIDTHS;

/**
 * Every page the build emits, read from the build's own output.
 *
 * This was a hand-written list, and it was wrong twice for the same reason. The first time
 * the trust surface shipped outside it, and the run said "all combinations clean" while
 * measuring five pages out of six. The comment left behind said so, and then compare shipped
 * and the count stayed at 100, which is evidence about eleven pages presented as evidence
 * about twelve.
 *
 * A list somebody has to remember to update is a gate that fails open, quietly, in the
 * direction of a clean report. So the list is now the directory: whatever the build emits is
 * what gets measured, and a page cannot ship unmeasured without also not shipping.
 */
export async function pagesFrom(root) {
  // The build says what it emitted, and this run measures exactly that.
  //
  // A floor of "at least N pages" was the same hand-maintained number the directory read was
  // supposed to retire, and it checked only how many there were. Twelve unrelated files would
  // have passed it, and it sat one below the count the build actually produced, so a page
  // could vanish without a word. The build now declares its own list and the two must agree
  // as sets: a page declared and absent is a build that half ran, and a page present and
  // undeclared is a stale artefact being measured as though it were current.
  const declared = JSON.parse(await readFile(joinPath(root, "pages.json"), "utf8"));
  const manifest = [...declared.pages].sort();
  const found = (await readdir(root)).filter((name) => name.endsWith(".html")).sort();

  const missing = manifest.filter((name) => !found.includes(name));
  const extra = found.filter((name) => !manifest.includes(name));
  if (missing.length > 0 || extra.length > 0) {
    throw new Error(
      `the build declared ${manifest.length} pages and the directory holds ${found.length}` +
        (missing.length > 0 ? `; declared and absent: ${missing.join(", ")}` : "") +
        (extra.length > 0 ? `; present and undeclared: ${extra.join(", ")}` : "") +
        "; run npm run build",
    );
  }
  // An empty baseline passes forever, so it is refused rather than reported clean.
  if (found.length === 0) {
    throw new Error("the build emitted no pages, so this run would prove nothing");
  }
  const unrouted = routePagesMissingFrom(found);
  if (unrouted.length > 0) {
    throw new Error(
      `the app routes to shells with no measured page: ${unrouted.join(", ")}` +
        "; the manifest agrees with the directory, so this is a build that emitted a smaller " +
        "app than it routes, not a stale artefact; run npm run build",
    );
  }
  return found;
}

/**
 * The pages a declared route surface requires, that a measured page set does not contain.
 *
 * WHY THE SET CHECK ABOVE IS NOT THIS CHECK. `pages.json` is written from what the build
 * emitted, so manifest and directory are two readings of the same act. Delete the shell loop in
 * `build.mjs` and both shrink together: they still agree, the run still reports every
 * combination clean, and it is measuring an app smaller than the one the URL scheme routes to.
 * Agreement between a build and its own output cannot detect a build that did less.
 *
 * So completeness is anchored to the route declaration instead. `SHELLS` is the vocabulary
 * `build.mjs` loops over to emit entry screens and `urls.mjs` validates navigation against, and
 * a shell that routes without a page is the failure this exists to make loud.
 *
 * Exported so it can be driven directly. A guard reachable only through a browser run is a
 * guard nobody watches fail.
 *
 * @param {readonly string[]} measured page file names the run will actually visit
 * @param {readonly string[]} shells the declared route vocabulary
 * @returns {string[]} required page names absent from `measured`, sorted
 */
export function routePagesMissingFrom(measured, shells = SHELLS) {
  const present = new Set(measured);
  return [...shells]
    .map((shell) => `shell-${shell}.html`)
    .filter((name) => !present.has(name))
    .sort();
}

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

  // A command the browser never answers stalled a whole mutation sweep for twenty minutes with no
  // output. Every command now has a deadline, so a hung browser is a named failure, not a silence.
  send(method, params = {}, sessionId, deadlineMs = 60000) {
    const id = this.#next++;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.#pending.delete(id);
        reject(new Error(`the browser did not answer ${method} within ${deadlineMs / 1000} s`));
      }, deadlineMs);
      this.#pending.set(id, {
        resolve: (value) => {
          clearTimeout(timer);
          resolve(value);
        },
        reject: (error) => {
          clearTimeout(timer);
          reject(error);
        },
      });
      this.#socket.send(JSON.stringify({ id, method, params, sessionId }));
    });
  }

  /** The next event `method` on `sessionId`, or a named failure if the browser never reports it. */
  waitFor(method, sessionId, deadlineMs = 60000) {
    return new Promise((resolve, reject) => {
      const listener = (message) => {
        if (message.method !== method || message.sessionId !== sessionId) return;
        this.#listeners.delete(listener);
        clearTimeout(timer);
        resolve(message.params);
      };
      const timer = setTimeout(() => {
        this.#listeners.delete(listener);
        reject(new Error(`the browser did not report ${method} within ${deadlineMs / 1000} s`));
      }, deadlineMs);
      this.#listeners.add(listener);
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
    const children = [...parent.children].filter((el) => {
      if (el.offsetParent === null || el.textContent.trim().length === 0) return false;
      // A visually-hidden element is clipped away so a screen reader can still reach it.
      // Nothing is painted there, so it cannot be painted flush against anything.
      //
      // The first version of this exemption skipped anything thinner than two pixels, which
      // is far wider than the thing it was written for: a value collapsed to zero width with
      // its text hidden, and a genuinely one-pixel element touching its neighbour, both
      // disappeared from a gate whose entire justification is catching what the others miss.
      // Clipping is exactly identifiable, so that is the only thing exempted.
      return getComputedStyle(el).clipPath === 'none';
    });
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
      // A visible border between two boxes is a separator, and with collapsed table borders
      // adjacent cells share one, so their rects touch exactly. Reading that as "painted
      // flush" was a false positive of this gate rather than a defect in the page.
      const afterStyle = getComputedStyle(after);
      const bordered =
        parseFloat(afterStyle.borderLeftWidth) > 0 || parseFloat(afterStyle.borderTopWidth) > 0;
      const separated = b.left - a.right >= 2 || b.top >= a.bottom || bordered;
      if (!separated) {
        glued.push(
          parent.className + ' > ' + before.className + ' | ' + after.className,
        );
      }
    }
  }

  // Meaning carried only by paint.
  //
  // Forced-colours mode removes background images, so any element whose meaning lives in one
  // loses it entirely, and the reader is not told anything is missing. This screen has exactly
  // that shape: the timeline hatches its gaps with a repeating gradient, and a gap that becomes
  // invisible is the one mark whose absence asserts something false about the law.
  //
  // The rule this project already states is that nothing means anything by colour alone. This
  // measures it rather than trusting it: an element painted with a background image must also
  // say what it is, in text or in an accessible name. Decorative elements are exempt because
  // they are already declared decorative.
  const paintOnly = [];
  for (const el of document.querySelectorAll('body *')) {
    if (el.closest('[aria-hidden="true"]') !== null) continue;
    const style = getComputedStyle(el);
    if (style.backgroundImage === 'none') continue;
    const named =
      el.textContent.trim().length > 0 ||
      (el.getAttribute('aria-label') ?? '').trim().length > 0 ||
      (el.getAttribute('alt') ?? '').trim().length > 0;
    if (!named) paintOnly.push(el.tagName + '.' + String(el.className).slice(0, 30));
  }

  // WCAG 2.2 SC 2.5.8, target size minimum: 24 by 24 CSS pixels. The exception this takes is
  // the inline one, a link inside a sentence, approximated as an anchor whose parent is a
  // paragraph. Everything else is a target somebody has to hit, and Lighthouse found three
  // handoff links at 19 to 21 px that every check here passed.
  const smallTargets = [];
  for (const el of document.querySelectorAll('a[href],button,input,select,textarea,summary')) {
    if (el.offsetParent === null) continue;
    const inSentence = el.tagName === 'A' && el.parentElement?.tagName === 'P';
    if (inSentence) continue;
    const rect = el.getBoundingClientRect();
    if (rect.width < 24 || rect.height < 24) {
      smallTargets.push({
        tag: el.tagName.toLowerCase(),
        cls: (el.className || '').toString().slice(0, 40),
        width: Math.round(rect.width * 10) / 10,
        height: Math.round(rect.height * 10) / 10,
      });
    }
  }

  // Same-origin destinations, collected so the run can check that each one resolves. A
  // visible action leading to a missing page is a promise the page cannot keep, and three
  // of them shipped: the provenance link and both ambiguity candidates returned 404.
  const sameOrigin = [...new Set(
    [...document.querySelectorAll('a[href]')]
      .map((el) => el.getAttribute('href'))
      .filter((href) => href && href.startsWith('/')),
  )];

  // The content of a closed disclosure is not rendered, so Tab never reaches it: it is not a
  // focusable element of the page as served. Its summary is. (S5-A10 puts each unofficial
  // rendering, with its link to the authentic text, inside a closed disclosure.)
  const focusable = [...document.querySelectorAll(
    'a[href],button,input,select,textarea,summary,[tabindex]:not([tabindex="-1"])')]
    .filter((el) => !el.closest('details:not([open]) > :not(summary)'));
  const headingEls = [...document.querySelectorAll('h1,h2,h3,h4,h5,h6')];
  const heads = headingEls.map((h) => h.tagName + ':' + h.textContent.trim().slice(0, 40));
  const headingLevels = headingEls.map((h) => Number(h.tagName.slice(1)));
  const body = getComputedStyle(document.body);
  const mainEl = document.querySelector('main');
  const mainStyle = mainEl ? getComputedStyle(mainEl) : null;
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
    // S5 names roving tabindex and pressed states as RUNTIME browser evidence, and neither was
    // measured here: both were covered only by unit assertions over a rendered tree. That is a
    // different claim. A unit test shows the attribute was written; only the hydrated page shows
    // the client still holds the invariant after it takes over. Exactly one option in a listbox may
    // be tabbable -- the rest are reached by arrow keys -- so a listbox with two tabbable options,
    // or none, is a keyboard trap or a dead group whichever way it fails.
    rovingGroups: [...document.querySelectorAll('[role=listbox]')].map((group) => {
      const options = [...group.querySelectorAll('[role=option]')];
      return {
        options: options.length,
        tabbable: options.filter((el) => el.getAttribute('tabindex') === '0').length,
      };
    }),
    // A toggle's pressed state has to be a boolean the assistive layer can announce. Anything else
    // renders as a styled control whose state never reaches a screen reader, which is the exact
    // failure FilterChips' own comment says aria-pressed exists to prevent.
    pressedValues: [...document.querySelectorAll('[aria-pressed]')].map((el) =>
      el.getAttribute('aria-pressed'),
    ),
    syntheticBanner: !!document.querySelector('[data-synthetic]'),
    horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
    bodyColor: body.color,
    bodyBackground: body.backgroundColor,
    scriptCount: document.querySelectorAll('script').length,
    // The policy as the browser parsed it, and every script's origin. A zero-script
    // count was only ever a proxy for nothing-unreviewed-executes, and it stops being
    // one the moment a page is hydrated. A gate that silently becomes vacuous is worse
    // than no gate, because it keeps reporting a pass.
    csp:
      document.querySelector('meta[http-equiv="Content-Security-Policy" i]')?.content ?? null,
    inlineScripts: [...document.querySelectorAll('script')].filter((el) => !el.src).length,
    scriptOrigins: [...document.querySelectorAll('script[src]')].map(
      (el) => new URL(el.src, document.baseURI).origin,
    ),
    documentOrigin: window.location.origin,
    // What the browser actually decoded the bytes as, not what the head claims. React writes
    // this attribute as charSet, which HTML matches case-insensitively, and the fixture is
    // ASCII enough that a wrong encoding would not surface until real French statute renders,
    // by which point every accented character in the corpus is wrong. Asserting the decoded
    // result also covers a wrong Content-Type header and a byte-order mark, which reading the
    // attribute back out of the DOM would not.
    characterSet: document.characterSet,
    // Statutory type, as rendered rather than as declared. The UX spec fixes serif at
    // 17px/1.65 on desktop, 16px/1.6 on mobile, and a 72ch maximum measure. A stylesheet
    // saying so is a comment: a font that fails to load, a rule overridden downstream, or a
    // measure that never applies all leave the declaration intact and the page wrong. This
    // is the type a reader actually gets for the law itself, which is the one run of text
    // on the site that is not ours.
    statutoryType: [...document.querySelectorAll('blockquote.law, .reading-text')].map((el) => {
      const style = getComputedStyle(el);
      return {
        fontSize: Number.parseFloat(style.fontSize),
        lineHeight: Number.parseFloat(style.lineHeight) / Number.parseFloat(style.fontSize),
        family: style.fontFamily,
        width: el.getBoundingClientRect().width,
      };
    }),
    // Hydration, as the client reports it. Set only after the first pass completes, and set
    // to recovered when React had to redraw because the server markup and the client tree
    // disagreed. React recovers from that quietly, so a page that hydrated by re-rendering
    // looks identical to one that hydrated cleanly unless something records the difference.
    hydrated: document.documentElement.dataset.hydrated ?? null,
    hydrationRecovered: document.documentElement.dataset.hydrationRecovered ?? null,
    shell: document.documentElement.dataset.shell ?? null,
    density: document.documentElement.dataset.density ?? null,
    mainLineHeight: mainStyle ? mainStyle.lineHeight : null,
    mainFontFamily: mainStyle ? mainStyle.fontFamily : null,
    contrastChecked: contrast.length,
    worstContrast: worst ? worst.ratio : null,
    worstContrastTag: worst ? worst.tag : null,
    worstContrastRequired: worst ? worst.required : null,
    contrastFailures: contrast.filter((c) => c.ratio < c.required).length,
    paintOnly: paintOnly.slice(0, 8),
    paintOnlyCount: paintOnly.length,
    glued: glued.slice(0, 8),
    gluedCount: glued.length,
    smallTargets: smallTargets.slice(0, 8),
    smallTargetCount: smallTargets.length,
    sameOrigin,
    // A control with no handler and no form is a promise the page cannot keep, and on a page
    // that ships no script every button is inert by construction. That last clause is the whole
    // rule, and it used to be a comment rather than a condition: the count was unconditional, so
    // a hydrated page carrying real controls failed a check written about pages that carry none.
    // No built page had ever had a control, so nothing had disagreed with it yet.
    inertControls:
      document.querySelectorAll('script').length === 0
        ? [...document.querySelectorAll('button, a:not([href])')].length
        : 0,
  };
})()`;

/**
 * Walk the page with real Tab presses and report what actually receives focus.
 *
 * Counting focusable elements is not a keyboard test: it says nothing about order,
 * nothing about whether focus is visible, and nothing about whether a control can be
 * reached at all. This presses Tab and records where focus lands.
 */
/**
 * Drives the three interactive contracts this sweep claims, rather than reading their opening
 * attributes.
 *
 * Roving tabindex is a behaviour: pressing ArrowDown must move BOTH the active element and the
 * single tab stop. A page can ship exactly one `tabindex="0"` for ever and be completely inert,
 * which is what the reviewer proved by replacing `MOVES[event.key]` with `undefined` -- the
 * attribute snapshot was unchanged and the earlier gate passed. Pressed state is the same shape:
 * `aria-pressed` can hold a correct boolean that no click will ever flip, which a no-op `onClick`
 * produces and an attribute check cannot see.
 *
 * Compare arming is the third, and the one that was claimed longest without being reachable: the
 * page said a pair of rows arms the comparison while its only two rows of one work shared a
 * lex_id, so selecting the second deselected the first and the armed state could never be shown.
 * Space is pressed on declared rows (`COMPARE_ROWS`) and every step is read back through the
 * button's own description, so the sentence judged is the one a screen reader hears.
 *
 * Returns null for pages carrying none of the three, so the gates stay silent where there is
 * nothing to drive rather than inventing a pass.
 */
export async function drivenBehaviour(session, sessionId, rows = null, compareAtLoad = null) {
  const read = async (expression) => {
    const { result } = await session.send(
      "Runtime.evaluate",
      { expression, returnByValue: true },
      sessionId,
    );
    return result.value;
  };

  // `minimum` is 2 for the roving probe, where a one-option list proves nothing about movement, and
  // 1 for the shrink probe, where a single surviving option must still be reachable.
  const listboxAt = (minimum) => `(() => {
    const box = document.querySelector('[role=listbox]');
    if (!box) return null;
    const options = [...box.querySelectorAll('[role=option]')];
    if (options.length < ${minimum}) return null;
    return {
      tab: options.findIndex((o) => o.getAttribute('tabindex') === '0'),
      tabbable: options.filter((o) => o.getAttribute('tabindex') === '0').length,
      active: options.indexOf(document.activeElement),
      count: options.length,
    };
  })()`;
  const listbox = listboxAt(2);

  // Space arms a row. Its `key` is a literal space and its `code` is the word, which is the one
  // key in this table where the two differ.
  const KEY_CODES = { ArrowDown: 40, ArrowUp: 38, Home: 36, End: 35, " ": 32 };
  const press = async (key) => {
    for (const type of ["rawKeyDown", "keyUp"]) {
      await session.send(
        "Input.dispatchKeyEvent",
        {
          type,
          key,
          code: key === " " ? "Space" : key,
          windowsVirtualKeyCode: KEY_CODES[key],
          nativeVirtualKeyCode: KEY_CODES[key],
        },
        sessionId,
      );
    }
  };

  let roving = null;
  let keys = null;
  const start = await read(listbox);
  if (start && start.tab >= 0) {
    // Focus the real tab stop first: an arrow key sent to the body proves nothing about the group.
    await read(`(() => {
      const box = document.querySelector('[role=listbox]');
      const options = [...box.querySelectorAll('[role=option]')];
      options[${start.tab}].focus();
      return true;
    })()`);
    await press("ArrowDown");
    const after = await read(listbox);
    roving = { before: { ...start, active: start.tab }, after };

    // Every key in the movement table is driven, not only ArrowDown. ArrowUp, Home and End were
    // declared and never pressed, so any of them could be dead while every gate stayed green --
    // the same defect the ArrowDown probe exists to catch. End goes first so ArrowUp starts from
    // a known place and Home has somewhere to come back from.
    keys = [];
    const last = start.count - 1;
    for (const [key, expected] of [
      ["End", last],
      ["ArrowUp", Math.max(last - 1, 0)],
      ["Home", 0],
    ]) {
      await press(key);
      keys.push({ key, expected, state: await read(listbox) });
    }
  }

  // Compare arming, before the chip click and before standing on the last row: the "main-work"
  // chip hides the annex row this probe needs, and the sequence ends with nothing selected so the
  // probes after it measure the page as it was served. Rows are found by title and focused
  // directly, the way the roving probe finds its tab stop, because arrow travel would make every
  // step depend on the movement table this probe is not about.
  let compare = null;
  const opening = await read(compareSnapshot(rows ?? {}));
  if (opening && rows === null) {
    compare = { undeclared: true };
  } else if (rows !== null && !opening) {
    // Declared and absent is not "nothing to drive": the page lost the control the probe was
    // written for, and passing it would read a page without comparison as one where it works.
    compare = { rows, missing: true };
  } else if (opening) {
    const drift = Object.fromEntries(
      Object.entries(opening.matches).filter(([, count]) => count !== 1),
    );
    if (Object.keys(drift).length > 0) {
      compare = { rows, drift };
    } else {
      opening.exposed = compareAtLoad ?? (await comparePresence(session, sessionId));
      opening.ink = await compareInk(session, sessionId);
      const steps = [opening];
      for (const step of COMPARE_STEPS.slice(1)) {
        await read(`(() => {
          const title = ${JSON.stringify(rows[step.press])};
          // The same listbox the snapshot reads, so a press can never land in another list.
          const box = document.querySelector('[role=listbox]');
          const row = [...(box ? box.querySelectorAll('[role=option]') : [])].find((option) => {
            const own = option.querySelector('.results-title');
            return own !== null && own.textContent.trim() === title;
          });
          if (!row) return false;
          row.focus();
          return true;
        })()`);
        await press(" ");
        const seen = await read(compareSnapshot(rows));
        if (seen) {
          seen.exposed = await comparePresence(session, sessionId);
          seen.ink = await compareInk(session, sessionId);
        }
        steps.push(seen);
      }
      compare = { rows, steps };
    }
  }

  // The pressed contract is two claims at once: the control's own state flips, and the thing it
  // controls changes with it. A toggle that announces itself pressed while filtering nothing is
  // still broken, so the represented count is read alongside it.
  const chip = `(() => {
    const el = document.querySelector('[aria-pressed]');
    if (!el) return null;
    const count = document.querySelector('.filter-count');
    return { pressed: el.getAttribute('aria-pressed'), represented: count ? count.textContent.trim() : null };
  })()`;

  // Stand on the last row before the filter runs. A filter that shortens the list must leave the
  // single tab stop on a row that still exists; otherwise no option is tabbable and Tab jumps over
  // the whole listbox. Standing on the first row would never expose that, because every filter
  // keeps index 0 in range.
  let standing = null;
  if (keys) {
    await press("End");
    standing = await read(listboxAt(1));
  }

  let pressed = null;
  let shrink = null;
  const chipBefore = await read(chip);
  if (chipBefore) {
    await read("(() => { document.querySelector('[aria-pressed]').click(); return true; })()");
    pressed = { before: chipBefore, after: await read(chip) };
    if (standing) shrink = { before: standing, after: await read(listboxAt(1)) };
  }

  return roving || pressed || compare ? { roving, keys, pressed, shrink, compare } : null;
}

/**
 * What the compare control says, in each state the probe drives it through. The component's own
 * words, bound to it by `compare-arming-gate.test.mjs`, so a reworded sentence fails there, once,
 * rather than in every combination of the browser run.
 */
export const COMPARE_SENTENCES = Object.freeze({
  none: "Select two states to compare them.",
  one: "One state selected. Select a second to compare.",
  armed: "Two states of one work selected.",
  three: "A comparison is between two states. Deselect one before comparing.",
  works:
    "These are two different works. Two unrelated instruments are not states of each other, " +
    "and their differences are not legislation.",
});

/**
 * The rows the compare probe presses Space on, per page, by title.
 *
 * Declared rather than discovered, because which rows share a work is the thing under test: a
 * probe that picked "two rows of one work" by reading lex_ids off the page would find none on a
 * page where arming is unreachable, and pass by driving nothing. The titles are guarded instead:
 * each must name exactly one row, or the probe reports the fixture changed under it.
 *
 * `first` and `sameWork` are two states of one work; `otherWork` is a different work. The preview
 * also carries "article 2", which shares `first`'s lex_id because two provisions of one state
 * share it. It is never driven: selecting either of those rows marks both, which is selection
 * keyed by state rather than by row, and re-keying it is search-hit identity, not this probe.
 */
export const COMPARE_ROWS = Object.freeze({
  "search-react.html": Object.freeze({
    first: "Acte synthetique de demonstration, article 1",
    sameWork: "Acte synthetique de demonstration, article 1, etat anterieur",
    otherWork: "Annexe synthetique de demonstration",
  }),
});

/**
 * The drive, in order. Step 0 is the page at load; every later step is one Space press on the
 * named row. Selection toggles, so the walk goes up to three rows and back down to none.
 */
export const COMPARE_STEPS = Object.freeze([
  { press: null, label: "at load", expect: "none", selected: [] },
  { press: "first", label: "after Space on one state", expect: "one", selected: ["first"] },
  {
    press: "sameWork",
    label: "with two states of one work selected",
    expect: "armed",
    selected: ["first", "sameWork"],
  },
  {
    press: "otherWork",
    label: "with three rows selected",
    expect: "three",
    selected: ["first", "sameWork", "otherWork"],
  },
  {
    press: "sameWork",
    label: "with rows of two different works selected",
    expect: "works",
    selected: ["first", "otherWork"],
  },
  { press: "otherWork", label: "after deselecting down to one state", expect: "one", selected: ["first"] },
  { press: "first", label: "after deselecting every row", expect: "none", selected: [] },
]);

// Why an armed control is wrong in each state that must not arm. Said in the failure so the
// reader of the log does not have to know the rule to know which half of it broke.
const NOT_ARMED_BECAUSE = Object.freeze({
  none: "nothing is selected",
  one: "one state is not a comparison",
  three: "a comparison is between two states",
  works: "two unrelated instruments are not states of each other",
});

/**
 * An in-page read of the compare control and the declared rows.
 *
 * The control is the one in the same results section as the listbox, and its sentence is read
 * through the button's `aria-describedby` rather than by class, so a sentence the button no longer
 * points at is a sentence nobody hears. `matches` counts the rows carrying each declared title.
 */
/**
 * The compare control as assistive technology is given it: what the browser's own accessibility
 * tree holds for the button, not what the DOM holds.
 *
 * A control under `aria-hidden`, or otherwise out of the tree, is a control a screen reader is
 * never offered. The DOM read cannot see that: every attribute it reads is still there. The
 * description is what `aria-describedby` resolves to in the tree, which is the sentence a reader
 * hears, so it is compared with the sentence on the page.
 */
/**
 * The ink the compare sentence leaves, measured from a photograph of its box.
 *
 * Rendering is not being seen. A sentence clipped to one pixel, or drawn in transparent text, is
 * rendered, visible to `checkVisibility` and returned by `innerText`, and no reader will ever read
 * it. This is the S5-A10 label measurement applied to the sentence: the element is scrolled into
 * view, photographed, and its pixels counted against the background of its own box. One page
 * carries the control, so this is about a hundred photographs in a run.
 */
async function compareInk(session, sessionId) {
  const { result } = await session.send(
    "Runtime.evaluate",
    {
      expression: `(() => {
        const box = document.querySelector('[role=listbox]');
        const section = box ? box.closest('section.results') : null;
        const control = (section ?? document).querySelector('.compare-arming');
        const button = control ? control.querySelector('button') : null;
        const described = button ? document.getElementById(button.getAttribute('aria-describedby')) : null;
        if (!described) return null;
        described.scrollIntoView({ block: 'center', inline: 'nearest' });
        const r = described.getBoundingClientRect();
        return { x: r.left + window.scrollX, y: r.top + window.scrollY, width: r.width, height: r.height };
      })()`,
      returnByValue: true,
    },
    sessionId,
  );
  const box = result.value;
  if (!box || box.width < 1 || box.height < 1) {
    return { pixels: 0, share: 0, width: Math.round(box?.width ?? 0), height: Math.round(box?.height ?? 0) };
  }
  const { data } = await session.send(
    "Page.captureScreenshot",
    { format: "png", clip: { ...box, scale: 1 }, captureBeyondViewport: false },
    sessionId,
  );
  return {
    ...inkMeasure(decodePng(Buffer.from(data, "base64"))),
    width: Math.round(box.width),
    height: Math.round(box.height),
  };
}

async function comparePresence(session, sessionId) {
  const { result } = await session.send(
    "Runtime.evaluate",
    {
      expression: `(() => {
        const box = document.querySelector('[role=listbox]');
        const section = box ? box.closest('section.results') : null;
        const control = (section ?? document).querySelector('.compare-arming');
        return control ? control.querySelector('button') : null;
      })()`,
    },
    sessionId,
  );
  if (!result.objectId) return { inTree: false, role: null, name: null, description: null };
  const { nodes } = await session.send("Accessibility.queryAXTree", { objectId: result.objectId }, sessionId);
  await session.send("Runtime.releaseObject", { objectId: result.objectId }, sessionId);
  const node = (nodes ?? []).find((candidate) => candidate.ignored !== true);
  if (!node) return { inTree: false, role: null, name: null, description: null };
  return {
    inTree: true,
    role: node.role?.value ?? null,
    name: (node.name?.value ?? "").trim(),
    description: (node.description?.value ?? "").trim(),
  };
}

function compareSnapshot(rows) {
  return `(() => {
    const rows = ${JSON.stringify(rows)};
    const box = document.querySelector('[role=listbox]');
    const section = box ? box.closest('section.results') : null;
    const control = (section ?? document).querySelector('.compare-arming');
    if (!control) return null;
    const button = control.querySelector('button');
    const described = button ? document.getElementById(button.getAttribute('aria-describedby')) : null;
    const options = box ? [...box.querySelectorAll('[role=option]')] : [];
    const titled = (title) => options.filter((option) => {
      const own = option.querySelector('.results-title');
      return own !== null && own.textContent.trim() === title;
    });
    const selected = {};
    const matches = {};
    for (const [key, title] of Object.entries(rows)) {
      const found = titled(title);
      matches[key] = found.length;
      selected[key] = found.length === 1 ? found[0].getAttribute('aria-selected') : null;
    }
    // The sentence as a reader is given it, not as the DOM holds it: innerText is what is
    // rendered, and a hidden element renders nothing. The box and the computed style say why.
    let shown = null;
    if (described) {
      const rect = described.getBoundingClientRect();
      const style = getComputedStyle(described);
      const visible = typeof described.checkVisibility === 'function'
        ? described.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true })
        : style.display !== 'none' && style.visibility !== 'hidden' && style.opacity !== '0';
      shown = {
        text: described.innerText.trim(),
        visible,
        hiddenAttr: described.hasAttribute('hidden'),
        width: Math.round(rect.width),
        height: Math.round(rect.height),
        display: style.display,
        visibility: style.visibility,
      };
    }
    return {
      sentence: described ? described.textContent.trim() : null,
      shown,
      ariaDisabled: button ? button.getAttribute('aria-disabled') : null,
      disabledAttr: button ? button.hasAttribute('disabled') : false,
      selected,
      matches,
    };
  })()`;
}

/**
 * The compare-arming verdict, as failure sentences. Pure, so the node tests hold every sentence
 * without a browser.
 *
 * Per step, at most one line about the control itself -- whether it armed, else what it said --
 * because an armed control saying the armed sentence is one defect, not two. Row state and the
 * `disabled` attribute are separate claims and get their own lines.
 *
 * @param {string} where  the page, viewport and scheme, as every other failure names them
 * @param {object|null} compare  what `drivenBehaviour` measured as `compare`
 */
export function compareFailures(where, compare) {
  if (compare === null) return [];
  if (compare.undeclared) {
    return [
      `${where}: a compare control is on this page and the compare probe declares no rows for it, ` +
        "so arming was never driven",
    ];
  }
  const { rows } = compare;
  if (compare.missing) {
    return [
      `${where}: the compare probe declares rows for this page and the page has no compare control, ` +
        "so two states of one work cannot be compared here",
    ];
  }
  if (compare.drift) {
    return Object.entries(compare.drift).map(
      ([key, count]) =>
        `${where}: the compare probe drives the row "${rows[key]}" and the page shows ${count} ` +
        "row(s) with that title; the fixture it was written for has changed, so arming was not driven",
    );
  }
  const failures = [];
  COMPARE_STEPS.forEach((step, index) => {
    const seen = compare.steps?.[index] ?? null;
    if (seen === null) {
      failures.push(
        `${where}: ${step.label}, the compare control was gone; a control that disappears while ` +
          "rows are selected cannot say why they cannot be compared",
      );
      return;
    }
    const d = seen.ariaDisabled;
    const s = seen.sentence;
    const armed = d === "false";
    if (step.expect === "armed" && !armed) {
      failures.push(
        `${where}: ${step.label} ("${rows.first}", "${rows.sameWork}"), Compare stayed ` +
          `aria-disabled="${d}" and said "${s}"; the armed state is unreachable`,
      );
    } else if (step.expect !== "armed" && armed) {
      failures.push(
        `${where}: ${step.label}, Compare was armed (aria-disabled="${d}") and said "${s}"; ` +
          NOT_ARMED_BECAUSE[step.expect],
      );
    } else if (s !== COMPARE_SENTENCES[step.expect]) {
      failures.push(
        `${where}: ${step.label}, the compare control said "${s}", not "${COMPARE_SENTENCES[step.expect]}"`,
      );
    }
    for (const key of Object.keys(rows)) {
      const want = step.selected.includes(key) ? "true" : "false";
      const value = seen.selected?.[key] ?? null;
      if (value !== want) {
        failures.push(
          `${where}: ${step.label}, row "${rows[key]}" is aria-selected="${value}", not "${want}"; ` +
            "the list does not say which rows are armed",
        );
      }
    }
    if (seen.disabledAttr) {
      failures.push(
        `${where}: ${step.label}, the Compare button carries the disabled attribute, so it leaves ` +
          "the Tab order and the reason it cannot be pressed is out of reach",
      );
    }
    // The sentence as a reader is given it. The DOM holding the right words says nothing: a
    // hidden element holds them and shows nobody, and `textContent` reads them all the same.
    const shown = seen.shown ?? null;
    if (shown !== null && (!shown.visible || shown.text === "" || shown.width === 0 || shown.height === 0)) {
      failures.push(
        `${where}: ${step.label}, the compare control's sentence is in the page and not shown ` +
          `(${shown.hiddenAttr ? "the hidden attribute" : `display ${shown.display}, visibility ${shown.visibility}`}, ` +
          `${shown.width}x${shown.height} box, the words "${shown.text}" in the document only); ` +
          "a reason a reader cannot see is not a reason",
      );
    } else if (seen.ink != null && (seen.ink.pixels < LABEL_INK.pixels || seen.ink.share < LABEL_INK.share)) {
      // Rendered is not seen. A sentence clipped to a pixel, or drawn in transparent text, is
      // rendered, passes every property check, and no reader will ever read it. This is the
      // S5-A10 label measurement: the pixels decide.
      failures.push(
        `${where}: ${step.label}, the compare control's sentence is not painted: ${seen.ink.pixels} ` +
          `ink pixel(s), ${Math.round(seen.ink.share * 1000) / 10}% of its ${seen.ink.width}x` +
          `${seen.ink.height} box, differ from the background; a reason nobody can read is not a reason`,
      );
    } else if (shown !== null && shown.text !== s) {
      failures.push(
        `${where}: ${step.label}, the compare control shows "${shown.text}" and its sentence is ` +
          `"${s}"; what a reader sees and what the control says must be one sentence`,
      );
    }
    // The control as assistive technology is given it. Every attribute this probe reads survives
    // aria-hidden, so only the browser's own accessibility tree can say the control is offered.
    const exposed = seen.exposed ?? null;
    if (exposed !== null && !exposed.inTree) {
      failures.push(
        `${where}: ${step.label}, the Compare button is not in the accessibility tree; a control a ` +
          "screen reader is never given cannot tell anyone why states cannot be compared",
      );
    } else if (exposed !== null && exposed.description !== s) {
      failures.push(
        `${where}: ${step.label}, the Compare button's description is "${exposed.description}" and ` +
          `the sentence on the page is "${s}"; a screen reader is told something else`,
      );
    }
  });
  return failures;
}

/**
 * S5-A10, driven: an unofficial rendering is never the default view, and is clearly labelled.
 *
 * Measured, not inferred from markup. At load, for every `.unofficial-rendering`: that it is a
 * `details`, whether it is open, whether its text is visible, and whether that text is in what the
 * page shows (`innerText`, which leaves out what the browser does not render). The UNOFFICIAL label
 * is measured the same way: the element whose own text carries the word is scrolled into view and
 * must render (no display, visibility or opacity hiding it), have a box and a font size a reader can
 * see, a colour that is not transparent, and be what the browser hits at its centre (so nothing
 * covers, clips or pushes it off the page). `textContent` ignores CSS, so a label a stylesheet hid
 * passed the first version of this check. Then every rendering is opened the way a keyboard reader
 * would -- focus its summary, press Enter -- and closed again, so a rendering that can never be
 * reached fails as surely as one that is shown unasked.
 */
export async function unofficialDisclosure(session, sessionId) {
  const read = async (expression) => {
    const { result } = await session.send(
      "Runtime.evaluate",
      { expression, returnByValue: true },
      sessionId,
    );
    return result.value;
  };
  const STATE = `(() => [...document.querySelectorAll('.unofficial-rendering')].map((el) => {
    const body = el.querySelector('blockquote');
    const text = body ? body.textContent.trim() : '';
    const summary = el.querySelector(':scope > summary');
    const word = summary
      ? [...summary.querySelectorAll('*')].find((node) =>
          [...node.childNodes].some((child) => child.nodeType === 3 && child.textContent.includes('UNOFFICIAL')))
      : null;
    let labelHidden = word ? null : 'no element carries the word';
    if (word) {
      word.scrollIntoView({ block: 'center', inline: 'nearest' });
      const box = word.getBoundingClientRect();
      const style = getComputedStyle(word);
      const channels = (style.color.match(/rgba?\\(([^)]*)\\)/) || [null, ''])[1].split(/[\\s,\\/]+/).filter(Boolean);
      const alpha = channels.length === 4 ? parseFloat(channels[3]) : 1;
      const hit = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
      if (!word.checkVisibility({ visibilityProperty: true, opacityProperty: true })) {
        labelHidden = 'display, visibility or opacity hides it';
      } else if (box.width < 8 || box.height < 8) {
        labelHidden = 'its box is ' + Math.round(box.width) + 'x' + Math.round(box.height) + ' px';
      } else if (parseFloat(style.fontSize) < 8) {
        labelHidden = 'its font size is ' + style.fontSize;
      } else if (alpha === 0) {
        labelHidden = 'its text colour is transparent';
      } else if (!hit || !(hit === word || word.contains(hit))) {
        labelHidden = 'something else is on top of it, or it is clipped or off the page';
      } else if (!summary.innerText.includes('UNOFFICIAL')) {
        labelHidden = 'it is not in the rendered text of the control';
      }
    }
    return {
      tag: el.tagName.toLowerCase(),
      open: el.open === true,
      label: summary ? summary.textContent.replace(/\\s+/g, ' ').trim() : null,
      labelHidden,
      visible: body ? body.checkVisibility({ visibilityProperty: true, opacityProperty: true }) : false,
      shown: text.length > 0 && document.body.innerText.includes(text),
    };
  }))()`;

  const load = await read(STATE);
  if (!load || load.length === 0) return null;

  // The verdict on the label comes from pixels. Every property list leaves a way out (a filter, a
  // text fill colour, a clip path, an alpha of 0.01), so the label's box is photographed by the
  // browser that painted it and must carry enough ink to draw a word. The named reasons above stay
  // as the diagnosis of why a label that is not painted is not painted.
  for (let index = 0; index < load.length; index++) {
    const box = await read(`(() => {
      const el = document.querySelectorAll('.unofficial-rendering')[${index}];
      const summary = el ? el.querySelector(':scope > summary') : null;
      const word = summary
        ? [...summary.querySelectorAll('*')].find((node) =>
            [...node.childNodes].some((child) => child.nodeType === 3 && child.textContent.includes('UNOFFICIAL')))
        : null;
      if (!word) return null;
      word.scrollIntoView({ block: 'center', inline: 'nearest' });
      const r = word.getBoundingClientRect();
      return { x: r.left + window.scrollX, y: r.top + window.scrollY, width: r.width, height: r.height };
    })()`);
    load[index].ink = { pixels: 0, share: 0, width: Math.round(box?.width ?? 0), height: Math.round(box?.height ?? 0) };
    if (box && box.width >= 1 && box.height >= 1) {
      const { data } = await session.send(
        "Page.captureScreenshot",
        { format: "png", clip: { ...box, scale: 1 }, captureBeyondViewport: false },
        sessionId,
      );
      load[index].ink = { ...inkMeasure(decodePng(Buffer.from(data, "base64"))), width: Math.round(box.width), height: Math.round(box.height) };
    }
  }

  const enter = async () => {
    for (const [type, extra] of [["keyDown", { text: "\r", unmodifiedText: "\r" }], ["keyUp", {}]]) {
      await session.send(
        "Input.dispatchKeyEvent",
        { type, key: "Enter", code: "Enter", windowsVirtualKeyCode: 13, nativeVirtualKeyCode: 13, ...extra },
        sessionId,
      );
    }
  };
  // Every rendering, not the first: a page with two can hide the second from the keyboard.
  const drives = [];
  for (let index = 0; index < load.length; index++) {
    const focused = await read(`(() => {
      const el = document.querySelectorAll('.unofficial-rendering')[${index}];
      const summary = el ? el.querySelector(':scope > summary') : null;
      if (!summary) return false;
      summary.focus();
      // Focus by script reaches a tabindex=-1 element that Tab never does, so the tab stop is
      // required too: a control only a script can reach is not one a keyboard reader has.
      return document.activeElement === summary && summary.tabIndex >= 0;
    })()`);
    let opened = null;
    let closed = null;
    if (focused) {
      await enter();
      opened = (await read(STATE))[index];
      await enter();
      closed = (await read(STATE))[index];
    }
    drives.push({ focused, opened, closed });
  }
  return { load, drives };
}

/**
 * The S5-A10 verdict on one measured page, as failure sentences. Pure, so the node tests can hold
 * every sentence without a browser.
 *
 * @param {string} where  the page, viewport and scheme, as every other failure names them
 * @param {object|null} measured  what `unofficialDisclosure` returned
 * @param {Array<boolean|null>} controls  the `expanded` state of every disclosure control in the
 *   accessibility tree whose name says UNOFFICIAL, at load
 */
/**
 * Enough ink to draw a word: at least 30 pixels and 3% of the label's box differ clearly from the
 * background. A word in the product's smallest type leaves hundreds; a one-pixel clip, a clip-path
 * sliver or text at an alpha of 0.01 leaves almost none.
 */
export const LABEL_INK = Object.freeze({ pixels: 30, share: 0.03 });
const painted = (ink) => (ink?.pixels ?? 0) >= LABEL_INK.pixels && (ink?.share ?? 0) >= LABEL_INK.share;

export function unofficialFailures(where, measured, controls = []) {
  const failures = [];
  const load = measured?.load ?? [];
  if (load.length === 0) {
    if (controls.length > 0) {
      failures.push(`${where}: ${controls.length} UNOFFICIAL disclosure control(s) with no rendering behind them`);
    }
    return failures;
  }
  load.forEach((one, index) => {
    const which = `unofficial rendering ${index + 1} of ${load.length}`;
    if (one.tag !== "details") {
      failures.push(
        `${where}: ${which} is a <${one.tag}>, not a closed disclosure; S5-A10 says translation is ` +
          "never the default view",
      );
    }
    if (one.open || one.visible || one.shown) {
      failures.push(
        `${where}: ${which} is shown by default (open ${one.open}, text visible ${one.visible}, ` +
          `text on screen ${one.shown}); S5-A10 says translation is never the default view`,
      );
    }
    if (!/UNOFFICIAL/.test(one.label ?? "")) {
      failures.push(
        `${where}: the control that opens ${which} does not say UNOFFICIAL ` +
          `(${JSON.stringify(one.label)})`,
      );
    } else if (!painted(one.ink)) {
      // The verdict is the pixels'. The named reason, when one applies, says why.
      failures.push(
        `${where}: the UNOFFICIAL label of ${which} is not painted: ${one.ink?.pixels ?? 0} ink ` +
          `pixel(s), ${Math.round((one.ink?.share ?? 0) * 1000) / 10}% of its ` +
          `${one.ink?.width ?? 0}x${one.ink?.height ?? 0} box, differ from the background` +
          `${one.labelHidden ? ` (${one.labelHidden})` : ""}; S5-A10 says clearly labelled unofficial`,
      );
    }
    const drive = measured.drives?.[index];
    if (!drive?.focused) {
      failures.push(`${where}: the UNOFFICIAL control of ${which} cannot take keyboard focus`);
    } else {
      if (!drive.opened?.open || !drive.opened?.shown) {
        failures.push(
          `${where}: Enter on the UNOFFICIAL control of ${which} did not show the rendering (open ` +
            `${drive.opened?.open}, text on screen ${drive.opened?.shown}); a reader who asks for it ` +
            "can never read it",
        );
      }
      if (drive.closed?.open || drive.closed?.shown) {
        failures.push(`${where}: Enter again on the UNOFFICIAL control of ${which} did not close the rendering`);
      }
    }
  });
  if (controls.length !== load.length) {
    failures.push(
      `${where}: ${load.length} unofficial rendering(s) and ${controls.length} disclosure ` +
        "control(s) named UNOFFICIAL in the accessibility tree; a screen reader cannot find one",
    );
  }
  const expanded = controls.filter((state) => state !== false).length;
  if (expanded > 0) {
    failures.push(
      `${where}: ${expanded} UNOFFICIAL disclosure control(s) are not reported collapsed to a ` +
        "screen reader at load",
    );
  }
  return failures;
}

/**
 * How much page time a settled page is run forward, on virtual time, before its network is judged.
 *
 * Watching the page for a fixed stretch of wall time only sees what the page does within that
 * stretch: a 400 ms window saw a loop polling every 150 ms and nothing slower, and a page that
 * reached out once, five seconds after load, passed every combination. Virtual time jumps to each
 * timer the page armed, holds while a request it made is in flight, and stops when this budget is
 * spent, so every timer due within the minute fires before the verdict, in well under a second.
 */
export const PAGE_CLOCK_BUDGET_MS = 60000;

/**
 * Run the page's clock forward by `budgetMs` of virtual time. The tab stays on virtual time for
 * good afterwards, so this is the last thing done on it. Returns null, or why it could not.
 */
export async function runPageClock(session, sessionId, budgetMs = PAGE_CLOCK_BUDGET_MS) {
  const expired = session.waitFor("Emulation.virtualTimeBudgetExpired", sessionId, 30000);
  // The wait's deadline can pass while the policy command is still unanswered. Handled here, so
  // that rejection is never unhandled -- which would end the whole run -- and is still awaited
  // below, where it becomes this combination's named failure.
  expired.catch(() => {});
  try {
    await session.send(
      "Emulation.setVirtualTimePolicy",
      { policy: "pauseIfNetworkFetchesPending", budget: budgetMs },
      sessionId,
    );
    await expired;
    return null;
  } catch (error) {
    return error.message;
  }
}

// The media type each kind of request has to be answered with. A missing asset under the object-URL
// grammar is answered 200 with the stand-in page, so a status check alone reads it as present; the
// type is what says a font, a script or an image was not what came back. `Other` is the browser's
// own requests (the favicon) and is not typed; any other kind this map does not name is refused,
// because a response nothing can judge is not one the gate may pass.
const EXPECTED_MEDIA = new Map([
  ["Document", /^text\/html$/],
  ["Stylesheet", /^text\/css$/],
  ["Script", /^(text|application)\/javascript$/],
  ["Font", /^font\/(woff2|woff|ttf|otf)$/],
  ["Image", /^image\//],
  ["Media", /^(audio|video)\//],
  ["TextTrack", /^text\/vtt$/],
  ["Manifest", /^application\/(manifest\+)?json$/],
  ["EventSource", /^text\/event-stream$/],
  ["Fetch", /^application\/json$/],
  ["XHR", /^application\/json$/],
]);

/**
 * The bounded-network verdict on one navigation, as failure sentences. Pure, so the node tests hold
 * every sentence without a browser.
 *
 * @param {string} where  the page, viewport and scheme, as every other failure names them
 * @param {Array<object>} events  what the browser reported: `{kind: 'request', type, url}` and
 *   `{kind: 'response', type, url, status, mime}`, in order
 * @param {number} settledAt  how many events had arrived when the page settled
 */
export function networkFailures(where, events, settledAt) {
  const failures = [];
  const pathOf = (url) => {
    try {
      const parsed = new URL(url);
      return parsed.protocol === "data:" ? "data:" : parsed.pathname;
    } catch {
      return url;
    }
  };
  // "an Image", "a Font": the sentence is matched by the induced mutations, so it is spelled right.
  const article = (word) => (/^[AEIOU]/.test(word) ? "an" : "a");
  for (const event of events) {
    if (event.kind !== "response" || pathOf(event.url) === "data:") continue;
    if (event.status >= 400) {
      failures.push(`${where}: ${article(event.type)} ${event.type} request for ${pathOf(event.url)} was answered ${event.status}`);
      continue;
    }
    if (event.type === "Other") continue;
    const expected = EXPECTED_MEDIA.get(event.type);
    if (!expected) {
      failures.push(
        `${where}: ${article(event.type)} ${event.type} request for ${pathOf(event.url)} is of a kind ` +
          "no page here makes, and the gate has no media type to judge its answer by",
      );
    } else if (!expected.test(event.mime ?? "")) {
      failures.push(
        `${where}: ${article(event.type)} ${event.type} request for ${pathOf(event.url)} was answered with ` +
          `${event.mime}; a missing or mistyped asset is not the asset`,
      );
    }
  }
  // The browser's own favicon request is not the page reaching out, whenever it lands. It is
  // excused by kind and exact address together: a script fetching the same path is still counted,
  // and so is an icon link swapped to carry a query string.
  const isFavicon = (event) => {
    if (event.type !== "Other") return false;
    try {
      const parsed = new URL(event.url);
      return parsed.pathname === "/favicon.svg" && parsed.search === "";
    } catch {
      return false;
    }
  };
  const late = events.slice(settledAt).filter((event) => event.kind === "request" && !isFavicon(event));
  if (late.length > 0) {
    failures.push(
      `${where}: ${late.length} request(s) after the page settled, during the tab walk, the ` +
        `driven actions or the minute of page time run after them: ` +
        [...new Set(late.map((event) => pathOf(event.url)))].join(", "),
    );
  }
  return failures;
}

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


/** Poll until the document is complete and its fonts have resolved. */
async function waitForSettled(session, sessionId, deadlineMs = 10000) {
  const started = Date.now();
  while (Date.now() - started < deadlineMs) {
    const { result } = await session.send(
      "Runtime.evaluate",
      {
        expression:
          'document.readyState === "complete" && document.fonts.status === "loaded"',
        returnByValue: true,
      },
      sessionId,
    );
    if (result.value === true) {
      // One frame after settling, so the first layout after font resolution is done.
      await new Promise((resolve) => setTimeout(resolve, 50));
      return;
    }
    await new Promise((resolve) => setTimeout(resolve, 25));
  }
  throw new Error("a page never reached a settled state within 10s");
}

// `client.js` was served as application/octet-stream. A classic script runs anyway, so every gate
// stayed green, but the product's own responses carry `nosniff` (BufferedHttpResponse), under which
// a browser refuses to run a script that is not typed as one. The harness now types every file it
// serves and sends `nosniff` too, so it measures the pages under the rule they will be served by.
const CONTENT_TYPES = new Map([
  [".woff2", "font/woff2"],
  [".html", "text/html; charset=utf-8"],
  [".css", "text/css; charset=utf-8"],
  [".js", "text/javascript; charset=utf-8"],
  [".json", "application/json; charset=utf-8"],
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
/**
 * The URL scheme, resolved onto the built pages.
 *
 * The static build is a preview of a routed application, so the harness has to route the
 * scheme or every real link in it is a 404. It routes exactly what `urls.mjs` defines and
 * nothing else: the three shell entries onto their generated pages, and object and search
 * paths onto one stand-in destination that says what it is. A link outside the scheme still
 * 404s, which is what makes the destination check worth running.
 *
 * Provenance no longer routes to the stand-in. It routes to the page built for that exact
 * record, because a provenance link that resolved to a proof chain belonging to a different
 * record would be worse than the 404 it replaces. A key with no page built for it still 404s,
 * which is what keeps the build honest about which records it rendered.
 *
 * Exported so a test can hold the route and the file name the build writes to one rule.
 */
export function routeSchemePath(path) {
  if (path === "/ask" || path === "/w" || path === "/dev") return `/shell-${path.slice(1)}.html`;
  if (/^\/(ask|w|dev)\/search$/.test(path)) return "/preview-destination.html";
  if (/^\/provenance\/[^/]+$/.test(path)) {
    // The same name `provenancePageName` mints in `provenance.mjs`, spelled here rather than
    // imported so this harness keeps depending on nothing it measures. A test holds the two
    // to one rule, because a disagreement between them is a 404 nothing else would find.
    return `/provenance-${path.slice("/provenance/".length).replaceAll(":", "~")}.html`;
  }
  if (parseObjectUrl(path) !== null) return "/preview-destination.html";
  return null;
}

/**
 * Is this resolved path inside the distribution root?
 *
 * Exported because the containment rule is the whole security property of this server and a
 * rule nothing can call is a rule nothing can test.
 */
export function withinRoot(root, candidate) {
  const base = resolvePath(root);
  const target = resolvePath(candidate);
  return target === base || target.startsWith(base + pathSep);
}

export async function serveDist(root) {
  const server = createServer((request, response) => {
    const refuse = () => {
      response.writeHead(404, { "content-type": "text/plain; charset=utf-8" });
      response.end("not found");
    };

    const raw = new URL(request.url, "http://127.0.0.1").pathname;
    // The URL parser normalises `..` segments, and this used to decode percent-escapes
    // afterwards, which put them back. Twelve encoded parent segments followed by an encoded
    // separator resolved to C:\Windows\win.ini and the harness served it. The server binds
    // loopback, but it is still a local file disclosure primitive living inside the tool that
    // reviews this product. Encoded separators and control bytes are refused before decoding.
    if (/%2f|%5c|%00/i.test(raw)) {
      refuse();
      return;
    }
    let path;
    try {
      path = decodeURIComponent(raw);
    } catch {
      refuse();
      return;
    }
    if (path.includes("\\") || path.includes("\0")) {
      refuse();
      return;
    }

    // A real file wins over a synthetic route. The object-URL grammar accepts any two safe
    // segments as a dossier, so /fonts/inter-400-latin.woff2 was routed to the stand-in
    // destination page and the browser received <!doctype where it expected wOF2. The
    // console reported an OTS parsing error, which is a long way from the cause.
    // Plain path join, not a file URL: fileURLToPath refuses encoded separators, and a
    // double-encoded payload still carries one after the first decode.
    const onDisk = existsSync(joinPath(root, path));
    const routed = onDisk ? null : routeSchemePath(path);
    const file = joinPath(root, routed ?? (path === "/" ? "/index.html" : path));
    // Belt and braces: whatever the two checks above let through, the resolved file still has
    // to sit inside the distribution root.
    if (!withinRoot(root, file)) {
      refuse();
      return;
    }
    // Lexical containment is not containment. `readFile` follows reparse points, so a directory
    // junction inside dist pointing outside it satisfied every string check above and still
    // served the outside file with a real 200. The final target is resolved and re-checked
    // against the resolved root before anything is opened, and a path that cannot be resolved
    // is refused rather than opened optimistically.
    Promise.all([realpath(file), realpath(root)])
      .then(([realFile, realRoot]) => {
        if (!withinRoot(realRoot, realFile)) {
          // Refused as absent, not as an error of this server's: what is outside the root is
          // nothing this site has, and an escape attempt learns nothing from the answer.
          throw Object.assign(new Error("resolved outside the distribution root"), { code: "ENOENT" });
        }
        return readFile(realFile);
      })
      .then(
        (body) => {
          response.writeHead(200, {
            "content-type": CONTENT_TYPES.get(extname(file)) ?? "application/octet-stream",
            "x-content-type-options": "nosniff",
          });
          response.end(body);
        },
        (error) => {
          // A file that is not there is 404. Any other read failure is this server's, not the
          // page's: one clean run reported a built page and its stylesheet "answered 404" while
          // both were on disk, which reads as a defect in the product. It now answers 500 and
          // names the error, so a harness failure can never be read as a missing page.
          const missing = error?.code === "ENOENT" || error?.code === "ENOTDIR";
          response.writeHead(missing ? 404 : 500, { "content-type": "text/plain; charset=utf-8" });
          response.end(missing ? "not found" : `the evidence server could not read it: ${error?.code ?? error}`);
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

/**
 * Remove profile directories a previous run left behind.
 *
 * The cleanup at the end of this run lives in a `finally`, so it survives an exception. It does
 * not survive the process being killed, and an interrupted evidence run is ordinary: 204
 * `lex-cdp-` directories had accumulated in the temp directory by the time anyone looked, on a
 * machine that then reached zero bytes free mid-write and truncated a source file to nothing.
 *
 * Sweeping at startup rather than trying harder to clean up at exit is the robust direction. It
 * costs one directory listing, and it works however the last run died, including a power cut. An
 * hour is well clear of any real run, so a concurrent one is never touched.
 */
async function sweepStaleProfiles() {
  const root = tmpdir();
  const cutoff = Date.now() - 60 * 60 * 1000;
  let removed = 0;
  const entries = await readdir(root).catch(() => []);
  for (const name of entries) {
    if (!name.startsWith("lex-cdp-")) continue;
    const full = join(root, name);
    const info = await stat(full).catch(() => null);
    if (info === null || !info.isDirectory() || info.mtimeMs > cutoff) continue;
    await rm(full, { force: true, recursive: true }).catch(() => {});
    removed += 1;
  }
  if (removed > 0) {
    console.error("  [evidence] swept " + removed + " stale browser profile(s)");
  }
}

async function main() {
  await sweepStaleProfiles();
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
    // Every combination gets a tab of its own. The bounded-network check runs the page's clock on
    // virtual time, and a page cannot leave virtual time once it is in it: "advance" does not
    // return it to the wall clock, it fast-forwards to each next timer for as long as the page
    // lives. Reusing the tab fired the next page's timers the moment it loaded, before it settled,
    // and a ten-second poll spun until the page stopped answering.
    let targetId = null;
    let sessionId = null;
    const freshTab = async () => {
      const previous = targetId;
      ({ targetId } = await session.send("Target.createTarget", { url: "about:blank" }));
      ({ sessionId } = await session.send("Target.attachToTarget", { targetId, flatten: true }));
      for (const domain of ["Accessibility", "Log", "Runtime", "Page", "Network"]) {
        await session.send(`${domain}.enable`, {}, sessionId);
      }
      if (previous) await session.send("Target.closeTarget", { targetId: previous });
    };

    let logged = [];
    // Every request and response of the current navigation, as the browser saw them. The server
    // alone cannot tell a navigation from a subresource, or what the browser asked for.
    let network = [];
    session.on((message) => {
      if (message.sessionId !== sessionId) return;
      if (message.method === "Network.requestWillBeSent") {
        network.push({ kind: "request", type: message.params.type, url: message.params.request.url });
      }
      if (message.method === "Network.responseReceived") {
        const { response, type } = message.params;
        network.push({ kind: "response", type, url: response.url, status: response.status, mime: response.mimeType });
      }
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

    for (const page of await pagesFrom(root)) {
      const url = `${site.origin}/${page}`;
      for (const viewport of WIDTHS) {
       // Forced colours is the third scheme rather than a fourth dimension, because it is a
       // rendering mode a reader is in, not an axis crossed with the other two. Windows High
       // Contrast is the common case and it removes background images, which matters here: the
       // timeline hatches its gaps with a repeating gradient, and a gap that becomes invisible
       // is the one mark on that screen whose absence asserts something false.
       for (const scheme of FAST ? ["light"] : ["light", "dark", "forced"]) {
        await freshTab();
        await session.send(
          "Emulation.setEmulatedMedia",
          {
            features:
              scheme === "forced"
                ? [
                    { name: "forced-colors", value: "active" },
                    { name: "prefers-color-scheme", value: "light" },
                    { name: "prefers-reduced-motion", value: "reduce" },
                  ]
                : [
                    { name: "prefers-color-scheme", value: scheme },
                    { name: "prefers-reduced-motion", value: "reduce" },
                  ],
          },
          sessionId,
        );
        await session.send(
          "Emulation.setDeviceMetricsOverride",
          { width: viewport.width, height: viewport.height, deviceScaleFactor: 1, mobile: viewport.label === "narrow" },
          sessionId,
        );
        logged = [];
        network = [];
        await session.send("Page.navigate", { url }, sessionId);
        // Wait for the document to be complete and its fonts resolved, rather than for a
        // fixed 250 ms. A fixed wait measures whatever has arrived: one combination reported
        // 17 px targets on a page that was 24 px in every other combination, which was the
        // stylesheet not yet applied rather than a layout defect. A harness that sometimes
        // measures an unstyled page produces both false failures and false passes.
        await waitForSettled(session, sessionId);
        // Everything the page needed has arrived by now; any request after this mark is the page
        // reaching back out on its own, which the bounded-network gate below refuses.
        const settledAt = network.length;
        const { result } = await session.send(
          "Runtime.evaluate",
          { expression: PROBE, returnByValue: true, awaitPromise: true },
          sessionId,
        );
        const observed = result.value;
        // The compare control as served, read before anything focuses into it. Chrome ignores
        // `aria-hidden` on a subtree that holds focus, and the tab walk below puts focus on the
        // button, so a control hidden from assistive technology is only visible as hidden here.
        const compareAtLoad = COMPARE_ROWS[page] ? await comparePresence(session, sessionId) : null;

        // The accessibility tree is what a screen reader actually receives. Reading the
        // DOM and asserting it "should" expose a name is a different claim.
        const { nodes } = await session.send("Accessibility.getFullAXTree", {}, sessionId);
        const named = (node) => (node.name?.value ?? "").trim().length > 0;
        const ignored = (node) => node.ignored === true;
        const axNodes = nodes.filter((node) => !ignored(node));
        const roles = axNodes.map((node) => node.role?.value).filter(Boolean);
        // Chrome reports a summary's role as `DisclosureTriangle`. The list said "disclosure
        // triangle", which no node ever matched, so a summary was never held to having a name.
        const disclosure = (node) => node.role?.value === "DisclosureTriangle";
        const interactive = axNodes.filter((node) =>
          ["link", "button", "textbox", "checkbox", "combobox"].includes(node.role?.value) ||
            disclosure(node),
        );
        const unnamedInteractive = interactive.filter((node) => !named(node));
        const headings = axNodes.filter((node) => node.role?.value === "heading");
        const unnamedHeadings = headings.filter((node) => !named(node));
        observed.axNodes = axNodes.length;
        observed.axInteractive = interactive.length;
        observed.axHeadings = headings.length;
        observed.axLandmarks = roles.filter((role) => ["main", "note", "group", "complementary"].includes(role)).length;
        // S5-A10's control as a screen reader receives it: one disclosure per unofficial rendering,
        // named UNOFFICIAL, and collapsed when the page is served.
        observed.unofficialControls = axNodes
          .filter((node) => disclosure(node) && (node.name?.value ?? "").includes("UNOFFICIAL"))
          .map((node) => node.properties?.find((property) => property.name === "expanded")?.value?.value ?? null);

        if (axNodes.length === 0) {
          failures.push(`${page} @${viewport.label}/${scheme}: the accessibility tree is empty`);
        }
        if (observed.paintOnlyCount > 0) {
          failures.push(
            `${page} @${viewport.label}/${scheme}: ${observed.paintOnlyCount} element(s) carry ` +
              `meaning only as paint, which forced colours removes: ${observed.paintOnly.join("; ")}`,
          );
        }
        if (observed.gluedCount > 0) {
          failures.push(
            `${page} @${viewport.label}/${scheme}: ${observed.gluedCount} adjacent element(s) ` +
              `painted flush against each other: ${observed.glued.join("; ")}`,
          );
        }
        if (observed.smallTargetCount > 0) {
          failures.push(
            `${page} @${viewport.label}/${scheme}: ${observed.smallTargetCount} target(s) below ` +
              `the WCAG 2.2 24 CSS px minimum: ` +
              observed.smallTargets
                .map((t) => `<${t.tag} class=${t.cls}> ${t.width}x${t.height}`)
                .join("; "),
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

        // Everything logged from here on comes from the tab walk and the driven probe below, which
        // run handlers the page load never runs. This check alone read the buffer before either of
        // them, and the next navigation clears it, so an exception thrown by a key or click handler
        // was never gated at all. `consoleAtLoad` marks the split; the second check is after the
        // driven probe.
        const consoleAtLoad = logged.length;
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
        // The policy, compared against the declared object rather than searched for a
        // substring. Asserting that the served value contains script-src self would pass
        // a policy that also contained unsafe-inline, which is the whole failure mode.
        if (observed.csp !== cspValue()) {
          failures.push(
            `${page} @${viewport.label}: served policy does not match the declared one.` +
              ` expected ${JSON.stringify(cspValue())}, got ${JSON.stringify(observed.csp)}`,
          );
        }
        for (const forbidden of FORBIDDEN_SOURCES) {
          if ((observed.csp ?? "").includes(forbidden)) {
            failures.push(`${page} @${viewport.label}: policy admits ${forbidden}`);
          }
        }
        // What the policy forbids, measured on the page rather than trusted to it. A meta
        // policy is not applied to markup the parser already saw in some browsers, so the
        // page must also not contain what the policy would have refused.
        if (observed.inlineScripts !== 0) {
          failures.push(
            `${page} @${viewport.label}: ${observed.inlineScripts} inline script tags; an` +
              " inline script cannot be reviewed by reading the deployed bundle",
          );
        }
        for (const origin of observed.scriptOrigins ?? []) {
          if (origin !== observed.documentOrigin) {
            failures.push(
              `${page} @${viewport.label}: a script is served from ${origin}, not this` +
                " origin; an answer assembled by code from elsewhere came from somewhere unstated",
            );
          }
        }
        if (observed.state === undefined) failures.push(`${page} @${viewport.label}: no data-preview-state`);
        // A page that ships a script must hydrate, and must hydrate without changing what
        // the server sent. The reader saw the server's document; if the client redraws it,
        // the text they kept is not the text that was served, and the legal content is
        // exactly what must not move between those two moments.
        if (observed.scriptCount > 0) {
          if (observed.hydrated === null) {
            failures.push(
              `${page} @${viewport.label}: ships a script and never reported hydrating; a` +
                " page with an inert runtime has shipped weight that does nothing",
            );
          } else if (observed.hydrated !== "clean") {
            failures.push(
              `${page} @${viewport.label}: hydrated as ${observed.hydrated}` +
                `${observed.hydrationRecovered ? `, ${observed.hydrationRecovered}` : ''}`,
            );
          }
        }
        for (const group of observed.rovingGroups) {
          if (group.options > 0 && group.tabbable !== 1) {
            failures.push(
              `${page} @${viewport.label}: a listbox with ${group.options} option(s) has ` +
                `${group.tabbable} tabbable; roving tabindex requires exactly one, and any other ` +
                "count is either a keyboard trap or a group the keyboard cannot enter",
            );
          }
        }
        for (const value of observed.pressedValues) {
          if (value !== "true" && value !== "false") {
            failures.push(
              `${page} @${viewport.label}: aria-pressed="${value}" is not a boolean a screen ` +
                "reader can announce; the control's state reaches sighted users only",
            );
          }
        }
        // The UX spec fixes statutory type at 17px/1.65 desktop and 16px/1.6 mobile, with a
        // 72ch maximum measure. Asserted on what rendered, because the law is the one run
        // of text on this site that is not ours to reflow at will.
        for (const [index, type] of (observed.statutoryType ?? []).entries()) {
          const narrow = viewport.width <= 480;
          const wantSize = narrow ? 16 : 17;
          const wantLead = narrow ? 1.6 : 1.65;
          const where = `${page} @${viewport.label}: statutory block ${index + 1}`;
          if (Math.abs(type.fontSize - wantSize) > 0.5) {
            failures.push(`${where} renders at ${type.fontSize}px, not ${wantSize}px`);
          }
          if (Math.abs(type.lineHeight - wantLead) > 0.05) {
            failures.push(
              `${where} has a line height of ${type.lineHeight.toFixed(2)}, not ${wantLead}`,
            );
          }
          if (!/serif/i.test(type.family)) {
            failures.push(`${where} is set in ${type.family}, which is not a serif`);
          }
          // 72ch is a maximum, and a ch at this size is roughly half the font size, so a
          // block wider than 72 times that is a measure the spec refuses.
          if (type.width > 72 * type.fontSize * 0.6) {
            failures.push(
              `${where} is ${Math.round(type.width)}px wide, past the 72ch maximum measure`,
            );
          }
        }
        // The UX spec fixes statutory type at 17px/1.65 desktop and 16px/1.6 mobile with a
        // 72ch maximum measure. Asserted on what rendered, because a stylesheet saying so
        // is a comment: a font that fails to load, a rule overridden downstream, or a
        // measure that never applies all leave the declaration intact and the page wrong.
        // The law is the one run of text on this site that is not ours to reflow at will.
        for (const [index, type] of (observed.statutoryType ?? []).entries()) {
          const narrow = viewport.width <= 480;
          const wantSize = narrow ? 16 : 17;
          const wantLead = narrow ? 1.6 : 1.65;
          const where = `${page} @${viewport.label}: statutory block ${index + 1}`;
          if (Math.abs(type.fontSize - wantSize) > 0.5) {
            failures.push(`${where} renders at ${type.fontSize}px, not ${wantSize}px`);
          }
          if (Math.abs(type.lineHeight - wantLead) > 0.05) {
            failures.push(
              `${where} leads at ${type.lineHeight.toFixed(2)}, not ${wantLead}`,
            );
          }
          if (!/Source Serif 4/.test(type.family)) {
            failures.push(`${where} is set in ${type.family}, not the specified serif`);
          }
          if (type.width > 72 * type.fontSize * 0.62) {
            failures.push(
              `${where} is ${Math.round(type.width)}px wide, past the 72ch maximum measure`,
            );
          }
        }
        if (observed.characterSet !== "UTF-8") {
          failures.push(
            `${page} @${viewport.label}: decoded as ${observed.characterSet}, not UTF-8; every ` +
              "accented character in French, German and Luxembourgish statute would be wrong",
          );
        }
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
        // DRIVEN LAST, and that ordering is load bearing. This probe focuses an option and
        // activates a filter, so it leaves the page in a different state from the one it was
        // served in. Run earlier it silently changed what every later check measured -- the tab
        // walk reported 6 stops on a page with 15 focusable elements, which was my probe's own
        // click and not a defect in the page.
        const behaviour = await drivenBehaviour(session, sessionId, COMPARE_ROWS[page] ?? null, compareAtLoad);
        // The behavioural half. The two checks above still earn their place -- they catch malformed
        // markup an inert page would also produce -- but on their own they pass a page whose
        // handlers are dead, so neither is allowed to stand as the evidence for its clause.
        if (behaviour?.roving) {
          const { before, after } = behaviour.roving;
          if (after.active === before.active) {
            failures.push(
              `${page} @${viewport.label}: ArrowDown moved focus nowhere in a listbox of ` +
                `${before.count} options; the group renders as a listbox and does not behave as one`,
            );
          } else if (after.tab !== after.active) {
            failures.push(
              `${page} @${viewport.label}: ArrowDown moved focus to option ${after.active} while ` +
                `the tab stop stayed on ${after.tab}; roving means the single tab stop follows focus`,
            );
          }
        }
        if (behaviour?.pressed) {
          const { before, after } = behaviour.pressed;
          if (after.pressed === before.pressed) {
            failures.push(
              `${page} @${viewport.label}: activating a toggle left aria-pressed at ` +
                `"${before.pressed}"; the control announces a state it never changes`,
            );
          } else if (before.represented !== null && after.represented === before.represented) {
            failures.push(
              `${page} @${viewport.label}: a toggle flipped to "${after.pressed}" while the ` +
                `state it represents stayed "${before.represented}"; a filter that announces ` +
                "itself on and filters nothing is worse than one that does neither",
            );
          }
        }
        for (const { key, expected, state } of behaviour?.keys ?? []) {
          if (!state || state.active !== expected) {
            failures.push(
              `${page} @${viewport.label}: ${key} moved focus to option ${state?.active ?? "none"}, ` +
                `expected ${expected}; the movement table names the key and the handler does not honour it`,
            );
          } else if (state.tabbable !== 1 || state.tab !== state.active) {
            failures.push(
              `${page} @${viewport.label}: after ${key}, ${state.tabbable} option(s) are tabbable and ` +
                `the tab stop is on ${state.tab} while focus is on ${state.active}; roving means one ` +
                "tab stop that follows focus",
            );
          }
        }
        if (behaviour?.shrink) {
          const { before, after } = behaviour.shrink;
          if (after && after.count < before.count && after.tabbable !== 1) {
            failures.push(
              `${page} @${viewport.label}: a filter shortened the listbox from ${before.count} to ` +
                `${after.count} options while its tab stop was on option ${before.active}, and ` +
                `${after.tabbable} options are tabbable; the listbox drops out of the Tab order`,
            );
          } else if (after && after.count < before.count) {
            // One tabbable option is not enough: it has to be the right one. A stop that jumps to the
            // first row whenever the list shortens keeps exactly one option tabbable and would pass
            // the check above, while moving the reader away from where they stood. The nearest row
            // that still exists is the stop the component promises.
            const expected = Math.min(before.active, after.count - 1);
            if (after.tab !== expected) {
              failures.push(
                `${page} @${viewport.label}: a filter shortened the listbox from ${before.count} to ` +
                  `${after.count} options while the reader stood on option ${before.active}, and the ` +
                  `tab stop moved to option ${after.tab}; it should stay on the nearest row that ` +
                  `exists, option ${expected}`,
              );
            }
          }
        }
        // Compare arming. Unlike the listbox lines above, it names the scheme like every newer
        // failure does, so a mutation expectation can hold the whole sentence.
        failures.push(...compareFailures(`${page} @${viewport.label}/${scheme}`, behaviour?.compare ?? null));
        // S5-A10, driven after the listbox probe: it opens and closes a disclosure, and pages with
        // unofficial renderings carry no listbox, so neither probe changes what the other measures.
        failures.push(...unofficialFailures(
          `${page} @${viewport.label}/${scheme}`,
          await unofficialDisclosure(session, sessionId),
          observed.unofficialControls,
        ));
        // Bounded network. The page's clock is run a minute forward on virtual time first, on every
        // combination and last on its tab, so a request the page would make on its own within that
        // minute has been made before the verdict, however slowly it polls.
        const clock = await runPageClock(session, sessionId);
        if (clock !== null) {
          failures.push(
            `${page} @${viewport.label}/${scheme}: the page's clock could not be run a minute ` +
              `forward (${clock}), so what it would request after settling is unjudged`,
          );
        }
        failures.push(...networkFailures(`${page} @${viewport.label}/${scheme}`, network, settledAt));
        row.requests = network.filter((event) => event.kind === "request").length;
        const late = logged.slice(consoleAtLoad);
        if (late.length > 0) {
          failures.push(
            `${page} @${viewport.label}/${scheme}: console output during the tab walk, driven ` +
              `actions or the minute of page time run after them ${JSON.stringify(late)}`,
          );
        }
       }
      }
    }
    // Every same-origin destination the pages offer has to resolve, and a reader following one
    // has to land somewhere coherent. This used to be a fetch and a status code, which proves a
    // file exists and nothing about what a reader arrives at: a 200 can be a blank body, a page
    // that never says what it is, or one that throws in the browser and renders half of itself.
    // So each destination is now navigated to in the same browser the rest of this run uses,
    // and the landing page has to declare its own state, carry exactly one first-level heading,
    // and log nothing.
    const destinations = new Map();
    for (const row of rows) {
      for (const href of row.sameOrigin ?? []) {
        if (!destinations.has(href)) destinations.set(href, row.page);
      }
    }
    // The last combination's tab is on virtual time; the destinations get one on the wall clock.
    await freshTab();
    for (const [href, fromPage] of destinations) {
      let status = 0;
      try {
        status = (await fetch(`${site.origin}${href}`, { redirect: "manual" })).status;
      } catch (error) {
        failures.push(`${fromPage}: ${href} could not be requested: ${error.message}`);
        continue;
      }
      if (status !== 200) {
        failures.push(
          `${fromPage}: ${href} answers ${status}; a visible action must not lead to a missing page`,
        );
        continue;
      }

      // Follow it the way a reader would. `logged` is the same per-page buffer the main loop
      // fills from the CDP listener, so clearing it here scopes it to this one navigation.
      logged = [];
      network = [];
      await session.send("Page.navigate", { url: `${site.origin}${href}` }, sessionId);
      await waitForSettled(session, sessionId);
      const { result } = await session.send(
        "Runtime.evaluate",
        {
          expression: `(() => ({
             state: document.documentElement.getAttribute('data-preview-state'),
             headings: document.querySelectorAll('h1').length,
             text: document.body.innerText.trim().length,
           }))()`,
          returnByValue: true,
          awaitPromise: true,
        },
        sessionId,
      );
      const landed = result.value;
      if (!landed?.state) {
        failures.push(
          `${fromPage}: following ${href} lands on a page that does not declare what it is; a ` +
            "reader who followed an action needs the destination to say where they arrived",
        );
      }
      if (landed && landed.headings !== 1) {
        failures.push(
          `${fromPage}: following ${href} lands on a page with ${landed.headings} first-level ` +
            "headings, so it does not name itself once",
        );
      }
      if (landed && landed.text < 40) {
        failures.push(
          `${fromPage}: following ${href} lands on ${landed.text} characters of text; a ` +
            "destination that says almost nothing is a dead end that answered 200",
        );
      }
      if (logged.length > 0) {
        failures.push(`${fromPage}: following ${href} logged ${logged.join(", ")}`);
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
        `ax=${row.axNodes} named-interactive=${row.axInteractive} landmarks=${row.axLandmarks} main=${row.axMain} glued=${row.gluedCount} inert=${row.inertControls} small=${row.smallTargetCount} requests=${row.requests}`,
    );
  }

  // A density that does not change the layout is a shell that is not a shell. The three
  // shells claim three densities, so the browser has to report three distinct computed
  // line heights on `main` at the same viewport, and the Gateway shell has to actually be
  // monospace. Nothing else in the run compares two pages against each other, and nothing
  // else could catch a density declared in the markup and absent from the stylesheet.
  const shellRows = rows.filter((row) => row.shell !== null && row.shell !== undefined);
  if (shellRows.length > 0) {
    for (const viewport of WIDTHS) {
      const atViewport = shellRows.filter((row) => row.viewport === viewport.label && row.scheme === "light");
      // One row per shell. More than one page may wear the same shell, and comparing rows
      // rather than shells reported four shells when there are three: the check itself had
      // the defect it exists to catch, counting appearances instead of kinds.
      const byShell = new Map();
      for (const row of atViewport) {
        if (!byShell.has(row.shell)) byShell.set(row.shell, row);
      }
      if (byShell.size < 2) continue;
      const heights = new Set([...byShell.values()].map((row) => row.mainLineHeight));
      if (heights.size !== byShell.size) {
        failures.push(
          `@${viewport.label}: ${byShell.size} shells but ${heights.size} distinct main ` +
            `line-height(s) (${[...heights].join(", ")}); a density that changes nothing is not a density`,
        );
      }
    }
    const gateway = shellRows.find((row) => row.shell === "dev");
    if (gateway && !/mono/i.test(gateway.mainFontFamily ?? "")) {
      failures.push(
        `the Gateway shell claims density monospace and computed ${gateway.mainFontFamily}`,
      );
    }
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
