// The live pages' interface copy, in one table, so every language the interface is offered in is
// one reviewed table beside this one (Decision 41; the launch contract's chrome line).
//
// English is the source. French is the reviewed table beside it (`live-chrome-fr.mjs`, reviewed by
// Claude (AI reviewer), under the owner's delegation of 2026-10-02; its receipt is `CHROME_REVIEWS`
// in `localization.mjs`). German and Luxembourgish have no reviewed table, so `liveChrome` refuses
// them and they answer `localization_unavailable` (`live-locale-page.jsx`). The table holds each
// page's title, eyebrow, heading and introduction, the forms' labels and buttons, each screen's idle
// sentence, and the sentences the search, dossier, reading, history, compare, radar and export
// screens say about an answer (Trust and Coverage lays its answer out with the shared `Coverage`
// component, whose copy is its own), down to the separators between a list's items. A sentence with
// values in it is a template with `{name}` placeholders (`fillParts`), so a translation can put them
// where its grammar needs them; a sentence that counts is `{ one, other }`, chosen by the language's
// plural rule (`countedEntry`). The phrases the platform itself sends (a matching rule, a scope, a
// reason something is not held) are the platform's English and are shown as sent, marked English on
// a page in another language (`ENGLISH_LANG`, `englishRun`).
//
// The sentences the screens say for a refusal stay in each screen's own module, in English, where the
// checkpoint list collects them (`refusal-sentences.mjs`); a reviewed language says them through
// `REFUSAL_TRANSLATIONS`, keyed by that English (`refusalSentence`, `refusalTemplate`,
// `refusalHint`), and a sentence with no reviewed translation throws rather than being said in
// English in its place.

