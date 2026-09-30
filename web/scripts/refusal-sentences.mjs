// The live pages' refusal sentences, in English as served and in French as the driver's draft, for the
// owner's review at the weekly checkpoint (Decision 95, ruling 4).
//
// The English sentences are collected from the live screens themselves (each screen's refusal table,
// the shared `no_corpus_mounted` sentence for each index a payload can name, and the two sentences a
// page says when it names a refusal only by its code), so the list is the product's and cannot drift
// from it. The French column is a draft: no French string ships until it is reviewed
// (`localization.mjs`: never machine-translated authoritative copy), and nothing in the product imports
// this file. A test holds every served English sentence to one French draft and no draft to a sentence
// the pages no longer say. The list is printed, not checked in (the tree admits no such file), and
// goes to the checkpoint in the pull request that changes it:
//
//   node web/scripts/refusal-sentences.mjs > refusal-sentences.md

import { pathToFileURL } from 'node:url';

import * as coverage from './live-coverage.mjs';
import * as search from './live-search.mjs';
import * as dossier from './live-dossier.mjs';
import * as reading from './live-reading.mjs';
import * as history from './live-history.mjs';
import * as compare from './live-compare.mjs';
import * as radar from './live-radar.mjs';
import { noCorpusMountedSentence } from './live-refusals.mjs';

/** The live screens, in the launch contract's order, each with its refusal table. */
export const SCREENS = Object.freeze([
  Object.freeze({ id: 'coverage', name: 'Trust and Coverage', module: coverage, table: coverage.LIVE_COVERAGE_REFUSAL_SENTENCES }),
  Object.freeze({ id: 'search', name: 'Search', module: search, table: search.LIVE_SEARCH_REFUSAL_SENTENCES }),
  Object.freeze({ id: 'dossier', name: 'Work dossier', module: dossier, table: dossier.LIVE_DOSSIER_REFUSAL_SENTENCES }),
  Object.freeze({ id: 'reading', name: 'Reading and Export composer', module: reading, table: reading.LIVE_READING_REFUSAL_SENTENCES }),
  Object.freeze({ id: 'history', name: 'Provision history', module: history, table: history.LIVE_HISTORY_REFUSAL_SENTENCES }),
  Object.freeze({ id: 'compare', name: 'Compare', module: compare, table: compare.LIVE_COMPARE_REFUSAL_SENTENCES }),
  Object.freeze({ id: 'radar', name: 'Radar', module: radar, table: radar.LIVE_RADAR_REFUSAL_SENTENCES }),
]);

/** The screens that say `no_corpus_mounted` with the shared sentence naming the missing index. */
const NAMING_THE_INDEX = Object.freeze(['search', 'dossier', 'reading', 'history', 'compare', 'radar']);

/**
 * The French drafts, keyed by the English sentence they translate. "Work" is "acte", "state" is
 * "version" (the consolidated text as it stood from a date), "build" is "déploiement".
 */
export const FRENCH_DRAFTS = Object.freeze({
  'This build has no index mounted.': 'Ce déploiement n’a aucun index monté.',
  'This build has no Luxembourg index mounted.': 'Ce déploiement n’a pas d’index luxembourgeois monté.',
  'This build has no EU index mounted.': 'Ce déploiement n’a pas d’index de l’Union européenne monté.',
  "This build has no index mounted for this request's publisher.": 'Ce déploiement n’a pas d’index monté pour l’éditeur de cette demande.',
  'This index holds no searchable text in the language asked for.': 'Cet index ne contient aucun texte consultable dans la langue demandée.',
  'This index holds no work under that identifier.': 'Cet index ne contient aucun acte sous cet identifiant.',
  'This work is not held in the language asked for.': 'Cet acte n’est pas disponible dans la langue demandée.',
  'No state of this work that this index holds applies on that date.': 'Aucune version de cet acte contenue dans cet index ne s’applique à cette date.',
  'Several states of this work apply on that date, and none is chosen.': 'Plusieurs versions de cet acte s’appliquent à cette date, et aucune n’est retenue.',
  "This state's text is withheld: its rights did not admit it.": 'Le texte de cette version n’est pas communiqué : ses droits ne le permettaient pas.',
  'This index holds this state but no text for it.': 'Cet index contient cette version, mais aucun texte pour elle.',
  "This index cannot read this work's text.": 'Cet index ne peut pas lire le texte de cet acte.',
  'No state of this work that this index holds carries that article id.': 'Aucune version de cet acte contenue dans cet index ne comporte cet identifiant d’article.',
  "This index cannot trace this work's articles.": 'Cet index ne peut pas retracer les articles de cet acte.',
  'No state of this work that this index holds applies on one of the dates.': 'Aucune version de cet acte contenue dans cet index ne s’applique à l’une des deux dates.',
  'Several states of this work apply on one of the dates, and none is chosen.': 'Plusieurs versions de cet acte s’appliquent à l’une des deux dates, et aucune n’est retenue.',
  'The two states were read under different rule profiles, so they are not compared.': 'Les deux versions ont été lues selon des profils de règles différents ; elles ne sont donc pas comparées.',
  "This index cannot compare this work's states.": 'Cet index ne peut pas comparer les versions de cet acte.',
  'This index holds no state in the language asked for.': 'Cet index ne contient aucune version dans la langue demandée.',
  "This index cannot list this work's changes.": 'Cet index ne peut pas lister les modifications de cet acte.',
});

/**
 * The French drafts of the two sentences a page says when it names a refusal only by its code, per
 * screen (the noun and its agreement differ), with the same `{code}` and `{reason}` placeholders.
 */
