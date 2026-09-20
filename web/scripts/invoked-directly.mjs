import { realpathSync } from "node:fs";
import { fileURLToPath, pathToFileURL } from "node:url";

/**
 * Whether this module is the script node was asked to run.
 *
 * Both sides are resolved to a real path before they are compared. `import.meta.url` is already
 * resolved through junctions and symlinks by the loader; `process.argv[1]` keeps whatever spelling
 * the caller typed. Comparing them unresolved makes a checkout reached through a junction — the
 * ordinary shape of a second working copy on Windows — a script that runs nothing, prints nothing
 * and exits 0, which is exactly what a passing run looks like to a caller reading an exit code.
 *
 * Each real path is then put back through `pathToFileURL`, which is what normalises the drive
 * letter's case and the separators, so a caller who types a lower-case drive or backslashes still
 * matches.
 *
 * A path that cannot be resolved is not this module: `realpathSync` throws for a path that is not
 * there, and a guard is the wrong place to raise it.
 *
 * @param {string} moduleUrl  the calling module's `import.meta.url`
 * @param {string|undefined} argv1  `process.argv[1]`, absent when node was given no script
 */
export function invokedDirectly(moduleUrl, argv1) {
  if (!argv1) return false;
  try {
    return real(fileURLToPath(moduleUrl)) === real(argv1);
  } catch {
    return false;
  }
}

const real = (path) => pathToFileURL(realpathSync(path)).href;
