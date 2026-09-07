import assert from 'node:assert/strict';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';

import { pagesFrom } from '../scripts/browser-evidence.mjs';
import { SHELLS } from '../scripts/urls.mjs';

// Drives the real `pagesFrom` against a real directory, because a predicate proved in isolation
// says nothing about whether anything calls it. Deleting the call site leaves the sibling
// predicate tests green, which is the exact shape of a guard that ships unreachable — I had one
// returned to me this morning for precisely that, so it is pinned here rather than assumed.

/** Builds a page directory whose manifest and contents agree, as a real build's output does. */
async function buildOutput(pages) {
  const root = await mkdtemp(join(tmpdir(), 'lex-route-coverage-'));
  await writeFile(join(root, 'pages.json'), JSON.stringify({ pages: [...pages].sort() }), 'utf8');
  for (const page of pages) {
    await writeFile(join(root, page), '<!doctype html><title>x</title>', 'utf8');
  }
  return root;
}

const EVERY_SHELL_PAGE = SHELLS.map((shell) => `shell-${shell}.html`);

test('pagesFrom accepts an output whose shells are all present', async () => {
  const root = await buildOutput([...EVERY_SHELL_PAGE, 'compare.html']);
  try {
    assert.deepEqual(await pagesFrom(root), [...EVERY_SHELL_PAGE, 'compare.html'].sort());
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test('pagesFrom refuses an output that routes to a shell it did not emit', async () => {
  // The self-consistent shrink: manifest and directory agree exactly, so the set check passes.
  // Only the route declaration knows a page is missing.
  const root = await buildOutput([...EVERY_SHELL_PAGE.slice(1), 'compare.html']);
  try {
    await assert.rejects(
      () => pagesFrom(root),
      (error) => {
        assert.match(error.message, /routes to shells with no measured page/);
        assert.match(error.message, new RegExp(EVERY_SHELL_PAGE[0].replace('.', '\\.')));
        return true;
      },
    );
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test('pagesFrom still refuses a manifest and directory that disagree', async () => {
  // The pre-existing guard must keep failing for its own cause rather than being absorbed.
  const root = await buildOutput(EVERY_SHELL_PAGE);
  try {
    await writeFile(
      join(root, 'pages.json'),
      JSON.stringify({ pages: [...EVERY_SHELL_PAGE, 'never-built.html'].sort() }),
      'utf8',
    );
    await assert.rejects(() => pagesFrom(root), /declared and absent: never-built\.html/);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});
