import assert from 'node:assert/strict';
import test from 'node:test';

import { BACKGROUNDS, TOKENS, mark, tokenCss } from '../scripts/design-tokens.mjs';

// WCAG 2.1 relative luminance and contrast, computed here rather than trusted, because the
// stylesheet already carries a comment about a link colour that was 8.32:1 on one ground and
// 1.93:1 on the other. A palette is not accessible because somebody looked at it.
function channel(value) {
  const c = value / 255;
  return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

function luminance(hex) {
  const m = /^#([0-9a-f]{6})$/i.exec(hex);
  assert.ok(m, `not a six-digit hex colour: ${hex}`);
  const n = Number.parseInt(m[1], 16);
  const r = channel((n >> 16) & 0xff);
  const g = channel((n >> 8) & 0xff);
  const b = channel(n & 0xff);
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrast(a, b) {
  const la = luminance(a);
  const lb = luminance(b);
  const [hi, lo] = la > lb ? [la, lb] : [lb, la];
  return (hi + 0.05) / (lo + 0.05);
}

test('every token carries an icon and a text label, so colour is never the only channel', () => {
  assert.equal(TOKENS.length, 8);
  for (const token of TOKENS) {
    assert.ok(token.icon && token.icon.trim().length > 0, `${token.name} has no icon`);
    assert.ok(token.label && token.label.trim().length > 0, `${token.name} has no label`);
    assert.ok(token.name.startsWith('--'), `${token.name} is not a custom property`);
  }
});

test('every token meets WCAG 2.2 AA for normal text on both grounds', () => {
  const failures = [];
  for (const token of TOKENS) {
    const light = contrast(token.light, BACKGROUNDS.light);
    const dark = contrast(token.dark, BACKGROUNDS.dark);
    if (light < 4.5) failures.push(`${token.name} light ${light.toFixed(2)}:1`);
    if (dark < 4.5) failures.push(`${token.name} dark ${dark.toFixed(2)}:1`);
  }
  assert.deepEqual(failures, [], `tokens below 4.5:1\n${failures.join('\n')}`);
});

test('the contrast helper itself detects a known failure', () => {
  // The user-agent link blue on the dark ground, the exact defect the stylesheet records.
  assert.ok(contrast('#0000ee', BACKGROUNDS.dark) < 4.5);
  assert.ok(contrast('#9ccfe0', BACKGROUNDS.dark) >= 4.5);
});

test('both schemes define every token', () => {
  const css = tokenCss();
  for (const token of TOKENS) {
    const occurrences = css.split(`${token.name}:`).length - 1;
    assert.equal(occurrences, 2, `${token.name} is not defined in both schemes`);
  }
});

test('mark always emits the icon and the label beside the text', () => {
  for (const token of TOKENS) {
    const html = mark(token.name, 'sample content');
    assert.match(html, /class="token-icon" aria-hidden="true"/, `${token.name} lost its icon`);
    assert.ok(html.includes('token-label'), `${token.name} lost its label`);
    assert.ok(html.includes('sample content'), `${token.name} lost its text`);
  }
});

test('there is no way to apply a token colour without its icon and label', async () => {
  const module = await import('../scripts/design-tokens.mjs');
  // Any exported function that returned a bare colour would let a caller style a span with
  // colour alone, which is the failure this module exists to make unavailable.
  for (const [name, value] of Object.entries(module)) {
    if (typeof value !== 'function' || name === 'mark' || name === 'tokenCss') continue;
    if (name === 'tokenNamed') continue;
    assert.fail(`unexpected export ${name} may hand out a colour without its label`);
  }
});

test('an unknown token is refused rather than rendered blank', () => {
  assert.throws(() => mark('--not-a-token', 'x'), /unknown semantic token/);
});

test('mark escapes content rather than trusting it', () => {
  const html = mark('--refusal', '<script>alert(1)</script>');
  assert.ok(!html.includes('<script>'));
  assert.ok(html.includes('&lt;script&gt;'));
});

test('the tokens are defined in one place only', async () => {
  const { readFile } = await import('node:fs/promises');
  const source = await readFile(new URL('../src/styles.css', import.meta.url), 'utf8');
  for (const token of TOKENS) {
    assert.ok(
      !source.includes(token.name),
      `${token.name} is hardcoded in src/styles.css as well as in design-tokens.mjs; ` +
        'two copies of a colour is two sources of truth and the one that drifts is the ' +
        'one nobody tested',
    );
  }
});

test('the built stylesheet carries every token in both schemes', async () => {
  const { readFile } = await import('node:fs/promises');
  let built;
  try {
    built = await readFile(new URL('../dist/styles.css', import.meta.url), 'utf8');
  } catch {
    return; // dist is a build output; the build test covers its absence.
  }
  for (const token of TOKENS) {
    assert.equal(
      built.split(`${token.name}:`).length - 1,
      2,
      `${token.name} is not in the built stylesheet twice`,
    );
  }
});
