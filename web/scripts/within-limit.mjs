// A probe's request to the API, kept within the API's per-client limit (the specification of record's R109;
// `V3ClientLimiter`): 60 operations a minute for one address, a bucket refilled at one a second, and a refusal
// `rate_limited` with 429 and `Retry-After` past it.
//
// The journeys and a release's image run ask a server on their own machine, which the limit does not count (a caller
// inside the host with no address forwarded by an ingress), so they never meet it. The deploy probe asks a candidate
// revision through the platform's ingress, from one address, and can ask more than a minute's worth in a few seconds (the
// accented-title check alone may ask about a hundred times). So each of its requests goes through here: a 429 is waited
// for, as its Retry-After says, and asked again, and from then on every request of this process to that origin is spaced
// `PACED_MS` apart, below the refill rate, so the probe keeps under the limit and a page a browser probe walks between its
// requests has a token left for its own. Nothing is recorded; the wait is bounded (`MOST_WAITED_SECONDS` in all for one
// request), past which the 429 is answered as it came.

/** Two seconds between requests to an origin once it has answered 429: half the refill rate. */
export const PACED_MS = 2000;

/** The most one Retry-After is waited, in seconds, whatever it says. */
export const MOST_RETRY_AFTER_SECONDS = 60;

/** The most one request waits in all, in seconds, before its 429 is answered as it came. */
export const MOST_WAITED_SECONDS = 300;

const PACED = new Map();

const pause = (milliseconds) => new Promise((resolve) => { setTimeout(resolve, milliseconds); });

/** The seconds a 429 asks to wait: its Retry-After in whole seconds, at least one and at most `MOST_RETRY_AFTER_SECONDS`. */
export function retryAfterSeconds(response) {
  const said = Number.parseInt(response.headers?.get?.("retry-after") ?? "", 10);
  return Math.min(MOST_RETRY_AFTER_SECONDS, Math.max(1, Number.isFinite(said) ? said : 1));
}

/**
 * `fetch(url, init)`, waiting out the API's per-client limit: answers the first response that is not 429, or the last 429
 * once `MOST_WAITED_SECONDS` would be passed. `fetchImpl`, `sleep`, `now` and `paced` (origin to the time of its last
 * request) are injectable for tests.
 */
export async function fetchWithinLimit(url, init, { fetchImpl = globalThis.fetch, sleep = pause, now = Date.now, paced = PACED } = {}) {
  const origin = new URL(url).origin;
  let waited = 0;
  for (;;) {
    if (paced.has(origin)) {
      const gap = paced.get(origin) + PACED_MS - now();
      if (gap > 0) await sleep(gap);
      paced.set(origin, now());
    }
    const response = await fetchImpl(url, init);
    if (response.status !== 429) return response;
    const seconds = retryAfterSeconds(response);
    if (waited + seconds > MOST_WAITED_SECONDS) return response;
    paced.set(origin, now());
    await sleep(seconds * 1000);
    waited += seconds;
  }
}
