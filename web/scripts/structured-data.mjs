// The schema.org Legislation markup of a law's page (the specification of record's R67: "Pages carry hreflang and
// schema.org Legislation markup"; the plan's item 3.6), as JSON-LD in the page's head (`Document.jsx`), written by the
// server's render from the answer the page shows, and only from what that answer holds: a field the answer does not hold
// is left out, never filled.
//
//   url                       the page's canonical link (`lawPageLinks`)
//   legislationIdentifier     the version's ELI (a Luxembourg state's legal resource: the act as published, `.../jo`, or a
//                             consolidated version), or its CELEX (an EU wording's own, a consolidated version's included);
//                             on a dossier, the work's ELI or the act's CELEX
//   name                      the publisher's title: the one title held for the version's expression in its language, or on
//                             a Luxembourg dossier for the act as published; left out when none or several are held, and
//                             for the EU, whose index holds no title
//   inLanguage                the text's language as BCP 47 writes it (`fr`, `de`, `en`); on a dossier, each held
//   legislationJurisdiction   `LU` for Legilux's acts, `EU` for EUR-Lex's: the publisher the answer is from
//   legislationDate           a Luxembourg act's date as its ELI names it (`/eli/etat/leg/{type}/{yyyy}/{mm}/{dd}/{n}`), the
//                             date the citation names the act by; left out for any other ELI and for the EU (a CELEX names
//                             a year)
//   legislationDateVersion    on a reading, the date the reading was asked for (a permalink's own date, or today in
//                             Luxembourg at a readable address)
//   isBasedOn                 the official sources the version's quoted articles name (`official_source`), each once
//   copyrightNotice           an EU text's acknowledgement, as the answer gives it (Decision 95)
//
// Never a person: no author, no `legislationPassedBy`, no `legislationResponsible`; and nothing the index does not hold, so
// no legal force (R21: the index holds no in-force flag), no legal value (R18: Lex's extraction is never authentic) and no
// relation to another act. A title can name a person (a naturalisation law names the person naturalised): the markup says
// no more than the page, and a page of a work the indexing record does not list is kept out of search engines whatever the
// switch (R92, `search-engines.mjs`).
//
// Data and pure functions only.

import { quotationLanguageTag } from "./live-reading.mjs";
import { isCalendarDate } from "./temporal.mjs";

/** A dated Luxembourg act's work ELI: its type, and the act's year, month and day. */
const LUXEMBOURG_DATED_ELI = /^http:\/\/data\.legilux\.public\.lu\/eli\/etat\/leg\/[a-z]+\/(\d{4})\/(\d{2})\/(\d{2})\/[na]\d+$/;

/** The act's date its work ELI names, or null. */
function eliDate(workIri) {
  const match = LUXEMBOURG_DATED_ELI.exec(workIri ?? "");
  if (match === null) return null;
  const date = `${match[1]}-${match[2]}-${match[3]}`;
  return isCalendarDate(date) ? date : null;
}

/** The one title held for an expression in a language (the citation's rule, `citation.mjs`), or null. */
function oneTitle(groups, expressionIri, language) {
  const titles = [...new Set(groups.filter((group) => group.expressionIri === expressionIri && (language === null || group.language === language))
    .flatMap((group) => group.titles.map((title) => (typeof title === "string" ? title : title.title).trim())))];
  return titles.length === 1 && titles[0].length > 0 ? titles[0] : null;
}

/** One value, or a list of several, as JSON-LD writes a property with more than one. */
const oneOrMany = (values) => (values.length === 1 ? values[0] : values);

/** An object with its absent members left out. */
function present(members) {
  return Object.fromEntries(Object.entries(members).filter(([, value]) => value !== null && value !== undefined && !(Array.isArray(value) && value.length === 0)));
}

/**
 * The markup of a reading page that shows `version` of `view` (a reading view, `readEvidenceBundleAnswer`) under the
 * canonical link `canonical`.
 */
export function readingStructuredData({ view, version, canonical }) {
  const europe = view.publisher === "eu-eurlex";
  const sources = [...new Set(version.articles.map((article) => article.officialSource).filter((source) => typeof source === "string" && source.length > 0))].sort();
  return present({
    "@context": "https://schema.org",
    "@type": "Legislation",
    url: canonical,
    legislationIdentifier: europe ? version.wordingCelex : version.publisherLegalResourceIri,
    name: europe ? null : oneTitle(view.citationFacts.titles, version.expressionIri, version.language),
    inLanguage: quotationLanguageTag(version.language),
    legislationJurisdiction: europe ? "EU" : "LU",
    legislationDate: europe ? null : eliDate(version.publisherWorkIri),
    legislationDateVersion: view.date,
    isBasedOn: sources.length === 0 ? null : oneOrMany(sources),
    copyrightNotice: europe ? view.acknowledgement : null,
  });
}

/** The markup of a dossier page of `view` (a dossier view, `readDossierAnswer`) under the canonical link `canonical`. */
export function dossierStructuredData({ view, canonical }) {
  const europe = view.publisher === "eu-eurlex";
  const languages = [...new Set((europe ? view.expressions : view.states).map((one) => quotationLanguageTag(one.language)))].sort();
  // A Luxembourg work is named by the act as published (its work ELI followed by /jo), when the dossier holds that expression.
  const published = europe ? [] : [...new Set(view.titles.map((group) => group.expressionIri).filter((iri) => iri.startsWith(`${view.publisherWorkIri}/jo/`)))];
  return present({
    "@context": "https://schema.org",
    "@type": "Legislation",
    url: canonical,
    legislationIdentifier: europe ? view.celex : view.publisherWorkIri,
    name: published.length === 1 ? oneTitle(view.titles, published[0], null) : null,
    inLanguage: languages.length === 0 ? null : oneOrMany(languages),
    legislationJurisdiction: europe ? "EU" : "LU",
    legislationDate: europe ? null : eliDate(view.publisherWorkIri),
  });
}
