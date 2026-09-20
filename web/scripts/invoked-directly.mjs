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
 * The resolution asks the operating system for the canonical name (`realpathSync.native`), which is
 * what settles the drive letter's case. `pathToFileURL` does **not**: it preserves whatever case it
 * is given, so `c:\…` and `C:\…` produce different hrefs, and comparing them made `node c:\…\x.mjs`
 * a script that ran nothing and exited 0 — the defect this helper exists to prevent, through a
 * second door. `pathToFileURL` is kept for the separators and the percent-encoding, after the
 * canonical name has been obtained. Some hosts have no native resolver, so the plain one is the
 * fallback and the comparison is then as good as the spelling the caller typed.
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

const real = (path) => pathToFileURL(canonical(path)).href;

const canonical = (path) => {
  try {
    return realpathSync.native(path);
  } catch (error) {
    // A host without a native resolver, not a path that is not there: that one is the caller's
    // answer and belongs to `invokedDirectly`'s own catch.
    if (error?.code === "ENOENT" || error?.code === "ENOTDIR") throw error;
    return realpathSync(path);
  }
};
