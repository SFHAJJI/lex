import assert from "node:assert/strict";
import test from "node:test";
import {
  UNKNOWN_WORK_BODY, UNKNOWN_WORK_HEADING, WORK_CANDIDATE_CAP, validateWorkCandidate,
  workCandidateHref, workCandidatesFrom,
} from "./workCandidates.ts";

test("valid candidates pass and links are rebuilt from validated parts", () => {
  const out = workCandidatesFrom([
    { work: "loi-2020-12-19-a1039", title: "Loi du 19 decembre 2020", publisher: "lu-legilux" },
  ]);
  assert.equal(out.length, 1);
  assert.equal(workCandidateHref(out[0]), "/lu-legilux/loi-2020-12-19-a1039");
});

test("hostile and malformed candidates are ignored, never rendered or navigated", () => {
  assert.equal(validateWorkCandidate(null), null);
  assert.equal(validateWorkCandidate("x"), null);
  assert.equal(validateWorkCandidate({ work: "../..", publisher: "lu-legilux" }), null);
  assert.equal(validateWorkCandidate({ work: "a/b", publisher: "lu-legilux" }), null);
  assert.equal(validateWorkCandidate({ work: "ok-slug", publisher: "evil host" }), null);
  assert.equal(validateWorkCandidate({ work: "ok-slug", publisher: "lu-legilux/../x" }), null);
  assert.equal(validateWorkCandidate({ work: "x".repeat(201), publisher: "lu-legilux" }), null);
  assert.equal(validateWorkCandidate({ work: "javascript:alert(1)", publisher: "lu-legilux" }), null);
  // A missing title survives; an over-long one is truncated, not rejected.
  const long = validateWorkCandidate({
    work: "ok-slug", publisher: "lu-legilux", title: "t".repeat(400) });
  assert.ok(long);
  assert.equal(long!.title!.length, 300);
});

test("the list is capped and non-arrays produce nothing", () => {
  const many = Array.from({ length: 9 }, (_, index) => ({
    work: `w-${index}`, publisher: "lu-legilux" }));
  assert.equal(workCandidatesFrom(many).length, WORK_CANDIDATE_CAP);
  assert.equal(workCandidatesFrom(undefined).length, 0);
  assert.equal(workCandidatesFrom("no").length, 0);
});

test("the frozen copy is byte-equal to Decision 41, heading and complete body", () => {
  assert.equal(UNKNOWN_WORK_HEADING, "Instrument not found in held records");
  assert.equal(UNKNOWN_WORK_BODY,
    "Lex does not hold an instrument matching this identifier. This is not evidence that "
    + "the instrument or law does not exist. Check the identifier. If possible held records "
    + "are listed below, choose one; otherwise search the official publisher.");
});
