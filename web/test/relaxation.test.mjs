import assert from 'node:assert/strict';
import test from 'node:test';

import {
  RELAXATIONS,
  renderRelaxationDisclosures,
  revertPath,
} from '../scripts/relaxation.mjs';

const SEARCH = '/ask/search?q=security+deposit+how+many+months&scope=lu';

const NONE = {
  fuzzy: { applied: false },
  crosswalk: { applied: false },
  semantic: { applied: false },
};

const ALL = {
  fuzzy: { applied: true, expansions: ['many -> mady', 'many -> man'] },
  crosswalk: {
    applied: true,
    understood_as: 'garantie locative',
    version: 'crosswalk/3',
    reviewed_on: '2026-08-15',
  },
  semantic: { applied: true, encoder: 'synthetic-encoder/1', benchmark: 'retrieval-bench/4' },
};

test('all three relaxations must be declared, because silence is not "off"', () => {
  for (const missing of RELAXATIONS) {
    const partial = { ...NONE };
    delete partial[missing];
    assert.throws(
      () => renderRelaxationDisclosures({ searchPath: SEARCH, relaxations: partial }),
      new RegExp(`${missing} must declare whether it was applied`),
      `${missing} could go undeclared`,
    );
  }
  assert.equal(renderRelaxationDisclosures({ searchPath: SEARCH, relaxations: NONE }), '');
});

test('a relaxation this interface cannot disclose is refused, not ignored', () => {
  // Adding a relaxation to the retrieval path without adding it here is how a silent one
  // ships: it would run, return different results, and disclose nothing.
  assert.throws(
    () =>
      renderRelaxationDisclosures({
        searchPath: SEARCH,
        relaxations: { ...NONE, stemming: { applied: true } },
      }),
    /is not a relaxation this interface can disclose/,
  );
});

test('fuzzy shows the expansions verbatim', () => {
  const html = renderRelaxationDisclosures({
    searchPath: SEARCH,
    relaxations: { ...NONE, fuzzy: ALL.fuzzy },
  });
  assert.ok(html.includes('many -&gt; mady'));
  assert.ok(html.includes('many -&gt; man'));

  // An applied fuzzy relaxation with nothing to show is a claim with no content.
  assert.throws(
    () =>
      renderRelaxationDisclosures({
        searchPath: SEARCH,
        relaxations: { ...NONE, fuzzy: { applied: true, expansions: [] } },
      }),
    /must list the expansions it applied, verbatim/,
  );
});

test('the crosswalk says it is editorial and not official, in the component words', () => {
  const html = renderRelaxationDisclosures({
    searchPath: SEARCH,
    relaxations: { ...NONE, crosswalk: ALL.crosswalk },
  });
  assert.ok(html.includes('Understood as: garantie locative'));
  assert.ok(html.includes('Editorial crosswalk, not official'));
  assert.ok(html.includes('crosswalk/3'));
  assert.ok(html.includes('reviewed 2026-08-15'));

  // The label is not a caller parameter, so it cannot be phrased away.
  for (const broken of [
    { applied: true, version: 'crosswalk/3', reviewed_on: '2026-08-15' },
    { applied: true, understood_as: 'x', reviewed_on: '2026-08-15' },
    { applied: true, understood_as: 'x', version: 'crosswalk/3' },
    { applied: true, understood_as: 'x', version: 'crosswalk/3', reviewed_on: '2026-99-99' },
  ]) {
    assert.throws(
      () => renderRelaxationDisclosures({ searchPath: SEARCH, relaxations: { ...NONE, crosswalk: broken } }),
      /is required|must carry its review date/,
      `${JSON.stringify(broken)} was disclosed`,
    );
  }
});

test('semantic ranking names the encoder and the benchmark that gates it', () => {
  const html = renderRelaxationDisclosures({
    searchPath: SEARCH,
    relaxations: { ...NONE, semantic: ALL.semantic },
  });
  assert.ok(html.includes('synthetic-encoder/1'));
  assert.ok(html.includes('retrieval-bench/4'));
  // The encoder and benchmark names are shown, because a reader who wants to check which
  // model ranked their results needs them. What must not appear is this screen vouching
  // for the outcome of a check it never saw: both values are caller-supplied strings, so
  // any caller could name any pair and the screen certified it.
  assert.ok(html.includes("does not carry that benchmark"));
  assert.ok(
    !html.includes('passing benchmark'),
    'the screen certified a benchmark result it does not hold',
  );
  assert.ok(
    !html.includes('serves only behind that gate'),
    'the screen asserted a deployment gate it cannot observe',
  );

  for (const broken of [
    { applied: true, benchmark: 'retrieval-bench/4' },
    { applied: true, encoder: 'synthetic-encoder/1' },
  ]) {
    assert.throws(
      () => renderRelaxationDisclosures({ searchPath: SEARCH, relaxations: { ...NONE, semantic: broken } }),
      /is required/,
      `${JSON.stringify(broken)} was disclosed`,
    );
  }
});

