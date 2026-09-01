import assert from 'node:assert/strict';
import test from 'node:test';

import {
  HANDOFF_HOSTS,
  PUBLISHER_HOSTS,
  handoffUri,
  publisherSourceUri,
  tryPublisherSourceUri,
} from '../scripts/routes.mjs';

const OK = 'https://legilux.public.lu/eli/etat/leg/loi/2002/08/02/n2';

test('a publisher source must be on that publisher own host set', () => {
  assert.equal(publisherSourceUri({ publisher: 'lu-legilux', uri: OK }), OK);
  // An EU host is a real publisher host, and still wrong under a Legilux label.
  assert.throws(
    () =>
      publisherSourceUri({
        publisher: 'lu-legilux',
        uri: 'https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32016R0679',
      }),
    /which is not one of legilux.public.lu/,
  );
});

test('a link is not official because the label above it says so', () => {
  for (const [uri, why] of [
    ['http://legilux.public.lu/x', 'plaintext http'],
    ['https://evil.example/legilux.public.lu', 'an arbitrary host'],
    ['https://legilux.public.lu.evil.example/x', 'a suffix that looks like the publisher'],
    ['https://legilux.public.lu@evil.example/x', 'userinfo before the real host'],
    ['https://legilux.public.lu:8443/x', 'an explicit port'],
    ['javascript:alert(1)', 'a script URL'],
    ['data:text/html,<b>x</b>', 'a data URL'],
    ['//legilux.public.lu/x', 'a scheme-relative URL'],
    ['HTTPS://legilux.public.lu/x', 'a scheme spelled to dodge a prefix check'],
    [' https://legilux.public.lu/x', 'leading whitespace'],
    ['https://legilux.public.lu/a b', 'an unencoded space'],
    ['https://legilux.public.lu\\@evil.example/x', 'a backslash the parsers disagree on'],
  ]) {
    assert.throws(
      () => publisherSourceUri({ publisher: 'lu-legilux', uri }),
      /source URI/,
      `${why} was accepted: ${uri}`,
    );
  }
});

test('a spelling that dodges a prefix check is refused before parsing', () => {
  // `new URL` normalises the scheme, so `HTTPS://` parses to protocol `https:` and the
  // semantic check alone cannot see it. The exact-spelling check is what catches a link
  // written to slip past a string test, and this case is the only thing holding it.
  assert.throws(
    () => publisherSourceUri({ publisher: 'lu-legilux', uri: 'HTTPS://legilux.public.lu/x' }),
    /spelled exactly/,
  );
});

test('userinfo is refused even when the host is the publisher', () => {
  // `https://legilux.public.lu@evil.example/` is caught by the host check. The direction the
  // host check cannot see is this one: a real publisher host with somebody else's name in
  // front of it, which is what a reader scanning the start of the link actually reads.
  assert.throws(
    () =>
      publisherSourceUri({
        publisher: 'lu-legilux',
        uri: 'https://evil.example@legilux.public.lu/x',
      }),
    /carries userinfo/,
  );
});

test('the publisher set is closed against the prototype', () => {
  for (const publisher of ['toString', 'constructor', 'hasOwnProperty', '__proto__', undefined]) {
    assert.throws(
      () => publisherSourceUri({ publisher, uri: OK }),
      /is not a publisher this build serves/,
      `${String(publisher)} reached a host set`,
    );
  }
});

test('a handoff has its own closed policy, and today it is synthetic only', () => {
  const counter = 'https://handoff.invalid/counter';
  assert.equal(handoffUri(counter), counter);
  // No real counter has been verified into this build, so none is offered. That is the
  // handoff registry being editorial work rather than something code may guess at.
  assert.deepEqual([...HANDOFF_HOSTS], ['handoff.invalid']);
  assert.throws(() => handoffUri('https://justice.public.lu/'), /which is not one of/);
  assert.throws(() => handoffUri('javascript:alert(1)'), /must be an https URI/);
});

test('a publisher host set is never empty and never shared by accident', () => {
  const seen = new Map();
  for (const [publisher, hosts] of Object.entries(PUBLISHER_HOSTS)) {
    assert.ok(hosts.length > 0, `${publisher} has no hosts`);
    for (const host of hosts) {
      assert.ok(!seen.has(host), `${host} is claimed by ${seen.get(host)} and ${publisher}`);
      seen.set(host, publisher);
    }
  }
  // The synthetic publisher is under a TLD that cannot resolve, so a fixture built on it
  // can never reach a real publisher by accident.
  assert.deepEqual([...PUBLISHER_HOSTS['preview-synthetic']], ['preview.invalid']);
});

test('the raw authority is validated before the parser erases the evidence', () => {
  // `new URL` reports no userinfo for `https://@host/` and no port for `https://host:443/`,
  // so a check that ran after parsing accepted both. A reader sees the raw string, so the
  // raw string is what has to be well formed.
  // Each case asserts its own message, so each guard is held by a case of its own. A shared
  // pattern would let the host grammar stand in for the port check and the userinfo check,
  // and either could then be deleted with nothing going red.
  for (const [uri, pattern, why] of [
    ['https://legilux.public.lu:443/x', /carries an explicit port/, 'a default port the parser hides'],
    ['https://@legilux.public.lu/x', /carries userinfo/, 'an empty userinfo the parser hides'],
    ['https:///legilux.public.lu/x', /does not carry a plain host/, 'no written authority at all'],
    ['https://LEGILUX.public.lu/x', /does not carry a plain host/, 'a host the parser would lowercase'],
    ['https://legilux.public.lu./x', /does not carry a plain host/, 'a trailing label separator'],
  ]) {
    assert.throws(
      () => publisherSourceUri({ publisher: 'lu-legilux', uri }),
      pattern,
      `${why} was accepted: ${uri}`,
    );
  }
});

test('there is one route policy, and it knows every host the other one knew', async () => {
  // `render.mjs` carried a second publisher map. They had already drifted: only one of them
  // knew `op.europa.eu`, so the same URL was official on one screen and not on another.
  assert.ok(PUBLISHER_HOSTS['eu-eurlex'].includes('op.europa.eu'));

  const { readFile } = await import('node:fs/promises');
  const render = await readFile(new URL('../scripts/render.mjs', import.meta.url), 'utf8');
  assert.ok(!render.includes('PUBLISHER_HOSTS'), 'render.mjs still declares its own host map');
  assert.ok(!render.includes('officialRouteHref'), 'render.mjs still has a second validator');
});

test('the non-throwing form refuses rather than raising, for captured data', () => {
  assert.equal(tryPublisherSourceUri('lu-legilux', 'https://evil.example/x'), null);
  assert.equal(tryPublisherSourceUri('unknown-publisher', OK), null);
  assert.equal(tryPublisherSourceUri('lu-legilux', OK), OK);
});
