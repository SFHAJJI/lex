// A search in the page's own address, after "#" (the specification of record's R109; the plan's item 3.5): "The site keeps
// search state in the URL fragment, so a search can be linked, and sends the query in a request body."
//
// The fragment is the browser's: no server receives it (a browser sends neither it nor, under this site's
// `Referrer-Policy: no-referrer`, any referrer), so the words a reader searched for stay out of every address a server could
// record, while a link to the page carries the search. Both servers render the search page and the home page without it;
// the page, once hydrated, reads it and asks that search, as the reader would (`LiveSearch.jsx`). The words still travel
// to the API only in the request's body (`v3-client.mjs`), never in a query string.
//
//   #q={phrase}&lang={language}[&work={identifier}][&date={yyyy-mm-dd}]
//
// The phrase as typed (never trimmed or folded: the platform matches it byte for byte), the language chosen, the work
// identifier when the reader named one (the search page's form; the home page's names none) and the date when the date
// field held one (the home page's starts at today in Luxembourg, R13, so a link keeps the date the search was asked at).
// Each value is percent-encoded as `encodeURIComponent` encodes it, the keys in that order. The lane is not written: the
// site asks the strict lane alone (`SEARCH_MODE`). A later page's cursor is not written: a link opens the first page.
//
// Read back, a fragment is one of three things (`readSearchFragment`):
// - `none`: no fragment, or one that holds no `=` (a position, such as the skip link's `#main`): no search is asked, and
//   nothing is said;
// - `search`: a search the form would send (`searchParameters`): the page fills its form with it and asks it;
// - `refused`: a fragment written as a search that this page will not send, for any reason (a key it does not know, a key
//   twice, a value that does not decode, an empty value, no phrase, a language the form does not offer, a date that is not
//   a calendar date, a work named on the home page, a phrase past the platform's ceilings, a fragment past this file's own
//   ceiling): nothing is asked, the form keeps its own values, and the page says that the address's search could not be
//   read, repeating nothing of it.

import { SEARCH_LANGUAGES, searchParameters } from "./live-search.mjs";

/** The keys a search's fragment holds, in the order they are written. */
export const SEARCH_FRAGMENT_KEYS = Object.freeze(["q", "lang", "work", "date"]);

/**
 * The longest fragment read as a search, in characters: a phrase at the platform's ceiling of 512 characters, each written
 * as up to twelve (a character outside the Basic Multilingual Plane is two UTF-16 units and four UTF-8 bytes, each `%XX`),
 * with room for a work identifier; anything longer is refused before any of it is decoded.
 */
export const SEARCH_FRAGMENT_MAX = 8192;

const encode = (value) => encodeURIComponent(value);

/**
 * The fragment that holds a search (`{ query, language, identifier, date }`, as the form holds it), with its "#": only a
 * search the form would send (`searchParameters` throws otherwise), the work and the date only when given, and with
 * `work: false` (the home page's form, which names no work) a search that names one is refused too.
 */
export function searchFragment({ query, language, identifier = "", date = "" }, { work = true } = {}) {
  searchParameters({ query, language, identifier, date });
  const named = identifier.trim().length > 0;
  if (named && !work) throw new Error("this form searches no named work");
  const parts = [`q=${encode(query)}`, `lang=${encode(language)}`];
  if (named) parts.push(`work=${encode(identifier)}`);
  if (date.trim().length > 0) parts.push(`date=${encode(date)}`);
  const fragment = `#${parts.join("&")}`;
  if (fragment.length > SEARCH_FRAGMENT_MAX) throw new Error(`a search's address is at most ${SEARCH_FRAGMENT_MAX} characters after "#"`);
  return fragment;
}

const refused = (reason) => Object.freeze({ state: "refused", reason });
const NONE = Object.freeze({ state: "none" });

/**
 * What a page's fragment (`location.hash`, with its "#", or empty) holds: `{ state: "none" }`, `{ state: "search", request }`
 * (`request` as the form holds it: `{ query, language, identifier, date }`, the last two "" when not named), or
 * `{ state: "refused", reason }` (the reason in English, for tests and the console of no one: the page never prints it).
 * `work: false` for the home page's form, which refuses a search that names a work.
 */
export function readSearchFragment(hash, { work = true } = {}) {
  if (typeof hash !== "string" || hash.length === 0 || hash === "#") return NONE;
  if (hash[0] !== "#") return refused("a fragment begins with #");
  const body = hash.slice(1);
  if (!body.includes("=")) return NONE;
  if (hash.length > SEARCH_FRAGMENT_MAX) return refused(`the fragment is longer than ${SEARCH_FRAGMENT_MAX} characters`);
  const values = new Map();
  for (const part of body.split("&")) {
    const equals = part.indexOf("=");
    if (equals < 0) return refused(`${JSON.stringify(part.slice(0, 20))} is no key and value`);
    const key = part.slice(0, equals);
    if (!SEARCH_FRAGMENT_KEYS.includes(key)) return refused(`${JSON.stringify(key.slice(0, 20))} is not a key of a search`);
    if (key === "work" && !work) return refused("this form searches no named work");
    if (values.has(key)) return refused(`${key} is named twice`);
    let value;
    try {
      value = decodeURIComponent(part.slice(equals + 1));
    } catch {
      return refused(`${key}'s value is not percent-encoded UTF-8`);
    }
    if (value.length === 0) return refused(`${key} is empty`);
    values.set(key, value);
  }
  if (!values.has("q")) return refused("a search names its phrase (q)");
  const identifier = values.get("work") ?? "";
  if (values.has("work") && identifier.trim().length === 0) return refused("a work is named by its identifier");
  const request = Object.freeze({
    query: values.get("q"),
    language: values.get("lang") ?? SEARCH_LANGUAGES[0].code,
    identifier,
    date: values.get("date") ?? "",
  });
  try {
    searchParameters(request);
  } catch (error) {
    return refused(error.message);
  }
  return Object.freeze({ state: "search", request });
}
