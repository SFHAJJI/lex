// Turning a React element into the bytes a browser receives.
//
// `renderToStaticMarkup` emits no doctype and no trailing newline, so both are added here
// rather than in every page. Static markup, not `renderToString`: these pages carry no
// hydration ids because the law-bearing surfaces ship no client runtime. Interactive
// surfaces render through their own entry, which does hydrate.

import { renderToStaticMarkup } from 'react-dom/server';

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
