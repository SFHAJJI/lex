import assert from 'node:assert/strict';
import test from 'node:test';

import {
  COMPARE_MODES,
  RENUMBER_LABEL,
  renderCompare,
  renderResolutionHeader,
} from '../scripts/compare.mjs';

const LEFT_DIGEST = '1111111111111111111111111111111111111111111111111111111111111111';
const RIGHT_DIGEST = '2222222222222222222222222222222222222222222222222222222222222222';

const LEFT = {
  lex_id: 'preview-synthetic:synthetic-preview-work:2001-01-01',
  valid_from: '2001-01-01',
  valid_to: '2002-01-01',
  publication_date: '2000-12-01',
  observed_from: '2026-01-01T00:00:00Z',
  body_sha256: LEFT_DIGEST,
  language: 'fr',
  profile: 'akn-lu/1',
  legal_time_sentence: 'Applicable from 2001-01-01 to 2002-01-01 (publisher)',
};

const RIGHT = {
  ...LEFT,
  lex_id: 'preview-synthetic:synthetic-preview-work:2002-01-01',
  valid_from: '2002-01-01',
  valid_to: null,
  publication_date: '2001-12-01',
  body_sha256: RIGHT_DIGEST,
  legal_time_sentence: 'Applicable from 2002-01-01 (publisher)',
};

const CHANGED = {
  changed: true,
  blocks: [{ anchor_label: 'Art. 1', removed: 'the old wording', added: 'the new wording' }],
  renumbering: [{ from: 'art_1', to: 'art_1bis' }],
};

const GOOD = { mode: 'temporal', left: LEFT, right: RIGHT, result: CHANGED };

test('the resolution header renders before the panes and carries both clocks', () => {
  const html = renderCompare(GOOD);
  const header = html.indexOf('compare-resolution');
  const panes = html.indexOf('compare-blocks');
  assert.ok(header !== -1 && panes !== -1);
  assert.ok(header < panes, 'the panes preceded the header');

  for (const side of [LEFT, RIGHT]) {
    assert.ok(html.includes(side.lex_id), 'a compared state is not named');
    assert.ok(html.includes(side.body_sha256), 'a body digest is missing from its card');
    assert.ok(html.includes(side.legal_time_sentence), 'legal time is missing');
    assert.ok(html.includes(`Published ${side.publication_date}`), 'record time is missing');
  }
  assert.ok(html.includes('First observed 2026-01-01T00:00:00Z'));
});

test('a side that cannot be fully resolved is not compared', () => {
  for (const [field, value, pattern] of [
    ['lex_id', '', /has no lex_id/],
    ['valid_from', '2001-13-01', /valid_from is not a calendar date/],
    ['publication_date', undefined, /publication_date is not a calendar date/],
    ['observed_from', '2026-01-01', /observed_from is not a UTC instant/],
    ['body_sha256', 'short', /has no body digest/],
    ['language', '', /does not say which language/],
    ['legal_time_sentence', '  ', /needs its legal-time sentence/],
  ]) {
    assert.throws(
      () => renderCompare({ ...GOOD, left: { ...LEFT, [field]: value } }),
      pattern,
      `left ${field}=${String(value)} was compared anyway`,
    );
    assert.throws(
      () => renderCompare({ ...GOOD, right: { ...RIGHT, [field]: value } }),
      pattern,
      `right ${field}=${String(value)} was compared anyway`,
    );
  }
  assert.throws(() => renderResolutionHeader({ left: LEFT, right: {} }), /has no lex_id/);
});

test('two profiles that differ replace the panes and cannot be overridden', () => {
  const crossed = {
    ...GOOD,
    right: { ...RIGHT, profile: 'pdf-lu/1' },
    // Anything a caller might reach for. None of them is read.
    force: true,
    override: true,
    allow_cross_profile: true,
  };
  const html = renderCompare(crossed);

  assert.ok(html.includes('profiles_differ'), 'the machine code is not shown');
  assert.ok(html.includes('akn-lu/1') && html.includes('pdf-lu/1'), 'both profiles must be named');
  assert.ok(html.includes('Not overridable'));
  assert.ok(!html.includes('compare-blocks'), 'panes rendered under a profiles_differ refusal');
  assert.ok(!html.includes('the new wording'), 'diff content leaked past the refusal');
  // The header still renders: a reader is entitled to see which pair was refused.
  assert.ok(html.includes(LEFT.lex_id) && html.includes(RIGHT.lex_id));
});

