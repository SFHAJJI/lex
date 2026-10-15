// The site's answer to a page request nothing claims (the specification of record's R60: "A 404 page offers search"), for
// the live server (`serve-live.mjs`); the API holds the same rule (`V3WebRoot.NotFound.cs`), and a test holds the two to one
// table (`tests/Lex.V3.Tests/Fixtures/SitePaths/not-found.json`).
//
// Which requests are a page's is a rule on the method and the path alone, never on a header (the API reads one header,
// Accept-Language, and only to choose a language; S4-A11): a GET or a HEAD whose path is not a machine's. A machine's path
// is under `/api/`, `/mcp` or `/health/`, and keeps its JSON problem. The page is in the reader's language: the interface
// language the path stands under (`/fr/…`, `/en/…`), else the one the root's redirect chooses (`negotiateLanguage`: the
// browser's preference of French and English, else French), in which case the answer depends on Accept-Language and says so
// (`Vary`).
//
// The build writes one 404 page per interface language under `not-found/`, and one maintenance page per language under
// `maintenance/` for the deploy kit; neither server serves either at its own address (`UNSERVED_DIRECTORIES`).

import { INTERFACE_LANGUAGES, negotiateLanguage } from "./permalinks.mjs";

/** The paths of machines: the API, the MCP endpoint and the health routes. */
export const MACHINE_PATH_PREFIXES = Object.freeze(["/api/", "/mcp", "/health/"]);

/** Where the build writes the 404 pages and the maintenance pages, one per interface language, `{language}.html`. */
export const NOT_FOUND_DIRECTORY = "not-found";
export const MAINTENANCE_DIRECTORY = "maintenance";

/** The build's directories neither server serves at their own address: the server renderers, the 404 and maintenance pages. */
export const UNSERVED_DIRECTORIES = Object.freeze(["ssr", NOT_FOUND_DIRECTORY, MAINTENANCE_DIRECTORY]);

/** Whether a request is a page's: a GET or a HEAD whose path (with its leading slash) is not a machine's. */
export function isPageRequest(method, pathname) {
  if (method !== "GET" && method !== "HEAD") return false;
  if (typeof pathname !== "string" || !pathname.startsWith("/")) return false;
  return !MACHINE_PATH_PREFIXES.some((prefix) => pathname.startsWith(prefix));
}

/** The interface language a path stands under (`/fr/…`, `/en/…`), or null. */
export function pathLanguage(pathname) {
  return INTERFACE_LANGUAGES.find((language) => typeof pathname === "string" && pathname.startsWith(`/${language}/`)) ?? null;
}

/**
 * The 404 page a request nothing claims is answered with, or null when it is not a page request: `{ page, negotiated }`,
 * `page` the built file (`not-found/fr.html`), `negotiated` whether the language came from Accept-Language rather than the
 * path.
 */
export function notFoundPageFor(method, pathname, acceptLanguage) {
  if (!isPageRequest(method, pathname)) return null;
  const language = pathLanguage(pathname);
  return Object.freeze({
    page: `${NOT_FOUND_DIRECTORY}/${language ?? negotiateLanguage(acceptLanguage)}.html`,
    negotiated: language === null,
  });
}
