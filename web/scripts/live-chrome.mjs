// The live pages' interface copy, in one table, so every language the interface is offered in is
// one reviewed table beside this one (Decision 41; the launch contract's chrome line).
//
// English is the source. French is drafted beside it for review and ships only when reviewed:
// until then `liveChrome` answers only for English, and French, German and Luxembourgish answer
// `localization_unavailable` (`live-locale-page.jsx`). The table holds each page's title, eyebrow,
// heading and introduction, the forms' labels and buttons, each screen's idle sentence, and the
// sentences the search, dossier, reading, history, compare, radar and export screens say about an
// answer (Trust and Coverage lays its answer out with the shared `Coverage` component, whose copy is
// its own). A sentence with
// values in it is a template with `{name}` placeholders (`fillParts`), so a translation can put them
// where its grammar needs them; a sentence that counts is `{ one, other }`, chosen by the language's
// plural rule (`countedEntry`). The phrases the platform itself sends (a matching rule, a scope, a
// reason something is not held) are the platform's English and are shown as sent.

import { COVERAGE_COPY } from './coverage.mjs';
import { REFUSAL_CARD_COPY } from './refusal-card.mjs';

const EN = Object.freeze({
  coverage: Object.freeze({
    title: "Trust and Coverage",
    eyebrow: "Gateway",
    heading: "Trust and Coverage",
    intro: "What the corpus this server mounts holds and what it recorded as missing, asked of the server when the page loads. The request carries no query text.",
    loading: "Asking this server for its coverage report.",
  }),
  search: Object.freeze({
    title: "Search",
    eyebrow: "Search",
    heading: "Search the held text",
    intro: "Finds the articles whose text contains the phrase exactly as typed, or every word of it, in the text this server holds. The phrase goes to this server in the request and nowhere else, and this page keeps nothing.",
    idle: "Type a phrase to search the article text this server holds.",
    population: Object.freeze({
      one: "“{query}” in {language}: {strict} with the exact phrase, {relaxed} with every word, in {works} work.",
      other: "“{query}” in {language}: {strict} with the exact phrase, {relaxed} with every word, in {works} works.",
    }),
    notCounted: "not counted",
    lane: Object.freeze({ strict: "exact phrase", relaxed: "every word" }),
    namesWork: "The phrase names the work “{title}” ({identifier}).",
    severalWorks: "The phrase matches the titles of several works:",
    candidate: "“{title}” ({identifier})",
    noTitlesHeld: "This index holds no work titles, so the phrase was matched against article text only.",
    ambiguousHeading: "Works with several versions on {date}",
    ambiguousNote: "These works have more than one version that applies on that date, so none is chosen and none contributes a hit.",
    hit: "{article} in {work}, version of {date}",
    noHit: "No article of the text this server holds contains “{query}”.",
    noText: "This index holds no searchable text in {language}; it holds text in {languages}.",
    nextPage: "Next page",
  }),
  dossier: Object.freeze({
    title: "Dossier",
    eyebrow: "Dossier",
    heading: "A work's dossier",
    intro: "What this server holds for one Luxembourg work: its titles, its publisher-dated states and what the dossier does not hold. The identifier goes to this server in the request and nowhere else, and this page keeps nothing.",
    idle: "Type a work identifier to read what this server holds for it.",
    noTitle: "This index holds no title for this work.",
    shortTitle: "{title} (short title)",
    heldIn: "{iri}, held in {languages}.",
    states: Object.freeze({
      one: "{count} state, from {from} to {to}.",
      other: "{count} states, from {from} to {to}.",
    }),
    statesIn: Object.freeze({
      one: "{count} state in {language}, from {from} to {to}.",
      other: "{count} states in {language}, from {from} to {to}.",
    }),
    articlesHeld: "Articles held",
    articlesNotAdmitted: "Articles not admitted",
    notHeldHeading: "What this dossier does not hold",
  }),
  reading: Object.freeze({
    title: "Reading",
    eyebrow: "Reading",
    heading: "The text on a date",
    intro: "The text of one Luxembourg work as it stood on one date, article by article, as the publisher wrote it, with what a quotation of it needs. The identifier and the date go to this server in the request and nowhere else, and this page keeps nothing.",
    idle: "Type a work identifier and a date to read the text that applied on it.",
    rights: "Text served under {rights}. Read on {date}.",
    rightsIn: "Text served under {rights}. Read on {date} in {language}.",
    stateHeading: "{work}, {language}, the state applying from {from}",
    stateHeadingNext: "{work}, {language}, the state applying from {from} (the next state held applies from {next})",
    counts: Object.freeze({
      one: "{count} article quoted, {withoutText} held without text, {notAdmitted} not admitted; {conflicts} with their own date differing from the state's.",
      other: "{count} articles quoted, {withoutText} held without text, {notAdmitted} not admitted; {conflicts} with their own date differing from the state's.",
    }),
    validityConflict: "This article's own date is {own}; its state applies from {state}.",
    evidence: "Text digest {text}, body digest {body}, official source {source}, {permalink}",
    withoutText: "Held without text: {articles}.",
    notHeldHeading: "What this reading does not hold",
  }),
  history: Object.freeze({
    title: "Provision history",
    eyebrow: "Provision history",
    heading: "One article through its states",
    intro: "Which held states of one Luxembourg work carry the publisher's article id, whether its wording changed from one state to the next, and which held states do not carry it. The identifier and the article id go to this server in the request and nowhere else, and this page keeps nothing.",
    idle: "Type a work identifier and the publisher's article id to trace it through the held states.",
    anchorHeading: "{anchor} in {work}",
    carried: Object.freeze({
      one: "Carried by {count} held state, from {from};",
      other: "Carried by {count} held states, from {from};",
    }),
    carriedIn: Object.freeze({
      one: "Carried by {count} held state in {language}, from {from};",
      other: "Carried by {count} held states in {language}, from {from};",
    }),
    absent: Object.freeze({
      one: "{count} held state does not carry it.",
      other: "{count} held states do not carry it.",
    }),
    runs: Object.freeze({
      one: "{language}: {count} wording run, {distinct} distinct",
      other: "{language}: {count} wording runs, {distinct} distinct",
    }),
    wording: Object.freeze({ first: "first held wording", changed: "wording changed", unchanged: "wording unchanged" }),
    wordingColumn: "Wording",
    ownDateColumn: "Article's own date",
    ownDateNotStated: "not stated",
    ownDateDiffers: "{date} (differs from the state's)",
    absentHeading: "Held states that do not carry {anchor}",
    absentRow: "{language}, from {from}: {permalink}",
  }),
  compare: Object.freeze({
    title: "Compare",
    eyebrow: "Compare",
    heading: "Two states, article by article",
    intro: "The states of one Luxembourg work that applied on two dates, compared by the publisher's article ids and wording, with nothing said about legal effect. The identifier and the dates go to this server in the request and nowhere else, and this page keeps nothing.",
    idle: "Type a work identifier and two dates to compare the states that applied on them.",
    summary: "{work}: {from} against {to}.",
    summaryIn: "{work}: {from} against {to} in {language}.",
    sideFrom: "From",
    sideTo: "To",
    side: "{side}: the state applying from {from}, {articles} articles, {conflicts} with their own date differing from the state's, {permalink}",
    sideNext: "{side}: the state applying from {from} (the next state held applies from {next}), {articles} articles, {conflicts} with their own date differing from the state's, {permalink}",
    counts: "{changed} changed, {added} added, {removed} removed, {unchanged} unchanged.",
    status: Object.freeze({ changed: "changed", added: "added", removed: "removed", unchanged: "unchanged" }),
    row: "{article}: {status} (wording {from} → {to})",
    noWording: "none",
    kept: Object.freeze({
      one: "{count} unchanged article",
      other: "{count} unchanged articles",
    }),
    bound: Object.freeze({ from: "from", to: "to" }),
    notCompared: "{language} is not compared: {reason} ({bound} date).",
  }),
  radar: Object.freeze({
    title: "Radar",
    eyebrow: "Radar",
    heading: "The change radar",
    intro: "The publisher-dated states of Luxembourg works that this server holds in a window of dates, each with the state it replaced and whether its wording changed. The dates and any identifier go to this server in the request and nowhere else, and this page keeps nothing.",
    idle: "Type two dates to list the publisher-dated states in that window.",
    summary: "{from} to {to}: {states} of {works}, of {held} held.",
    states: Object.freeze({
      one: "{count} state",
      other: "{count} states",
    }),
    works: Object.freeze({
      one: "{count} work",
      other: "{count} works",
    }),
    noRow: "No held state is dated in this window.",
    windowMisses: "This window does not meet what this index holds.",
    windowMissesRange: "This window does not meet what this index holds, from {first} to {last}.",
    row: "{work}, {language}, from {from}: {verdict}. {permalink}",
    wording: Object.freeze({ changed: "wording changed", unchanged: "wording unchanged" }),
    compared: "{wording} from the state of {date}{baseline}: {changed} changed, {added} added, {removed} removed, {unchanged} unchanged",
    notCompared: "not compared: {reason}{named}",
    reason: Object.freeze({
      first_held_state: "the first state this index holds for the work and language, so there is nothing to compare it with",
      ambiguous_version: "several states of the work apply on this date, so none is compared",
      ambiguous_baseline: "several states apply on the date before it, so no baseline is chosen",
      profiles_differ: "this state and its baseline were read under different rule profiles, so they are not compared",
    }),
    named: "({label} {permalinks})",
    baseline: "baseline",
    candidates: "candidates",
    truncated: "The rows stop before {date}; a next request starts there.",
  }),
  export: Object.freeze({
    title: "Export composer",
    eyebrow: "Export composer",
    heading: "Take articles away, with their citations",
    intro: "Read one Luxembourg work on one date, pin the articles you need, and save them as JSON, CSV or PDF. Each exported article carries its citation, its text digest, its official source and the rights it was served under, and every export carries the watermark. The identifier and the date go to this server in the request and nowhere else; the file is made in this page, and this page keeps nothing.",
    idle: "Type a work identifier and a date, then pin the articles to take away.",
    readOn: "Read on {date}. Pin the articles to export.",
    readOnIn: "Read on {date} in {language}. Pin the articles to export.",
    pin: "Pin {article}",
    withoutTextHeading: "Held without text",
    withoutTextNote: "held without text; an export records it as excluded, with its reason.",
    panelHeading: "Export",
    nothingPinned: "Nothing is pinned yet. Pin an article above and its export appears here, with what it carries.",
    counts: Object.freeze({
      one: "{count} article pinned: {withText} exported with text, {excluded} excluded.",
      other: "{count} articles pinned: {withText} exported with text, {excluded} excluded.",
    }),
    rights: "Text served under {rights}.",
    snapshot: "Read on {date}, from the snapshot observed at {observedAt}. Corpus {corpus}, index {index}, registry {registry}.",
    item: "{article} ({language}, applying from {from}): {citation}, text digest {digest}, official source {source}",
    excluded: "{article} ({language}, applying from {from}): excluded, {reason}, {citation}",
    save: Object.freeze({ json: "Save as JSON", csv: "Save as CSV", pdf: "Save as PDF" }),
    formatRefused: "{format} is not offered for this export: {reason}.",
    composeFailed: "This export cannot be composed: {reason}.",
    jsonSummary: "The JSON as it will be saved",
  }),
  card: Object.freeze({
    heading: "Evaluation card",
    target: "Run over: {target}",
    machineReadable: "The same card for machines, as JSON",
    clean: "Every machine gate on this card passes, and every shuffled control caught its shuffle.",
    notClean: "{gates}, and {controls}.",
    gatesNotPassing: Object.freeze({
      one: "{count} gate does not pass",
      other: "{count} gates do not pass",
    }),
    controlsNotCaught: Object.freeze({
      one: "{count} shuffled control did not catch the shuffle",
      other: "{count} shuffled controls did not catch the shuffle",
    }),
    listed: "{summary} ({list})",
    gateListed: "{gate} in {set}, {arm}: {verdict}",
    controlListed: "{control} in {set}, {arm}: {verdict}",
    verdict: Object.freeze({ pass: "pass", fail: "fail", not_measured: "not measured" }),
    controlVerdict: Object.freeze({
      caught_the_shuffle: "caught the shuffle",
      missed_the_shuffle: "missed the shuffle",
      not_applicable: "not applicable",
    }),
    caption: "{set}, {arm}: {cases} cases, digest {digest}",
    columns: Object.freeze({
      gate: "Gate",
      verdict: "Verdict",
      value: "Value",
      threshold: "Threshold",
      cases: "Cases",
      wilson: "Wilson 95%",
      ruleOfThree: "Rule of three (95%)",
    }),
    verdictReason: "{verdict} ({reason})",
    none: "none",
    notARate: "not a rate",
    interval: "{low} to {high}",
    ruleOfThree: "failure rate below {bound}",
    controlsHeading: "Shuffled controls",
    control: "{control} on {set}, {arm}: {verdict}, {reason} (seed {seed}, {cases} cases, digest {digest}).",
    controlNote: "{control} on {set}, {arm}: {verdict}, {reason} (seed {seed}, {cases} cases, digest {digest}). {note}.",
    statisticalHeading: "Statistical rows",
    statistical: "{dataset}, {name}: not yet labelled. Gates {gates}. {governedBy}.",
    negativeHeading: "Negative results",
    negative: "Hypothesis: {hypothesis}. Dataset: {dataset}. Result: {result}. Decision: {decision}. What would reverse it: {reverse}.",
  }),
  // The refusal card's words, from `refusal-card.mjs`, their one English source (the string renderer
  // says them too); the French is drafted beside them.
  refusalCard: REFUSAL_CARD_COPY,
  // Trust and Coverage's answer, from `coverage.mjs`, likewise the one English source of its words.
  coverageAnswer: COVERAGE_COPY,
  shell: Object.freeze({
    title: "{title} - Lex V3 live",
    bannerLead: "Live development build.",
    banner: "This page shows what the server it was loaded from answers. When a coverage report arrives, its digests name the corpus it counted; until then, or when no corpus is mounted, nothing below describes one. It is not a release and not legal advice.",
    localeNav: "Interface language",
  }),
  common: Object.freeze({
    loading: "Asking this server.",
    languageNames: Object.freeze({ fra: "French", deu: "German", eng: "English" }),
    language: "Language",
    appliesFrom: "Applies from",
    nextFrom: "Next state from",
    permalink: "Permalink",
    noneHeld: "none held",
    notHeldRow: "{item}: {reason}",
  }),
  form: Object.freeze({
    workIdentifier: "Work identifier",
    workIdentifierOptional: "Work identifier (optional)",
    phrase: "Phrase",
    date: "Date",
    datePlaceholder: "yyyy-mm-dd",
    articleIdPlaceholder: "art_15",
    from: "From",
    to: "To",
    articleId: "Article id",
    language: "Language",
    anyLanguage: "Any held language",
    submit: Object.freeze({
      search: "Search",
      dossier: "Read the dossier",
      reading: "Read",
      history: "Trace",
      compare: "Compare",
      radar: "List changes",
      export: "Read for export",
    }),
  }),
});