test('profiles_differ needs both profiles known, not one', () => {
  // One unknown profile is not a disagreement between two extractors, so it is not this
  // refusal. It is also not nothing: the comparison says it could not be checked.
  const html = renderCompare({ ...GOOD, right: { ...RIGHT, profile: undefined } });
  assert.ok(!html.includes('compare-refused'), 'an unknown profile was reported as a difference');
  assert.ok(!html.includes('Not overridable'), 'the refusal sentence rendered');
  assert.ok(html.includes('does not record its extraction profile'));
  assert.ok(html.includes('compare-blocks'), 'the comparison should still render');

  const known = renderCompare(GOOD);
  assert.ok(!known.includes('does not record its extraction profile'));
});

test('a temporal comparison cannot cross languages', () => {
  assert.throws(
    () => renderCompare({ ...GOOD, right: { ...RIGHT, language: 'de' } }),
    /cannot cross languages/,
    'a translation would have been rendered as an amendment',
  );
});

test('a language comparison must be two languages of one state', () => {
  const state = { ...LEFT, profile: 'xhtml-eu/1' };
  const en = { ...state, language: 'en', body_sha256: LEFT_DIGEST };
  const fr = { ...state, language: 'fr', body_sha256: RIGHT_DIGEST };

  const html = renderCompare({ mode: 'language', left: en, right: fr, result: CHANGED });
  assert.ok(html.includes('Language comparison, same state'));
  assert.ok(html.includes('Nothing here is a change over time'));
  assert.ok(html.includes(LEFT_DIGEST) && html.includes(RIGHT_DIGEST), 'separate hashes');

  assert.throws(
    () => renderCompare({ mode: 'language', left: en, right: { ...en }, result: CHANGED }),
    /needs two different languages/,
  );
  assert.throws(
    () =>
      renderCompare({
        mode: 'language',
        left: en,
        right: { ...fr, valid_from: '2005-01-01' },
        result: CHANGED,
      }),
    /these two cover different periods/,
    'change over time was labelled a language difference',
  );

  // Same dates is not same state. Two unrelated works that happen to share validity dates
  // would have rendered as two authentic expressions of each other.
  assert.throws(
    () =>
      renderCompare({
        mode: 'language',
        left: en,
        right: { ...fr, lex_id: 'preview-synthetic:a-different-work:2001-01-01' },
        result: CHANGED,
      }),
    /two different works/,
  );
});

test('a comparison must declare its axis', () => {
  assert.deepEqual([...COMPARE_MODES], ['temporal', 'language']);
  assert.throws(() => renderCompare({ ...GOOD, mode: undefined }), /must declare its axis/);
  assert.throws(() => renderCompare({ ...GOOD, mode: 'semantic' }), /must declare its axis/);
});

test('an unchanged result renders the publisher note verbatim and builds no panes', () => {
  const note = 'the same version applied on both dates';
  // Both dates resolving to one state means one state: same identifier, same digest.
  const SAME = { ...GOOD, right: { ...LEFT }, result: { changed: false, note } };
  const html = renderCompare(SAME);
  assert.ok(html.includes(`changed: false. ${note}`), 'the note is not verbatim');
  assert.ok(!html.includes('compare-blocks'), 'empty panes rendered for an unchanged result');
  assert.ok(html.includes('This is the answer, not an empty result'));
  assert.ok(html.includes(LEFT.lex_id), 'the covering state is still named');

  assert.throws(
    () => renderCompare({ ...SAME, result: { changed: false } }),
    /must not compose its own sentence/,
  );
});

