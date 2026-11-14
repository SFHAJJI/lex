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

test('the card says the verdict in a word, the article and its heading, the act, the quotation, its version and three ways on', async () => {
  const { AssistantPanel } = await import(pathToFileURL(await bundle('app/AssistantPanel.jsx', 'answer-card-test.mjs')).href);
  const titles = { [WORK]: { title: 'Loi du 27 juillet 1997 sur le contrat d’assurance.', language: 'fra' } };
  const markup = renderToStaticMarkup(h(AssistantPanel, { locale: 'fr', dossier: answer(), phase: 'complete', titles }));
  const at = (needle) => {
    const index = markup.indexOf(needle);
    assert.ok(index >= 0, `the card holds ${needle}`);
    return index;
  };
  const order = [
    at('<strong>Trouvé</strong>'),
    at('Article 44 : Délai de prescription'),
    at('Loi du 27 juillet 1997 sur le contrat d’assurance.'),
    at('<blockquote class="assistant-quote"'),
    at('Version applicable depuis le 2025-11-25'),
    at('Voir à une autre date'),
    at('Ce qui a changé'),
    at('Source officielle'),
    at('Applicable à partir du 25 novembre 2025'),
  ];
  assert.deepEqual(order, [...order].sort((a, b) => a - b), 'in the owner\'s order');
  assert.ok(markup.includes(`href="/fr${WORK}#article-art_44"`), 'another date: the law page at the article\'s history');
  assert.ok(markup.includes(`href="/fr${WORK}#changes-art_44"`), 'what changed: the law page at the article\'s comparison');
  assert.ok(markup.includes(`href="${ELI}" rel="external"`), 'the official source');
  // The heading claim names the act by its address: one action away, never in the card's face.
  const face = markup.slice(0, markup.indexOf('assistant-checks'));
  assert.ok(!face.includes(`>${WORK}`), 'no address where a title exists');
  assert.ok(markup.includes(`${WORK}, article 44`), 'every block of the answer is still drawn');
  // With no title asked yet, the card names no act rather than an address.
  const untitled = renderToStaticMarkup(h(AssistantPanel, { locale: 'fr', dossier: answer(), phase: 'complete' }));
  assert.ok(!untitled.includes('class="assistant-act"'), 'no act line without a title');
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
