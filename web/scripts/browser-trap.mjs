// A trap for every way this repository's scripts start a browser, for a machine where none may run: the driver's, since
// the owner's order of 2026-10-16 (headless Chrome runs from tests there made 263 failed Windows sign-ins and locked the
// owner's account). Installed before anything imports `browser-evidence.mjs`, it puts traps in place of that module's
// launcher, `findBrowser` and `launchBrowser` (every other export is the real one), and wraps `node:child_process`, so a
// process whose command or script names a browser, or a script that walks pages in one (`browser-evidence.mjs`,
// `journey.mjs`, `evidence-mutations.mjs`), is never started; `strict` traps every process start. A touch is recorded and
// stops its caller before anything is started: it throws. Installed with `exitCode`, it also says so on standard error,
// and the process then ends with that code whatever else it would end with, once its own clean-up has run (a release's
// container and work directory are removed as on any failure), even if something caught the error.
//
//   LEX_BROWSER_TRAP=exit node --import ./scripts/browser-trap.mjs scripts/image-rehearsal.mjs ... --no-browser
//     a run with the trap loaded first: a touch fails it with `TRAP_EXIT_CODE` (97).
//   installBrowserTrap({ strict: true }) in a test, before it imports any script, then takeTouches().
//
// The launcher is replaced through the in-thread module hooks (`module.registerHooks`, Node 22.15 or later); a Node
// without them cannot be trapped, and installing then throws, so nothing runs untrapped.

// The namespace, not named imports: a Node without `registerHooks` must reach the refusal below, not fail to link.
import * as nodeModule from "node:module";

/** The exit code of a run the trap ended (`LEX_BROWSER_TRAP=exit`). */
export const TRAP_EXIT_CODE = 97;

/** The query that names the real launcher behind the trap. */
const REAL = "lex-browser-trap-real";
const LAUNCHER = /\/scripts\/browser-evidence\.mjs$/i;
const BROWSER_COMMAND = /(?:^|[\\/\s"'])(?:chrome|chromium|chromium-browser|google-chrome(?:-stable)?|msedge|microsoft[- ]edge(?:-stable)?)(?:\.exe)?(?=$|[\s"'])/i;
const PAGE_WALKER = /(?:^|[\\/\s"'])(?:browser-evidence|journey|evidence-mutations)\.mjs(?=$|[\s"'])/i;
const SPAWNERS = Object.freeze(["spawn", "spawnSync", "execFile", "execFileSync", "exec", "execSync", "fork"]);

const touches = [];
let installed = null;
let exitArmed = false;

/** Every touch since the last call, as `what it was asked`, and clears them. */
export function takeTouches() {
  return touches.splice(0, touches.length);
}

/**
 * Records a touch and stops its caller by throwing. Installed with an exit code, it says so on standard error and arms the
 * process to end with that code: an `exit` listener sets it last, so neither the run's own exit code nor a caller that
 * caught the error can end the run as anything else.
 */
export function browserTrapTouched(what) {
  const touch = String(what).slice(0, 300);
  touches.push(touch);
  if (installed?.exitCode !== undefined && installed?.exitCode !== null) {
    process.stderr.write(`the browser trap was touched: ${touch}; nothing was started, and the run ends with ${installed.exitCode}\n`);
    if (!exitArmed) {
      exitArmed = true;
      process.on("exit", () => { process.exitCode = installed.exitCode; });
    }
  }
  throw new Error(`the browser trap was touched: ${touch}; nothing was started`);
}

/** The launcher as the trap serves it: the real module's every export, but its two that find and start a browser. */
function trapSource(realUrl) {
  return [
    `export * from ${JSON.stringify(realUrl)};`,
    `import { browserTrapTouched } from ${JSON.stringify(import.meta.url)};`,
    "export async function findBrowser(...args) { return browserTrapTouched(`findBrowser(${args.join(', ')})`); }",
    "export async function launchBrowser(...args) { return browserTrapTouched(`launchBrowser(${args.map(String).join(', ')})`); }",
    "findBrowser.lexBrowserTrap = true;",
    "launchBrowser.lexBrowserTrap = true;",
  ].join("\n");
}

/**
 * Whether a process start, as `node:child_process` is asked it (`[command, args?, options?]`), names a browser or a page
 * walker: its command, or any of its arguments, is a browser's executable (Chrome, Chromium, Edge, or `LEX_BROWSER`'s), or
 * one of the scripts that walk pages in one. `strict`: every start.
 */
export function startsBrowser(args, { strict = false } = {}) {
  if (strict) return true;
  const [command, second] = args;
  const words = [String(command ?? ""), ...(Array.isArray(second) ? second.map(String) : [])];
  const configured = process.env.LEX_BROWSER;
  return words.some((word) => BROWSER_COMMAND.test(word) || PAGE_WALKER.test(word) || (configured && word.includes(configured)));
}

/**
 * Installs the trap, once per process: the launcher's two functions replaced for every later import of it, and every
 * process start of `node:child_process` checked first (`strict`: every one refused). Answers `{ strict, exitCode }`.
 */
export function installBrowserTrap({ strict = false, exitCode = null } = {}) {
  if (installed !== null) {
    if (installed.strict !== strict || installed.exitCode !== exitCode) throw new Error("the browser trap is installed already, otherwise");
    return installed;
  }
  if (typeof nodeModule.registerHooks !== "function") {
    throw new Error("this Node has no module.registerHooks (22.15 or later), so the browser trap cannot replace the launcher; nothing is run");
  }
  nodeModule.registerHooks({
    load(url, context, nextLoad) {
      const parsed = new URL(url);
      if (parsed.protocol === "file:" && LAUNCHER.test(parsed.pathname) && !parsed.searchParams.has(REAL)) {
        parsed.searchParams.set(REAL, "1");
        return { format: "module", shortCircuit: true, source: trapSource(parsed.href) };
      }
      return nextLoad(url, context);
    },
  });
  const childProcess = nodeModule.createRequire(import.meta.url)("node:child_process");
  for (const name of SPAWNERS) {
    const original = childProcess[name];
    const trapped = function (...args) {
      if (startsBrowser(args, { strict })) return browserTrapTouched(`${name}(${String(args[0])}${Array.isArray(args[1]) ? `, ${args[1].join(" ")}` : ""})`);
      return original.apply(this, args);
    };
    trapped.lexBrowserTrap = true;
    childProcess[name] = trapped;
  }
  nodeModule.syncBuiltinESMExports();
  installed = Object.freeze({ strict, exitCode });
  // A run whose own script walks pages in a browser is refused before it starts: `browser-evidence.mjs` run directly would
  // call its own launcher, behind the trap's (whose process check would still refuse the browser's start).
  if (PAGE_WALKER.test(process.argv[1] ?? "")) browserTrapTouched(`the script ${process.argv[1]}`);
  return installed;
}

// Loaded first by `--import` for a run: installed with the exit code, so a touch fails the run with it.
if (process.env.LEX_BROWSER_TRAP === "exit") installBrowserTrap({ exitCode: TRAP_EXIT_CODE });
