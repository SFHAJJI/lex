// The publisher's relation records on the dossier (index 10's `publisher_relations` and `amended_after_consolidation`; the
// specification of record's R29, R72 and R21), read by `scripts/publisher-relations.mjs` and laid out by `LiveDossier.jsx`:
// one tab per kind of relation the records name, the inverse views labelled derived from the publisher's forward relation,
// the acting act's dates each with its role, the kinds that name no act said only of a relation read in full, what the family
// does not see, and R72's banner when an act published after every consolidated version amends the act. An answer over an
// older index carries neither member and shows neither. Every value is synthetic, set on the census's own dossier answer.

import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { createElement as h } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import { DossierAnswerView } from "../.react-build/app.mjs";
import { dossierOutcome } from "../scripts/live-dossier.mjs";
import { AMENDED_AFTER_STATUSES, RELATION_VIEWS, readAmendedAfter, readPublisherRelations } from "../scripts/publisher-relations.mjs";
import { LIVE_CHROME } from "../scripts/live-chrome.mjs";
import { assertTimeVocabulary } from "../scripts/publisher-vocabulary.mjs";

const census = JSON.parse(await readFile(new URL("../../schemas/v3-platform/envelope-samples.json", import.meta.url), "utf8"));
const sample = census.envelopes.find((entry) => entry.operation === "dossier" && entry.scenario.startsWith("the work, in the language it is held in")).envelope;
const view = (outcome) => renderToStaticMarkup(h(DossierAnswerView, { outcome }));
const text = (markup) => markup.replace(/<[^>]+>/g, " ").replace(/&#x27;|&#39;/g, "'").replace(/&quot;/g, '"').replace(/&amp;/g, "&").replace(/\s+/g, " ");

const ELI = "http://data.legilux.public.lu/eli/etat/leg/";
const EVIDENCE = "b".repeat(64);
const FAMILY_KEYS = ["legislative-code-g", "legislative-loi-g", "legislative-rgd-g"];

function other(iri, { title = null, address = null, held = false, published = null, entry = null } = {}) {
  return {
    iri, work_key: address === null ? null : address.slice("/lu-legilux/".length), address, held, title, title_language: title === null ? null : "fra",
    title_withheld: false, publication_date: published, entry_into_force: entry,
  };
}

function edge(view, edgeType, direction, derived, otherAct, published, entry = null, impact = null) {
  return {
    view, edge_type: edgeType, source_predicate: edgeType === "impact" ? "jolux:LegalResourceImpact" : `jolux:${edgeType}`, direction, derived,
    asserted_on: edgeType === "impact" ? "impact_record" : derived ? "other_act" : "this_act",
    subject_iri: null, object_iri: null, acting_iri: null, acted_on_iri: null, is_self_reference: false, other: otherAct,
    acting_publication_date: published, acting_entry_into_force: entry, evidence_sha256: EVIDENCE, impact,
  };
}

/** The census's dossier with index 10's two members, consistent with its own work. */
function withRelations({ status = "notice", acts = 1, families = "acquired_complete", familyStates = {}, consolidated = true } = {}) {
  const envelope = structuredClone(sample);
  const value = envelope.result.value;
  const amending = other(`${ELI}loi/2016/06/01/n1/jo`, { title: "Loi synthétique du 1er juin 2016", address: "/lu-legilux/loi-2016-06-01-n1", published: "2016-06-03", entry: "2016-06-07" });
  const second = other(`${ELI}loi/2017/03/01/n2/jo`, { address: "/lu-legilux/loi-2017-03-01-n2", published: "2017-03-02" });
  const edges = families === "acquired_complete" ? [
    edge("amended_by", "modifies", "inbound", true, amending, "2016-06-03", "2016-06-07"),
    edge("amended_by", "modifies", "inbound", true, second, "2017-03-02"),
    edge("amends", "modifies", "outbound", false, other(`${ELI}loi/2000/01/01/n1/jo`, { published: "2000-01-04" }), "1991-08-12", "1991-09-01"),
    edge("repealed_by", "repeals", "inbound", true, other(`${ELI}rgd/2017/01/01/n1/jo`), null),
    edge("impact_received", "impact", "inbound", true, amending, "2016-06-03", "2016-06-07", {
      impact_iri: `${ELI}loi/2016/06/01/n1/legal-analysis/LegalResourceImpact/1`, type_iri: "http://data.legilux.public.lu/resource/authority/impact-type/1",
      type_code: "1", to_expression_iri: null, consolidated_by_iri: `${ELI}loi/1991/08/10/n3/consolide/20170101`, consolidated_by_expression_iri: null,
      consolidated_by_state: null, ambiguous_parts: [],
    }),
  ] : [];
  const counts = Object.fromEntries(RELATION_VIEWS.map((entry) => [entry.view, edges.filter((candidate) => candidate.view === entry.view).length]));
  value.publisher_relations = {
    rule: "the publisher's relation records about this act",
    inverse_label: "derived from the publisher's forward relation",
    not_seen: "the relation family holds the publisher's records whose subject identifier is under code, loi and rgd",
    families: ["modifies", "repeals", "rectifies", "modified_temporarily_by", "impact"].map((edgeType) => {
      const state = familyStates[edgeType] ?? families;
      return {
        edge_type: edgeType, source_predicates: [`jolux:${edgeType}`], state,
        family_keys: state === "acquired_complete" ? FAMILY_KEYS : [], subject_classes: state === "acquired_complete" ? ["code", "loi", "rgd"] : null,
        reason: state === "acquired_complete" ? null : "no relation-assertions family was designated for this run",
      };
    }),
    edge_count: edges.length,
    edge_counts: counts,
    edges,
  };
  const notice = status === "notice";
  const listed = [{ acting_iri: amending.iri, publication_date: "2016-06-03", other: amending, evidence_sha256: EVIDENCE },
    { acting_iri: second.iri, publication_date: "2017-03-02", other: second, evidence_sha256: EVIDENCE }].slice(0, acts);
  const state = value.states[0];
  value.amended_after_consolidation = {
    status, derived: true, source_predicate: "jolux:modifies",
    acts: notice ? listed : [],
    latest_consolidation: consolidated ? {
      state_sha256: "c".repeat(64), language: "fra", applicability_date: "2015-01-01",
      stable_coordinate: `/lu-legilux/${value.work_key}/2015-01-01`, permalink: `/lu-legilux/${value.work_key}/2015-01-01--${"c".repeat(64)}/fra`,
      expression_iri: null, publisher_legal_resource_iri: `${value.publisher_work_iri}/consolide/20150101`, publication_date: "2015-01-10",
    } : null,
    latest_consolidation_publication_date: consolidated ? "2015-01-10" : null,
    consolidations: consolidated ? 1 : 0, consolidations_without_publication_date: 0, amending_acts: 2, amending_acts_without_publication_date: 0,
    notice: notice ? "An act published on 2016-06-03 (…) amends this act, according to the publisher's relation records." : null,
    rule: "R72: an act the publisher records as modifying this act",
  };
  assert.ok(state, "the census's dossier holds a state");
  return envelope;
}

test("the dossier lists the publisher's relations by kind, the inverse views derived, each act's dates by role, and what it does not see", () => {
  const outcome = dossierOutcome({ state: "success", envelope: withRelations() });
  assert.equal(outcome.state, "success", outcome.sentence);
  const markup = view(outcome);
  const shown = text(markup);
  assert.ok(markup.includes('<section data-relations="5">'), markup);
  assert.ok(shown.includes("Relations in the publisher's records"));
  // The tabs: one per kind the records name, each an in-page link to its section, with its count.
  assert.ok(markup.includes('<nav aria-label="Relations listed"><ul data-relation-tabs="4">'), markup);
  assert.ok(markup.includes('<a href="#relations-amended_by">Acts that amend this act (2)</a>'));
  assert.ok(markup.includes('<section id="relations-amended_by" data-relation-group="amended_by" data-derived="true">'));
  assert.ok(markup.includes('<section id="relations-amends" data-relation-group="amends" data-derived="false">'));
  // An inverse view says it is derived; this act's own record does not.
  const derivedAt = markup.indexOf('data-relation-group="amended_by"');
  const ownAt = markup.indexOf('data-relation-group="amends"');
  assert.ok(text(markup.slice(derivedAt, ownAt)).includes("Derived from the publisher's forward relation: the publisher records each of these relations on the other act, not on this one."));
  assert.ok(!markup.slice(ownAt, markup.indexOf('data-relation-group="repealed_by"')).includes("Derived from the publisher"));
  // The other act by its title, in its own language, linked to its page; or by its identifier.
  assert.ok(markup.includes('<a href="/en/lu-legilux/loi-2016-06-01-n1"><span lang="fr">Loi synthétique du 1er juin 2016</span></a>, published on <time dateTime="2016-06-03">2016-06-03</time>; entry into force: <time dateTime="2016-06-07">2016-06-07</time> (publisher)'), markup);
  assert.ok(markup.includes(`<a href="/en/lu-legilux/loi-2017-03-01-n2"><code>${ELI}loi/2017/03/01/n2/jo</code></a>, published on <time dateTime="2017-03-02">2017-03-02</time> (publisher)`));
  assert.ok(markup.includes(`<code>${ELI}rgd/2017/01/01/n1/jo</code>; the publisher&#x27;s record gives no publication date for it`), "an act with no address stays text, and no date is invented");
  // Each row's dates are the other act's own: an act this act amends is not dated by this act's publication.
  assert.ok(markup.includes(`<code>${ELI}loi/2000/01/01/n1/jo</code>, published on <time dateTime="2000-01-04">2000-01-04</time> (publisher)`), markup);
  assert.ok(!markup.includes("1991-08-12"), "the acting act's date of an outbound edge is this act's, and is not said beside the other act");
  // The impact record: read from the publisher's table of modifications, with the consolidated version it names.
  assert.ok(shown.includes("Read from the publisher's table of modifications"));
  assert.ok(markup.includes(`The publisher records this change as integrated in <code>${ELI}loi/1991/08/10/n3/consolide/20170101</code>.`));
  // The kinds read in full that name no act, and what the family does not see.
  assert.ok(shown.includes("The publisher's records, as this site read them, name no act for: Acts this act repeals; Acts that modify this act for a time;"), shown);
  assert.ok(shown.includes("This site read the relation records the publisher keeps on codes, laws and grand-ducal regulations"));
  assertTimeVocabulary(text(markup.slice(markup.indexOf("<section data-relations"))), "the dossier's relations");
});

test("R72's banner says an act published after the latest consolidated version amends the act, names it and its date, and is derived", () => {
  const markup = view(dossierOutcome({ state: "success", envelope: withRelations() }));
  const at = markup.indexOf('<aside class="relation-notice"');
  assert.ok(at > 0 && at < markup.indexOf("<section data-relations"), "the banner is above the dossier's body");
  const banner = text(markup.slice(at, markup.indexOf("</aside>", at)));
  assert.ok(banner.includes("Amended, with no consolidated version published since"));
  assert.ok(banner.includes("An act published on 2016-06-03 ( Loi synthétique du 1er juin 2016 ) amends this act, according to the publisher's relation records; no consolidated version in the publisher's catalogue, as this site read it, was published after it."), banner);
  assert.ok(markup.includes('The latest consolidated version listed there was published on <time dateTime="2015-01-10">2015-01-10</time>.'), banner);
  assert.ok(banner.includes("This is derived from the publisher's forward relation"));
  assert.ok(banner.includes("An amending act other than a code, a law or a grand-ducal regulation is not seen."));
  assertTimeVocabulary(banner, "R72's banner");

  // Two acts: listed, each with its date.
  const two = text(view(dossierOutcome({ state: "success", envelope: withRelations({ acts: 2 }) })));
  assert.ok(two.includes("These acts amend this act, according to the publisher's relation records; no consolidated version in the publisher's catalogue, as this site read it, was published after them:"));
  assert.ok(two.includes("published on 2017-03-02 (publisher)"));

  // An act whose catalogue lists no consolidated version: the notice says so, with no date for one.
  const never = text(view(dossierOutcome({ state: "success", envelope: withRelations({ consolidated: false }) })));
  assert.ok(never.includes("That catalogue lists no consolidated version of this act."), never);
  assert.ok(!never.includes("The latest consolidated version listed there"));

  // The note is the modifies family's: when it was not read in full, it names no class it would claim to have read.
  const unscoped = text(view(dossierOutcome({ state: "success", envelope: withRelations({ familyStates: { modifies: "incomplete" } }) })));
  assert.ok(unscoped.includes("an amending act whose record this site did not read is not seen"), unscoped);
  assert.ok(!unscoped.includes("other than a code, a law or a grand-ducal regulation"));

  // No banner unless the notice is raised.
  for (const status of AMENDED_AFTER_STATUSES.filter((value) => value !== "notice")) {
    const quiet = view(dossierOutcome({ state: "success", envelope: withRelations({ status }) }));
    assert.ok(!quiet.includes("relation-notice"), status);
    assert.ok(quiet.includes("<section data-relations"), `${status}: the relations are listed still`);
  }
});

test("a relation family not read in full says so and claims no absence; an older index shows neither member", () => {
  const unread = view(dossierOutcome({ state: "success", envelope: withRelations({ status: "not_acquired", families: "unacquired" }) }));
  assert.ok(unread.includes('<section data-relations="not_acquired">'));
  assert.ok(text(unread).includes("This site does not hold the publisher's relation records for this act, so none is listed here, and nothing here says that there is none."));
  assert.ok(!text(unread).includes("name no act for"), "no absence is said of a relation not read in full");

  const older = structuredClone(sample);
  delete older.result.value.publisher_relations;
  delete older.result.value.amended_after_consolidation;
  const markup = view(dossierOutcome({ state: "success", envelope: older }));
  assert.ok(!markup.includes("data-relations") && !markup.includes("relation-notice"));
  assert.equal(readPublisherRelations(older.result.value), null);
  assert.equal(readAmendedAfter(older.result.value, older.result.value.work_key), null);
});

test("a kind whose family was not read in full lists the edges recorded and says the records are not all held, never that there is none", () => {
  const markup = view(dossierOutcome({ state: "success", envelope: withRelations({ familyStates: { repeals: "incomplete" } }) }));
  const shown = text(markup);
  assert.ok(markup.includes('data-relation-group="repealed_by"'), "the recorded repeal is listed");
  assert.ok(shown.includes("This site does not hold all of the publisher's records for: Acts that repeal this act; Acts this act repeals."), shown);
  assert.ok(!/name no act for:[^.]*Acts this act repeals/.test(shown), "no absence is said of a kind not read in full");
});

test("the forms the French review replaced are said nowhere", () => {
  const french = JSON.stringify(LIVE_CHROME.fr.dossier);
  for (const replaced of ["Chaque donnée", "une donnée enregistrée", "et non sur celui-ci", "un enregistrement qui nomme", "}, publié le",
    "publiée après lui", "qu’il recense", "chaque acte nommé enregistre", ". Il nomme l’acte", "Modifié après la dernière"]) {
    assert.ok(!french.includes(replaced), `the French says ${replaced}`);
  }
  const english = JSON.stringify(LIVE_CHROME.en.dossier);
  for (const replaced of ["Amended after the latest consolidated version", "only: {classes};"]) {
    assert.ok(!english.includes(replaced), `the English says ${replaced}`);
  }
});

test("an answer that breaks its own rules is drift, never rendered", () => {
  const cases = [
    ["an inverse view said not derived", (value) => { value.publisher_relations.edges[0].derived = false; }],
    ["a count that does not count", (value) => { value.publisher_relations.edge_counts.amended_by = 3; }],
    ["a view of another type", (value) => { value.publisher_relations.edges[0].edge_type = "repeals"; }],
    ["a notice naming no act", (value) => { value.amended_after_consolidation.acts = []; }],
    ["an act not after the latest consolidated version", (value) => { value.amended_after_consolidation.acts[0].publication_date = "2015-01-10"; }],
    ["a family acquired in full with no family", (value) => { value.publisher_relations.families[0].family_keys = []; }],
    ["an address of another work", (value) => { value.publisher_relations.edges[0].other.address = "/lu-legilux/loi-1999-01-01-n1"; }],
    ["a date that is not a date", (value) => { value.publisher_relations.edges[0].acting_publication_date = "2016-06"; }],
  ];
  for (const [name, tamper] of cases) {
    const envelope = withRelations();
    tamper(envelope.result.value);
    assert.equal(dossierOutcome({ state: "success", envelope }).state, "invalid_envelope", name);
  }
});

test("every kind of relation has its tab's name, and the copy says no interval in force, in English and in French", () => {
  for (const language of ["en", "fr"]) {
    const copy = LIVE_CHROME[language].dossier;
    assert.deepEqual(Object.keys(copy.relationGroups), RELATION_VIEWS.map((entry) => entry.view), `${language}: one name per view, in its order`);
    for (const [key, value] of Object.entries(copy).filter(([key]) => key.startsWith("relation") || key.startsWith("amendedAfter"))) {
      for (const said of typeof value === "string" ? [value] : Object.values(value)) assertTimeVocabulary(said, `${language} dossier.${key}`);
    }
  }
});
