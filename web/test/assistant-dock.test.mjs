// The assistant docked beside every main page (the owner's brief of 2026-11-15, sections 5 and 6; the mockup's V1Home,
// V2Read, V3Compare and V4Changes), held to drafted answers shaped as the live ones of 15/11/2026 (drafted by an agent; not
// D4 or D1): its first screen, the answer card and "How this answer was built", the states of section 6, the search-first
// rule, the conversation kept on another page, the tab it folds to, and every word it shows from its copy tables.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { build } from 'esbuild';

import { ASSISTANT_PANEL_COPY, builtFoot, clarifyCandidates, dockTurnOf, stageRows, stripSentence } from '../scripts/assistant-panel.mjs';
import { articleStrip, bundleFacts, firstSentenceOf, maskedCount, numericDate, plannerName, referenceOpening } from '../scripts/answer-view.mjs';
import { EVALUATION_RECORD } from '../scripts/evaluation-record.mjs';
import { REGISTRY_RECORD } from '../scripts/registry-record.mjs';
import { SEARCH_MODE } from '../scripts/live-search.mjs';
import { textHref } from '../scripts/text-address.mjs';
import { QUOTE_CLOSE, QUOTE_OPEN } from '../scripts/quotation-marks.mjs';
import { bundle } from '../scripts/react-build.mjs';

const web = new URL('../', import.meta.url);
const samples = JSON.parse(readFileSync(new URL('../schemas/v3-platform/envelope-samples.json', web), 'utf8'));
const EN = ASSISTANT_PANEL_COPY.en;
const FR = ASSISTANT_PANEL_COPY.fr;

let dockModule;
async function dock() {
  if (!dockModule) dockModule = await import(pathToFileURL(await bundle('app/AssistantDock.jsx', 'assistant-dock-test.mjs')).href);
  return dockModule;
}

// --- Drafted answers, shaped as the live ones (15/11/2026) -------------------------------------------------------------------

const claim = (template_id, text, locale = 'fr', facts = []) => ({ kind: 'claim', template_id, locale, facts, text });
const WORK = '/lu-legilux/loi-1997-07-27-n10';
const STATE = `${WORK}/2025-11-25--${'d'.repeat(64)}/fra`;
const TODAY = '2026-11-15';
const QUESTION = "Quel est le délai de prescription d'un contrat d'assurance ?";
const SENTENCE = "Le délai de prescription de toute action dérivant du contrat d'assurance est de trois ans.";
const TITLE = "Loi du 27 juillet 1997 sur le contrat d'assurance";
const PLANNER = 'azure-openai/gpt-4.1-mini/prompt-a625e51e3b58';

function trace(question, { read = true, query = "délai de prescription d'un contrat d'assurance" } = {}) {
  return {
    schema: 'lex-v3-answer-trace/1',
    question,
    answer_locale: 'fr',
    today: TODAY,
    planner_id: PLANNER,
    plan: {
      schema: 'lex-v3-assistant-plan/1',
      parts: [{ part_id: 'p1', proposed_verdict: 'answer', span: [0, 59] }],
      steps: [{ step_id: 's1', part_id: 'p1', operation_id: 'search', arguments: { language: { choice: 'fra' }, query: { span: [12, 58] } } }],
      follow_ups: [{ step_id: 'f1', after: 's1', condition: 'on_single_article', operation_id: 'evidence_bundle', arguments: { article: { pick: 'sole_article', result: 's1' } } }],
    },
    steps: [
      { step_id: 's1', operation_id: 'search', fired: true, outcome: 'result', parameters: { language: 'fra', limit: 10, query }, result_sha256: 'c'.repeat(64) },
      read
        ? { step_id: 'f1', operation_id: 'evidence_bundle', fired: true, outcome: 'result', parameters: { article: 'art_44', date: TODAY, identifier: WORK }, result_sha256: 'e'.repeat(64) }
        : { step_id: 'f1', operation_id: 'evidence_bundle', fired: false, outcome: 'not_fired', parameters: null },
    ],
  };
}

function dossier(verdict, blocks, question = QUESTION, quotes = 0, traced = {}) {
  return {
    schema: 'lex-v3-answer-dossier/1',
    verdict,
    answer_locale: 'fr',
    label: claim(`verdict.${verdict}`, 'D’après le texte officiel'),
    notices: [claim('notice.assembled_by_model', 'Un modèle d’IA a assemblé cette réponse.'), claim('notice.not_legal_advice', 'Lex montre le texte de la loi.')],
    plan: { rendered: [claim('plan.heading', 'Ce que Lex a fait de votre question')], plan_sha256: 'a'.repeat(64) },
    parts: [{ part_id: 'p1', span: [0, 59], class: 'lookup', cues: [], verdict, blocks }],
    gates: { quotes, disclosures: 0, non_template_strings: 0, disclosure_complete: true },
    trace: trace(question, traced),
  };
}

