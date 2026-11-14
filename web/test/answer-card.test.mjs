// The answer card (the owner's order of 2026-11-13, section C), held to a drafted answer shaped as the live one for the
// insurance prescription question (art. 44 of loi-1997-07-27-n10): the verdict in a word, "Article 44 : Délai de
// prescription", the act's own title from its dossier (never its address), the quotation, "Version applicable depuis le …",
// and three ways on: the law page at this article (its history, its comparison) and the official source. The law page reads
// which article it was opened at from its hash.

import assert from 'node:assert/strict';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { actTitlesOf, lawPageHref, quoteWork, verdictWordKey } from '../scripts/assistant-panel.mjs';
import { focusOf } from '../.react-build/app.mjs';
import { bundle } from '../scripts/react-build.mjs';

const claim = (template_id, text, locale = 'fr') => ({ kind: 'claim', template_id, locale, facts: [], text });
const WORK = '/lu-legilux/loi-1997-07-27-n10';
const STATE = `${WORK}/2025-11-25--${'d'.repeat(64)}/fra`;
const ELI = 'http://data.legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/consolide/20251125';

/** A drafted answer (drafted by an agent; not D4 or D1; not a switch-on gate), its quotation as the live one carries it. */
function answer() {
  return {
    schema: 'lex-v3-answer-dossier/1',
    verdict: 'answer',
    answer_locale: 'fr',
    label: claim('verdict.answer', 'D’après le texte officiel'),
    notices: [claim('notice.assembled_by_model', 'Un modèle d’IA a assemblé cette réponse.'), claim('notice.not_legal_advice', 'Lex montre le texte de la loi.')],
    plan: { rendered: [claim('plan.heading', 'Ce que Lex a fait de votre question')], plan_sha256: 'a'.repeat(64) },
    parts: [{
      part_id: 'p1',
      span: [0, 10],
      class: 'lookup',
      cues: [],
      verdict: 'answer',
      blocks: [
        claim('law.quote_heading', `${WORK}, article 44, tel qu’il se lisait le 14 novembre 2026`),
        {
          kind: 'quote', quote_id: 'q1', language: 'fra', anchor: 'art_44', from: '2025-11-25',
          text: 'Article 44Délai de prescription1.Le délai de prescription de toute action dérivant du contrat d’assurance est de trois ans.',
          offset: 0, length: 120, article_text_sha256: 'b'.repeat(64),
          article_permalink: `${STATE}/art_44`, state_permalink: STATE, official_source: ELI,
        },
        claim('law.interval', 'Applicable à partir du 25 novembre 2025 (éditeur).'),
      ],
    }],
    gates: { quotes: 1, disclosures: 0, non_template_strings: 0, disclosure_complete: true },
    trace: { schema: 'lex-v3-answer-trace/1', question: 'Quel est le délai de prescription ?', steps: [] },
  };
}

