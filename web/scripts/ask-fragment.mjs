// The address fragment that opens the assistant's page with a question asked (2026-11-13), in a module of its own so that the
// home page's bundle, which links to that page, carries none of the panel's copy (the web suite checks it).
//
// This module is data and pure functions only.

/**
 * The address fragment that opens the assistant's page with a question asked (`#q=…`, 2026-11-13: the home page's question
 * box and its example questions). A fragment never reaches the server (R109): the page reads it and asks `ask` as the field
 * would. At most the field's 1,000 characters.
 */
export const ASK_FRAGMENT_MAX = 1000;

/** The fragment that asks `question` on the assistant's page. */
export function askFragment(question) {
  return `#q=${encodeURIComponent(question)}`;
}

/** The question an address fragment asks (`#q=…`), trimmed, or null when it carries none, an empty one or a longer one. */
export function readAskFragment(hash) {
  if (typeof hash !== 'string' || !hash.startsWith('#')) return null;
  let asked;
  try {
    asked = new URLSearchParams(hash.slice(1)).get('q');
  } catch {
    return null;
  }
  if (asked === null) return null;
  const question = asked.trim();
  return question.length === 0 || question.length > ASK_FRAGMENT_MAX ? null : question;
}