export const FRENCH_TEMPLATE_DRAFTS = Object.freeze({
  coverage: Object.freeze({
    unexpected: 'Le rapport de couverture a été refusé avec le code {code}.',
    unshown: 'Le rapport de couverture a été refusé avec le code {code}, et sa fiche ne peut pas être affichée : {reason}.',
  }),
  search: Object.freeze({
    unexpected: 'La recherche a été refusée avec le code {code}.',
    unshown: 'La recherche a été refusée avec le code {code}, et sa fiche ne peut pas être affichée : {reason}.',
  }),
  dossier: Object.freeze({
    unexpected: 'Le dossier a été refusé avec le code {code}.',
    unshown: 'Le dossier a été refusé avec le code {code}, et sa fiche ne peut pas être affichée : {reason}.',
  }),
  reading: Object.freeze({
    unexpected: 'La lecture a été refusée avec le code {code}.',
    unshown: 'La lecture a été refusée avec le code {code}, et sa fiche ne peut pas être affichée : {reason}.',
  }),
  history: Object.freeze({
    unexpected: 'L’historique de la disposition a été refusé avec le code {code}.',
    unshown: 'L’historique de la disposition a été refusé avec le code {code}, et sa fiche ne peut pas être affichée : {reason}.',
  }),
  compare: Object.freeze({
    unexpected: 'La comparaison a été refusée avec le code {code}.',
    unshown: 'La comparaison a été refusée avec le code {code}, et sa fiche ne peut pas être affichée : {reason}.',
  }),
  radar: Object.freeze({
    unexpected: 'Le radar des modifications a été refusé avec le code {code}.',
    unshown: 'Le radar des modifications a été refusé avec le code {code}, et sa fiche ne peut pas être affichée : {reason}.',
  }),
});

/**
 * Every refusal sentence the live pages say for a code, once each, with the pages and codes that say
 * it, in the order the screens first say them.
 */
export function servedRefusalSentences() {
  const rows = new Map();
  const add = (sentence, screen, code) => {
    const row = rows.get(sentence) ?? { sentence, uses: [] };
    row.uses.push({ screen, code });
    rows.set(sentence, row);
  };
  for (const screen of SCREENS) {
    for (const [code, sentence] of Object.entries(screen.table)) add(sentence, screen.id, code);
    if (screen.id === NAMING_THE_INDEX[0]) {
      for (const requiredCorpus of ['lu', 'eu', null]) {
        for (const id of NAMING_THE_INDEX) add(noCorpusMountedSentence({ required_corpus: requiredCorpus }), id, 'no_corpus_mounted');
      }
    }
  }
  return [...rows.values()].map((row) => Object.freeze({ sentence: row.sentence, uses: Object.freeze(row.uses) }));
}

/** The two sentences each page says when it names a refusal only by its code, with placeholders. */
export function servedRefusalTemplates() {
  return SCREENS.map((screen) => Object.freeze({
    screen: screen.id,
    unexpected: screen.module.unexpectedRefusalSentence('{code}'),
    unshown: screen.module.unshownRefusalSentence('{code}', '{reason}'),
  }));
}

function pagesOf(uses) {
  const names = [];
  for (const use of uses) {
    const name = SCREENS.find((screen) => screen.id === use.screen).name;
    if (!names.includes(name)) names.push(name);
  }
  return names.join(', ');
}

/** The checkpoint list, as Markdown. */
export function renderCheckpointList() {
  const sentences = servedRefusalSentences();
  const lines = [
    '# Refusal sentences, English and French, for the owner\'s review',
    '',
    'Owner ruling 4 of 2026-09-30 (Decision 95): the owner reviews the French and English refusal',
    'sentences at the weekly checkpoint. This is the list, printed by `node web/scripts/refusal-sentences.mjs`',
    'from the sentences the live pages say, so it is the product\'s own.',
    '',
    '- **English**: served today, on the live pages.',
    '- **French**: the driver\'s draft. Nothing French ships until it is reviewed (no',
    '  machine-translated authoritative copy); the reviewed wording then replaces the draft here and',
    '  ships with the French interface.',
    '- Vocabulary in the drafts: a *work* is an *acte*, a *state* is a *version* (the text as it stood',
    '  from a date), a *build* is a *déploiement*, a refusal *card* is a *fiche*.',
    '',
    `## Refusal sentences by code (${sentences.length})`,
    '',
    '| # | Code | Pages | English (served) | French (draft) |',
    '|---|------|-------|------------------|----------------|',
    ...sentences.map((row, index) => {
      const codes = [...new Set(row.uses.map((use) => `\`${use.code}\``))].join(', ');
      return `| ${index + 1} | ${codes} | ${pagesOf(row.uses)} | ${row.sentence} | ${FRENCH_DRAFTS[row.sentence] ?? '(no draft)'} |`;
    }),
    '',
    '`no_corpus_mounted` names the index its payload says is missing (Luxembourg, EU, or neither), so',
    'three sentences stand for one code.',
    '',
    '## When a page names a refusal only by its code',
    '',
    'A code a page has no sentence for, and a refusal whose card cannot be shown, are said with the code',
    '(and the reason the card cannot be shown). `{code}` and `{reason}` are filled when said.',
    '',
    '| Page | English (served) | French (draft) |',
    '|------|------------------|----------------|',
    ...servedRefusalTemplates().flatMap((row) => {
      const name = SCREENS.find((screen) => screen.id === row.screen).name;
      const drafts = FRENCH_TEMPLATE_DRAFTS[row.screen] ?? {};
      return [
        `| ${name} | ${row.unexpected} | ${drafts.unexpected ?? '(no draft)'} |`,
        `| ${name} | ${row.unshown} | ${drafts.unshown ?? '(no draft)'} |`,
      ];
    }),
    '',
  ];
  return lines.join('\n');
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.stdout.write(renderCheckpointList());
}
