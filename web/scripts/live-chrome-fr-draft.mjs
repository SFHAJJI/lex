// The French draft of the live pages' interface copy, beside the English table (`live-chrome.mjs`), for review.
//
// Decision 41: French interface copy ships only after evidence-based legal-language review; machine translation
// alone is not sufficient. This is the driver's draft for that review, as the refusal sentences were
// (`refusal-sentences.mjs`): nothing in the product imports it, and `liveChrome('fr')` refuses until the reviewed
// wording becomes a table of its own. A test holds this draft to the English table's exact shape, so an entry added
// in English without a draft is caught.
//
//   node web/scripts/live-chrome-fr-draft.mjs > chrome-fr.md
//
// Vocabulary, as in the refusal drafts: a work is an "acte", a state a "version", the publisher's article id an
// "identifiant d'article".

import { pathToFileURL } from 'node:url';

import { liveChrome } from './live-chrome.mjs';

export const LIVE_CHROME_FR_DRAFT = Object.freeze({
  coverage: Object.freeze({
    title: 'Confiance et couverture',
    eyebrow: 'Passerelle',
    heading: 'Confiance et couverture',
    intro: 'Ce que contient le corpus monté sur ce serveur et ce qu’il a consigné comme manquant, demandé au serveur au chargement de la page. La requête ne contient aucun texte de recherche.',
  }),
  search: Object.freeze({
    title: 'Recherche',
    eyebrow: 'Recherche',
    heading: 'Rechercher dans le texte détenu',
    intro: 'Trouve les articles dont le texte contient l’expression exactement telle que saisie, ou chacun de ses mots, dans le texte que ce serveur détient. L’expression est envoyée à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
  }),
  dossier: Object.freeze({
    title: 'Dossier',
    eyebrow: 'Dossier',
    heading: 'Le dossier d’un acte',
    intro: 'Ce que ce serveur détient pour un acte luxembourgeois : ses intitulés, ses versions datées par l’éditeur et ce que le dossier ne contient pas. L’identifiant est envoyé à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
  }),
  reading: Object.freeze({
    title: 'Lecture',
    eyebrow: 'Lecture',
    heading: 'Le texte à une date',
    intro: 'Le texte d’un acte luxembourgeois tel qu’il se présentait à une date, article par article, tel que l’éditeur l’a écrit, avec ce qu’il faut pour le citer. L’identifiant et la date sont envoyés à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
  }),
  history: Object.freeze({
    title: 'Historique d’une disposition',
    eyebrow: 'Historique d’une disposition',
    heading: 'Un article à travers ses versions',
    intro: 'Quelles versions détenues d’un acte luxembourgeois comportent l’identifiant d’article de l’éditeur, si sa formulation a changé d’une version à la suivante, et quelles versions détenues ne le comportent pas. L’identifiant de l’acte et l’identifiant d’article sont envoyés à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
  }),
  compare: Object.freeze({
    title: 'Comparer',
    eyebrow: 'Comparer',
    heading: 'Deux versions, article par article',
    intro: 'Les versions d’un acte luxembourgeois applicables à deux dates, comparées par les identifiants d’article et la formulation de l’éditeur, sans rien affirmer sur l’effet juridique. L’identifiant et les dates sont envoyés à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
  }),
  radar: Object.freeze({
    title: 'Radar',
    eyebrow: 'Radar',
    heading: 'Le radar des modifications',
    intro: 'Les versions datées par l’éditeur des actes luxembourgeois que ce serveur détient dans une période donnée, chacune avec la version qu’elle a remplacée et l’indication d’un changement de formulation. Les dates et tout identifiant sont envoyés à ce serveur dans la requête et nulle part ailleurs, et cette page ne conserve rien.',
  }),
  export: Object.freeze({
    title: 'Composition d’export',
    eyebrow: 'Composition d’export',
    heading: 'Emporter des articles, avec leurs citations',
    intro: 'Lisez un acte luxembourgeois à une date, épinglez les articles dont vous avez besoin et enregistrez-les en JSON, CSV ou PDF. Chaque article exporté porte sa citation, l’empreinte de son texte, sa source officielle et les droits sous lesquels il a été servi, et chaque export porte le filigrane. L’identifiant et la date sont envoyés à ce serveur dans la requête et nulle part ailleurs ; le fichier est créé dans cette page, et cette page ne conserve rien.',
  }),
  form: Object.freeze({
    workIdentifier: 'Identifiant de l’acte',
    workIdentifierOptional: 'Identifiant de l’acte (facultatif)',
    phrase: 'Expression',
    date: 'Date',
    from: 'Du',
    to: 'Au',
    articleId: 'Identifiant d’article',
    language: 'Langue',
    anyLanguage: 'Toute langue détenue',
    submit: Object.freeze({
      search: 'Rechercher',
      dossier: 'Lire le dossier',
      reading: 'Lire',
      history: 'Retracer',
      compare: 'Comparer',
      radar: 'Lister les modifications',
      export: 'Lire pour exporter',
    }),
  }),
});

/** Every entry of a table as `[path, text]`, in the table's order. */
export function entriesOf(table, path = []) {
  return Object.entries(table).flatMap(([key, value]) => (typeof value === 'string' ? [[[...path, key].join('.'), value]] : entriesOf(value, [...path, key])));
}

/** The draft beside the English, as Markdown, for review. */
export function renderChromeDraft() {
  const french = new Map(entriesOf(LIVE_CHROME_FR_DRAFT));
  return [
    '# The live pages\' interface copy: English, and the French draft for review',
    '',
    'Decision 41: French interface copy ships only once reviewed. Printed by `node web/scripts/live-chrome-fr-draft.mjs`',
    'from the English table the pages render (`live-chrome.mjs`) and the draft beside it.',
    '',
    '| Entry | English (served) | French (draft) |',
    '|-------|------------------|----------------|',
    ...entriesOf(liveChrome('en')).map(([path, english]) => `| \`${path}\` | ${english} | ${french.get(path) ?? '(no draft)'} |`),
    '',
  ].join('\n');
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.stdout.write(renderChromeDraft());
}
