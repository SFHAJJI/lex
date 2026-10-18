// The five legal pages every footer links (the plan's item 7.1; the specification of record's R49, R50, R62, R70, R74 and
// R92; the owner's items O5, O28 and O29): the legal notice, the privacy notice, the terms of use, the accessibility
// statement and the cookies page, in each interface language, under its path (`/en/legal-notice.html`, `/fr/…`).
//
// Their words are the chrome table's (`live-chrome.mjs`, one section per page and `legal` for what they share), so the
// French is the reviewed table's (Decision 41). What only the owner can supply, the publisher's name, address and contact
// and how long an e-mail is kept, comes from one data file (`legal-publisher.mjs`), each value a placeholder, «LIKE_THIS»,
// until it is filled. A page that still says one is not ready to publish, and the site cannot open while any does: the
// opening's check (`openingFailures`, run by `deploy-probe.mjs --opening` from `deploy/deploy.ps1 -Open` and before every
// `-Public` deployment's promotion) asks each served legal page and fails closed on a placeholder, a page it cannot read,
// or a cookie.
//
// Every statement the pages make is one the code or the deployment supports (STATUS-WEB.md lists each with its evidence):
// no cookie and nothing written to the browser's storage (the journeys, the release's image probe, the deployment probe),
// nothing kept of a request (`PublicRequestRecordingTests`, the image probe's privacy record), nothing asked of another
// origin (the CSP, `csp.mjs`), the host and its region (`deploy/main.bicep`, the environment as read back), the texts'
// licences (the rights admission policy /1, Decision 95). The pages ask nothing, carry no script and are never indexed.
//
// This module is data and pure functions only.

import { LEGAL_PUBLISHER } from "./legal-publisher.mjs";
import { INTERFACE_LANGUAGES } from "./permalinks.mjs";

/** The five pages: each one's chrome section, its file under each interface language, and its state. */
export const LEGAL_PAGES = Object.freeze([
  Object.freeze({ key: "legalNotice", file: "legal-notice.html", state: "live-legal-notice" }),
  Object.freeze({ key: "privacy", file: "privacy.html", state: "live-privacy" }),
  Object.freeze({ key: "terms", file: "terms.html", state: "live-terms" }),
  Object.freeze({ key: "accessibility", file: "accessibility.html", state: "live-accessibility" }),
  Object.freeze({ key: "cookies", file: "cookies.html", state: "live-cookies" }),
]);

/** The page of a chrome section's key, or undefined. */
export const legalPage = (key) => LEGAL_PAGES.find((page) => page.key === key);

/** Every legal page's address, in every interface language (French first, as `INTERFACE_LANGUAGES` orders them), in the footer's order. */
export const LEGAL_PAGE_PATHS = Object.freeze(INTERFACE_LANGUAGES.flatMap((language) => LEGAL_PAGES.map((page) => `/${language}/${page.file}`)));

/** The date the five texts were last changed, which each page prints ("Last updated"): changed with any of their words. */
export const LEGAL_TEXTS_UPDATED = "2026-10-18";

/**
 * The placeholders the data file holds until the owner fills it, by field: each is what the field's value is while it is
 * unfilled, and what a page prints in its place.
 */
export const PUBLISHER_PLACEHOLDERS = Object.freeze({
  publisherName: "«PUBLISHER_NAME»",
  publisherAddress: "«PUBLISHER_ADDRESS»",
  contactEmail: "«CONTACT_EMAIL»",
  reportRetentionMonths: "«REPORT_RETENTION_MONTHS»",
});

/** A placeholder as the data file writes one, wherever it stands in a page. */
export const PLACEHOLDER = /«[A-Z][A-Z0-9_]*»/g;

/** The attribute a page marks an unfilled detail with, so a check finds it even where its token was cut. */
export const PLACEHOLDER_ATTRIBUTE = "data-legal-placeholder";

/** The place that hears complaints about personal data in Luxembourg, which the privacy notice names, as a link. */
export const CNPD_SITE = Object.freeze({ href: "https://cnpd.public.lu/", text: "cnpd.public.lu" });

/** The EU texts' acknowledgement's address (Decision 95; `V3CorpusMount.EuropeTextAcknowledgement`), printed, not linked. */
export const EURLEX_SITE = "https://eur-lex.europa.eu";

/**
 * Luxembourg's accessibility law, as the accessibility statement cites it: its French title, which the page marks French
 * (`lang="fr"`) on an English page. Read from Guichet.lu's accessibility page on 2026-10-18; never from Legilux.
 */
export const ACCESSIBILITY_LAW = "loi du 28 mai 2019 relative à l’accessibilité des sites internet et des applications mobiles des organismes du secteur public";

