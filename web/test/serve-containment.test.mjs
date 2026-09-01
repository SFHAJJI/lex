// The review harness's own static server, attacked over real HTTP.
//
// O11: the server decoded percent-escapes after the URL parser had already normalised `..`
// segments, which put the traversal back. Twelve encoded parent segments followed by an encoded
// separator resolved outside the distribution root and the file was served. It binds loopback,
// so it is not remotely reachable, but it is a local file disclosure primitive living inside
// the tool this project uses to decide whether the product is honest.
//
// These tests drive the actual server over the actual network stack rather than calling a path
// helper, because the defect lived in the seam between URL parsing, percent-decoding and path
// joining. A unit test of any one of those three would have passed while the seam leaked.

import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, mkdir, writeFile, rm, symlink } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { serveDist, withinRoot } from '../scripts/browser-evidence.mjs';

const SECRET = 'this file is outside the distribution root and must never be served';

async function withServer(run) {
  const base = await mkdtemp(join(tmpdir(), 'lex-serve-'));
  const root = join(base, 'dist');
  await mkdir(root, { recursive: true });
  await writeFile(join(root, 'index.html'), '<!doctype html><title>inside</title>', 'utf8');
  await writeFile(join(base, 'secret.txt'), SECRET, 'utf8');
  const site = await serveDist(root);
  try {
    await run(site, { base, root });
  } finally {
    await site.close();
    await rm(base, { force: true, recursive: true });
  }
}

test('a file inside the root is still served, so the guard is not just refusing everything', async () => {
  await withServer(async (site) => {
    const response = await fetch(`${site.origin}/index.html`);
    assert.equal(response.status, 200);
    assert.equal((await response.text()).includes('inside'), true);
  });
});

test('encoded traversal never reaches a file outside the distribution root', async () => {
  // The first payload is the one that was reported: encoded parent segments defeat the URL
  // parser's normalisation because the decode happened afterwards. The rest are the obvious
  // neighbours, included because fixing exactly one reported string is how a guard ends up
  // proving nothing.
  const payloads = [
    '/..%2fsecret.txt',
    '/..%2F..%2Fsecret.txt',
    `/${'..%2f'.repeat(12)}secret.txt`,
    '/..%5csecret.txt',
    '/%2e%2e%2fsecret.txt',
    '/..%252fsecret.txt',
    '/subdir/..%2f..%2fsecret.txt',
  ];
  await withServer(async (site) => {
    for (const payload of payloads) {
      const response = await fetch(`${site.origin}${payload}`);
      const body = await response.text();
      assert.equal(
        body.includes(SECRET),
        false,
        `${payload} served content from outside the distribution root`,
      );
      assert.equal(response.status, 404, `${payload} was answered ${response.status}, not 404`);
    }
  });
});

test('a NUL byte in the path is refused rather than truncating it', async () => {
  await withServer(async (site) => {
    const response = await fetch(`${site.origin}/index.html%00.txt`);
    assert.equal(response.status, 404);
  });
});

test('containment accepts the root itself and refuses its siblings', () => {
  // Pinned separately from the HTTP tests: a sibling directory whose name merely starts with
  // the root's name is the classic prefix-comparison bug, and it is invisible over HTTP unless
  // a request happens to name it.
  const root = join(tmpdir(), 'lex-root');
  assert.equal(withinRoot(root, root), true);
  assert.equal(withinRoot(root, join(root, 'page.html')), true);
  assert.equal(withinRoot(root, join(root, 'nested', 'page.html')), true);
  assert.equal(withinRoot(root, `${root}-sibling/page.html`), false);
  assert.equal(withinRoot(root, join(tmpdir(), 'elsewhere.txt')), false);
});

test('a directory junction inside the root cannot serve a file outside it', async (t) => {
  // O11-R1. Lexical containment is not containment: readFile follows reparse points, so a
  // junction placed inside dist and pointing outside satisfied every string check and still
  // returned the outside sentinel with a real 200. This drives the actual server, because the
  // defect is in what gets opened, not in what the path looks like.
  const base = await mkdtemp(join(tmpdir(), 'lex-junction-'));
  const root = join(base, 'dist');
  const outside = join(base, 'outside');
  await mkdir(root, { recursive: true });
  await mkdir(join(outside, 'nested'), { recursive: true });
  await writeFile(join(root, 'index.html'), '<!doctype html><title>inside</title>', 'utf8');
  await writeFile(join(outside, 'nested', 'secret.txt'), SECRET, 'utf8');

  let linked = false;
  try {
    // 'junction' needs no elevation on Windows; POSIX takes the directory symlink.
    await symlink(outside, join(root, 'leak'), process.platform === 'win32' ? 'junction' : 'dir');
    linked = true;
  } catch {
    // Named inconclusive rather than a silent pass: a host that cannot create the link has not
    // demonstrated the property, and saying nothing would read as having proved it.
    t.diagnostic('INCONCLUSIVE: this host does not permit link creation; the escape is unproven here');
  }

  const site = await serveDist(root);
  try {
    if (linked) {
      const response = await fetch(`${site.origin}/leak/nested/secret.txt`);
      const body = await response.text();
      assert.equal(body.includes(SECRET), false, 'a junction served a file outside the root');
      assert.equal(response.status, 404);
    }
    // The root itself must still work, so the guard is not simply refusing everything.
    assert.equal((await fetch(`${site.origin}/index.html`)).status, 200);
  } finally {
    await site.close();
    await rm(base, { force: true, recursive: true });
  }
});
