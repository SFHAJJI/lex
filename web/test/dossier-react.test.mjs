// The dossier ported to React, measured against the string renderer.
//
// The claim being tested is not that React works. It is that both renderers apply the same rules
// and reject the same inputs, because a framework quietly becoming a second home for legal rules
// is the worst available outcome of adopting one.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { Dossier } from '../.react-build/app.mjs';
import {
  NOT_INGESTED,
  STATUS_CAPTION,
  renderDossier,
  validateDossier,
} from '../scripts/dossier.mjs';

const IDENTITY = {
  title: 'Acte synthetique de demonstration',
  title_language: 'fr',
  publisher: 'preview-synthetic',
  work_identifier: 'https://preview.invalid/synthetic-preview-work',
  document_type: 'CODE',
};

const GOOD = {
  identity: IDENTITY,
  status: { binding_status: 'in_force' },
  dates: [
    { role: 'publication', date: '2021-01-26', source: 'publisher record' },
    { role: 'observed_from', date: '2026-08-14T23:05:14Z', source: 'this corpus' },
  ],
  coverage: { states_held: 4, states_with_text: 4, holes: [] },
};

const react = (props) => renderToStaticMarkup(h(Dossier, props));

test('both renderers pin the same caption literal', () => {
  // Pinned to a literal rather than compared against itself: imported and asserted against
  // itself, this constant could be redefined to anything and both renderers would agree.
  assert.equal(STATUS_CAPTION, 'current-state flag, not a historical statement');
  assert.equal(react(GOOD).includes(STATUS_CAPTION), true);
  assert.equal(renderDossier(GOOD).includes(STATUS_CAPTION), true);
});

test('the flag never appears without its caption, in either renderer', () => {
  // The caption is the only reason the chip is allowed on the page: a held state applicable
  // before entry into force carries in_force, and without the caption that chip is false.
  const html = react(GOOD);
  assert.equal(html.includes('<code>in_force</code>'), true);
  assert.equal(html.indexOf(STATUS_CAPTION) > html.indexOf('in_force'), true);
});

test('a derived value is refused as a publisher flag by both renderers', () => {
  for (const bad of ['REPEALED (lex derived)', 'In Force', '', 'in force']) {
    const props = { ...GOOD, status: { binding_status: bad } };
    assert.throws(() => react(props), /publisher flag|caption about nothing/);
    assert.throws(() => renderDossier(props), /publisher flag|caption about nothing/);
  }
});

test('zero states held is not a work without gaps, in either renderer', () => {
  // "No gap" is a claim about a record that exists. A reader told a work has no gaps concludes
  // the corpus holds its whole history.
  const props = { ...GOOD, coverage: { states_held: 0, states_with_text: 0, holes: [] } };
  for (const html of [react(props), renderDossier(props)]) {
    assert.equal(html.includes('No gap between the states held'), false);
    assert.equal(html.includes('No state of this work is held by this corpus'), true);
    assert.equal(html.includes('not absence of law'), true);
  }
});

test('the record clock keeps its instant and the legal clock gains no time of day', () => {
  const instantOnLegal = {
    ...GOOD,
    dates: [{ role: 'publication', date: '2021-01-26T00:00:00Z', source: 'publisher record' }],
  };
  assert.throws(() => react(instantOnLegal), /calendar date/);
  assert.throws(() => renderDossier(instantOnLegal), /calendar date/);

  const dateOnRecord = {
    ...GOOD,
    dates: [{ role: 'observed_from', date: '2026-08-14', source: 'this corpus' }],
  };
  assert.throws(() => react(dateOnRecord), /UTC instant/);
  assert.throws(() => renderDossier(dateOnRecord), /UTC instant/);
});

test('an absent date says what it waits for, and an unfilled slot says where to look', () => {
  const props = {
    ...GOOD,
    dates: [
      ...GOOD.dates,
      { role: 'entry_into_force', date: null, source: 'publisher axiom', awaiting: 'the axiom service' },
    ],
    slots: [{ what: 'responsible ministry', where: 'published on the publisher own channel' }],
  };
  for (const html of [react(props), renderDossier(props)]) {
    assert.equal(html.includes(NOT_INGESTED), true);
    assert.equal(html.includes('the axiom service'), true);
    assert.equal(html.includes('published on the publisher own channel'), true);
  }
  // An absent date with no explanation is refused by both.
  const silent = {
    ...GOOD,
    dates: [...GOOD.dates, { role: 'entry_into_force', date: null, source: 'publisher axiom' }],
  };
  assert.throws(() => react(silent), /what it is waiting for/);
  assert.throws(() => renderDossier(silent), /what it is waiting for/);
});

test('a reversed or empty coverage hole is refused by both renderers', () => {
  for (const hole of [{ from: '2024-12-28', to: '2004-04-02' }, { from: '2004-01-01', to: '2004-01-01' }]) {
    const props = { ...GOOD, coverage: { states_held: 2, states_with_text: 2, holes: [hole] } };
    assert.throws(() => react(props), /backwards or empty/);
    assert.throws(() => renderDossier(props), /backwards or empty/);
  }
});

test('the title carries its own language, not the chrome around it', () => {
  assert.equal(react(GOOD).includes('lang="fr"'), true);
  assert.throws(() => react({ ...GOOD, identity: { ...IDENTITY, title_language: 'french' } }), /carries its own language/);
});

test('the validator is the single source both renderers consult', () => {
  const card = validateDossier(GOOD);
  assert.equal(react(GOOD).includes(card.workIdentifier), true);
  assert.equal(renderDossier(GOOD).includes(card.workIdentifier), true);
});
