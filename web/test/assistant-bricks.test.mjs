// The dock's reading of a bricks answer (V4, plan /2), held to answers the engine itself made: `bricks-answers.json` holds
// six dossiers the pipeline answered over the test corpus of the law of 27 July 1997 on the insurance contract, with no
// model, on 18/11/2026 (a definition, a rule and its cases, an absence shown by its sweep, two answers by scope, a refusal
// with the law beside it, a greeting). What is checked: every word drawn is the server's sentence or the law's own; one line
// of the law is quoted, not the whole article; the stages are the bricks', tagged "AI model" only when a call is recorded.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { ASSISTANT_PANEL_COPY, bricksBody, bricksOf, dockTurnOf, quoteLines, stageRows, validateDossier } from '../scripts/assistant-panel.mjs';
import { bundle } from '../scripts/react-build.mjs';

const answers = JSON.parse(readFileSync(new URL('./bricks-answers.json', import.meta.url), 'utf8'));
const EN = ASSISTANT_PANEL_COPY.en;
const FR = ASSISTANT_PANEL_COPY.fr;

const asked = (dossier) => (dossier.verdict === 'refuse'
  ? { state: 'refusal', envelope: { refusal: { code: 'advice_boundary', helpful_payload: { answer_dossier: dossier } } } }
  : { state: 'success', envelope: { result: { object_type: 'answer_dossier', value: dossier } } });

let dockModule;
async function dock() {
  if (!dockModule) dockModule = await import(pathToFileURL(await bundle('app/AssistantDock.jsx', 'assistant-bricks-test.mjs')).href);
  return dockModule;
}

test('every sample is an answer_dossier/1 the panel accepts, and carries the bricks record in its trace', () => {
  for (const [name, dossier] of Object.entries(answers)) {
    assert.doesNotThrow(() => validateDossier(dossier), name);
    assert.equal(bricksOf(dossier)?.version, 'lex-v4-bricks/1', name);
    assert.equal(dossier.trace.plan.schema, 'lex-v3-assistant-plan/2', name);
  }
  assert.equal(bricksOf({ trace: { plan: { schema: 'lex-v3-assistant-plan/1' } } }), null, 'an answer the planner model planned has none');
});

test('a bricks answer that quotes is an answer; one that quotes nothing says its own sentences; a refuse stays the advice boundary', () => {
  assert.equal(dockTurnOf(asked(answers.definition), 'fr').kind, 'answer');
  assert.equal(dockTurnOf(asked(answers.rule), 'fr').kind, 'answer');
  assert.equal(dockTurnOf(asked(answers.several), 'fr').kind, 'answer');
  assert.equal(dockTurnOf(asked(answers.absence), 'fr').kind, 'said', 'a sweep that proves an absence is not "not found by search"');
  assert.equal(dockTurnOf(asked(answers.greeting), 'fr').kind, 'said');
  assert.equal(dockTurnOf(asked(answers.refusal), 'fr').kind, 'advice');
});

test('the definition is one line of the law, with its article and heading, and the note on the reader\'s word', () => {
  const body = bricksBody(answers.definition);
  const quotes = body.filter((entry) => entry.type === 'quote');
  assert.equal(quotes.length, 1);
  assert.equal(quotes[0].quote.text, 'Personne lésée: dans une assurance de responsabilité, la personne victime d\'un dommage dont l\'assuré est responsable.');
  assert.equal(quotes[0].quote.anchor, 'art_1er');
  assert.equal(quotes[0].heading, 'Définitions');
  assert.equal(quotes[0].date, '2026-11-18');
  assert.ok(quotes[0].quote.offset > 0, 'the line at its place in the article, not the article from its start');
  assert.equal(body[0].type, 'line');
  assert.equal(body[0].template, 'answer.definition');
  assert.ok(body.some((entry) => entry.type === 'remark' && entry.text.includes('personne lésée')), 'the note: the reader wrote "personne lésé"');
  const more = body.find((entry) => entry.type === 'more');
  assert.equal(more.articles.length, 10, 'the articles that use the term, one step away');
  assert.ok(more.articles.every((article) => article.identifier === '/lu-legilux/loi-1997-07-27-n10' && article.date === '2026-11-18'));
  assert.ok(!body.some((entry) => entry.type === 'line' && /^(Empreinte|Permalien|Source officielle|Applicable)/u.test(entry.text)), 'the digest and the links stay in the answer, not in the card');
});

test('the rule is shown first, then its cases, each a line with its own article; no yes and no no', () => {
  const body = bricksBody(answers.rule);
  const quotes = body.filter((entry) => entry.type === 'quote');
  assert.equal(quotes[0].quote.text, 'Hormis les cas prévus par la loi, l\'assureur ne peut résilier le contrat.');
  assert.equal(quotes[0].heading, 'Cas, formes et effets de la résiliation');
  assert.equal(quotes.length, 5);
  assert.deepEqual(new Set(quotes.map((entry) => entry.quote.anchor)).size, 5, 'five articles');
  const lines = body.filter((entry) => entry.type === 'line').map((entry) => entry.template);
  assert.deepEqual(lines.slice(0, 2), ['answer.rule', 'answer.rule_cases']);
  assert.ok(body.findIndex((entry) => entry.template === 'answer.rule_cases') > body.findIndex((entry) => entry.type === 'quote'), 'the cases come after the rule');
  assert.ok(body.some((entry) => entry.type === 'remark' && entry.text.includes('unilatérale')), 'the word the law does not use is said');
  assert.ok(!body.some((entry) => typeof entry.text === 'string' && /^(Oui|Non)\b/u.test(entry.text)));
});

