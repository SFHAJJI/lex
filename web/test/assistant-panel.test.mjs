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

test('the panel is hidden: the module admits only that, and no page imports it', async () => {
  assert.equal(ASSISTANT_PANEL, 'hidden', 'the assistant is switched off on Lex (R30), so its entry stays hidden (R47, R52)');
  for (const directory of ['app', 'scripts']) {
    for (const name of await readdir(join(web, directory))) {
      if (!/\.(mjs|jsx)$/.test(name) || ['AssistantPanel.jsx', 'assistant-panel.mjs'].includes(name)) continue;
      const source = await readFile(join(web, directory, name), 'utf8');
      assert.doesNotMatch(source, /(?:from\s+|import\s*\(\s*)["'][^"']*(?:AssistantPanel|assistant-panel)(?:\.jsx|\.mjs)?["']/, `${directory}/${name} imports the hidden panel`);
    }
  }
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
  assert.ok(markup.indexOf('assistant-plan') < markup.indexOf('assistant-label'), 'R45(c): the validated plan is shown first');
  assert.ok(markup.indexOf('assistant-plan') < markup.indexOf('assistant-quote'));
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

test('the panel holds French and English only, and its French keeps the no-break spaces', () => {
  assert.deepEqual(Object.keys(ASSISTANT_PANEL_COPY), ['en', 'fr']);
  assert.deepEqual(Object.keys(ASSISTANT_PANEL_COPY.fr), Object.keys(ASSISTANT_PANEL_COPY.en));
  for (const [key, text] of Object.entries(ASSISTANT_PANEL_COPY.fr)) {
    assert.doesNotMatch(text, / [:;?!»]|« /, `fr.${key}: a no-break space before : ; ? ! and inside guillemets`);
    assert.doesNotMatch(text, /'/, `fr.${key}: the French apostrophe is U+2019`);
  }
});
