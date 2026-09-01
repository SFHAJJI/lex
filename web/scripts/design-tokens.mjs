// The eight semantic tokens of UX spec section 1.
//
// Product spec section 7 rule 5 says no meaning is carried by colour alone. That rule is
// impossible to keep by review once twelve screens exist, because the failure looks like
// nothing: a coloured span that reads correctly to everyone who can see the colour. So the
// rule is enforced by the shape of this module instead. A token is not a colour; it is a
// colour *and* an icon *and* a text label, and `mark()` is the only way to apply one. There
// is deliberately no exported function that returns just a colour.

const LIGHT_BACKGROUND = '#f4f1e8';
const DARK_BACKGROUND = '#111713';

/**
 * @typedef {object} Token
 * @property {string} name      the CSS custom property
 * @property {string} light     colour on the light ground
 * @property {string} dark      colour on the dark ground
 * @property {string} icon      the required icon, never the only carrier of meaning
 * @property {string} label     the required text label, read by assistive technology
 * @property {string} [pattern] a non-colour texture where the spec names one
 */

/** @type {readonly Token[]} */
export const TOKENS = Object.freeze([
  {
    name: '--time-legal',
    light: '#1f4d33',
    dark: '#7fd3a3',
    icon: '▣',
    label: 'legal time',
    pattern: 'solid underline',
  },
  {
    name: '--time-record',
    light: '#4a4230',
    dark: '#cbbf9a',
    icon: '◎',
    label: 'record time',
    pattern: 'dotted underline',
  },
  {
    name: '--provisional',
    light: '#6b4512',
    dark: '#e3b567',
    icon: '◱',
    label: 'PROVISIONAL, publisher-scheduled',
    pattern: 'diagonal watermark',
  },
  {
    name: '--conflict',
    light: '#8a2f1a',
    dark: '#f0a08c',
    icon: '⚠',
    label: 'dates disagree, both are the publisher’s',
  },
  {
    name: '--derived',
    light: '#3b4176',
    dark: '#a8b0ee',
    icon: '⚙',
    label: 'derived, not publisher-asserted',
  },
  {
    name: '--unofficial',
    light: '#5b3a6b',
    dark: '#d0a8e0',
    icon: '◰',
    label: 'UNOFFICIAL',
  },
  {
    name: '--refusal',
    light: '#1f4a5c',
    dark: '#8fd0e6',
    icon: '⛨',
    label: 'typed refusal',
  },
  {
    name: '--hole',
    light: '#4c4740',
    dark: '#bdb6ac',
    icon: '░',
    label: 'no publisher state covers this period',
    pattern: 'hatched',
  },
]);

const BY_NAME = new Map(TOKENS.map((token) => [token.name, token]));

export const BACKGROUNDS = Object.freeze({
  light: LIGHT_BACKGROUND,
  dark: DARK_BACKGROUND,
});

/** The custom properties for both schemes, to be appended to the stylesheet. */
export function tokenCss() {
  const light = TOKENS.map((t) => `  ${t.name}: ${t.light};`).join('\n');
  const dark = TOKENS.map((t) => `    ${t.name}: ${t.dark};`).join('\n');
  return `:root {\n${light}\n}\n\n@media (prefers-color-scheme: dark) {\n  :root {\n${dark}\n  }\n}\n`;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

/**
 * Applies a token to text. The icon is decorative and hidden from assistive technology,
 * because the label carries the same meaning as words; a screen reader that announced both
 * would say it twice, and a reader that could see only the icon would be back to meaning
 * carried by one channel.
 *
 * @param {string} name  a token name from TOKENS
 * @param {string} text  the content the token qualifies
 */
export function mark(name, text) {
  const token = BY_NAME.get(name);
  if (!token) {
    throw new Error(`unknown semantic token ${name}`);
  }

  const style = `color: var(${token.name})`;
  const cls = `token token${token.name}`;
  return (
    `<span class="${escapeHtml(cls)}" style="${escapeHtml(style)}">` +
    `<span class="token-icon" aria-hidden="true">${escapeHtml(token.icon)}</span>` +
    `<span class="token-label">${escapeHtml(token.label)}</span>` +
    `<span class="token-text">${escapeHtml(text)}</span>` +
    '</span>'
  );
}

export function tokenNamed(name) {
  return BY_NAME.get(name);
}
