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

/**
 * The provenance strip, pinned to every envelope-bearing page.
 *
 * 35-ideal-ux.md section 1 requires a collapsed one-liner that never scrolls away,
 * expanding to the exact identities. Every value here is read from `context`; there
 * is no default and no computed fallback, because a strip that invents a digest is
 * worse than no strip at all.
 *
 * Note what is deliberately absent: the two-clocks convention. This envelope declares
 * `capabilities: preview_mechanics_only` and carries no legal time, no valid_from and
 * no timeline_semantics. Rendering a legal clock here would mean inventing one.
 */
function envelopeStrip(context) {
  const rows = [
    ["Jurisdiction", context.jurisdiction],
    ["Capabilities", context.capabilities],
    ["Source kind", context.source?.source_kind],
    ["Observed at", context.freshness?.observed_at],
    ["Upstream health", context.freshness?.upstream_health],
    ["Index format", context.index_format],
    ["Snapshot", context.snapshot?.snapshot_sha256],
    ["Artifact", context.artifact?.artifact_id],
    ["Runtime source", context.runtime?.source_sha256],
    ["Builder source", context.builder?.source_sha256],
    ["Operation catalog", context.operation?.catalog_sha256],
    ["Refusal registry", context.refusal_registry?.sha256],
    ["Request", context.request_ref],
  ].filter(([, value]) => value !== undefined && value !== null);

  const items = rows
    .map(
      ([label, value]) =>
        `          <div class="strip-row"><dt>${escapeHtml(label)}</dt>` +
        `<dd><code>${escapeHtml(value)}</code></dd></div>`,
    )
    .join("\n");

  return `      <aside class="envelope-strip" aria-label="Provenance">
        <details>
          <summary>
            <span class="icon" aria-hidden="true">&#9635;</span>
            <span class="label">Provenance</span>
            <span class="summary-value">${escapeHtml(context.jurisdiction ?? "")} /
              ${escapeHtml(context.freshness?.observed_at ?? "")}</span>
          </summary>
          <dl class="strip">
${items}
          </dl>
        </details>
      </aside>`;
}

/**
 * A successful envelope. It names an object set and its digest, and nothing else is
 * claimed: this envelope's capability is `preview_mechanics_only`, so the page
 * describes the mechanics it proves and makes no statement about any law.
 */
export function renderSuccess({ envelope }) {
  const { result, context } = envelope;
  return page({
    state: "success",
    title: "Preview object set",
    main: `      <h1>
        <span class="icon" aria-hidden="true">&#10003;</span>
        <span class="label">Object set resolved</span>
      </h1>
      <p class="state">The request returned an envelope for operation
        <code>${escapeHtml(context.operation?.operation_id ?? "")}</code>.</p>
      <dl class="result">
        <div><dt>Object set</dt><dd><code>${escapeHtml(result.object_set_id)}</code></dd></div>
        <div><dt>Digest</dt><dd><code>${escapeHtml(result.object_set_sha256)}</code></dd></div>
      </dl>
      <p class="boundary">This envelope declares
        <code>${escapeHtml(context.capabilities ?? "")}</code>. It carries no legal
        time, no publisher wording and no applicability, so this page states none.</p>
${envelopeStrip(context)}`,
  });
}

/**
 * A refusal, rendered as an answer.
 *
 * 35-ideal-ux.md section 1 is explicit that refusals are never red error toasts: the
 * card is neutral, the machine code is shown verbatim in monospace beside one human
 * sentence, the helpful payload is the body, and the official routes are the footer.
 *
 * `asserts_absence_of_law` is rendered explicitly rather than assumed. A refusal that
 * silently left it out would read as an absence claim, which is the single most
 * damaging thing this surface could imply.
 */
export function renderRefusal({ envelope }) {
  const { refusal, context } = envelope;

  const list = (label, values) => {
    if (!Array.isArray(values) || values.length === 0) {
      return "";
    }
    const items = values
      .map((value) => `          <li>${escapeHtml(value)}</li>`)
      .join("\n");
    return `        <section class="payload">
          <h3>${escapeHtml(label)}</h3>
          <ul>
${items}
          </ul>
        </section>`;
  };

  const absence = refusal.asserts_absence_of_law === true;

  return page({
    state: "refusal",
    title: `Refusal: ${refusal.code}`,
    main: `      <div class="refusal-card" role="group" aria-labelledby="refusal-code">
        <h1 class="refusal-header">
          <span class="icon" aria-hidden="true">&#128737;</span>
          <code id="refusal-code" class="code-chip">${escapeHtml(refusal.code)}</code>
          <span class="label">The requested identifier was not recognised.</span>
        </h1>
        <dl class="result">
          <div><dt>Requested coordinate</dt>
            <dd><code>${escapeHtml(refusal.requested_coordinate)}</code></dd></div>
          <div><dt>Identifier family checked</dt>
            <dd><code>${escapeHtml(refusal.checked_identifier_family)}</code></dd></div>
        </dl>
${list("Publisher contexts checked", refusal.publisher_contexts_checked)}
${list("Records that may be held", refusal.possible_held_records)}
${list("What would answer this", refusal.what_would_answer)}
${list("Official search routes", refusal.official_search_actions)}
        <p class="boundary">${
          absence
            ? "This response asserts the absence of a law."
            : "This response does <strong>not</strong> assert that no such law exists. " +
              "The identifier was not recognised here; that is a fact about this index, " +
              "not about the law."
        }</p>
      </div>
${envelopeStrip(context)}`,
  });
}

export const SYNTHETIC_MARKER_VALUE = SYNTHETIC_MARKER;
