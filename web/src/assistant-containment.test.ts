import assert from "node:assert/strict";
import test from "node:test";
import {
  assistantUnavailableActions,
  retainsAssistantConversation,
  type AskReply,
  type UiEffect,
} from "./api.ts";

test("assistant containment maps only closed action tokens to fixed local routes", () => {
  const ui: UiEffect = {
    gap: {
      status: "assistant_v3_unavailable",
      explanation: "reviewed notice",
      available: [],
      actions: ["search", { token: "browse", href: "javascript:alert(1)" },
        "javascript:alert(1)", "browse", "search"],
      requested_locale: "en",
      available_locales: ["en", "fr"],
    },
  };

  assert.deepEqual(assistantUnavailableActions(ui), [
    { token: "search", label: "Search held law", href: "/?space=search" },
    { token: "browse", label: "Browse held records", href: "/browse" },
  ]);
});

test("assistant containment uses reviewed French labels only for a supported French notice", () => {
  assert.deepEqual(assistantUnavailableActions({
    gap: {
      status: "assistant_v3_unavailable",
      explanation: "avis relu",
      available: [],
      actions: ["search", "browse"],
      requested_locale: "fr",
      available_locales: ["en", "fr"],
    },
  }), [
    { token: "search", label: "Rechercher dans les textes détenus", href: "/?space=search" },
    { token: "browse", label: "Parcourir les textes détenus", href: "/browse" },
  ]);
});

test("unknown result statuses cannot activate containment navigation", () => {
  assert.deepEqual(assistantUnavailableActions({
    gap: {
      status: "unknown_future_status",
      explanation: "untrusted",
      available: [],
      actions: ["search", "browse"],
      requested_locale: "fr",
      available_locales: ["en", "fr"],
    },
  }), []);
});

test("assistant containment cannot enter client conversation history", () => {
  for (const status of ["assistant_v3_unavailable", "localization_unavailable"]) {
    const reply: AskReply = {
      reply: "reviewed notice",
      ui: { gap: { status, explanation: "reviewed notice", available: [] } },
    };
    assert.equal(retainsAssistantConversation(reply), false);
  }

  assert.equal(retainsAssistantConversation({
    reply: "ordinary typed result",
    ui: { gap: { status: "unknown_future_status", explanation: "typed", available: [] } },
  }), true);
});
