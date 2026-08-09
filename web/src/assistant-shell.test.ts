import assert from "node:assert/strict";
import test from "node:test";
import {
  STARTER_PROMPTS,
  assistantWorkspaceState,
  assistantWorkspaceUrl,
  parseAssistantPanelState,
} from "./assistantShell.ts";

test("first visit is closed and only valid tab-scoped state is restored", () => {
  assert.deepEqual(parseAssistantPanelState(null), { open: false, minimized: false });
  assert.deepEqual(parseAssistantPanelState("garbage"), { open: false, minimized: false });
  assert.deepEqual(parseAssistantPanelState('{"open":true,"minimized":false}'),
    { open: true, minimized: false });
  assert.deepEqual(parseAssistantPanelState('{"open":true,"minimized":true}'),
    { open: true, minimized: true });
  assert.deepEqual(parseAssistantPanelState('{"open":false,"minimized":true}'),
    { open: false, minimized: false });
});

test("starter prompts demonstrate the four typed research capabilities", () => {
  assert.deepEqual(STARTER_PROMPTS, [
    "Show Article 6 of the GDPR as it stood on 1 January 2021.",
    "Compare Article 92 of the CRR between 1 January 2020 and 31 December 2024.",
    "When did Article 92 of the CRR change?",
    "Which Luxembourg and EU laws changed most during 2024?",
  ]);
});

test("typed effects map to bounded workspace state rather than model-authored links", () => {
  const law = assistantWorkspaceUrl({ provision: {
    subject: { work: "eu-eurlex:32016R0679", date: "2021-01-01", anchor: "art_6" },
    valid_from: "2021-01-01", provisions: [],
  } });
  assert.equal(law,
    "/?space=law&work=eu-eurlex%3A32016R0679&date=2021-01-01&anchor=art_6");

  const diff = assistantWorkspaceUrl({ diff: {
    subject: { work: "eu-eurlex:32013R0575", anchor: "art_92", language: "en" },
    from_date: "2020-01-01", to_date: "2024-12-31",
  } });
  assert.equal(diff,
    "/?space=law&work=eu-eurlex%3A32013R0575&date=2020-01-01&to=2024-12-31&anchor=art_92&mode=compare&language=en");

  assert.deepEqual(assistantWorkspaceState({ diff: {
    subject: { work: "eu-eurlex:32013R0575", anchor: "art_92", language: "en" },
    from_date: "2020-01-01", to_date: "2024-12-31", status: "profiles_differ",
  } }), {
    space: "law", q: undefined, asOf: undefined, work: "eu-eurlex:32013R0575",
    date: "2020-01-01", to: "2024-12-31", anchor: "art_92", mode: "compare",
    from: undefined, until: undefined, order: undefined, retrieval: undefined,
    jurisdiction: undefined, hierarchy: undefined, domain: undefined,
    sourceClass: undefined, actForm: undefined, bindingStatus: undefined,
    language: "en",
  });

  const ranking = assistantWorkspaceUrl({ ranking: {
    from_date: "2024-01-01", to_date: "2024-12-31", order: "by_churn",
    works_changed: 2, new_versions: 3, rows: [],
  } });
  assert.equal(ranking,
    "/?space=time&from=2024-01-01&until=2024-12-31&order=by_churn");

  assert.equal(assistantWorkspaceUrl({ gap: {
    status: "no_result", explanation: "No result", available: [],
  } }), undefined);
});

test("aggregate results replace stale workspace scope with the exact tool scope", () => {
  const crossCorpus = assistantWorkspaceState({
    ranking: {
      from_date: "2024-01-01", to_date: "2024-12-31", order: "by_churn",
      works_changed: 397, new_versions: 440, rows: [],
    },
  });
  assert.deepEqual(crossCorpus, {
    space: "time", q: undefined, asOf: undefined, work: undefined, date: undefined,
    to: undefined, anchor: undefined, mode: "read", from: "2024-01-01",
    until: "2024-12-31", order: "by_churn", retrieval: undefined,
    jurisdiction: undefined, hierarchy: undefined, domain: undefined,
    sourceClass: undefined, actForm: undefined, bindingStatus: undefined,
    language: undefined,
  });

  const luxembourgOnly = {
    ranking: {
      from_date: "2024-01-01", to_date: "2024-12-31", order: "by_churn",
      works_changed: 210, new_versions: 240, rows: [],
    },
    workspace: { jurisdiction: "lu", source_class: "LOI" },
  };
  assert.equal(assistantWorkspaceUrl(luxembourgOnly),
    "/?space=time&from=2024-01-01&until=2024-12-31&order=by_churn&jurisdiction=lu&sourceClass=LOI");
});

test("dated lists and filter-only effects share the same deterministic workspace mapping", () => {
  const inForce = {
    in_force: { date: "2024-06-30", total: 12, rows: [] },
    workspace: { jurisdiction: "eu", hierarchy: "secondary_eu_law" },
  };
  assert.deepEqual(assistantWorkspaceState(inForce), {
    space: "search", q: undefined, asOf: "2024-06-30", work: undefined,
    date: undefined, to: undefined, anchor: undefined, mode: "read", from: undefined,
    until: undefined, order: undefined, retrieval: undefined, jurisdiction: "eu",
    hierarchy: "secondary_eu_law", domain: undefined, sourceClass: undefined,
    actForm: undefined, bindingStatus: undefined, language: undefined,
  });
  assert.equal(assistantWorkspaceUrl(inForce),
    "/?space=search&asOf=2024-06-30&jurisdiction=eu&hierarchy=secondary_eu_law");

  assert.equal(assistantWorkspaceUrl({ workspace: {
    jurisdiction: "lu", source_class: "LOI,CODE",
  } }), "/?space=search&jurisdiction=lu&sourceClass=LOI%2CCODE");
});

test("search and whole-work timeline effects open their matching workspace state", () => {
  const search = { workspace: {
    query: "capital requirements", jurisdiction: "eu",
  } };
  assert.deepEqual(assistantWorkspaceState(search), {
    space: "search", q: "capital requirements", asOf: undefined, work: undefined,
    date: undefined, to: undefined, anchor: undefined, mode: "read", from: undefined,
    until: undefined, order: undefined, retrieval: undefined, jurisdiction: "eu",
    hierarchy: undefined, domain: undefined, sourceClass: undefined, actForm: undefined,
    bindingStatus: undefined, language: undefined,
  });
  assert.equal(assistantWorkspaceUrl(search),
    "/?space=search&q=capital+requirements&jurisdiction=eu");

  assert.equal(assistantWorkspaceUrl({ timeline: {
    subject: { work: "eu-eurlex:32013r0575" },
  } }), "/?space=law&work=eu-eurlex%3A32013r0575");
});
