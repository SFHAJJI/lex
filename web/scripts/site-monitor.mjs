// The outside monitor (the specification of record's R107; the plan's item 10.3; docs/INCIDENT-RUNBOOK.md): the public site
// asked from outside Azure, as any reader asks it, by `.github/workflows/v3-monitor.yml` every five minutes on GitHub's
// runners, so it still speaks when the subscription, the app or its region does not.
//
// Five checks, each a request any anonymous reader could make, and nothing else:
// - readiness: `GET /health/ready` answers 204 (the server holds the law; 503 when its mount was refused);
// - the home page: `GET /fr/` answers 200 with the page, its Content-Security-Policy with `frame-ancestors 'none'`, and HSTS;
// - one dated reading page: an EU act's readable address at a fixed date (`DATED_READING`), rendered by the server from its
//   answer, whose answer state is `success`;
// - one API operation: `coverage` answers 200 with an answer, and its freshness says the data is not stale (R87: past the
//   declared maximum age every page shows the staleness banner, and the owner is alerted here);
// - the certificate: the name's TLS certificate is trusted and expires no sooner than `CERTIFICATE_DAYS` days from now.
// A check that fails is asked once more after `SECOND_LOOK_MS` (R107: "alerting the owner after two failures"); only a check
// that fails both times fails the run. What it prints is each check's name, status and time, the data's age in days and the
// certificate's end: nothing a reader sent, since it sends nothing of anyone's, and records nothing anywhere but the run's
// own log and summary.
//
//   node scripts/site-monitor.mjs [--origin https://law.soufien.lu] [--report <file>] [--second-look-ms <n>]
//
// Exit 0 when every check holds (on the first look or the second), 1 when one fails twice, 2 on a usage error.

import { appendFile } from "node:fs/promises";
import { connect } from "node:tls";
import { invokedDirectly } from "./invoked-directly.mjs";

/** The public site. */
export const SITE = "https://law.soufien.lu";

/** One dated reading page: the GDPR's French wording on a fixed date, an act of the EU population every release holds. */
export const DATED_READING = "/fr/eu-eurlex/32016R0679/fra?date=2024-01-01";

/** R107: an alert when the certificate is within 21 days of its end. */
export const CERTIFICATE_DAYS = 21;

/** Each request's time, at most, before the check fails as timed out. */
export const TIMEOUT_MS = 20_000;

/** The wait before a failed check is asked a second time. */
export const SECOND_LOOK_MS = 60_000;

const COVERAGE_REQUEST = JSON.stringify({ operation_id: "coverage", parameters: {} });

const DAY_MS = 86_400_000;

/** A fetch bounded by `TIMEOUT_MS`, never following a redirect, never sending a cookie or a referrer. */
async function ask(fetchImpl, url, init = {}) {
  const started = Date.now();
  const response = await fetchImpl(url, {
    ...init, redirect: "manual", credentials: "omit", referrerPolicy: "no-referrer", signal: AbortSignal.timeout(TIMEOUT_MS),
  });
  const text = await response.text();
  return { response, text, ms: Date.now() - started };
}

const said = (status, ms, more = "") => `${status} in ${ms} ms${more}`;

/** The four HTTP checks, by name: each answers `{ ok, detail }`. */
export const HTTP_CHECKS = Object.freeze({
  async readiness(origin, fetchImpl) {
    const { response, ms } = await ask(fetchImpl, `${origin}/health/ready`);
    return { ok: response.status === 204, detail: said(response.status, ms, response.status === 204 ? "" : ", not 204: the server holds no law") };
  },

  async home(origin, fetchImpl) {
    const { response, text, ms } = await ask(fetchImpl, `${origin}/fr/`);
    const problems = [];
    if (response.status !== 200) problems.push(`not 200`);
    if (!/^text\/html/i.test(response.headers.get("content-type") ?? "")) problems.push("not a page");
    if (!(response.headers.get("content-security-policy") ?? "").includes("frame-ancestors 'none'")) problems.push("no Content-Security-Policy with frame-ancestors 'none'");
    if (!(response.headers.get("strict-transport-security") ?? "").includes("max-age=")) problems.push("no HSTS");
    if (!text.includes('id="live-home-root"')) problems.push("not the home page");
    return { ok: problems.length === 0, detail: said(response.status, ms, problems.length ? `: ${problems.join(", ")}` : "") };
  },

  async reading(origin, fetchImpl) {
    const { response, text, ms } = await ask(fetchImpl, `${origin}${DATED_READING}`);
    const state = /data-answer-state="([a-z_]+)"/.exec(text)?.[1] ?? "none";
    const ok = response.status === 200 && state === "success";
    return { ok, detail: said(response.status, ms, `, answer state ${state}${ok ? "" : ", not a reading"}`) };
  },

  async api(origin, fetchImpl) {
    const { response, text, ms } = await ask(fetchImpl, `${origin}/api/v3/coverage`, {
      method: "POST", headers: { "content-type": "application/json" }, body: COVERAGE_REQUEST,
    });
    let envelope = null;
    try { envelope = JSON.parse(text); } catch { /* named below */ }
    const freshness = envelope?.context?.freshness;
    const ages = (freshness?.channels ?? []).map((channel) => `${channel.publisher} ${channel.age_days ?? "?"} days`).join(", ");
    const problems = [];
    if (response.status !== 200) problems.push("not 200");
    if (envelope === null) problems.push("not JSON");
    else if (envelope.result === null || envelope.refusal !== null) problems.push(`no answer (${envelope.refusal?.code ?? "no result"})`);
    if (freshness?.stale !== false) problems.push(`the data is stale or its age unstated (maximum ${freshness?.maximum_data_age_days ?? "?"} days)`);
    return { ok: problems.length === 0, detail: said(response.status, ms, `${ages ? `, data ${ages}` : ""}${problems.length ? `: ${problems.join(", ")}` : ""}`) };
  },
});

