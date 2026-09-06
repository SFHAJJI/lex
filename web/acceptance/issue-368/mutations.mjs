import assert from 'node:assert/strict';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';

const file = 'web/scripts/citation-checker.mjs';
const original = readFileSync(file, 'utf8');
mkdirSync('artifacts/issue-368', { recursive: true });
const mutations = [
  ['digest-filter', 'candidate.hash === parsed.digest', 'true', 'a stale permalink refuses substitution'],
  ['publisher', 'identity.publisher !== parsed.publisher', 'false', 'a matching hash does not excuse'],
  ['work', 'identity.work !== parsed.key', 'false', 'a matching hash does not excuse'],
  ['date', 'candidate.valid_from !== parsed.at', 'false', 'a matching hash does not excuse'],
  ['state-hash-shape', "typeof candidate.hash !== 'string' || !/^[0-9a-f]{64}$/.test(candidate.hash)", 'false', 'permalink candidates require a canonical state hash'],
  ['calendar', 'if (!isCalendarDate(at)) return null;', '', 'a permalink with an impossible calendar date'],
  ['unavailable', "verdict: 'pinned_state_unavailable', raw, parsed, note: PINNED_STATE_UNAVAILABLE_NOTE", "verdict: 'out_of_corpus', raw, parsed, note: OUT_OF_CORPUS_NOTE", 'a stale permalink refuses substitution'],
  ['ambiguity', 'if (candidates.length > 1)', 'if (false)', 'a permalink selects its exact state hash'],
];
try {
  for (const [name, from, to, testName] of mutations) {
    assert.equal(original.split(from).length - 1, 1, `${name}: one target`);
    writeFileSync(file, original.replace(from, to));
    const result = spawnSync(process.execPath, ['--test', 'web/test/citation-checker.test.mjs'], { encoding: 'utf8' });
    const log = result.stdout + result.stderr;
    writeFileSync(`artifacts/issue-368/mutation-${name}.log`, log);
    assert.equal(result.status, 1, `${name}: expected test failure`);
    assert.ok(log.split('\n').some(line => line.startsWith('not ok ') && line.includes(testName)), `${name}: expected relevant failing assertion`);
    console.log(`${name}: exit=${result.status}, relevant assertion failed`);
  }
} finally {
  writeFileSync(file, original);
}
assert.equal(readFileSync(file, 'utf8'), original);
console.log(`restored source sha256=${createHash('sha256').update(original).digest('hex')}`);
