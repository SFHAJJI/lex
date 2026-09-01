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

import { CHROME_LOCALES } from "./localization.mjs";
import { tryPublisherSourceUri } from "./routes.mjs";

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

// Exported so every page in this line goes through one shell. The shell is what carries the
// synthetic banner and data-preview-state, and a page that builds its own head forgets them:
// the trust surface did exactly that and the browser run caught it.
export function page({
  state,
  title,
  main,
  locale = "en",
  copyLocale = "en",
  shell = null,
  density = null,
}) {
  // `lang` is the page's own language, never the subject's. A work page is English chrome
  // about a French law and stays `en`; a page of French statute is `fr`. The locale is
  // checked against the reviewed four, because a tag nobody reviewed the chrome in tells a
  // screen reader to use a voice for text that was never written in that language.
  if (!CHROME_LOCALES.includes(locale)) {
    throw new Error(`${JSON.stringify(locale)} is not one of the four chrome locales`);
  }
  if (!CHROME_LOCALES.includes(copyLocale)) {
    throw new Error(`${JSON.stringify(copyLocale)} is not one of the four chrome locales`);
  }
  // The comment above stated this rule and the code enforced a weaker one, which is how a
  // request for German returned English prose under `lang="de"` on every page in this build.
  // Being one of the four locales is not the same as being the language this page is written
  // in, and only the second is what the tag asserts. Decision 63 forbids substituting a
  // locale, and a substitution wearing the requested tag is the substitution nobody can see.
  if (locale !== copyLocale) {
    throw new Error(
      `this page would be labelled ${locale} while its copy is written in ${copyLocale}; a ` +
        "screen reader would read one language in the voice of another, and a reader would " +
        "have been served a locale nobody reviewed",
    );
  }
  // The shell rides on the root element as data attributes and nowhere else. A stylesheet
  // can select on them; nothing in the render path can branch on them, because `page` has
  // already been handed the finished `main`.
  const shellAttributes =
    shell === null
      ? ""
      : ` data-shell="${escapeHtml(shell)}" data-density="${escapeHtml(density ?? "")}"`;
  return `<!doctype html>
<html lang="${escapeHtml(locale)}" data-product-line="lex-v3" data-preview-state="${escapeHtml(state)}"${shellAttributes}>
  <head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>${escapeHtml(title)} - Lex V3 preview</title>
    <link rel="icon" href="./favicon.svg" type="image/svg+xml">
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
 * expanding to the exact identities. Every row is read from `context`; a row whose value
 * is absent is omitted rather than defaulted, because a strip that invents a digest is
 * worse than no strip.
 *
 * Note what is deliberately absent: the two-clocks convention. This envelope is a
 * synthetic preview-mechanics response. It carries no legal time, no valid_from and no
 * timeline_semantics, so rendering a legal clock would mean inventing one.
 */
function envelopeStrip(context) {
  const rows = [
    ["Operation", context.operation?.operation_id],
    ["Operation catalog", context.operation?.catalog_id],
    ["Catalog digest", context.operation?.catalog_sha256],
    ["Refusal registry", context.refusal_registry?.registry_id],
    ["Registry digest", context.refusal_registry?.sha256],
    ["Snapshot", context.snapshot?.snapshot_id],
    ["Snapshot digest", context.snapshot?.snapshot_sha256],
    ["Artifact digest", context.artifact?.sha256],
    ["Index schema", context.index?.schema],
    ["Index digest", context.index?.sha256],
    ["Index build", context.index?.build_id],
    ["Runtime", context.runtime?.component_id],
    ["Runtime source", context.runtime?.source_sha256],
    ["Builder", context.builder?.component_id],
    ["Builder source", context.builder?.source_sha256],
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
            <span class="summary-value">${escapeHtml(context.operation?.operation_id ?? "")} /
              ${escapeHtml(context.snapshot?.snapshot_id ?? "")}</span>
          </summary>
          <dl class="strip">
${items}
          </dl>
        </details>
      </aside>`;
}

function definition(label, value) {
  if (value === undefined || value === null || value === "") {
    return "";
  }
  return `          <div><dt>${escapeHtml(label)}</dt><dd><code>${escapeHtml(value)}</code></dd></div>`;
}

