import assert from 'node:assert/strict';
import test from 'node:test';

import {
  ITEM_KINDS,
  LICENCES,
  REGISTER_COLUMNS,
  renderEvidenceBundle,
  renderRegister,
} from '../scripts/evidence-bundle.mjs';

const DIGEST = '5512d26f4fcdf962273e5f4ac59b893401b380a128a737ba718d3326cba0ed7e';

const ITEM = {
  kind: 'publisher_text',
  citation: 'Synthetic preview work, article 1, state applicable from 2001-01-01',
  identifier: 'preview-synthetic:synthetic-preview-work:2001-01-01',
  valid_from: '2001-01-01',
  valid_to: '2002-01-01',
  publication_date: '2000-12-01',
  observed_from: '2026-01-01T00:00:00Z',
  record_sha256: DIGEST,
  licence: 'cc-by-4.0',
  attribution: 'Synthetic preview publisher, CC BY 4.0',
  publisher: 'preview-synthetic',
  official_uri: 'https://preview.invalid/synthetic-preview-work/2001-01-01',
  text: 'LEX V3 SYNTHETIC PREVIEW. Article 1. This text has no legal authority.',
};

const VERIFICATION = {
  recompute_recipe: 'Fetch each official source with GET and hash the exact bytes with SHA-256.',
  signing_key: 'synthetic-public-key',
  fetch_note: 'Use GET rather than HEAD: the publisher answers 403 to HEAD on both hosts.',
};

const COLUMNS = ['identifier', 'valid_from', 'valid_to', 'official_uri', 'record_sha256'];

const GOOD = {
  items: [ITEM],
  columns: COLUMNS,
  verification: VERIFICATION,
  // The publisher's own vocabulary. Without it every item's dates were labelled
  // "applicable", so an EU consolidation state was exported as an applicability claim.
  semantics: 'publisher_applicability',
};

test('a derived or unofficial item cannot enter a bundle', () => {
  // Not excluded by default, refused. A labelled convenience that quietly enters a bundle
  // stops being labelled at the exact moment it matters.
  assert.deepEqual([...ITEM_KINDS], ['publisher_text', 'derived', 'unofficial']);
  for (const kind of ['derived', 'unofficial']) {
    assert.throws(
      () => renderEvidenceBundle({ ...GOOD, items: [{ ...ITEM, kind }] }),
      new RegExp(`is ${kind} and cannot enter an evidence bundle`),
      `${kind} entered`,
    );
  }
  assert.throws(
    () => renderEvidenceBundle({ ...GOOD, items: [{ ...ITEM, kind: undefined }] }),
    /must declare its kind/,
  );
});

test('rights are applied at compose time, not left to the caller', () => {
  // The caller passes text; the licence decides whether it travels. A bundle cannot carry
  // text the publisher did not license, whatever was handed to it.
  const withheld = renderEvidenceBundle({
    ...GOOD,
    items: [{ ...ITEM, licence: 'licence-scl' }],
  });
  assert.ok(!withheld.includes('no legal authority'), 'withheld text travelled anyway');
  assert.ok(withheld.includes('Text withheld by licence'));
  assert.ok(withheld.includes(DIGEST), 'the digest must still travel');
  assert.ok(withheld.includes('preview.invalid'), 'the official link must still travel');

  const embedded = renderEvidenceBundle(GOOD);
  assert.ok(embedded.includes('no legal authority'));
  assert.ok(embedded.includes('CC BY 4.0'));

  // cc0 needs no attribution line, and does not get one.
  const cc0 = renderEvidenceBundle({ ...GOOD, items: [{ ...ITEM, licence: 'cc0' }] });
  assert.ok(!cc0.includes('bundle-attribution'));

  assert.throws(
    () => renderEvidenceBundle({ ...GOOD, items: [{ ...ITEM, licence: 'ask-me' }] }),
    /can only do that for a licence it knows/,
  );
  assert.ok(!LICENCES['licence-scl'].embedsText);
});

test('every item carries the three dates and its digest', () => {
  for (const [field, value, pattern] of [
    ['valid_from', '2026-99-99', /valid_from is not a calendar date/],
    ['publication_date', undefined, /publication_date is not a calendar date/],
    ['observed_from', '2026-01-01', /needs the instant it was observed/],
    ['record_sha256', 'not-a-digest', /needs its record digest/],
    ['citation', '  ', /needs its citation string/],
  ]) {
    assert.throws(
      () => renderEvidenceBundle({ ...GOOD, items: [{ ...ITEM, [field]: value }] }),
      pattern,
      `${field}=${String(value)} was bundled`,
    );
  }

  const html = renderEvidenceBundle(GOOD);
  assert.ok(html.includes('applicable'));
  assert.ok(html.includes('published'));
  assert.ok(html.includes('observed'));
});