test('the unchanged answer is bound to the records, not to the caller flag', () => {
  // Both directions were reachable and both are the worst thing this screen can do. Two
  // byte-identical states rendered red and green change blocks; two different states rendered
  // the publisher's "nothing changed" note over a header naming both of them.
  assert.throws(
    () => renderCompare({ ...GOOD, right: { ...LEFT }, result: CHANGED }),
    /a diff here would be invented/,
    'a diff was invented between one state and itself',
  );
  assert.throws(
    () =>
      renderCompare({
        ...GOOD,
        result: { changed: false, note: 'the same version applied on both dates' },
      }),
    /printed over evidence against it/,
    'the publisher note was printed over two different states',
  );

  // The digest alone is enough to make them different states, and so is the identifier.
  assert.throws(
    () =>
      renderCompare({
        ...GOOD,
        right: { ...LEFT, body_sha256: RIGHT_DIGEST },
        result: { changed: false, note: 'x' },
      }),
    /printed over evidence against it/,
  );
  assert.throws(
    () =>
      renderCompare({
        ...GOOD,
        right: { ...LEFT, lex_id: 'preview-synthetic:synthetic-preview-work:2099-01-01' },
        result: { changed: false, note: 'x' },
      }),
    /printed over evidence against it/,
  );
});

test('an extraction profile is a string or it is absent', () => {
  // It used to be "a non-empty string, or anything else means unknown", so a renamed upstream
  // field turned a non-overridable refusal into a rendered diff with a caveat above it.
  for (const profile of [new String('pdf-lu/1'), 1, { id: 'pdf-lu/1' }, ['pdf-lu/1'], true]) {
    assert.throws(
      () => renderCompare({ ...GOOD, right: { ...RIGHT, profile } }),
      /neither a profile nor an absent one/,
      `${String(profile)} was read as an unknown profile`,
    );
  }
  // Absent stays absent, which is the one case that legitimately means unknown.
  for (const profile of [undefined, null]) {
    const html = renderCompare({ ...GOOD, right: { ...RIGHT, profile } });
    assert.ok(html.includes('does not record its extraction profile'));
  }
});

test('a change block carries text or nothing, never a shape', () => {
  for (const value of [[], {}, false, 0, ['a'], '   ']) {
    assert.throws(
      () =>
        renderCompare({
          ...GOOD,
          result: { changed: true, blocks: [{ anchor_label: 'Art. 1', added: value }] },
        }),
      /is blank, which renders as a change that is not there|reached the page as/,
      `${JSON.stringify(value)} rendered as a change`,
    );
  }
});

test('a renumber row names two anchors and neither is blank', () => {
  for (const row of [{ from: '', to: 'art_2' }, { from: 'art_1', to: '  ' }, { from: 'art_1' }]) {
    assert.throws(
      () => renderCompare({ ...GOOD, result: { ...CHANGED, renumbering: [row] } }),
      /needs its (from|to) anchor/,
      `${JSON.stringify(row)} carried the mechanical label over nothing`,
    );
  }
});

test('a result with no verdict, or a changed result with no changes, is refused', () => {
  assert.throws(() => renderCompare({ ...GOOD, result: {} }), /must say whether it changed/);
  assert.throws(
    () => renderCompare({ ...GOOD, result: { changed: 'yes', blocks: CHANGED.blocks } }),
    /must say whether it changed/,
  );
  assert.throws(
    () => renderCompare({ ...GOOD, result: { changed: true, blocks: [] } }),
    /would render two empty panes/,
  );
  assert.throws(
    () =>
      renderCompare({
        ...GOOD,
        // Both absent, which is the case this guard is for; a blank string is refused
        // earlier, by the rule about shapes that render as a change.
        result: { changed: true, blocks: [{ anchor_label: 'Art. 1' }] },
      }),
    /neither a removal nor an addition/,
  );
  assert.throws(
    () =>
      renderCompare({
        ...GOOD,
        result: { changed: true, blocks: [{ removed: 'x', added: 'y' }] },
      }),
    /does not say which provision/,
  );
});

test('renumbering says it was detected mechanically', () => {
  const html = renderCompare(GOOD);
  assert.ok(html.includes(RENUMBER_LABEL));
  assert.equal(
    RENUMBER_LABEL,
    'renumbering detected mechanically by identical text hash, not publisher-asserted',
  );
  assert.ok(html.includes('art_1bis'));

  // No renumbering, no section, and no label claiming there was.
  const plain = renderCompare({ ...GOOD, result: { ...CHANGED, renumbering: [] } });
  assert.ok(!plain.includes(RENUMBER_LABEL));
  assert.ok(!plain.includes('compare-renumbering'));
});