test('the answer says the sentence that answers, one line that cites it, three ways on, and the full article one action away', async () => {
  const { AssistantPanel } = await import(pathToFileURL(await bundle('app/AssistantPanel.jsx', 'answer-card-test.mjs')).href);
  const titles = { [WORK]: { title: 'Loi du 27 juillet 1997 sur le contrat d’assurance.', language: 'fra', versions: 10, since: '2006' } };
  const question = "Quel est le délai de prescription des actions dérivant d'un contrat d'assurance ?";
  const markup = renderToStaticMarkup(h(AssistantPanel, { locale: 'fr', dossier: answer(), phase: 'complete', titles, question }));
  const at = (needle) => {
    const index = markup.indexOf(needle);
    assert.ok(index >= 0, `the answer holds ${needle}`);
    return index;
  };
  const order = [
    at('data-answer-sentence'),
    at('<mark>trois ans</mark>'),
    at('Article 44'),
    at('Délai de prescription</span>'),
    at('Loi du 27 juillet 1997 sur le contrat d’assurance.'),
    at('Article complet'),
    at('10 versions depuis 2006'),
    at('Source officielle'),
    at('<blockquote class="assistant-quote"'),
    at('Version applicable depuis le 25/11/2025'),
    at('Applicable à partir du 25 novembre 2025'),
    at('Voir le travail'),
  ];
  assert.deepEqual(order, [...order].sort((a, b) => a - b), 'in the owner\'s order');
  // The sentence is the quotation's own characters, cut at its paragraph and its stop: nothing added, dropped or changed.
  const sentence = markup.slice(markup.indexOf('data-answer-sentence'), markup.indexOf('</p>', markup.indexOf('data-answer-sentence'))).replace(/<[^>]+>/g, '').replace(/^[^>]*>/, '');
  assert.equal(sentence, 'Le délai de prescription de toute action dérivant du contrat d’assurance est de trois ans.');
  assert.ok(answer().parts[0].blocks[1].text.includes(sentence), 'a span of the quotation');
  assert.ok(markup.includes(`href="/fr${WORK}#article-art_44"`), 'the versions: the law page at the article');
  assert.ok(markup.includes(`href="${ELI}" rel="external"`), 'the official source');
  assert.ok(/<div class="answer-article" id="[^"]+" hidden=""/.test(markup), 'the full article is one action away');
  // No verdict word and no status line on the screen: the label is the answer's heading, for a screen reader.
  assert.ok(!markup.includes('<strong>Trouvé</strong>'), 'no verdict word');
  assert.ok(markup.includes('class="visually-hidden">Réponse\u00a0: D’après le texte officiel</h3>'), 'the label, as the answer\'s heading');
  // The heading claim names the act by its address: one action away, never in the answer's face.
  const face = markup.slice(0, markup.indexOf('class="answer-article"'));
  assert.ok(!face.includes(`>${WORK}`), 'no address where a title exists');
  assert.ok(markup.includes(`${WORK}, article 44`), 'every block of the answer is still drawn');
  // With no title asked yet, the answer names no act rather than an address, and says the versions without counting them.
  const untitled = renderToStaticMarkup(h(AssistantPanel, { locale: 'fr', dossier: answer(), phase: 'complete', question }));
  assert.ok(!untitled.includes('class="assistant-act"'), 'no act line without a title');
  assert.ok(untitled.includes('Les versions de l’acte'), 'the versions, uncounted');
  // In English, a French quotation says it is the original French text.
  const english = renderToStaticMarkup(h(AssistantPanel, { locale: 'en', dossier: { ...answer(), answer_locale: 'en' }, phase: 'complete', question }));
  assert.ok(english.includes('The quote is the original French text.'));
});

test('the work, closed: four boxes, the model said once, the time, the cost and the raw plan', async () => {
  const { AssistantPanel } = await import(pathToFileURL(await bundle('app/AssistantPanel.jsx', 'answer-card-test.mjs')).href);
  const dossier = answer();
  dossier.trace.steps = [
    { step_id: 's1', operation_id: 'search', fired: true, parameters: { language: 'fra', limit: 10, query: 'délai de prescription' } },
    { step_id: 'f1', operation_id: 'evidence_bundle', fired: true, parameters: { article: 'art_44', date: '2026-11-14', identifier: WORK } },
  ];
  const work = { seconds: 1.62, search: { matched: 3, more: false }, verified: { checked: 1, ok: 1 } };
  const markup = renderToStaticMarkup(h(AssistantPanel, { locale: 'fr', dossier, phase: 'complete', work }));
  assert.ok(/<details class="answer-work" data-answer-work="">/.test(markup), 'closed by default');
  assert.deepEqual([...markup.matchAll(/data-work="([a-z]+)"/g)].map((match) => match[1]), ['document', 'question', 'retrieval', 'answer']);
  assert.equal((markup.match(/le seul appel au modèle/g) ?? []).length, 1, 'the model, once, on the question');
  assert.equal((markup.match(/code, sans modèle/g) ?? []).length, 3);
  assert.ok(markup.includes('La version applicable depuis le 25/11/2025, telle que publiée'));
  assert.ok(markup.includes('Au 14/11/2026 : chercher « délai de prescription » dans le texte en français, puis lire l’article 44'));
  assert.ok(markup.includes('Articles trouvés : 3 ; gardés : 1'));
  assert.ok(markup.includes('Citations conformes à l’empreinte du texte publié : 1 sur 1'));
  assert.ok(markup.includes('Temps : 1,6 s'));
  assert.ok(/Coût : environ 0,\d{3} € par question/.test(markup));
  assert.ok(markup.includes('Plan brut') && markup.includes('assistant-trace'), 'the raw plan');
});

