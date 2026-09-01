import assert from 'node:assert/strict';
import test from 'node:test';

import {
  HASH_KINDS,
  renderEnvelopeStrip,
  renderVerifyCluster,
} from '../scripts/verify-cluster.mjs';

const DIGEST = 'ad5dace9bd5116f493fe3035fd01546284e24dd2057a15f8d6be95eba8f82cf0';
const SOURCE = 'https://legilux.public.lu/eli/etat/leg/loi/2002/08/02/n2/consolide/20070901/fr';
const LEX_ID = 'lu-legilux:loi-2002-08-02-n2:2007-09-01--99b621c3';

const ENVELOPE = {
  publisher_name: 'Service central de législation (Legilux)',
  timeline_semantics: 'publisher_applicability',
  freshness: { built_at: '2026-08-15T09:22:08Z', stamp_signature_valid: true },
  artifact: {
    corpus_commit: 'c087f9153a8cde5429965ffa897db001f3acdf09',
    code_commit: '27f0e02cb0da8e0fdf9f8322d3eef3b3ae09c776',
    manifest_set_id: '4dff34d9e957d469e87ca2b1dbe0e74b5a85519da3631b37ddf2ea81d3553b59',
    content_digest: 'c064f74a9827d610125d25c999f79df626cd987432aa110f2e05ce48388b5eef',
  },
};

test('the copy control carries the whole digest, not the eight shown', () => {
  const html = renderVerifyCluster({
    sourceUri: SOURCE,
    lexId: LEX_ID,
    hash: { kind: 'record_sha256', value: DIGEST },
  });
  assert.ok(html.includes(`data-copy="${DIGEST}"`), 'the copy control lost the full digest');
  assert.ok(html.includes('>ad5dace9<'), 'the chip should show the first eight characters');
  // A truncated digest in a citation cannot be verified by anyone.
  assert.ok(!html.includes('data-copy="ad5dace9"'), 'the copy control copied the truncation');
});

test('a chip must name which digest it shows', () => {
  for (const kind of HASH_KINDS) {
    const html = renderVerifyCluster({
      sourceUri: SOURCE,
      lexId: LEX_ID,
      hash: { kind, value: DIGEST },
    });
    assert.ok(html.includes(kind), `${kind} was not named on the chip`);
  }
  assert.throws(
    () =>
      renderVerifyCluster({
        sourceUri: SOURCE,
        lexId: LEX_ID,
        hash: { kind: 'sha256', value: DIGEST },
      }),
    /must name which digest/,
  );
});

test('a digest that is not 64 lowercase hex is refused', () => {
  for (const bad of [DIGEST.toUpperCase(), DIGEST.slice(0, 63), `${DIGEST}0`, 'not-a-digest']) {
    assert.throws(
      () =>
        renderVerifyCluster({
          sourceUri: SOURCE,
          lexId: LEX_ID,
          hash: { kind: 'body_sha256', value: bad },
        }),
      /64 lowercase hex/,
    );
  }
});

test('the official source anchor points at the publisher', () => {
  const html = renderVerifyCluster({
    sourceUri: SOURCE,
    lexId: LEX_ID,
    hash: { kind: 'record_sha256', value: DIGEST },
  });
  assert.ok(html.includes(`href="${SOURCE}"`));
  assert.throws(
    () =>
      renderVerifyCluster({
        sourceUri: 'not a url',
        lexId: LEX_ID,
        hash: { kind: 'record_sha256', value: DIGEST },
      }),
    /is not a URL/,
  );
});

test('an invalid stamp is not rendered like a valid one', () => {
  const good = renderEnvelopeStrip({ envelope: ENVELOPE });
  assert.ok(good.includes('stamp valid'));
  assert.ok(!good.includes('NOT valid'));

  const bad = renderEnvelopeStrip({
    envelope: {
      ...ENVELOPE,
      freshness: { ...ENVELOPE.freshness, stamp_signature_valid: false },
    },
  });
  assert.ok(bad.includes('stamp NOT valid'));
  // It takes the conflict token, so it carries an icon and a label rather than a colour.
  assert.ok(bad.includes('token-icon'));
  assert.ok(bad.includes('token-label'));
  assert.notEqual(good, bad);
});

test('an absent signature verdict is refused rather than shown as valid', () => {
  assert.throws(
    () =>
      renderEnvelopeStrip({
        envelope: { ...ENVELOPE, freshness: { built_at: '2026-08-15T09:22:08Z' } },
      }),
    /an absent signature verdict is not the same/,
  );
});

test('the strip carries the four identity fields, and names the ones it lacks', () => {
  const full = renderEnvelopeStrip({ envelope: ENVELOPE });
  for (const field of ['corpus_commit', 'code_commit', 'manifest_set_id', 'content_digest']) {
    assert.ok(full.includes(field), `${field} missing from the strip`);
  }

  const sparse = renderEnvelopeStrip({ envelope: { ...ENVELOPE, artifact: {} } });
  assert.equal(sparse.split('not recorded').length - 1, 4, 'absent fields were silently dropped');
});

test('a missing build time is stated rather than omitted', () => {
  const html = renderEnvelopeStrip({
    envelope: { ...ENVELOPE, freshness: { stamp_signature_valid: true } },
  });
  assert.ok(html.includes('index build time not recorded'));
});

test('values are escaped rather than trusted', () => {
  const html = renderEnvelopeStrip({
    envelope: { ...ENVELOPE, publisher_name: '<img src=x onerror=alert(1)>' },
  });
  assert.ok(!html.includes('<img'));
  assert.ok(html.includes('&lt;img'));
});
