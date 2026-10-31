// The one citation generator (the plan's item 3.3; the specification of record's R65): "One citation generator for copy,
// export and permalink, per publisher and language, in three forms. (a) Conventional, the default ...; (b) Conventional plus
// the dated permalink; (c) An evidence form with ELI or CELEX, observed date, text hash, gazette reference and the
// authenticity sentence; exports keep this form. The citation's language follows the quoted text."
//
// Copy (the cited article at its permalink, `PinnedReading.jsx`), export (`export-build.mjs`: JSON, CSV and PDF) and the
// permalink page all compose a citation here and nowhere else. The platform composes no citation string: it serves the facts
// (an evidence bundle's states and articles, and a Luxembourg bundle's `citation_facts`: the publisher's titles and the act
// as published with its Mémorial issue and publication dates), and this module says them (driver decision, STATUS-WEB.md).
//
// The rules, each a field the index holds or a rule stated here, never a field invented:
//   - Language. A citation is said in the language of the text it cites, never the interface's: `fra` in the French table's
//     `citation` words, `eng` in the English table's. A text in another language is cited by its permalink alone (no form).
//   - A Luxembourg act is named in French legal usage by its type and date, read from its ELI (`/eli/etat/leg/{type}/{yyyy}/
//     {mm}/{dd}/{n}`: loi, rgd, agd, rmin, amin, its last segment `n{n}` or, for every act published since 2017, `a{n}`);
//     its title words follow when the publisher's title held for the text's expression, in its language, is one title that
//     starts with that type and date (`citation.luxembourg.titles`) and whose words after them are short and on one line (at
//     most 200 characters, and no line break, which the publisher's titles write as the two characters backslash and n);
//     otherwise the type and date stand alone, as French usage cites an act, and the evidence form quotes the title whole
//     (a consolidated version's title, which starts "Version consolidée ...", is not read into the usual form). A code
//     (`/eli/etat/leg/code/{name}`) is named by its one held title. Any other type is given no usual form. "modifiée" is
//     never written: the index does not hold whether an act was amended.
//   - The article's number is read from the publisher's id by a fixed rule (`art_15`, `art_1er`, `art_30-2`, `art_40bis`,
//     `art_iii`, `art_unique`, a code's `art_l_121-6`, a number before a heading); an id of no shape here (a quoted article
//     of an amending act, a heading alone) is given no usual form. An EU provision is named by its own heading when it reads
//     "Article ..." (the text's words, "Article 32 bis", its no-break spaces as plain ones, since a citation is copied), else
//     by its id's number (`006` is 6). The usual form is set in its composed form (NFC); the evidence form quotes the title as
//     the publisher served it.
//   - An EU act is named as EUR-Lex numbers it, from its CELEX's type (R, L, D), year and number: "Regulation (EU)
//     2016/679", "Regulation (EU) No 575/2013", "Directive 2006/112/EC", a year before 1999 on two digits. The domain is
//     the numbering's for the act's year (EEC before 1993, EC from 1994 to 2008, EU from 2010); in 1993 and 2009, whose acts
//     carry either, none is written. The index holds no author or domain of an EU act, so a Euratom or CFSP act is named as
//     the Union's: a limit the slice records (STATUS-WEB.md). Any other type is given no usual form; an EU title the index
//     /7 holds is quoted by the evidence form, never read into the usual one.
//   - The version date is added when the version is not known to be the one that answers today in Luxembourg: a Luxembourg
//     state with a later held state applicable on or before today, or held only as published (R76: it may have been amended
//     since); an EU wording that a later one answers today, or the original wording of a bundle that holds no consolidation.
//     It is said in R21's words, as a version "applicable" on its own date for Luxembourg and as the text "at" its wording
//     date for the EU (never an EU applicability date).
//   - The evidence form's lines: the usual form (the permalink alone when there is none), the permalink, the ELI of the
//     resource quoted (or the CELEX of the wording), the publisher's title, the version's date or the wording's, the gazette
//     reference (a Luxembourg act's Mémorial A issue, "not held" when the index holds none; an EU wording's Official Journal
//     reference as its Formex file states it, over an EU index /7, in EUR-Lex's form: the act's own, or for a consolidated
//     wording the act's it consolidates, and "not held" where the index holds none), the publication date when the index
//     holds one, the text's SHA-256 (the
//     provision's text_sha256, R64), the date Lex was consulted (the answer's own date in Luxembourg; the index holds no
//     time the publisher served the bytes), an act held only as published's label, and the authenticity sentence: the
//     as-published sentence of its case, the site's sentence for consolidated text, or the EU's statement its act's own date
//     chose, the electronic edition's or the dated one (R19, R20; the owner's decision of 2026-10-18, O4).
//
// `verify` resolves every form it emits: the permalink forms by the permalink, as a path or under the site's origin, and the
// evidence form by its two hashes, the version's in the permalink and the text's (`article_text_sha256`, `evidenceVerdict`).
// The usual form is read by the exact-reference phase of the plan's item 4.2, which the words here are written for: asked as
// a search's query, it opens Reading at the article it cites (`usualVerdict`; the answer census asks it one).
//
// This module is data and pure functions only: the live pages' bundles, the export and the tests share it.

