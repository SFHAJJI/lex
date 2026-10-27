// The outside monitor (R107; `site-monitor.mjs`, run by `.github/workflows/v3-monitor.yml`): each check against made-up
// answers, the second look before a failure counts, the certificate's 21 days, what it prints, and the workflow it runs in:
// scheduled, outside Azure, with no secret and nothing but the site asked.
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import { CERTIFICATE_DAYS, DATED_READING, HTTP_CHECKS, SITE, certificateCheck, look, monitor } from "../scripts/site-monitor.mjs";
import { CANONICAL_HOST } from "../scripts/routes.mjs";

const ORIGIN = "https://site.example";
const NOW = Date.parse("2026-10-27T12:00:00Z");
const PAGE_HEADERS = {
  "content-type": "text/html; charset=utf-8",
  "content-security-policy": "default-src 'none'; script-src 'self'; frame-ancestors 'none'",
  "strict-transport-security": "max-age=31536000; includeSubDomains",
};
const FRESH = { stale: false, maximum_data_age_days: 30, channels: [{ publisher: "lu-legilux", age_days: 14 }, { publisher: "eu-eurlex", age_days: 21 }] };

function answer(status, body = "", headers = {}) {
  return { status, headers: { get: (name) => headers[name.toLowerCase()] ?? null }, text: async () => body };
}

/** A site that answers as the live one does, with `change` applied to the answers by path. */
function site(change = {}) {
  const asked = [];
  const fetchImpl = async (url, init) => {
    asked.push({ url, init });
    const path = url.slice(ORIGIN.length);
    if (Object.hasOwn(change, path)) return change[path](init);
    if (path === "/health/ready") return answer(204);
    if (path === "/fr/") return answer(200, '<div id="live-home-root"><section data-answer-state="idle"></section></div>', PAGE_HEADERS);
    if (path === DATED_READING) return answer(200, '<section data-answer-state="success"><p>Article 1</p></section>', PAGE_HEADERS);
    if (path === "/api/v3/coverage") return answer(200, JSON.stringify({ result: { value: {} }, refusal: null, context: { freshness: FRESH } }), { "content-type": "application/json" });
    return answer(404);
  };
  return { asked, fetchImpl };
}

const goodCertificate = async () => ({ authorized: true, authorizationError: null, validTo: new Date(NOW + 120 * 86_400_000) });

test("the live site's answers pass every check, asking nothing but the site, with nothing of anyone's in the requests", async () => {
  const { asked, fetchImpl } = site();
  let slept = 0;
  const result = await monitor(ORIGIN, { fetchImpl, certificate: goodCertificate, now: () => NOW, sleep: async () => { slept++; } });
  assert.equal(result.ok, true, result.lines.join("\n"));
  assert.deepEqual(result.failed, []);
  assert.equal(slept, 0, "no second look when every check holds");
  assert.deepEqual(asked.map(({ url }) => url.slice(ORIGIN.length)), ["/health/ready", "/fr/", DATED_READING, "/api/v3/coverage"]);
  for (const { url, init } of asked) {
    assert.ok(url.startsWith(`${ORIGIN}/`), "only the site is asked");
    assert.equal(init.redirect, "manual");
    assert.equal(init.credentials, "omit");
    assert.equal(init.referrerPolicy, "no-referrer");
    assert.ok(init.signal, "every request is bounded in time");
  }
  assert.equal(asked[3].init.body, '{"operation_id":"coverage","parameters":{}}', "the one operation, with no parameter of anyone's");
  assert.deepEqual(result.lines.map((line) => line.split(":")[0]), ["ok readiness", "ok home", "ok reading", "ok api", "ok certificate"]);
  assert.match(result.lines[3], /data lu-legilux 14 days, eu-eurlex 21 days/);
  assert.match(result.lines[4], /in 120 days/);
});

test("each check fails on what would be an incident", async () => {
  const cases = [
    ["readiness", { "/health/ready": () => answer(503, "No law is mounted.\n") }, /503 .*holds no law/],
    ["home", { "/fr/": () => answer(502, "upstream") }, /not 200/],
    ["home", { "/fr/": () => answer(200, '<div id="live-home-root"></div>', { ...PAGE_HEADERS, "content-security-policy": "default-src 'self'" }) }, /frame-ancestors/],
    ["home", { "/fr/": () => answer(200, '<div id="live-home-root"></div>', { ...PAGE_HEADERS, "strict-transport-security": null }) }, /no HSTS/],
    ["home", { "/fr/": () => answer(200, "<p>Lex is being updated</p>", PAGE_HEADERS) }, /not the home page/],
    ["reading", { [DATED_READING]: () => answer(200, '<section data-answer-state="transport_failure"></section>', PAGE_HEADERS) }, /answer state transport_failure/],
    ["reading", { [DATED_READING]: () => answer(404, "") }, /404 .*answer state none/],
    ["api", { "/api/v3/coverage": () => answer(429, "{}", { "content-type": "application/json" }) }, /not 200/],
    ["api", { "/api/v3/coverage": () => answer(200, JSON.stringify({ result: null, refusal: { code: "no_corpus_mounted" }, context: { freshness: FRESH } })) }, /no answer \(no_corpus_mounted\)/],
    ["api", { "/api/v3/coverage": () => answer(200, JSON.stringify({ result: {}, refusal: null, context: { freshness: { ...FRESH, stale: true } } })) }, /stale/],
    ["api", { "/api/v3/coverage": () => answer(200, "<html>") }, /not JSON/],
    ["api", { "/api/v3/coverage": () => { throw Object.assign(new Error("aborted"), { name: "TimeoutError" }); } }, /no answer: timed out/],
  ];
  for (const [check, change, reason] of cases) {
    const { fetchImpl } = site(change);
    const results = await look(ORIGIN, { fetchImpl, certificate: goodCertificate, now: () => NOW });
    assert.equal(results[check].ok, false, `${check}: ${results[check].detail}`);
    assert.match(results[check].detail, reason, check);
    for (const [other, { ok }] of Object.entries(results)) if (other !== check) assert.equal(ok, true, `${check}'s failure is its own, not ${other}'s`);
  }
});