const EMAIL = /^[^\s@<>"«»]+@[^\s@<>"«»]+\.[^\s@<>"«».]+$/;
const MONTHS = /^[1-9][0-9]?$/;

/** Whether a detail is still unfilled: its placeholder, or any text that still carries one's marks. */
export function isUnfilled(value) {
  return typeof value !== "string" || value.trim() === "" || value.includes("«") || value.includes("»");
}

/**
 * Why the publisher's details are not ready to publish, as failures (empty when they are): each field still its
 * placeholder (or empty), an e-mail address that is not one, a retention that is not a whole number of months from 1 to 99,
 * a field the pages do not know, and a field missing.
 */
export function publisherFailures(publisher = LEGAL_PUBLISHER) {
  const unfilled = Object.entries(PUBLISHER_PLACEHOLDERS).filter(([field]) => isUnfilled(publisher?.[field]))
    .map(([field, placeholder]) => `${field} is not filled in (it still says ${placeholder}, or nothing): the owner fills it in web/scripts/legal-publisher.mjs`);
  return [...unfilled, ...malformedPublisherFailures(publisher)];
}

/**
 * The failures of `publisherFailures` but the fields still unfilled: a detail filled in a form the pages cannot print, or a
 * field they do not know. The build refuses these (`build-live.mjs`), so a release never carries a detail filled wrong; an
 * unfilled one is built as its placeholder, and only the opening refuses it.
 */
export function malformedPublisherFailures(publisher = LEGAL_PUBLISHER) {
  const failures = [];
  if (publisher === null || typeof publisher !== "object") return ["the publisher's details are not an object of fields"];
  for (const field of Object.keys(publisher)) {
    if (!Object.hasOwn(PUBLISHER_PLACEHOLDERS, field)) failures.push(`${field} is no detail the legal pages print`);
  }
  for (const field of Object.keys(PUBLISHER_PLACEHOLDERS)) {
    const value = publisher[field];
    if (isUnfilled(value)) continue;
    if (value !== value.trim() || /\s{2}|[\n\r\t]/.test(value)) failures.push(`${field} ${JSON.stringify(value)} has a space at an end, or a line break, tab or double space`);
    else if (field === "contactEmail" && !EMAIL.test(value)) failures.push(`contactEmail ${JSON.stringify(value)} is not an e-mail address`);
    else if (field === "reportRetentionMonths" && !MONTHS.test(value)) failures.push(`reportRetentionMonths ${JSON.stringify(value)} is not a whole number of months, from 1 to 99`);
  }
  return failures;
}

/** The placeholders a page's text still holds, each once, in the order they first stand, and whether any detail is marked unfilled. */
export function placeholdersIn(text) {
  const found = [...new Set(String(text).match(PLACEHOLDER) ?? [])];
  return Object.freeze({ tokens: Object.freeze(found), marked: String(text).includes(PLACEHOLDER_ATTRIBUTE) });
}

/** Why one legal page's bytes are not ready to publish (empty when they are): a placeholder it still says, or marks. */
export function legalPageTextFailures(path, text) {
  const { tokens, marked } = placeholdersIn(text);
  if (tokens.length === 0 && !marked) return [];
  const said = tokens.length > 0 ? tokens.join(", ") : `a detail marked ${PLACEHOLDER_ATTRIBUTE}`;
  return [`${path} still says ${said}: the owner fills web/scripts/legal-publisher.mjs, and a release built from it is deployed, before the site opens`];
}

/**
 * Why the legal pages a release image holds are not ready to publish (empty when they are): `files` the image's live
 * files as `[path, bytes]` (`deploy-probe.mjs`, `liveFilesOf`). Each of the ten pages must be there, and say no placeholder.
 */
export function builtLegalPageFailures(files) {
  const held = new Map(files);
  return LEGAL_PAGE_PATHS.flatMap((path) => {
    const bytes = held.get(path.slice(1));
    return bytes === undefined ? [`the release holds no ${path}`] : legalPageTextFailures(path, Buffer.from(bytes).toString("utf8"));
  });
}

/**
 * The opening's check, asked of the site at `origin` before it admits everyone: each of the ten legal pages must answer
 * 200 with a page, set no cookie, and say no placeholder. It fails closed: a page it cannot ask, or that answers anything
 * else, is a failure, so a site whose pages cannot be read is not opened. `ask` is how a page is asked (`fetch` by default).
 */
export async function openingFailures(origin, { ask = fetch } = {}) {
  const failures = [];
  for (const path of LEGAL_PAGE_PATHS) {
    let answer;
    let text;
    try {
      answer = await ask(`${origin}${path}`, { redirect: "manual" });
      text = await answer.text();
    } catch (error) {
      failures.push(`${path} could not be asked (${error?.message ?? error}), so the site is not opened`);
      continue;
    }
    if (answer.status !== 200 || !(answer.headers.get("content-type") ?? "").startsWith("text/html")) {
      failures.push(`${path} answered ${answer.status} ${answer.headers.get("content-type") ?? "with no type"}, not 200 with a page`);
      continue;
    }
    if (answer.headers.get("set-cookie") !== null) failures.push(`${path} sets a cookie, which the cookies page says no page does`);
    failures.push(...legalPageTextFailures(path, text));
  }
  return failures;
}
