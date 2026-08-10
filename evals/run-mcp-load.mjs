import { writeFile } from "node:fs/promises";
import { performance } from "node:perf_hooks";

const values = new Map(process.argv.slice(2).map(argument => {
  const split = argument.indexOf("=");
  if (split < 0) throw new Error(`Expected --name=value, received ${argument}`);
  return [argument.slice(0, split), argument.slice(split + 1)];
}));
const baseUrl = required("--base-url").replace(/\/$/, "");
const output = required("--out");
const durationSeconds = integer("--duration-seconds", 125, 120, 600);
const ratePerMinute = integer("--rate-per-minute", 80, 1, 100);
const maximumP95Ms = integer("--maximum-p95-ms", 1_500, 1, 60_000);
const requestTimeoutMs = integer("--request-timeout-ms", 10_000, 100, 60_000);
const results = [];
let sequence = 0;

function required(name) {
  const value = values.get(name);
  if (!value) throw new Error(`${name} is required`);
  return value;
}

function integer(name, fallback, minimum, maximum) {
  const raw = values.get(name);
  const value = raw === undefined ? fallback : Number(raw);
  if (!Number.isInteger(value) || value < minimum || value > maximum)
    throw new Error(`${name} must be an integer from ${minimum} to ${maximum}`);
  return value;
}

function percentile(sorted, fraction) {
  if (sorted.length === 0) return null;
  return sorted[Math.min(sorted.length - 1, Math.ceil(sorted.length * fraction) - 1)];
}

function body(hybrid) {
  return JSON.stringify({
    jsonrpc: "2.0",
    id: ++sequence,
    method: "tools/call",
    params: {
      name: "search",
      arguments: {
        query: hybrid ? "personal data protection rights" : "32016R0679",
        publisher: "eu-eurlex",
        retrieval_mode: hybrid ? "hybrid" : "keyword",
        fuzzy: "off",
        limit: 3,
      },
    },
  });
}

async function call(kind, hybrid) {
  const started = performance.now();
  const cancellation = AbortSignal.timeout(requestTimeoutMs);
  try {
    const response = await fetch(`${baseUrl}/mcp`, {
      method: "POST",
      headers: {
        "accept": "application/json, text/event-stream",
        "content-type": "application/json",
      },
      body: body(hybrid),
      signal: cancellation,
    });
    const text = await response.text();
    const status = response.status;
    const rateLimited = status === 429 && text.includes("rate_limited");
    const busy = status === 503 && text.includes("busy");
    const boundedOverload = rateLimited || busy;
    const ok = status === 200 && !text.includes('"error"');
    results.push({ kind, hybrid, status, ok, boundedOverload, rateLimited, busy,
      elapsed_ms: Math.round((performance.now() - started) * 10) / 10 });
  } catch (error) {
    results.push({ kind, hybrid, status: 0, ok: false, boundedOverload: false,
      elapsed_ms: Math.round((performance.now() - started) * 10) / 10,
      error: error instanceof Error ? error.name : "request_failed" });
  }
}

// A hybrid-heavy burst must fail excess work at the public boundary rather than growing the
// execution set without limit. The subsequent two-minute stream proves the limiter recovers.
await Promise.all(Array.from({ length: 48 }, (_, index) => call("burst", index % 2 === 0)));

const intervalMs = 60_000 / ratePerMinute;
const sustainedStarted = performance.now();
const scheduled = [];
let sustainedIndex = 0;
while (performance.now() - sustainedStarted < durationSeconds * 1_000) {
  scheduled.push(call("sustained", sustainedIndex++ % 5 === 0));
  const target = sustainedStarted + sustainedIndex * intervalMs;
  const wait = target - performance.now();
  if (wait > 0) await new Promise(resolve => setTimeout(resolve, wait));
}
await Promise.all(scheduled);
// Fill the remainder of the current per-client window without spoofing forwarded addresses.
// The exact ceiling is also covered with a controlled clock in unit tests; this public path
// proves the overload remains a bounded JSON-RPC response under real ingress.
for (let index = 0; index < 80; index++) await call("rate_probe", false);
await new Promise(resolve => setTimeout(resolve, 61_000));
await call("recovery", false);

const sustained = results.filter(item => item.kind === "sustained");
const acceptedLatency = sustained.filter(item => item.ok)
  .map(item => item.elapsed_ms).sort((left, right) => left - right);
const report = {
  schema: "lex-mcp-load/1",
  generated_at: new Date().toISOString(),
  target: baseUrl,
  configuration: {
    duration_seconds: durationSeconds,
    rate_per_minute: ratePerMinute,
    burst_requests: 48,
    maximum_p95_ms: maximumP95Ms,
    request_timeout_ms: requestTimeoutMs,
  },
  outcome: {
    total: results.length,
    accepted: results.filter(item => item.ok).length,
    bounded_overload: results.filter(item => item.boundedOverload).length,
    busy: results.filter(item => item.busy).length,
    rate_limited: results.filter(item => item.rateLimited).length,
    failed: results.filter(item => !item.ok && !item.boundedOverload).length,
    sustained_accepted: sustained.filter(item => item.ok).length,
    sustained_rejected: sustained.filter(item => !item.ok).length,
    p50_ms: percentile(acceptedLatency, 0.50),
    p95_ms: percentile(acceptedLatency, 0.95),
    p99_ms: percentile(acceptedLatency, 0.99),
    maximum_ms: acceptedLatency.at(-1) ?? null,
    recovery_succeeded: results.at(-1)?.kind === "recovery" && results.at(-1)?.ok === true,
  },
  results,
};
await writeFile(output, `${JSON.stringify(report, null, 2)}\n`, "utf8");

const problems = [];
if (report.outcome.bounded_overload === 0) problems.push("burst produced no bounded overload");
if (report.outcome.busy === 0) problems.push("burst produced no busy response");
if (report.outcome.rate_limited === 0) problems.push("rate probe produced no rate-limited response");
if (report.outcome.failed !== 0) problems.push("requests failed outside the bounded overload contract");
if (report.outcome.sustained_rejected !== 0) problems.push("admissible sustained requests were rejected");
if (!report.outcome.recovery_succeeded) problems.push("the endpoint did not recover after overload");
if (report.outcome.p95_ms === null || report.outcome.p95_ms > maximumP95Ms)
  problems.push(`p95 ${report.outcome.p95_ms}ms exceeded ${maximumP95Ms}ms`);
console.log(JSON.stringify(report.outcome));
if (problems.length > 0) {
  for (const problem of problems) console.error(problem);
  process.exitCode = 1;
}
