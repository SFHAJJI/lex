import assert from 'node:assert/strict';
import test from 'node:test';

import {
  REFUSAL_EXAMPLES,
  payloadContractOf,
  renderRefusalCatalog,
} from '../scripts/refusal-catalog.mjs';
import { REFUSAL_CODES, REQUIRED_PAYLOAD, RETRYABLE } from '../scripts/refusal-card.mjs';
import { PUBLISHER_HOSTS } from '../scripts/routes.mjs';

const REAL_HOSTS = Object.entries(PUBLISHER_HOSTS)
  .filter(([publisher]) => publisher !== 'preview-synthetic')
  .flatMap(([, hosts]) => hosts);

test('the catalog covers the closed registry exactly', () => {
  assert.deepEqual(Object.keys(REFUSAL_EXAMPLES).sort(), [...REFUSAL_CODES].sort());
});

test('every code appears on the page with its contract state and a rendered example', () => {
  const html = renderRefusalCatalog();
  for (const code of REFUSAL_CODES) {
    assert.ok(html.includes(`<code>${code}</code>`), `${code} has no heading`);
    assert.ok(html.includes(REFUSAL_EXAMPLES[code].sentence), `${code} has no worked example`);
  }
  // Nineteen entries, not one entry repeated nineteen times.
  assert.equal(html.split('class="catalog-entry"').length - 1, REFUSAL_CODES.length);
  assert.equal(html.split('class="refusal-card"').length - 1, REFUSAL_CODES.length);
});

test('the page says which payloads are settled and which are not', () => {
  const specified = REFUSAL_CODES.filter((c) => payloadContractOf(c).state === 'specified');
  const unspecified = REFUSAL_CODES.filter((c) => payloadContractOf(c).state === 'unspecified');
  const elsewhere = REFUSAL_CODES.filter((c) => payloadContractOf(c).state === 'enforced elsewhere');

  assert.equal(specified.length, 9);
  assert.equal(unspecified.length, 9);
  assert.deepEqual(elsewhere, ['advice_boundary']);

  const html = renderRefusalCatalog();
  assert.ok(html.includes('9 codes have payload keys fixed by the specification'));
  assert.ok(html.includes('9 do not yet, and say so below'));
  // The word appears once per unspecified entry, so the honesty is per code and not only
  // in the summary sentence a reader may skip.
  assert.ok(html.split('>unspecified<').length - 1 >= unspecified.length);
});

test('each entry states its required keys and cites where they come from', () => {
  const html = renderRefusalCatalog();
  for (const [code, requirement] of Object.entries(REQUIRED_PAYLOAD)) {
    for (const key of requirement.keys) {
      assert.ok(html.includes(`<code>${key}</code>`), `${code} does not list ${key}`);
    }
    assert.ok(
      html.includes(requirement.basis.slice(0, 40).replace(/"/g, '&quot;')),
      `${code} does not cite its basis`,
    );
  }
});

test('retryable is stated per code rather than left to be inferred', () => {
  const html = renderRefusalCatalog();
  assert.equal(html.split('<dt>retryable</dt>').length - 1, REFUSAL_CODES.length);
  assert.equal(html.split('<dd>yes</dd>').length - 1, RETRYABLE.size);
});

test('the catalog is served in the Gateway shell', () => {
  const html = renderRefusalCatalog();
  assert.ok(html.includes('data-shell="dev"'));
  assert.ok(html.includes('data-density="monospace"'));
});

test('nothing on the catalog is a real coordinate', () => {
  const html = renderRefusalCatalog();
  for (const host of REAL_HOSTS) {
    assert.ok(!html.includes(host), `${host} is a real publisher host and appears in the catalog`);
  }
  const hosts = [...html.matchAll(/https?:\/\/([^/"\s]+)/g)].map((match) => match[1]);
  assert.ok(hosts.length > 0);
  for (const host of hosts) {
    assert.ok(host.endsWith('.invalid'), `${host} can resolve`);
  }
  assert.ok(html.includes('none of it is law'));
});

test('a code added to the registry without an example fails here', () => {
  // The guard is the set comparison in the first test; this one proves it is a comparison
  // and not a length check, by removing one and adding one.
  const drifted = { ...REFUSAL_EXAMPLES };
  delete drifted.rate_limited;
  drifted.some_new_code = { sentence: 'x', payload: { a: 'b' } };
  assert.notDeepEqual(Object.keys(drifted).sort(), [...REFUSAL_CODES].sort());
  assert.equal(Object.keys(drifted).length, REFUSAL_CODES.length);
});