import { CHROME_LOCALE, fillText, liveChrome } from './live-chrome.mjs';
import { articlePermalink } from './permalinks.mjs';
import { CANONICAL_HOST } from './routes.mjs';
import { isCalendarDate, luxembourgDate } from './temporal.mjs';
import { datedByEntryIntoForce, holdsToday } from './date-role.mjs';
import { europeReferenceOf, europeTitleOf, officialJournalWords } from './europe-publication.mjs';

/** The origin a citation's permalink is printed under: the site's own (`Document.jsx`'s canonical links). */
export const CITATION_ORIGIN = `https://${CANONICAL_HOST}`;

/** The text languages a citation is composed in, each with the table whose `citation` words it says. */
export const CITATION_LANGUAGES = Object.freeze({ fra: 'fr', eng: 'en' });

/** The Luxembourg act types a usual citation names by type and date, as their ELIs write them. */
const LUXEMBOURG_DATED = /^http:\/\/data\.legilux\.public\.lu\/eli\/etat\/leg\/(loi|rgd|agd|rmin|amin)\/(\d{4})\/(\d{2})\/(\d{2})\/[na]\d+$/;
const LUXEMBOURG_CODE = /^http:\/\/data\.legilux\.public\.lu\/eli\/etat\/leg\/code\/[a-z0-9_]+$/;

/** The most title words a usual citation carries after the act's type and date: a longer title is quoted by the evidence form alone. */
export const TITLE_WORDS_AT_MOST = 200;

/** The Latin suffixes of an article number, as an id writes them after its digits (`art_40bis`). */
const LATIN_SUFFIX = '(?:bis|ter|quater|quinquies|sexies|septies|octies|novies|nonies|decies|undecies|duodecies)';
const ARTICLE_PREFIX = '(?:art_?|article_)';
const NUMBERED_ID = new RegExp(`^${ARTICLE_PREFIX}(\\d+(?:-\\d+)*${LATIN_SUFFIX}?)$`);
const FIRST_ID = new RegExp(`^${ARTICLE_PREFIX}(1|i)er$`);
const ROMAN_ID = new RegExp(`^${ARTICLE_PREFIX}([ivxlc]+)$`);
const UNIQUE_ID = new RegExp(`^${ARTICLE_PREFIX}unique$`);
const CODE_ID = /^art_([lrda])_(\d+(?:-\d+)*)$/;
/** A number the publisher's id follows with the article's heading (`art_2_champ_d'application` is article 2). */
const HEADED_ID = /^(?:art_|article_)(\d+(?:-\d+)*)_\p{L}/u;

/** The Mémorial A issue an act as published is part of, as the publisher's isPartOf names it. */
const MEMORIAL_ISSUE = /^http:\/\/data\.legilux\.public\.lu\/eli\/etat\/leg\/memorial\/(\d{4})\/a0*(\d+)$/;

/** A CELEX an EU act is numbered from: sector 3, its year, its type and its number. */
const ACT_CELEX = /^3(\d{4})([RLD])0*(\d+)$/;

