import assert from 'node:assert/strict';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';

const file = 'eng/verify-v3-tree.ps1';
const original = readFileSync(file);
const source = original.toString('utf8');
const from = "'^web/acceptance/issue-368/(?:README\\.md|sources\\.sha256|[a-z0-9-]+\\.(?:log|mjs))$'";
assert.equal(source.split(from).length - 1, 1);
mkdirSync('artifacts/issue-368', { recursive: true });
try {
  writeFileSync(file, source.replace(from, () => "'^web/acceptance/.+$'"));
  const result = spawnSync('pwsh', ['-NoLogo', '-NoProfile', '-File', file], { encoding: 'utf8' });
  const log = result.stdout + result.stderr;
  writeFileSync('artifacts/issue-368/tree-mutation.log', log);
  assert.equal(result.status, 1);
  assert.ok(log.includes('A legacy path mutation escaped the V3 structural allowlist'));
  console.log('broad evidence directory allowance: exit=1, rejected-neighbor assertion failed');
} finally {
  writeFileSync(file, original);
}
assert.deepEqual(readFileSync(file), original);
console.log('tree verifier restored byte-for-byte');