test('the verdict word, the act a quotation is from and the law page at one article', () => {
  assert.equal(verdictWordKey('answer'), 'verdictFound');
  assert.equal(verdictWordKey('answer_with_enrichment'), 'verdictFound');
  assert.equal(verdictWordKey('refuse'), 'verdictOutOfScope');
  for (const verdict of ['clarify', 'point', 'split']) assert.equal(verdictWordKey(verdict), 'verdictNotFound', verdict);
  assert.deepEqual(quoteWork({ article_permalink: `${STATE}/art_44` }), { publisher: 'lu-legilux', work: 'loi-1997-07-27-n10', address: WORK });
  assert.equal(quoteWork({ article_permalink: 'https://example.org/x' }), null);
  const eu = quoteWork({ article_permalink: '/eu-eurlex/32016R0679/2016-04-27--' + 'e'.repeat(64) + '/eng/art_5' });
  assert.equal(lawPageHref(eu, 'art_5', 'article', 'en'), '/en/eu-eurlex/32016R0679', 'an EU act\'s page as it is');
  assert.equal(lawPageHref(quoteWork({ article_permalink: `${STATE}/art_62-11` }), 'art_62-11', 'changes', 'fr'), `/fr${WORK}#changes-art_62-11`);
  assert.deepEqual(focusOf('#article-art_44'), { kind: 'article', anchor: 'art_44' });
  assert.deepEqual(focusOf('#changes-art_62-11'), { kind: 'changes', anchor: 'art_62-11' });
  assert.equal(focusOf('#law-timeline'), null);
  assert.equal(focusOf(''), null);
});

test("the act's title is asked of its dossier once per act, and an act whose dossier cannot be read keeps none", async () => {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push([url, JSON.parse(init.body)]);
    return { json: async () => ({ result: { value: { broken: true } } }) };
  };
  const twice = answer();
  twice.parts[0].blocks.push({ ...twice.parts[0].blocks[1], quote_id: 'q2', anchor: 'art_45', article_permalink: `${STATE}/art_45` });
  assert.deepEqual(await actTitlesOf(twice, fetchImpl), {}, 'a dossier that does not read gives no title');
  assert.deepEqual(calls, [['/api/v3/dossier', { operation_id: 'dossier', parameters: { identifier: WORK } }]], 'one request per act');
});

test('the date chip says the date chosen in the question sent, as the intake reads a date, and the changes page opens on a year', async () => {
  const { questionAsked, writtenDate } = await import('../.react-build/app.mjs');
  const { lastTwelveMonths } = await import('../scripts/live-radar.mjs');
  assert.equal(writtenDate('2019-03-03'), '03/03/2019', 'day first, as the intake reads a numeric date');
  assert.equal(questionAsked('  Quel est le délai de prescription ? ', ''), 'Quel est le délai de prescription ?', 'today: the question as typed');
  assert.equal(questionAsked('What is the limitation period?', '2020-01-01'), 'What is the limitation period? (as of 01/01/2020)');
  assert.equal(questionAsked('   ', '2020-01-01'), '', 'no question, nothing asked');
  assert.deepEqual(lastTwelveMonths('2026-11-14'), { dateFrom: '2025-11-14', dateTo: '2026-11-14' });
  assert.deepEqual(lastTwelveMonths('2028-02-29'), { dateFrom: '2027-02-28', dateTo: '2028-02-29' });
  assert.equal(lastTwelveMonths('14/11/2026'), null);
});
