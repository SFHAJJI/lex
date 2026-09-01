import assert from 'node:assert/strict';
import test from 'node:test';

import {
  DENSITIES,
  SHELL_SKINS,
  renderShellEntry,
  shellChrome,
  skinFor,
} from '../scripts/shells.mjs';
import { SHELLS, parseObjectUrl } from '../scripts/urls.mjs';
import { renderRefusalCard } from '../scripts/refusal-card.mjs';
import { renderStateBanner } from '../scripts/state-banner.mjs';

// Content that carries facts about a law: an interval, a hash, a refusal code, a payload.
// If a shell can change any of this, the shells are forks.
const CONTENT =
  renderStateBanner({
    envelope: { timeline_semantics: 'publisher_applicability' },
    state: {
      valid_from: '2001-01-01',
      valid_to: '2002-01-01',
      publication_date: '2000-12-01',
      observed_from: '2026-01-01T00:00:00Z',
    },
  }) +
  renderRefusalCard({
    code: 'no_version_for_date',
    sentence: 'No publisher state covers 1999-06-01.',
    payload: {
      history_begins: '2001-01-01',
      nearest_earlier: null,
      nearest_later: '2001-01-01',
      what_would_answer: ['new_official_observation'],
      asserts_absence_of_law: false,
    },
  });

/** Visible text, with tags removed and whitespace normalised. */
function visibleText(html) {
  return html
    .replace(/<script[\s\S]*?<\/script>/g, '')
    .replace(/<style[\s\S]*?<\/style>/g, '')
    .replace(/<[^>]+>/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

test('every shell has a skin and no skin exists outside the three', () => {
  assert.deepEqual([...SHELL_SKINS.keys()].sort(), [...SHELLS].sort());
  for (const shell of SHELLS) {
    const skin = skinFor(shell);
    assert.ok(DENSITIES.includes(skin.density), `${shell} has an unknown density`);
    assert.ok(skin.name.length > 0);
    assert.ok(skin.audience.length > 0);
  }
  assert.equal(new Set([...SHELL_SKINS.values()].map((s) => s.density)).size, 3);
});

test('a fourth shell is a fork, not a skin', () => {
  for (const bad of ['gateway', 'admin', 'toString', 'constructor', '__proto__', undefined]) {
    assert.throws(() => skinFor(bad), /unknown shell/, `${String(bad)} was skinned`);
  }
});

test('the same content reads identically under all three shells', () => {
  // This is the rule. A shell chooses density, never facts. If Ask hid the hash because it
  // looks technical, the citizen and the lawyer would hold different evidence about the same
  // law, and the citizen would be the one missing it.
  const rendered = SHELLS.map((shell) =>
    shellChrome({ shell, state: 'shell-test', title: 'Shell test', main: CONTENT }),
  );

  const texts = rendered.map(visibleText);
  assert.equal(new Set(texts).size, 1, 'a shell changed the visible text');

  // And the facts are all actually present, so the test is not comparing three blanks.
  for (const fragment of [
    'Applicable from 2001-01-01 to 2002-01-01',
    'no_version_for_date',
    'history_begins',
    'No earlier state is held',
  ]) {
    assert.ok(texts[0].includes(fragment), `${fragment} is missing, so this proves nothing`);
  }
});

test('the shell rides on the root element and nowhere else', () => {
  const bodyOf = (html) => html.slice(html.indexOf('<body>'));
  const reference = bodyOf(
    shellChrome({ shell: SHELLS[0], state: 's', title: 'T', main: CONTENT }),
  );
  assert.ok(reference.length > 200, 'the reference body is too small to compare');

  for (const shell of SHELLS) {
    const html = shellChrome({ shell, state: 's', title: 'T', main: CONTENT });
    assert.ok(html.includes(`data-shell="${shell}"`), `${shell} did not reach the root`);
    assert.ok(html.includes(`data-density="${skinFor(shell).density}"`));
    assert.equal(
      bodyOf(html),
      reference,
      `${shell} changed the body markup, not only the root attributes`,
    );
  }
});

test('a page with no shell carries no shell attributes at all', async () => {
  const { page } = await import('../scripts/render.mjs');
  const html = page({ state: 's', title: 'T', main: '<h1>T</h1>' });
  assert.ok(!html.includes('data-shell'), 'a shell-less page claimed a shell');
  assert.ok(!html.includes('data-density'));
});

test('an entry screen links to the other shells and to no shell-prefixed object URL', () => {
  for (const shell of SHELLS) {
    const html = renderShellEntry({ shell });
    assert.ok(html.includes(`data-shell="${shell}"`));

    const hrefs = [...html.matchAll(/href="([^"]+)"/g)].map((match) => match[1]);
    const internal = hrefs.filter((href) => href.startsWith('/'));
    assert.ok(internal.length >= 2, `${shell} lost its shell switcher`);

    for (const href of internal) {
      // Every internal link is either a shell entry, which is allowed to carry a prefix,
      // or an object URL, which must not. `parseObjectUrl` returns null for a prefixed one.
      const segments = href.split('/').filter(Boolean);
      if (segments.length === 1 && SHELLS.includes(segments[0])) continue;
      assert.notEqual(
        parseObjectUrl(href),
        null,
        `${href} is neither a shell entry nor a shell-neutral object URL`,
      );
    }

    // The other two shells are reachable, so a reader is never trapped in a skin.
    for (const other of SHELLS.filter((one) => one !== shell)) {
      assert.ok(internal.includes(`/${other}`), `${shell} does not offer ${other}`);
    }
  }
});

test('the neutrality promise is on the page, in words, not only in the code', () => {
  for (const shell of SHELLS) {
    const html = renderShellEntry({ shell });
    assert.ok(html.includes('never changes what the law says'));
    assert.ok(html.includes('the same link for every reader'));
  }
});
