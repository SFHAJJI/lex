// The French pages as their bundles render them (the launch contract's chrome line: "Chrome in FR and EN").
//
// The views and the screens' own modules are compiled for French here, as the live build compiles a French
// page and its script (`__LEX_CHROME_LOCALE__`), and every census answer and refusal is rendered through
// them beside the English. The French pages must say:
// - each refusal in the reviewed French (`live-chrome-fr.mjs`, reviewed by Claude, an AI reviewer, under the
//   owner's delegation of 2026-10-02), never in English: the sentences by code, the two sentences that name
//   a refusal only by its code, and the hints a card that cannot be shown still carries;
// - the platform's own English (its reasons, rules, scopes and notes, the EU acknowledgement and
//   authenticity statement, a refusal payload's texts) marked `lang="en"`, so a screen reader reads it as
//   English, where the English pages mark nothing;
// - an EU refusal card's offered wordings by their dates as wordings', never as applicable states;
// - the separators of a list and of a label in French typography: a no-break space before ":" and ";".

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createRequire } from "node:module";
import test from "node:test";
import { fileURLToPath, pathToFileURL } from "node:url";

import { build } from "esbuild";

import * as english from "../.react-build/app.mjs";
import { LIVE_CHROME, REFUSAL_TRANSLATIONS } from "../scripts/live-chrome.mjs";
import { historyBeginsHint, nearestAnchorsHint } from "../scripts/live-refusals.mjs";

const require = createRequire(import.meta.url);
const { createElement: h } = require("react");
const { renderToStaticMarkup } = require("react-dom/server");

const web = new URL("../", import.meta.url);
const OUT = fileURLToPath(new URL(".react-build/app-fr-test.mjs", web));
const FR = LIVE_CHROME.fr;
const SAID = REFUSAL_TRANSLATIONS.fr;
const NB = " ";

/** Each screen: its census operation, its module, the function that maps an answer to its state, and its view. */
const SCREENS = Object.freeze([
  ["search", "search", "live-search.mjs", "searchOutcome", "SearchAnswerView"],
  ["dossier", "dossier", "live-dossier.mjs", "dossierOutcome", "DossierAnswerView"],
  ["reading", "evidence_bundle", "live-reading.mjs", "readingOutcome", "ReadingAnswerView"],
  ["export", "evidence_bundle", "live-reading.mjs", "readingOutcome", "ExportAnswerView"],
  ["history", "article_history", "live-history.mjs", "historyOutcome", "HistoryAnswerView"],
  ["compare", "diff", "live-compare.mjs", "compareOutcome", "CompareAnswerView"],
  ["radar", "changes_in_period", "live-radar.mjs", "radarOutcome", "RadarAnswerView"],
  ["coverage", "coverage", "live-coverage.mjs", "coverageOutcome", "CoverageAnswerView"],
]);

/** A screen module's name as the French build exports it whole (`live-search.mjs` is `live_search`). */
const moduleName = (module) => module.replace(/\.mjs$/, "").replaceAll("-", "_");

/** The views and the screens' modules, compiled for French as the live build compiles a French page's script. */
async function frenchBuild() {
  const modules = [...new Set(SCREENS.map(([, , module]) => module))];
  await build({
    stdin: {
      contents: ["export * from './app/index.jsx';", ...modules.map((module) => `export * as ${moduleName(module)} from './scripts/${module}';`)].join("\n"),
      resolveDir: fileURLToPath(web),
      sourcefile: "french-test.jsx",
      loader: "jsx",
    },
    outfile: OUT,
    bundle: true,
    format: "esm",
    platform: "node",
    jsx: "automatic",
    packages: "external",
    logLevel: "silent",
    define: { __LEX_CHROME_LOCALE__: JSON.stringify("fr") },
  });
  return import(`${pathToFileURL(OUT).href}?${Date.now()}`);
}

