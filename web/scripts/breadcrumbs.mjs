// The breadcrumbs of a law's pages (the specification of record's R48: "Header, compact search, breadcrumbs (publisher, act,
// article, version) and footer"; the plan's item 6.6): where the dossier or the reading a reader is on stands in the law this
// site holds, in the record's order, said from the answer the page shows and nothing else.
//
// - The publisher: the law it publishes, named by the table (`breadcrumbs.publishers`). It has no page of its own here, so
//   it is text.
// - The act: its work key (Luxembourg) or its CELEX (EU), as the page's own headings name it, set as code; a link to its
//   dossier at its address, except on the dossier itself, where it is the page.
// - The article: on a reading whose address names an article the version shown holds, the publisher's id of it, as the
//   reading's own heading of that article prints it; never guessed from the text.
// - The version: on a reading that shows one version of the act (the one its permalink pins, or the only one shown), that
//   version said by its date's role (R21): a Luxembourg state applying from its date, or the act as published dated by its
//   entry into force (never said as applying from it, `date-role.mjs`); an EU wording as the original or the consolidated
//   wording of its date (never a Luxembourg word: the launch contract's "never merged").
//
// The crumb that is the page itself is marked current (`aria-current="page"`) and is no link: the act on a dossier, the
// version on a reading. A reading of several versions (two that cover the date, or one per language) names no version, so no
// crumb is the page; a page with no answer has no breadcrumbs.
//
// Data and pure functions only: the screens and the tests share them.

import { CHROME_LOCALE, fillText, liveChrome } from './live-chrome.mjs';
import { europeWorkSegment, permalinkHref } from './permalinks.mjs';
import { datedByEntryIntoForce } from './date-role.mjs';

/** The parts a crumb can be, in the record's order (R48). */
export const CRUMB_PARTS = Object.freeze(['publisher', 'act', 'article', 'version']);

const isEurope = (view) => view.publisher === 'eu-eurlex';

/** An act's address with no interface language, as the API mints a dossier's (`/lu-legilux/{work}`, `/eu-eurlex/{celex}`). */
function actAddress(view) {
  return isEurope(view) ? `/eu-eurlex/${europeWorkSegment(view.celex)}` : `/lu-legilux/${view.workKey}`;
}

/** The act's name on the page: its work key or its CELEX, as the page's headings name it; null when the view names none. */
function actName(view) {
  const name = isEurope(view) ? view.celex : view.workKey;
  return typeof name === 'string' && name.length > 0 ? name : null;
}

function crumb(part, text, { href = null, code = false, current = false } = {}) {
  return Object.freeze({ part, text, href: current ? null : href, code, current });
}

function publisherCrumb(view, copy) {
  return crumb('publisher', copy.publishers[isEurope(view) ? 'eu-eurlex' : 'lu-legilux']);
}

/** A version said by its date's role, in the table's words (`breadcrumbs.state` and the rest). */
export function versionWords(view, version, copy = liveChrome().breadcrumbs) {
  if (isEurope(view)) {
    return fillText(version.kind === 'consolidated_version' ? copy.europeConsolidated : copy.europeOriginal, { date: version.wordingDate });
  }
  return fillText(datedByEntryIntoForce(version) ? copy.stateEntryIntoForce : copy.state, { date: version.applicabilityDate });
}

/**
 * A dossier's breadcrumbs, from its view (`readDossier`, or the EU dossier's): the publisher, and the act, which is the
 * page. None for a view that names no act.
 */
export function dossierCrumbs(view, { locale = CHROME_LOCALE } = {}) {
  if (view === null || typeof view !== 'object' || actName(view) === null) return Object.freeze([]);
  const copy = liveChrome(locale).breadcrumbs;
  return Object.freeze([publisherCrumb(view, copy), crumb('act', actName(view), { code: true, current: true })]);
}

/**
 * A reading's breadcrumbs, from its view (`readEvidenceBundle` or `readEuropeEvidenceBundle`): the publisher, the act (a link
 * to its dossier), the article the address names when the version shown holds it (`article`, the publisher's id, or null),
 * and the version, which is the page: the one `permalink` pins when the view shows it, else the only version shown, else
 * none.
 */
export function readingCrumbs(view, { permalink = null, article = null, locale = CHROME_LOCALE } = {}) {
  if (view === null || typeof view !== 'object' || actName(view) === null) return Object.freeze([]);
  const copy = liveChrome(locale).breadcrumbs;
  const versions = (isEurope(view) ? view.wordings : view.states) ?? [];
  const pinned = permalink === null ? undefined : versions.find((version) => version.permalink === permalink);
  const version = pinned ?? (versions.length === 1 ? versions[0] : undefined);
  const crumbs = [
    publisherCrumb(view, copy),
    crumb('act', actName(view), { href: permalinkHref(actAddress(view), locale), code: true }),
  ];
  const held = version !== undefined && article !== null
    && [...(version.articles ?? []), ...(version.articlesWithoutText ?? [])].some((one) => one.publisherId === article);
  if (held) crumbs.push(crumb('article', article, { code: true }));
  if (version !== undefined) crumbs.push(crumb('version', versionWords(view, version, copy), { current: true }));
  return Object.freeze(crumbs);
}