/**
 * A successful envelope.
 *
 * Every vocabulary value here has already been resolved from its wire index by
 * `decodeEnvelope`. The renderer never sees an integer where a reader expects a name,
 * and never carries a lookup table of its own.
 */
export function renderSuccess({ envelope }) {
  const { result, context } = envelope;
  const objects = (result?.objects ?? [])
    .map(
      (object) => `        <article class="object">
          <h2>${escapeHtml(object.object_id ?? "")}</h2>
          <dl class="result">
${[
  definition("Work", object.work_id),
  definition("Version", object.version_key),
  definition("Anchor", object.anchor),
  definition("Holding state", object.body_holding_state),
  definition("Holding disposition", object.body_holding_disposition),
  definition("Body digest", object.body_sha256),
]
  .filter(Boolean)
  .join("\n")}
          </dl>
${
  object.body === undefined
    ? ""
    : `          <pre class="body" aria-label="Synthetic body text">${escapeHtml(object.body)}</pre>`
}
        </article>`,
    )
    .join("\n");

  return page({
    state: "success",
    title: "Preview object set",
    main: `      <h1>
        <span class="icon" aria-hidden="true">&#10003;</span>
        <span class="label">Coordinate resolved</span>
      </h1>
      <dl class="result">
${[
  definition("Matched coordinate", envelope.matched_coordinate),
  definition("Identifier family", envelope.matched_identifier_family),
  definition("Object set", result?.object_set_id),
  definition("Object set schema", result?.schema),
]
  .filter(Boolean)
  .join("\n")}
      </dl>
${objects}
      <p class="boundary">Every value above is synthetic. The body text carries no legal
        authority, and this page makes no statement about any law.</p>
${envelopeStrip(context)}`,
  });
}

/**
 * The official routes, as real links.
 *
 * These are the handoff the pack requires: a refusal that names where to look next is an
 * answer, and one that names it as unclickable text is a smaller answer. A route that
 * the one route policy in `routes.mjs` refuses is rendered as inert text with the reason,
 * because a
 * destination this surface cannot vouch for must not become something a reader can
 * activate from an otherwise inert page.
 */
function officialRoutes(actions) {
  if (!Array.isArray(actions) || actions.length === 0) {
    return "";
  }
  const items = actions
    .map((action) => {
      const label = `${action.publisher}: ${action.uri}`;
      const href = tryPublisherSourceUri(action.publisher, String(action.uri));
      const inner = href
        ? `<a href="${escapeHtml(href)}" rel="noreferrer noopener">${escapeHtml(label)}</a>`
        : `${escapeHtml(label)} <span class="note">(not linked: not an official host for this publisher)</span>`;
      return `          <li>${inner}</li>`;
    })
    .join(String.fromCharCode(10));
  return `        <section class="payload">
          <h2>Official search routes</h2>
          <ul>
${items}
          </ul>
        </section>`;
}

/**
 * A refusal, rendered as an answer.
 *
 * 35-ideal-ux.md section 1 is explicit that refusals are never red error toasts: the
 * card is neutral, the machine code is shown verbatim in monospace beside one human
 * sentence, the helpful payload is the body, and the official routes are the footer.
 *
 * `asserts_absence_of_law` is rendered explicitly rather than assumed. A refusal that
 * silently left it out would read as an absence claim, which is the single most damaging
 * thing this surface could imply.
 */
export function renderRefusal({ envelope }) {
  const { refusal, context } = envelope;

  const list = (label, values, format) => {
    if (!Array.isArray(values) || values.length === 0) {
      return "";
    }
    const items = values
      .map((value) => `          <li>${escapeHtml(format ? format(value) : value)}</li>`)
      .join("\n");
    return `        <section class="payload">
          <h2>${escapeHtml(label)}</h2>
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
${[
  definition("Requested coordinate", refusal.requested_coordinate),
  definition("Identifier family checked", refusal.checked_identifier_family),
]
  .filter(Boolean)
  .join("\n")}
        </dl>
${list("Publisher contexts checked", refusal.publisher_contexts_checked)}
${list(
  "Records that may be held",
  refusal.possible_held_records,
  (record) => `${record.coordinate} (${record.identifier_family}, ${record.publisher})`,
)}
${list("What would answer this", refusal.what_would_answer)}
${officialRoutes(refusal.official_search_actions)}
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