const french = await frenchBuild();
/** A screen's module as the French page's script holds it. */
const frenchModule = (module) => french[moduleName(module)];
const census = JSON.parse(await readFile(new URL("../schemas/v3-platform/envelope-samples.json", web), "utf8"));
const props = (outcome) => ({ outcome, pins: new Set(), onPin: () => {}, onNextPage: () => {} });
const escaped = (text) => text.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;").replaceAll("'", "&#x27;");
const ENTITIES = { "&amp;": "&", "&lt;": "<", "&gt;": ">", "&quot;": '"', "&#x27;": "'" };
const decode = (text) => text.replace(/&(?:amp|lt|gt|quot|#x27);/g, (entity) => ENTITIES[entity]);
const englishModule = async (module) => import(new URL(`scripts/${module}`, web).href);

/** A census envelope by operation and the start of its scenario, copied. */
function envelopeOf(operation, scenarioStart) {
  const entry = census.envelopes.find((candidate) => candidate.operation === operation && candidate.scenario.startsWith(scenarioStart));
  assert.ok(entry, `the census holds ${operation}: ${scenarioStart}`);
  return structuredClone(entry.envelope);
}

/** The same refusal without the absence evidence its card requires, so its card cannot be shown. */
function unshowable(operation, scenarioStart) {
  const envelope = envelopeOf(operation, scenarioStart);
  delete envelope.refusal.helpful_payload.what_would_answer;
  delete envelope.refusal.helpful_payload.asserts_absence_of_law;
  return envelope;
}

/**
 * A render's text with every element that carries a language of its own cut out: the publisher's text in its
 * language, and the platform's English marked English. What is left is what the page says in its own language.
 */
function unmarked(markup) {
  let rest = markup.replace(/^[\s\S]*?<body[^>]*>/, "");
  for (let at = rest.search(/<([a-z]+)[^>]* lang="[a-z-]+"/); at >= 0; at = rest.search(/<([a-z]+)[^>]* lang="[a-z-]+"/)) {
    const tag = rest.slice(at).match(/^<([a-z]+)/)[1];
    let depth = 0;
    let end = -1;
    for (const match of rest.slice(at).matchAll(new RegExp(`<(/?)${tag}[\\s>]`, "g"))) {
      depth += match[1] === "/" ? -1 : 1;
      if (depth === 0) {
        end = at + match.index + `</${tag}>`.length;
        break;
      }
    }
    assert.ok(end > at, "an element with a language of its own is closed");
    rest = `${rest.slice(0, at)} ${rest.slice(end)}`;
  }
  return decode(rest.replace(/<[^>]+>/g, " "));
}

/** The platform's prose an answer carries: every string of at least four words, as sent and as a page starts a sentence with it. */
function proseOf(value) {
  const found = new Set();
  const walk = (node) => {
    if (typeof node === "string" && node.trim().split(/\s+/).length >= 4) found.add(node);
    else if (Array.isArray(node)) node.forEach(walk);
    else if (node !== null && typeof node === "object") Object.values(node).forEach(walk);
  };
  walk(value);
  return [...found];
}
const shown = (text, prose) => text.includes(prose) || text.includes(`${prose.charAt(0).toUpperCase()}${prose.slice(1)}`);

test("a French page says each census refusal in its reviewed French, and never the English", async () => {
  let said = 0;
  for (const [name, operation, module, outcomeName, view] of SCREENS) {
    const inEnglishOf = (await englishModule(module))[outcomeName];
    for (const entry of census.envelopes.filter((candidate) => candidate.operation === operation && candidate.envelope.refusal)) {
      const label = `${name}: ${entry.scenario}`;
      const englishState = inEnglishOf({ state: "refusal", envelope: entry.envelope });
      const frenchState = frenchModule(module)[outcomeName]({ state: "refusal", envelope: entry.envelope });
      assert.equal(frenchState.card, true, `${label}: the census refusals are cards`);
      assert.equal(frenchState.sentence, SAID.sentences[englishState.sentence], `${label}: the reviewed French of ${JSON.stringify(englishState.sentence)}`);
      const markup = renderToStaticMarkup(h(french[view], props(frenchState)));
      assert.ok(markup.includes(`<span class="token-text">${escaped(frenchState.sentence)}</span>`), `${label}: the card says the French`);
      assert.ok(!markup.includes(escaped(englishState.sentence)), `${label}: and not the English`);
      assert.ok(markup.includes(escaped(FR.refusalCard.tokenLabel)), `${label}: in the French card's words`);
      said += 1;
    }
  }
  assert.ok(said >= 20, `every screen's census refusals were said (${said})`);

  // A code a screen has no sentence for is named by its code, in each screen's French.
  for (const [screen, , module] of SCREENS) {
    const id = screen === "export" ? "reading" : screen;
    assert.equal(frenchModule(module).unexpectedRefusalSentence("snapshot_unknown"), SAID.templates[id].unexpected.replace("{code}", "snapshot_unknown"), screen);
  }
});

test("an EU refusal on a French page is said in the EU's French words: wordings and their dates, never a version that applies", () => {
  const refused = (outcomeOf, code, payload) => outcomeOf({ state: "refusal", envelope: { refusal: { code, helpful_payload: payload }, context: { publisher: "eu-eurlex" } } });
  const later = {
    requested_date: "2021-04-27", history_begins: "2016-04-27", nearest_earlier: "2016-04-27", nearest_later: null,
    what_would_answer: ["new_official_observation"], asserts_absence_of_law: false,
  };
  const frenchState = refused(frenchModule("live-reading.mjs").readingOutcome, "no_version_for_date", later);
  assert.equal(frenchState.sentence, "Cet index ne contient aucun libellé de cet acte de l’UE pour cette date.");
  const markup = renderToStaticMarkup(h(french.ReadingAnswerView, { outcome: frenchState }));
  assert.ok(markup.includes(escaped(FR.refusalCard.europeNullSentences.nearest_later)), "the declared null speaks of wordings");
  // Words, not the refusal's code (`no_version_for_date`), which is the contract's.
  assert.doesNotMatch(unmarked(markup), /\bversions?\b|s’appliqu|applicab/i, "no Luxembourg date word on an EU refusal");
});

test("a refusal whose card cannot be shown is said in French, the card's English reason marked English and the hint in French", async () => {
  const history = (payload) => ["The history this index holds for this work begins on {date}.", { date: payload.history_begins }, historyBeginsHint(payload.history_begins)];
  const nearest = (payload) => ["The nearest article ids this index holds are {ids}.", { ids: payload.nearest_anchors.join(", ") }, nearestAnchorsHint(payload.nearest_anchors)];
  const cases = [
    ["reading", "evidence_bundle", "a date before the work's history", "live-reading.mjs", "readingOutcome", "ReadingAnswerView", history],
    ["compare", "diff", "a from date before the work's history", "live-compare.mjs", "compareOutcome", "CompareAnswerView", history],
    ["history", "article_history", "an article id no held state carries", "live-history.mjs", "historyOutcome", "HistoryAnswerView", nearest],
  ];
  for (const [screen, operation, scenario, module, outcomeName, view, hintOf] of cases) {
    const envelope = unshowable(operation, scenario);
    const englishState = (await englishModule(module))[outcomeName]({ state: "refusal", envelope });
    const frenchState = frenchModule(module)[outcomeName]({ state: "refusal", envelope });
    assert.equal(frenchState.card, false, `${screen}: the card cannot be shown`);
    const { code } = envelope.refusal;
    const [hintTemplate, hintValues, englishHint] = hintOf(envelope.refusal.helpful_payload);
    // The card's reason is its own English message: the one the English page says, between the sentence's fixed words.
    const [englishBefore, englishAfter] = (await englishModule(module)).unshownRefusalSentence("{code}", "{reason}").replace("{code}", code).split("{reason}");
    assert.ok(englishState.sentence.startsWith(englishBefore) && englishState.sentence.endsWith(`${englishAfter} ${englishHint}`), englishState.sentence);
    const reason = englishState.sentence.slice(englishBefore.length, englishState.sentence.length - `${englishAfter} ${englishHint}`.length);
    assert.ok(reason.length > 10, `${screen}: the reason is read back (${reason})`);
    const [before, after] = SAID.templates[screen].unshown.replace("{code}", code).split("{reason}");
    const hint = Object.entries(hintValues).reduce((text, [key, value]) => text.replace(`{${key}}`, value), SAID.hints[hintTemplate]);
    assert.equal(frenchState.sentence, `${before}${reason}${after} ${hint}`, `${screen}: the French sentence, the English reason, the French hint`);
    const markup = renderToStaticMarkup(h(french[view], props(frenchState)));
    assert.ok(markup.includes(`<p role="status">${escaped(before)}<span lang="en">${escaped(reason)}</span>${escaped(`${after} ${hint}`)}</p>`), `${screen}: the reason is marked English, nothing else is`);
    assert.ok(renderToStaticMarkup(h(english[view], props(englishState))).includes(`<p role="status">${escaped(englishState.sentence)}</p>`), `${screen}: the English page says its sentence as it did`);
  }
});

test("the platform's English on a French page is marked English, and on an English page it is said as it always was", async () => {
  const answers = [];
  for (const [name, operation, module, outcomeName, view] of SCREENS) {
    const inEnglishOf = (await englishModule(module))[outcomeName];
    for (const entry of census.envelopes.filter((candidate) => candidate.operation === operation)) {
      const state = entry.envelope.refusal ? "refusal" : "success";
      answers.push([`${name}: ${entry.scenario}`, entry.envelope, view, inEnglishOf({ state, envelope: entry.envelope }), frenchModule(module)[outcomeName]({ state, envelope: entry.envelope })]);
    }
  }
  let marked = 0;
  const shownKinds = new Set();
  for (const [label, envelope, view, englishState, frenchState] of answers) {
    const englishText = unmarked(renderToStaticMarkup(h(english[view], props(englishState))));
    const frenchMarkup = renderToStaticMarkup(h(french[view], props(frenchState)));
    const frenchText = unmarked(frenchMarkup);
    const frenchAll = decode(frenchMarkup.replace(/<[^>]+>/g, " "));
    for (const prose of proseOf(envelope)) {
      // Prose the English page says unmarked is the platform's; the publisher's text carries its own language on both.
      if (!shown(frenchAll, prose) || !shown(englishText, prose)) continue;
      marked += 1;
      assert.ok(!shown(frenchText, prose), `${label}: the platform's ${JSON.stringify(prose.slice(0, 70))} is on the French page unmarked`);
      assert.ok(shown(englishText, prose), `${label}: the English page says ${JSON.stringify(prose.slice(0, 70))} unmarked, as it always did`);
      shownKinds.add(label.split(":")[0]);
    }
  }
  assert.ok(marked >= 40, `the platform's prose was found on the pages (${marked})`);
  assert.deepEqual([...shownKinds].sort(), ["compare", "coverage", "dossier", "export", "history", "radar", "reading", "search"], "on every screen");

  // The EU acknowledgement and authenticity statement stand above an EU text, marked English.
  const view = {
    publisher: "eu-eurlex", celex: "32016R0679", acknowledgement: "© European Union, https://eur-lex.europa.eu",
    authenticity: "Only the Official Journal of the European Union published in electronic form is authentic and produces legal effects.",
    wordings: [], notHeld: [{ item: "later_wordings", reason: "no consolidated version is held" }],
  };
  const markup = renderToStaticMarkup(h(french.ReadingAnswerView, { outcome: { state: "success", view } }));
  assert.ok(markup.includes(`<p data-acknowledgement="" lang="en">${escaped(view.acknowledgement)}</p>`));
  assert.ok(markup.includes(`<p data-authenticity="" lang="en">${escaped(view.authenticity)}</p>`));
  assert.ok(markup.includes(`<strong>later_wordings</strong>${NB}: <span lang="en">no consolidated version is held</span>`), "a reason something is not held, in French typography around it");
  const englishMarkup = renderToStaticMarkup(h(english.ReadingAnswerView, { outcome: { state: "success", view } }));
  assert.ok(englishMarkup.includes(`<p data-acknowledgement="">${escaped(view.acknowledgement)}</p>`) && englishMarkup.includes("<strong>later_wordings</strong>: no consolidated version is held"), "the English page marks nothing");

  // A refusal payload's texts, on the card.
  const refusal = envelopeOf("dossier", "a work the index does not hold");
  const card = renderToStaticMarkup(h(french.DossierAnswerView, props(frenchModule("live-dossier.mjs").dossierOutcome({ state: "refusal", envelope: refusal }))));
  const disclosure = refusal.refusal.helpful_payload.population_disclosure;
  assert.equal(typeof disclosure, "string");
  assert.ok(card.includes(`<dd lang="en">${escaped(disclosure)}</dd>`), "the payload's prose is marked English on the card");
});

test("an EU refusal card offers wordings by their dates, never applicable states, in English and in French", () => {
  const sha = (digit) => digit.repeat(64);
  const candidates = [
    { valid_from: "2024-02-01", hash: sha("a"), publication_date: "2024-01-15", href: `/lu-legilux/loi-x/2024-02-01--${sha("a")}`, withdrawn: false },
    { valid_from: "2024-03-01", hash: sha("b"), publication_date: null, href: `/lu-legilux/loi-x/2024-03-01--${sha("b")}`, withdrawn: false },
  ];
  const outcome = (publisher, sentence) => ({
    state: "refusal", code: "ambiguous_version", card: true, sentence, payload: { requested_date: "2024-06-01", candidates }, context: { publisher },
  });
  const render = (app, publisher, sentence) => renderToStaticMarkup(h(app.ReadingAnswerView, { outcome: outcome(publisher, sentence) }));

  const europe = render(english, "eu-eurlex", "This index holds different texts of this EU act for that date, so none is chosen.");
  assert.ok(europe.includes("wording of 2024-02-01, hash <code>aaaaaaaa</code>, published 2024-01-15"), europe);
  assert.ok(europe.includes("wording of 2024-03-01, hash <code>bbbbbbbb</code>, publication date not stated by the platform"));
  assert.ok(europe.includes("The publisher ranks neither wording."), "the card's note speaks of wordings");
  assert.doesNotMatch(unmarked(europe), /applicab|\bstates?\b|\bversions?\b/i, "an EU card says no Luxembourg date word");
  assert.ok(render(english, "lu-legilux", "Several states of this work apply on that date, and none is chosen.").includes("applicable from 2024-02-01, hash <code>aaaaaaaa</code>, published 2024-01-15"), "a Luxembourg card keeps its words");

  const europeFr = render(french, "eu-eurlex", SAID.sentences["This index holds different texts of this EU act for that date, so none is chosen."]);
  assert.ok(europeFr.includes("libellé du 2024-02-01, empreinte <code>aaaaaaaa</code>, publié le 2024-01-15"), europeFr);
  assert.ok(europeFr.includes("libellé du 2024-03-01, empreinte <code>bbbbbbbb</code>, date de publication non indiquée par la plateforme"));
  assert.ok(europeFr.includes(escaped(FR.refusalCard.europeNotes.ambiguous_version)));
  assert.doesNotMatch(unmarked(europeFr), /applicab|s’appliqu|\bversions?\b/i, "nor does it in French");
  assert.ok(render(french, "lu-legilux", SAID.sentences["Several states of this work apply on that date, and none is chosen."]).includes("applicable à partir du 2024-02-01, empreinte <code>aaaaaaaa</code>, publiée le 2024-01-15"));
});

test("a list's separators and a label's colon are the table's, so a French page sets a no-break space before them", () => {
  const titles = [{ language: "fra", expressionIri: "e-fr", titles: [{ title: "Loi du 10 août 1991" }], shortTitles: [{ title: "Loi avocats" }] }];
  assert.ok(renderToStaticMarkup(h(english.DossierTitles, { titles })).includes('fra: <span><span lang="fr">Loi du 10 août 1991</span></span><span>; <span lang="fr">Loi avocats</span> (short title)</span>'));
  assert.ok(renderToStaticMarkup(h(french.DossierTitles, { titles })).includes(`fra${NB}: <span><span lang="fr">Loi du 10 août 1991</span></span><span>${NB}; <span lang="fr">Loi avocats</span> (intitulé court)</span>`));

  const lineage = {
    anchor: "art_15", workKey: "loi-x", language: null, historyBegins: "2024-02-01",
    rows: [{ stateSha256: "s", language: "fra", applicabilityDate: "2024-02-01", nextApplicabilityDate: null, wordingChanged: false, permalink: "/p",
      articles: [{ validFrom: "2024-01-01", validityConflict: true }, { validFrom: null, validityConflict: false }] }],
    absent: [], wordingRuns: { fra: 1, deu: 2 }, distinctWordings: { fra: 1, deu: 2 }, wordingRule: "the wording rule as the platform states it",
  };
  const englishLineage = renderToStaticMarkup(h(english.HistoryView, { view: lineage }));
  assert.ok(englishLineage.includes("fra: 1 wording run, 1 distinct; deu: 2 wording runs, 2 distinct.") && englishLineage.includes("2024-01-01 (differs from the state&#x27;s); not stated"), englishLineage);
  const frenchLineage = renderToStaticMarkup(h(french.HistoryView, { view: lineage }));
  assert.ok(frenchLineage.includes(`fra${NB}: 1 séquence de libellé, libellés distincts${NB}: 1${NB}; deu${NB}: 2 séquences de libellé, libellés distincts${NB}: 2.`), frenchLineage);
  assert.ok(frenchLineage.includes(`2024-01-01 (diffère de celle de la version)${NB}; non indiquée`));
  assert.ok(frenchLineage.includes('<p lang="en">The wording rule as the platform states it.</p>'));

  const search = {
    publisher: "lu-legilux", query: "garantie", language: "fra", date: "2024-02-01",
    population: { strictHits: 0, relaxedHits: 0, worksWithHits: 0 }, workResolution: { outcome: "not_asked" },
    ambiguousWorks: [{ workKey: "loi-x", candidates: ["/a", "/b"] }], hits: [], searchableTextHeld: true,
    matching: "the phrase is matched byte for byte", truncated: false,
  };
  assert.ok(renderToStaticMarkup(h(english.SearchResultsView, { view: search })).includes("<li>loi-x: <span><code>/a</code></span><span>, <code>/b</code></span></li>"));
  assert.ok(renderToStaticMarkup(h(french.SearchResultsView, { view: search })).includes(`<li>loi-x${NB}: <span><code>/a</code></span><span>, <code>/b</code></span></li>`));
});

test("a state with no French is said in English and marked English on a French page, and as it was on an English page", async () => {
  for (const [name, , module, outcomeName, view] of SCREENS) {
    for (const asked of [{ state: "transport_failure", code: "network_error" }, { state: "invalid_envelope", reason: "the answer is not one this page reads" }]) {
      const englishState = (await englishModule(module))[outcomeName](asked);
      const frenchState = frenchModule(module)[outcomeName](asked);
      assert.equal(frenchState.sentence, englishState.sentence, `${name}: the same English`);
      assert.ok(renderToStaticMarkup(h(french[view], props(frenchState))).includes(`<p role="status"><span lang="en">${escaped(englishState.sentence)}</span></p>`), `${name} ${asked.state}: marked English`);
      assert.ok(renderToStaticMarkup(h(english[view], props(englishState))).includes(`<p role="status">${escaped(englishState.sentence)}</p>`), `${name} ${asked.state}: unmarked on the English page`);
    }
  }
  // A request the form will not send, likewise: the session says why, in English, marked.
  const said = [];
  const session = frenchModule("live-search.mjs").createSearchSession({ contract: null, fetchImpl: () => assert.fail("nothing is asked"), onOutcome: (outcome) => said.push(outcome) });
  assert.equal(session.ask({ query: " ", language: "fra" }), false);
  assert.deepEqual(said, [{ state: "invalid_request", sentence: "a search needs a phrase to look for.", runs: [{ lang: "en", text: "a search needs a phrase to look for." }] }]);
});

test("the evaluation card's and the export panel's English is marked English on a French page (review of #912)", async () => {
  // The card as the platform publishes it, and the same card with its first set emptied, so that each gate carries a reason.
  const { EMPTY_CASES_SHA256, readEvaluationCard } = await import("../scripts/evaluation-card.mjs");
  const card = JSON.parse(await readFile(new URL("../schemas/v3-platform/evaluation-card.json", web), "utf8"));
  const emptied = structuredClone(card);
  Object.assign(emptied.machine_gates[0], { cases: 0, cases_sha256: EMPTY_CASES_SHA256 });
  emptied.machine_gates[0].gates = emptied.machine_gates[0].gates.map((gate) => ({ gate: gate.gate, verdict: "not_measured", value: null, threshold: gate.threshold, n: 0, not_measured_reason: "no_measurable_query" }));
  Object.assign(emptied.shuffled_controls[0], { verdict: "not_applicable", cases: 0, cases_sha256: EMPTY_CASES_SHA256, reason: "there is no temporal case to shift: the mount holds no Luxembourg state for this arm" });
  delete emptied.shuffled_controls[0].note;
  for (const view of [readEvaluationCard(card), readEvaluationCard(emptied)]) {
    const markup = renderToStaticMarkup(h(french.EvaluationCardView, { view }));
    const englishOf = [
      ["target", view.target],
      ...view.sets.flatMap((set) => set.gates.filter((gate) => gate.reason !== null).map((gate) => [`${set.set} ${gate.gate}'s reason`, gate.reason])),
      ...view.controls.flatMap((control) => [[`${control.control}'s reason`, control.reason], ...(control.note === null ? [] : [[`${control.control}'s note`, control.note]])]),
      ...view.statisticalRows.flatMap((row) => [[`${row.dataset}'s name`, row.name], [`${row.dataset}'s gates`, row.gates], [`${row.dataset}'s governance`, row.governedBy]]),
      ...view.negativeResults.flatMap((row) => [row.hypothesis, row.dataset, row.result, row.decision, row.whatWouldReverseIt].map((text) => [`a negative result's ${text}`, text])),
    ];
    for (const [what, text] of englishOf) assert.ok(markup.includes(`<span lang="en">${escaped(text)}</span>`), `the card's ${what} is marked English`);
  }
  assert.ok(readEvaluationCard(emptied).sets[0].gates.every((gate) => gate.reason !== null), "the emptied set's gates each carry a reason, so their marking is read");

  // The export panel: the watermark and the platform's rights rule, which the exported file carries in English.
  const envelope = envelopeOf("evidence_bundle", "the work on its state's date");
  const outcome = frenchModule("live-reading.mjs").readingOutcome({ state: "success", envelope });
  const { exportState, pinKey } = await import("../scripts/live-export.mjs");
  const pins = new Set(outcome.view.states.flatMap((held) => [...held.articles, ...held.articlesWithoutText].map((article) => pinKey(held.stateSha256, article.publisherId))));
  const composed = exportState(outcome, pins);
  assert.equal(composed.state, "composed");
  const panel = renderToStaticMarkup(h(french.ExportPanel, { outcome, pins, onSave: () => {} }));
  assert.ok(panel.includes(`<p data-watermark="" lang="en">${escaped(composed.model.watermark)}</p>`), "the watermark is marked English");
  assert.ok(panel.includes(`<span lang="en">${escaped(composed.model.rightsRule)}</span>`), "the rights rule is marked English");
});
