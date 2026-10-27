// The probes' requests within the API's per-client limit (R109; `within-limit.mjs`): a 429 is waited out as its
// Retry-After says and asked again, then the origin is paced below the refill rate; a server that never limits is asked
// once per request, with no wait; and the wait is bounded.
import assert from "node:assert/strict";
import test from "node:test";

import { MOST_RETRY_AFTER_SECONDS, MOST_WAITED_SECONDS, PACED_MS, fetchWithinLimit, retryAfterSeconds } from "../scripts/within-limit.mjs";

function response(status, retryAfter) {
  return { status, headers: { get: (name) => (name.toLowerCase() === "retry-after" ? retryAfter ?? null : null) } };
}

function clock() {
  let at = 1_000_000;
  const slept = [];
  return {
    slept,
    now: () => at,
    sleep: async (milliseconds) => { slept.push(milliseconds); at += milliseconds; },
  };
}

test("a server that never limits is asked once per request, with no wait and no pacing", async () => {
  const { slept, now, sleep } = clock();
  const paced = new Map();
  const asked = [];
  for (let index = 0; index < 100; index++) {
    const answer = await fetchWithinLimit("http://127.0.0.1:5000/api/v3/search", { method: "POST" },
      { fetchImpl: async (url) => { asked.push(url); return response(200); }, sleep, now, paced });
    assert.equal(answer.status, 200);
  }
  assert.equal(asked.length, 100);
  assert.deepEqual(slept, [], "the journeys and the image runs, on their own machine, never wait");
  assert.equal(paced.size, 0);
});

test("a 429 is waited out as its Retry-After says and asked again, and the origin is then paced below the refill rate", async () => {
  const { slept, now, sleep } = clock();
  const paced = new Map();
  const answers = [response(429, "1"), response(429, "3"), response(200)];
  const asked = [];
  const fetchImpl = async (url) => { asked.push(url); return answers.shift() ?? response(200); };
  const first = await fetchWithinLimit("https://candidate.example/api/v3/resolve", {}, { fetchImpl, sleep, now, paced });
  assert.equal(first.status, 200);
  assert.equal(asked.length, 3, "asked until answered");
  assert.deepEqual(slept, [1000, 1000, 3000], "the first Retry-After, the pace before asking again, then the second Retry-After");

  slept.length = 0;
  await fetchWithinLimit("https://candidate.example/api/v3/dossier", {}, { fetchImpl, sleep, now, paced });
  assert.equal(slept.length, 1);
  assert.ok(slept[0] > 0 && slept[0] <= PACED_MS, "the next request to that origin waits out the pace");
  slept.length = 0;
  await fetchWithinLimit("https://other.example/api/v3/dossier", {}, { fetchImpl, sleep, now, paced });
  assert.deepEqual(slept, [], "another origin is not paced");
  assert.ok(PACED_MS >= 1000, "below one request a second, the limit's refill");
});

test("the wait is bounded: an unreadable or huge Retry-After is read within bounds, and past the most in all the 429 is answered", async () => {
  assert.equal(retryAfterSeconds(response(429, null)), 1);
  assert.equal(retryAfterSeconds(response(429, "soon")), 1);
  assert.equal(retryAfterSeconds(response(429, "0")), 1);
  assert.equal(retryAfterSeconds(response(429, "86400")), MOST_RETRY_AFTER_SECONDS);

  const { slept, now, sleep } = clock();
  let asked = 0;
  const limited = await fetchWithinLimit("https://candidate.example/api/v3/search", {},
    { fetchImpl: async () => { asked++; return response(429, "60"); }, sleep, now, paced: new Map() });
  assert.equal(limited.status, 429, "answered as it came once the most has been waited");
  assert.equal(slept.filter((wait) => wait === 60_000).length, MOST_WAITED_SECONDS / 60);
  assert.equal(asked, MOST_WAITED_SECONDS / 60 + 1);
});
