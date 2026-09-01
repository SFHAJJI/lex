// Turning a React element into the bytes a browser receives.
//
// `renderToStaticMarkup` emits no doctype and no trailing newline, so both are added here
// rather than in every page. Static markup, not `renderToString`: these pages carry no
// hydration ids because the law-bearing surfaces ship no client runtime. Interactive
// surfaces render through their own entry, which does hydrate.

import { renderToStaticMarkup, renderToString } from 'react-dom/server';

/**
 * Render a document element to a complete HTML response.
 *
 * @param {React.ReactElement} element a `<Document>` element, not a fragment
 * @returns {string} the full response body, doctype first
 */
export function renderDocument(element) {
  const markup = renderToStaticMarkup(element);
  if (!markup.startsWith('<html')) {
    throw new Error(
      'renderDocument was handed something that is not a whole document; the shell is what ' +
        'carries the synthetic banner, the preview state and the asset links, and a page that ' +
        'renders around it loses all three silently',
    );
  }
  return `<!doctype html>\n${markup}\n`;
}

/**
 * Render a document that will be hydrated.
 *
 * `renderToStaticMarkup` deliberately omits the markers React uses to match a client tree to
 * server markup, so a page rendered with it cannot be hydrated: React reports a recoverable
 * error and re-renders the whole root on the client. That is precisely the failure the hydration
 * gate exists to catch, and it caught this one in my own proof page.
 *
 * Static markup stays the default, because a page that ships no script should not carry
 * hydration markers it will never use. This is the opt-in for the pages that do.
 */
export function renderHydratableDocument(element) {
  const markup = renderToString(element);
  if (!markup.startsWith('<html')) {
    throw new Error(
      'renderHydratableDocument was handed something that is not a whole document; the shell ' +
        'is what carries the synthetic banner, the preview state and the asset links',
    );
  }
  return `<!doctype html>
${markup}
`;
}
