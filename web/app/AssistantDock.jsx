// The assistant docked beside the page (the owner's brief of 2026-11-15, section 5, "Assistant panel"): 384 px, in the same
// place on Home, the text page and Changes, can be hidden, and its conversation survives navigation.
//
// PLACEHOLDER written by the driver so the shell can mount the dock; the assistant agent replaces this file's body (keep the
// export names: `AssistantDock`, `LIVE_DOCK_ROOT`, `liveDockTree`).

import { CHROME_LOCALE } from '../scripts/live-chrome.mjs';

/** The dock's root element's id: the server renders the dock into it and `client-live-dock-entry.jsx` hydrates it. */
export const LIVE_DOCK_ROOT = 'lx-dock-root';

/** The dock as rendered before any conversation: the same tree on the server and in the browser's first render. */
export function AssistantDock({ locale = CHROME_LOCALE, page = null }) {
  return <div className="lx-dock-inner" data-dock-page={page ?? ''} data-dock-locale={locale} />;
}

/** The tree the server renders and the browser hydrates. */
export function liveDockTree(page = null) {
  return <AssistantDock locale={CHROME_LOCALE} page={page} />;
}