/**
 * The certificate `host` presents on port 443, as `{ authorized, authorizationError, validTo }` (`validTo` a Date): a TLS
 * handshake with the name, nothing sent over it.
 */
export function presentedCertificate(host, { timeoutMs = TIMEOUT_MS } = {}) {
  return new Promise((resolve, reject) => {
    const socket = connect({ host, port: 443, servername: host, rejectUnauthorized: false, timeout: timeoutMs }, () => {
      const certificate = socket.getPeerCertificate();
      const answer = { authorized: socket.authorized, authorizationError: socket.authorizationError ?? null, validTo: new Date(certificate.valid_to) };
      socket.end();
      resolve(answer);
    });
    socket.on("timeout", () => { socket.destroy(); reject(new Error("timed out")); });
    socket.on("error", reject);
  });
}

/** The certificate check: trusted, and `CERTIFICATE_DAYS` or more days before its end. */
export async function certificateCheck(origin, { certificate = presentedCertificate, now = Date.now } = {}) {
  const { authorized, authorizationError, validTo } = await certificate(new URL(origin).hostname);
  const days = Math.floor((validTo.getTime() - now()) / DAY_MS);
  const problems = [];
  if (!authorized) problems.push(`not trusted (${authorizationError ?? "unknown"})`);
  if (!(days >= CERTIFICATE_DAYS)) problems.push(`within ${CERTIFICATE_DAYS} days of its end`);
  return { ok: problems.length === 0, detail: `ends ${Number.isNaN(validTo.getTime()) ? "unknown" : validTo.toISOString().slice(0, 10)}, in ${days} days${problems.length ? `: ${problems.join(", ")}` : ""}` };
}

/** One look: every check, each a failure of its own when it throws (a refused connection, a time-out). */
export async function look(origin, { fetchImpl = globalThis.fetch, certificate, now } = {}) {
  const results = {};
  for (const [name, check] of [...Object.entries(HTTP_CHECKS).map(([name, run]) => [name, () => run(origin, fetchImpl)]), ["certificate", () => certificateCheck(origin, { certificate, now })]]) {
    try {
      results[name] = await check();
    } catch (error) {
      results[name] = { ok: false, detail: `no answer: ${error?.name === "TimeoutError" ? "timed out" : error?.message ?? String(error)}` };
    }
  }
  return results;
}

/**
 * The monitor: a look, and when a check fails a second look after `secondLookMs`; it fails only by the checks that failed
 * both times. Answers `{ ok, failed, lines }`: the checks that failed twice, and one line per check as the last look saw it.
 */
export async function monitor(origin, { fetchImpl, certificate, now, sleep = (ms) => new Promise((resolve) => { setTimeout(resolve, ms); }), secondLookMs = SECOND_LOOK_MS } = {}) {
  const first = await look(origin, { fetchImpl, certificate, now });
  const failing = Object.keys(first).filter((name) => !first[name].ok);
  if (failing.length === 0) return { ok: true, failed: [], lines: lines(first, {}) };
  await sleep(secondLookMs);
  const second = await look(origin, { fetchImpl, certificate, now });
  const failed = failing.filter((name) => !second[name].ok);
  return { ok: failed.length === 0, failed, lines: lines(second, first) };
}

function lines(results, earlier) {
  return Object.entries(results).map(([name, { ok, detail }]) => {
    const before = earlier[name] && !earlier[name].ok ? ` (first look: ${earlier[name].detail})` : "";
    return `${ok ? "ok" : "FAILED"} ${name}: ${detail}${before}`;
  });
}

if (invokedDirectly(import.meta.url, process.argv[1])) {
  const argv = process.argv.slice(2);
  const option = (name, fallback) => {
    const at = argv.indexOf(name);
    return at < 0 ? fallback : argv[at + 1];
  };
  const origin = option("--origin", SITE);
  const report = option("--report", null);
  const secondLookMs = Number(option("--second-look-ms", String(SECOND_LOOK_MS)));
  if (!/^https:\/\/[a-z0-9.-]+$/.test(origin ?? "") || !Number.isInteger(secondLookMs) || secondLookMs < 0) {
    console.error("usage: node scripts/site-monitor.mjs [--origin https://<host>] [--report <file>] [--second-look-ms <n>]");
    process.exit(2);
  }
  const result = await monitor(origin, { secondLookMs });
  const text = [`${result.ok ? "The site answers" : `The site failed ${result.failed.join(", ")} twice`} (${origin}, ${new Date().toISOString()}):`, ...result.lines.map((line) => `- ${line}`)].join("\n");
  console.log(text);
  if (report) await appendFile(report, `${text}\n`);
  if (process.env.GITHUB_STEP_SUMMARY) await appendFile(process.env.GITHUB_STEP_SUMMARY, `${text}\n`);
  process.exit(result.ok ? 0 : 1);
}
