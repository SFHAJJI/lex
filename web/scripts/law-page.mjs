// The law page (the owner's order of 2026-11-13, section B): one page per act, at its readable address, read from its
// dossier. This file decides what the page says and derives nothing the answer does not hold: the act's own title, its
// status today, its versions in one language in date order, the version a date falls in, the two versions compared by
// default, and an article's citation. `LawPage.jsx` lays it out; the comparison is the `diff` operation's, read by
// `compare-answer.mjs`, and an article's history is `article_history`'s, read by `history-answer.mjs`.

import { datedByEntryIntoForce } from './date-role.mjs';

/**
 * The publisher's prefix of a consolidated version's title: "Version consolidée applicable au 25/12/2006 : Loi du …". It names
 * the version, not the act, so the act's heading never shows it (the live dossier did on 2026-11-13).
 */
const VERSION_TITLE_PREFIX = /^\s*(?:version\s+consolid[ée]e|texte\s+coordonn[ée])(?=[\s:])[^:]{0,80}:\s*/iu;

/** An act's title without the publisher's version prefix; the title itself when it has none. */
export function actTitleOf(title) {
  if (typeof title !== 'string') return null;
  const stripped = title.replace(VERSION_TITLE_PREFIX, '').trim();
  return stripped.length > 0 ? stripped : title.trim();
}

/** The act's versions in one language, oldest first: the language of its latest version, else the first held. */
export function versionsOf(view) {
  if (view.states.length === 0) return [];
  const latest = view.states.reduce((best, state) => (best === null || state.applicabilityDate > best.applicabilityDate ? state : best), null);
  return view.states
    .filter((state) => state.language === latest.language)
    .slice()
    .sort((a, b) => (a.applicabilityDate < b.applicabilityDate ? -1 : a.applicabilityDate > b.applicabilityDate ? 1 : 0));
}

/**
 * The act's own title, in the language of its latest version: the title of that version's expression, its version prefix
 * removed; else any title the dossier holds; else null. With its language, so the page marks it as statute text.
 */
export function actTitle(view) {
  const versions = versionsOf(view);
  const latest = versions.at(-1) ?? null;
  const groups = view.titles.filter((group) => group.titles.length > 0);
  const own = latest === null ? null : groups.find((group) => group.expressionIri === latest.expressionIri) ?? null;
  const group = own ?? groups.find((candidate) => candidate.language === latest?.language) ?? groups[0] ?? null;
  if (group === null) return null;
  return Object.freeze({ title: actTitleOf(group.titles[0].title), language: group.language });
}

/**
 * The act's status today (`today`, the date in Luxembourg the answer was given on), from the version that applies today, never
 * from the latest alone (the review of #1046: a later version dated after today does not make the act "not yet in force"
 * while an earlier one applies): `in_force` when a held version applies today, `not_yet` when its first version applies after
 * today; null otherwise (no version applies today and one applied before, so its end is not this page's to name), when no
 * today is named, or when a version is dated by its entry into force (no applicability date stated: no status is claimed, as
 * on the dossier's versions).
 */
export function lawStatus(view, today) {
  const versions = versionsOf(view);
  if (versions.length === 0 || today === null || today === undefined || versions.some(datedByEntryIntoForce)) return null;
  if (versionOn(versions, today) >= 0) return 'in_force';
  return versions[0].applicabilityDate > today ? 'not_yet' : null;
}

/** The index of the version that applies on `date` (from on or before it, the next from after it), or -1. */
export function versionOn(versions, date) {
  if (typeof date !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(date)) return -1;
  for (let index = versions.length - 1; index >= 0; index -= 1) {
    const version = versions[index];
    if (version.applicabilityDate <= date && (version.nextApplicabilityDate === null || date < version.nextApplicabilityDate)) return index;
  }
  return -1;
}

/**
 * The date of a claim as a reader types it: day first (25/11/2025, 25.11.2025, 25-11-2025, 1/2/2020), or as a calendar date
 * (2025-11-25); the calendar date it names, or null for anything else, a day that does not exist included.
 */
