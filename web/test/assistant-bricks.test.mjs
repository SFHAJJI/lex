// The dock's reading of a bricks answer (V4, plan /2), held to answers the engine itself made: `bricks-answers-record.mjs` holds
// nine dossiers the pipeline answered over the test corpus of the law of 27 July 1997 on the insurance contract, on
// 18/11/2026 (a definition, a rule and its cases, an absence shown by its sweep, two answers by scope, a refusal with the
// law beside it, a greeting; then three answers of operations: a cited article of a law the index names, opened by code;
// a cited article a scripted model described; and a question code searched alone). A .NET test (BricksTests) fails when the file is not what the engine answers. What is checked: every word drawn is the server's sentence or the law's own; one line
// of the law is quoted, not the whole article; the stages are the bricks', tagged "AI model" only when a call is recorded.

import assert from 'node:assert/strict';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { ASSISTANT_PANEL_COPY, actTitleAt, aroundAddress, bricksBody, bricksOf, bricksWork, builtNote, dockTurnOf, linesBeside, modelWasAsked, questionCallOf, quoteLines, stageRows, validateDossier, workTitleToRead } from '../scripts/assistant-panel.mjs';
import { BRICKS_ANSWERS } from '../scripts/bricks-answers-record.mjs';
import { bundle } from '../scripts/react-build.mjs';

const answers = structuredClone(BRICKS_ANSWERS);
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
  const operations = ['searched', 'described', 'cited'];
  for (const [name, dossier] of Object.entries(answers)) {
    assert.doesNotThrow(() => validateDossier(dossier), name);
    assert.equal(dossier.trace.plan.bricks.version, 'lex-v4-bricks/1', name);
    assert.equal(dossier.trace.plan.schema, 'lex-v3-assistant-plan/2', name);
    // An answer of the tables has the bricks' layout; an answer of operations reads as every answer of operations does.
    assert.equal(bricksOf(dossier) === null, operations.includes(name), name);
    assert.equal(questionCallOf(dossier) !== null, operations.includes(name), name);
  }
  assert.equal(bricksOf({ trace: { plan: { schema: 'lex-v3-assistant-plan/1' } } }), null, 'an answer the planner model planned has none');
  assert.equal(questionCallOf({ trace: { plan: { schema: 'lex-v3-assistant-plan/1' } } }), null);
});

