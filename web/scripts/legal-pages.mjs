// The five legal pages every footer links (the plan's item 7.1; the specification of record's R49, R50, R62 and R70, from
// which the owner's decision of 2026-10-19 departs, below): the legal notice, the privacy notice, the terms of use, the
// accessibility statement and the cookies page, in each interface language, under its path (`/en/legal-notice.html`,
// `/fr/…`).
//
// Their words are the chrome table's (`live-chrome.mjs`, one section per page and `legal` for what they share), so the
// French is the reviewed table's (Decision 41). The owner decided on 2026-10-19 that the site names no person and publishes
// no postal address, no e-mail address and no contact channel of any kind ("i dont want to mention nobody name etc etc
// including me"; "the user is there to have temporal feature over lux and eu laws thats it"; "why r we mentioning emails
// andadress"), departing from R49 (an editor, an address, an e-mail, a controller and its contact, an objection contact)
// and from O5's recommended answer. So the pages say what Lex is, where its texts come from and on what licences, who hosts
// it, what it keeps of a reader (nothing), how accessible it is and that it sets no cookie, and nothing else: there is no
// detail for anyone to fill in, and nothing waits for one. The privacy notice keeps the reader's right to complain to the
// CNPD, the regulator, named with a link to its site and no postal address (`CNPD_SITE`; the driver's decision of
// 2026-10-19): a right of the reader's, not a contact of Lex's.
//
// The opening's check (`openingFailures`, run by `deploy-probe.mjs --opening` from `deploy/deploy.ps1 -Open`) and every
// `-Public` deployment's probe of its candidate (`builtLegalPageFailures`) keep that decision on the served site: each
// legal page must answer, set no cookie and show nothing of `SHOWN_NOWHERE` (an e-mail address, a mail or telephone link, a
// placeholder a template left, a Luxembourg postcode). The check fails closed: a page it cannot read keeps the site private.
//
// Every statement the pages make is one the code or the deployment supports (STATUS-WEB.md lists each with its evidence):
// no cookie and nothing written to the browser's storage (the journeys, the release's image probe, the deployment probe),
// nothing kept of a request (`PublicRequestRecordingTests`, the image probe's privacy record, the API's cleared logging),
// nothing asked of another origin (the CSP, `csp.mjs`), a search's words in a request's body and never in its address
// (`v3-client.mjs`, `search-address.mjs`), the host and its region (`deploy/main.bicep`, the environment as read back), the
// texts' licences (the rights admission policy /1, Decision 95). The pages ask nothing, carry no script and are never
// indexed.
//
// This module is data and pure functions only.

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
export const LEGAL_TEXTS_UPDATED = "2026-10-19";

/**
 * The supervisory authority a reader may complain to (GDPR art. 77), which the privacy notice names, as a link to its site
 * and with no postal address: the reader's right, not a contact of Lex's (the driver's decision of 2026-10-19).
 */
export const CNPD_SITE = Object.freeze({ href: "https://cnpd.public.lu/", text: "cnpd.public.lu" });

/** The EU texts' acknowledgement's address (Decision 95; `V3CorpusMount.EuropeTextAcknowledgement`), printed, not linked. */
export const EURLEX_SITE = "https://eur-lex.europa.eu";

/**
 * The acknowledgement EU texts carry on this site, as the platform serves it with them (`EuropeTextAcknowledgement`), in
 * English on either interface: the legal notice quotes it as it is shown, marked English on a French page (the French
 * review).
 */
export const EURLEX_ACKNOWLEDGEMENT = `© European Union, ${EURLEX_SITE}`;

/**
 * Luxembourg's accessibility law, as the accessibility statement cites it: its French title, which the page marks French
 * (`lang="fr"`) on an English page. Read from Guichet.lu's accessibility page on 2026-10-18; never from Legilux.
 */
export const ACCESSIBILITY_LAW = "loi du 28 mai 2019 relative à l’accessibilité des sites internet et des applications mobiles des organismes du secteur public";

/**
 * What no legal page may show, by the owner's decision of 2026-10-19 (no person, no postal address, no e-mail address, no
 * contact channel): each with what it is. Each is a mark only a person's details or a contact leave in a page's bytes, so
 * the check reads the served bytes as they are, markup and all, and needs no judgement of words; the words themselves
 * (a contact, an address to write to) are held at build time, in both languages (`legal-pages.test.mjs`).
 */
export const SHOWN_NOWHERE = Object.freeze([
  Object.freeze({ what: "an e-mail address", pattern: /[^\s@<>"'()]+@[^\s@<>"'()]+\.[A-Za-z]{2,}/ }),
  Object.freeze({ what: "a mail or telephone link", pattern: /\b(?:mailto|tel):/i }),
  Object.freeze({ what: "a placeholder a template left («LIKE_THIS»)", pattern: /«[A-Z][A-Z0-9_]*»/ }),
  Object.freeze({ what: "a Luxembourg postcode, as a postal address writes it", pattern: /\bL-\d{4}\b/ }),
]);

/** What one legal page's text shows of `SHOWN_NOWHERE`, as the kinds it shows, each once, in that list's order. */
export function shownOnPage(text) {
  return SHOWN_NOWHERE.filter(({ pattern }) => pattern.test(String(text))).map(({ what }) => what);
}

/** Why one legal page's bytes are not ready to publish (empty when they are): what it shows that the decision keeps off the site. */
export function legalPageTextFailures(path, text) {
  const shown = shownOnPage(text);
  return shown.length === 0 ? [] : [`${path} shows ${shown.join(", ")}, which the owner's decision of 2026-10-19 keeps off the site (no person, no postal address, no e-mail address, no contact)`];
}

/**
 * Why the legal pages a release image holds are not ready to publish (empty when they are): `files` the image's live
 * files as `[path, bytes]` (`deploy-probe.mjs`, `liveFilesOf`). Each of the ten pages must be there, and show nothing of
 * `SHOWN_NOWHERE`.
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
 * 200 with a page, set no cookie, and show nothing of `SHOWN_NOWHERE`. It fails closed: a page it cannot ask, or that
 * answers anything else, is a failure, so a site whose pages cannot be read is not opened. `ask` is how a page is asked
 * (`fetch` by default).
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