import { COVERAGE_COPY } from './coverage.mjs';
import { REFUSAL_CARD_COPY } from './refusal-card.mjs';
import { LIVE_CHROME_FR, REFUSALS_FR } from './live-chrome-fr.mjs';

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
    intro: "Finds the articles whose text contains the phrase exactly as typed, or every word of it, in the text this server holds and may search: text its rights withhold is not searched. The phrase goes to this server in the request and nowhere else, and this page keeps nothing.",
    idle: "Type a phrase to search the article text this server holds and may search.",
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
    ambiguousWork: "{work}: {candidates}",
    hit: "{article} in {work}, version of {date}",
    noHit: "No article of the text this server holds and may search contains “{query}”.",
    noText: "This index holds no searchable text in {language}; it holds text in {languages}.",
    nextPage: "Next page",
    euWording: "Every hit is in the original wording of {celex} this server holds in {language}, dated {date} and pinned by its digest: {permalink}",
    euHit: "{heading} of {celex}, wording of {date}",
    notHeldHeading: "What this search does not cover",
    // The one box's placeholder, word for word the specification of record's (R52).
    placeholder: "Search by title, number, ELI or CELEX, or words from the text",
    // What a Luxembourg search was asked at (R13: the date is printed), said of what this site holds (R21, R22, R76): the
    // latest held version applicable by the date, which for an act held only as published is that act as published.
    dated: "Only Luxembourg acts were searched. For each, the latest version this site holds that is applicable from {date} or an earlier date (publisher) was searched, and an act with no such version was not. An act held only as published is searched as published, and may have been amended since.",
    undated: "Only Luxembourg acts were searched, with no date: every version this site holds was searched, and each result names its version's date.",
    // A search with no work named searches Luxembourg law alone, and its no-hit sentence says so.
    noHitLuxembourg: "No article of the Luxembourg text this server holds and may search contains “{query}”.",
    // Under the home page's box (and the 404 page's): what it searches, and where an EU act is searched.
    luxembourgOnly: "This search covers Luxembourg law. To search within an EU act, name it by its CELEX number on the {link}.",
    searchPage: "search page",
  }),
  dossier: Object.freeze({
    title: "Dossier",
    eyebrow: "Dossier",
    heading: "A work's dossier",
    intro: "What this server holds for one work: a Luxembourg work's titles and publisher-dated states, or an EU work's expressions and the original wording of each, and what the dossier does not hold. The identifier goes to this server in the request and nowhere else, and this page keeps nothing.",
    idle: "Type a work identifier to read what this server holds for it.",
    noTitle: "This index holds no title for this work.",
    shortTitle: "{title} (short title)",
    titleGroup: "{language}: {titles}",
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
    euExpressions: Object.freeze({
      one: "{count} expression held, shown with its original wording.",
      other: "{count} expressions held, each shown with its original wording.",
    }),
    wordingDate: "Wording date",
  }),
  reading: Object.freeze({
    pinReplaced: "This link pins {pinned}, which is not the version this site serves for that date; the version it serves is shown below.",
    europePinReplaced: "This link pins {pinned}, which is not the wording that answers that date on this site; the wording that does is shown below.",
    // A permalink whose digest is not the version (or EU wording) the site holds at its date (R64: pinned_digest_mismatch,
    // "with the current link"), said above the refusal card with that link; the cited article, first, at an address that
    // names one; and what changed since the version shown (R64's banner), with a link to the act at today's date (R67).
    pinNotHeld: "This link pins {pinned}, which is not the version this site holds for that date; the version it holds is {current}.",
    europePinNotHeld: "This link pins {pinned}, which is not a wording this site holds for that date; the wording that answers that date on this site is {current}.",
    citedTitle: "{work}, {article}",
    citedFrom: "Quoted from {source}.",
    wholeAct: "The whole act at this version",
    europeWholeAct: "The whole act in this wording",
    newerVersion: "A newer version is applicable from {date} (publisher).",
    europeNewerWording: "A later wording of this act is held: the wording of {date}.",
    openToday: "Read this act at today's date",
    title: "Reading",
    eyebrow: "Reading",
    heading: "The text on a date",
    intro: "The text of one Luxembourg work as it stood on one date, or of one EU work in the wording that answers that date (its original wording or a later consolidated one), article by article, as the publisher wrote it, with what a quotation of it needs. The identifier and the date go to this server in the request and nowhere else, and this page keeps nothing.",
    idle: "Type a work identifier and a date to read its text on that date.",
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
    europeWordingHeading: "{celex}, {language}, the original wording of {date} (the act's date in the publisher's Formex package; no later wording is held)",
    europeOriginalHeading: "{celex}, {language}, the original wording of {date} (the act's date in the publisher's Formex package)",
    europeConsolidatedHeading: "{celex}, {language}, the consolidated wording of {date} (the publisher's consolidation date)",
    europeHoldsUntil: "This wording answers the dates from {date} to the day before {next}, the date of the next wording.",
    europeSameDateWorks: Object.freeze({
      one: "{count} other publisher work carries this date: {held} with this same text held here, {notHeld} with no text held here.",
      other: "{count} other publisher works carry this date: {held} with this same text held here, {notHeld} with no text held here.",
    }),
    europeUnplaced: Object.freeze({
      one: "{count} wording of this act has no usable publisher date and is not placed in time: {held} with this same text held here, {notHeld} with no text held here.",
      other: "{count} wordings of this act have no usable publisher date and are not placed in time: {held} with this same text held here, {notHeld} with no text held here.",
    }),
    europeLatest: "This is the latest wording held: it answers every later date, and an amendment the publisher has not yet consolidated is not in it.",
    europeCounts: Object.freeze({
      one: "{count} article quoted, {withoutText} held without text.",
      other: "{count} articles quoted, {withoutText} held without text.",
    }),
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
    intro: "The states of one Luxembourg work applicable on two dates, compared by the publisher's article ids and wording, with nothing said about legal effect. The identifier and the dates go to this server in the request and nowhere else, and this page keeps nothing.",
    idle: "Type a work identifier and two dates to compare the states applicable on them.",
    summary: "{work}: {from} against {to}.",
    summaryIn: "{work}: {from} against {to} in {language}.",
    sideFrom: "From",
    sideTo: "To",
    side: "{side}: the state applying from {from}, {articles} articles, {conflicts} with their own date differing from the state's, {permalink}",
    sideNext: "{side}: the state applying from {from} (the next state held applies from {next}), {articles} articles, {conflicts} with their own date differing from the state's, {permalink}",
    counts: "{changed} changed, {added} added, {removed} removed, {unchanged} unchanged.",
    // Both dates select one state: said in the publisher's clock, never "applied" (R21; the plan's item 0.6).
    sameVersion: "Both dates fall within the same version, applicable from {from} (publisher).",
    // Both dates select the one state of an act held only as published (the review of #964): what is held, never one
    // version covering both dates. The publisher may date a consolidated version, or an end-of-force date, between them,
    // and the card names each, a version in the as-published block's form (`common.asPublished.consolidationFromTo`).
    sameTextHeld: "Both dates select the one text held, the act as published, applicable from {from} (publisher); later versions are not held.",
    sameTextHeldOfSeveral: "Both dates select the same text held, the act as published, applicable from {from} (publisher); no consolidated version is held.",
    betweenNamed: Object.freeze({
      one: "The publisher's record names, for this act, {count} consolidated version whose applicability date falls after {after} and on or before {onOrBefore}:",
      other: "The publisher's record names, for this act, {count} consolidated versions whose applicability dates fall after {after} and on or before {onOrBefore}:",
    }),
    betweenEndOfForce: "The publisher's record gives {date} as this act's end-of-force date, which falls after {after} and on or before {onOrBefore}.",
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
    intro: "Read one Luxembourg work on one date, or one EU work in the wording that answers that date, pin the articles you need, and save them as JSON, CSV or PDF. Each exported article carries its citation, its text digest and its official source, with the rights it was served under or, for EU text, the acknowledgement it was served with and the authenticity statement; every export carries the watermark and says what it excludes, with the reason. The identifier and the date go to this server in the request and nowhere else; the file is made in this page, and this page keeps nothing.",
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
    // An EU export's: its text is served with Decision 95's acknowledgement, and each article is dated as its wording is.
    europeRights: "Text served with the acknowledgement {acknowledgement}, which every exported article carries, and the authenticity statement below.",
    europeItem: "{article} ({language}, wording of {date}): {citation}, text digest {digest}, official source {source}",
    europeExcluded: "{article} ({language}, wording of {date}): excluded, {reason}, {citation}",
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
  // says them too); the French table holds their French.
  refusalCard: REFUSAL_CARD_COPY,
  // Trust and Coverage's answer, from `coverage.mjs`, likewise the one English source of its words.
  coverageAnswer: COVERAGE_COPY,
  shell: Object.freeze({
    title: "{title} - Lex",
    localeNav: "Interface language",
  }),
  // The site around every live screen (`Document.jsx`): its name, its menu, and the statements every page carries below
  // its content: what Lex is not (the specification of record's R50, word for word, which replaced the footer's
  // "Documentation, not legal advice."; the home page, the search results and Reading say it too), which texts are
  // authentic (product spec section 4; the export watermark's "Consolidations have no legal effect"; R20 rewrites it), and
  // where the texts come from.
  site: Object.freeze({
    brand: "Lex",
    tagline: "The Luxembourg and EU law this site holds, at a date you choose",
    skip: "Skip to the content",
    menu: "Main menu",
    search: "Search",
    reading: "Read",
    dossier: "Dossier",
    history: "History",
    compare: "Compare",
    radar: "Changes",
    export: "Export",
    coverage: "What this site holds",
    notAdvice: "Lex shows the published text of the law. It does not tell you whether or how it applies to your situation and is not legal advice.",
    authenticity: "Consolidated texts have no legal effect: the authentic texts are those published in the Journal officiel du Grand-Duché de Luxembourg (Mémorial A) and in the Official Journal of the European Union.",
    sources: "The texts come from Legilux and from the Publications Office of the European Union, as this site holds them; every quote names its official source.",
  }),
  // The home page (the specification of record's R47 and R52; UX spec section 2, Screen 1: Home and search): the one box
  // first, what Lex is and is not, a coverage line whose every count is the coverage answer's (R11), and links to the
  // site's served pages. A count is said with its noun by the language's plural rule.
  home: Object.freeze({
    title: "Luxembourg and EU law at a date you choose",
    eyebrow: "Search the law",
    heading: "Luxembourg and EU law at a date you choose",
    intro: "Search the texts this site holds, open a law as it stood on the date you choose, see how each article changed, compare two dates, and export with citations anyone can check against the official source. Where the site does not hold a text or a date, it says so.",
    aboutHeading: "What Lex is and is not",
    coverage: "This site holds {acts} of Luxembourg law, in {versions} with {articles}, as of the index built on {date}.",
    coverageUndated: "This site holds {acts} of Luxembourg law, in {versions} with {articles}; the index it holds records no build date.",
    acts: Object.freeze({
      one: "{count} act",
      other: "{count} acts",
    }),
    versions: Object.freeze({
      one: "{count} version",
      other: "{count} versions",
    }),
    articles: Object.freeze({
      one: "{count} article",
      other: "{count} articles",
    }),
    links: "Elsewhere on this site",
  }),
  // A service failure on a screen, panel by panel (the specification of record's R60; `live-failure.mjs`): its line, word
  // for word the specification's, above what the screen says of the failure; the retry, which asks the same request again;
  // and the publishers' official sites, each named by what it publishes.
  failure: Object.freeze({
    line: "Lex could not complete this request. This is not an answer about the law.",
    retry: "Try again",
    official: "The publishers' official sites:",
    sites: Object.freeze({
      legilux: "Legilux, for Luxembourg law",
      eurlex: "EUR-Lex, for EU law",
    }),
  }),
  // The page at an address the site has no page for (R60: "A 404 page offers search"): it says so, says that this is about
  // the site's pages and not about the law, and offers the home page's search.
  notFound: Object.freeze({
    title: "Page not found",
    eyebrow: "Page not found",
    heading: "There is no page at this address",
    intro: "Check the address, or search the texts this site holds below. A page missing here says nothing about the law, only about this site's pages.",
    home: "Go to the home page",
  }),
  // The page the deploy kit can show while the site is deployed (R60): no answer, the same line's second half, and where the
  // law is published.
  maintenance: Object.freeze({
    title: "Maintenance",
    eyebrow: "Maintenance",
    heading: "Lex is being updated",
    intro: "This site is being updated and cannot answer for the moment. This page is not an answer about the law. Try again shortly.",
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
    // Between the items of a list said in one line: its spaces are its content (French sets a no-break
    // space before the semicolon).
    listSeparator: "; ",
    // The annexes of one EU wording a corpus disposition holds, never served as text (`europe-annexes.mjs`).
    europeAnnexes: Object.freeze({
      one: "{count} annex of the {language} wording is not served as text, and is never searched, quoted or exported: {reason}. Official source {source}.",
      other: "{count} annexes of the {language} wording are not served as text, and are never searched, quoted or exported: {reason}. Official source {source}.",
    }),
    // A Luxembourg act held only as first published (the plan's item 0.6, `as-published.mjs`): its label, what this site
    // holds of later versions, and the authenticity sentence of its case, with the act as published in {act}. The label
    // and the authenticity sentences are drafts pending the lawyer's read (the owner's item O4).
    asPublished: Object.freeze({
      label: "as published; may have been amended since; no consolidated text",
      laterNotHeld: "Later versions: not held.",
      laterNamed: Object.freeze({
        one: "Later versions: not held. The publisher's record names {count} consolidated version of this act, whose text this site does not hold:",
        other: "Later versions: not held. The publisher's record names {count} consolidated versions of this act, whose text this site does not hold:",
      }),
      consolidationFrom: "{iri}, applicable from {from} (publisher)",
      consolidationFromTo: "{iri}, applicable from {from} to {to} (publisher)",
      consolidationUndated: "{iri}, with no single applicability date stated by the publisher",
      endOfForce: "The publisher's record gives {date} as this act's end-of-force date.",
      authenticity: Object.freeze({
        electronic_journal_officiel: "This text was extracted and normalised by Lex, for information; it is not authentic. Since 1 January 2017, only the Journal officiel du Grand-Duché de Luxembourg (Mémorial A) as published electronically is authentic (loi du 23 décembre 2016, art. 5). The act as published: {act}.",
        printed_memorial: "This text was extracted and normalised by Lex, for information; it is not authentic. For an act published before 1 January 2017, the printed Mémorial is authentic, and the publisher's PDF is its digital reproduction. The act as published: {act}.",
        publication_date_not_stated: "This text was extracted and normalised by Lex, for information; it is not authentic. The publisher's record gives no single publication date for this act, so Lex does not say which edition is authentic. The act as published: {act}.",
      }),
    }),
  }),
  form: Object.freeze({
    workIdentifier: "Work identifier",
    workIdentifierOptional: "Work identifier (optional)",
    phrase: "Phrase",
    date: "Date",
    datePlaceholder: "yyyy-mm-dd",
    // Beside a search's date field: today, the calendar date in Luxembourg (R13), never the build's.
    today: "Today in Luxembourg: {date}.",
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

export const LIVE_CHROME = Object.freeze({ en: EN, fr: LIVE_CHROME_FR });

/**
 * What each reviewed language says for the refusal sentences the screens hold in English, keyed by that
 * English: the sentences by code, each screen's two sentences for a refusal named only by its code, and
 * the hints a card that cannot be shown still carries (`REFUSALS_FR`).
 */
export const REFUSAL_TRANSLATIONS = Object.freeze({ fr: REFUSALS_FR });

/**
 * The interface language this bundle was built for: English, unless the live build compiled it for
 * another reviewed language (`build-live.mjs` defines `__LEX_CHROME_LOCALE__`). One bundle, one
 * language: a page and the script that hydrates it are built together, so they say the same words.
 */
export const CHROME_LOCALE = typeof __LEX_CHROME_LOCALE__ === 'string' ? __LEX_CHROME_LOCALE__ : 'en';

/**
 * The `lang` that English text carries on this bundle's pages: none on an English page, whose own
 * language it is, and `en` on a page in any other, so a screen reader reads the platform's English
 * (its reasons, rules, scopes and statements) as English rather than in the page's voice.
 */
export const ENGLISH_LANG = CHROME_LOCALE === 'en' ? undefined : 'en';

/**
 * Where a live page's own file is served for this bundle's language: under the language's own path, `/en/` or `/fr/`
 * (the specification of record's R61; the root sends a reader to one of them). The home page is the language's root,
 * `/en/`, not its file.
 */
export function livePath(file) {
  return file === 'index.html' ? `/${CHROME_LOCALE}/` : `/${CHROME_LOCALE}/${file}`;
}

/** The interface copy for a reviewed language; any other throws, since serving it would be a substitution. */
export function liveChrome(locale = CHROME_LOCALE) {
  const table = LIVE_CHROME[locale];
  if (table === undefined) throw new Error(`no reviewed interface copy in ${JSON.stringify(locale)}: it answers localization_unavailable`);
  return table;
}

/** Every entry of a table as `[path, text]`, in the table's order. */
export function entriesOf(table, path = []) {
  return Object.entries(table).flatMap(([key, value]) => (typeof value === 'string' ? [[[...path, key].join('.'), value]] : entriesOf(value, [...path, key])));
}

function reviewedWording(locale, path, english) {
  let node = Object.hasOwn(REFUSAL_TRANSLATIONS, locale) ? REFUSAL_TRANSLATIONS[locale] : undefined;
  for (const key of path) node = node !== null && typeof node === 'object' && Object.hasOwn(node, key) ? node[key] : undefined;
  if (typeof node !== 'string') {
    throw new Error(`no reviewed ${JSON.stringify(locale)} wording of ${JSON.stringify(english)}: a reviewed language never says the English in its place`);
  }
  return node;
}

/** A refusal sentence a screen holds in English, as this bundle's language says it. */
export function refusalSentence(english, locale = CHROME_LOCALE) {
  return locale === 'en' ? english : reviewedWording(locale, ['sentences', english], english);
}

/**
 * One of the two templates a screen (`search`, `reading`, ...) says a refusal with when it names it only
 * by its code (`unexpected`, `unshown`), as this bundle's language says it.
 */
export function refusalTemplate(screen, kind, english, locale = CHROME_LOCALE) {
  return locale === 'en' ? english : reviewedWording(locale, ['templates', screen, kind], english);
}

/** A hint a refusal whose card cannot be shown still carries, as a template in this bundle's language. */
export function refusalHint(english, locale = CHROME_LOCALE) {
  return locale === 'en' ? english : reviewedWording(locale, ['hints', english], english);
}

/**
 * A run of English inside what a page in another language says: the reason a refusal's card cannot
 * be shown, or a message this interface has no reviewed wording for there (a transport failure, an
 * answer it cannot read, a request it will not send). On an English page it is plain text; elsewhere
 * it is set apart, so the page marks it `lang="en"` and nothing English passes as the page's language.
 */
export function englishRun(text) {
  return ENGLISH_LANG === undefined ? text : Object.freeze({ lang: ENGLISH_LANG, text });
}

/**
 * The sentence a state carries, from its runs (text, `englishRun`s, or `fillParts` made of both):
 * `sentence`, its whole text, and, when a run is English on a page in another language, `runs`, for
 * the page to lay out with that run marked. On an English page a state carries its sentence alone.
 */
export function statement(...runs) {
  const all = runs.flat();
  const sentence = all.map((run) => (typeof run === 'string' ? run : run.text)).join('');
  return all.every((run) => typeof run === 'string') ? { sentence } : { sentence, runs: Object.freeze(all) };
}

/** A state's sentence that has no reviewed wording in this bundle's language: said in English, and marked so. */
export function englishStatement(text) {
  return statement(englishRun(text));
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