export function readClaimDate(text) {
  const typed = typeof text === 'string' ? text.trim() : '';
  let year;
  let month;
  let day;
  const iso = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(typed);
  const dayFirst = /^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})$/.exec(typed);
  if (iso !== null) [, year, month, day] = iso;
  else if (dayFirst !== null) [, day, month, year] = dayFirst;
  else return null;
  const date = new Date(Date.UTC(Number(year), Number(month) - 1, Number(day)));
  if (date.getUTCFullYear() !== Number(year) || date.getUTCMonth() !== Number(month) - 1 || date.getUTCDate() !== Number(day)) return null;
  return `${year}-${month.padStart(2, '0')}-${day.padStart(2, '0')}`;
}

/** The two versions compared when the page opens: the one before the latest, and the latest; null for one version. */
export function defaultPair(versions) {
  return versions.length < 2 ? null : Object.freeze([versions.length - 2, versions.length - 1]);
}

/**
 * What a click on a version does (one click reads it, a second compares the two): from `{ read, other }` (indexes, `other`
 * null when one version is read), clicking `index` while two are compared, or the one read, starts over with that one read;
 * clicking another while one is read compares the two, the earlier first.
 */
export function afterClick(selection, index) {
  if (selection.other !== null || selection.read === index) return Object.freeze({ read: index, other: null });
  return Object.freeze({ read: Math.min(selection.read, index), other: Math.max(selection.read, index) });
}

/** An article's number as a citation writes it: "44" for "art_44", "62-11" for "art_62-11"; the id itself otherwise. */
export function articleNumberOf(publisherId) {
  const match = /^art_(.+)$/u.exec(publisherId);
  return match === null ? publisherId : match[1];
}

/** The most characters a heading holds, as the platform's SearchWords.HeadingMaxCharacters. */
const HEADING_MAX = 150;

const isLower = (character) => character !== character.toUpperCase() && character === character.toLowerCase();
const isUpper = (character) => character !== character.toLowerCase() && character === character.toUpperCase();
const isDigit = (character) => character >= '0' && character <= '9';
const isLetter = (character) => isLower(character) || isUpper(character);

/** Where the article number a text opens with ends ("Article 44", "Art. 74.", "Article 62-11"), or -1. */
function numberEnd(text) {
  let index = 0;
  while (index < text.length && /\s/u.test(text[index])) index += 1;
  if (text.slice(index, index + 3).toLowerCase() !== 'art') return -1;
  while (index < text.length && isLetter(text[index])) index += 1;
  while (index < text.length && (text[index] === '.' || /\s/u.test(text[index]))) index += 1;
  const digits = index;
  while (index < text.length && (isDigit(text[index]) || text[index] === '-' || (index > digits && isLower(text[index]) && index - digits <= 6))) index += 1;
  if (index === digits) return -1;
  while (index < text.length && (text[index] === '.' || /\s/u.test(text[index]))) index += 1;
  return index;
}

/**
 * An article's heading in its own text, as the platform's search reads it (SearchWords.HeadingOf): after its number, a run
 * that opens with a capital and ends where the article's text is glued to it ("Article 44Délai de prescription1.Le délai …"
 * gives "Délai de prescription"), within 150 characters; empty when what follows the number is no heading.
 */
export function articleHeadingOf(text) {
  if (typeof text !== 'string') return '';
  const start = numberEnd(text);
  if (start < 0 || start >= text.length || !isUpper(text[start])) return '';
  const end = Math.min(text.length, start + HEADING_MAX + 1);
  for (let index = start + 1; index < end; index += 1) {
    const before = text[index - 1];
    const current = text[index];
    if (isLower(before) && isUpper(current)) return text.slice(start, index).trim();
    if (isDigit(current) && text[index + 1] === '.' && !isDigit(before)) return text.slice(start, index).trim();
    if (current === '.' && isLower(before) && index + 1 < text.length && (/\s/u.test(text[index + 1]) || isUpper(text[index + 1]))) return '';
  }
  return '';
}

/** The year of a calendar date, for "unchanged since". */
export function yearOf(date) {
  return typeof date === 'string' && /^\d{4}-/.test(date) ? date.slice(0, 4) : null;
}
