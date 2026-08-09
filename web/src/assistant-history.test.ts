import test from "node:test";
import assert from "node:assert/strict";
import { askQuestionError, boundedAskHistory } from "./api.ts";

test("assistant history is validated and bounded to six restored turns", () => {
  const source = Array.from({ length: 14 }, (_, index) => ({
    role: index % 2 === 0 ? "user" : "assistant",
    content: `message ${index}`,
  }));
  source.splice(4, 0, { role: "tool", content: "untrusted" });

  const history = boundedAskHistory(source);

  assert.equal(history.length, 12);
  assert.equal(history[0].content, "message 2");
  assert.equal(history.at(-1)?.content, "message 13");
  assert.ok(history.every((item) => item.role === "user" || item.role === "assistant"));
});

test("assistant history rejects non-array persisted state", () => {
  assert.deepEqual(boundedAskHistory({ role: "user", content: "question" }), []);
});

test("assistant history cannot exceed the server message limit or begin with an orphan reply", () => {
  const history = boundedAskHistory([
    { role: "assistant", content: "orphan" },
    { role: "user", content: "question" },
    { role: "assistant", content: "x".repeat(6_500) },
  ]);

  assert.deepEqual(history.map((item) => item.role), ["user", "assistant"]);
  assert.equal(history[1].content.length, 4000);
});

test("an over-limit current question is rejected instead of silently changed", () => {
  const question = "x".repeat(4001);

  assert.match(askQuestionError(question) ?? "", /4,000/);
  assert.throws(() => boundedAskHistory([{ role: "user", content: question }]), RangeError);
});
