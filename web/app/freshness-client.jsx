// The footer strip and the staleness banner as a page in the browser follows its answers (the plan's items 6.1 and 6.5):
// each answer the page receives (`askV3` announces every one, `freshness.mjs`) draws both again from that answer's facts,
// when they differ from those shown. Browser only: the server renderer never loads it (`Freshness.jsx` is what it renders).
//
// repair-pages (22/11/2026): the strip also says, first, when the assistant's own index was built (the engine's data, read
// once per page from `GET /api/next/config`, which carries no word of a question); the reading service's index, which the
// text and changes pages read, is then named as that service's.

import { createRoot } from 'react-dom/client';
import { ANSWER_EVENT, freshnessFacts, freshnessKey } from '../scripts/freshness.mjs';
import { FRESHNESS_STRIP_ID, FreshnessStrip, STALENESS_BANNER_ID, StalenessBanner, engineIndexFacts } from './Freshness.jsx';

// A page may load two bundles that both follow its answers (a page's own script and the dock's): what they draw and know is
// kept on the document and its containers under one global key, so both draw into one root from the same facts.
const SHARED = Symbol.for('lex.freshness');
const shared = (node) => {
  if (node[SHARED] === undefined) node[SHARED] = {};
  return node[SHARED];
};

function draw(container, element, key) {
  if (container === null || container.dataset.freshnessKey === key) return;
  const own = shared(container);
  if (own.root === undefined) {
    // What the server rendered here is not hydrated: it is drawn afresh from the newer facts.
    container.replaceChildren();
    own.root = createRoot(container);
  }
  container.dataset.freshnessKey = key;
  own.root.render(element);
}

/** Draws the strip from the facts known for this document (the engine's, the reading service's last). */
function drawStrip(document) {
  const facts = shared(document).facts ?? null;
  const engine = shared(document).engine ?? null;
  const key = `${freshnessKey(facts)}|${engine === null ? 'no-engine' : JSON.stringify(engine)}`;
  const container = document.getElementById(FRESHNESS_STRIP_ID);
  // A page that never asks the server (no answer) keeps its "no answer" strip unless the engine's facts are known.
  if (facts === null && engine === null) return;
  draw(container, <FreshnessStrip facts={facts} engine={engine} />, key);
}

/**
 * Draws the strip and the banner from `envelope`'s facts, when it carries facts a page can read and they differ from those
 * shown; returns whether it drew.
 */
export function showFreshness(document, envelope) {
  const facts = freshnessFacts(envelope);
  if (facts === null) return false;
  shared(document).facts = facts;
  drawStrip(document);
  draw(document.getElementById(STALENESS_BANNER_ID), <StalenessBanner facts={facts} />, freshnessKey(facts));
  return true;
}

/** Reads the engine's configuration once for a document and draws the strip with its index facts. */
export async function showEngineIndex(document, fetchImpl = globalThis.fetch) {
  const own = shared(document);
  if (own.asked) return false;
  own.asked = true;
  try {
    if (typeof fetchImpl !== 'function') return false;
    const response = await fetchImpl('/api/next/config', { method: 'GET', headers: { accept: 'application/json' } });
    if (!response.ok) return false;
    const engine = engineIndexFacts(await response.json());
    if (engine === null) return false;
    own.engine = engine;
    drawStrip(document);
    return true;
  } catch {
    return false;
  }
}

/** Follows every answer the page receives (`askV3` announces each), once per document; reads the engine's index once. */
export function followAnswers(document) {
  const own = shared(document);
  if (own.followed) return;
  own.followed = true;
  document.addEventListener(ANSWER_EVENT, (event) => {
    showFreshness(document, event.detail);
  });
  showEngineIndex(document);
}
