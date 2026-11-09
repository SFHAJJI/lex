// The assistant's page, live: `/fr/assistant` and `/en/assistant` (R47, R55; docs/ANSWER-DOSSIER-1.md).
//
// It exists only while the server reports the assistant switched on. The server renders it, from its coverage answer for the
// freshness header every page carries (R87; `ssr-entry.jsx`, kind `assistant`), and serves it and its script only then
// (V3WebRoot): while the assistant is off, both are paths the site does not hold, and no page links to it (R52). It is never
// built as a file and never indexed. `client-live-assistant-entry.jsx` hydrates the same tree, and the panel asks `ask`
// when the reader submits a question.

import { Document } from './Document.jsx';
import { LiveAssistant } from './AssistantPanel.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { CHROME_LOCALE, livePath } from '../scripts/live-chrome.mjs';
import { panelCopy } from '../scripts/assistant-panel.mjs';

export const LIVE_ASSISTANT_ROOT = 'live-assistant-root';

/** The tree the server renders and the browser hydrates: the live panel, in the bundle's language. */
export function liveAssistantTree() {
  return <LiveAssistant locale={CHROME_LOCALE} />;
}

/** The assistant's page. `freshness` is what the coverage answer it was rendered from says of the law's freshness (R87). */
export function renderLiveAssistantPage({ freshness = null } = {}) {
  const copy = panelCopy(CHROME_LOCALE);
  return renderHydratableDocument(
    <Document state="live-assistant" title={copy.heading} shell="ask" density={skinFor('ask').density} banner="live" indexing="never" freshness={freshness}>
      <div id={LIVE_ASSISTANT_ROOT}>{liveAssistantTree()}</div>
      <script src={livePath('client-live-assistant.js')} defer />
    </Document>,
  );
}
