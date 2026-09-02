// The verification cluster ported to React, measured against the string renderer.
//
// The claim being tested is not that React works. It is that both renderers apply the same
// rules and reject the same inputs, because a framework quietly becoming a second home for
// legal rules is the worst available outcome of adopting one.
//
// The strongest form that claim can take here is byte equality, so that is what is asserted:
// this cluster contains no apostrophe, so the two surfaces produce the same bytes rather than
// merely the same claims. A test that only checked for the presence of a digest would pass
// against a component that had quietly stopped checking the host set.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { VerifyCluster } from '../.react-build/app.mjs';
import { PUBLISHER_HOSTS } from '../scripts/routes.mjs';
import { HASH_KINDS, renderVerifyCluster } from '../scripts/verify-cluster.mjs';

const DIGEST = 'ad5dace9bd5116f493fe3035fd01546284e24dd2057a15f8d6be95eba8f82cf0';
const PUBLISHER = 'preview-synthetic';
const SOURCE = 'https://preview.invalid/synthetic-preview-work/2001-01-01';
const LEX_ID = 'preview-synthetic:synthetic-preview-work:2001-01-01';

const RECORD = { sourceUri: SOURCE, lexId: LEX_ID, hash: { kind: 'record_sha256', value: DIGEST } };

const react = (props) => renderToStaticMarkup(h(VerifyCluster, props));
const string = (props) => renderVerifyCluster({ publisher: PUBLISHER, ...props });

test('both renderers produce the same cluster, byte for byte', () => {
  for (const kind of HASH_KINDS) {
    const props = { ...RECORD, hash: { kind, value: DIGEST } };
    assert.equal(react(props), string(props), `${kind} rendered differently`);
  }
});

test('the whole digest is present, emphasised at the front, in the React runtime too', () => {
  const html = react(RECORD);
  // One contiguous text run, so a selection yields the whole digest and not the truncation. A
  // truncated digest in a citation cannot be verified by anyone.
  const value = html.split('<code class="verify-hash-value">')[1].split('</code>')[0];
  assert.equal(value.replace(/<[^>]+>/g, ''), DIGEST);
  assert.ok(html.includes('>ad5dace9<'), 'the first eight lost their emphasis');
  // This line ships no client script, so a Copy button would be a control with no handler.
  assert.ok(!html.includes('<button'), 'an inert control was rendered');
  assert.ok(!html.includes('data-copy'), 'a copy affordance was promised without a script');
});

test('the digest says which digest it is, in both renderers', () => {
  // Three digests are present on every state and they answer different questions, so
  // sixty-four hex characters with no label is a number rather than evidence.
  for (const kind of HASH_KINDS) {
    assert.ok(react({ ...RECORD, hash: { kind, value: DIGEST } }).includes(kind));
  }
  for (const bad of ['sha256', '', undefined, 'toString']) {
    const props = { ...RECORD, hash: { kind: bad, value: DIGEST } };
    assert.throws(() => react(props), /must name which digest/, `${String(bad)} was accepted`);
    assert.throws(() => string(props), /must name which digest/);
  }
});

test('a digest that is not 64 lowercase hex is refused by both renderers', () => {
  for (const bad of [DIGEST.toUpperCase(), DIGEST.slice(0, 63), `${DIGEST}0`, 'not-a-digest']) {
    const props = { ...RECORD, hash: { kind: 'body_sha256', value: bad } };
    assert.throws(() => react(props), /64 lowercase hex/, `${bad} was rendered as a digest`);
    assert.throws(() => string(props), /64 lowercase hex/);
  }
});

test('an official-source link is validated against the publisher, not merely escaped', () => {
  // Bound to the closed host set rather than to a literal: a build that added a host would
  // otherwise make this test assert a message instead of a policy.
  assert.deepEqual([...PUBLISHER_HOSTS[PUBLISHER]], ['preview.invalid']);

  for (const [uri, why] of [
    ['http://preview.invalid/x', 'plaintext http under an official label'],
    ['https://evil.example/fake', 'an arbitrary host under an official label'],
    ['https://preview.invalid@evil.example/x', 'userinfo hiding the real host'],
    ['https://preview.invalid:8443/x', 'an explicit port'],
    ['javascript:alert(1)', 'a script URL'],
    ['not a url', 'not a URL at all'],
  ]) {
    const props = { ...RECORD, sourceUri: uri };
    assert.throws(() => react(props), /source URI/, `${why} was rendered as Official source`);
    assert.throws(() => string(props), /source URI/);
  }
});

test('a provenance link is never built from an identifier that names no record', () => {
  // `/provenance/undefined` is a visible action leading nowhere, and CLAUDE.md records that
  // exact shape shipping three times.
  for (const bad of [undefined, '', '   ', null, 7]) {
    assert.throws(() => react({ ...RECORD, lexId: bad }), /lex_id|does not name a publisher/);
    assert.throws(() => string({ ...RECORD, lexId: bad }), /requires a lex_id/);
  }
  // Non-empty and still not an identity. Both renderers read it the same strict way, so the
  // sentence is the same one.
  for (const bad of ['garbage', 'preview-synthetic:synthetic-preview-work', ':::']) {
    assert.throws(() => react({ ...RECORD, lexId: bad }), /does not name a publisher, a work and a state/);
    assert.throws(() => string({ ...RECORD, lexId: bad }), /does not name a publisher, a work and a state/);
  }
  const html = react(RECORD);
  assert.ok(!html.includes('provenance/undefined'));
  assert.ok(!html.includes('provenance/null'));
});

test('the React cluster has no publisher to be told, so the two links cannot disagree', () => {
  // The defect this closes: told `lu-legilux` beside an eu-eurlex identifier, the anchor is
  // checked against Legilux hosts while Provenance addresses a Union record. Both links
  // resolve and both look official, which is why escaping is not the guard.
  const told = react({ ...RECORD, publisher: 'lu-legilux' });
  assert.equal(told, react(RECORD), 'a publisher prop reached the output');
  assert.ok(told.includes('/provenance/preview-synthetic%3A'));

  // The string renderer still takes one from its existing callers, and refuses it rather than
  // checking the anchor against one publisher's hosts while Provenance names another's. Both
  // halves of the guard are exercised: a declared publisher whose host set does not admit the
  // source is refused on the host, and one whose host set does admit it is refused on the
  // record it disagrees with.
  assert.throws(
    () => renderVerifyCluster({ ...RECORD, publisher: 'lu-legilux' }),
    /is not one of legilux\.public\.lu/,
  );
  assert.throws(
    () =>
      renderVerifyCluster({
        ...RECORD,
        publisher: 'lu-legilux',
        sourceUri: 'https://legilux.public.lu/eli/synthetic/2001-01-01',
      }),
    /the publisher is written on the record/,
  );
  assert.throws(
    () => renderVerifyCluster({ ...RECORD, publisher: undefined }),
    /is not a publisher this build serves/,
  );
});
