// The client runtime.
//
// #360 requires runtime browser evidence for hydration, and hydration is the thing that makes
// every later interactive requirement possible: roving tabindex on results, focus containment in
// the ambiguous-version interstitial, Escape without selection, pressed-state filters. None of
// them can be built or measured until a component is genuinely alive in the browser.
//
// The rule this file exists to enforce is narrower than "React works". It is that hydration must
// change nothing a reader can see. The server already rendered the legal text; the client attaches
// behaviour to it. If hydration alters the markup, then what the reader was shown before the
// script ran and what they are shown after are two different documents, and only one of them was
// the one the server can be held to. React calls that a hydration mismatch and recovers by
// re-rendering, silently, which is exactly the failure this product cannot tolerate: the page
// would still look fine.
//
// So the client asserts a clean hydration rather than hoping for one, and the browser run
// measures that assertion rather than trusting this comment.

import { useEffect } from 'react';
import { hydrateRoot } from 'react-dom/client';
import { parseSitePath } from '../scripts/permalinks.mjs';
import { writeLanguageLinks } from './page-address.mjs';
import { addressBeforeAnswer } from '../scripts/initial-answer.mjs';
import { writeLawPageLinks } from './head-links.mjs';
import { followAnswers } from './freshness-client.jsx';

/**
 * Attach behaviour to server-rendered markup, and say so.
 *
 * The flag is read by the browser evidence run. It is set only after hydration completes without
 * React reporting a recoverable error, so a page that hydrated by silently re-rendering does not
 * get to claim it hydrated cleanly.
 *
 * @param {Element} container the server-rendered root to attach to
 * @param {React.ReactElement} element the same tree the server rendered
 */
export function attach(container, element) {
  if (!container) {
    throw new Error('nothing to hydrate; the server-rendered root is missing from the document');
  }

  // Recorded on the element rather than in a closure, because the flag is written from an effect
  // that commits after React has already reported any recoverable error, and the two need to see
  // the same value.
  const state = { mismatched: false };

  // The interface languages lead to this same page in each, its article included (R61), with the date a readable address
  // keeps, the page a reading names and the fragment (`sameAddressIn`): the build wrote their home pages, which a reader
  // without scripts follows. The links stand outside the hydrated root, so React reads none of this; a page that writes its
  // own address writes them again (`page-address.mjs`).
  writeLanguageLinks(document, window.location);

  // A law's page as built (served when the server could not render it) names no canonical link: it names an address the
  // platform mints, with no interface language (R64, R67), never the request's own (`addressBeforeAnswer`): a reading's
  // state's permalink with no article, which the reading page replaces with what its answer shows once loaded
  // (`readingAddressShown`; review of #967: any article segment named itself canonical). The head is outside the root too.
  if (parseSitePath(window.location.pathname)?.locale && document.head.querySelector('link[rel="canonical"]') === null) {
    writeLawPageLinks(document, addressBeforeAnswer(window.location.pathname));
  }

  // The footer's strip and the staleness banner follow every answer the page receives (R87, R23): both stand outside the
  // hydrated root, so React's hydration reads neither.
  followAnswers(document);

  hydrateRoot(container, <Hydrated state={state}>{element}</Hydrated>, {
    // React recovers from a hydration mismatch by re-rendering the subtree, quietly. A page whose
    // text changed between the server response and the hydrated result is a page where the reader
    // saw one document and kept another.
    onRecoverableError: (error) => {
      state.mismatched = true;
      document.documentElement.dataset.hydrationRecovered = String(error?.message ?? error);
      document.documentElement.dataset.hydrated = 'recovered';
    },
  });
}

/**
 * Reports hydration from a commit-time effect.
 *
 * The first version set the flag in a microtask, which runs before hydration finishes, so it
 * reported clean before React could say otherwise and a deliberate mismatch passed the gate. An
 * effect commits after the hydration pass, by which point any recoverable error has been
 * reported. Found by mutating the tree and watching the gate not fire.
 */
function Hydrated({ state, children }) {
  useEffect(() => {
    if (!state.mismatched) {
      document.documentElement.dataset.hydrated = 'clean';
    }
  }, [state]);
  return children;
}