test('a change block reads linearly and does not rely on colour', () => {
  const html = renderCompare(GOOD);
  assert.ok(html.includes('In Art. 1:'));
  assert.ok(html.includes('<span class="visually-hidden">removed: </span>'));
  assert.ok(html.includes('<span class="visually-hidden">added: </span>'));
  assert.ok(html.includes('<del>the old wording</del>'), 'removal is not struck through');
  assert.ok(html.includes('<ins>the new wording</ins>'), 'addition is not underlined');
});

test('one side refusing keeps the other and renders no comparison', () => {
  const refusal = {
    refusal: {
      code: 'no_version_for_date',
      sentence: 'No publisher state covers 1990-01-01.',
      payload: {
        nearest_earlier: null,
        nearest_later: '2001-01-01',
        history_begins: '2001-01-01',
        what_would_answer: ['corrected_identifier', 'new_official_observation'],
        asserts_absence_of_law: false,
      },
    },
  };
  const html = renderCompare({ ...GOOD, left: refusal });

  assert.ok(html.includes('no_version_for_date'), 'the refusal is not shown');
  assert.ok(html.includes(RIGHT.lex_id), 'the healthy side was dropped');
  assert.ok(html.includes(RIGHT.body_sha256));
  assert.ok(!html.includes('compare-blocks'), 'a comparison rendered from half a resolution');
  assert.ok(html.includes('One side of this comparison did not resolve'));

  // And the mirror, so neither side is the special one.
  const other = renderCompare({ ...GOOD, right: refusal });
  assert.ok(other.includes('no_version_for_date'));
  assert.ok(other.includes(LEFT.lex_id));
  assert.ok(!other.includes('compare-blocks'));
});

test('values are escaped rather than trusted', () => {
  const html = renderCompare({
    ...GOOD,
    result: {
      changed: true,
      blocks: [{ anchor_label: '<img src=x onerror=alert(1)>', added: 'x' }],
      renumbering: [],
    },
  });
  assert.ok(!html.includes('<img'));
  assert.ok(html.includes('&lt;img'));
});

test('O3: a comparison binds both sides to one canonical work, on either axis', () => {
  // The identity check lived inside the language branch, so the temporal path walked past it.
  // Every other guard passes here: same publisher, same language, same profile, different
  // periods, different digests. Only the work differs, and nothing looked.
  assert.throws(
    () => renderCompare({ ...GOOD, right: { ...RIGHT, lex_id: 'preview-synthetic:a-different-work:2002-01-01' } }),
    /two different works/,
    'a temporal diff rendered between two unrelated instruments',
  );

  // Cross-publisher is the same falsehood with a wider gap.
  assert.throws(
    () => renderCompare({ ...GOOD, right: { ...RIGHT, lex_id: 'eu-eurlex:some-regulation:2002-01-01' } }),
    /two different works/,
    'a temporal diff rendered across two publishers',
  );

  // And the work halves must be real segments rather than any two strings that happen to
  // match: slicing made `garbage` and `garbage` name the same work.
  for (const lexId of ['garbage', 'preview-synthetic:only-two-parts', '::2001-01-01']) {
    assert.throws(
      () =>
        renderCompare({
          mode: 'temporal',
          left: { ...LEFT, lex_id: lexId },
          right: { ...RIGHT, lex_id: lexId },
          result: CHANGED,
        }),
      /does not name a publisher, a work and a state/,
      `${lexId} was accepted as binding a canonical work`,
    );
  }
});

test('O7-R1: a language comparison does not certify both texts', () => {
  // "Both texts are authentic" was printed on the strength of being asked to compare them.
  // Decision 58 binds authenticity to the exact resource and nothing here carries that.
  const html = renderCompare({
    mode: 'language',
    left: { ...LEFT, language: 'fr' },
    right: { ...LEFT, language: 'de', body_sha256: RIGHT_DIGEST },
    result: CHANGED,
  });
  assert.equal(html.includes('Language comparison, same state'), true);
  assert.equal(
    html.toLowerCase().includes('authentic'),
    false,
    'the comparison certified two texts it holds no authenticity evidence for',
  );
});
