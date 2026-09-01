// Build the preview surface.
//
// The static shell is copied, then all five state pages are generated. The two
// data-bearing pages render captured envelopes produced by the merged C# production
// path, never fabricated ones: two of the envelope's digests are computed at request
// time over a canonical serialisation of a typed object graph, and a JavaScript
// reimplementation differing by one byte would still yield a well-formed hex string that
// validates. A plausible wrong digest is worse than a missing one.
//
// Every captured envelope is decoded and validated before it is rendered. The wire
// format encodes closed vocabularies as integer indices, so an undecoded envelope would
// put bare numbers where a reader expects machine codes.

import { cp, mkdir, readdir, readFile, rm, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";

import { loadCaptured } from "./captured-envelopes.mjs";
import { tokenCss } from "./design-tokens.mjs";
import { renderTrustSurface } from "./trust-surface.mjs";
import { renderShellEntry } from "./shells.mjs";
import { renderRefusalCatalog } from "./refusal-catalog.mjs";
import { renderComparePreview } from "./compare-preview.mjs";
import { renderTimelinePreview } from "./timeline-preview.mjs";
import { renderProvisionHistoryPreview } from "./provision-history-preview.mjs";
import { renderGetHelpPreview } from "./get-help-preview.mjs";
import { renderExportComposerPreview } from "./export-composer-preview.mjs";
import { renderCitationCheckerPreview } from "./citation-checker-preview.mjs";
import { renderCoveragePreview } from "./coverage-preview.mjs";
import { renderSearchPreview } from "./search-preview.mjs";
import { renderDossierPreview } from "./dossier-preview.mjs";
import { renderReadingPreview } from "./reading-preview.mjs";
import { provenancePreviewPages } from "./provenance-preview.mjs";
import { renderLocaleUnavailable, REVIEWED_CHROME_LOCALES } from "./locale-unavailable.mjs";
import { CHROME_LOCALES } from "./localization.mjs";
import { page } from "./render.mjs";
import { SHELLS } from "./urls.mjs";
import { decodeEnvelope, validateEnvelope } from "./envelope.mjs";
import {
  renderLoading,
  renderTransportFailure,
  renderInvalidEnvelope,
  renderSuccess,
  renderRefusal,
} from "./render.mjs";

const source = new URL("../src/", import.meta.url);
const destination = new URL("../dist/", import.meta.url);

await rm(destination, { force: true, recursive: true });
await mkdir(destination, { recursive: true });
await cp(source, destination, { recursive: true });

// The pages copied in rather than generated. They are pages a reader can reach, so they are
// declared and measured like any other; the entry page shipped unmeasured for exactly as
// long as nothing named it.
const copiedPages = (await readdir(source)).filter((name) => name.endsWith(".html"));

// The semantic tokens are appended from `design-tokens.mjs` rather than written into
// `src/styles.css`. Two copies of a colour is two sources of truth, and the one that drifts
// is always the one nobody tested. A test asserts the stylesheet source does not define them.
const stylesheet = new URL("styles.css", destination);
await writeFile(
  stylesheet,
  `${await readFile(stylesheet, "utf8")}
/* Semantic tokens, generated from scripts/design-tokens.mjs. */
${tokenCss()}`,
  "utf8",
);

// Each page states one thing that is true and nothing about any law. The reasons are
// concrete rather than generic, because "an error occurred" teaches a reader nothing
// and invites them to guess.
const pages = [
  ["state-loading.html", renderLoading()],
  [
    "state-transport-failure.html",
    renderTransportFailure({
      reason: "the connection closed before any response header arrived",
    }),
  ],
  [
    "state-invalid-envelope.html",
    renderInvalidEnvelope({
      problems: [
        'branch "sideways" is not one of ["success","refusal"]',
        "context.operation.operation_id: not in the closed set",
      ],
    }),
  ],
];

// The schemas that describe the captured envelopes. `result` declares its own schema
// identity, and the vocabularies for the object fields live there, so the object-set
// schema is registered rather than duplicated into the envelope schema.
const json = async (url) => JSON.parse(await readFile(url, "utf8"));

const envelopeSchema = await json(
  new URL("../../schemas/v3-synthetic-preview/synthetic-resolve-envelope.schema.json", import.meta.url),
);
const registry = new Map([
  [
    "lex-v3-preview-object-set/1",
    await json(new URL("../../schemas/v3-preview/preview-object-set.schema.json", import.meta.url)),
  ],
]);

for (const [name, file, render] of [
  ["state-success.html", "success.json", renderSuccess],
  ["state-refusal.html", "refusal.json", renderRefusal],
]) {
  const raw = loadCaptured(file);
  const { decoded, problems } = decodeEnvelope(envelopeSchema, raw, registry);
  const invalid = problems.concat(
    decoded ? validateEnvelope(envelopeSchema, decoded, registry) : [],
  );
  if (invalid.length > 0) {
    throw new Error(`captured ${file} did not decode and validate: ${invalid.join("; ")}`);
  }
  pages.push([name, render({ envelope: decoded })]);
}

// The trust surface: the four components every screen composes, rendered together so the
// browser evidence run exercises their contrast, focus order and reflow at 320 CSS pixels
// before there are twelve screens carrying copies of the same defect.
pages.push(["trust-surface.html", renderTrustSurface()]);

// One entry screen per shell. They exist so the browser run can measure that three
// densities of the same component library still reflow, keep contrast and keep readable
// separation, and so the neutrality rule is visible rather than only tested.
for (const shell of SHELLS) {
  pages.push([`shell-${shell}.html`, renderShellEntry({ shell })]);
}

// The refusal catalog, UX spec section 11: the closed registry as public API surface,
// with one worked example each and an honest column for the payloads not yet settled.
pages.push(["refusal-catalog.html", renderRefusalCatalog()]);

// Compare, in the five states where a diff would otherwise be confidently wrong. The
// refusals replace the panes, so they are measured for contrast and reflow beside them.
pages.push(["compare.html", renderComparePreview()]);

// The timeline, in the shapes where a chart would draw something the publisher never said.
pages.push(["timeline.html", renderTimelinePreview()]);

// Coverage, the page whose job is to say what is missing, in a finished build and an
// unfinished one.
pages.push(["coverage.html", renderCoveragePreview()]);

// Discovery, in the four results read as something they are not, including the zero-hit
// case that is read as the law being silent.
pages.push(["search.html", renderSearchPreview()]);

// The dossier, the one screen where the publisher current-state flag belongs, and the three
// shapes in which a hub page misleads.
pages.push(["dossier.html", renderDossierPreview()]);

// Reading: one work's text as it stood on one date, with every provision carrying the
// permalink that makes it checkable.
pages.push(["reading.html", renderReadingPreview()]);

// One page per chrome locale this build has no reviewed copy in. They are built rather than
// described, because the browser run measures what is built, and until now every measurement
// in this package was made against English only.
for (const locale of CHROME_LOCALES.filter((one) => !REVIEWED_CHROME_LOCALES.includes(one))) {
  pages.push([`locale-unavailable-${locale}.html`, renderLocaleUnavailable({ requested: locale })]);
}

// The destination every scheme-valid link in the preview resolves to. A visible action that
// leads to a missing page is a promise the page cannot keep, and three of them shipped: the
// provenance link and both ambiguity candidates answered 404. This is the preview's stand-in
// for screens that do not exist yet, and it says so rather than pretending to be one.
pages.push([
  "preview-destination.html",
  page({
    state: "preview-destination",
    title: "Preview destination",
    main: `      <p class="eyebrow">Preview destination</p>
      <h1>Preview destination</h1>
      <p>You followed a real link from the synthetic preview. The screen it addresses is not
        built yet, so this page stands in for it. The URL you arrived by is the scheme the
        product uses; nothing here is law, and no coordinate was resolved.</p>`,
  }),
]);

// Provenance: the destination of the Provenance link every data view in this product carries.
// It answered 404 everywhere, which is the worst thing that can be wrong with a product whose
// claim is that an answer can be checked rather than trusted. One page per record, named after
// the record, because a provenance link resolving to some other record's proof chain would be
// worse than the missing page it replaces. The fourth page is the refusal a reader meets after
// following a link to a record this corpus does not hold.
pages.push(...provenancePreviewPages());

// The hydrated page and the script that hydrates it. Compiled from app/ through the same
// esbuild pipeline the tests use, so the bytes measured in the browser are the bytes the tests
// asserted against rather than a second compilation that happens to agree.
const { bundle, bundleClient, resetWork } = await import("./react-build.mjs");
await resetWork();
const ssr = await import(pathToFileURL(await bundle("app/index.jsx", "app.mjs")).href);
pages.push(["hydration.html", ssr.renderHydrationProof()]);

// The two screens ported to React in this change, built so the browser run measures them at
// 320 CSS pixels rather than only in a test. Both carry a wide table that must scroll inside its
// own labelled box instead of making the page scroll sideways, and a page that scrolls sideways
// hides a column, which on these two screens means hiding a disclosure.
pages.push(["timeline-react.html", ssr.renderTimelineReactPage()]);
pages.push(["coverage-react.html", ssr.renderCoverageReactPage()]);
// The search screen, built rather than only unit tested.
//
// It was held out because the browser gate counted every button on a built page as an inert
// control, a rule written when no built page carried one. The gate now asks whether the page
// ships a script before deciding a control is inert, which is what its own failure message
// always said. This is the first screen in the product with real controls, so it is the first
// one whose target sizes, focus order and disabled-but-reachable behaviour can be measured in a
// browser instead of asserted in a test.
pages.push(["search-react.html", ssr.renderSearchScreenPage()]);

// S4 and S12, added after the first React wave. Both are string renderers today and both are
// built, so the browser gate measures their contrast, target sizes and reading order rather than
// leaving those asserted only in a test.
pages.push(["provision-history.html", renderProvisionHistoryPreview()]);
pages.push(["get-help.html", renderGetHelpPreview()]);
pages.push(["export-composer.html", renderExportComposerPreview()]);
pages.push(["citation-checker.html", renderCitationCheckerPreview()]);

const clientBundle = await bundleClient("app/client-entry.jsx", "client.js");
await cp(clientBundle, new URL("client.js", destination));

for (const [name, html] of pages) {
  await writeFile(new URL(name, destination), html, "utf8");
}

// What this build emitted, so the browser run measures exactly this and nothing else. A page
// present on disk and absent here is a stale artefact; one declared here and absent there is a
// build that half ran. Either way the run refuses rather than reporting a clean number.
await writeFile(
  new URL("pages.json", destination),
  `${JSON.stringify({ pages: [...copiedPages, ...pages.map(([name]) => name)].sort() }, null, 2)}
`,
  "utf8",
);

console.log(`generated ${pages.length} state pages`);