/**
 * The `lang` a citation is marked with on this bundle's pages: its text's language (`fr`, `en`), and none on a page in that
 * language already, as a page marks no English on an English page.
 */
export function citationLang(language) {
  const tag = CITATION_LANGUAGES[language];
  return tag === undefined || tag === CHROME_LOCALE ? undefined : tag;
}

/** The words of a citation in a text's language, or null when no citation is composed in it. */
export function citationWords(language) {
  const locale = CITATION_LANGUAGES[language];
  return locale === undefined ? null : liveChrome(locale).citation;
}

/** The table a citation in `language` says its other sentences from (the as-published label, the site's statements). */
function tableOf(language) {
  return liveChrome(CITATION_LANGUAGES[language]);
}

/** A calendar date in words, in a citation's language: "10 août 1991", "1er février 2024", "27 April 2016". */
export function dateInWords(date, words) {
  if (!isCalendarDate(date)) throw new Error(`a citation says a calendar date, not ${JSON.stringify(date)}`);
  const [year, month, day] = date.split('-');
  const number = Number(day);
  return fillText(number === 1 ? words.firstOfMonth : words.date, { day: number, month: words.months[Number(month) - 1], year });
}

/** A Luxembourg article's number as its usual citation writes it ("Art. 15", "Art. 1er", "Art. L. 121-6"), or null. */
export function luxembourgArticle(publisherId, words) {
  const own = words.luxembourg;
  if (UNIQUE_ID.test(publisherId)) return own.articleUnique;
  const first = FIRST_ID.exec(publisherId);
  if (first !== null) return fillText(own.articleFirst, { number: first[1].toUpperCase() });
  const numbered = NUMBERED_ID.exec(publisherId);
  if (numbered !== null) return fillText(own.article, { number: numbered[1] });
  const roman = ROMAN_ID.exec(publisherId);
  if (roman !== null) return fillText(own.article, { number: roman[1].toUpperCase() });
  const code = CODE_ID.exec(publisherId);
  if (code !== null) return fillText(own.article, { number: `${code[1].toUpperCase()}. ${code[2]}` });
  const headed = HEADED_ID.exec(publisherId);
  if (headed !== null) return fillText(own.article, { number: headed[1] });
  return null;
}

/** An EU provision as its usual citation names it: its own heading when it reads "Article ...", else its id's number. */
export function europeArticle(article, words) {
  const heading = (article.heading ?? '').trim();
  // A citation is copied text: the heading's no-break spaces, wide or narrow ("Article\u00a0premier"), are plain ones in it.
  if (/^Article[\s\u00a0\u202f]+\S/.test(heading)) return heading.replace(/[\u00a0\u202f]/g, ' ');
  const numbered = /^0*(\d+)([A-Z]*)$/.exec(article.publisherId);
  return numbered === null ? null : fillText(words.europe.article, { number: `${numbered[1]}${numbered[2].toLowerCase()}` });
}

/** The domain EUR-Lex's numbering gives an act of `year`, or null for a year whose acts carry either of two. */
function europeDomain(year) {
  if (year <= 1992) return 'EEC';
  if (year >= 1994 && year <= 2008) return 'EC';
  if (year >= 2010) return 'EU';
  return null;
}

/** An EU act as EUR-Lex numbers it ("Regulation (EU) 2016/679"), with the article named, or null for a CELEX of no such act. */
function europeConventional(celex, articleText, words) {
  const parts = ACT_CELEX.exec(celex);
  if (parts === null || articleText === null) return null;
  const year = Number(parts[1]);
  const type = parts[2];
  const number = parts[3];
  const printedYear = year < 1999 ? parts[1].slice(2) : parts[1];
  const domainKey = europeDomain(year);
  const domain = domainKey === null ? null : words.europe.domains[domainKey];
  let designation;
  if (year >= 2015) {
    designation = domain === null ? fillText(words.europe.plain, { year: printedYear, number }) : fillText(words.europe.yearFirst, { domain, year: printedYear, number });
  } else if (type === 'R') {
    designation = domain === null ? fillText(words.europe.numberedNoDomain, { number, year: printedYear }) : fillText(words.europe.numbered, { domain, number, year: printedYear });
  } else {
    designation = domain === null ? fillText(words.europe.plain, { year: printedYear, number }) : fillText(words.europe.suffixed, { year: printedYear, number, domain });
  }
  return fillText(words.europe.acts[type], { article: articleText, designation });
}

