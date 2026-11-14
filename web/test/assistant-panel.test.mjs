// The Assistant panel: built, tested and hidden (R47, R52, R55, R45(c)-(e); docs/ANSWER-DOSSIER-1.md).
//
// The panel exists so the switch-on release has nothing left to build, and it is shown nowhere: no page imports it, and no
// built page carries its marker. Its renderings here are of drafted answers: drafted by an agent; not D4 or D1; not a
// switch-on gate.

import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { readdir, readFile } from 'node:fs/promises';
import { join } from 'node:path';
import test from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import * as ASSISTANT_PANEL_IO from '../scripts/assistant-panel.mjs';
import {
  ASSISTANT_PANEL,
  ASSISTANT_PANEL_COPY,
  ASSISTANT_PANEL_MARKER,
  MAXIMUM_CONTEXT_CHIPS,
  PRESENTATION_RESULTS,
  VERDICTS,
  announcements,
  contextChips,
  followUpRequest,
  validateDossier,
} from '../scripts/assistant-panel.mjs';
import { bundle } from '../scripts/react-build.mjs';

const web = fileURLToPath(new URL('..', import.meta.url));

const claim = (template_id, text, locale = 'fr') => ({ kind: 'claim', template_id, locale, facts: [], text });

/** A drafted answer in the contract's shape (drafted by an agent; not D4 or D1; not a switch-on gate). */
function drafted() {
  return {
    schema: 'lex-v3-answer-dossier/1',
    verdict: 'answer',
    answer_locale: 'fr',
    label: claim('verdict.answer', 'D’après le texte officiel'),
    notices: [claim('notice.assembled_by_model', 'Un modèle d’IA a assemblé cette réponse.'), claim('notice.not_legal_advice', 'Lex montre le texte de la loi.')],
    plan: { rendered: [claim('plan.heading', 'Ce que Lex a fait de votre question')], plan_sha256: 'a'.repeat(64) },
    parts: [
      {
        part_id: 'p1',
        span: [0, 10],
        class: 'lookup',
        cues: [],
        verdict: 'answer',
        blocks: [
          claim('law.quote_heading', '/lu-legilux/loi-1991-08-10-n3, article 1er, tel qu’il se lisait le 1er février 2024'),
          { kind: 'quote', quote_id: 'q1', language: 'fra', text: 'Art. 1er. La profession d’avocat…', offset: 0, length: 33, article_text_sha256: 'b'.repeat(64) },
          { kind: 'handoff', entry_id: 'legilux', role: 'official_source', link: 'https://legilux.public.lu/', reviewed_by_person: false, claim: claim('handoff.official_source', 'Source officielle : Legilux, https://legilux.public.lu/') },
        ],
      },
    ],
    gates: { quotes: 1, disclosures: 0, non_template_strings: 0, disclosure_complete: true },
    trace: { schema: 'lex-v3-answer-trace/1', question: 'Que dit l’article 1er ?', steps: [{ step_id: 's1', parameters: { identifier: '/lu-legilux/loi-1991-08-10-n3', date: '2024-02-01', article: 'art_1er' } }] },
  };
}

let panel;
async function Panel() {
  if (!panel) {
    const out = await bundle('app/AssistantPanel.jsx', 'assistant-panel-test.mjs');
    panel = (await import(pathToFileURL(out).href)).AssistantPanel;
  }
  return panel;
}