const disclosure = (flag, text) => ({ kind: 'disclosure', flag, object: 'q1', claim: claim(`disclosure.${flag}`, text) });

function answer() {
  return dossier('answer', [
    claim('law.quote_heading', `${WORK}, article 44, tel qu’il se lisait le 15 novembre 2026`, 'fr', [
      { kind: 'work_address', name: 'work', value: WORK }, { kind: 'anchor', name: 'anchor', value: 'art_44' }, { kind: 'calendar_date', name: 'date', value: TODAY },
    ]),
    {
      kind: 'quote', quote_id: 'q1', language: 'fra', anchor: 'art_44', work: WORK, from: '2025-11-25',
      text: `Article 44Délai de prescription1.${SENTENCE}Le délai court à partir du jour de l'événement qui donne ouverture à l'action.`,
      offset: 0, length: 200, article_text_sha256: 'b'.repeat(64), article_permalink: `${STATE}/art_44`, state_permalink: STATE,
      official_source: 'http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/consolide/20251125',
    },
    claim('law.content_hash', `Empreinte du contenu : ${'b'.repeat(64)}`),
    disclosure('authenticity', 'Ceci est la copie que Lex donne du texte, à titre d’information ; elle ne fait pas foi.'),
    disclosure('no_legal_effect', 'Un texte consolidé est un outil de documentation sans effet juridique.'),
    disclosure('validity_conflict', 'L’éditeur date cet article du 7 septembre 1997.'),
  ], QUESTION, 1);
}

const notFound = () => dossier('point', [
  claim('point.no_hit', 'Lex n’a trouvé aucun article avec vos mots.'),
  { kind: 'handoff', entry_id: 'legilux', role: 'official_source', link: 'https://legilux.public.lu/', reviewed_by_person: false, claim: claim('handoff.official_source', 'Source officielle : Legilux, https://legilux.public.lu/') },
], 'Combien coûte un vélo ?', 0, { read: false, query: 'coûte un vélo' });

const action = (identifier, article, date) => ({
  kind: 'action', operation_id: 'evidence_bundle', parameters: { article, date, identifier },
  claim: claim('action.read_article', `Lire l’article de ${identifier}`),
});

const clarify = () => dossier('clarify', [
  claim('clarify.which_act', 'Vos mots figurent dans plus d’un acte que Lex détient. De quel acte s’agit-il ?'),
  action('/lu-legilux/loi-2008-05-13-n1', 'art_1er', '2013-01-01'),
  action('/lu-legilux/loi-2018-02-03-a110', 'art_1er', '2018-03-01'),
  action('/lu-legilux/loi-2018-02-03-a110', 'art_1er', '2018-03-01'),
], 'Quel est le délai de préavis pour résilier un contrat d’assurance ?', 0, { read: false });

const advice = () => dossier('refuse', [
  { kind: 'refusal', code: 'advice_boundary', claim: claim('refusal.advice_boundary', 'Lex ne dit pas ce qui s’applique à votre situation.') },
], 'Dois-je payer ma prime ?', 0, { read: false });

/** An article's history as article_history answers it: ten versions held, the wording changed in one of them or none. */
function history(changedAt = -1) {
  const dates = ['2006-12-25', '2007-12-21', '2011-04-16', '2013-03-01', '2014-01-01', '2016-01-01', '2018-01-01', '2020-01-01', '2023-01-01', '2025-11-25'];
  return {
    states: dates.flatMap((date, at) => [
      { applicability_date: date, language: 'fra', text_status: 'held', articles: [{ publisher_id: 'art_44' }], wording_changed: at === changedAt },
      { applicability_date: date, language: 'deu', text_status: 'held', articles: [{ publisher_id: 'art_44' }], wording_changed: false },
    ]),
  };
}

const answerTurn = (extra = {}) => ({
  ...dockTurnOf({ state: 'success', envelope: { result: { object_type: 'answer_dossier', value: answer() }, context: { commits: { serving_runtime_source_commit: 'f'.repeat(40) } } } }, 'en'),
  question: QUESTION, seconds: 1.6, id: 't1', facts: { title: TITLE, titleLanguage: 'fra', heading: 'Délai de prescription' }, strip: articleStrip(history(), 'fra'),
  ...extra,
});

const turnOf = (value, question, id) => ({ ...dockTurnOf({ state: 'success', envelope: { result: { object_type: 'answer_dossier', value } } }, 'en'), question, seconds: 2.2, id });

// --- What a render shows ----------------------------------------------------------------------------------------------------

const decode = (text) => text.replaceAll('&#x27;', "'").replaceAll('&quot;', '"').replaceAll('&lt;', '<').replaceAll('&gt;', '>').replaceAll('&amp;', '&');

/** The text a render shows a reader: its text nodes and its read attributes, the plan as returned (behind a click) left out. */
function shown(markup) {
  const withoutPlan = markup.replace(/<pre\b[\s\S]*?<\/pre>/gu, '');
  const read = [...withoutPlan.matchAll(/\s(?:aria-label|placeholder|title)="([^"]*)"/gu)].map((match) => match[1]);
  return decode([withoutPlan.replace(/<[^>]*>/gu, ' '), ...read].join(' ')).replace(/\s+/gu, ' ');
}