export const LIVE_CHROME = Object.freeze({ en: EN });

/**
 * The interface language this bundle was built for: English, unless the live build compiled it for
 * another reviewed language (`build-live.mjs` defines `__LEX_CHROME_LOCALE__`). One bundle, one
 * language: a page and the script that hydrates it are built together, so they say the same words.
 */
export const CHROME_LOCALE = typeof __LEX_CHROME_LOCALE__ === 'string' ? __LEX_CHROME_LOCALE__ : 'en';

/** Where a live page's own file is served for this bundle's language: `/` for English, `/fr/` for French. */
export function livePath(file) {
  return CHROME_LOCALE === 'en' ? `/${file}` : `/${CHROME_LOCALE}/${file}`;
}

/** The interface copy for a reviewed language; any other throws, since serving it would be a substitution. */
export function liveChrome(locale = CHROME_LOCALE) {
  const table = LIVE_CHROME[locale];
  if (table === undefined) throw new Error(`no reviewed interface copy in ${JSON.stringify(locale)}: it answers localization_unavailable`);
  return table;
}

/**
 * The entry of a counted sentence (`{ one, other }`) for a count, chosen by the language's own plural
 * rule: French says "0 version" where English says "0 states", so the rule is the language's, never a
 * comparison with 1.
 */