test('an answer of operations says who read the question: a model described it, or code searched the words alone', async () => {
  assert.deepEqual(questionCallOf(answers.described), { outcome: 'described', asks: ['article_or_act_cited'] });
  assert.deepEqual(questionCallOf(answers.searched), { outcome: 'not_wired', asks: [] });
  assert.equal(dockTurnOf(asked(answers.described), 'fr').kind, 'answer', 'the cited article is quoted');
  assert.equal(dockTurnOf(asked(answers.searched), 'fr').kind, 'notfound', 'a search that finds nothing is the card of a search');

  const described = stageRows(answers.described, 'fr');
  assert.deepEqual(described.map((row) => row.key), ['masking', 'question', 'route', 'validator', 'execution', 'composer']);
  assert.deepEqual(described.filter((row) => row.model).map((row) => row.key), ['question'], 'the one model step, and it is not a planner');
  assert.equal(described[1].line, 'décrite par un modèle d’IA : un article ou un acte est cité');
  assert.equal(described[2].line, 'chercher, puis lire l’article');
  assert.equal(builtNote(answers.described, described, 'fr'), FR.dockDescribedNote);
  assert.equal(builtNote(answers.described, described, 'en'), EN.dockDescribedNote);

  const searched = stageRows(answers.searched, 'en');
  assert.ok(searched.every((row) => row.model === false), 'no model took part');
  assert.equal(searched[1].line, EN.dockQuestionByCode);
  assert.equal(builtNote(answers.searched, searched, 'en'), EN.dockBricksNoModel);
  // A cited article of a law Lex knows by name: code read the act and the article, and no model was asked.
  assert.deepEqual(questionCallOf(answers.cited), { outcome: 'cited', asks: [] });
  assert.equal(dockTurnOf(asked(answers.cited), 'fr').kind, 'answer');
  const cited = stageRows(answers.cited, 'fr');
  assert.ok(cited.every((row) => row.model === false));
  assert.equal(cited[1].line, FR.dockQuestionCited);
  assert.equal(cited[2].line, 'lire l’article');
  assert.equal(builtNote(answers.cited, cited, 'fr'), FR.dockBricksNoModel);
  const failed = structuredClone(answers.searched);
  failed.trace.plan.bricks.question_call = 'failed';
  assert.equal(builtNote(failed, stageRows(failed, 'en'), 'en'), EN.dockNoModelUsed, 'a model that was asked and gave nothing is not "no model was asked"');

  // A tables answer whose only model step is the question call says the model described, not that it ranked passages.
  const reached = structuredClone(answers.rule);
  reached.trace.plan.bricks.model_calls = [{ step: 'question' }];
  assert.equal(builtNote(reached, stageRows(reached, 'en'), 'en'), EN.dockDescribedNote);
  assert.equal(builtNote(answers.rule, stageRows(answers.rule, 'en'), 'en'), EN.dockBricksNoModel);
  // The cost a card prints is a model's: an answer of code alone has none, one a model described or was asked for has.
  assert.equal(modelWasAsked(answers.rule, stageRows(answers.rule, 'en')), false);
  assert.equal(modelWasAsked(answers.cited, stageRows(answers.cited, 'en')), false);
  assert.equal(modelWasAsked(answers.described, stageRows(answers.described, 'en')), true);
  assert.equal(modelWasAsked(failed, stageRows(failed, 'en')), true, 'a model asked that gave nothing was still asked');

  const { DockView } = await dock();
  const turn = { id: 't3', kind: 'answer', question: "Que dit l'article 39 de la loi sur le contrat d'assurance ?", dossier: answers.described, servingCommit: null, seconds: 1.2 };
  const markup = renderToStaticMarkup(h(DockView, { page: 'live-home', locale: 'fr', shown: turn }));
  assert.ok(markup.includes(FR.dockDescribedNote), 'the work says what the model did');
  assert.ok(!markup.includes(FR.dockPlanNote), 'and never that a model planned');
  assert.match(markup, /data-stage="question"[\s\S]*?data-stage-by="model"/u);
  assert.match(markup, /data-stage="route"[\s\S]*?data-stage-by="code"/u);
  assert.ok(!markup.includes('data-stage="planner"'), 'no planner stage');
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
  // The extraction's line counts the lines the model pointed at; a line code added to them is not the model's.
  assert.equal(called.find((row) => row.key === 'extraction').line, 'lines pointed at: 5, each checked in the text');
  withModel.trace.plan.bricks.answer.items[3].by = 'code';
  withModel.trace.plan.bricks.answer.items[4].by = 'code';
  assert.equal(stageRows(withModel, 'en').find((row) => row.key === 'extraction').line, 'lines pointed at: 3, each checked in the text');
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

  // The absence's sentence names the act it read by its address. The card never shows the address (2026-11-19: no raw
  // identifier on a page): "this act", in the answer's language, until the act's title is read, then the title.
  // (Release 16's candidate: the title was never asked for, the card being of the kind `said`, not `answer`.)
  const address = bricksWork(answers.absence);
  assert.equal(workTitleToRead(said, null), address, 'a card that quotes nothing asks for the title of the act it read');
  assert.equal(workTitleToRead({ ...said, workTitle: null }, null), null, 'asked once: a title that could not be read is not asked again');
  assert.equal(workTitleToRead({ ...said, kind: 'advice' }, null), null);
  assert.equal(workTitleToRead(said, { identifier: address, article: 'art_1er' }), null, 'an answer that opens a text names it by its quotation');
  assert.ok(!absence.includes(address), 'with no title read, the address is not in sight');
  assert.match(absence, /Lex a parcouru le texte de cet acte \(lignes/u, 'the act is "cet acte" until its title is read');
  const unread = renderToStaticMarkup(h(DockView, { page: 'live-home', locale: 'en', shown: { ...said, workTitle: null } }));
  assert.ok(!unread.includes(address) && unread.includes('le texte de cet acte'), 'a title that could not be read: still no address, and the answer\'s language (French), not the page\'s');
  const titled = renderToStaticMarkup(h(DockView, { page: 'live-home', locale: 'fr', shown: { ...said, workTitle: { title: 'Loi du 27 juillet 1997 sur le contrat d\'assurance', language: 'fra' } } }));
  assert.ok(!titled.includes(address), 'the address is no longer in sight');
  assert.match(titled, /Lex a parcouru le texte de «[^<]*<span lang="fr">Loi du 27 juillet 1997 sur le contrat d&#x27;assurance<\/span>/u);

  // A refusal shows the law's lines its answer holds beside it (the plan's dispatcher, route 1: "Lex's refusal with the law
  // shown beside it"): the lines alone, each with its article and a way to it, none marked as an answer to the reader's case.
  // A refusal that holds lines of the law is what Lex's own operations and the plans made before 2026-11-19 give; since
  // that day the bricks quote the general rule as an answer (`general`) and refuse only with no line found (`refusal`).
  const withLines = { ...structuredClone(answers.general), verdict: 'refuse' };
  assert.equal(linesBeside(answers.refusal).length, 0, 'the engine\'s refusal of today holds no line');
  const beside = linesBeside(withLines);
  assert.ok(beside.length > 0, 'the engine\'s refusal holds lines of the law');
  const refused = dockTurnOf(asked(withLines), 'fr');
  assert.equal(refused.kind, 'advice');
  const card = renderToStaticMarkup(h(DockView, { page: 'live-home', locale: 'fr', shown: { ...refused, id: 't3', question: 'Mon assureur peut-il résilier mon contrat ?' } }));
  assert.match(card, /data-dock-card="advice"/u);
  assert.ok(card.includes(FR.dockAdvice), 'the refusal\'s own sentence first');
  assert.ok(card.indexOf(FR.dockAdvice) < card.indexOf(FR.dockAdviceLaw) && card.indexOf(FR.dockAdviceLaw) < card.indexOf(FR.dockAdviceExample), 'then the lines, then a question it can answer');
  for (const entry of beside) assert.ok(card.includes(`data-dock-extract="${entry.quote.anchor}"`), entry.quote.anchor);
  assert.ok(!/data-dock-beside[\s\S]*lx-dock-mark[\s\S]*data-dock-built/u.test(card), 'no line is marked as the answer');
  assert.ok(!card.includes(FR.dockWholeOpen), 'no text was opened for a refusal: the card does not say one is');
  // A refusal that holds no line (the operations path, a plan /1) keeps its card as it was.
  assert.equal(linesBeside(answers.described).length, 0);
  assert.equal(linesBeside(null).length, 0);
});

test('the dock\'s new words are in both copy tables', () => {
  assert.ok(typeof EN.dockAdviceLaw === 'string' && typeof FR.dockAdviceLaw === 'string' && EN.dockAdviceLaw !== FR.dockAdviceLaw);
  for (const key of Object.keys(EN).filter((name) => /^dock(Stage(Question|Route|Retrieval|Arbiter|Extraction|Checks)|Question|Kind|Route|Retrieval|Arbiter|Extraction|Checks|Bricks|Said|Asks|Described|NoModel)/u.test(name))) {
    assert.equal(typeof FR[key], 'string', key);
    assert.ok(FR[key].length > 0 && EN[key].length > 0, key);
  }
});

test('an answer that names the act it read by its address: the act\'s title is read from its dossier, with the browser\'s own fetch in a page', async () => {
  // The absence of the engine's own samples names its act by its address, in the server's sentence.
  const absence = answers.absence;
  const address = bricksWork(absence);
  assert.equal(address, '/lu-legilux/loi-1997-07-27-n10');
  const line = bricksBody(absence).find((entry) => entry.type === 'line' && entry.text.includes(address));
  assert.ok(line, 'a sentence of the absence names the act by its address');
  const around = aroundAddress(line.text, address);
  assert.equal(around[0] + address + around[1], line.text);
  assert.equal(aroundAddress('no address here', address), null);
  assert.equal(aroundAddress(line.text, null), null);
  assert.equal(bricksWork({ trace: { plan: { bricks: { work: '/eu-eurlex/32016R0679' } } } }), null, 'another publisher\'s act keeps its address');
  assert.equal(bricksWork({ trace: { plan: {} } }), null);

  // The title is the act's own, read from its dossier; a dossier that cannot be read gives none (the card says "this act").
  const asked = [];
  const none = await actTitleAt(address, async (url, init) => { asked.push([url, JSON.parse(init.body)]); throw new Error('offline'); });
  assert.equal(none, null);
  assert.deepEqual(asked, [['/api/v3/dossier', { operation_id: 'dossier', parameters: { identifier: address } }]]);

  // In a page the dock hands no fetch: the request goes out with the browser's own. (Until 2026-11-19 it called
  // `undefined`, read no title, and the live card kept the address in sight: Release 18's shot fr-10.)
  const browsers = globalThis.fetch;
  const sent = [];
  globalThis.fetch = async (url, init) => { sent.push([url, JSON.parse(init.body)]); throw new Error('offline'); };
  try {
    assert.equal(await actTitleAt(address, undefined), null);
  } finally {
    globalThis.fetch = browsers;
  }
  assert.deepEqual(sent, [['/api/v3/dossier', { operation_id: 'dossier', parameters: { identifier: address } }]], 'asked with the browser\'s own fetch');
});

test('the general rule behind a personal wording is drawn as an answer, with the line that says the text applies nothing to the reader', async () => {
  const { DockView } = await dock();
  const turn = { ...dockTurnOf(asked(answers.general), 'fr'), id: 't9', question: 'Mon assureur peut-il résilier mon contrat ?' };
  assert.equal(turn.kind, 'answer', 'an answer: the rule is quoted, nothing is refused');
  const lines = bricksBody(answers.general);
  assert.ok(lines.some((entry) => entry.type === 'quote'), 'the rule, quoted');
  const caveat = lines.find((entry) => entry.type === 'remark' && entry.text === 'Le texte ne dit pas comment il s’applique à votre situation.');
  assert.ok(caveat, 'the server\'s own sentence on the reader\'s situation, among the remarks');
  const markup = renderToStaticMarkup(h(DockView, { page: 'live-home', locale: 'fr', shown: turn }));
  assert.ok(markup.includes('Le texte ne dit pas comment il s’applique à votre situation.'));
  assert.ok(!markup.includes('data-dock-card="advice"'), 'not the refusal\'s card');
});

test('the work says that a model named the articles to read when the tables of contents were read', () => {
  const routed = structuredClone(answers.rule);
  routed.trace.plan.bricks.model_calls = [{ step: 'outline' }, { step: 'extraction' }];
  const rows = stageRows(routed, 'en');
  const outline = rows.find((row) => row.key === 'outline');
  assert.deepEqual([outline?.name, outline?.line, outline?.model], [EN.dockStageOutline, EN.dockOutlineLine, true]);
  assert.ok(rows.findIndex((row) => row.key === 'outline') < rows.findIndex((row) => row.key === 'extraction'), 'in the order the steps ran');
  assert.equal(stageRows(routed, 'fr').find((row) => row.key === 'outline').name, FR.dockStageOutline);
  assert.ok(!stageRows(answers.rule, 'en').some((row) => row.key === 'outline'), 'no row when the step did not run');
  assert.equal(modelWasAsked(routed, rows), true);
});
