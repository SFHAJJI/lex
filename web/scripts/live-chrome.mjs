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
    // Only the phrase is matched (the plan's item 4.5, R90): the relaxed lane is off until its check passes, so the page no
    // longer says it finds "every word of it"; capitals and accents are ignored (the plan's item 4.4), so it no longer says
    // "exactly as typed".
    intro: "Finds the acts whose title matches the phrase and, in the text this server holds and may search, the articles that contain it, with capitals and accents ignored: text its rights withhold is not searched. The phrase goes to this server in the body of the request, never in the request's address; this page keeps the search only in its own address, in the part after “#”, which the browser never sends, so a link to that address shares the search.",
    idle: "Type a phrase to search the article text this server holds and may search.",
    population: Object.freeze({
      one: "“{query}” in {language}: {strict} with the phrase, {relaxed} with every word, in {works} work.",
      other: "“{query}” in {language}: {strict} with the phrase, {relaxed} with every word, in {works} works.",
    }),
    notCounted: "not counted",
    lane: Object.freeze({ strict: "phrase", relaxed: "every word" }),
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
    // The EU acts a search that names no work found by their titles (EU index /7, Release 3's item 1;
    // `europe-publication.mjs`): each act once, by its title in the language that matched, linked to its dossier; their text
    // is not searched here.
    euTitleExact: "The phrase is the title of an EU act this site holds: {title}",
    euTitlePhrase: "The phrase is in the title of an EU act this site holds: {title}",
    euTitles: Object.freeze({
      one: "The phrase is in the title of {count} EU act this site holds:",
      other: "The phrase is in the titles of {count} EU acts this site holds:",
    }),
    euTitleAct: "{title} ({celex})",
    euTitlesSearched: "The titles of the EU acts this site holds were matched too; the text of those acts was not searched.",
    runEuTitles: "EU titles: the phrase is in no title of an EU act this site holds.",
    runEuTitlesMatched: Object.freeze({
      one: "EU titles: the phrase is in the title of the EU act shown above.",
      other: "EU titles: the phrase is in the titles of the EU acts shown above.",
    }),
    ambiguousHeading: "Works with several versions on {date}",
    ambiguousNote: "These works have more than one version that applies on that date, so none is chosen and none contributes a hit.",
    ambiguousWork: "{work}: {candidates}",
    noHit: "No article of the text this server holds and may search contains “{query}”.",
    noText: "This index holds no searchable text in {language}; it holds text in {languages}.",
    nextPage: "Next page",
    // An EU act's search (the audit of 2026-10-27, gap 5): its passages counted by the wordings they are in, what was
    // searched, with no date or at the date asked, the wordings not searched, and the other wordings that hold a passage's
    // text when an undated search showed it once. Each wording is headed as the reading heads it (`reading.europe…`).
    euFound: "“{query}” in {language}: {passages} with the phrase, in {wordings} of {celex}.",
    euWordings: Object.freeze({
      one: "{count} wording",
      other: "{count} wordings",
    }),
    euSearched: "Searched: {wordings} of {celex} in {language}, with {articles} of searchable text.",
    euDated: "Searched at {date}: the one wording of this EU act that date selects, the latest dated on or before it.",
    euUndated: "Searched with no date: each wording of this EU act, original or consolidated, whose text this site holds and may search; where several of them hold the same text of an article, that text is shown once, under the latest of them.",
    euNotSearched: Object.freeze({
      one: "{count} other wording of this act was not searched: its text is not held here or its rights withhold it, two different texts carry its date, or the publisher gives it no usable date.",
      other: "{count} other wordings of this act were not searched: their text is not held here or their rights withhold it, two different texts carry their date, or the publisher gives them no usable date.",
    }),
    euSameWording: Object.freeze({
      one: "The same text is in {count} other wording this site searched.",
      other: "The same text is in {count} other wordings this site searched.",
    }),
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
    opensReading: "This reference opens the reading: {link}",
    opensDossier: "This reference opens the dossier: {link}",
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
    found: "“{query}” in {language}: {passages} with the phrase, in {acts}.",
    passages: Object.freeze({
      one: "{count} passage",
      other: "{count} passages",
    }),
    searched: "Searched: {acts} of Luxembourg law in {language}, in {versions} with {articles} of searchable text, as of the index built on {date}.",
    searchedUndated: "Searched: {acts} of Luxembourg law in {language}, in {versions} with {articles} of searchable text; the index this site holds records no build date.",
    // An index older than today: both dates (R13; the plan's item 3.4), today the date in Luxembourg the answer was given on.
    searchedOlder: "Searched: {acts} of Luxembourg law in {language}, in {versions} with {articles} of searchable text, as of the index built on {date}; today in Luxembourg is {today}.",
    // A fragment written as a search that the form will not send (the plan's item 3.5): nothing of it is repeated.
    addressUnread: "The search in this page's address could not be read, so nothing was searched.",
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
    // How the titles were matched, said under the titles line when the ladder ran: as the text is, with case and accents
    // ignored (the French review of this slice, its point 4).
    titleMatching: "Titles are matched with capitals and accents ignored.",
    runTitlesNotRun: "Titles: not searched, since a work was named.",
    // The title ladder's line when the EU acts' titles were matched too (EU index /7): the Luxembourg titles said as such, so
    // the two lines never contradict each other (the French review of 2026-10-27, its point 2).
    runLuxembourgTitles: "Luxembourg titles: no title of a Luxembourg act this site holds matches the phrase.",
    runLuxembourgTitlesMatched: Object.freeze({
      one: "Luxembourg titles: the phrase matches the title of the Luxembourg act shown above.",
      other: "Luxembourg titles: the phrase matches the titles of the Luxembourg acts shown above.",
    }),
    runText: "Text: no article searched contains the phrase, even with capitals and accents ignored.",
    runTextWords: "Text, every word in any order: no article searched contains them all.",
    exactOnly: "Only the phrase was matched in the text, with capitals and accents ignored, œ and æ read as oe and ae, the typographic apostrophes as the straight one, a no-break space as a space and a soft hyphen as nothing: every other character of it counts, hyphens and punctuation included, and nothing is stemmed or expanded. A looser search of the text stays off until its own check has passed.",
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
    overlap: "From {from} to the day before {to}, two versions in the publisher's catalogue are both applicable (publisher).",
    sameDate: "On {date}, the publisher's catalogue lists {count} versions.",
    unclearEndDay: "{date} is the end the publisher's catalogue gives for one version, the day before the next begins; whether that day belongs to it is unclear, and this site gives it to neither.",
    invalidInterval: "The version from {date} has an end in the publisher's catalogue, {to}, on or before its own start, so it covers no date; its own link still opens it.",
    chainNote: "These are the dates of the publisher's catalogue as this site read it; a gap in the catalogue is not a gap in the law.",
    // The act's force, as the publisher records it (R79, E3): its own status token and dates, each said with its role (R21),
    // never read into whether the act applies to anyone.
    forceHeading: "The act's force, as the publisher records it",
    forceStatus: "Status, in the publisher's own words: {status}.",
    forceStatusNone: "The publisher's record gives no status for this act.",
    forceEntry: "Entry into force: {date} (the publisher's record).",
    forceEntryNone: "The publisher's record gives no entry-into-force date for this act.",
    forceEnd: "End of force: {date} (the publisher's record).",
    forceOtherForms: "The publisher's record also gives an entry-into-force or end-of-force date in a form that is not a calendar date; it is not shown here.",
    forceNote: "These are the publisher's own statements about the act as a whole, shown as it gives them. They say nothing of a particular article or version, and Lex does not say whether or how the act applies to anyone's situation.",
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
    // A readable address that reads the act on another day than today (`?date=`, the plan's item 3.4; R13): the date it
    // reads, and today beside it, with the link to the act at today's date.
    notToday: "This page reads the act on {date}, as this site holds it for that date; today in Luxembourg is {today}.",
    title: "Reading",
    eyebrow: "Reading",
    heading: "The text on a date",
    intro: "The text of one Luxembourg work as it stood on one date, or of one EU work in the wording that answers that date (its original wording or a later consolidated one), article by article, as the publisher wrote it, with what a quotation of it needs. The identifier and the date go to this server in the request and nowhere else, and this page keeps nothing.",
    idle: "Type a work identifier and a date to read its text on that date.",
    rights: "Text served under {rights}. Read on {date}.",
    rightsIn: "Text served under {rights}. Read on {date} in {language}.",
    // The L5 single-channel disclosure (LU-DATA-DESIGN v2 §1.2, §5): a text served on the publisher's catalogue alone, under
    // one of the owner's rulings, says so beside the text with the date its licence page was observed. Drafts the owner revises.
    disclosure: Object.freeze({
      lu_rights_publisher_catalogue_only_file_silent: "This file does not state its own licence. It is served on the publisher's catalogue alone, which declares CC BY 4.0 for this file, as observed on {date}.",
      lu_rights_publisher_catalogue_only_own_block_empty: "This file's own metadata names it but states no licence. It is served on the publisher's catalogue alone, which declares CC BY 4.0 for this file, as observed on {date}.",
      lu_rights_publisher_catalogue_only_file_names_other_identity: "This file's licence statement names another publication, not this file. It is served on the publisher's catalogue alone, which declares CC BY 4.0 for this file, as observed on {date}.",
    }),
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
    // The copy button beside each citation form (R65), which only a browser running the page's script shows.
    copyCitation: "Copy",
    copyCitationLabel: "Copy: {form}",
    citationCopied: "Citation copied.",
    citationNotCopied: "This browser did not copy the citation; select its text instead.",
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
    // R57 (the audit of 2026-10-27, gap 2): the wording that changed is shown, and a comparison has an address of its own.
    intro: "The states of one Luxembourg work applicable on two dates, compared by the publisher's article ids and wording, with nothing said about legal effect; the wording that changed is shown, the text deleted struck through and the text inserted underlined. The identifier and the dates go to this server in the request; when the act is named by its address on this site, they are also written into this page's address, so that the comparison can be shared and read without JavaScript; this page keeps nothing.",
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
    // A changed article's wording, inline (R57): what a screen reader, or a reader without the page's styles, hears around
    // each deletion and insertion, which the eye sees struck through and underlined.
    deletedStart: "[deleted:",
    deletedEnd: "]",
    insertedStart: "[inserted:",
    insertedEnd: "]",
    // R57's jump list, and the links from each change to the previous and the next.
    jumpHeading: "The changes",
    jumpItem: "{article}: {status}",
    previousChange: "Previous change",
    nextChange: "Next change",
    // A side whose text this site does not serve (its rights did not admit it): the digests stand, and no wording is shown.
    wordingNotServed: Object.freeze({
      from: "This article's wording is not shown: this site does not serve the text of the version at the from date.",
      to: "This article's wording is not shown: this site does not serve the text of the version at the to date.",
      both: "This article's wording is not shown: this site serves the text of neither version.",
    }),
    wordingWhole: "This article changed too much to be compared word by word here: apart from any beginning or end its two texts share, its text at the from date is shown deleted and its text at the to date inserted, each in one piece.",
    // The wording of the changes in pages (the review of #1003, F4: at most 50 articles and 96 KiB a page, as a reading's), the
    // page shown by its first and last article, and the links to the pages before and after it.
    wordingPage: "The wording that changed is shown in pages; this is page {page} of {pages}: {first} to {last}.",
    wordingPageOne: "The wording that changed is shown in pages; this is page {page} of {pages}: {first} only.",
    wordingOtherPage: "This article's wording is on another page of this comparison.",
    previousPage: "Previous page of the wording",
    nextPage: "Next page of the wording",
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
    // A row whose state is an act dated by its entry into force, said as that (R21; the review of #987), as History says it.
    rowEntryIntoForce: "{work}, {language}, the act as published, dated by its entry into force on {from} (publisher): {verdict}. {permalink}",
    wording: Object.freeze({ changed: "wording changed", unchanged: "wording unchanged" }),
    compared: "{wording} from the state of {date}{baseline}: {changed} changed, {added} added, {removed} removed, {unchanged} unchanged",
    notCompared: "not compared: {reason}{named}",
    // The same after a row naming the act as published (`rowEntryIntoForce`), a twin for a language that agrees it with "act".
    notComparedAct: "not compared: {reason}{named}",
    reason: Object.freeze({
      first_held_state: "the first state this index holds for the work and language, so there is nothing to compare it with",
      ambiguous_version: "several states of the work cover this date, so none is compared",
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
    // What the card holds and what it does not (the audit of 2026-10-27, overclaim 3): said above its verdict, whatever it is,
    // so "every machine gate on this card passes" is never read as every gate Lex has.
    scope: "This card holds only the machine gates listed below ({gates}). It does not hold the gates on the data (that every count matches the publisher's and that every admitted file yields text), on the quality of answers, on speed, or on two systems deriving the same index, and it tests none of the refusal codes no operation sends ({codes}).",
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
  // The breadcrumbs of a law's pages (the specification of record's R48: "breadcrumbs (publisher, act, article, version)";
  // `breadcrumbs.mjs`): the menu's name, the law each publisher publishes, and a version said by its date's role (R21): a
  // Luxembourg state as applying from its date, or the act as published dated by its entry into force, never as applying
  // from it; an EU wording by its own date, never with a Luxembourg word. The act and the article are the page's own codes.
  breadcrumbs: Object.freeze({
    label: "Breadcrumb",
    publishers: Object.freeze({
      "lu-legilux": "Luxembourg law",
      "eu-eurlex": "EU law",
    }),
    state: "State applying from {date}",
    stateEntryIntoForce: "Act as published, dated by its entry into force on {date} (publisher)",
    europeOriginal: "Original wording of {date}",
    europeConsolidated: "Consolidated wording of {date}",
  }),
  // How current the law this site holds is, and the commits behind an answer (the specification of record's R87 and R23;
  // the plan's items 6.1 and 6.5; `freshness.mjs`, `Freshness.jsx`): the strip in every page's footer and the staleness
  // banner under its header, each said from the server's answer and nothing else. Every date is the server's, in
  // Luxembourg; every count is the answer's (R11). Until the refresh has run, each channel says "lag not yet measured", and
  // the publisher re-check's line is absent (R69). A page that ships no script (the legal pages, the maintenance page) never
  // has an answer, so its strip says so (`noAnswer`) in place of the as-built line's promise (the review of #990).
  freshness: Object.freeze({
    heading: "How current this is",
    asBuilt: "Each answer from the server says when its index was built, when each publisher's texts were last collected and which code built and serves it; this page shows it once it has an answer.",
    noAnswer: "This page carries no answer from the server, so there is nothing to show here. On a page that has one, this part says, from that answer, when the index was built, when each publisher's texts were last collected and which code built and serves it.",
    built: "Index built on {date}.",
    builtUndated: "The index records no build date.",
    channels: Object.freeze({
      "lu-legilux": "Luxembourg law, from Legilux",
      "eu-eurlex": "EU law, from the Publications Office of the European Union",
    }),
    collected: Object.freeze({
      one: "{channel}: last complete collection ended on {date}, {count} full day before the server answered on {today}; lag not yet measured.",
      other: "{channel}: last complete collection ended on {date}, {count} full days before the server answered on {today}; lag not yet measured.",
    }),
    collectedUnstated: "{channel}: when its last complete collection ended is not recorded with this index; lag not yet measured.",
    indexCommit: "Index built from",
    serviceCommit: "Service running",
    commitUnstated: "(not recorded)",
    staleHeading: "Some texts may be out of date",
    stale: Object.freeze({
      one: "{channel}: its last complete collection ended on {date}, more than {count} day before the server answered on {today}. The publisher may have published changes since; check its official site.",
      other: "{channel}: its last complete collection ended on {date}, more than {count} days before the server answered on {today}. The publisher may have published changes since; check its official site.",
    }),
  }),
  // The home page (the specification of record's R47 and R52; UX spec section 2, Screen 1: Home and search): the one box
  // first, what Lex is and is not, a coverage line whose every count is the coverage answer's (R11), and links to the
  // site's served pages. A count is said with its noun by the language's plural rule.
  home: Object.freeze({
    title: "Luxembourg and EU law at a date you choose",
    eyebrow: "Search the law",
    heading: "Luxembourg and EU law at a date you choose",
    intro: "Search the texts this site holds, open a law as it stood on the date you choose, and export with citations anyone can check against the official source. For a Luxembourg act of which the site holds more than one version, see whether each article's wording changed from one version to the next, and which articles differ between the versions of two dates. Where the site does not hold a text or a date, it says so.",
    aboutHeading: "What Lex is and is not",
    coverage: "This site holds {acts} of Luxembourg law, in {versions} with {articles}, as of the index built on {date}.",
    coverageUndated: "This site holds {acts} of Luxembourg law, in {versions} with {articles}; the index it holds records no build date.",
    // An index older than today: both dates (R13; the plan's item 3.4).
    coverageOlder: "This site holds {acts} of Luxembourg law, in {versions} with {articles}, as of the index built on {date}; today in Luxembourg is {today}.",
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
    // The example searches (R47; `home-examples.mjs`): each a usual citation of an article this site holds, derived from the
    // mount at build and kept only when searching it opens that article's text; the citations themselves are data, said in
    // their text's language.
    examplesHeading: "Example searches",
    examplesIntro: "Each searches for an article this site holds, cited as it is usually written, and leads to its text.",
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
  // The five legal pages every footer links (the plan's item 7.1; R49, R50, R62, R70; `legal-pages.mjs`), and what they
  // share (`legal`). Each page's `sections` are its parts in order: a part's `heading`, then its paragraphs, a `list` being
  // one list of items. A paragraph's placeholders are the footer's statements (`notAdvice`, `authenticity`: R50's sentence
  // and the authenticity line, said once, word for word), a link to another legal page (`legalNoticePage`, `cookiesPage`,
  // in the words of `legal.inline`), the EU texts' acknowledgement as they carry it, in English (`eurlexAcknowledgement`),
  // Luxembourg's accessibility law by its French title (`accessibilityLaw`) and the CNPD's site, a link (`cnpdSite`). By the
  // owner's decision of 2026-10-19 the pages name no person and give no postal address, no e-mail address and no contact
  // of any kind, departing from R49 and O5; the reader's right to complain to the CNPD stays, its site linked and its
  // address left out (the driver's decision of 2026-10-19). Every statement is one the code or the deployment supports (STATUS-WEB.md, the sections of 2026-10-18 and
  // 2026-10-19 on the legal pages).
  legal: Object.freeze({
    menu: "Legal information",
    updated: "Last updated: {date}.",
    inline: Object.freeze({
      legalNotice: "legal notice",
      privacy: "privacy notice",
      terms: "terms of use",
      accessibility: "accessibility statement",
      cookies: "cookies page",
    }),
  }),
  legalNotice: Object.freeze({
    title: "Legal notice",
    eyebrow: "About this site",
    heading: "Legal notice",
    intro: "What Lex is, where its texts come from and under which licences, and who hosts it.",
    sections: Object.freeze({
      what: Object.freeze({
        heading: "What Lex is",
        text: "Lex is an independent, non-commercial tool for reading Luxembourg and EU law over time: for the texts and dates it holds, it shows a text as it stood on the date you choose, and how it changed. It is free, carries no advertising and asks for no account.",
        notAdvice: "{notAdvice}",
      }),
      status: Object.freeze({
        heading: "No official status",
        text: "Lex has no official status. It is not a service of the Luxembourg State, of the Service central de législation, which publishes Legilux, or of the Publications Office of the European Union, and none of them endorses it.",
        prevails: "{authenticity} Where a text on Lex differs from its official publication, the official publication prevails.",
      }),
      sources: Object.freeze({
        heading: "Sources and licences",
        luxembourg: "The Luxembourg texts come from Legilux, published by the Service central de législation. Lex shows the text of a file only when Legilux's own records of that file give it the Creative Commons Attribution 4.0 International licence (CC BY 4.0); for any other file it shows the official link and not the text.",
        europe: "The EU texts come from the Publications Office of the European Union and carry its acknowledgement, “{eurlexAcknowledgement}”. They are reused as the European Union's reuse policy allows (Commission Decision 2011/833/EU on the reuse of Commission documents).",
        changes: "Lex extracts and normalises the texts it shows, and every article it quotes names its official source.",
      }),
      hosting: Object.freeze({
        heading: "Hosting",
        text: "The site is hosted by Microsoft, on its Microsoft Azure cloud service, in the France Central region, in France.",
      }),
    }),
  }),
  privacy: Object.freeze({
    title: "Privacy notice",
    eyebrow: "About this site",
    heading: "Privacy notice",
    intro: "What happens to your data when you read Lex: it asks you for none, sets no cookie and records nothing about you.",
    sections: Object.freeze({
      reading: Object.freeze({
        heading: "When you read the site",
        request: "Each page you open and each search you send is a request from your browser to this site's server. The request carries your device's IP address, the address of the page and the technical details every browser sends. A search's words travel in the body of the request, never in the request's address; a link that keeps a search holds it in the part after the “#”, which your browser never sends.",
        host: "Microsoft, which hosts the site, carries each request to the server and its answer back to you, and handles the connection data this needs under its own terms.",
        kept: "The server uses the request to answer it and to count it (see below), and writes nothing of it anywhere: it keeps no access log and no record of what is read or searched. This is tested on the server's own code, before it is deployed: the server is run with requests carrying marked values (a search phrase, an IP address, a browser's name), and none of them may appear in anything it writes. The hosting is set up from a template that adds no log store and no monitoring service that would keep requests; the hosting's settings, once deployed, are not tested this way.",
        // The per-client limit (R109; V3ClientLimiter): what the server holds of an IP address, where, and for how long.
        limit: "So that no single IP address can overload the server, the server counts the requests that come from each IP address. For this it holds, in its working memory only, a fingerprint of the IP address beside a count of its recent requests. The fingerprint is computed with a secret key that is drawn at random, never written down and replaced every day; the IP address itself is not kept. Neither the fingerprint nor the count is ever written down, and both are forgotten at the latest about two minutes after the last request from that IP address, or when the server restarts. An IP address that sends too many requests at once or in a minute is asked to wait a moment.",
        nothingHeld: "Lex has no accounts and no sign-up, so it never knows who you are. Since it records nothing about a visit, and forgets the count above within minutes, it holds nothing about you that it could later show, correct or erase.",
      }),
      nothingElse: Object.freeze({
        heading: "No cookies, no trackers, no other sites",
        cookies: "The site sets no cookie and writes nothing to your browser's storage (see the {cookiesPage}). It loads nothing from another site: no audience measurement, no advertising, and no fonts, scripts or images from elsewhere.",
        links: "Links to the publishers' sites, such as Legilux and EUR-Lex, are ordinary links. If you follow one, your browser asks that site directly, under that site's own privacy notice; Lex asks your browser not to tell it which page you came from.",
      }),
      named: Object.freeze({
        heading: "People named in published acts",
        why: "Some published acts name people, for example a law granting nationality. Lex reproduces texts as the official journals publish them, the Journal officiel du Grand-Duché de Luxembourg and the Official Journal of the European Union, so that the law can be read at a date and checked against its official source.",
        searchEngines: "Search engines may index the page of an act only once a check has found that the act names no private person; the page of any other act asks them not to.",
      }),
      // The reader's right to complain to the supervisory authority (GDPR art. 77): the regulator, named with a link to its
      // site and no postal address, not a contact of Lex's (the driver's decision of 2026-10-19, STATUS-WEB.md).
      rights: Object.freeze({
        heading: "Your rights",
        complaint: "You may lodge a complaint with the Commission nationale pour la protection des données (CNPD), {cnpdSite}.",
      }),
    }),
  }),
  terms: Object.freeze({
    title: "Terms of use",
    eyebrow: "About this site",
    heading: "Terms of use",
    intro: "How Lex may be used, under which licences its texts are shown, what it does not promise and how to cite the texts.",
    sections: Object.freeze({
      what: Object.freeze({
        heading: "What Lex is",
        notAdvice: "{notAdvice}",
        service: "It is an information service, free of charge: it shows the texts it holds, at the dates it holds them, and says so when it holds no text or no date. {authenticity}",
      }),
      use: Object.freeze({
        heading: "Using Lex",
        text: "Anyone may read, search, cite and export the texts on Lex, free of charge and with no account.",
      }),
      texts: Object.freeze({
        heading: "The texts and their licences",
        licences: "Each text keeps the licence of its source: the Creative Commons Attribution 4.0 International licence (CC BY 4.0) for the Luxembourg texts Lex shows, and the European Union's reuse policy (Commission Decision 2011/833/EU) for the EU texts. The {legalNoticePage} says where the texts come from and the acknowledgement each carries. These terms add no restriction to any reuse of the texts that their own licences allow.",
      }),
      warranty: Object.freeze({
        heading: "No warranty",
        text: "Lex is offered as it is. It does not guarantee that the texts are complete, up to date or free of errors, nor that the site is always available. Check the official text before you rely on it. Nothing in these terms excludes or limits a liability that the law does not allow to be excluded or limited.",
      }),
      citing: Object.freeze({
        heading: "How to cite",
        text: "Cite the official text, by its official reference. Lex gives each article it quotes a citation and a permalink to the text shown, so that anyone can check the quotation against its official source.",
      }),
    }),
  }),
  accessibility: Object.freeze({
    title: "Accessibility statement",
    eyebrow: "About this site",
    heading: "Accessibility statement",
    intro: "How accessible Lex is, what has been done and what has not been checked yet.",
    sections: Object.freeze({
      status: Object.freeze({
        heading: "Conformance status",
        text: "Lex is built to meet the Web Content Accessibility Guidelines (WCAG) 2.2 at level AA. It has not been audited yet, so it does not claim to meet them: until an audit of the site in service is done, it is non-conformant, as a public sector body's site with no audit is declared in Luxembourg.",
      }),
      done: Object.freeze({
        heading: "What has been done",
        list: Object.freeze({
          language: "Every page states its language, and every quoted text of law is marked with the language it is written in.",
          skip: "A link at the top of every page leads straight to its content.",
          server: "The pages of an act and of its text come complete from the server and can be read without scripts.",
          dates: "Dates are always calendar dates, never relative ones.",
          colour: "No information is given by colour alone: the words a search found are bold as well as highlighted.",
          whole: "A comparison is one list, read in order, and the tables and lists of conditions in a text of law are always shown whole.",
          keyboard: "Every form can be used with the keyboard alone, and every control shows when it has the focus: automated tests check this in a browser for each change to the site's code.",
          announced: "An answer is announced to screen readers when it arrives.",
        }),
      }),
      limits: Object.freeze({
        heading: "What has not been checked yet",
        list: Object.freeze({
          audit: "No audit by an accessibility expert, and no test with a screen reader such as NVDA or VoiceOver, has been done yet.",
          reflow: "How the pages reflow on narrow screens and at high zoom has not been checked against the guidelines.",
          pdf: "Files exported as PDF are not tagged, so a screen reader cannot follow their structure; the JSON export carries the same text.",
        }),
      }),
      law: Object.freeze({
        heading: "The law",
        text: "In Luxembourg, the {accessibilityLaw} sets the accessibility rules for public sector bodies. Lex is not published by a public sector body, and gives this statement of its own accord.",
      }),
      prepared: Object.freeze({
        heading: "This statement",
        text: "This statement was prepared from the site's own code and automated tests. No outside audit has been done.",
      }),
    }),
  }),
  cookies: Object.freeze({
    title: "Cookies",
    eyebrow: "About this site",
    heading: "Cookies",
    intro: "Lex sets no cookie.",
    sections: Object.freeze({
      none: Object.freeze({
        heading: "No cookie, no browser storage",
        text: "This site sets no cookie, neither its own nor anyone else's, and writes nothing to your browser's storage. It loads nothing from other sites, so no other site can set one through it. That is why it asks you for no consent.",
      }),
      checked: Object.freeze({
        heading: "How this is checked",
        text: "Automated tests fail if a page of the site sets a cookie, writes to the browser's storage or asks another site for anything, and each release is checked, before it serves readers, for any answer that sets a cookie.",
      }),
    }),
  }),
  // The three reference pages (the specification of record's launch set: Developers, Get help, How to read Lex; R42, R46,
  // R48, R50, R55; `reference-pages.mjs`), and what they share (`reference`: the footer's menu of them). Like the legal pages,
  // each page's `sections` are its parts in order, a part's `heading` then its paragraphs, a `list` being one list. A
  // paragraph's placeholders are values the page fills: the footer's statements (`notAdvice`, `authenticity`), the failure
  // line (`failureLine`), links to other pages (`developersPage`, `guidePage`, `privacyPage`, `termsPage`), the publishers'
  // sites (`legiluxSite`, `eurlexSite`), and other entries of this table, said as they are said where they stand (`skip`,
  // `cite`, `conventional`, `withPermalink`, `evidence`, `freshnessHeading`, `staleHeading`, `asPublishedLabel`,
  // `noDateMark`, `withoutText`). Get help names no person and gives no contact, by the owner's decision of 2026-10-19, and
  // says so. The Developers page's sections are laid out by the page from the contracts (`developers.mjs`); its placeholders
  // are code (an operation, a member, a path) or a count the contracts give. The glossary's terms are entries of this table
  // (`GLOSSARY`); its `terms` are their definitions.
  reference: Object.freeze({
    menu: "Help and reference",
    contents: "On this page",
    inline: Object.freeze({
      guide: "How to read Lex",
      developers: "Developers page",
    }),
    // A banner's info control (R48: the guide "linked from each banner's info control"; `GuideLink.jsx`): a link to the
    // section of How to read Lex that explains the banner, whose name holds that section's heading.
    info: "How to read this",
    infoNamed: "How to read this: {section}",
  }),
  help: Object.freeze({
    title: "Get help",
    eyebrow: "Help",
    heading: "Get help",
    intro: "What Lex can and cannot do for you, how to read its answer when it cannot give the one you asked for, and where the official texts are.",
    sections: Object.freeze({
      what: Object.freeze({
        heading: "What Lex is",
        text: "Lex shows the Luxembourg and EU law it holds as the publishers published it: a text as it stood on the date you choose, how each article of a Luxembourg act changed, and a citation anyone can check against the official source.",
        notAdvice: "{notAdvice}",
        adviser: "For what a text means for you, or what you should do, ask a lawyer or another adviser of your choice.",
      }),
      not: Object.freeze({
        heading: "What Lex does not do",
        list: Object.freeze({
          apply: "It does not apply the law to anyone's situation, compute a deadline or an amount, or say who is right.",
          effect: "It does not say what a change means in effect, predict a decision or draft a text.",
          consolidation: "It makes no consolidation of its own: every consolidated text it shows is the publisher's.",
          caseLaw: "It holds no case law and no court decisions.",
          choose: "It never chooses between two versions for you when the publisher's dates leave both possible and their texts differ, and never fills a gap between versions.",
          record: "It keeps no record of who reads it or of what is searched.",
        }),
      }),
      refusals: Object.freeze({
        heading: "When Lex cannot answer",
        text: "When Lex cannot give the answer you asked for, it says why, in one plain sentence first: for example, that it holds no version of the act for the date you chose, that two versions cover that date and the publisher does not say which one applies, or that the licence of a file keeps its text off the site. Then it says what would answer instead, such as the nearest dates it holds, and gives the official link.",
        absence: "Such an answer is about what Lex holds, never about the law: a text missing from Lex may exist at the publisher.",
        failure: "When the site itself fails, it says so in other words: “{failureLine}” Try again, or go to the official sites below.",
        details: "After the sentence come the refusal's code and its details, for programs; the {developersPage} lists every code.",
      }),
      official: Object.freeze({
        heading: "Where the official texts are",
        authenticity: "{authenticity}",
        check: "Every article Lex quotes names its official source, so that it can be checked there:",
        list: Object.freeze({
          legilux: "{legiluxSite}, for Luxembourg law: the Journal officiel du Grand-Duché de Luxembourg and the consolidated texts",
          eurlex: "{eurlexSite}, for EU law: the Official Journal of the European Union",
        }),
      }),
      advice: Object.freeze({
        heading: "Who can advise you",
        text: "Lex names no adviser: a list of who can advise you, by subject, must first be checked by a person, and has not been yet.",
      }),
      contact: Object.freeze({
        heading: "No contact",
        text: "Lex has no contact channel: no e-mail address, no form, no telephone number and no postal address, and it names no person. No one can be reached through Lex, to ask about the law or about the site, or to report a problem, so Lex receives no message and keeps none.",
        complaint: "To complain about how this site handles personal data, the {privacyPage} names the authority to turn to.",
      }),
      guide: Object.freeze({
        heading: "Reading Lex",
        text: "The page {guidePage} explains the dates Lex shows, what a version is and the words it uses, with a glossary.",
      }),
    }),
  }),
  guide: Object.freeze({
    title: "How to read Lex",
    eyebrow: "Guide",
    heading: "How to read Lex",
    intro: "What the dates on Lex mean, what a version is, how Lex says what it holds and what it does not, and the words it uses, with a glossary.",
    sections: Object.freeze({
      clocks: Object.freeze({
        heading: "Two kinds of date",
        text: "Lex keeps two kinds of date apart, and never merges them, in what it writes or in what a screen reader hears.",
        luxembourg: "The first kind says, from the publisher's dates, which text answers a date. A Luxembourg version is “applicable from” the date Legilux states for it, until the day before the end date Legilux gives it or, where it gives none, until the day before the next version begins; “(publisher)” after a date marks it as the publisher's date, never one Lex worked out.",
        europe: "For an EU act, Lex shows wordings: the original wording, which carries the act's date in the publisher's Formex package, and each consolidated wording, which carries the publisher's consolidation date. A wording answers every date from its own to the day before the next one's, and that is all Lex says of it: a consolidated wording is documentation, not a statement that the act bound anyone on that date.",
        second: "The second kind says when a text was published, and when Lex collected it. A publication date is the publisher's; the index's build date and each publisher's last complete collection, at the foot of a page that has an answer from the server, are Lex's own. Neither says from when a text is applicable.",
        never: "Lex says no more of a period than the publisher's dates say: it never claims that a text bound anyone from one date to another.",
      }),
      roles: Object.freeze({
        heading: "Each date with what it is",
        text: "The publisher's record can give an act several dates: its entry into force, its application, the end of its validity, a deadline, its signature. Lex shows each one as a separate value, under its own name, and never gives one as another.",
        entryIntoForce: "When the file of an act as first published states no date from which it is applicable, Lex dates it by its entry into force in the publisher's record and says so, with the mark “{noDateMark}”.",
        calendar: "Dates are always calendar dates, never relative ones such as “yesterday”.",
      }),
      versions: Object.freeze({
        heading: "As published, and consolidated",
        published: "An act as published is the act as the official journal published it. A consolidated version is a text of the act with the changes later acts made to it, as of a date, put together by the publisher.",
        authenticity: "{authenticity}",
        asPublished: "Where Lex holds a Luxembourg act only as first published, it labels it “{asPublishedLabel}”: later acts may have changed it, and Lex holds no version with those changes.",
        languages: "When the publisher has issued an act in another language beside French, Lex shows each of the publisher's language versions it holds, and says so when the publisher's record does not state which one is authentic.",
      }),
      textless: Object.freeze({
        heading: "Versions whose text Lex does not hold",
        text: "Lex can know from the publisher's catalogue that a version exists without holding its text: its file may be a PDF, which Lex does not read; its licence may not allow Lex to show it; or Lex may not have collected it. Lex never shows another version's text as that version's.",
        article: "An article held without text is named apart, under “{withoutText}”, and is never quoted, searched or exported as text.",
        annexes: "An EU annex the publisher provides only as images is never shown as text, searched, quoted or exported; Lex gives its official source.",
      }),
      gaps: Object.freeze({
        heading: "Gaps",
        text: "When the publisher's catalogue lists no version for some dates between two versions, Lex shows the gap as it is: it does not stretch the version before it over those dates, and an unchanged wording on either side of a gap says nothing of them.",
        law: "A gap in the catalogue is not a gap in the law, and an absence on Lex is an absence in what Lex holds: when Lex holds no text for a date, that is not evidence that the act or the law did not exist.",
      }),
      ambiguity: Object.freeze({
        heading: "Two versions for one date",
        text: "The publisher's dates can make two versions cover the same date without saying which one applies. When their texts differ, or Lex does not hold the text of each, Lex chooses neither: it shows both, each with its dates and its link, and you choose which one to read. When it holds the text of each and it is the same, it can show that text once, as the text of the act as published or of the version that begins last.",
      }),
      freshness: Object.freeze({
        heading: "How current Lex is",
        text: "At the foot of every page, in the part a screen reader names “{freshnessHeading}”, a page that has an answer from the server says when the index Lex answers from was built and, for each publisher, when Lex last finished collecting its texts. A text the publisher published after that is not on Lex yet.",
        stale: "When a publisher's last complete collection is older than Lex allows, a banner says so under the header of every page that has an answer from the server: “{staleHeading}”. The publisher may have published changes since; check its official site.",
        lag: "Until Lex has measured how long a publisher's new texts take to reach it, each publisher's line says that the lag is not yet measured.",
      }),
      links: Object.freeze({
        heading: "Permalinks and citations",
        permalink: "A permalink names one version for good: its address holds the publisher, the act, the version's date and the digest of its text, the language and the article; for an EU act, it names one wording in the same way. It shows that text, and says above it when a newer version is applicable or a later wording is held. If Lex serves another text for that date, or no longer holds that one, it says so first, and shows the text it serves or links the one it holds.",
        readable: "An act's readable address, with no date and no digest, follows the act: it opens the version, or for an EU act the wording, that answers today's date in Luxembourg.",
        cite: "Under a quoted article, “{cite}” gives it in three forms: “{conventional}”, as it is usually written; “{withPermalink}”, the same followed by the permalink; and “{evidence}”, with the identifiers, dates and digest that let anyone check the quotation. In a document, cite the official text by its official reference.",
        digest: "A digest is a fingerprint of an exact text: if one character changes, the digest changes. Lex computes one over each text it quotes, so that a quotation can be checked character for character.",
      }),
      keyboard: Object.freeze({
        heading: "With a keyboard",
        text: "Lex has no keyboard shortcuts of its own, so no key does anything you did not ask for. Tab moves to the next link, field or button, and Shift and Tab to the one before; Enter follows a link or sends a form.",
        skip: "The first link on every page, “{skip}”, leads past the menus straight to the content.",
      }),
    }),
    glossary: Object.freeze({
      heading: "Glossary",
      intro: "The words Lex uses, as its pages print them, and what each one means.",
      terms: Object.freeze({
        appliesFrom: "In a dossier's table of versions: the date from which the publisher states a Luxembourg version is applicable. It is Legilux's date, never one Lex works out.",
        versionApplicable: "How a search result, a reading or a citation names a Luxembourg version: by the date from which the publisher states it is applicable, “(publisher)” marking the date as the publisher's.",
        asPublished: "Lex holds this Luxembourg act only as the official journal first published it: later acts may have changed it, and Lex holds no consolidated version with their changes.",
        entryIntoForce: "The date the publisher's record gives for an act to enter into force. It is a date of its own, not the date from which a version is applicable, and Lex never gives one as the other.",
        application: "Of an EU act: the date the publisher's record gives for the act, or a part of it, to apply.",
        endOfValidity: "Of an EU act: the date the publisher's record gives for the act, or a part of it, to cease to have effect.",
        wordingDate: "Of an EU act: the date that the wording shown carries, the act's own date for the original wording and the publisher's consolidation date for a consolidated one.",
        wordingChanged: "Between two versions of a Luxembourg act: the article's text differs from the version before. Lex says nothing of what the change means.",
        wordingUnchanged: "Between two versions of a Luxembourg act: the article's text is the same as in the version before.",
        catalogue: "On a dossier: what the publisher's catalogue says between an act's versions, such as a gap, two versions on one date, or a version whose text Lex does not hold.",
        withoutText: "Lex knows the article from the publisher's record but does not hold its text, or may not show it. It is listed apart and never quoted, and an export records it as excluded, with the reason.",
        notAdmitted: "On a dossier: how many articles of a version Lex records without admitting them as text it quotes; the note under the table, from the server, says what the count covers.",
        absence: "Under a refusal: what would turn it into an answer, such as a corrected identifier, or a text the publisher publishes later.",
        permalink: "An address that names one version, or one EU act's wording, for good: the publisher, the act, the date and digest of that version or wording, the language and the article. It shows that text or, if not, says first why.",
        usualCitation: "An article's citation as it is usually written, with the date of its version, or of its EU wording, when that text is not known to be the one that answers today.",
        withPermalink: "The usual citation, followed by the permalink of the text quoted.",
        evidenceForm: "The citation with what it takes to check it: the act's identifier, its dates, the official journal's reference where Lex holds it, the digest of the text quoted and which text is authentic.",
        textDigest: "A fingerprint of an article's exact text: if one character changes, the digest changes, so that anyone can check that a quotation is the text Lex served.",
        today: "Today's date in Luxembourg, which Lex reads a text at when you give no date.",
        dossier: "What Lex holds for one act: a Luxembourg act's titles and versions, or an EU act's expressions, with the publisher's dates, and what Lex does not hold.",
        changes: "The page that lists the Luxembourg versions dated in a window of dates, each with the version it follows and whether the wording changed.",
        coverage: "The page that says what Lex holds and what it lacks, as counted in its index, and how its quality checks went.",
        freshness: "At the foot of every page that has an answer from the server: when the index was built, and when each publisher's texts were last collected.",
        stale: "A banner shown under the header of every page that has an answer from the server when a publisher's texts were last collected longer ago than Lex allows.",
        indexCommit: "The revision of Lex's code that built the index an answer comes from.",
        serviceCommit: "The revision of Lex's code that served the answer.",
      }),
    }),
  }),
  developers: Object.freeze({
    title: "Developers",
    eyebrow: "Developers",
    heading: "The Lex API, for programs and AI agents",
    // Between the codes of a list said in one line (the MCP methods, an answer's objects, a payload's fields, the events).
    codeSeparator: ", ",
    intro: "The answers this site's pages show come from one API, and the same answers are open to any program, over REST and over MCP, at this site's own address. It is free and anonymous: there is no key, no account and no paid plan. This page is generated from the operation registry and the schemas this release checks every request and answer against, so it lists what the server does.",
    access: Object.freeze({
      heading: "Two ways in, one answer",
      rest: "REST: {request}, with a JSON body that names the operation and its parameters, {body}. The path takes no query, and any other method gets only a failure below the envelope.",
      mcp: "MCP: JSON-RPC messages sent by POST to {endpoint}, over the Streamable HTTP transport, protocol version {version}. The server answers {methods}; every registered operation is a tool of the same name, whose input schema is the {parameters} member of the operation's request schema.",
      instructions: "Its answer to {initialize} carries {instructions}, which a client may hand its model, and {ping} is answered with an empty result. The instructions, as the server sends them:",
      same: "Both give the same envelope for the same request: a tool call is turned into the REST request document and answered by the same code, and its result carries the envelope as its structured content. A refusal is an answer, not an error: REST sends it as a successful HTTP answer, and MCP as a tool result with {isError}; only the refusal of a request past its client's limit has another status over REST, given below.",
      restExample: "For example, the coverage report, asked with curl:",
      mcpExample: "The same over MCP, as a tools/call message:",
      client: "A client that speaks Streamable HTTP is given the address {endpoint}; one that reads its servers from a JSON file, for example:",
    }),
    operations: Object.freeze({
      heading: "The operations",
      summary: Object.freeze({
        one: "The registry lists {count} operation; this server serves {served} of them. Each gives its REST path, its MCP tool, its parameters as its request schema states them, the objects its answer carries, and its two schemas.",
        other: "The registry lists {count} operations; this server serves {served} of them. Each gives its REST path, its MCP tool, its parameters as its request schema states them, the objects its answer carries, and its two schemas.",
      }),
      europe: "For an EU identifier, some served operations refuse with {code}, naming the modes available; when an EU index is mounted, the coverage answer's {member} lists them.",
      rest: "REST",
      tool: "MCP tool",
      parameters: "Parameters",
      noParameters: "none",
      required: "required",
      optional: "optional",
      objects: "Answer carries",
      schemas: "Schemas",
      requestSchema: "request",
      resultSchema: "answer",
      notServed: "Not served from data: a request to it is answered inside the envelope with the refusal {code}, naming the mode asked, {mode}, and the modes this server serves; the data that would serve it: {data}.",
      descriptions: Object.freeze({
        answer_drift: "The past dated answers that a later publisher revision invalidated, found from the event log.",
        article_history: "One article of a Luxembourg act through the versions held: whether its wording changed from one to the next, and which versions do not carry it.",
        as_observed: "The version of a Luxembourg act applicable on a date as one build of the event log held it, with that build's time as an upper bound on when it was observed.",
        as_of: "The version of a Luxembourg act applicable on a date, with its articles; for an EU act, the wording that answers the date.",
        ask: "The assistant, which is off: every question receives the presentation result that says so, naming the operations that answer from the law Lex holds. No model writes an answer.",
        browse: "The Luxembourg acts held, as one ordered and paged list, with their identifiers, languages, version dates and the publisher's document types.",
        changes_in_period: "The Luxembourg versions whose applicability begins in a period, for one act or across the index.",
        citation: "The references the publisher wrote in the text of a Luxembourg version's articles.",
        cited_by: "The references, in any Luxembourg version held, whose target is a given act.",
        classification: "The publisher's typed facts about a Luxembourg act, as the publisher states them: document type, legal value, responsible body, historical identifiers, publication and document dates.",
        concepts: "The concepts the Publications Office attaches to EU acts: EuroVoc descriptors, directory codes and subject matters.",
        coverage: "What this server holds and serves, counted from its index, and what it does not hold.",
        diff: "The article-by-article difference between the versions of a Luxembourg act applicable on two dates.",
        dossier: "An act's record as the index holds it: a Luxembourg act's titles and versions, or an EU act's expressions.",
        events: "The event log of what each build of the index found, polled by cursor and delivered at least once.",
        evidence_bundle: "What quoting a version needs, a page at a time: each article's text with its digests, the version's permalink, sources and official identities, under the rights rule; for an EU act, the same for the wording that answers the date.",
        in_force_on: "The Luxembourg acts with a version applicable on a date.",
        knowable_on: "What a reader could have known on a date: each version's publication beside the time Lex observed it.",
        manifestation: "The publisher's manifestations of a Luxembourg act's expressions, with their formats and files, the one Lex retained marked with its digest.",
        provenance: "The sources, rule profiles and digests behind a Luxembourg version.",
        relations: "The references of a Luxembourg act in both directions, as one ordered and paged list.",
        resolve: "The act and the expression that a Luxembourg identifier or a pinned permalink names.",
        search: "An exact reference opens its act; any other phrase is searched, with capitals and accents ignored, in the Luxembourg article text whose rights allow it, or in the wordings of one EU act named by its identifier.",
        status_on: "The publisher's own statements on a Luxembourg act's entry into force and its end, word for word, beside the version applicable on a date.",
        timeline: "Every version of a Luxembourg act held, in the publisher's order of applicability; for an EU act, every wording discovered, in order of date.",
        transposition: "The links between EU directives and the Luxembourg acts that transpose them, as each publisher asserts them.",
        verify: "Checks a pinned permalink against the digest the index holds, or gives the current digests to pin.",
      }),
    }),
    envelope: Object.freeze({
      heading: "The envelope",
      text: "Every answer, over REST and over MCP, is one envelope, {schema}, with these members:",
      members: Object.freeze({
        schema: "The envelope's schema identity.",
        version: "The platform's version.",
        object_type: "The object's type: an envelope.",
        request_ref: "An opaque reference to this one request, for the client's own use.",
        operation_id: "The operation that answered.",
        registry_schema: "The operation registry's schema identity.",
        registry_sha256: "The digest of the operation registry this server runs.",
        context: "What the answer was given from, below.",
        verdict: "What kind of answer this is, below.",
        result: "The answer's object, with its own schema and type, or null when the operation refuses.",
        refusal: "The refusal, with its code and payload, or null when the operation answers.",
      }),
      contextText: "Its {context} says what the answer was given from:",
      context: Object.freeze({
        commits: "The revisions of Lex's code that built the index, {builder} (“{builderLabel}”), and that serve the answer, {runtime} (“{runtimeLabel}”).",
        freshness: "How current the data is: when the index was built, each publisher's newest complete collection with its age and lag, the maximum age allowed, and whether the data is older than that.",
        jurisdiction: "The jurisdiction of the law answered: Luxembourg or the EU.",
        provisional: "Whether the answer is provisional; this server always sends {value}.",
        publisher: "The publisher whose law is answered.",
        snapshot: "The corpus snapshot the index was derived from, by its identity and digest.",
        status: "Whether the operation answered or refused.",
        timeline_semantics: "How the answer's dates are to be read: {luxembourg} for Luxembourg, the dates from which the publisher states its versions are applicable; {europe} for the EU, the dates of the publisher's wordings.",
      }),
      verdictText: "Its {verdict} is one of the registry's closed set:",
      verdicts: Object.freeze({
        answer: "The operation answered.",
        answer_with_enrichment: "Reserved for the assistant, which is off; this server sends it for no answer.",
        point: "The answer points elsewhere: what {ask} gives, since the assistant is off.",
        clarify: "Reserved for the assistant, which is off; this server sends it for no answer.",
        refuse: "The operation refused, with a typed refusal.",
        split: "Reserved for the assistant, which is off; this server sends it for no answer.",
      }),
      registry: "{member} is {digest} for this release: the digest of the registry, which covers every operation, every schema's digest and every refusal code with the fields its payload must carry, so it changes whenever any of them changes. Each schema's identity ends in its version.",
    }),
    refusals: Object.freeze({
      heading: "Refusals",
      text: Object.freeze({
        one: "When an operation cannot answer, it refuses with a code of the registry, which holds {count}, never with an empty answer: the envelope's verdict is {refuse}, its result is null, and its refusal, {schema}, carries the code and a payload. The registry fixes the fields each code's payload must carry; a payload may carry more.",
        other: "When an operation cannot answer, it refuses with one of {count} codes, never with an empty answer: the envelope's verdict is {refuse}, its result is null, and its refusal, {schema}, carries the code and a payload. The registry fixes the fields each code's payload must carry; a payload may carry more.",
      }),
      pages: "On this site's pages, a refusal's plain sentence comes first, and its code and payload follow it.",
      fields: "Its payload carries {fields}, and is held to {schema}.",
      codes: Object.freeze({
        advice_boundary: "The question asks for advice: Lex shows the text, and does not apply it to anyone's situation.",
        ambiguous_identifier: "The identifier names more than one act; the candidates are listed, and none is chosen.",
        ambiguous_version: "More than one version covers the date and the publisher does not say which applies; each candidate is listed, and none is chosen.",
        anchor_not_in_version: "The index holds no article with that identifier in the version; the nearest article identifiers are given, and no text search is run in its place.",
        derivation_refused: "Lex will not derive what was asked from the text it holds; the payload says what and why.",
        format_not_available: "The format asked for is not offered for this answer; the formats offered are listed.",
        identifier_unknown: "No act the index holds has that identifier; the payload gives the official search and what would answer.",
        language_not_available: "The text is not held in that language; the languages held are listed.",
        no_corpus_mounted: "This server holds no law at the moment.",
        no_version_for_date: "No version the index holds covers the date; the payload gives the date its history begins and the nearest dates held before and after it.",
        not_transposable: "Only a directive is transposed, and this instrument is not one; the payload says why.",
        out_of_corpus_scope: "Lex does not hold the instrument, and the payload says why: it names the instrument, its official place, the reason and who to ask, an institution or an official service and never a person.",
        pinned_digest_mismatch: "The digest a permalink pins is not one Lex holds for that version; the payload gives the current digest and its permalink.",
        profiles_differ: "The two versions were read under different rule profiles, so they are not compared.",
        rate_limited: "Too many requests; the payload says when to ask again.",
        retrieval_mode_unavailable: "The operation is not served in the mode asked: for an EU identifier, or not at all when no data serves it; the modes available are listed.",
        snapshot_unknown: "The snapshot or the log the request names is not one this server holds; the payload says what would answer.",
        text_not_available: "The publisher's file holds no text Lex can read, such as an annex given only as images; its official identity and source are given.",
        text_withheld: "The licence of the text does not allow Lex to show it; its official link and digest are given.",
        upstream_unreachable: "A source Lex depends on could not be reached; the payload says whether to ask again.",
      }),
      outOfScope: Object.freeze({
        heading: "What Lex never holds",
        text: "An identifier that names one of these is refused with {code} by every operation that takes one, before anything is looked up. The list, {list}, names for each its official place and who to ask, an institution or an official service and never a person; each refusal names the list's digest, {digest}.",
        row: "{instrument}: {reason} For example {examples}. Official place: {official}. Who to ask: {who}.",
        pending: "Not listed yet: communal regulations, whose official place is each commune's own, and the decisions of Luxembourg's courts, which no identifier names exactly. Each still answers {code}.",
        // R80: an EU act outside the mounted EU population, refused by the mount itself, not by the list.
        population: "An EU act outside the EU population this site holds, named by its CELEX, is refused with {code} too, though it is on no list: whether an act is outside depends on the EU index the server mounts. The refusal names the rule that population follows, with the number of acts on its seed list, and gives the act's page on EUR-Lex as its official place; it names neither the out-of-scope list nor its digest.",
      }),
      presentationHeading: "Two answers that are not refusals",
      presentation: Object.freeze({
        assistant_v3_unavailable: "{code}: what {ask} answers to every question, since the assistant is off; it names the operations that answer from the law Lex holds.",
        localization_unavailable: "{code}: the page a reader in German or Luxembourgish is shown, since only French and English are reviewed; it links the pages in both.",
      }),
      transportHeading: "Failures below the envelope",
      transport: "A request the server cannot read or route gets no envelope: it is answered with a problem document, {schema}, with an HTTP status and one of these codes. Unlike a refusal, it is not an answer about the law.",
      transportRow: "{code}, HTTP {status}: {title}",
    }),
    examples: Object.freeze({
      heading: "Asked of this server now",
      built: "When this server serves this page, it renders it from its own answer to {request}, which asks for the first act it holds, and shows that answer here whole, with the requests a program could send next about that act. This copy was served as built, without them; the examples above ask for the coverage report.",
      live: "As it served this page, this server answered this request from the law it holds:",
      answer: "Its answer, whole:",
      next: "{identifier}, the first act it holds, asked for its dossier over REST, then over MCP for its text on {date}, its latest version's date:",
      nextUndated: "{identifier}, the first act it holds, asked for its dossier over REST, then over MCP for its text:",
      refused: "It refused, inside the envelope: a refusal is an answer, read as any other.",
      none: "It holds no act to ask about.",
    }),
    citing: Object.freeze({
      heading: "Quoting and citing",
      quote: "Every quoted article carries {text}, the digest of its text; {body}, the digest of the publisher's file it was read from; {source}, its official source; and {permalink}, which pins the publisher, the act, the date and digest of the version or EU wording, the language and the article: {shape}.",
      verify: "{verify} checks a permalink's digest against the one the index holds, from what this server holds: it never asks the publisher.",
      // The published key set (R116; `V3KeySet`).
      keys: "Each release of Lex is signed. The keys that sign releases, each with its identity, role, validity and status, are published at {keySet}: check a release's signatures against that set, never only against the key the release carries.",
      forms: "On the reading page, an article is cited in three forms, “{conventional}”, “{withPermalink}” and “{evidence}”, and an export carries all three. In a document, cite the official text by its official reference.",
    }),
    limits: Object.freeze({
      heading: "Limits",
      anonymous: "There is no key and no account, and the server keeps no record of a request; for the per-client limit below, it counts requests in its working memory alone: see the {privacyPage}.",
      size: "A request body is at most {bytes} bytes; a larger one is answered with {code}.",
      paging: "Lists are paged: {limit} sets a page's size, within the bounds each request schema states, and {after} continues from where the last page stopped.",
      // The per-client limit (R109, R46): its figures are the server's (`CLIENT_LIMIT`, held to `V3ClientLimiter`).
      rate: "Each client, known by its IP address, may send {perMinute} API requests a minute, REST and MCP together, with at most {atOnce} being answered at once. Past either limit, a request to an operation (over MCP, a tool call) is refused with {code}, which carries {retryAfter}, the seconds to wait before asking again, as the {retryAfterHeader} header does; over REST its status is {status}. The service is small: send one request at a time and poll gently.",
      pages: "The pages the server renders itself have a budget of their own, {renders} a minute for each client; past it, a page is sent as built, and the browser then asks the API for its answer. No page is refused.",
      availability: "Nothing is promised about availability: see the {termsPage}.",
    }),
    events: Object.freeze({
      heading: "Following changes",
      text: "{events} reads the log of what each build of the index found: when a log begins, one {first} event for each version held, then each later build's comparison events, each dated by its build, never by when it was observed. Poll it with {after}, the cursor the last answer gave as {nextAfter}, and {limit}, and filter it with {event}.",
      atLeastOnce: "Delivery is at least once: the same cursor can answer the same events again, so a client keeps the last cursor and skips the events it has seen, by {seq} within {logId}. There are no webhooks and no push: poll.",
      names: "The events a log may hold:",
      cursor: "A cursor matches {pattern}.",
      drift: "{drift} lists the past dated answers that a later publisher revision invalidated, found from the log's revising events.",
      platform: "The events answer states its own delivery, as the server sends it:",
    }),
    replay: Object.freeze({
      heading: "Replay guarantees",
      text: "What a program can rely on when it asks again: the guarantees the specification names {first} to {last}, each as this server supports it today. Each is held by a test of {tests}, named beside it, which runs the real API on test mounts.",
      test: "Test: {test}.",
      guarantees: Object.freeze({
        G1: Object.freeze({
          name: "Versions are never rewritten",
          text: "A version is named by the digest of its text. When a later build reads a publisher's file that has been replaced, the log records it as {fileReplaced}; the version stays the same when the text is unchanged, and a new version is named only when the text changed. A permalink to the earlier version still verifies while a kept build holds its text, naming the version that replaced it.",
        }),
        G2: Object.freeze({
          name: "The same request, the same bytes",
          text: "On a server built from the same commit ({servingCommit}), every served operation answers the same request, asked of the same snapshot, with the same canonical bytes, asked again later or of a byte copy of the mount, apart from what belongs to the request or to the moment it is answered. The request's own members are {observedAt} and {requestRef}. The moment's are each channel's data age and whether it is past the maximum, {ageDays} and {channelStale}, and the envelope's {stale}, counted to the server's answer; and, when a search reads an exact reference that names no date, the date it is read at, today in Luxembourg ({recognisedDate}) if the search names none either, the version, or for an EU act the wording, that date selects, and the address the answer opens, the act's readable address or the permalink of that version or wording: a search date that is today's is no date given. A request past its client's limit is refused with {rateLimited} instead. The release is signed as a whole; an answer carries no signature of its own.",
        }),
        G3: Object.freeze({
          name: "Nothing is deleted from the log",
          text: "Each build's events are the start of the next build's log, row for row. The code can keep earlier builds beside the one a server mounts, by a retention rule: every build listed as referenced by a published permalink, the last build of each day for {days} days and the first of each month. Lex does not keep them yet: the release this site serves holds only the build its server mounts. So a version the log names is re-verifiable only while a kept build holds its text, which today means the mounted build; one whose text no kept build holds is still named by the log, without its text, and a permalink to it no longer verifies.",
        }),
        G4: Object.freeze({
          name: "Answers as one build held them",
          text: "{asObserved} answers what one build of the log held, named by its index digest, bounded by that build's time, which is an upper bound on when its texts were observed. No observation time is held, so a time is refused, and {knowableOn}, which needs one, is not served.",
        }),
        G5: Object.freeze({
          name: "Checked without Lex",
          text: "The answers of {bundle} for a version, page by page, and the publisher's file are enough to check each article's text and to recompute the file's digest, each text digest, the digest of the article identities and the version's own digest, from the derivations {provenance} publishes. Each article's identity, the wording digest, the rule-profile digests and the receipt are not recomputable: the wording digest reads a token stream the answer does not carry, and the others' derivations are not published.",
        }),
      }),
    }),
    changes: Object.freeze({
      heading: "How the API changes",
      versioned: "Each schema's identity ends in its version, as {example} does, and so does the registry's, {registry}. A change that removes or renames a member, or changes what one means, raises that version; the envelope admits no member it does not list, so a new member of the envelope raises its version too.",
      additive: "An answer's object, and a refusal's payload, may gain members within a version: a program ignores the members it does not know. Within a version of the registry, a payload never loses a field the registry requires of its code.",
      digest: "Any change to an operation's definition, to a schema the registry names or to a refusal code changes the registry's digest, which every envelope carries: a program that keeps it learns of the change from the first answer after it. A fix to how an operation behaves that leaves its definition as it is keeps the digest, so the digest says when the contract changes, not when an answer does. The out-of-scope list is not part of the registry: every {code} refusal the list decides names the list's identity, {scopeList}, and its digest, {scopeListDigest}, which changes with any change to the list.",
      registryInEnvelope: "One member of the envelope names the registry's version, {member}: a new version of the registry changes the one value that member admits, and so the envelope's schema and its digest, without raising the envelope's own version. A program that holds the envelope's schema takes it again when the registry's version changes, and checks it against the digest the registry names.",
      closed: "The operations and the refusal codes are closed sets, changed only by a decision of the specification. An operation not served from data becomes served without a new version of the registry: its refusal gives way to answers.",
      notice: "There is no period of notice and no deprecation window: a change is published with the release that makes it, on this page and in the schemas it links.",
      history: "Since {date}, the registry, {registry}, names each refusal code's payload schema and gives {code} its reason and who to ask; {previous} named neither.",
    }),
    schemas: Object.freeze({
      heading: "The schemas",
      text: "Each schema is a JSON Schema document ({dialect}), served here byte for byte as the server holds it, with the digest of its bytes; when the server loads a schema, it checks that digest against the one the registry names.",
      envelope: "The envelope: {link}",
      refusal: "The refusal: {link}",
      payloads: "Each refusal code's payload: one schema per code, linked beside its code under the refusals.",
      digest: "digest {digest}",
    }),
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
    // The items published with a consolidated EU Treaty (its Protocols, Annexes, Declarations and tables of equivalences), never
    // served as text, beside the Treaty's own articles (`europe-annexes.mjs`, `treaty_part_not_served`; review of #996, F2).
    europeTreatyParts: Object.freeze({
      one: "{count} item published with the {language} wording of the Treaty (a Protocol, Annex, Declaration or table of equivalences) is not served as text, and is never searched, quoted or exported: {reason}. Official source {source}.",
      other: "{count} items published with the {language} wording of the Treaty (Protocols, Annexes, Declarations or tables of equivalences) are not served as text, and are never searched, quoted or exported: {reason}. Official source {source}.",
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
    // An EU wording's titles and Official Journal reference, as the EU index /7 holds them (Release 3's item 1;
    // `europe-publication.mjs`): the publisher's title, or the one the act's Formex package gives, in its own language; the
    // publisher's short title; and the reference the act's Formex package states, which for a consolidated wording is the
    // publication of the act it consolidates. Said only where the answer carries them; what is not held is said apart.
    europePublication: Object.freeze({
      title: "Title in {language}, as the publisher records it: {title}",
      titleInPackage: "Title in {language}, as the act's Formex package gives it: {title}",
      shortTitle: "Short title in {language}, as the publisher records it: {title}",
      noTitle: "This site holds no title of the {language} wording.",
      journal: "Official Journal reference of the {language} wording: {reference}",
      journalConsolidated: "Official Journal reference of the act the {language} wording consolidates: {reference}",
      noJournal: "This site holds no Official Journal reference of the {language} wording.",
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
      // An Official Journal reference as EUR-Lex writes it (`europe-publication.mjs`): an issue with its date and first page,
      // or, since October 2023, the act's own number and its date; a reference whose date the package does not state is
      // said with its year.
      journal: Object.freeze({
        issue: "OJ {series} {number}, {date}, p. {page}",
        issueNoPage: "OJ {series} {number}, {date}",
        issueUndated: "OJ {series} {number} ({year}), p. {page}",
        issueUndatedNoPage: "OJ {series} {number} ({year})",
        act: "OJ {series}, {number}, {date}",
        actUndated: "OJ {series}, {number}",
      }),
      // The platform's authenticity statements for EU text (`V3CorpusMount.EuropeTextAuthenticity`), word for word, one for
      // each basis the act's own date chooses (the owner's decision of 2026-10-18, O4): the generator says the cited text's
      // language's only when the answer says exactly one of these.
      authenticity: Object.freeze({
        electronic_edition: "Only the Official Journal of the European Union published in electronic form is authentic and produces legal effects (Regulation (EU) No 216/2013, Article 1(2)); this text is a reproduction read from the Publications Office's Formex package, not the authentic edition.",
        edition_by_publication_date: "For an act published before 1 July 2013, the printed Official Journal of the European Union is authentic; the electronic edition is authentic since that date (Regulation (EU) No 216/2013). This text is a reproduction read from the Publications Office's Formex package, not the authentic edition.",
      }),
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
      // The reference the wording's Formex file states (EU index /7): the act's own, or for a consolidated wording that of the
      // act it consolidates, which is not itself published in the Official Journal.
      officialJournal: "Published in: {reference}",
      officialJournalConsolidated: "The act consolidated was published in: {reference}",
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
