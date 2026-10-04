// The live pages' French beside their English, printed for the owner, who may revise any of it: the refusal
// sentences (Decision 95, ruling 4) and the interface table (Decision 41).
//
// The English sentences are collected from the live screens themselves (each screen's refusal table,
// the shared `no_corpus_mounted` sentence for each index a payload can name, and the two sentences a
// page says when it names a refusal only by its code), so the list is the product's and cannot drift
// from it. The French is the reviewed French the French pages say (`live-chrome-fr.mjs`), reviewed by
// Claude (AI reviewer), under the owner's delegation of 2026-10-02. A test holds every served English
// sentence to one French sentence and no French sentence to one the pages no longer say. Nothing in the
// product imports this file (it imports every screen). The list is printed, not checked in (the tree
// admits no such file), and goes to the checkpoint in the pull request that changes it:
//
//   node web/scripts/refusal-sentences.mjs > french-and-english.md

import { pathToFileURL } from 'node:url';

import * as coverage from './live-coverage.mjs';
import * as search from './live-search.mjs';
import * as dossier from './live-dossier.mjs';
import * as reading from './live-reading.mjs';
import * as history from './live-history.mjs';
import * as compare from './live-compare.mjs';
import * as radar from './live-radar.mjs';
import { historyBeginsHint, nearestAnchorsHint, noCorpusMountedSentence } from './live-refusals.mjs';
import { LIVE_CHROME, entriesOf } from './live-chrome.mjs';
import { REFUSALS_FR } from './live-chrome-fr.mjs';

/** The live screens, in the launch contract's order, each with its refusal table. */
export const SCREENS = Object.freeze([
  Object.freeze({ id: 'coverage', name: 'Trust and Coverage', module: coverage, table: coverage.LIVE_COVERAGE_REFUSAL_SENTENCES }),
  Object.freeze({ id: 'search', name: 'Search', module: search, table: search.LIVE_SEARCH_REFUSAL_SENTENCES }),
  Object.freeze({ id: 'dossier', name: 'Work dossier', module: dossier, table: dossier.LIVE_DOSSIER_REFUSAL_SENTENCES }),
  Object.freeze({ id: 'reading', name: 'Reading and Export composer', module: reading, table: reading.LIVE_READING_REFUSAL_SENTENCES, europeTable: reading.LIVE_READING_EUROPE_REFUSAL_SENTENCES }),
  Object.freeze({ id: 'history', name: 'Provision history', module: history, table: history.LIVE_HISTORY_REFUSAL_SENTENCES }),
  Object.freeze({ id: 'compare', name: 'Compare', module: compare, table: compare.LIVE_COMPARE_REFUSAL_SENTENCES }),
  Object.freeze({ id: 'radar', name: 'Radar', module: radar, table: radar.LIVE_RADAR_REFUSAL_SENTENCES }),
]);

/** The screens that say `no_corpus_mounted` with the shared sentence naming the missing index. */
const NAMING_THE_INDEX = Object.freeze(['search', 'dossier', 'reading', 'history', 'compare', 'radar']);

/** The French each page says, keyed as the screens hold the English (`REFUSALS_FR`, reviewed). */
export const FRENCH_SENTENCES = REFUSALS_FR.sentences;
export const FRENCH_TEMPLATES = REFUSALS_FR.templates;
export const FRENCH_HINTS = REFUSALS_FR.hints;

/**
 * The hints a refusal whose card cannot be shown carries after its sentence, with the pages that say
 * them, as templates (review of #793: the list left them out).
 */
export const SERVED_HINTS = Object.freeze([
  Object.freeze({ template: historyBeginsHint('{date}'), code: 'no_version_for_date', screens: Object.freeze(['reading', 'compare']) }),
  Object.freeze({ template: nearestAnchorsHint(['{ids}']), code: 'anchor_not_in_version', screens: Object.freeze(['history']) }),
]);

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
    // A screen that words an EU refusal apart says those sentences too (review of #903).
    for (const [code, sentence] of Object.entries(screen.europeTable ?? {})) add(sentence, screen.id, code);
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
    '- **English**: served on the English pages.',
    '- **French**: served on the French pages (`/fr/`), reviewed by Claude (AI reviewer), under the',
    '  owner\'s delegation of 2026-10-02. The owner may revise any sentence; the revision replaces it',
    '  in `web/scripts/live-chrome-fr.mjs`.',
    '- Vocabulary: a *work* is an *acte*, a Luxembourg *state* is a *version*, an EU *wording* is a',
    '  *libellé*, a *build* is a *déploiement*, a refusal *card* is a *fiche*.',
    '',
    `## Refusal sentences by code (${sentences.length})`,
    '',
    '| # | Code | Pages | English (served) | French (served) |',
    '|---|------|-------|------------------|----------------|',
    ...sentences.map((row, index) => {
      const codes = [...new Set(row.uses.map((use) => `\`${use.code}\``))].join(', ');
      return `| ${index + 1} | ${codes} | ${pagesOf(row.uses)} | ${row.sentence} | ${FRENCH_SENTENCES[row.sentence] ?? '(no French)'} |`;
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
    '| Page | English (served) | French (served) |',
    '|------|------------------|----------------|',
    ...servedRefusalTemplates().flatMap((row) => {
      const name = SCREENS.find((screen) => screen.id === row.screen).name;
      const french = FRENCH_TEMPLATES[row.screen] ?? {};
      return [
        `| ${name} | ${row.unexpected} | ${french.unexpected ?? '(no French)'} |`,
        `| ${name} | ${row.unshown} | ${french.unshown ?? '(no French)'} |`,
      ];
    }),
    '',
    '## The hint a card that cannot be shown still carries',
    '',
    'Said after the sentence above, so the reader can ask again. `{date}` and `{ids}` are filled when said.',
    '',
    '| Code | Pages | English (served) | French (served) |',
    '|------|-------|------------------|----------------|',
    ...SERVED_HINTS.map((hint) => `| \`${hint.code}\` | ${pagesOf(hint.screens.map((screen) => ({ screen })))} | ${hint.template} | ${FRENCH_HINTS[hint.template] ?? '(no French)'} |`),
    '',
  ];
  return lines.join('\n');
}

/** The interface table, English beside French, entry by entry (what the draft module printed for the review). */
export function renderChromeList() {
  const french = new Map(entriesOf(LIVE_CHROME.fr));
  return [
    '# The live pages\' interface copy, English and French',
    '',
    'Every entry of the chrome table the live pages render (`web/scripts/live-chrome.mjs`, and',
    '`web/scripts/live-chrome-fr.mjs` for the French). A no-break space (U+00A0) stands before : ; ! ? %',
    'and » and after « in the French.',
    '',
    '| Entry | English | French |',
    '|-------|---------|--------|',
    ...entriesOf(LIVE_CHROME.en).map(([path, english]) => `| \`${path}\` | ${english} | ${french.get(path) ?? '(no French)'} |`),
    '',
  ].join('\n');
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.stdout.write(`${renderCheckpointList()}\n${renderChromeList()}`);
}