test('the register refuses the three columns that turn a record into an opinion', () => {
  for (const [column, phrase] of [
    ['impact', /an impact is an assessment/],
    ['owner_action', /tells a reader what to do/],
    ['compliant', /applies the law to a person/],
  ]) {
    assert.throws(
      () => renderRegister({ items: [ITEM], columns: [...COLUMNS, column] }),
      phrase,
      `${column} was accepted as a column`,
    );
  }
});

test('the register column set is closed', () => {
  assert.throws(
    () => renderRegister({ items: [ITEM], columns: ['identifier', 'notes'] }),
    /is not a register column/,
  );
  assert.throws(
    () => renderRegister({ items: [ITEM], columns: ['identifier', 'identifier'] }),
    /appears twice/,
  );
  assert.throws(() => renderRegister({ items: [ITEM], columns: [] }), /needs columns/);

  // Every declared column renders.
  const html = renderRegister({ items: [ITEM], columns: [...REGISTER_COLUMNS] });
  for (const column of REGISTER_COLUMNS) {
    assert.ok(html.includes(`>${column}<`), `${column} is declared and does not render`);
  }
});

test('the verification annex is mandatory and complete', () => {
  for (const field of ['recompute_recipe', 'signing_key', 'fetch_note']) {
    const partial = { ...VERIFICATION };
    delete partial[field];
    assert.throws(
      () => renderEvidenceBundle({ ...GOOD, verification: partial }),
      new RegExp(`needs ${field}`),
      `${field} could go missing`,
    );
  }
  const html = renderEvidenceBundle(GOOD);
  assert.ok(html.includes('How to verify this bundle'));
  assert.ok(html.includes('GET rather than HEAD'));
});

test('the watermark is the component words, on every bundle', () => {
  const html = renderEvidenceBundle(GOOD);
  assert.ok(
    html.includes(
      'Documentation. Consolidations have no legal effect. Authentic sources cited per item.',
    ),
  );
});

test('an official link goes through the one route policy', () => {
  for (const uri of ['https://evil.example/x', 'http://preview.invalid/x', 'not a url']) {
    assert.throws(
      () => renderEvidenceBundle({ ...GOOD, items: [{ ...ITEM, official_uri: uri }] }),
      /source URI/,
      `${uri} was cited as official`,
    );
  }
});

test('an empty bundle is refused rather than rendered as a cover sheet', () => {
  assert.throws(() => renderEvidenceBundle({ ...GOOD, items: [] }), /is a cover sheet/);
});

test('values are escaped rather than trusted', () => {
  const html = renderEvidenceBundle({
    ...GOOD,
    items: [{ ...ITEM, citation: '<img src=x onerror=alert(1)>' }],
  });
  assert.ok(!html.includes('<img'));
  assert.ok(html.includes('&lt;img'));
});

test('O5: the interval label is the publisher vocabulary, not this screen assuming one', () => {
  // An EU consolidation state exported under the word "applicable" is an applicability claim
  // the publisher never made, inside the artefact a reader keeps and cites.
  const lu = renderEvidenceBundle({ ...GOOD, semantics: 'publisher_applicability' });
  assert.equal(lu.includes('<dt>applicable</dt>'), true);
  assert.equal(lu.includes('<dt>consolidated wording</dt>'), false);

  const eu = renderEvidenceBundle({ ...GOOD, semantics: 'official_consolidation_state' });
  assert.equal(eu.includes('<dt>consolidated wording</dt>'), true);
  assert.equal(
    eu.includes('<dt>applicable</dt>'),
    false,
    'a consolidation state was exported as applicability',
  );
});

test('O5: a bundle without a declared vocabulary is refused', () => {
  for (const semantics of [undefined, null, '', 'in_force', 'publisher_applicability_v2']) {
    assert.throws(
      () => renderEvidenceBundle({ ...GOOD, semantics }),
      /renders in the publisher's own vocabulary/,
      `${JSON.stringify(semantics)} was accepted as a date vocabulary`,
    );
  }
});

test('O7: a licence requiring attribution refuses an item that carries none', () => {
  // The licence table declared two obligations and enforced one, so the bundle travelled with
  // the publisher's text and without the credit the publisher's own licence requires.
  for (const licence of ['cc-by-4.0', 'licence-scl']) {
    assert.throws(
      () =>
        renderEvidenceBundle({
          ...GOOD,
          items: [{ ...ITEM, licence, attribution: undefined }],
        }),
      /requires attribution, and carries none/,
      `${licence} was bundled without attribution`,
    );
    assert.throws(
      () => renderEvidenceBundle({ ...GOOD, items: [{ ...ITEM, licence, attribution: '   ' }] }),
      /requires attribution, and carries none/,
      `${licence} was bundled with blank attribution`,
    );
  }
  // cc0 requires none, so it must still be renderable without one.
  assert.equal(
    typeof renderEvidenceBundle({
      ...GOOD,
      items: [{ ...ITEM, licence: 'cc0', attribution: undefined }],
    }),
    'string',
  );
});
