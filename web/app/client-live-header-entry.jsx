// The V4 header's find field (the owner's brief of 2026-11-15, section 3), bundled to /{language}/client-live-header.js and
// loaded by every V4 page. The field is a form with no action and a control with no name (R109: no word a reader types
// reaches an address a server could record): on submit, the reader goes to the search page with the words after "#"
// (`searchFragment`), which the browser never sends; the search page asks them in a request's body, and opens the text
// itself when the words name one.

import { livePath } from '../scripts/live-chrome.mjs';
import { searchFragment } from '../scripts/search-address.mjs';

/** The search page's address for `words`, the words after "#", or null for words no search would send (none, too long). */
export function findAddress(words) {
  try {
    return `${livePath('search.html')}${searchFragment({ query: words, language: 'fra' }, { work: false })}`;
  } catch {
    return null;
  }
}

const form = typeof document === 'undefined' ? null : document.querySelector('[data-header-find]');
if (form !== null) {
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    const address = findAddress(form.querySelector('input')?.value ?? '');
    if (address !== null) window.location.assign(address);
  });
}