test('three applied relaxations are three blocks, not one merged banner', () => {
  const html = renderRelaxationDisclosures({ searchPath: SEARCH, relaxations: ALL });
  assert.equal(html.split('data-relaxation=').length - 1, 3);
  for (const relaxation of RELAXATIONS) {
    assert.ok(html.includes(`data-relaxation="${relaxation}"`), `${relaxation} lost its block`);
  }
  // Three reverts, one per block. Merging them would make undoing one undo all of them.
  assert.equal(html.split('class="relaxation-revert"').length - 1, 3);
});

test('each revert turns off exactly its own relaxation and leaves the rest alone', () => {
  assert.equal(
    revertPath(SEARCH, 'fuzzy'),
    '/ask/search?q=security+deposit+how+many+months&scope=lu&fuzzy=off',
  );
  assert.equal(
    revertPath(SEARCH, 'crosswalk'),
    '/ask/search?q=security+deposit+how+many+months&scope=lu&crosswalk=off',
  );
  assert.equal(
    revertPath(SEARCH, 'semantic'),
    '/ask/search?q=security+deposit+how+many+months&scope=lu&retrieval_mode=keyword',
  );

  // An existing setting is replaced, not appended twice.
  assert.equal(
    revertPath('/ask/search?q=x&fuzzy=auto', 'fuzzy'),
    '/ask/search?q=x&fuzzy=off',
  );
  // The query survives the round trip, which is the whole point of reverting.
  const reverted = new URL(revertPath(SEARCH, 'fuzzy'), 'https://example.invalid');
  assert.equal(reverted.searchParams.get('q'), 'security deposit how many months');
  assert.equal(reverted.searchParams.get('scope'), 'lu');
});

test('a revert needs a real search path and a real relaxation', () => {
  assert.throws(() => revertPath(SEARCH, 'stemming'), /is not a relaxation/);
  assert.throws(
    () => revertPath('search?q=x', 'fuzzy'),
    /needs the current same-origin search path/,
  );

  // O2. Every one of these begins with a slash or reads as a path, and the old guard was
  // exactly `startsWith('/')`. A protocol-relative URL is off-site and starts with a
  // slash, so the revert control offered a one-tap trip to another origin under a label
  // promising the reader their own words back.
  for (const hostile of [
    '//evil.example/ask/search?q=x',
    '/ask/search?q=x#fragment',
    '/evil/search?q=x',
    '/ask/search/extra?q=x',
    '/ask/search?q=x?y=z',
    'https://evil.example/ask/search?q=x',
    'javascript:alert(1)',
  ]) {
    assert.throws(
      () => revertPath(hostile, 'fuzzy'),
      /needs the current same-origin search path/,
      `${hostile} was accepted as a revert target`,
    );
  }

  // And the backslash form, written as a code point so no shell or editor can eat it.
  assert.throws(
    () => revertPath('/ask' + String.fromCharCode(92) + 'search?q=x', 'fuzzy'),
    /needs the current same-origin search path/,
  );
  // A fragment is refused by the same policy now, so the message is the policy's, not a
  // separate guard's. One rule, one place.
  assert.throws(
    () => revertPath('/ask/search?q=x#art_1', 'fuzzy'),
    /needs the current same-origin search path/,
  );
});

test('values are escaped rather than trusted', () => {
  const html = renderRelaxationDisclosures({
    searchPath: SEARCH,
    relaxations: {
      ...NONE,
      crosswalk: {
        applied: true,
        understood_as: '<img src=x onerror=alert(1)>',
        version: 'crosswalk/3',
        reviewed_on: '2026-08-15',
      },
    },
  });
  assert.ok(!html.includes('<img'));
  assert.ok(html.includes('&lt;img'));
});