/** The words of the one publisher title held for an expression in a language, or null when none or several are held. */
function heldTitle(facts, expressionIri, language) {
  const groups = facts.titles.filter((group) => group.expressionIri === expressionIri && group.language === language);
  const titles = [...new Set(groups.flatMap((group) => group.titles))];
  return titles.length === 1 ? titles[0].trim() : null;
}

/** A title without its final full stop, as a citation continues with it. */
const withoutFullStop = (text) => text.replace(/\.$/, '').trim();

/** A text compared as the publisher and Lex may write the same words, character for character: case, apostrophes and spaces aside. */
const folded = (text) => text.toLowerCase().replace(/[’']/g, "'").replace(/[\s\u00a0]/g, ' ');

/**
 * A Luxembourg act's usual citation of one article, or null: the type and date from the ELI and, when the one title held
 * starts with them, the title's words after them; a code by its one title.
 */
function luxembourgConventional(state, articleText, facts, words) {
  if (articleText === null) return null;
  const title = heldTitle(facts, state.expressionIri, state.language);
  if (LUXEMBOURG_CODE.test(state.publisherWorkIri)) {
    return title === null ? null : fillText(words.luxembourg.acts.code, { article: articleText, code: withoutFullStop(title) });
  }
  const dated = LUXEMBOURG_DATED.exec(state.publisherWorkIri);
  if (dated === null) return null;
  const [, type, year, month, day] = dated;
  const date = dateInWords(`${year}-${month}-${day}`, words);
  const act = fillText(words.luxembourg.acts[type], { article: articleText, date });
  if (title === null) return act;
  // The words after the title's type and date, when it starts with them (a day's "1er" written as "1" too).
  const starts = [date, dateInWords(`${year}-${month}-${day}`, { ...words, firstOfMonth: words.date })]
    .map((said) => folded(fillText(words.luxembourg.titles[type], { date: said })));
  // Matched and continued in its composed form: a title written with combining accents starts with its type and date too.
  const plain = withoutFullStop(title).normalize('NFC');
  const prefix = starts.find((start) => folded(plain).startsWith(start) && (folded(plain).length === start.length || /^[\s,]/.test(plain.slice(start.length))));
  if (prefix === undefined) return act;
  const rest = plain.slice(prefix.length).trim();
  // Words long or on several lines (an omnibus act's list of the acts it amends) are no usual citation's: the type and date
  // stand alone, and the evidence form quotes the title whole.
  if (rest.length === 0 || rest.length > TITLE_WORDS_AT_MOST || /\\n|[\r\n]/.test(rest)) return act;
  return rest.startsWith(',') ? `${act}${rest}` : `${act} ${rest}`;
}

/** Whether a Luxembourg state is known to be the one that answers `today` (never one held only as published). */
function luxembourgCurrent(view, state, today) {
  if (view.asPublished !== null) return false;
  // On index 9 by the interval's end, so a closed state, or an act followed only by states without text, is not today's.
  return holdsToday(state, today);
}

/** Whether an EU wording is known to be the one that answers `today`: in the time view only. */
function europeCurrent(wording, today) {
  if (wording.nextDate === undefined) return false;
  return wording.wordingDate <= today && (wording.nextDate === null || wording.nextDate > today);
}

/** A sentence's first letter capitalised, and a full stop at its end when it has no final sign. */
const asSentence = (text) => `${text.charAt(0).toUpperCase()}${text.slice(1)}${/[.!?]$/.test(text) ? '' : '.'}`;

/**
 * The three forms of one article's citation (R65), in its text's language, or null for a text in a language no citation is
 * composed in. `view` is a reading view (`readEvidenceBundle` or `readEuropeEvidenceBundle`), `version` the state or EU
 * wording the article is read in, `article` one of its quoted articles or of its articles held without text (`{publisherId}`,
 * with `heading`, `textSha256` and `permalink` when quoted), and `observedAt` the answer's own instant
 * (`context.freshness.observed_at`), whose date in Luxembourg is today and the date Lex was consulted.
 *
 * @returns {null|{language: string, conventional: string|null, withPermalink: string, evidence: string,
 *   evidenceLines: string[], permalink: string, textSha256: string|null}}
 */
export function citationForms({ view, version, article, observedAt }) {
  const words = citationWords(version.language);
  if (words === null) return null;
  if (typeof observedAt !== 'string' || observedAt.length === 0) throw new Error('a citation says the date Lex was consulted, and no instant was given');
  const table = tableOf(version.language);
  const evidence = words.evidence;
  const today = luxembourgDate(observedAt);
  const europe = view.publisher === 'eu-eurlex';
  // An article held without text is cited as any article of its version is: the version's permalink and its id (R64).
  const path = article.permalink ?? articlePermalink(version.permalink, article.publisherId);
  const permalink = `${CITATION_ORIGIN}${path}`;
  const textSha256 = article.textSha256 ?? null;

  let conventional;
  const lines = [];
  if (europe) {
    const usual = europeConventional(view.celex, europeArticle(article, words), words);
    conventional = usual === null || europeCurrent(version, today)
      ? usual
      : fillText(words.europe.version, { citation: usual, date: dateInWords(version.wordingDate, words) });
  } else {
    const usual = luxembourgConventional(version, luxembourgArticle(article.publisherId, words), view.citationFacts, words);
    conventional = usual === null || luxembourgCurrent(view, version, today)
      ? usual
      : fillText(datedByEntryIntoForce(version) ? words.luxembourg.versionEntryIntoForce : words.luxembourg.version,
        { citation: usual, date: dateInWords(version.applicabilityDate, words) });
  }
  // The usual form is copied text, in its composed form (NFC): a title the publisher wrote with combining accents reaches no
  // copy decomposed. The evidence form quotes the title as served.
  if (conventional !== null) conventional = conventional.normalize('NFC');
  const withPermalink = conventional === null ? permalink : fillText(words.withPermalink, { citation: conventional, permalink });

  lines.push(conventional ?? permalink);
  lines.push(fillText(evidence.permalink, { permalink }));
  if (europe) {
    lines.push(fillText(evidence.celex, { celex: version.wordingCelex ?? view.celex }));
    // The publisher's title of the wording's expression, over an EU index /7, as the evidence form quotes a Luxembourg one.
    const title = europeTitleOf(view.titles ?? null, version.expressionIri);
    if (title !== null && title.source === 'cellar_expression_title') lines.push(fillText(evidence.title, { title: title.text }));
    lines.push(fillText(version.kind === 'consolidated_version' ? evidence.consolidatedWording : evidence.originalWording, { date: version.wordingDate }));
    // The Official Journal reference the wording's Formex file states (EU index /7): the act's own, or for a consolidated
    // wording that of the act it consolidates; "not held" where the index holds none, never guessed from a file name.
    const reference = europeReferenceOf(view.officialJournal ?? null, version.expressionIri);
    lines.push(reference === null
      ? evidence.officialJournalNotHeld
      : fillText(reference.kind === 'consolidated_act_publication' ? evidence.officialJournalConsolidated : evidence.officialJournal,
        { reference: officialJournalWords(reference, words) }));
  } else {
    const facts = view.citationFacts;
    lines.push(fillText(evidence.eli, { eli: version.publisherLegalResourceIri }));
    const title = heldTitle(facts, version.expressionIri, version.language);
    if (title !== null) lines.push(fillText(evidence.title, { title }));
    // A version dated by its entry into force is said as that, never as applicable from it (R21; `date-role.mjs`).
    lines.push(fillText(datedByEntryIntoForce(version) ? evidence.versionEntryIntoForce : evidence.version, { date: version.applicabilityDate }));
    const issues = [...new Set(facts.memorialIssues)];
    if (issues.length === 0) lines.push(evidence.memorialNotHeld);
    for (const iri of issues) {
      const issue = MEMORIAL_ISSUE.exec(iri);
      lines.push(issue === null ? fillText(evidence.memorialIri, { iri }) : fillText(evidence.memorial, { number: issue[2], year: issue[1], iri }));
    }
    // A publication date is said only when the index holds exactly one, as a civil date.
    const published = [...new Set(facts.publicationDates)];
    if (published.length === 1 && isCalendarDate(published[0])) lines.push(fillText(evidence.publicationDate, { date: published[0] }));
  }
  lines.push(textSha256 === null ? evidence.noText : fillText(evidence.textSha256, { digest: textSha256 }));
  lines.push(fillText(evidence.consulted, { date: today }));
  if (europe) {
    // The platform's statement, the one the act's own date chose (the owner's decision of 2026-10-18, O4), said in the text's
    // language when it is one of the two statements this table holds word for word, else as served.
    const statements = liveChrome('en').citation.europe.authenticity;
    const basis = Object.keys(statements).find((key) => statements[key] === view.authenticity);
    lines.push(basis === undefined ? view.authenticity : words.europe.authenticity[basis]);
  } else if (view.asPublished !== null) {
    lines.push(asSentence(table.common.asPublished.label));
    lines.push(fillText(table.common.asPublished.authenticity[view.asPublished.authenticity.basis], { act: view.asPublished.journalOfficielIri }));
  } else {
    // A consolidated state: the site's sentence on consolidated text (R20's own sentence, with the acts that amended it,
    // waits for its wording, the owner's item O4).
    lines.push(table.site.authenticity);
  }

  return Object.freeze({
    language: version.language,
    conventional,
    withPermalink,
    evidence: lines.join('\n'),
    evidenceLines: Object.freeze(lines),
    permalink,
    textSha256,
  });
}

/**
 * What the exact-reference phase (the plan's item 4.2) says of a citation's usual form, asked as a search's query with no
 * work named (R65: "verify resolves every form"): `matches` when the platform read it as a reference and opens Reading at
 * the cited article, by the permalink of the version the form names, or, for a form that names no date (its version
 * answers today), by the act's readable address of that article; otherwise why not (`refused`, `another_act`,
 * `another_version`, `another_article`). `view` is the search answer as `readSearchAnswer` reads it.
 */
export function usualVerdict(forms, view) {
  if (forms.conventional === null || view === null || typeof view !== 'object' || view.kind !== 'reference') return 'refused';
  if (view.outcome !== 'opens' || view.opens.page !== 'reading') return 'refused';
  // The cited article's permalink: /{publisher}/{work}/{date}--{sha256}/{language}/{article}.
  const path = forms.permalink.slice(CITATION_ORIGIN.length);
  const [, publisher, work, , language, article] = path.split('/');
  const opened = view.opens.address.split('/');
  if (opened[1] !== publisher || opened[2] !== work) return 'another_act';
  if (view.opens.addressKind === 'readable') {
    // Today's version, at the article: what a form with no date cites.
    if (opened[3] !== language) return 'another_version';
    return opened[4] === article ? 'matches' : 'another_article';
  }
  if (view.opens.address === path) return 'matches';
  return opened.slice(0, 5).join('/') === path.split('/').slice(0, 5).join('/') ? 'another_article' : 'another_version';
}

/**
 * What `verify`'s answer says of a citation's evidence form (R65: "verify resolves every form"), asked at the form's
 * permalink: `matches` when the answer verifies the version the permalink pins and names the article's text digest the
 * form carries; otherwise why not (`refused`, `another_version`, `another_article`, `text_differs`).
 */
export function evidenceVerdict(forms, verification) {
  if (verification === null || typeof verification !== 'object' || verification.verdict !== 'digest_matches') return 'refused';
  const path = forms.permalink.slice(CITATION_ORIGIN.length);
  if (verification.article_permalink !== path) {
    const pinned = path.slice(0, path.lastIndexOf('/'));
    return verification.permalink === pinned ? 'another_article' : 'another_version';
  }
  return verification.article_text_sha256 === forms.textSha256 ? 'matches' : 'text_differs';
}
