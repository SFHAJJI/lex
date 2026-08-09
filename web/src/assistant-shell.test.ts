import assert from "node:assert/strict";
import test from "node:test";
import {
  STARTER_PROMPTS,
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
    "Compare Article 92 of the CRR between 2020 and 2024.",
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
    subject: { work: "eu-eurlex:32013R0575", anchor: "art_92" },
    from_date: "2020-01-01", to_date: "2024-01-01",
  } });
  assert.equal(diff,
    "/?space=law&work=eu-eurlex%3A32013R0575&date=2020-01-01&to=2024-01-01&anchor=art_92&mode=compare");

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