test("the certificate must be trusted and at least 21 days from its end (R107)", async () => {
  assert.equal(CERTIFICATE_DAYS, 21);
  const at = (days) => async () => ({ authorized: true, authorizationError: null, validTo: new Date(NOW + days * 86_400_000) });
  assert.equal((await certificateCheck(ORIGIN, { certificate: at(21), now: () => NOW })).ok, true);
  const near = await certificateCheck(ORIGIN, { certificate: at(20), now: () => NOW });
  assert.equal(near.ok, false);
  assert.match(near.detail, /within 21 days of its end/);
  const untrusted = await certificateCheck(ORIGIN, { certificate: async () => ({ authorized: false, authorizationError: "CERT_HAS_EXPIRED", validTo: new Date(NOW - 86_400_000) }), now: () => NOW });
  assert.equal(untrusted.ok, false);
  assert.match(untrusted.detail, /not trusted \(CERT_HAS_EXPIRED\)/);
  const asked = [];
  await certificateCheck(ORIGIN, { certificate: async (host) => { asked.push(host); return at(90)(); }, now: () => NOW });
  assert.deepEqual(asked, ["site.example"], "the handshake is with the site's own name");
});

test("a check fails the run only when it fails twice: one failure is looked at again after the wait (R107)", async () => {
  let calls = 0;
  const flaky = site({ "/health/ready": () => (++calls === 1 ? answer(503) : answer(204)) });
  const waits = [];
  const recovered = await monitor(ORIGIN, { fetchImpl: flaky.fetchImpl, certificate: goodCertificate, now: () => NOW, sleep: async (ms) => { waits.push(ms); }, secondLookMs: 1234 });
  assert.equal(recovered.ok, true, "a single failure is not an alert");
  assert.deepEqual(waits, [1234], "one second look, after the wait");
  assert.match(recovered.lines[0], /^ok readiness: 204 .*\(first look: 503/);

  const down = site({ "/health/ready": () => answer(503) });
  const failed = await monitor(ORIGIN, { fetchImpl: down.fetchImpl, certificate: goodCertificate, now: () => NOW, sleep: async () => {} });
  assert.equal(failed.ok, false);
  assert.deepEqual(failed.failed, ["readiness"]);
  assert.match(failed.lines[0], /^FAILED readiness: 503/);
});

test("the monitor asks the public site by default, and holds the dated reading at the readable address of an act every release holds", () => {
  assert.equal(SITE, `https://${CANONICAL_HOST}`);
  assert.equal(DATED_READING, "/fr/eu-eurlex/32016R0679/fra?date=2024-01-01", "the GDPR, an act of the EU population's 82 seeds, at a fixed date");
  assert.deepEqual(Object.keys(HTTP_CHECKS), ["readiness", "home", "reading", "api"]);
});

test("the workflow runs it every five minutes from outside Azure, with no secret, and says a failure twice: a failed run and an issue", async () => {
  const workflow = await readFile(new URL("../../.github/workflows/v3-monitor.yml", import.meta.url), "utf8");
  assert.match(workflow, /schedule:\n\s+- cron: "\*\/5 \* \* \* \*"/, "R107: every five minutes");
  assert.match(workflow, /workflow_dispatch:/, "and on demand");
  assert.doesNotMatch(workflow, /secrets\./, "no secret: the token is the run's own");
  assert.doesNotMatch(workflow, /azure\/login|az login|AZURE_/i, "nothing of Azure: it asks the site as a reader does");
  assert.match(workflow, /permissions:\n\s+contents: read\n\s+issues: write\n/, "it may read the repository and write issues, nothing more");
  assert.match(workflow, /runs-on: ubuntu-latest/);
  assert.match(workflow, /node web\/scripts\/site-monitor\.mjs --origin https:\/\/law\.soufien\.lu/);
  assert.match(workflow, /GH_TOKEN: \$\{\{ github\.token \}\}/);
  assert.match(workflow, /if: failure\(\)/, "a failure opens or updates the incident issue");
  assert.match(workflow, /gh issue (create|comment)/);
  assert.match(workflow, /gh issue close/, "and the issue is closed once the site answers again");
  // The run checks out only what it runs; the script imports nothing else.
  const source = await readFile(new URL("../scripts/site-monitor.mjs", import.meta.url), "utf8");
  const imports = [...source.matchAll(/^import .* from "([^"]+)";$/gm)].map((match) => match[1]).sort();
  assert.deepEqual(imports, ["./invoked-directly.mjs", "node:fs/promises", "node:tls"]);
  for (const file of ["web/scripts/site-monitor.mjs", "web/scripts/invoked-directly.mjs"]) assert.ok(workflow.includes(file), `${file} is checked out`);
});
