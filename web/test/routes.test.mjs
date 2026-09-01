import assert from 'node:assert/strict';
import test from 'node:test';

import { HANDOFF_HOSTS, PUBLISHER_HOSTS, handoffUri, publisherSourceUri } from '../scripts/routes.mjs';

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
