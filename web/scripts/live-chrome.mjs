// The live pages' interface copy, in one table, so every language the interface is offered in is
// one reviewed table beside this one (Decision 41; the launch contract's chrome line).
//
// English is the source. French is drafted beside it for review and ships only when reviewed:
// until then `liveChrome` answers only for English, and French, German and Luxembourgish answer
// `localization_unavailable` (`live-locale-page.jsx`). The table holds each page's title, eyebrow,
// heading and introduction, the forms' labels and buttons, each screen's idle sentence, and the
// sentences the search, dossier, reading and history screens say about an answer. A sentence with
// values in it is a template with `{name}` placeholders (`fillParts`), so a translation can put them
// where its grammar needs them; a sentence that counts is `{ one, other }`, chosen by the language's
// plural rule (`countedEntry`). The phrases the platform itself sends (a matching rule, a scope, a
// reason something is not held) are the platform's English and are shown as sent.

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
    digest: "Text digest {digest}, {permalink}",
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
  }),
  radar: Object.freeze({
    title: "Radar",
    eyebrow: "Radar",
    heading: "The change radar",
    intro: "The publisher-dated states of Luxembourg works that this server holds in a window of dates, each with the state it replaced and whether its wording changed. The dates and any identifier go to this server in the request and nowhere else, and this page keeps nothing.",
    idle: "Type two dates to list the publisher-dated states in that window.",
  }),
  export: Object.freeze({
    title: "Export composer",
    eyebrow: "Export composer",
    heading: "Take articles away, with their citations",
    intro: "Read one Luxembourg work on one date, pin the articles you need, and save them as JSON, CSV or PDF. Each exported article carries its citation, its text digest, its official source and the rights it was served under, and every export carries the watermark. The identifier and the date go to this server in the request and nowhere else; the file is made in this page, and this page keeps nothing.",
    idle: "Type a work identifier and a date, then pin the articles to take away.",
  }),
  common: Object.freeze({
    loading: "Asking this server.",
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

/** The interface copy for a reviewed language; any other throws, since serving it would be a substitution. */
export function liveChrome(locale = 'en') {
  const table = LIVE_CHROME[locale];
  if (table === undefined) throw new Error(`no reviewed interface copy in ${JSON.stringify(locale)}: it answers localization_unavailable`);
  return table;
}

/**
 * The entry of a counted sentence (`{ one, other }`) for a count, chosen by the language's own plural
 * rule: French says "0 version" where English says "0 states", so the rule is the language's, never a
 * comparison with 1.
 */
export function countedEntry(entry, count, locale = 'en') {
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
export function fillCounted(entry, count, values = {}, locale = 'en') {
  return fillText(countedEntry(entry, count, locale), { count, ...values });
}
