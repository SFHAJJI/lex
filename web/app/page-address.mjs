// The page's own address, written in the browser (the specification of record's R13 and R109; the plan's items 3.4 and
// 3.5): a readable address keeps the date it was read at in its query (`?date=`, `withDateQuery`), and a search page keeps
// its search after "#" (`search-address.mjs`), so a link a reader copies opens what they saw.
//
// The address is replaced, never pushed: no history entry is added and no history state is written (the journeys hold
// both), and the page stays at its own path. Nothing here sends anything: the fragment never reaches a server, and the
// query holds a date and a page number only. The language menu's links, which lead to the same page in each interface
// language (R61), are written again from the new address, as they are when a page loads (`client.jsx`).

import { sameAddressIn } from '../scripts/permalinks.mjs';

/** Writes the language menu's links for the address the page stands at (`sameAddressIn`). */
export function writeLanguageLinks(document, location) {
  for (const link of document.querySelectorAll('[data-locale-nav] a[data-same-document]')) {
    link.setAttribute('href', sameAddressIn(link.dataset.sameDocument, location));
  }
}

/**
 * Replaces the page's address with its own path, `search` (with its "?", or empty) and `hash` (with its "#", or empty), each
 * kept as it is when not given, adding no history entry and keeping the history state as it was; then writes the language
 * menu's links again. Returns whether the address changed.
 */
export function replacePageAddress(window, { search = window.location.search, hash = window.location.hash } = {}) {
  const { pathname } = window.location;
  const before = `${pathname}${window.location.search}${window.location.hash}`;
  const after = `${pathname}${search}${hash}`;
  if (after !== before) window.history.replaceState(window.history.state, '', after);
  writeLanguageLinks(window.document, window.location);
  return after !== before;
}