const forms = (markup) => {
  for (const form of markup.match(/<form\b[^>]*>/gu) ?? []) assert.doesNotMatch(form, /\s(?:action|method)=/u, form);
  for (const control of markup.match(/<(?:input|select|textarea|button)\b[^>]*>/gu) ?? []) assert.doesNotMatch(control, /\sname=/u, control);
};

// --- The first screen -------------------------------------------------------------------------------------------------------

test("the dock's first screen, as the server renders it: the header, the intro, the three examples, what it does and does not do, the field", async () => {
  const { AssistantDock, liveDockTree } = await dock();
  for (const page of ['live-home', 'live-text', 'live-radar']) {
    const markup = renderToStaticMarkup(h(AssistantDock, { page }));
    assert.equal(markup, renderToStaticMarkup(liveDockTree(page)), `${page}: the tree the server renders is the one the browser hydrates`);
    const said = shown(markup);
    for (const key of ['heading', 'dockPreview', 'dockHide', 'dockIntro', 'dockExampleOne', 'dockExampleOneCaption', 'dockExampleTwo', 'dockExampleTwoCaption', 'dockExampleThree', 'dockExampleThreeCaption', 'dockDoes', 'dockDoesText', 'dockDoesNot', 'dockDoesNotText', 'dockHowLink', 'dockPlaceholder', 'dockAsk']) {
      assert.ok(said.includes(EN[key]), `${page}: ${key}`);
    }
    assert.ok(markup.includes('href="/en/evidence.html"'), 'how an answer is built, with the measured results');
    assert.ok(markup.includes('<label class="lx-visually-hidden" for="lx-dock-q">Your question</label>'), 'the field is labelled');
    assert.match(markup, /<span class="lx-dock-example-question" lang="fr">Quel est/u);
    assert.match(markup, /<span class="lx-dock-example-question" lang="en">What does Article 5/u);
    assert.ok(!markup.includes('data-lex-assistant-panel') && !markup.includes('assistant-question'), "the old panel's marker and field are not the dock's");
    assert.ok(!markup.includes('style='), 'no inline style (the Content-Security-Policy blocks them)');
    forms(markup);
  }
  // The notice before a question (R44), word for word, behind "preview, not legal advice".
  const markup = renderToStaticMarkup(h((await dock()).DockView, { page: 'live-home', locale: 'fr' }));
  for (const key of ['notice', 'preview', 'processor', 'dockIntro', 'dockExampleThree', 'dockAsk']) assert.ok(decode(markup).includes(FR[key]), `fr: ${key}`);
  assert.doesNotMatch(markup, /lang="en"/u, 'the French examples are all French');
});

