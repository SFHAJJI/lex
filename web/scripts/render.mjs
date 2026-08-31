// Static page generation for the S0-06 preview surface.
//
// Every page is produced at build time and ships as inert HTML. There is no client
// script, so the browser cannot infer, resolve, retry, or decide anything. That is
// the product rule made structural rather than promised.
//
// Three of the five states carry no envelope by construction. `loading` has nothing
// to render yet; `transport_failure` never received one; `invalid_envelope` received
// bytes it refused to trust. Inventing content for any of them would be precisely the
// failure this surface exists to prevent, so each renders its own state and nothing
// about the law.

/** Escape for HTML text and quoted attribute contexts. */
export function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

const SYNTHETIC_MARKER = "lex-v3-synthetic-preview";

/**
 * The banner every generated page carries.
 *
 * The machine-readable marker exists so a build assertion can prove the banner was
 * not dropped by editing a template. A page that reaches a browser without it is not
 * a preview page; it is an unlabelled page that looks like one.
 */
function syntheticBanner() {
  return `<aside class="synthetic" role="note" data-synthetic="${SYNTHETIC_MARKER}">
      <strong>Synthetic preview.</strong>
      This page is generated from a synthetic fixture. It is not law, not promotable,
      and describes no real legal record.
    </aside>`;
}

function page({ state, title, main }) {
  return `<!doctype html>
<html lang="en" data-product-line="lex-v3" data-preview-state="${escapeHtml(state)}">
  <head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>${escapeHtml(title)} - Lex V3 preview</title>
    <link rel="stylesheet" href="./styles.css">
  </head>
  <body>
    ${syntheticBanner()}
    <main id="main">
${main}
    </main>
  </body>
</html>
`;
}

/**
 * Nothing has arrived yet. The page says so and shows no legal content, because a
 * skeleton that mimics a result teaches a reader to expect one.
 */
export function renderLoading() {
  return page({
    state: "loading",
    title: "Loading",
    main: `      <h1>Loading</h1>
      <p class="state">The request has not returned yet. Nothing is known about the
        requested coordinate, and nothing is shown.</p>`,
  });
}

/**
 * The request never produced an envelope. This is a fact about the transport, and
 * the page must not let it read as a fact about the law: an unreachable service is
 * not an absent record.
 */
export function renderTransportFailure({ reason }) {
  return page({
    state: "transport_failure",
    title: "Request did not complete",
    main: `      <h1>The request did not complete</h1>
      <p class="state">${escapeHtml(reason)}</p>
      <p class="boundary">This says nothing about whether the requested record
        exists. No answer was received, so no absence can be claimed.</p>`,
  });
}

/**
 * Bytes arrived and were refused. The page renders the reasons and no content at
 * all: a partially rendered page from an envelope that failed validation is the
 * worst outcome available, because it looks like an answer.
 */
export function renderInvalidEnvelope({ problems }) {
  const items = problems
    .map((problem) => `        <li>${escapeHtml(problem)}</li>`)
    .join("\n");
  return page({
    state: "invalid_envelope",
    title: "Response refused",
    main: `      <h1>The response was refused</h1>
      <p class="state">A response arrived but did not satisfy the published envelope
        schema, so none of it is shown.</p>
      <ul class="problems">
${items}
      </ul>
      <p class="boundary">This says nothing about whether the requested record
        exists.</p>`,
  });
}

export const SYNTHETIC_MARKER_VALUE = SYNTHETIC_MARKER;
