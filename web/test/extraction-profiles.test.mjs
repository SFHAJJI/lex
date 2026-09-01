import assert from 'node:assert/strict';
import test from 'node:test';
import { readdirSync, readFileSync } from 'node:fs';

import {
  DESCRIBED_PROFILES,
  UNDESCRIBED_NOTE,
  describeProfile,
  profileNote,
} from '../scripts/extraction-profiles.mjs';

test('a described profile returns its own note', () => {
  const described = describeProfile('akn-lu/1');
  assert.equal(described.described, true);
  assert.match(described.note, /^the publisher's own XML/);
  assert.equal(described.profile, 'akn-lu/1');
});

test('two profiles do not share a note', () => {
  // akn-lu/1 and akn-lu/2 differ by one real behaviour, the empty structural placeholders. A
  // table that gave them one sentence would be describing a distinction it had erased.
  assert.notEqual(profileNote('akn-lu/1'), profileNote('akn-lu/2'));
  assert.match(profileNote('akn-lu/2'), /structural placeholders/);
});

test('an undescribed profile states the absence and is flagged, never silently blank', () => {
  const described = describeProfile('akn-lu/3');
  assert.equal(described.described, false);
  assert.equal(described.note, UNDESCRIBED_NOTE);
  assert.equal(described.profile, 'akn-lu/3');
  // The note must say it is a gap in the page, not a fact about the text. Pin the subject so a
  // rewrite of the second half cannot leave this passing on the wrong claim.
  assert.ok(UNDESCRIBED_NOTE.startsWith('This build carries no description'));
  assert.match(UNDESCRIBED_NOTE, /gap\s+in this page, not a statement about the text/);
});

test('an inherited property is not a profile', () => {
  // The table is Object.create(null) precisely so these return null rather than finding
  // Object.prototype members and reporting a profile that does not exist.
  for (const name of ['constructor', 'toString', 'hasOwnProperty', '__proto__']) {
    assert.equal(profileNote(name), null, `${name} was treated as a profile`);
    assert.equal(describeProfile(name).described, false);
  }
});

test('a missing or empty profile is undescribed rather than throwing', () => {
  for (const value of [undefined, null, '', 0, {}]) {
    assert.equal(profileNote(value), null);
  }
});

test('the described set is non-trivial and sorted', () => {
  assert.ok(DESCRIBED_PROFILES.length >= 4, 'the table is too small to be real');
  assert.deepEqual([...DESCRIBED_PROFILES].sort(), [...DESCRIBED_PROFILES]);
});

test('every profile a V3 surface renders as provenance is one this table describes', () => {
  // The point of the file. Asserting against a hand-written copy of the same list would prove
  // nothing, so this reads the profiles the surfaces actually carry and requires each to be
  // described. A new profile in a fixture, a preview or a renderer fails here, offline, rather
  // than reaching a reader as an identifier with silence beside it.
  //
  // Scanned: singular `extraction_profile` and `profile` assignments, which are the field the
  // timeline table and the reading view render as "how this text was obtained".
  //
  // Not scanned: the plural `profiles` array in a `profiles_differ` refusal payload. That names
  // the two profiles that disagree, and the card's claim is that they cannot be compared, not
  // that either describes how a text was obtained. Its fixtures are deliberately synthetic
  // (`synthetic-pdf/1`), and requiring a provenance sentence for a synthetic value would force
  // this table to describe extractions that never happened.
  //
  // Not scanned either: `test/`. Test fixtures deliberately carry invalid profiles, including
  // `<img src=x onerror=alert(1)>`, to prove the renderers escape them. Those are inputs chosen
  // to be refused, not values a surface claims provenance for, and demanding a description for
  // an attack string would invert what that fixture exists to prove. The shipping surfaces are
  // `scripts/` and `app/`, and the preview pages live in `scripts/*-preview.mjs`, so what
  // actually reaches a reader is covered.
  const roots = ['scripts', 'app'];
  const found = new Set();
  for (const root of roots) {
    const dir = new URL(`../${root}/`, import.meta.url);
    for (const name of readdirSync(dir)) {
      if (!/\.(mjs|jsx)$/.test(name)) continue;
      if (name === 'extraction-profiles.mjs') continue;
      // Read through the URL rather than converting it to a path. `pathname` is `/C:/...`
      // on Windows and `/home/...` on Linux, so slicing the leading slash happens to work on
      // one and produces a relative path on the other. This passed here and failed CI, which
      // is the whole reason CI runs somewhere else.
      const source = readFileSync(new URL(name, dir), 'utf8');
      for (const match of source.matchAll(
        /\b(?:extraction_profile|profile)\s*:\s*['"]([^'"]+)['"]/g,
      )) {
        found.add(match[1]);
      }
    }
  }
  assert.ok(found.size > 0, 'found no profile literals at all, so this test proves nothing');
  assert.ok(
    found.has('akn-lu/1') && found.has('xhtml-eu/1'),
    `the scan missed profiles known to be present: found ${[...found].join(', ')}`,
  );
  const undescribed = [...found].filter((profile) => profileNote(profile) === null);
  assert.deepEqual(
    undescribed,
    [],
    `V3 surfaces render ${undescribed.join(', ')} with no description in extraction-profiles.mjs`,
  );
});