test('the three examples are the brief\'s, checked live on 15/11/2026, and the French copy keeps its no-break spaces', () => {
  assert.equal(EN.dockExampleOne, "Quel est le délai de prescription d'un contrat d'assurance ?");
  assert.equal(EN.dockExampleTwo, "Loi du 27 juillet 1997 sur le contrat d'assurance, article 38, 1er juin 2012");
  assert.equal(EN.dockExampleThree, 'What does Article 5 of the GDPR say?');
  for (const [key, text] of Object.entries(FR).filter(([name]) => name.startsWith('dock'))) {
    assert.doesNotMatch(text, / [:;?!»€]|« /u, `fr.${key}`);
    assert.doesNotMatch(text, /'/u, `fr.${key}`);
  }
});

// --- An answer --------------------------------------------------------------------------------------------------------------

test('an answer: the question as a bubble, the verdict, the first sentence and […], the article by the text\'s title, the strip, three notes', async () => {
  const { DockView } = await dock();
  const markup = renderToStaticMarkup(h(DockView, { page: 'live-text', shown: answerTurn(), initialNotes: true }));
  const said = shown(markup);
  assert.match(markup, /<p class="lx-dock-bubble" lang="fr" data-dock-question="">Quel est le délai/u);
  assert.ok(said.includes(EN.dockVerdictToday));
  assert.ok(decode(markup).includes(`${QUOTE_OPEN}<mark class="lx-dock-mark">${SENTENCE}</mark> […]${QUOTE_CLOSE}`), 'the first sentence, then […]: the whole article is on the page');
  assert.ok(decode(markup).includes(`Article 44 of the <span lang="fr">${TITLE}</span>. ${EN.dockWholeOpen}`), markup);
  assert.equal((markup.match(/<rect class="lx-dock-tick"/gu) ?? []).length, 10, 'ten ticks, one per version held in the quote\'s language');
  assert.ok(said.includes('Same wording in all 10 versions held'));
  assert.ok(said.includes('3 notes'));
  assert.ok(said.includes('Un texte consolidé est un outil de documentation sans effet juridique.'), 'the notes are the answer\'s disclosures');
  // Never in the main flow (the brief's section 9): no address, no id, no hash, no ISO date.
  assert.doesNotMatch(said, /\/lu-legilux\/|art_44|[0-9a-f]{64}|\d{4}-\d{2}-\d{2}/u, said);
  // On another page the card links the text at the article, at the answer's date (today: none).
  const elsewhere = renderToStaticMarkup(h(DockView, { page: 'live-radar', shown: answerTurn() }));
  assert.ok(elsewhere.includes(`href="${textHref({ identifier: WORK, article: 'art_44' }, 'en').replaceAll('&', '&amp;')}"`), elsewhere);
  assert.ok(shown(elsewhere).includes(EN.dockWholeLink));
  // A wording that changed says when, dd/mm/yyyy, its tick in the time colour.
  const changed = renderToStaticMarkup(h(DockView, { page: 'live-text', shown: answerTurn({ strip: articleStrip(history(3), 'fra') }) }));
  assert.ok(shown(changed).includes('Wording changed on 01/03/2013'));
  assert.equal((changed.match(/lx-dock-tick-changed/gu) ?? []).length, 1);
});

test('how this answer was built: five stages from the trace, the AI model on the planner alone, time and cost, the plan exactly as returned', async () => {
  const { DockView } = await dock();
  const turn = answerTurn();
  const markup = renderToStaticMarkup(h(DockView, { page: 'live-text', shown: turn, initialPlan: true }));
  const rows = [...markup.matchAll(/<li class="lx-dock-row" data-stage="([a-z]+)"><span class="lx-dock-row-name">([^<]+)<\/span><span class="lx-dock-row-line">([^<]+)<\/span><span class="([^"]+)" data-stage-by="([a-z]+)">([^<]+)<\/span><\/li>/gu)]
    .map((match) => [match[1], match[2], decode(match[3]), match[5], match[6]]);
  assert.deepEqual(rows, [
    ['masking', 'Masking', 'nothing to mask', 'code', 'code'],
    ['planner', 'Planner', 'search, then read the article', 'model', 'AI model'],
    ['validator', 'Validator', 'plan accepted, nothing repaired', 'code', 'code'],
    ['execution', 'Execution', 'art. 44 read as of 15/11/2026', 'code', 'code'],
    ['composer', 'Composer', 'quote identical to the text', 'code', 'code'],
  ]);
  assert.equal((markup.match(/lx-dock-tag-model/gu) ?? []).length, 1, 'one AI model call');
  const cost = EVALUATION_RECORD.dev.costEur.toLocaleString('en-GB', { minimumFractionDigits: 3, maximumFractionDigits: 3 });
  assert.ok(markup.includes(`>1.6 s, about €${cost}<`), 'the time the answer took and the latest test run\'s average cost, said "about"');
  assert.ok(shown(markup).includes(`${EN.dockPlanNote} ${EN.dockHidePlan}`));
  const escape = (text) => text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#x27;');
  assert.ok(markup.includes(`<pre class="lx-dock-json" translate="no" data-dock-json="plan">${escape(JSON.stringify(turn.dossier.trace.plan, null, 2))}</pre>`), 'trace.plan exactly as returned');
  assert.ok(markup.includes(`<pre class="lx-dock-json" translate="no" data-dock-json="steps">${escape(JSON.stringify(turn.dossier.trace.steps, null, 2))}</pre>`), 'trace.steps exactly as returned');
  assert.ok(shown(markup).includes(`Engine: ${plannerName(PLANNER)}`), 'the engine by name');
  assert.ok(markup.includes('href="/en/evidence.html#registry"'), 'the MLflow registry line');
  // Closed, the plan is one action away and not in the page.
  const closed = renderToStaticMarkup(h(DockView, { page: 'live-text', shown: turn }));
  assert.match(closed, /<div class="lx-dock-plan" id="lx-dock-t1-plan" hidden="" data-dock-plan=""><\/div>/u);
  assert.match(closed, /aria-expanded="false" aria-controls="lx-dock-t1-plan" data-dock-see-plan="">See the plan</u);
  // In French, each stage in French; the time and cost as French writes them.
  const french = stageRows(turn.dossier, 'fr').map((row) => row.line);
  assert.deepEqual(french, ['rien à masquer', 'chercher, puis lire l’article', 'plan accepté, rien de réparé', 'art. 44 lu au 15/11/2026', 'citation identique au texte']);
  assert.equal(builtFoot(1.6, 0.0010916, 'fr'), '1,6 s, environ 0,001 €');
  assert.equal(builtFoot(null, 0.0010916, 'en'), 'about €0.001');
});

// --- The states of section 6 ------------------------------------------------------------------------------------------------

test('not found: the sentence, the words searched, the same five stages, "Find a text by name", and "Find in this law" on a text page only', async () => {
  const { DockView } = await dock();
  const turn = turnOf(notFound(), 'Combien coûte un vélo ?', 't2');
  assert.equal(turn.kind, 'notfound');
  const markup = renderToStaticMarkup(h(DockView, { page: 'live-text', shown: turn }));
  const said = shown(markup);
  assert.ok(said.includes(EN.dockNotFound));
  assert.ok(decode(markup).includes('Words searched: “<span lang="fr">coûte un vélo</span>”'), said);
  assert.equal((markup.match(/<li class="lx-dock-row"/gu) ?? []).length, 5);
  assert.ok(said.includes('search run, no single article to read') && said.includes('nothing quoted, every sentence from a template'));
  assert.ok(said.includes(EN.dockFindByName) && said.includes(EN.dockFindInLaw));
  assert.ok(!shown(renderToStaticMarkup(h(DockView, { page: 'live-home', shown: turn }))).includes(EN.dockFindInLaw), 'no law is open on Home');
  forms(markup);
});

test('clarify: each candidate a button with the text\'s title and the article\'s heading, never an address', async () => {
  const { DockView } = await dock();
  const turn = turnOf(clarify(), 'Quel est le délai de préavis ?', 't3');
  assert.equal(turn.kind, 'clarify');
  assert.equal(clarifyCandidates(turn.dossier).length, 2, 'each candidate once');
  const before = shown(renderToStaticMarkup(h(DockView, { page: 'live-home', shown: turn })));
  assert.ok(before.includes(EN.dockClarify) && before.includes(EN.dockChoicesPending) && before.includes('Article 1er'));
  const choices = [{ title: 'Code du travail', titleLanguage: 'fra', heading: 'Champ d’application' }, { title: 'Loi du 3 février 2018 portant sur le bail commercial', titleLanguage: 'fra', heading: null }];
  const markup = renderToStaticMarkup(h(DockView, { page: 'live-home', shown: { ...turn, choices } }));
  const said = shown(markup);
  assert.ok(said.includes('Article 1er, Champ d’application') && said.includes('Code du travail'));
  assert.ok(said.includes('Article 1er Loi du 3 février 2018 portant sur le bail commercial'), said);
  assert.equal((markup.match(/class="lx-dock-choice"/gu) ?? []).length, 2);
  assert.doesNotMatch(said, /\/lu-legilux\/|art_1er/u, 'never an address such as /lu-legilux/loi-1997-07-27-n10');
});

test('the advice boundary: one sentence and a reworded question it can answer', async () => {
  const { DockView } = await dock();
  const turn = turnOf(advice(), 'Dois-je payer ma prime ?', 't4');
  assert.equal(turn.kind, 'advice');
  const said = shown(renderToStaticMarkup(h(DockView, { page: 'live-home', shown: turn })));
  assert.ok(said.includes(EN.dockAdvice) && said.includes(`${EN.dockAdviceExample} ${EN.dockExampleOne}`), said);
  // Advice also arrives as the refusal advice_boundary carrying its answer.
  assert.equal(dockTurnOf({ state: 'refusal', envelope: { refusal: { code: 'advice_boundary', helpful_payload: { answer_dossier: advice() } } } }, 'en').kind, 'advice');
});

test('no answer: switched off is told from a question it could not answer (planner, plan, cap), the limit, and a failure with "Try again"', async () => {
  const { DockView } = await dock();
  const card = samples.envelopes.find((entry) => entry.operation === 'ask').envelope;
  assert.equal(card.result.value.presentation_result, 'assistant_v3_unavailable');
  assert.equal(dockTurnOf({ state: 'success', envelope: card }, 'en').kind, card.result.value.question_read === true ? 'couldnot' : 'off');
  const read = { ...card, result: { ...card.result, value: { ...card.result.value, question_read: true } } };
  const off = { ...card, result: { ...card.result, value: { ...card.result.value, question_read: false } } };
  assert.equal(dockTurnOf({ state: 'success', envelope: read }, 'en').kind, 'couldnot');
  assert.equal(dockTurnOf({ state: 'success', envelope: off }, 'en').kind, 'off');
  assert.equal(dockTurnOf({ state: 'refusal', envelope: { refusal: { code: 'rate_limited', helpful_payload: {} } } }, 'en').kind, 'rate');
  assert.equal(dockTurnOf({ state: 'transport_failure', code: 'network_error' }, 'en').kind, 'failure');
  assert.equal(dockTurnOf({ state: 'invalid_envelope', reason: 'x' }, 'en').kind, 'failure');
  const said = (kind) => shown(renderToStaticMarkup(h(DockView, { page: 'live-text', shown: { kind, question: 'Q', id: kind } })));
  assert.ok(said('off').includes(EN.dockOff) && said('off').includes('they use no model'));
  assert.ok(said('couldnot').includes('this month’s spending cap is reached'));
  assert.ok(said('rate').includes(EN.rateLimited) && said('rate').includes(EN.dockTryAgain));
  assert.ok(said('failure').includes(EN.dockFailure) && said('failure').includes(EN.dockTryAgain));
  assert.ok(!said('off').includes(EN.dockTryAgain));
});

test('loading, the conversation kept on another page, the tab it folds to', async () => {
  const { DockView } = await dock();
  const loading = renderToStaticMarkup(h(DockView, { page: 'live-home', shown: { id: 'pending', kind: 'pending', question: 'Q', phase: 'ask' } }));
  assert.ok(shown(loading).includes(EN.dockLoadingAsk) && loading.includes('aria-busy="true"') && loading.includes('lx-dock-skeleton'));
  const kept = renderToStaticMarkup(h(DockView, { page: 'live-radar', kept: answerTurn() }));
  assert.ok(shown(kept).includes(`${EN.dockStays} ${QUESTION} Answered from article 44. Open the answer again`), shown(kept));
  assert.ok(kept.includes(`placeholder="${EN.dockPlaceholderText}"`));
  assert.ok(shown(renderToStaticMarkup(h(DockView, { page: 'live-home', kept: answerTurn() }))).includes(EN.dockIntro), 'Home keeps its first screen');
  const folded = renderToStaticMarkup(h(DockView, { page: 'live-text', collapsed: true }));
  assert.ok(folded.includes('data-dock-tab') && shown(folded).includes(EN.dockOpen) && !folded.includes('lx-dock-q'));
  const opened = renderToStaticMarkup(h(DockView, { page: 'live-text', shown: { kind: 'opened', question: EN.dockExampleTwo, id: 't5', opening: { identifier: WORK, article: 'art_38', date: '2012-06-01', title: TITLE, titleLanguage: 'fra' } } }));
  assert.ok(decode(opened).includes(`Article 38 of the <span lang="fr">${TITLE}</span>, as it read on 01/06/2012. ${EN.dockWholeOpen}`), opened);
  assert.ok(shown(opened).includes(EN.dockOpenedLabel));
  assert.ok(!opened.includes('lx-dock-built'), 'no model call: no stages to show');
});

// --- The search-first rule --------------------------------------------------------------------------------------------------

const sample = (operation, scenario) => structuredClone(samples.envelopes.find((entry) => entry.operation === operation && scenario.test(entry.scenario)).envelope);

function fakeApi(answers) {
  const requests = [];
  const fetchImpl = async (url, init) => {
    requests.push({ url: String(url), init, body: JSON.parse(init.body) });
    const next = answers[String(url)].shift();
    return new Response(JSON.stringify(next), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  return { requests, fetchImpl };
}

const OPENS = {
  phase: 'exact_reference', outcome: 'opens', kind: 'citation', reason: null,
  opens: { address: `${WORK}/2011-04-16--${'f'.repeat(64)}/fra/art_38`, address_kind: 'permalink', article_id: 'art_38', language: 'fra', page: 'reading', publisher: 'lu-legilux', state_date: '2011-04-16', work: 'loi-1997-07-27-n10' },
  recognised: { act: { title: 'Version consolidée applicable au 01/01/2016 : Loi du 27 juillet 1997 sur le contrat d\'assurance.', title_language: 'fra' }, date: { date: '2012-06-01' } },
};

test('the search-first rule: a citation opens by code with no model call; any other sentence goes to ask, the words in the body alone', async () => {
  const { runQuestion, DOCK_SEARCH_MODE } = await dock();
  assert.equal(DOCK_SEARCH_MODE, SEARCH_MODE, "the search page's strict lane");
  const words = EN.dockExampleTwo;
  // A citation: search reads it as an exact reference that opens; ask is never called.
  const opened = sample('search', /the phrase as the site asks it, the strict lane alone/u);
  opened.result.value.reference = OPENS;
  const refused = sample('search', /a language the mount holds no text in/u);
  let api = fakeApi({ '/api/v3/search': [refused, opened] });
  const turn = await runQuestion(words, 'en', api.fetchImpl);
  assert.equal(turn.kind, 'opened');
  assert.deepEqual({ ...turn.opening }, { identifier: WORK, date: '2012-06-01', article: 'art_38', title: TITLE, titleLanguage: 'fra', language: 'fra' });
  assert.deepEqual(api.requests.map((request) => [request.url, request.body.parameters.language]), [['/api/v3/search', 'eng'], ['/api/v3/search', 'fra']], 'asked again once, in the language the act is held in');
  for (const request of api.requests) {
    assert.equal(request.init.method, 'POST');
    assert.equal(request.body.parameters.query, words, 'the words in the body');
    assert.equal(request.body.parameters.mode, 'strict');
    assert.ok(!request.url.includes('?') && !request.url.includes('#'));
  }
  // A question: search reads no reference, so ask answers; the time it took is measured.
  const none = sample('search', /the phrase as the site asks it, the strict lane alone/u);
  none.result.value.reference = { phase: 'exact_reference', outcome: 'no_reference', opens: null, reason: null };
  const asked = sample('ask', /contained assistant/u);
  asked.result = { ...asked.result, object_type: 'answer_dossier', value: answer() };
  asked.verdict = 'answer';
  api = fakeApi({ '/api/v3/search': [none], '/api/v3/ask': [asked] });
  const answered = await runQuestion(QUESTION, 'en', api.fetchImpl);
  assert.deepEqual(api.requests.map((request) => request.url), ['/api/v3/search', '/api/v3/ask']);
  assert.deepEqual(api.requests[1].body, { operation_id: 'ask', parameters: { question: QUESTION } }, 'the question alone (R45(e))');
  assert.equal(answered.kind, 'answer', JSON.stringify(answered).slice(0, 300));
  assert.equal(typeof answered.seconds, 'number');
  // The answer's text opens at its article, at its date (today: none), as the text page's address names it.
  const { answerOpening } = await dock();
  assert.deepEqual({ ...answerOpening(answered, 'en') }, { identifier: WORK, article: 'art_44', date: null, language: 'fra' });
});

// --- The readings behind the card -------------------------------------------------------------------------------------------

test('the readings: dd/mm/yyyy, the first sentence, the masked runs, an exact reference, a version\'s own title, the strip', () => {
  assert.equal(numericDate('2026-11-15'), '15/11/2026');
  assert.deepEqual({ ...firstSentenceOf(`Article 44Délai de prescription1.${SENTENCE}Le délai court.`) }, { sentence: SENTENCE, more: true });
  assert.equal(firstSentenceOf('Article 7')?.sentence ?? null, null);
  assert.equal(maskedCount(QUESTION), 0);
  assert.equal(maskedCount('écrivez à jean@example.lu ou au +352 621 123 456, IBAN LU28 0019 4006 4475 0000'), 3);
  assert.equal(referenceOpening({ reference: { phase: 'exact_reference', outcome: 'no_reference', opens: null } }), null);
  assert.equal(referenceOpening({ reference: OPENS }).title, TITLE, "the version's prefix and the final stop taken off");
  const lu = {
    states: [{ expression_iri: 'x', language: 'fra', articles: [{ publisher_id: 'art_1', text: 'Art. 1er.DéfinitionsAu sens de la présente loi.' }, { publisher_id: 'art_44', text: `Article 44Délai de prescription1.${SENTENCE}` }] }],
    citation_facts: { titles: [{ expression_iri: 'x', language: 'fra', titles: [{ title: `Version consolidée applicable au 25/11/2025 : ${TITLE}.` }] }] },
  };
  assert.deepEqual({ ...bundleFacts(lu, 'art_44', 'fra') }, { title: TITLE, titleLanguage: 'fra', heading: 'Délai de prescription' }, 'the article asked, not the first of its page');
  const eu = {
    wordings: [{ language: 'eng', articles: [{ publisher_id: '005', text: 'Article 5Principles relating to processing of personal data1.Personal data shall be:' }] }],
    titles: { entries: [{ kind: 'title', language: 'eng', source: 'cellar_expression_title', text: 'Regulation (EU) 2016/679 (General Data Protection Regulation) (Text with EEA relevance)' }] },
  };
  assert.equal(bundleFacts(eu, '005', 'eng').title, 'Regulation (EU) 2016/679 (General Data Protection Regulation)');
  const strip = articleStrip({ states: [...history().states, { applicability_date: '2026-01-01', language: 'fra', text_status: 'not_held', articles: [] }] }, 'fra');
  assert.equal(strip.ticks.length, 10, 'the versions held that hold the article, in its language');
  assert.equal(stripSentence(strip, 'en'), 'Same wording in all 10 versions held');
  assert.equal(stripSentence(articleStrip({ states: [history().states[0]] }, 'fra'), 'fr'), 'Une seule version détenue');
});

// --- Every word from the copy tables ----------------------------------------------------------------------------------------

test('every word the dock shows comes from its copy tables or from the answer (the pseudo-locale scan)', async () => {
  const table = fileURLToPath(new URL('scripts/assistant-panel.mjs', web));
  const line = 'export const ASSISTANT_PANEL_COPY = /* @__PURE__ */ Object.freeze({ en: PANEL_EN, fr: PANEL_FR });';
  const pseudo = `
const __pseudoText = (text) => text.split(/(\\{[A-Za-z]+\\})/).map((part, index) => (index % 2 === 1 ? part
  : part.replace(/[A-Za-z]/g, (letter) => String.fromCodePoint(letter.codePointAt(0) + 0xfee0)))).join('');
const __pseudo = (node) => Object.freeze(Object.fromEntries(Object.entries(node).map(([key, value]) => [key, __pseudoText(value)])));
export const ASSISTANT_PANEL_COPY = Object.freeze({ en: __pseudo(PANEL_EN), fr: PANEL_FR });`;
  const out = fileURLToPath(new URL('.react-build/assistant-dock-pseudo.mjs', web));
  await build({
    entryPoints: [fileURLToPath(new URL('app/AssistantDock.jsx', web))], outfile: out, bundle: true, format: 'esm', platform: 'node',
    jsx: 'automatic', packages: 'external', logLevel: 'silent',
    plugins: [{
      name: 'pseudo-panel',
      setup(builder) {
        builder.onLoad({ filter: /assistant-panel\.mjs$/ }, (args) => {
          const source = readFileSync(args.path, 'utf8');
          assert.equal(args.path, table);
          assert.equal(source.split(line).length, 2, 'the tables are swapped on their one line');
          return { contents: source.replace(line, pseudo), loader: 'js' };
        });
      },
    }],
  });
  const { DockView } = await import(`${pathToFileURL(out).href}?${Date.now()}`);
  const data = [QUESTION, SENTENCE, TITLE, 'coûte un vélo', 'Combien coûte un vélo ?', 'Code du travail', 'Champ d’application', 'Q', plannerName(PLANNER), REGISTRY_RECORD.model,
    'Ceci est la copie que Lex donne du texte, à titre d’information ; elle ne fait pas foi.', 'Un texte consolidé est un outil de documentation sans effet juridique.', 'L’éditeur date cet article du 7 septembre 1997.',
    'Quel est le délai de préavis ?', 'Dois-je payer ma prime ?', '1er'];
  const renders = {
    home: { page: 'live-home' },
    answer: { page: 'live-text', shown: answerTurn(), initialNotes: true },
    elsewhere: { page: 'live-radar', shown: answerTurn({ strip: articleStrip(history(3), 'fra') }) },
    notFound: { page: 'live-text', shown: turnOf(notFound(), 'Combien coûte un vélo ?', 't2') },
    clarify: { page: 'live-home', shown: { ...turnOf(clarify(), 'Quel est le délai de préavis ?', 't3'), choices: [{ title: 'Code du travail', titleLanguage: 'fra', heading: 'Champ d’application' }, null] } },
    advice: { page: 'live-home', shown: turnOf(advice(), 'Dois-je payer ma prime ?', 't4') },
    off: { page: 'live-text', shown: { kind: 'off', question: 'Q', id: 'o' } },
    couldnot: { page: 'live-text', shown: { kind: 'couldnot', question: 'Q', id: 'c' } },
    rate: { page: 'live-text', shown: { kind: 'rate', question: 'Q', id: 'r' } },
    failure: { page: 'live-text', shown: { kind: 'failure', question: 'Q', id: 'f' } },
    loading: { page: 'live-text', shown: { kind: 'pending', question: 'Q', id: 'pending', phase: 'search' } },
    kept: { page: 'live-radar', kept: answerTurn() },
    opened: { page: 'live-text', shown: { kind: 'opened', question: 'Q', id: 'p', opening: { identifier: WORK, article: 'art_38', date: '2012-06-01', title: TITLE, titleLanguage: 'fra' } } },
    folded: { page: 'live-text', collapsed: true },
  };
  for (const [name, props] of Object.entries(renders)) {
    let said = shown(renderToStaticMarkup(h(DockView, { locale: 'en', ...props })));
    // The example questions are the table's too, and come out swapped; what is left is the answer's own data.
    for (const value of data.sort((a, b) => b.length - a.length)) said = said.replaceAll(value, ' ');
    assert.doesNotMatch(said, /[A-Za-z]{2,}/u, `${name}: ${said.match(/[^\s]*[A-Za-z]{2,}[^\s]*/u)?.[0]} is shown and is not from the table`);
  }
});

// --- In the shell ------------------------------------------------------------------------------------------------------------

test("in the shell's dock slot: the dock's first screen inside #lx-dock-root, the dock's script beside it, the same tree the entry hydrates", async () => {
  const { Document } = await import(pathToFileURL(await bundle('app/Document.jsx', 'assistant-dock-shell-test.mjs')).href);
  const { liveDockTree, LIVE_DOCK_ROOT } = await dock();
  for (const state of ['live-home', 'live-text', 'live-radar']) {
    const html = renderToStaticMarkup(h(Document, { state, title: 'Lex', banner: 'live', layout: 'v4', dock: true }, h('p', null, 'page')));
    const slot = html.indexOf(`<div id="${LIVE_DOCK_ROOT}" class="lx-dock-root" data-dock-page="${state}">`);
    assert.ok(slot > 0, `${state}: the dock's root`);
    assert.ok(html.includes(`<div id="${LIVE_DOCK_ROOT}" class="lx-dock-root" data-dock-page="${state}">${renderToStaticMarkup(liveDockTree(state))}</div>`), `${state}: the tree the entry hydrates`);
    assert.ok(html.includes('src="/en/client-live-dock.js"'));
    assert.ok(html.slice(slot).includes('id="lx-dock-q"') && !html.includes('style='), `${state}: the field, and no inline style`);
    forms(html.slice(slot, html.indexOf('</aside>', slot)));
  }
});