test('an absence shows the sweep and what it proves; a greeting says one sentence', () => {
  const absence = bricksBody(answers.absence);
  assert.ok(absence.every((entry) => entry.type === 'line'));
  assert.match(absence[0].text, /lignes\u00A0: 913\u00A0; articles\u00A0: 147/u);
  assert.equal(absence[1].text, 'Lignes contenant «\u00A0franchise\u00A0»\u00A0: 0\u00A0; articles concernés\u00A0: 0.');
  assert.equal(absence.at(-1).template, 'absence.terms_nowhere');
  assert.deepEqual(bricksBody(answers.greeting).map((entry) => entry.template), ['direct.greeting']);
});

test('a quotation of several lines is broken where the law starts a line, and no character is added or dropped', () => {
  // The publisher's lines follow each other with nothing between them, as the stored text holds them.
  const parts = ['Assuré: ', 'dans une assurance de dommages: la personne garantie;', 'dans une assurance de personnes: la personne assurée.'];
  const quote = { text: parts.join(''), breaks: [parts[0].length, parts[0].length + parts[1].length] };
  assert.deepEqual([...quoteLines(quote)], parts.map((part) => part.trim()));
  assert.equal(quoteLines(quote).join('').length, quote.text.length - 1, 'only the white space at a break is trimmed');
  assert.deepEqual([...quoteLines({ text: 'Une ligne.' })], ['Une ligne.']);
  assert.deepEqual([...quoteLines({ text: 'Une ligne.', breaks: [0, 99, 'x'] })], ['Une ligne.'], 'a break outside the text is no break');
});

test('the stages are the bricks\', each read from the trace, and none is tagged "AI model" when no call is recorded', () => {
  const rows = stageRows(answers.rule, 'fr');
  assert.deepEqual(rows.map((row) => row.key), ['masking', 'question', 'route', 'retrieval', 'checks']);
  assert.ok(rows.every((row) => row.model === false), 'no model was asked for these samples');
  assert.equal(rows[1].line, '2 termes du dictionnaire reconnus\u00A0; réponse cherchée\u00A0: une règle');
  assert.equal(rows[2].line, FR.dockRouteRule);
  assert.equal(rows[3].line, 'le terme figure sur 45 lignes, dans 25 articles\u00A0; 14 retenues');
  assert.equal(rows[4].line, 'citations identiques au texte\u00A0: 5');
  assert.equal(stageRows(answers.definition, 'en')[2].line, EN.dockRouteDefinition);
  assert.equal(stageRows(answers.definition, 'en')[3].line, EN.dockRetrievalLine);
  assert.equal(stageRows(answers.absence, 'en')[3].line, EN.dockRetrievalNone);
  assert.equal(stageRows(answers.greeting, 'en')[3].line, EN.dockRetrievalSkipped);

  // With the two model steps recorded, they are rows of their own, and only they are tagged.
  const withModel = structuredClone(answers.rule);
  withModel.trace.plan.bricks.model_calls = [{ step: 'arbiter' }, { step: 'extraction' }];
  withModel.trace.plan.bricks.candidates[0].role = 'primary';
  withModel.trace.plan.bricks.candidates[1].role = 'supporting';
  const called = stageRows(withModel, 'en');
  assert.deepEqual(called.filter((row) => row.model).map((row) => row.key), ['arbiter', 'extraction']);
  assert.equal(called.find((row) => row.key === 'arbiter').line, 'main: 1; supporting: 1; set aside: 0');
});

test('the dock draws a bricks answer: the lead line marked, each case with its article and heading, the further articles as links', async () => {
  const { DockView } = await dock();
  const turn = { id: 't1', kind: 'answer', question: 'l\'assureur peut résilier le contrat de façon unilatérale ?', dossier: answers.rule, servingCommit: null, seconds: 0.2 };
  const markup = renderToStaticMarkup(h(DockView, { page: 'live-home', locale: 'fr', shown: turn }));
  assert.match(markup, /<mark class="lx-dock-mark">Hormis les cas prévus par la loi, l&#x27;assureur ne peut résilier le contrat\.<\/mark>/u);
  assert.equal((markup.match(/data-dock-extract=/gu) ?? []).length, 5);
  assert.match(markup, /Article 22, Mise en demeure et faculté de résiliation\./u);
  assert.match(markup, /class="lx-dock-chip"[^>]*>Article 34</u);
  assert.match(markup, /La règle, dans les termes du texte/u);
  assert.match(markup, /Cas prévus par le texte, relevés dans ce texte/u);
  assert.ok(markup.includes(FR.dockBricksNoModel), 'the work says no model was asked');
  assert.ok(!markup.includes(FR.dockPlanNote), 'and no longer that a model planned');
  assert.ok(!markup.includes('[…]'), 'a line is shown whole');

  const said = { id: 't2', kind: 'said', question: 'C\'est quoi la franchise ?', dossier: answers.absence, servingCommit: null };
  const absence = renderToStaticMarkup(h(DockView, { page: 'live-home', locale: 'fr', shown: said }));
  assert.match(absence, /data-dock-card="said"/u);
  assert.match(absence, /Aucun de ces termes ne figure dans ce texte\./u);
  assert.ok(!absence.includes(FR.dockNotFound), 'an absence proved by a sweep is not the card of a search that found nothing');
});

test('the dock\'s new words are in both copy tables', () => {
  for (const key of Object.keys(EN).filter((name) => /^dock(Stage(Question|Route|Retrieval|Arbiter|Extraction|Checks)|Question|Kind|Route|Retrieval|Arbiter|Extraction|Checks|Bricks|Said)/u.test(name))) {
    assert.equal(typeof FR[key], 'string', key);
    assert.ok(FR[key].length > 0 && EN[key].length > 0, key);
  }
});
