// The answer a page carries when the server rendered it from its own answer (the specification of record's R67: the
// dossier and reading pages are complete HTML from the server, readable without JavaScript, then enhanced).
//
// The server writes the API's envelope into the page, beside the root it rendered, as an inert data block
// (`<script type="application/json">`, which no Content-Security-Policy treats as a script); the browser reads it and
// renders the same tree from it, so hydration changes nothing and the page asks the API nothing as it loads. The block
// stands outside the hydrated root, whose markup must be exactly the tree's.
//
// Data and pure functions only: the pages, their client entries and the server-render bundle share it.

/** The id of the data block that carries the answer a page was rendered from. */
export const INITIAL_ANSWER_ID = 'lex-initial-answer';

/**
 * A value as JSON that cannot end the block it stands in: `<`, `>` and `&` written as Unicode escapes (so no `</script>`
 * and no comment opener), and the two line separators JavaScript once refused in strings, escaped too.
 */
export function jsonInScript(value) {
  return JSON.stringify(value)
    .replace(/</g, '\\u003c')
    .replace(/>/g, '\\u003e')
    .replace(/&/g, '\\u0026')
    .replace(/\u2028/g, '\\u2028')
    .replace(/\u2029/g, '\\u2029');
}

/** The address of the law a page stands at, with its interface language taken off (`/en/lu-legilux/x` is `/lu-legilux/x`). */
export function lawPathOf(path) {
  const match = /^\/(?:en|fr)(\/(?:lu-legilux|eu-eurlex)\/.*)$/.exec(path ?? '');
  return match === null ? null : match[1];
}

/**
 * The answer a page was rendered from, as its client entry reads it: `{ envelope, path }`, or null when the page carries
 * none (a page the server did not render with an answer, or a block that is not JSON, which then hydrates as the empty
 * form and is reported as a hydration that changed the page).
 */
export function readInitialAnswer(document, location) {
  const block = document.getElementById(INITIAL_ANSWER_ID);
  if (block === null) return null;
  try {
    return { envelope: JSON.parse(block.textContent ?? ''), path: location.pathname };
  } catch {
    return null;
  }
}
