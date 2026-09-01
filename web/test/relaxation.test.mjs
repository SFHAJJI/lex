import assert from 'node:assert/strict';
import test from 'node:test';

import {
  RELAXATIONS,
  renderRelaxationDisclosures,
  requireRelaxationAccount,
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
  assert.ok(html.includes('serves only behind that gate'));

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

  // The control comes first. A refusal that also refuses the true case is not a check, and the
  // three shells are all legitimate here.
  for (const good of ['/ask/search?q=x', '/w/search', '/dev/search?q=x&fuzzy=on']) {
    assert.equal(typeof revertPath(good, 'fuzzy'), 'string', `${good} was refused`);
  }

  // A leading slash used to be the whole check, and `//evil.example/x` has one. This control is
  // the one a reader reaches for when they have stopped trusting what they are being shown, and
  // it promised them their own words back while sending them to another origin.
  for (const bad of [
    '//evil.example/steal',
    '/' + String.fromCharCode(92) + 'evil.example/steal',
    'https://evil.example/steal',
    'search?q=x',
    '/ask/read?q=x',
    '/ask/search?q=x?y=z',
    '/ask/search	?q=x',
  ]) {
    assert.throws(
      () => revertPath(bad, 'fuzzy'),
      /needs the current same-origin search path/,
      `${JSON.stringify(bad)} was accepted as a revert target`,
    );
  }

  assert.throws(() => revertPath('/ask/search?q=x#art_1', 'fuzzy'), /same-origin search path/);
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

test('the account is a contract a caller can check without rendering anything', () => {
  // The search screen has to know the account is whole before it can cross-check its badges
  // against it. Re-implementing the same checks there is how two versions of one contract start
  // to disagree, and the type check had already drifted into being the screen's alone.
  assert.equal(requireRelaxationAccount(NONE), NONE);

  // An account is an object with one entry per relaxation. An array has no entries to read.
  for (const bad of [undefined, null, [], 'off', 0, NONE.fuzzy.applied]) {
    assert.throws(
      () => requireRelaxationAccount(bad),
      /an absent set is not "none ran"/,
      `${JSON.stringify(bad)} was read as an account`,
    );
  }

  // Complete: a relaxation the caller did not mention is a relaxation this screen cannot
  // disclose, which is a different fact from one that did not run.
  for (const relaxation of RELAXATIONS) {
    const partial = { ...NONE };
    delete partial[relaxation];
    assert.throws(
      () => requireRelaxationAccount(partial),
      new RegExp(`${relaxation} must declare whether it was applied`),
      `${relaxation} could go missing`,
    );
  }

  // Closed: adding one to the retrieval path without adding it here is how a silent relaxation
  // ships. Including the keys every object answers for, which a prototype lookup would accept.
  for (const extra of ['rerank', 'constructor', 'toString']) {
    assert.throws(
      () => requireRelaxationAccount({ ...NONE, [extra]: { applied: false } }),
      /is not a relaxation this interface can disclose/,
      `${extra} was accepted as a relaxation`,
    );
  }

  // And the renderer holds the same contract, because it asks this function rather than
  // repeating it.
  assert.throws(
    () => renderRelaxationDisclosures({ searchPath: SEARCH, relaxations: [] }),
    /an absent set is not "none ran"/,
  );
});
