// The footer strip and the staleness banner as a page in the browser follows its answers (the plan's items 6.1 and 6.5):
// each answer the page receives (`askV3` announces every one, `freshness.mjs`) draws both again from that answer's facts,
// when they differ from those shown. Browser only: the server renderer never loads it (`Freshness.jsx` is what it renders).

import { createRoot } from 'react-dom/client';
import { ANSWER_EVENT, freshnessFacts, freshnessKey } from '../scripts/freshness.mjs';
import { FRESHNESS_STRIP_ID, FreshnessStrip, STALENESS_BANNER_ID, StalenessBanner } from './Freshness.jsx';

const roots = new WeakMap();

function draw(container, element, key) {
  if (container === null || container.dataset.freshnessKey === key) return;
  let root = roots.get(container);
  if (root === undefined) {
    // What the server rendered here is not hydrated: it is drawn afresh from the newer facts.
    container.replaceChildren();
    root = createRoot(container);
    roots.set(container, root);
  }
  container.dataset.freshnessKey = key;
  root.render(element);
}

/**
 * Draws the strip and the banner from `envelope`'s facts, when it carries facts a page can read and they differ from those
 * shown; returns whether it drew.
 */
export function showFreshness(document, envelope) {
  const facts = freshnessFacts(envelope);
  if (facts === null) return false;
  const key = freshnessKey(facts);
  draw(document.getElementById(FRESHNESS_STRIP_ID), <FreshnessStrip facts={facts} />, key);
  draw(document.getElementById(STALENESS_BANNER_ID), <StalenessBanner facts={facts} />, key);
  return true;
}

const followed = new WeakSet();

/** Follows every answer the page receives (`askV3` announces each), once per document. */
export function followAnswers(document) {
  if (followed.has(document)) return;
  followed.add(document);
  document.addEventListener(ANSWER_EVENT, (event) => {
    showFreshness(document, event.detail);
  });
}
