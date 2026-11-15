// The text page's address (the owner's brief of 2026-11-15, section 3): one page per text, `text.html` under an interface
// language, its state in the query so that every state is a link that can be copied and reopened:
//
//   text     the text, by the identifier the API names it with (`/lu-legilux/loi-1997-07-27-n10`, or an EU CELEX)
//   date     the version read, as the date the reader asked for (yyyy-mm-dd); none is today
//   article  the article open, by the publisher's id the API names it with (`art_44`)
//   from, to the two versions compared (yyyy-mm-dd each): compare mode
//   view     `inline` for compare mode's "In one text"; none is side by side
//
// None of these is a word a reader typed (R109): a law's identifier, dates and an article's id. The old addresses (a law's
// own address, the reading, history and compare pages) forward here once their script runs.

import { europeWorkSegment, INTERFACE_LANGUAGES } from './permalinks.mjs';
import { CHROME_LOCALE } from './live-chrome.mjs';

const DATE = /^[0-9]{4}-[0-9]{2}-[0-9]{2}$/u;
const ARTICLE = /^[^\s/?#]{1,120}$/u;

/** The site path of a text, by the identifier the API names it with (`/lu-legilux/{work}` or an EU CELEX), or null. */
export function textPath(identifier) {
  if (typeof identifier !== 'string' || identifier.length === 0) return null;
  if (/^\/lu-legilux\/[^/?#]+$/u.test(identifier)) return identifier;
  if (/^\/eu-eurlex\/[^/?#]+$/u.test(identifier)) return identifier;
  if (/^[0-9A-Za-z][0-9A-Za-z()._/-]*$/u.test(identifier)) return `/eu-eurlex/${europeWorkSegment(identifier)}`;
  return null;
}

/** The text page's file under each interface language. */
export const TEXT_PAGE = 'text.html';

/**
 * The text page's link for a state, under `locale`'s path: the text, and any of the version's date, the article, the compare
 * pair and the view. A value that is not of its shape is left out, never written. Null when the identifier names no text.
 */
export function textHref({ identifier, date = null, article = null, from = null, to = null, view = null }, locale = CHROME_LOCALE) {
  if (textPath(identifier) === null || !INTERFACE_LANGUAGES.includes(locale)) return null;
  // Written by hand, not with URLSearchParams: the API's server renderer runs in an engine that has none (as on 2026-11-14).
  const parts = [`text=${encodeURIComponent(identifier)}`];
  const compare = typeof from === 'string' && DATE.test(from) && typeof to === 'string' && DATE.test(to);
  if (!compare && typeof date === 'string' && DATE.test(date)) parts.push(`date=${date}`);
  if (compare) parts.push(`from=${from}`, `to=${to}`);
  if (typeof article === 'string' && ARTICLE.test(article)) parts.push(`article=${encodeURIComponent(article)}`);
  if (compare && view === 'inline') parts.push('view=inline');
  return `/${locale}/${TEXT_PAGE}?${parts.join('&')}`;
}

/** The state a text page's query names (`location.search`), each value null when absent or not of its shape. */
export function textStateOf(search) {
  // Read by hand too (no URLSearchParams in the server renderer's engine); a value that does not decode is absent.
  const query = new Map();
  for (const pair of String(typeof search === 'string' ? search : '').replace(/^\?/u, '').split('&')) {
    if (pair.length === 0) continue;
    const at = pair.indexOf('=');
    const key = at < 0 ? pair : pair.slice(0, at);
    let value = at < 0 ? '' : pair.slice(at + 1);
    try {
      value = decodeURIComponent(value.replaceAll('+', ' '));
    } catch {
      continue;
    }
    if (!query.has(key)) query.set(key, value);
  }
  const get = (key) => (query.has(key) ? query.get(key) : null);
  const text = get('text');
  const date = get('date');
  const article = get('article');
  const from = get('from');
  const to = get('to');
  const compare = from !== null && DATE.test(from) && to !== null && DATE.test(to);
  return Object.freeze({
    identifier: text !== null && textPath(text) !== null ? text : null,
    date: date !== null && DATE.test(date) ? date : null,
    article: article !== null && ARTICLE.test(article) ? article : null,
    from: compare ? from : null,
    to: compare ? to : null,
    view: compare && get('view') === 'inline' ? 'inline' : 'side',
  });
}

/**
 * The events the text page and the assistant dock speak to each other with, on `window` (both are on the same page, each
 * its own React root):
 *
 *   lex:open  detail { identifier, date, article, highlight }   the dock asks the page to open a text at an article; a text
 *             page standing at that text calls preventDefault() and opens it in place; otherwise the dock navigates to
 *             textHref(detail).
 *   lex:dock  detail { collapsed }                               the text page asks the dock to fold to its tab (compare mode)
 *             or to open again.
 */
export const OPEN_EVENT = 'lex:open';
export const DOCK_EVENT = 'lex:dock';
