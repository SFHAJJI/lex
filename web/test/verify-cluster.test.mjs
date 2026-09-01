import assert from 'node:assert/strict';
import test from 'node:test';

import {
  HASH_KINDS,
  renderEnvelopeStrip,
  renderVerifyCluster,
} from '../scripts/verify-cluster.mjs';

const DIGEST = 'ad5dace9bd5116f493fe3035fd01546284e24dd2057a15f8d6be95eba8f82cf0';
const PUBLISHER = 'preview-synthetic';
const SOURCE = 'https://preview.invalid/synthetic-preview-work/2001-01-01';
const LEX_ID = 'preview-synthetic:synthetic-preview-work:2001-01-01';

const GOOD_CLUSTER = {
  publisher: PUBLISHER,
  sourceUri: SOURCE,
  lexId: LEX_ID,
  hash: { kind: 'record_sha256', value: DIGEST },
};

const ENVELOPE = {
  publisher_name: 'Synthetic preview publisher, applicability semantics',
  timeline_semantics: 'publisher_applicability',
  freshness: { built_at: '2026-01-01T00:00:00Z', stamp_signature_valid: true },
  artifact: {
    corpus_commit: 'synthetic-corpus-commit',
    code_commit: 'synthetic-code-commit',
    manifest_set_id: 'synthetic-manifest-set',
    content_digest: 'synthetic-content-digest',
  },
};

test('the whole digest is on the page, and no control promises what it cannot do', () => {
  const html = renderVerifyCluster({
    publisher: PUBLISHER,
    sourceUri: SOURCE,
    lexId: LEX_ID,
    hash: { kind: 'record_sha256', value: DIGEST },
  });
  // One contiguous text run, so a selection yields the whole digest, not the truncation.
  const value = html.split('<code class="verify-hash-value">')[1].split('</code>')[0];
  assert.equal(value.replace(/<[^>]+>/g, ''), DIGEST);
  assert.ok(html.includes('>ad5dace9<'), 'the first eight lost their emphasis');
  // This line ships no client script, so a Copy button would be a control with no handler.
  assert.ok(!html.includes('<button'), 'an inert control was rendered');
  assert.ok(!html.includes('data-copy'), 'a copy affordance was promised without a script');
});

