// The live pages' interface copy, in one table, so every language the interface is offered in is
// one reviewed table beside this one (Decision 41; the launch contract's chrome line).
//
// English is the source. French is drafted beside it for review and ships only when reviewed:
// until then `liveChrome` answers only for English, and French, German and Luxembourgish answer
// `localization_unavailable` (`live-locale-page.jsx`). This first table holds each page's title,
// eyebrow, heading and introduction, and the forms' labels and buttons; the screens' own sentences
// follow into it.

const EN = Object.freeze({
  coverage: Object.freeze({
    title: "Trust and Coverage",
    eyebrow: "Gateway",
    heading: "Trust and Coverage",
    intro: "What the corpus this server mounts holds and what it recorded as missing, asked of the server when the page loads. The request carries no query text.",
  }),
  search: Object.freeze({
    title: "Search",
    eyebrow: "Search",
    heading: "Search the held text",
    intro: "Finds the articles whose text contains the phrase exactly as typed, or every word of it, in the text this server holds. The phrase goes to this server in the request and nowhere else, and this page keeps nothing.",
  }),
  dossier: Object.freeze({
    title: "Dossier",
    eyebrow: "Dossier",
    heading: "A work's dossier",
    intro: "What this server holds for one Luxembourg work: its titles, its publisher-dated states and what the dossier does not hold. The identifier goes to this server in the request and nowhere else, and this page keeps nothing.",
  }),
  reading: Object.freeze({
    title: "Reading",
    eyebrow: "Reading",
    heading: "The text on a date",
    intro: "The text of one Luxembourg work as it stood on one date, article by article, as the publisher wrote it, with what a quotation of it needs. The identifier and the date go to this server in the request and nowhere else, and this page keeps nothing.",
  }),
  history: Object.freeze({
    title: "Provision history",
    eyebrow: "Provision history",
    heading: "One article through its states",
    intro: "Which held states of one Luxembourg work carry the publisher's article id, whether its wording changed from one state to the next, and which held states do not carry it. The identifier and the article id go to this server in the request and nowhere else, and this page keeps nothing.",
  }),
  compare: Object.freeze({
    title: "Compare",
    eyebrow: "Compare",
    heading: "Two states, article by article",
    intro: "The states of one Luxembourg work that applied on two dates, compared by the publisher's article ids and wording, with nothing said about legal effect. The identifier and the dates go to this server in the request and nowhere else, and this page keeps nothing.",
  }),
  radar: Object.freeze({
    title: "Radar",
    eyebrow: "Radar",
    heading: "The change radar",
    intro: "The publisher-dated states of Luxembourg works that this server holds in a window of dates, each with the state it replaced and whether its wording changed. The dates and any identifier go to this server in the request and nowhere else, and this page keeps nothing.",
  }),
  export: Object.freeze({
    title: "Export composer",
    eyebrow: "Export composer",
    heading: "Take articles away, with their citations",
    intro: "Read one Luxembourg work on one date, pin the articles you need, and save them as JSON, CSV or PDF. Each exported article carries its citation, its text digest, its official source and the rights it was served under, and every export carries the watermark. The identifier and the date go to this server in the request and nowhere else; the file is made in this page, and this page keeps nothing.",
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
