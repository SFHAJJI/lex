// How current the law on this site is, and which code built and serves it: the freshness header and the commits every
// answer carries (the specification of record's R87 and R23; the plan's items 6.1 and 6.5), as a page says them.
//
// The server states every value (`V3EnvelopeJson.FreshnessNode`): the index's build time; for each publisher channel the
// mount holds, when its newest complete collection ended (as the mount's build report states it, or null), its age in whole
// days at the answer and whether that age is past the declared maximum; the maximum itself; whether any channel is past it;
// the lag range, which is never stated while no observation time is held (plan 1.12) and the refresh has not run; the
// publisher re-check's line (R69), which none has earned yet; and the two commits, "index built from" and "service
// running". A page computes no age, no staleness and no lag of its own: it reads the answer's, by the reader's rules
// (`v3-envelope.mjs` has already held the envelope to them), and says each date in Luxembourg, as the home page's coverage
// line says the index's (R13).
//
// Every page carries the strip in its footer and, when a channel is stale, the banner under its header (`Freshness.jsx`):
// a page the server rendered from an answer says them from that answer; a page as built says the strip's as-built line, and
// once it has any answer, from the latest (`showFreshness`, `client.jsx`). No request is made for it.
//
// Data and pure functions only: the pages, their client entries and the server renderer share it.

import { luxembourgDate } from "./temporal.mjs";

/** The publisher channels a page names, in the order an answer names them. */
export const FRESHNESS_CHANNELS = Object.freeze(["lu-legilux", "eu-eurlex"]);

/** The event a page's every answer is announced by (`askV3`), which the footer strip and the banner follow. */
export const ANSWER_EVENT = "lex-v3-answer";

const COMMIT = /^[0-9a-f]{40}$/;
const isCount = (value) => Number.isInteger(value) && value >= 0;

function dateIn(instant) {
  try {
    return luxembourgDate(instant);
  } catch {
    return null;
  }
}

/**
 * What a page says of an answer's freshness and commits, or null when the envelope carries none it can read: `today`, the
 * answer's date in Luxembourg; `builtOn`, the index's build date in Luxembourg or null; `maximumAgeDays`; `stale`; each
 * channel's `publisher`, `completedOn` (the date its last complete collection ended, in Luxembourg, or null), `ageDays`
 * and `stale` (both null when no completion is stated); `lagMeasured` (always false: none is stated); and the two commits.
 */
export function freshnessFacts(envelope) {
  const freshness = envelope?.context?.freshness;
  const commits = envelope?.context?.commits;
  if (freshness === null || typeof freshness !== "object" || commits === null || typeof commits !== "object") return null;
  if (!Array.isArray(freshness.channels) || !isCount(freshness.maximum_data_age_days) || typeof freshness.stale !== "boolean") return null;
  const today = typeof freshness.observed_at === "string" ? dateIn(freshness.observed_at) : null;
  if (today === null) return null;
  const builtOn = freshness.index_built_at === null ? null : dateIn(freshness.index_built_at);
  if (freshness.index_built_at !== null && builtOn === null) return null;
  const channels = [];
  for (const channel of freshness.channels) {
    if (!FRESHNESS_CHANNELS.includes(channel?.publisher)) return null;
    const completedOn = channel.newest_completed_observation === null ? null : dateIn(channel.newest_completed_observation);
    if (channel.newest_completed_observation !== null && completedOn === null) return null;
    if (completedOn === null ? channel.age_days !== null || channel.stale !== null : !isCount(channel.age_days) || typeof channel.stale !== "boolean") {
      return null;
    }
    channels.push(Object.freeze({ publisher: channel.publisher, completedOn, ageDays: channel.age_days, stale: channel.stale }));
  }
  const commit = (value) => (typeof value === "string" && COMMIT.test(value) ? value : null);
  return Object.freeze({
    today,
    builtOn,
    maximumAgeDays: freshness.maximum_data_age_days,
    stale: freshness.stale,
    channels: Object.freeze(channels),
    lagMeasured: freshness.channels.some((channel) => channel.lag_days !== null),
    indexCommit: commit(commits.index_builder_source_commit),
    serviceCommit: commit(commits.serving_runtime_source_commit),
  });
}

/** The channels past the maximum age, which the staleness banner names; none when the facts are none. */
export function staleChannels(facts) {
  return facts === null ? [] : facts.channels.filter((channel) => channel.stale === true);
}

/** A key that is the same for the same facts, so a page does not draw the strip again for an answer that says the same. */
export function freshnessKey(facts) {
  return facts === null ? "as-built" : JSON.stringify(facts);
}

/**
 * Announces an answer a page received to whatever follows its answers (the footer strip and the banner, `client.jsx`);
 * nothing outside a page (a test, the server renderer) has a document to announce it on.
 */
export function announceAnswer(envelope) {
  const target = globalThis.document;
  if (target === undefined || target === null || typeof target.dispatchEvent !== "function" || typeof globalThis.CustomEvent !== "function") return;
  target.dispatchEvent(new globalThis.CustomEvent(ANSWER_EVENT, { detail: envelope }));
}