export function countedEntry(entry, count, locale = CHROME_LOCALE) {
  const chosen = entry[new Intl.PluralRules(locale).select(count)] ?? entry.other;
  if (typeof chosen !== 'string') throw new Error(`a counted entry has no "other" sentence: ${JSON.stringify(entry)}`);
  return chosen;
}

/**
 * A template's `{name}` placeholders filled from `values`, as parts: text is joined into strings and any
 * other value (a quotation marked in its own language, an identifier set as code) is kept as a part of
 * its own. A placeholder without a value, or a value without a placeholder, throws: either means the
 * template and the page disagree about what the sentence says.
 */
export function fillParts(template, values = {}) {
  const parts = [];
  const used = new Set();
  const append = (part) => {
    const text = typeof part === 'number' ? String(part) : part;
    if (typeof text === 'string' && typeof parts.at(-1) === 'string') parts[parts.length - 1] += text;
    else if (text !== '') parts.push(text);
  };
  template.split(/\{([A-Za-z]+)\}/).forEach((piece, index) => {
    if (index % 2 === 0) return append(piece);
    if (!Object.hasOwn(values, piece)) throw new Error(`the template ${JSON.stringify(template)} has no value for {${piece}}`);
    used.add(piece);
    return append(values[piece]);
  });
  const unused = Object.keys(values).filter((name) => !used.has(name));
  if (unused.length > 0) throw new Error(`the template ${JSON.stringify(template)} has no place for ${unused.map((name) => `{${name}}`).join(', ')}`);
  return parts;
}

/** A template filled with text only, as one string. */
export function fillText(template, values = {}) {
  const parts = fillParts(template, values);
  if (parts.some((part) => typeof part !== 'string')) throw new Error(`the template ${JSON.stringify(template)} was filled with a value that is not text`);
  return parts.join('');
}

/** A counted sentence for `count`, filled with the count as `{count}` and the other values as text. */
export function fillCounted(entry, count, values = {}, locale = CHROME_LOCALE) {
  return fillText(countedEntry(entry, count, locale), { count, ...values });
}
