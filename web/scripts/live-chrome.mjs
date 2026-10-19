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
    // Only exact matching runs (the plan's item 4.5, R90): the relaxed lane is off until its check passes, so the page no
    // longer says it finds "every word of it".
    intro: "Finds the acts whose title matches the phrase and, in the text this server holds and may search, the articles that contain it exactly as typed: text its rights withhold is not searched. The phrase goes to this server in the request and nowhere else, and this page keeps nothing.",
    idle: "Type a phrase to search the article text this server holds and may search.",
    population: Object.freeze({
      one: "“{query}” in {language}: {strict} with the exact phrase, {relaxed} with every word, in {works} work.",
      other: "“{query}” in {language}: {strict} with the exact phrase, {relaxed} with every word, in {works} works.",
    }),
    notCounted: "not counted",
    lane: Object.freeze({ strict: "exact phrase", relaxed: "every word" }),
    // The title ladder's answer, on top (the plan's item 4.3): the act whose title the phrase is, begins or holds every word
    // of, linked to its dossier; several acts are counted and listed, the first few shown and the rest one step away. The
    // title stands last, after a colon, with no final sign of its own: most of the publisher's titles end with a full stop,
    // and French puts no bare title after "de" (the French review of this slice, entries 2 to 4).
    titleExact: "The phrase is the title of an act: {title}",
    titleStarts: "The phrase begins the title of an act: {title}",
    titleWords: "Every word of the phrase is in the title of an act: {title}",
    severalTitles: Object.freeze({
      one: "The phrase matches the title of {count} act:",
      other: "The phrase matches the titles of {count} acts:",
    }),
    moreTitles: Object.freeze({
      one: "{count} more act",
      other: "{count} more acts",
    }),
    noTitlesHeld: "This index holds no work titles, so the phrase was matched against article text only.",
    ambiguousHeading: "Works with several versions on {date}",
    ambiguousNote: "These works have more than one version that applies on that date, so none is chosen and none contributes a hit.",
    ambiguousWork: "{work}: {candidates}",
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
    // What a dated search searched, by the interval rules (the review of #987, F5: the rule before index 9 is no longer the one).
    dated: "Only Luxembourg acts were searched. For each, the version whose publisher dates cover {date} was searched; an act with no such version, or for which this site does not hold that version's text, was not. An act held only as published is searched as published, and may have been amended since.",
    undated: "Only Luxembourg acts were searched, with no date: every version this site holds was searched, and each result names its version's date.",
    // A search with no work named searches Luxembourg law alone, and its no-hit sentence says so.
    noHitLuxembourg: "No article of the Luxembourg text this server holds and may search contains “{query}”.",
    // Under the home page's box (and the 404 page's): what a search by words covers, what an exact reference opens (the
    // plan's item 4.2), and where words are searched within an EU act.
    // "when this site holds it" (the review of #974, F1): a reference to an act this site does not hold opens nothing.
    searchScope: "A search by words covers Luxembourg law. An ELI, a CELEX number, a Mémorial reference, a permalink, or the citation of an article of an act, with or without a date, opens that act, Luxembourg or EU, when this site holds it. To search within an EU act, name it by its CELEX number on the {link}.",
    searchPage: "search page",
    // What an exact reference was read as (the plan's item 4.2), said before its act opens and above a refusal of it: the
    // act, the article and the date as read, the date in words. A date the reference does not name is said apart.
    recognised: "Recognised: {act}.",
    recognisedArticle: "Recognised: {act}, article {article}.",
    recognisedDate: "Recognised: {act}, at {date}.",
    recognisedArticleDate: "Recognised: {act}, article {article}, at {date}.",
    recognisedToday: "No date was given: the date read is today in Luxembourg, {date}.",
    recognisedSearchDate: "The reference names no date: the date read is the search's, {date}.",
    opensReading: "Opening the reading: {link}",
    opensDossier: "Opening the dossier: {link}",
    notOpenedDate: "“{date}” names no day of the calendar, so nothing was opened and no word was searched.",
    notOpenedTwoDates: "The reference names two dates that disagree, so nothing was opened and no word was searched.",
    // The review of #974: a citation by a title this site holds no act under is searched as words, and the page says so; the
    // acts a reference may name are listed, one only the publisher's catalogue names said as not held; and an absent act
    // the catalogue names is named under its card.
    titleNotHeld: "This site holds no act under the title the phrase cites, so its words were searched.",
    candidateNotHeld: "{act} (this site holds no version of it)",
    catalogueNamesIt: "The publisher's record names this act ({works}), and this site holds no version of it.",
    // Results by act (the plan's item 4.3; R59, R21, R11): what was found, counted from the answer; what was searched, every
    // count the answer's and dated by the index's build; each act once, with its passages below it, each version dated in the
    // publisher's words ("applicable from", R21), and an act held only as first published said so, as the reading says it.
    found: "“{query}” in {language}: {passages} with the exact phrase, in {acts}.",
    passages: Object.freeze({
      one: "{count} passage",
      other: "{count} passages",
    }),
    searched: "Searched: {acts} of Luxembourg law in {language}, in {versions} with {articles} of searchable text, as of the index built on {date}.",
    searchedUndated: "Searched: {acts} of Luxembourg law in {language}, in {versions} with {articles} of searchable text; the index this site holds records no build date.",
    withheld: Object.freeze({
      one: "{count} version whose rights withhold its text was not searched.",
      other: "{count} versions whose rights withhold their text were not searched.",
    }),
    severalOnDate: Object.freeze({
      one: "{count} act with several versions on {date} was not searched, since none is chosen.",
      other: "{count} acts with several versions on {date} were not searched, since none is chosen.",
    }),
    untitled: "{work} (this index holds no title for this act)",
    actPassages: Object.freeze({
      one: "{count} passage in this act.",
      other: "{count} passages in this act.",
    }),
    version: "Version applicable from {date} (publisher)",
    versionAsPublished: "The act as published, applicable from {date} (publisher)",
    // An act dated by its entry into force (index 9's date_role, S7a; R21), and a passage an undated search folded the other
    // versions holding its wording into.
    versionAsPublishedEntryIntoForce: "The act as published, entry into force on {date} (publisher)",
    sameWording: Object.freeze({
      one: "The same wording is in {count} other version this site searched.",
      other: "The same wording is in {count} other versions this site searched.",
    }),
    // The plain no-hit card (the plan's item 4.5; R59, R90): which searches ran, that only exact matching ran and what that
    // means, the population searched (the line above), and the publishers' own search pages, as links only.
    searchesRun: "What was searched",
    runReference: "Exact references: the phrase is no reference this site reads (an ELI, a CELEX number, a Mémorial reference, a permalink or a citation).",
    runReferenceNotRun: "Exact references: not read, since a work was named.",
    runTitles: "Titles: no title of an act this site holds matches the phrase.",
    runTitlesMatched: Object.freeze({
      one: "Titles: the phrase matches the title shown above.",
      other: "Titles: the phrase matches the titles shown above.",
    }),
    // How the titles were matched, said under the titles line when the ladder ran: unlike the text, case and accents are
    // ignored (the French review of this slice, its point 4).
    titleMatching: "Titles are matched with capitals and accents ignored.",
    runTitlesNotRun: "Titles: not searched, since a work was named.",
    runText: "Text: no article searched contains the phrase exactly as typed.",
    runTextWords: "Text, every word in any order: no article searched contains them all.",
    exactOnly: "Only exact matching ran on the text: capitals, accents and every other character of the phrase count, and nothing is folded, stemmed or expanded. A looser search of the text stays off until its own check has passed.",
    officialSearch: "The publishers' own search pages:",
    officialSites: Object.freeze({
      legilux: "Legilux search, for Luxembourg law",
      eurlex: "EUR-Lex advanced search, for EU law",
    }),
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
    // The span of the states when the first is the act as published dated by its entry into force (S7b; R21): every state so
    // dated (one per language, all on one date), or the first so dated and the latest applying from its date; and the same in
    // one language.
    statesEntryIntoForce: Object.freeze({
      one: "{count} state, the act as published, dated by its entry into force on {from} (publisher).",
      other: "{count} states, the act as published in each of its languages, dated by its entry into force on {from} (publisher).",
    }),
    statesFromEntryIntoForce: "{count} states: the first, the act as published, dated by its entry into force on {from} (publisher); the latest applying from {to}.",
    statesInEntryIntoForce: "{count} state in {language}, the act as published, dated by its entry into force on {from} (publisher).",
    statesInFromEntryIntoForce: "{count} states in {language}: the first, the act as published, dated by its entry into force on {from} (publisher); the latest applying from {to}.",
    // The publisher's catalogue between the versions (index 9's chain, S7b; its web S7c): the versions whose text is not held,
    // each named as the refusal card names it, then the gaps, overlaps, same-date groups, unclear end days and invalid intervals.
    chainHeading: "The publisher's catalogue between versions",
    textlessHeading: "Versions whose text this site does not hold",
    textlessRow: "{version}: {why}.",
    gap: "From {from}, the end the publisher's catalogue gives for one version, to {to}, when the next begins, the catalogue lists no version: a gap this site does not fill.",
    overlap: "From {from} to {to}, two versions in the publisher's catalogue are both applicable (publisher).",
    sameDate: "On {date}, the publisher's catalogue lists {count} versions.",
    unclearEndDay: "{date} is the end the publisher's catalogue gives for one version, the day before the next begins; whether that day belongs to it is unclear, and this site gives it to neither.",
    invalidInterval: "The version from {date} has an end in the publisher's catalogue, {to}, on or before its own start, so it covers no date; its own link still opens it.",
    chainNote: "These are the dates of the publisher's catalogue as this site read it; a gap in the catalogue is not a gap in the law.",
    // A state dated by its entry into force (S7a; R21), in the date column, and what that mark means, said once under the table.
    entryIntoForceDate: "no date stated (entry into force: {date})",
    entryIntoForceNote: "The mark “no date stated” means that the act's file states no exact applicability date; the date in brackets is the act's entry into force in the publisher's record, not an applicability date.",
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
    citedWithoutText: "Held without text in {source}.",
    wholeAct: "The whole act at this version",
    europeWholeAct: "The whole act in this wording",
    newerVersion: "A newer version is applicable from {date} (publisher).",
    europeNewerWording: "A later wording of this act is held: the wording of {date}.",
    europeLaterWordingNotHeld: "The publisher's record names, for this act, a later wording of {date}, whose text this site does not hold in this language.",
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
    // A state dated by its entry into force (index 9's date_role, S7a; R21): never said as applying from that date.
    stateHeadingEntryIntoForce: "{work}, {language}, the act as published, dated by its entry into force on {from} (publisher)",
    stateHeadingEntryIntoForceNext: "{work}, {language}, the act as published, dated by its entry into force on {from} (publisher) (the next state held applies from {next})",
    // The places of the publisher's document before an article that its version holds no article at (S7a).
    missingBefore: Object.freeze({
      one: "At this point, the publisher's document has an article that this site does not hold in this version.",
      other: "At this point, the publisher's document has {count} articles that this site does not hold in this version.",
    }),
    counts: Object.freeze({
      one: "{count} article quoted, {withoutText} held without text, {notAdmitted} not admitted; {conflicts} with their own date differing from the state's.",
      other: "{count} articles quoted, {withoutText} held without text, {notAdmitted} not admitted; {conflicts} with their own date differing from the state's.",
    }),
    validityConflict: "This article's own date is {own}; its state applies from {state}.",
    // The same for a state dated by its entry into force (R21; the review of #987, F5): never said as applying from it.
    validityConflictEntryIntoForce: "This article's own date is {own}; its state is the act as published, dated by its entry into force on {state} (publisher).",
    // S7c: an expression in another language than French that the publisher issued (index 9's translation_of; R18), and what a
    // consolidated version or a code's version is and is not (the design's L5 caveat; spec 33 §3), with the act it consolidates.
    translation: "This text is the publisher's {language} expression of the act, beside its French expression {french}. The publisher's record does not state which language version is authentic.",
    consolidationCaveat: "A consolidated version is documentation without probative value. Only the texts published in the Journal officiel (Mémorial A) are authentic: the act as published, {act}, and the acts that amended it, whose list Lex does not yet hold.",
    codeCaveat: "A version of a code is documentation without probative value: the authentic texts are the acts published in the Journal officiel (Mémorial A) that it brings together.",
    // Content /3 (S3b): the publisher's deleted wording, struck through, said to a screen reader at its start and end; the
    // sentence under an article that holds some; a note whose reference the publisher wrote without a marker.
    deletedWordingStart: "Wording the publisher marks deleted:",
    deletedWordingEnd: "End of the wording marked deleted.",
    deletedWordingNote: "Struck-through wording is wording the publisher marks deleted in its text. It is not part of the text this site quotes, cites or searches.",
    unmarkedNote: "no marker",
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
    // The cited article's citations, from the one citation generator (`citation.mjs`; the plan's item 3.3, R65): each form is
    // said in the language of the text it cites (`citation` below), under these labels in the page's.
    cite: "Cite this article",
    citeConventional: "Usual citation",
    citeWithPermalink: "With its permalink",
    citeEvidence: "Evidence form",
    citeNotComposed: "Lex composes no usual citation for this article; its permalink cites it.",
    // Reading by page (the plan's item 3.7): a long version's contents, by page, the pages shown, and the links to the pages
    // before and after them, which the page loads in place when it can.
    contents: "Contents",
    contentsPage: "Page {number}: {first} to {last}",
    contentsPageOne: "Page {number}: {first}",
    pageShown: "Page {number} of {pages}: {first} to {last}.",
    pageShownOne: "Page {number} of {pages}: {first}.",
    pagesShown: "Pages {from} to {to} of {pages}: {first} to {last}.",
    pager: "Pages",
    previousPage: "Previous articles",
    nextPage: "Next articles",
    nextPageFailed: "The next articles could not be loaded here; the link opens them on a page of their own.",
    // A page past a version's last (the review of #978, F2): the pages are this site's reading's, not the publisher's.
    pageNotHeld: Object.freeze({
      one: "This version is read here on {count} page, so there is no page {page}.",
      other: "This version is read here on {count} pages, so there is no page {page}.",
    }),
    europePageNotHeld: Object.freeze({
      one: "This wording is read here on {count} page, so there is no page {page}.",
      other: "This wording is read here on {count} pages, so there is no page {page}.",
    }),
    firstPage: "First page",
    lastPage: "Last page (page {pages})",
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
    // The lineage's first row dated by its entry into force (R21; the review of #987).
    carriedEntryIntoForce: Object.freeze({
      one: "Carried by {count} held state, the act as published, dated by its entry into force on {from} (publisher);",
      other: "Carried by {count} held states, the first being the act as published, dated by its entry into force on {from} (publisher);",
    }),
    carriedInEntryIntoForce: Object.freeze({
      one: "Carried by {count} held state in {language}, the act as published, dated by its entry into force on {from} (publisher);",
      other: "Carried by {count} held states in {language}, the first being the act as published, dated by its entry into force on {from} (publisher);",
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
    absentRowEntryIntoForce: "{language}, the act as published, dated by its entry into force on {from} (publisher): {permalink}",
    // Index 9's continuity breaks (S7b): the runs of dates no version whose text this site holds covers, each with why, the
    // mark on a row after one, and what an unchanged wording across one does not say.
    breaksHeading: "Dates no version with held text covers",
    breakTo: "In {language}, from {from} to the day before {to}: {reasons}.",
    breakOpen: "In {language}, from {from}, with no later version whose text this site holds: {reasons}.",
    breakReasons: Object.freeze({
      withheld_rights: "a version whose rights did not admit its text",
      not_available_pdf: "a version whose publisher file is a PDF, which this site does not read",
      not_available_derivation: "a version whose text could not be read from the publisher's file",
      not_available_misfiled: "a version whose publisher file is another version's",
      not_acquired: "a version this site did not acquire",
      catalogue_hole: "a gap in the publisher's catalogue",
      publisher_end_convention_unclear: "a day the publisher's catalogue leaves unclear between two versions",
      publisher_interval_invalid: "a version whose end is on or before its start",
      no_expression_in_language: "a version with no expression held in this language",
    }),
    afterBreak: "after dates no version with held text covers",
    breaksNote: "No version whose text this site holds covers these dates, so an unchanged wording across them says nothing of them.",
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
    // A side dated by its entry into force (S7b; R21): the act as published, never "the state applying from" that date.
    sideEntryIntoForce: "{side}: the act as published, dated by its entry into force on {from} (publisher), {articles} articles, {conflicts} with their own date differing from the state's, {permalink}",
    sideEntryIntoForceNext: "{side}: the act as published, dated by its entry into force on {from} (publisher) (the next state held applies from {next}), {articles} articles, {conflicts} with their own date differing from the state's, {permalink}",
    counts: "{changed} changed, {added} added, {removed} removed, {unchanged} unchanged.",
    // Both dates select one state: said in the publisher's clock, never "applied" (R21; the plan's item 0.6).
    sameVersion: "Both dates fall within the same version, applicable from {from} (publisher).",
    // Both dates select the one state of an act held only as published (the review of #964): what is held, never one
    // version covering both dates. The publisher may date a consolidated version, or an end-of-force date, between them,
    // and the card names each, a version in the as-published block's form (`common.asPublished.consolidationFromTo`).
    sameTextHeld: "Both dates select the one text held, the act as published, applicable from {from} (publisher); later versions are not held.",
    sameTextHeldOfSeveral: "Both dates select the same text held, the act as published, applicable from {from} (publisher); no consolidated version is held.",
    // The same three when that state is dated by its entry into force (S7b; R21), as the platform's note says them.
    sameVersionEntryIntoForce: "Both dates fall within the same version, the act as published, dated by its entry into force on {from} (publisher).",
    sameTextHeldEntryIntoForce: "Both dates select the one text held, the act as published, dated by its entry into force on {from} (publisher); later versions are not held.",
    sameTextHeldOfSeveralEntryIntoForce: "Both dates select the same text held, the act as published, dated by its entry into force on {from} (publisher); no consolidated version is held.",
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
      no_held_baseline: "the day before it is covered by no state whose text this site holds, so there is no baseline",
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
    itemEntryIntoForce: "{article} ({language}, the act as published, entry into force on {from}): {citation}, text digest {digest}, official source {source}",
    excludedEntryIntoForce: "{article} ({language}, the act as published, entry into force on {from}): excluded, {reason}, {citation}",
    // An EU export's: its text is served with Decision 95's acknowledgement, and each article is dated as its wording is.
    europeRights: "Text served with the acknowledgement {acknowledgement}, which every exported article carries, and the authenticity statement below.",
    europeItem: "{article} ({language}, wording of {date}): {citation}, text digest {digest}, official source {source}",
    europeExcluded: "{article} ({language}, wording of {date}): excluded, {reason}, {citation}",
    save: Object.freeze({ json: "Save as JSON", csv: "Save as CSV", pdf: "Save as PDF" }),
    formatRefused: "{format} is not offered for this export: {reason}.",
    composeFailed: "This export cannot be composed: {reason}.",
    jsonSummary: "The JSON as it will be saved",
    // Each exported article's usual citation (`citation.mjs`), in its text's language; the file carries the three forms.
    usualCitation: "Usual citation: {citation}",
    // Reading by page (the plan's item 3.7): a version's every article pinned at once, its other pages read first.
    pinAll: "Pin every article",
    readingPages: "Reading every page: {done} of {pages} read.",
    pagesFailed: "Not every page could be read; nothing more was pinned.",
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
    // A calendar date in words (a reference's date printed back as it was read): the day, the month's name, the year; the
    // first of a month is written as the language writes it.
    longDate: "{day} {month} {year}",
    firstDay: "{day}",
    months: Object.freeze(["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"]),
    // The annexes of one EU wording a corpus disposition holds, never served as text (`europe-annexes.mjs`).
    europeAnnexes: Object.freeze({
      one: "{count} annex of the {language} wording is not served as text, and is never searched, quoted or exported: {reason}. Official source {source}.",
      other: "{count} annexes of the {language} wording are not served as text, and are never searched, quoted or exported: {reason}. Official source {source}.",
    }),
    // The EU act's dates as the publisher records them in its dated statements (the plan's item 2.1; R79's E1, R21: each date a
    // value of its own, said with its role; `europe-force-dates.mjs`), where the act is read or its dossier shown: the act's
    // dates, never a wording's. A qualifier is the publisher's note in its own codes, said word for word and never translated.
    europeForceDates: Object.freeze({
      heading: "The publisher's dates this site holds for this act",
      roles: Object.freeze({
        entry_into_force: "Entry into force: {date}",
        application: "Application: {date}",
        end_of_validity: "End of validity, in whole or in part: {date}",
        deadline: "Deadline: {date}",
        signature: "Signature: {date}",
        not_pinned: "A date with the publisher's code {code}, to which this site gives no role: {date}",
      }),
      notPinnedNoCode: "A date with no code from the publisher, to which this site gives no role: {date}",
      openEnd: "End of validity: no date (the publisher records {date}, its mark of an open end)",
      qualifier: "(the publisher's qualifier, word for word: {qualifier})",
      roleNames: Object.freeze({
        entry_into_force: "entry into force",
        application: "application",
        end_of_validity: "end of validity",
        deadline: "deadline",
      }),
      notHeld: "Not held for this act: {roles}. The publisher's dated statements this site holds for it give no such date, which says nothing of whether the act has one.",
      conflict: "The publisher records more than one entry into force for this act ({dates}): each is shown, and this site chooses none.",
      notWording: "None of the dates above is used to choose a wording: each wording below carries its own date.",
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
  // The one citation generator's words (`citation.mjs`; the plan's item 3.3, the specification of record's R65). A citation is
  // said in the language of the text it cites, never the interface's, so the generator reads this section from the table of
  // that language whatever the page's: `fra` from the French table, `eng` from this one. The usual form is French legal
  // usage for a Luxembourg act and EUR-Lex's for an EU act, in words the exact-reference reader of the plan's item 4.2 reads:
  // an act's type and date or an EU act's name, an article number, and a date after "dans sa version applicable au", "au" or
  // "as at". The evidence form's lines say what each value is, and what the index does not hold.
  citation: Object.freeze({
    months: Object.freeze(["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"]),
    date: "{day} {month} {year}",
    firstOfMonth: "{day} {month} {year}",
    withPermalink: "{citation} ({permalink})",
    luxembourg: Object.freeze({
      article: "Article {number}",
      articleFirst: "Article {number}",
      articleUnique: "Sole article",
      acts: Object.freeze({
        loi: "{article} of the Law of {date}",
        rgd: "{article} of the Grand-Ducal Regulation of {date}",
        agd: "{article} of the Grand-Ducal Decree of {date}",
        rmin: "{article} of the Ministerial Regulation of {date}",
        amin: "{article} of the Ministerial Decree of {date}",
        code: "{article} of the {code}",
      }),
      // How the publisher's title of an act of each type starts, so the words after its type and date can follow them.
      titles: Object.freeze({
        loi: "Law of {date}",
        rgd: "Grand-Ducal Regulation of {date}",
        agd: "Grand-Ducal Decree of {date}",
        rmin: "Ministerial Regulation of {date}",
        amin: "Ministerial Decree of {date}",
      }),
      version: "{citation}, as applicable on {date}",
      // A version dated by its entry into force (S7a; R21): said as that, never as applicable on it.
      versionEntryIntoForce: "{citation}, as published, entry into force on {date}",
    }),
    europe: Object.freeze({
      article: "Article {number}",
      acts: Object.freeze({
        R: "{article} of Regulation {designation}",
        L: "{article} of Directive {designation}",
        D: "{article} of Decision {designation}",
      }),
      domains: Object.freeze({ EU: "EU", EC: "EC", EEC: "EEC" }),
      numbered: "({domain}) No {number}/{year}",
      numberedNoDomain: "No {number}/{year}",
      yearFirst: "({domain}) {year}/{number}",
      suffixed: "{year}/{number}/{domain}",
      plain: "{year}/{number}",
      version: "{citation}, as at {date}",
      // The platform's authenticity statement for EU text (`V3CorpusMount.EuropeTextAuthenticity`), word for word: the
      // generator says it in the cited text's language only when the answer says exactly this.
      authenticity: "Only the Official Journal of the European Union published in electronic form is authentic and produces legal effects (Regulation (EU) No 216/2013, Article 1(2)); this text is a reproduction read from the Publications Office's Formex package, not the authentic edition.",
    }),
    evidence: Object.freeze({
      permalink: "Permalink: {permalink}",
      eli: "ELI: {eli}",
      celex: "CELEX: {celex}",
      title: "Title (publisher): {title}",
      version: "Version: applicable from {date} (publisher)",
      versionEntryIntoForce: "Version: the act as published, entry into force on {date} (publisher)",
      memorial: "Published in: Mémorial A No {number} of {year} ({iri})",
      memorialIri: "Published in: {iri}",
      memorialNotHeld: "Published in: the issue of the Mémorial A is not held by this index.",
      publicationDate: "Publication date (publisher): {date}",
      originalWording: "Wording: the original wording of {date}",
      consolidatedWording: "Wording: the consolidated wording of {date}",
      officialJournalNotHeld: "Published in: the Official Journal reference is not held by this index.",
      textSha256: "Text digest (SHA-256): {digest}",
      noText: "Text digest (SHA-256): none, the article is held without text.",
      consulted: "Consulted on Lex on {date} (date in Luxembourg).",
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
