import assert from 'node:assert/strict';
import test from 'node:test';

import { PUBLISHER_HOSTS } from '../scripts/routes.mjs';
import { renderTrustSurface } from '../scripts/trust-surface.mjs';

const REAL_PUBLISHER_HOSTS = Object.entries(PUBLISHER_HOSTS)
  .filter(([publisher]) => publisher !== 'preview-synthetic')
  .flatMap(([, hosts]) => hosts);

test('the page banner is true: nothing on it is a real coordinate', () => {
  // The first version of this page carried a real statutory excerpt, a real Legilux URI and
  // real corpus and code commits under a banner reading "no legal data is loaded". A page
  // whose own disclaimer is false is worse than one with no disclaimer, because the reader
  // who checks is misled by the act of checking.
  const html = renderTrustSurface();

  for (const host of REAL_PUBLISHER_HOSTS) {
    assert.ok(!html.includes(host), `${host} is a real publisher host and appears on the page`);
  }

  const hosts = [...html.matchAll(/https?:\/\/([^/"\s]+)/g)].map((match) => match[1]);
  assert.ok(hosts.length >= 2, 'the page stopped exercising outbound links');
  for (const host of hosts) {
    assert.ok(
      host.endsWith('.invalid'),
      `${host} can resolve; a synthetic fixture belongs under a reserved TLD that cannot`,
    );
  }

  // A 40 hex identity is a git commit. The page names its identities synthetic instead.
  assert.equal(html.match(/\b[0-9a-f]{40}\b/g), null, 'a commit-shaped identity is on the page');
  for (const field of [
    'synthetic-corpus-commit',
    'synthetic-index-builder-commit',
    'synthetic-serving-runtime-commit',
  ]) {
    assert.ok(html.includes(field), `${field} is not on the page`);
  }
});

test('the quoted text says of itself that it is not law', () => {
  const html = renderTrustSurface();
  assert.ok(html.includes('SYNTHETIC PREVIEW'));
  assert.ok(html.includes('no legal authority'));
  assert.ok(html.includes('nothing here is law'));
});

test('the page does not claim that a publisher key selects authenticity', () => {
  // Decision 58 binds authenticity to the exact resource. The first version of this section
  // said the publisher key selected the rule, which taught the reader the thing the decision
  // forbids, on the page whose whole job is to be checkable.
  const html = renderTrustSurface();
  assert.ok(!html.includes('publisher key selects'));
  assert.ok(html.includes("resource's own evidence decides"));
  assert.ok(html.includes('sole authentic language'));
});

test('the page still exercises every component it claims to', () => {
  const html = renderTrustSurface();
  for (const marker of [
    'state-banner',
    'verify-cluster',
    'envelope-strip',
    'refusal-card',
    'refusal-candidates',
    'localization-unavailable',
    'law-authenticity',
    'blockquote class="law"',
  ]) {
    assert.ok(html.includes(marker), `${marker} is no longer exercised by the trust surface`);
  }
  // Both timeline vocabularies, so neither publisher's words can be quietly dropped.
  assert.ok(html.includes('Applicable from'));
  assert.ok(html.includes('Consolidated wording state from'));
});

test('the trust page states no measured figure it cannot compute', () => {
  // This page mounts no index and reads no envelope, so any percentage on it is a literal
  // somebody typed. Decision 27 puts counts at index build precisely so they cannot go stale in
  // a template, and this is the page that argues the product is checkable: a number nobody can
  // check is the worst sentence available here.
  const html = renderTrustSurface();
  assert.equal(html.includes('39.8'), false, 'a hand-typed corpus statistic returned');
  assert.equal(
    /\d+(\.\d+)?\s*percent/.test(html),
    false,
    'the trust page states a percentage it cannot derive',
  );
});