test('the panel is hidden: the module admits only that, and only the assistant page reaches it', async () => {
  assert.equal(ASSISTANT_PANEL, 'hidden', 'the assistant ships switched off (R30), so its entry stays hidden until the server reports it on (R47, R52)');
  // The panel is drawn by the assistant's page and its entry alone, which the server serves only while the assistant is on
  // (V3WebRoot); `ssr-entry.jsx` renders that page for the server; the home page imports the copy module for the one link's
  // name, which it shows only then (`LiveHome.jsx`). No other module reaches the panel.
  const reaching = ['AssistantPanel.jsx', 'assistant-panel.mjs', 'live-assistant-page.jsx', 'client-live-assistant-entry.jsx', 'ssr-entry.jsx'];
  for (const directory of ['app', 'scripts']) {
    for (const name of await readdir(join(web, directory))) {
      if (!/\.(mjs|jsx)$/.test(name) || reaching.includes(name)) continue;
      const source = await readFile(join(web, directory, name), 'utf8');
      assert.doesNotMatch(source, /(?:from\s+|import\s*\(\s*)["'][^"']*(?:AssistantPanel|assistant-panel|live-assistant-page)(?:\.jsx|\.mjs)?["']/, `${directory}/${name} imports the hidden panel`);
    }
  }
  const home = await readFile(join(web, 'app', 'LiveHome.jsx'), 'utf8');
  assert.doesNotMatch(home, /assistant-panel|AssistantPanel/, "the home page names the link from the chrome table and bundles nothing of the panel's copy");
});

test('no built page carries the panel or any assistant control', async () => {
  for (const output of ['dist', 'dist-live']) {
    const root = join(web, output);
    if (!existsSync(root)) continue;
    const pending = [root];
    while (pending.length > 0) {
      const directory = pending.pop();
      for (const entry of await readdir(directory, { withFileTypes: true })) {
        const path = join(directory, entry.name);
        if (entry.isDirectory()) pending.push(path);
        else if (entry.name.endsWith('.html')) {
          const html = await readFile(path, 'utf8');
          assert.ok(!html.includes(ASSISTANT_PANEL_MARKER), `${path} carries the assistant panel`);
          assert.ok(!html.includes('assistant-question'), `${path} carries the assistant's field`);
        } else if (entry.name.endsWith('.js') && entry.name !== 'client-live-assistant.js') {
          // The processor and preview sentences reach a visitor's browser only in the assistant page's own script, which the
          // server serves only while the assistant is on; no other bundle (the home page's included) carries the panel's copy.
          const js = await readFile(path, 'utf8');
          // The panel's own processor sentences, not the service's name: the privacy notice names Azure OpenAI on every page's
          // chrome since the switch-on (the owner's yes of 2026-11-10), and says so in its own words.
          for (const needle of [ASSISTANT_PANEL_MARKER, ASSISTANT_PANEL_COPY.en.processor, ASSISTANT_PANEL_COPY.fr.processor, ASSISTANT_PANEL_COPY.en.preview, ASSISTANT_PANEL_COPY.fr.preview]) {
            assert.ok(!js.includes(needle), `${path} carries the assistant panel's copy (${needle.slice(0, 30)})`);
          }
        }
      }
    }
  }
});

test('the panel draws the plan first, two polite announcements and no alert, and the notice above the field', async () => {
  const AssistantPanel = await Panel();
  const markup = renderToStaticMarkup(h(AssistantPanel, { locale: 'fr', dossier: drafted(), phase: 'verdict' }));
  assert.ok(markup.includes(`${ASSISTANT_PANEL_MARKER}="hidden"`));
  assert.ok(markup.indexOf('assistant-notice') < markup.indexOf('assistant-question'), 'R44: the notice sits above the input');
  assert.ok(markup.includes(ASSISTANT_PANEL_COPY.fr.notice));
  assert.ok(markup.indexOf('assistant-preview') > markup.indexOf('assistant-notice') && markup.indexOf('assistant-preview') < markup.indexOf('assistant-question'),
    'the preview line sits with the notice, above the field');
  assert.ok(markup.includes(ASSISTANT_PANEL_COPY.fr.preview) && markup.includes(ASSISTANT_PANEL_COPY.fr.processor));
  // The answer card (the owner's order of 2026-11-13, section C, over R45(c)'s plan-first order): the verdict in a word first,
  // then the quotation's card, then how Lex found it (the validated plan and the trace) one action away, then the notices in
  // one line under the answer, and what the assistant sends at the foot of the page.
  assert.ok(markup.indexOf('assistant-verdict') < markup.indexOf('assistant-quote'), 'the verdict first');
  assert.ok(markup.includes('<strong>Trouvé</strong>'), 'in a word');
  assert.ok(markup.indexOf('assistant-quote') < markup.indexOf('assistant-how'), 'the quotation before how it was found');
  assert.match(markup, /<details class="assistant-how"><summary>Comment Lex a trouvé<\/summary><ol class="assistant-plan">/, 'the validated plan, one action away');
  assert.ok(markup.indexOf('assistant-how') < markup.indexOf('assistant-disclaimer'), 'the notices under the answer');
  assert.equal((markup.match(/class="assistant-disclaimer"/g) ?? []).length, 1, 'in one line');
  assert.ok(markup.lastIndexOf('assistant-processor') > markup.indexOf('assistant-disclaimer'), 'what is sent, at the foot');
  assert.match(markup, /role="status" aria-live="polite" aria-atomic="true">Réponse : D’après le texte officiel\.</, 'one announcement at the verdict');
  assert.doesNotMatch(markup, /role="alert"|aria-live="assertive"/, 'an answer is never announced as an alert');
  assert.match(markup, /<blockquote class="assistant-quote" lang="fr" data-text-sha256="b{64}">/, 'a quotation carries its held language');
  assert.match(markup, /maxLength="1000"|maxlength="1000"/, 'the field holds what the intake reads');
  const complete = renderToStaticMarkup(h(AssistantPanel, { locale: 'en', dossier: { ...drafted(), answer_locale: 'en' }, phase: 'complete' }));
  assert.match(complete, /aria-live="polite" aria-atomic="true">The answer is complete\.</, 'one announcement at completion');
});

test('the dossier is checked before it is drawn, and a presentation result is never a verdict', () => {
  assert.deepEqual(VERDICTS, ['answer', 'answer_with_enrichment', 'point', 'clarify', 'refuse', 'split']);
  assert.deepEqual(PRESENTATION_RESULTS, ['assistant_v3_unavailable', 'localization_unavailable']);
  assert.equal(validateDossier(drafted()).parts.length, 1);
  const broken = [
    ['a presentation result as a verdict', (d) => { d.verdict = 'assistant_v3_unavailable'; }],
    ['no plan', (d) => { d.plan.rendered = []; }],
    ['free prose', (d) => { d.parts[0].blocks.push({ kind: 'note', text: 'You will win.' }); }],
    ['a claim without its template', (d) => { delete d.label.template_id; }],
    ['a quotation without its hash', (d) => { d.parts[0].blocks[1].article_text_sha256 = 'nope'; }],
    ['a German answer', (d) => { d.answer_locale = 'de'; }],
    ['no trace', (d) => { delete d.trace; }],
  ];
  for (const [why, mutate] of broken) {
    const dossier = drafted();
    mutate(dossier);
    assert.throws(() => validateDossier(dossier), Error, why);
  }
});

test('follow-ups carry at most two typed chips from the last trace and never an earlier question', () => {
  const chips = contextChips(drafted().trace);
  assert.deepEqual(chips, [{ kind: 'work', value: '/lu-legilux/loi-1991-08-10-n3' }, { kind: 'date', value: '2024-02-01' }]);
  assert.ok(chips.length <= MAXIMUM_CONTEXT_CHIPS);
  const request = followUpRequest('Et l’article 2 ?', chips);
  assert.deepEqual(Object.keys(request), ['question', 'context']);
  assert.ok(!JSON.stringify(request).includes('Que dit'), 'the earlier question is never resent (R45(e))');
  assert.throws(() => followUpRequest('Et ?', [{ kind: 'question', value: 'Que dit l’article 1er ?' }]), Error);
  const view = validateDossier(drafted());
  assert.deepEqual(announcements(view, 'fr'), ['Réponse : D’après le texte officiel.', 'La réponse est complète.']);
});

test('the privacy notice says what the assistant sends, to which processor, and what that processor may keep, in both languages', async () => {
  const { LIVE_CHROME } = await import('../scripts/live-chrome.mjs');
  for (const [table, processor, entity] of [[LIVE_CHROME.en, 'processor', 'Microsoft Ireland Operations Limited'], [LIVE_CHROME.fr, 'sous-traitant', 'Microsoft Ireland Operations Limited']]) {
    const section = table.privacy.sections.assistant;
    assert.deepEqual(Object.keys(section), ['heading', 'processor']);
    assert.ok(section.processor.includes(processor) && section.processor.includes(entity), 'the processor, named');
    assert.match(section.processor, /30 (days|jours)/, 'what the processor may keep, and for how long');
    assert.doesNotMatch(section.processor, /modified abuse monitoring|surveillance des abus modifi/, 'only the retention case that holds');
    // What goes with the question is said, as AzureOpenAiAssistantPlanner.RequestBody sends it (the review of #1022).
    assert.match(section.processor, /language of the answer|langue de la réponse/);
    assert.match(section.processor, /operations|opérations de Lex/);
    assert.doesNotMatch(section.processor, /Nothing else is sent|Rien d’autre n’est envoyé/, 'nothing else about the reader, not nothing else at all');
  }
});

test('the panel holds French and English only, and its French keeps the no-break spaces', () => {
  assert.deepEqual(Object.keys(ASSISTANT_PANEL_COPY), ['en', 'fr']);
  assert.deepEqual(Object.keys(ASSISTANT_PANEL_COPY.fr), Object.keys(ASSISTANT_PANEL_COPY.en));
  for (const [key, text] of Object.entries(ASSISTANT_PANEL_COPY.fr)) {
    assert.doesNotMatch(text, / [:;?!»]|« /, `fr.${key}: a no-break space before : ; ? ! and inside guillemets`);
    assert.doesNotMatch(text, /'/, `fr.${key}: the French apostrophe is U+2019`);
  }
});

test("the assistant's page renders the live panel, never indexed, and is built as no file", async () => {
  const out = await bundle('app/live-assistant-page.jsx', 'live-assistant-page-test.mjs');
  const { renderLiveAssistantPage, LIVE_ASSISTANT_ROOT } = await import(pathToFileURL(out).href);
  const html = renderLiveAssistantPage();
  assert.ok(html.includes(`id="${LIVE_ASSISTANT_ROOT}"`));
  assert.ok(html.includes(`${ASSISTANT_PANEL_MARKER}="hidden"`), "the panel's marker, so the built-pages check sees it if a page ever carried it");
  const escaped = (text) => text.replaceAll('&', '&amp;').replaceAll("'", '&#x27;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
  for (const key of ['notice', 'preview', 'processor']) assert.ok(html.includes(escaped(ASSISTANT_PANEL_COPY.en[key])), key);
  assert.match(html, /<meta name="robots" content="noindex/, "never indexed");
  assert.ok(html.includes('src="/en/client-live-assistant.js"'));
  assert.equal(existsSync(join(web, 'dist-live', 'en', 'assistant.html')), false, "the page is rendered by the server only while the assistant is on, never built as a file");
});

test("the panel reads an ask envelope: an answer, a refuse as advice_boundary, a presentation result, and ask's limit", () => {
  const { askOutcome, askRequest } = ASSISTANT_PANEL_IO;
  assert.deepEqual(askRequest('Que dit la loi ?'), { operation_id: 'ask', parameters: { question: 'Que dit la loi ?' } }, 'the question alone (R45(e))');
  const dossier = drafted();
  assert.equal(askOutcome({ result: { object_type: 'answer_dossier', value: dossier }, refusal: null }, 'fr').dossier, dossier);
  assert.equal(askOutcome({ result: null, refusal: { code: 'advice_boundary', helpful_payload: { answer_dossier: dossier } } }, 'fr').dossier, dossier);
  assert.equal(askOutcome({ result: null, refusal: { code: 'rate_limited', helpful_payload: {} } }, 'fr').sentence, ASSISTANT_PANEL_COPY.fr.rateLimited);
  assert.equal(askOutcome({ result: { object_type: 'handoff_card', value: { presentation_result: 'assistant_v3_unavailable' } } }, 'en').sentence, ASSISTANT_PANEL_COPY.en.failed);
  assert.equal(askOutcome({ result: { object_type: 'handoff_card', value: { presentation_result: 'localization_unavailable', sentences: { fr: 'FR', en: 'EN' } } } }, 'fr').sentence, 'FR');
  assert.equal(askOutcome(null, 'en').dossier, null);
});

test('a question in the page address is read once, trimmed, and only from "#q=" (2026-11-13: the home page opens the assistant with it)', async () => {
  const { askFragment, readAskFragment, ASK_FRAGMENT_MAX } = ASSISTANT_PANEL_IO;
  for (const question of ["Que dit l'article 5 du RGPD ?", 'What does Article 50 of the AI Act say?', 'a & b = c #1 ?']) {
    assert.equal(readAskFragment(askFragment(question)), question, question);
    assert.ok(!askFragment(question).slice(1).includes('#'), 'one fragment');
  }
  assert.equal(readAskFragment('#q=%20%20RGPD%20'), 'RGPD');
  for (const none of ['', '#', '#q=', '#q=%20', '?q=RGPD', 'q=RGPD', '#work=x', `#q=${'a'.repeat(ASK_FRAGMENT_MAX + 1)}`, null, undefined]) {
    assert.equal(readAskFragment(none), null, String(none));
  }
  // The home page reaches it through its own module, which carries none of the panel's copy.
  const fragment = await readFile(join(web, 'scripts', 'ask-fragment.mjs'), 'utf8');
  assert.doesNotMatch(fragment, /import /, 'the fragment module imports nothing');
});

test("a quotation is set in the paragraphs its own numbering shows, and no word is added, dropped or changed", () => {
  const { quoteParagraphs } = ASSISTANT_PANEL_IO;
  const article = 'Article 50 Transparency obligations for providers 1. Providers shall ensure that AI systems are designed. 2. Deployers shall inform: (a) the persons concerned; (b) the authority. 3. Paragraph 1 shall not apply.';
  assert.deepEqual(quoteParagraphs(article), [
    'Article 50 Transparency obligations for providers',
    '1. Providers shall ensure that AI systems are designed.',
    '2. Deployers shall inform:',
    '(a) the persons concerned;',
    '(b) the authority.',
    '3. Paragraph 1 shall not apply.',
  ]);
  const luxembourg = 'Art. 1er. (1) Le contrat est formé. (2) Il comprend: a) la police; b) les conditions. Voir les points (a) et (b) et l\'article 5(1).';
  assert.deepEqual(quoteParagraphs(luxembourg), ['Art. 1er.', '(1) Le contrat est formé.', '(2) Il comprend:', 'a) la police;', "b) les conditions. Voir les points (a) et (b) et l'article 5(1)."]);
  for (const text of [article, luxembourg, 'One sentence with no numbering at all.', 'Mentions 2. Something mid-sentence without a stop']) {
    assert.equal(quoteParagraphs(text).join(' '), text.replace(/\s+/g, ' ').trim(), 'the words, in order, every one');
  }
  assert.deepEqual(quoteParagraphs(''), []);
});

test("an answer's quotations come in the interface's language first, the other language and every check one action away, with every block drawn", async () => {
  const { quoteGroups, groupsForLocale } = ASSISTANT_PANEL_IO;
  const claim = (template_id, text) => ({ kind: 'claim', template_id, text, locale: 'en', facts: [] });
  const quote = (language, text) => ({ kind: 'quote', language, text, article_text_sha256: 'a'.repeat(64) });
  const group = (language) => [
    claim('law.quote_heading', `/eu-eurlex/32024R1689, article 050 (${language})`),
    quote(language, `Article 50 ${language} 1. First. 2. Second.`),
    claim('law.interval_eu_open', 'Consolidated wording state from 27 July 2026.'),
    claim('law.content_hash', `Content hash: ${'b'.repeat(64)}`),
    claim('law.official_source', 'Official source: https://publications.europa.eu/resource/cellar/x'),
    claim('law.permalink', '/eu-eurlex/32024R1689/2026-07-27--c/eng'),
  ];
  const blocks = [claim('law.lead', 'Lead.'), ...group('eng'), ...group('fra')];
  const { lead, groups } = quoteGroups(blocks);
  assert.equal(lead.length, 1);
  assert.deepEqual(groups.map((one) => one.language), ['eng', 'fra']);
  assert.equal(lead.length + groups.reduce((sum, one) => sum + one.blocks.length, 0), blocks.length, 'every block, once');
  assert.deepEqual(groupsForLocale(groups, 'fr').shown.map((one) => one.language), ['fra']);
  assert.deepEqual(groupsForLocale(groups, 'en').other.map((one) => one.language), ['fra']);
  assert.deepEqual(groupsForLocale(groups.slice(1), 'en').shown.map((one) => one.language), ['fra'], 'none in the language: every group shown');
});

test('a quotation whose publisher ran its parts together is set in its paragraphs, no character added, changed or dropped', async () => {
  const { quoteParagraphs } = await import('../scripts/assistant-panel.mjs');
  const cases = [
    ['Article 44Délai de prescription1.Le délai est de trois ans.Le délai court.2.Sous réserve.3.L’action récursoire.',
      ['Article 44', 'Délai de prescription', '1.Le délai est de trois ans.', 'Le délai court.', '2.Sous réserve.', '3.L’action récursoire.']],
    ['Art. 74.Garanties accessoiresMême lorsque le sinistre se produit. Il couvre McDonald et LuxTrust.',
      ['Art. 74.', 'Garanties accessoires', 'Même lorsque le sinistre se produit. Il couvre McDonald et LuxTrust.']],
    ['Article 1erDéfinitionsAu sens de la présente loi, on entend par:Contrat d’assurance: un contrat.',
      ['Article 1er', 'Définitions', 'Au sens de la présente loi, on entend par:', 'Contrat d’assurance: un contrat.']],
    ['S.A.R.L. capital', ['S.A.R.L. capital']],
    ['loi du 27 juillet 1997.Le preneur', ['loi du 27 juillet 1997.Le preneur']],
    ['l’article 89 se prescrit', ['l’article 89 se prescrit']],
  ];
  for (const [text, expected] of cases) {
    const paragraphs = quoteParagraphs(text);
    assert.deepEqual(paragraphs, expected, text);
    assert.equal(paragraphs.join('').replace(/\s/g, ''), text.replace(/\s/g, ''), `${text}: the same characters`);
  }
});
