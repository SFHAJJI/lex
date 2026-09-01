import assert from 'node:assert/strict';
import test from 'node:test';

import { SHELLS, dossierUrl, parseObjectUrl, readingUrl, shellUrl } from '../scripts/urls.mjs';

// The live permalink observed on the running service, which is the shape these builders owe.
const LIVE =
  '/lu-legilux/loi-2002-08-02-n2/2007-09-01--99b621c38dec11dcd362c0db35d9e9c090e62613cc5c20b0727c0b30fd39ce66#art_1er__2';
const HASH = '99b621c38dec11dcd362c0db35d9e9c090e62613cc5c20b0727c0b30fd39ce66';

test('the reading URL reproduces the live permalink shape exactly', () => {
  const built = readingUrl({
    publisher: 'lu-legilux',
    work: 'loi-2002-08-02-n2',
    validFrom: '2007-09-01',
    hash: HASH,
    anchor: 'art_1er__2',
  });
  assert.equal(built, LIVE);
});

test('a reading URL without the state hash is refused', () => {
  assert.throws(
    () =>
      readingUrl({
        publisher: 'lu-legilux',
        work: 'loi-2002-08-02-n2',
        validFrom: '2007-09-01',
        hash: undefined,
      }),
    /the link can drift silently/,
  );
  assert.throws(
    () =>
      readingUrl({
        publisher: 'lu-legilux',
        work: 'loi-2002-08-02-n2',
        validFrom: '2007-09-01',
        hash: HASH.slice(0, 8),
      }),
    /64 hex character state hash/,
  );
});

test('the publisher anchor is used verbatim', () => {
  const built = readingUrl({
    publisher: 'lu-legilux',
    work: 'code-travail',
    validFrom: '2021-01-26',
    hash: HASH,
    anchor: 'art_L_121-6',
  });
  assert.ok(built.endsWith('#art_L_121-6'), 'the anchor was normalised');
  assert.ok(!built.includes('art-l-121-6'));
});

test('an object URL can never carry a shell prefix', () => {
  for (const shell of SHELLS) {
    assert.throws(
      () => dossierUrl({ publisher: shell, work: 'loi-2006-07-31-n2' }),
      /is a shell prefix/,
      `${shell} was accepted as a publisher`,
    );
  }
  // And a shell-prefixed path does not parse as an object, so it cannot round-trip in.
  assert.equal(parseObjectUrl('/ask/loi-2006-07-31-n2'), null);
  assert.equal(parseObjectUrl(`/w/lu-legilux/2007-09-01--${HASH}`), null);
});

test('shells apply to entry paths, not to object paths', () => {
  assert.equal(shellUrl('ask'), '/ask');
  assert.equal(shellUrl('w', '/search'), '/w/search');
  assert.throws(() => shellUrl('workbench'), /unknown shell/);
  assert.throws(() => shellUrl('ask', 'search'), /absolute path/);
});

test('every built URL parses back to what built it', () => {
  const reading = {
    publisher: 'eu-eurlex',
    work: '32016r0679',
    validFrom: '2016-05-04',
    hash: HASH,
    anchor: 'art_5',
  };
  const parsed = parseObjectUrl(readingUrl(reading));
  assert.deepEqual(parsed, { kind: 'reading', ...reading });

  const dossier = { publisher: 'lu-legilux', work: 'loi-2006-07-31-n2' };
  assert.deepEqual(parseObjectUrl(dossierUrl(dossier)), {
    kind: 'dossier',
    ...dossier,
    anchor: null,
  });
});

test('a malformed version key does not resolve to a guess', () => {
  assert.equal(parseObjectUrl('/lu-legilux/loi/2007-09-01--tooshort'), null);
  assert.equal(parseObjectUrl('/lu-legilux/loi/notadate--' + HASH), null);
  assert.equal(parseObjectUrl('/lu-legilux/loi/extra/segments/here'), null);
  assert.equal(parseObjectUrl('relative/path'), null);
});

test('a path traversal or separator in a segment is refused', () => {
  for (const bad of ['..', 'a/b', '', '.hidden']) {
    assert.throws(
      () => dossierUrl({ publisher: 'lu-legilux', work: bad }),
      /not a safe URL segment/,
      `${JSON.stringify(bad)} was accepted`,
    );
  }
});

test('an anchor containing a separator is refused rather than encoded away', () => {
  assert.throws(
    () =>
      readingUrl({
        publisher: 'lu-legilux',
        work: 'code-travail',
        validFrom: '2021-01-26',
        hash: HASH,
        anchor: 'art_5#art_6',
      }),
    /not a publisher anchor/,
  );
});
