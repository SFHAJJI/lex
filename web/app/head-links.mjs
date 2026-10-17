// The head links of a page at a law's address, written in the browser (R61, R64, R67).
//
// The server renders a law's page with them (`Document.jsx`, `lawPath`). The page as built (`reading.html`,
// `dossier.html`), which either server sends when it cannot render one, stands at every law's address and names none, so
// once loaded it writes them for the work's address, or a reading's state's permalink (`client.jsx`), and the reading
// page names what its answer shows, the article the address names when the state holds it (`LiveReading.jsx`,
// `readingAddressShown`), as the server's render does: only addresses the platform mints, never the request's. A link
// element is not a script, so the page's Content-Security-Policy is untouched; the head is outside the hydrated root, so
// React reads none of it.

import { lawPageLinks } from '../scripts/permalinks.mjs';
import { CANONICAL_HOST } from '../scripts/routes.mjs';
import { CHROME_LOCALE } from '../scripts/live-chrome.mjs';

/**
 * Writes into `document`'s head the links of a page at `lawPath`, a law's address with no interface language: the
 * canonical link to that address in the page's interface language (the language its bundle was built for), and the address
 * in each interface language and for any other (`lawPageLinks`), replacing any the head held.
 */
export function writeLawPageLinks(document, lawPath) {
  const { canonical, alternates } = lawPageLinks(lawPath, `https://${CANONICAL_HOST}`, CHROME_LOCALE);
  const head = document.head;
  let link = head.querySelector('link[rel="canonical"]');
  if (link === null) {
    link = document.createElement('link');
    link.setAttribute('rel', 'canonical');
    head.append(link);
  }
  link.setAttribute('href', canonical);
  for (const old of head.querySelectorAll('link[rel="alternate"][hreflang]')) old.remove();
  for (const { hrefLang, href } of alternates) {
    const alternate = document.createElement('link');
    alternate.setAttribute('rel', 'alternate');
    alternate.setAttribute('hreflang', hrefLang);
    alternate.setAttribute('href', href);
    head.append(alternate);
  }
}
