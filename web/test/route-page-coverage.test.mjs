import assert from 'node:assert/strict';
import test from 'node:test';

import { routePagesMissingFrom } from '../scripts/browser-evidence.mjs';
import { SHELLS } from '../scripts/urls.mjs';

// #360's completeness limb: the browser matrix must measure the whole app, not merely a set
// that agrees with itself.
//
// `pagesFrom` already refuses a manifest and a directory that disagree, and the comment there is
// right that a hand-written list fails open. But `pages.json` is written from what the build
// emitted, so both readings describe the same act. Delete the shell loop in `build.mjs` and the
// manifest and the directory shrink together, still agree, and the run reports every combination
// clean while measuring an app smaller than the one `urls.mjs` routes to. That is the gap this
// closes, and it is the one I recorded as unchecked when the driven-behaviour evidence landed.

test('every declared shell has a page in the measured set', () => {
  const measured = SHELLS.map((shell) => `shell-${shell}.html`);

  assert.deepEqual(routePagesMissingFrom(measured), []);
});

test('a shell that routes without a page is named', () => {
  // The failure the set check cannot see: a build that emitted one fewer entry screen.
  const measured = SHELLS.slice(1).map((shell) => `shell-${shell}.html`);

  assert.deepEqual(routePagesMissingFrom(measured), [`shell-${SHELLS[0]}.html`]);
});

test('every shell missing is every shell named, not just the first', () => {
  // A guard that stops at the first offender turns a whole missing loop into one line.
  assert.deepEqual(
    routePagesMissingFrom([]),
    [...SHELLS].map((shell) => `shell-${shell}.html`).sort(),
  );
});

test('unrelated pages do not satisfy a shell', () => {
  // The count is not the property. Twelve unrelated pages passing is the exact shape the
  // manifest check was written to retire, and it must not creep back in here.
  const measured = ['compare.html', 'timeline.html', 'coverage.html', 'trust-surface.html'];

  assert.deepEqual(
    routePagesMissingFrom(measured),
    [...SHELLS].map((shell) => `shell-${shell}.html`).sort(),
  );
});

test('extra pages beside a complete shell set are not a coverage failure', () => {
  // This guard answers "is every route measured", never "is anything else measured". The
  // manifest/directory set check owns the other direction, and duplicating it here would make
  // two guards fail for one cause.
  const measured = [...SHELLS.map((shell) => `shell-${shell}.html`), 'compare.html'];

  assert.deepEqual(routePagesMissingFrom(measured), []);
});

test('the route vocabulary is injectable so the rule is pinned rather than the current list', () => {
  // Pinning against SHELLS alone would pass if SHELLS itself were emptied, which is the
  // vocabulary-drift shape rather than the coverage shape.
  assert.deepEqual(routePagesMissingFrom([], ['ask']), ['shell-ask.html']);
  assert.deepEqual(routePagesMissingFrom(['shell-ask.html'], ['ask']), []);
});
