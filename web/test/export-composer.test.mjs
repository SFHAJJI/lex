import assert from 'node:assert/strict';
import test from 'node:test';

import {
  COMPOSE_TIME_NOTE,
  DISPOSITIONS,
  EMPTY_CART_NOTE,
  WATERMARK_PREVIEW,
  dispositionOf,
  exportComposerModel,
  renderExportComposer,
  withheldSummary,
} from '../scripts/export-composer.mjs';

const MATTER = { reference: 'M-2026-014', author: 'S. Hajji' };

function item(overrides = {}) {
  return {
    kind: 'publisher_text',
    lex_id: 'lu-legilux:loi-1993-04-05-n1:2025-01-01',
    valid_from: '2025-01-01',
    valid_to: null,
    official_uri: 'https://legilux.public.lu/eli/etat/leg/loi/1993/04/05/n1/consolide/20250101',
    record_sha256: 'a'.repeat(64),
    licence: 'cc-by-4.0',
    attribution: 'Journal officiel du Grand-Duche de Luxembourg',
    text_public: true,
    ...overrides,
  };
}

test('a cc-by item whose rights are established travels with its text', () => {
  assert.equal(dispositionOf(item(), 'x'), 'travels_with_text');
});

test('a licence that does not embed text is named as a licence limit', () => {
  assert.equal(
    dispositionOf(item({ licence: 'licence-scl' }), 'x'),
    'withheld_by_licence',
  );
});

test('rights are asked before the licence, so a closed gate is never reported as a licence limit', () => {
  // The item's licence would happily embed text. The reason it cannot is that no public-text
  // right was established, and that is the more serious fact, so it must be the one reported.
  assert.equal(
    dispositionOf(item({ text_public: false, licence: 'cc-by-4.0' }), 'x'),
    'withheld_by_rights',
  );
  // And an item that fails both is still reported by the rights gate, not the licence.
  assert.equal(
    dispositionOf(item({ text_public: false, licence: 'licence-scl' }), 'x'),
    'withheld_by_rights',
  );
});

test('an absent text_public is refused, never read as false', () => {
  const bare = item();
  delete bare.text_public;
  assert.throws(() => dispositionOf(bare, 'cart item 1'), /text_public/);
});

test('an unknown licence is refused rather than treated as non-embedding', () => {
  assert.throws(() => dispositionOf(item({ licence: 'cc-by-nc' }), 'x'), /known licence/);
});

test('the composer derives disposition and refuses a caller who declares it', () => {
  // A caller-declared disposition must not be able to change the answer. If it could, the
  // preview and the bundle could disagree about the same item.
  const model = exportComposerModel({
    items: [item({ licence: 'licence-scl', disposition: 'travels_with_text' })],
    matter: MATTER,
  });
  assert.equal(model.rows[0].disposition, 'withheld_by_licence');
});

test('derived and unofficial items are refused from the cart, not silently dropped', () => {
  for (const kind of ['derived', 'unofficial']) {
    assert.throws(
      () => exportComposerModel({ items: [item({ kind })], matter: MATTER }),
      /excludes derived joins and unofficial translations/,
    );
  }
});

test('a matter reference and an author are required, because no record carries them', () => {
  for (const field of ['reference', 'author']) {
    const matter = { ...MATTER, [field]: '   ' };
    assert.throws(
      () => exportComposerModel({ items: [item()], matter }),
      new RegExp(`matter ${field}`),
    );
  }
});

test('text that would travel without an attribution is refused', () => {
  assert.throws(
    () => exportComposerModel({ items: [item({ attribution: '  ' })], matter: MATTER }),
    /names no attribution/,
  );
});

test('an item withheld by licence needs no attribution to compose', () => {
  const model = exportComposerModel({
    items: [item({ licence: 'licence-scl', attribution: undefined })],
    matter: MATTER,
  });
  assert.equal(model.rows[0].disposition, 'withheld_by_licence');
});

test('counts cover every disposition and sum to the cart', () => {
  const model = exportComposerModel({
    items: [
      item(),
      item({ licence: 'licence-scl' }),
      item({ text_public: false }),
      item({ text_public: false, licence: 'licence-scl' }),
    ],
    matter: MATTER,
  });
  assert.deepEqual(Object.keys(model.counts).sort(), [...DISPOSITIONS].sort());
  const total = DISPOSITIONS.reduce((sum, name) => sum + model.counts[name], 0);
  assert.equal(total, model.rows.length);
  assert.equal(model.withheld, 3);
});

test('the withheld sentence gives both numbers, never a bare count', () => {
  const model = exportComposerModel({
    items: [item(), item({ licence: 'licence-scl' })],
    matter: MATTER,
  });
  const sentence = withheldSummary(model);
  // Pin the subject, not a fragment that survives a rewrite of the other half.
  assert.ok(sentence.startsWith('1 of 2 pinned items will export'), sentence);
});

test('a cart that is entirely exportable says nothing about withholding', () => {
  const model = exportComposerModel({ items: [item(), item()], matter: MATTER });
  assert.equal(withheldSummary(model), null);
  assert.ok(!renderExportComposer({ items: [item()], matter: MATTER }).includes('compose-withheld'));
});

test('one withheld item is singular', () => {
  const model = exportComposerModel({ items: [item({ text_public: false })], matter: MATTER });
  assert.ok(withheldSummary(model).startsWith('1 of 1 pinned item will export'));
});

test('an empty cart renders its own sentence rather than an empty list', () => {
  const html = renderExportComposer({ items: [], matter: MATTER });
  assert.ok(html.includes(EMPTY_CART_NOTE));
  assert.ok(!html.includes('<ol'));
});

test('the composed page states the compose-time rule and the watermark before export', () => {
  const html = renderExportComposer({ items: [item()], matter: MATTER });
  assert.ok(html.includes(COMPOSE_TIME_NOTE));
  assert.ok(html.includes(WATERMARK_PREVIEW));
});

test('each row carries a sentence, so meaning never rests on a badge beside a licence name', () => {
  const html = renderExportComposer({
    items: [item({ licence: 'licence-scl' })],
    matter: MATTER,
  });
  assert.ok(html.includes('The licence does not let the text travel.'));
});

test('an item missing its hash, interval or official source cannot be listed', () => {
  for (const field of ['valid_from', 'official_uri', 'record_sha256']) {
    const bare = item();
    bare[field] = '';
    assert.throws(
      () => exportComposerModel({ items: [bare], matter: MATTER }),
      new RegExp(field),
    );
  }
});

test('an unparseable identity is refused while the cart is still editable', () => {
  assert.throws(
    () => exportComposerModel({ items: [item({ lex_id: 'not-an-identity' })], matter: MATTER }),
    /./,
  );
});

test('the register columns are the closed set and carry no assessment column', () => {
  const model = exportComposerModel({ items: [item()], matter: MATTER });
  for (const forbidden of ['impact', 'owner action', 'compliant']) {
    assert.ok(!model.columns.includes(forbidden));
  }
  assert.ok(model.columns.includes('record_sha256'));
});

test('output escapes a hostile identifier rather than emitting it raw', () => {
  const html = renderExportComposer({
    items: [item()],
    matter: { reference: '<script>alert(1)</script>', author: 'A' },
  });
  assert.ok(!html.includes('<script>alert(1)</script>'));
  assert.ok(html.includes('&lt;script&gt;'));
});
