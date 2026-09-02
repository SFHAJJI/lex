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
  // D38's gate. An evidence bundle is a public surface, so the record has to state whether the
  // publisher's text may travel at all, separately from what the licence permits.
  text_public: true,
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

const GOOD = { items: [ITEM], columns: COLUMNS, verification: VERIFICATION };

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

  // cc0 waives the attribution obligation. It does not waive provenance: a body that travels
  // names its source, because a bundle exists to be checked against the publisher and a
  // quotation with nobody named cannot be checked against anything.
  const cc0 = renderEvidenceBundle({ ...GOOD, items: [{ ...ITEM, licence: 'cc0' }] });
  assert.ok(cc0.includes('bundle-attribution'), 'a cc0 body travelled with no source named');
  assert.throws(
    () =>
      renderEvidenceBundle({
        ...GOOD,
        items: [{ ...ITEM, licence: 'cc0', attribution: undefined }],
      }),
    /names no attribution/,
    'a cc0 item exported a body with no source named',
  );
  // A body that does not travel needs no attribution line, whatever the licence waives.
  assert.ok(
    !renderEvidenceBundle({
      ...GOOD,
      items: [{ ...ITEM, licence: 'cc0', text_public: false }],
    }).includes('bundle-attribution'),
  );

  assert.throws(
    () => renderEvidenceBundle({ ...GOOD, items: [{ ...ITEM, licence: 'ask-me' }] }),
    /can only do that for a licence it knows/,
  );
  assert.ok(!LICENCES['licence-scl'].embedsText);
});

test('D38 decides whether a body travels, not the caller-supplied licence label', () => {
  // The composer used to ask the licence label whether the text could travel, so a caller who
  // wrote `cc0` over a Legilux item exported the publisher's text on the strength of their own
  // label. The licence is a term of the grant; text_public is whether the grant was established
  // with recorded evidence, and only the second can unlock a body.
  const closed = renderEvidenceBundle({
    ...GOOD,
    items: [{ ...ITEM, text_public: false }],
  });
  assert.ok(!closed.includes('no legal authority'), 'a closed gate exported the body anyway');
  assert.ok(closed.includes('text gate has not cleared'));
  // And it says which of the two gates refused, because one may change tomorrow and the other
  // will not.
  assert.ok(!closed.includes('Text withheld by licence'), 'two different refusals read as one');

  // Absence is refused rather than read as false. A closed gate and an unstated one are
  // different facts, and reading the second as the first hides a payload defect behind a
  // correct-looking refusal.
  for (const stated of [undefined, null, 'true', 1, '']) {
    assert.throws(
      () => renderEvidenceBundle({ ...GOOD, items: [{ ...ITEM, text_public: stated }] }),
      /does not carry text_public/,
      `text_public=${JSON.stringify(stated)} was composed anyway`,
    );
  }
});

test('an item is labelled in its own publisher terms, and a bundle may span publishers', () => {
  const EU_ITEM = {
    ...ITEM,
    identifier: 'eu-eurlex:32016R0679:2016-05-04',
    publisher: 'eu-eurlex',
    official_uri: 'https://eur-lex.europa.eu/eli/reg/2016/679/2016-05-04',
  };
  const html = renderEvidenceBundle({ ...GOOD, items: [ITEM, EU_ITEM], columns: COLUMNS });
  assert.ok(html.includes('<dt>applicable</dt>'), 'the Luxembourg item lost its own word');
  assert.ok(html.includes('<dt>consolidated wording</dt>'), 'the Union item was called applicable');

  // There is no bundle-wide vocabulary to pass, and passing one is refused rather than ignored.
  for (const declared of ['publisher_applicability', 'official_consolidation_state', 'toString']) {
    assert.throws(
      () => renderEvidenceBundle({ ...GOOD, semantics: declared }),
      /does not take a date vocabulary/,
      `${declared} was accepted as a bundle vocabulary`,
    );
  }
});

test('the declared publisher and the identifier must name one publisher', () => {
  // The official-source host check runs against the declared value, so a disagreement here
  // decides whose official hosts this item may link to.
  assert.throws(
    () =>
      renderEvidenceBundle({
        ...GOOD,
        items: [{ ...ITEM, identifier: 'eu-eurlex:32016R0679:2016-05-04' }],
      }),
    /while its identifier names/,
    'an item linked to one publisher under another publisher name',
  );
  for (const identifier of ['garbage', 'preview-synthetic:work', '', undefined]) {
    assert.throws(
      () => renderEvidenceBundle({ ...GOOD, items: [{ ...ITEM, identifier }] }),
      /does not name a publisher, a work and a state/,
      `identifier=${JSON.stringify(identifier)} was bundled`,
    );
  }
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
  assert.ok(html.includes('<dt>applicable</dt>'));
  assert.ok(html.includes('<dt>published</dt>'));
  assert.ok(html.includes('<dt>observed</dt>'));
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

test('an exported period runs forwards, and the open sentinel is not a date', () => {
  // A bundle is the artefact that leaves the room and is read by people who were not here.
  for (const [valid_to, pattern] of [
    ['2001-01-01', /ends on or before it begins/],
    ['2000-01-01', /ends on or before it begins/],
    ['9999-12-31', /exports a law that ceases in the year 9999/],
  ]) {
    assert.throws(
      () => renderEvidenceBundle({ ...GOOD, items: [{ ...ITEM, valid_to }] }),
      pattern,
      `valid_to=${valid_to} was exported as an applicability period`,
    );
  }
  // An open interval ends in null, which is the shape that survives.
  assert.ok(renderEvidenceBundle({ ...GOOD, items: [{ ...ITEM, valid_to: null }] }).includes('no end recorded'));
});

test('an item whose text may travel and carries none is refused', () => {
  // Otherwise an empty quotation sits beneath a citation, a digest and an official link, which
  // reads as text this bundle holds.
  for (const text of [undefined, '', '   ']) {
    assert.throws(
      () => renderEvidenceBundle({ ...GOOD, items: [{ ...ITEM, text }] }),
      /carries none; an empty quotation/,
      `text=${JSON.stringify(text)} produced an empty quotation`,
    );
  }
  // Neither gate had a body to carry, so neither demands one. Asked of the licence alone, this
  // also demanded text from an item whose rights gate is closed.
  assert.ok(
    renderEvidenceBundle({
      ...GOOD,
      items: [{ ...ITEM, licence: 'licence-scl', text: undefined }],
    }).includes('Text withheld by licence'),
  );
  assert.ok(
    renderEvidenceBundle({
      ...GOOD,
      items: [{ ...ITEM, text_public: false, text: undefined }],
    }).includes('text gate has not cleared'),
  );
});
