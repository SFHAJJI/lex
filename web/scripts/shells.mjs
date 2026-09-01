// The three shells of UX spec section 1: Ask, Workbench and Gateway.
//
// "Shell is a URL prefix, not a fork." That sentence is the whole design, and it is the one
// a component library quietly breaks: somebody hides a field on the citizen shell because it
// looks technical, and from then on two readers of the same law hold different facts about
// it. The citizen is the one who loses, because the field that looks technical is usually
// the hash, the conflict badge or the profile, which is to say the evidence.
//
// So a shell here changes exactly two things: how dense the layout is, and which entry screen
// you land on. It cannot change content, and that is enforced rather than promised:
// `shellChrome` takes already-rendered content and wraps it, so there is no parameter through
// which a shell could select what to render, and a test asserts that the visible text of the
// same content is byte-identical under all three.
//
// The URL half of the rule is enforced in `urls.mjs`, where the object builders have no shell
// parameter and refuse a publisher segment that spells one.

import { page } from './render.mjs';
import { SHELLS, shellUrl } from './urls.mjs';

/**
 * What each shell actually changes. Density and the entry screen. Nothing else.
 *
 * A Map, not an object literal, for the same reason the timeline vocabulary is one: an object
 * literal inherits `toString` and stops being a closed set the moment an untrusted string
 * reaches it.
 */
export const SHELL_SKINS = new Map([
  [
    'ask',
    Object.freeze({
      name: 'Ask',
      density: 'comfortable',
      firstBreakpoint: 'mobile',
      audience: 'residents and students, in four languages',
      entry: 'Ask about a law, in FR, DE, EN or LB',
    }),
  ],
  [
    'w',
    Object.freeze({
      name: 'Workbench',
      density: 'compact',
      firstBreakpoint: 'desktop',
      audience: 'professionals who must defend a dated answer',
      entry: 'Title, ELI, CELEX, citation, or keywords',
    }),
  ],
  [
    'dev',
    Object.freeze({
      name: 'Gateway',
      density: 'monospace',
      firstBreakpoint: 'desktop',
      audience: 'developers consuming the same operations as REST and MCP',
      entry: 'The operation catalog, the closed refusal registry, and replay guarantees',
    }),
  ],
]);

export const DENSITIES = Object.freeze(['comfortable', 'compact', 'monospace']);

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

export function skinFor(shell) {
  const skin = SHELL_SKINS.get(shell);
  if (skin === undefined) {
    throw new Error(
      `unknown shell ${JSON.stringify(shell)}; the shells are ${SHELLS.join(', ')} and a ` +
        'fourth one is a fork, not a skin',
    );
  }
  return skin;
}

/**
 * Wrap already-rendered content in a shell.
 *
 * `main` arrives rendered. That is the point: there is no callback, no selector and no
 * feature flag through which a shell could decide what the reader is shown. It can only
 * decide how densely it is laid out.
 *
 * @param {object} input
 * @param {string} input.shell   one of SHELLS
 * @param {string} input.state   the preview state, for the page shell
 * @param {string} input.title   plain text, escaped once by the page shell
 * @param {string} input.main    rendered HTML, identical across shells by construction
 * @param {string} [input.locale]
 */
export function shellChrome({ shell, state, title, main, locale = 'en' }) {
  const skin = skinFor(shell);
  return page({
    state,
    title,
    locale,
    main,
    shell,
    density: skin.density,
  });
}

/**
 * The entry screen for one shell. This is the only screen a shell may differ in, and it
 * differs in its copy and its own link, never in a fact about the law.
 */
export function renderShellEntry({ shell, locale = 'en' }) {
  const skin = skinFor(shell);
  const others = SHELLS.filter((one) => one !== shell);

  const switcher = others
    .map((one) => {
      const other = skinFor(one);
      return (
        `<li><a href="${escapeHtml(shellUrl(one))}">${escapeHtml(other.name)}</a> ` +
        `<span class="shell-audience">${escapeHtml(other.audience)}</span></li>`
      );
    })
    .join('');

  return shellChrome({
    shell,
    locale,
    state: `shell-${shell}`,
    title: skin.name,
    main:
      `      <p class="eyebrow">${escapeHtml(skin.name)}</p>\n` +
      `      <h1>${escapeHtml(skin.name)}</h1>\n` +
      `      <p>${escapeHtml(skin.entry)}</p>\n` +
      '      <p class="shell-neutrality">A shell chooses how densely this is laid out and ' +
      'which screen you start on. It never changes what the law says, and every link to a ' +
      'law is the same link for every reader.</p>\n' +
      `      <ul class="shell-switcher">${switcher}</ul>`,
  });
}
