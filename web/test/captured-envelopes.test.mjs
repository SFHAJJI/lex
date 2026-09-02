// The guards that make the captured fixtures evidence rather than my typing.
//
// An audit found the digest recomputation in `loadCaptured` unproven: the whole suite stayed
// green with it removed. That is the one guard whose absence changes what the fixtures mean.
// The file's own comment says the recomputation "is the entire reason these bytes came from a
// production run instead of from me", and a claim nothing holds is a claim.
//
// This is also the codebase's own lesson pointing at itself: a passing test is not
// automatically a real test, and the first tool snapshots here wrote one byte each and passed
// forever. So each assertion below was watched failing before it was trusted.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createHash } from 'node:crypto';

import {
  CAPTURED_NAMES,
  loadCaptured,
  verifyCapture,
} from '../scripts/captured-envelopes.mjs';

test('there are captured envelopes, and loading one is not trivial', () => {
  assert.ok(CAPTURED_NAMES.length > 0, 'nothing was captured');
  for (const name of CAPTURED_NAMES) {
    const decoded = loadCaptured(name);
    assert.equal(typeof decoded, 'object');
    assert.notEqual(decoded, null);
    // A capture that decodes to almost nothing proves nothing, and an empty baseline passes
    // forever. These are envelopes, so they carry the fields an envelope carries.
    assert.ok(Object.keys(decoded).length > 2, `${name} decoded to almost nothing`);
  }
});

test('a capture whose bytes changed is refused, not loaded', () => {
  // The distinction this guard exists for: without it, a fixture that changed silently and one
  // that was fabricated are indistinguishable.
  const text = '{"envelope":{"status":"ok"}}';
  const sha256 = createHash('sha256').update(Buffer.from(text, 'utf8')).digest('hex');
  const bytes = Buffer.byteLength(text, 'utf8');
  const good = { text, sha256, bytes };

  assert.equal(verifyCapture('probe', good), text);

  // One character, in a place JSON still parses, so nothing downstream would notice.
  const edited = text.replace('"ok"', '"OK"');
  assert.throws(
    () => verifyCapture('probe', { ...good, text: edited, bytes: Buffer.byteLength(edited) }),
    /does not match its recorded identity/,
    'an edited capture verified as though it were the captured bytes',
  );

  // And the constant beside the bytes is not trusted either, in both of its halves.
  assert.throws(
    () => verifyCapture('probe', { ...good, sha256: 'f'.repeat(64) }),
    /does not match its recorded identity/,
  );
  assert.throws(
    () => verifyCapture('probe', { ...good, bytes: bytes + 1 }),
    /does not match its recorded identity/,
    'the byte length is not checked',
  );
});

test('an envelope nobody captured is refused rather than returned empty', () => {
  assert.throws(() => loadCaptured('not-captured'), /no captured envelope named/);
  assert.throws(() => loadCaptured(undefined), /no captured envelope named/);
  // A prototype key is not a capture.
  assert.throws(() => loadCaptured('toString'), /no captured envelope named/);
});