test('a chip must name which digest it shows', () => {
  for (const kind of HASH_KINDS) {
    const html = renderVerifyCluster({
      publisher: PUBLISHER,
    sourceUri: SOURCE,
      lexId: LEX_ID,
      hash: { kind, value: DIGEST },
    });
    assert.ok(html.includes(kind), `${kind} was not named on the chip`);
  }
  assert.throws(
    () =>
      renderVerifyCluster({
        publisher: PUBLISHER,
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
          publisher: PUBLISHER,
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
    publisher: PUBLISHER,
    sourceUri: SOURCE,
    lexId: LEX_ID,
    hash: { kind: 'record_sha256', value: DIGEST },
  });
  assert.ok(html.includes(`href="${SOURCE}"`));
});

test('an invalid stamp is not rendered like a valid one, and not as a date conflict', () => {
  const good = renderEnvelopeStrip({ envelope: ENVELOPE });
  assert.ok(good.includes('stamp signature valid'));
  assert.ok(!good.includes('did NOT verify'));

  const bad = renderEnvelopeStrip({
    envelope: {
      ...ENVELOPE,
      freshness: { ...ENVELOPE.freshness, stamp_signature_valid: false },
    },
  });
  assert.ok(bad.includes('did NOT verify'));
  assert.ok(bad.includes('<strong'), 'the invalid case lost its emphasis');
  // It used to borrow --conflict, whose label reads "dates disagree, both are the
  // publisher's". A signature that did not verify is not a date conflict, and the token
  // would have put that false sentence in the one place a reader goes to decide trust.
  assert.ok(!bad.includes('token--conflict'), 'signature invalidity reused the conflict token');
  assert.ok(!bad.includes('dates disagree'));
  assert.notEqual(good, bad);
});

test('an official-source link is validated against the publisher, not merely escaped', () => {
  for (const [uri, why] of [
    ['http://preview.invalid/x', 'plaintext http under an official label'],
    ['https://evil.example/fake', 'an arbitrary host under an official label'],
    ['https://preview.invalid@evil.example/x', 'userinfo hiding the real host'],
    ['https://preview.invalid:8443/x', 'an explicit port'],
    ['javascript:alert(1)', 'a script URL'],
    ['not a url', 'not a URL at all'],
  ]) {
    assert.throws(
      () =>
        renderVerifyCluster({
          publisher: PUBLISHER,
          sourceUri: uri,
          lexId: LEX_ID,
          hash: { kind: 'record_sha256', value: DIGEST },
        }),
      /source URI/,
      `${why} was rendered as Official source`,
    );
  }
});

test('a publisher outside the closed set has no host to be checked against', () => {
  for (const publisher of ['unknown-publisher', 'toString', 'constructor', undefined]) {
    assert.throws(
      () =>
        renderVerifyCluster({
          publisher,
          sourceUri: SOURCE,
          lexId: LEX_ID,
          hash: { kind: 'record_sha256', value: DIGEST },
        }),
      /is not a publisher this build serves/,
      `${String(publisher)} was accepted as a publisher`,
    );
  }
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

test('the strip names both source commits and the ones it lacks', () => {
  const full = renderEnvelopeStrip({ envelope: ENVELOPE });
  for (const field of [
    'corpus_commit',
    'index_builder_source_commit',
    'serving_runtime_source_commit',
    'manifest_set_id',
    'content_digest',
  ]) {
    assert.ok(full.includes(field), `${field} missing from the strip`);
  }
  // `code_commit` answered a different question than it appeared to: it is the commit that
  // built the index, and the commit that served the answer was nowhere. Neither may be
  // printed under the ambiguous name.
  assert.ok(!full.includes('>code_commit<'), 'the ambiguous provenance label is still printed');

  const sparse = renderEnvelopeStrip({ envelope: { ...ENVELOPE, artifact: {} } });
  assert.equal(sparse.split('not recorded').length - 1, 5, 'absent fields were silently dropped');
});

test('the strip refuses a timeline vocabulary it does not know', () => {
  for (const semantics of [undefined, 'in_force', 'toString', 'constructor', '']) {
    assert.throws(
      () => renderEnvelopeStrip({ envelope: { ...ENVELOPE, timeline_semantics: semantics } }),
      /unknown timeline_semantics/,
      `${String(semantics)} reached the strip`,
    );
  }
});

test('the strip refuses a build instant that is not one', () => {
  for (const builtAt of ['not-a-timestamp', '2026-99-99T00:00:00Z', '2026-01-01', '2026-01-01T25:00:00Z']) {
    assert.throws(
      () =>
        renderEnvelopeStrip({
          envelope: { ...ENVELOPE, freshness: { ...ENVELOPE.freshness, built_at: builtAt } },
        }),
      /not a UTC instant/,
      `${builtAt} was rendered as a build time`,
    );
  }
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

test('a legacy code_commit is not rendered under a V3 provenance name', () => {
  // Decision 63 gives code_commit no standing. An alias from it into
  // index_builder_source_commit renders a legacy-only value under a stronger V3 fact, which
  // is the V2 envelope surviving inside the V3 line.
  const legacy = renderEnvelopeStrip({
    envelope: {
      ...ENVELOPE,
      artifact: { code_commit: 'legacy-code-commit', manifest_set_id: 'synthetic-manifest-set' },
    },
  });
  assert.ok(!legacy.includes('legacy-code-commit'), 'a legacy value was promoted');
  assert.ok(legacy.includes('index_builder_source_commit'));
  assert.ok(legacy.split('not recorded').length - 1 >= 3, 'absent V3 facts were filled in');
});

test('a provenance link is never built from a missing identifier', () => {
  // `/provenance/undefined` is a visible action leading nowhere, and CLAUDE.md records that
  // exact shape shipping three times. Nothing held this guard.
  for (const bad of [undefined, '', '   ', null, 7]) {
    assert.throws(
      () => renderVerifyCluster({ ...GOOD_CLUSTER, lexId: bad }),
      /requires a lex_id for the provenance link/,
      `lexId=${JSON.stringify(bad)} produced a link`,
    );
  }
  const html = renderVerifyCluster(GOOD_CLUSTER);
  assert.ok(!html.includes('provenance/undefined'));
  assert.ok(!html.includes('provenance/null'));
});

test('the envelope strip names the party it is about', () => {
  for (const bad of [undefined, '', null, 7]) {
    assert.throws(
      () => renderEnvelopeStrip({ envelope: { ...ENVELOPE, publisher_name: bad, publisher: bad } }),
      /requires a publisher/,
      `publisher=${JSON.stringify(bad)} was rendered as a freshness claim about nobody`,
    );
  }
});
