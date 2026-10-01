// The one way the web surface asks the V3 API a question.
//
// A request is a same-origin POST to `/api/v3/{operation}` with the envelope request
// `{"operation_id", "parameters"}` as its only body. The parameters never go anywhere else: not
// into the URL, the history, storage, the console or the page title; the API records no query text,
// IP or user agent (S4-A11) and this surface does not either. No cookie or credential is sent, the
// answer is not cached, no referrer is sent, a redirect is an error rather than followed, and a
// failure is reported once and never retried behind the reader's back.
//
// What comes back is one of the renderable states (`STATES` in `envelope.mjs`): `success` and
// `refusal` carry an envelope the strict reader verified (`v3-envelope.mjs`), `transport_failure`
// carries the problem code the API sent below the envelope (or `network_error`), and
// `invalid_envelope` carries the reason the answer could not be read. A caller renders `loading`
// itself until the promise settles.

import { readV3Envelope } from "./v3-envelope.mjs";

/** The operations the API serves; a request for any other is refused here, before it is sent. */
export const SERVED_OPERATIONS = Object.freeze([
  "answer_drift", "article_history", "as_observed", "as_of", "ask", "browse", "changes_in_period", "citation",
  "cited_by", "classification", "coverage", "diff", "dossier", "events", "evidence_bundle",
  "in_force_on", "manifestation", "provenance", "relations", "resolve", "search", "status_on",
  "timeline", "verify",
]);

const PROBLEM_MEDIA_TYPE = "application/problem+json";
const JSON_MEDIA_TYPE = "application/json";

/**
 * Reads the whole body as text. A cancellation stays a cancellation; any other failure while the
 * body arrives (a reset connection) is the network's, not a malformed answer's.
 */
async function bodyText(response) {
  try {
    return { text: await response.text() };
  } catch (error) {
    if (error?.name === "AbortError") throw error;
    return { failure: { state: "transport_failure", code: "network_error" } };
  }
}

function parseJson(text) {
  try {
    return { value: JSON.parse(text) };
  } catch {
    return { invalid: true };
  }
}

function mediaType(response) {
  const value = response.headers.get("content-type") ?? "";
  return value.split(";")[0].trim().toLowerCase();
}

/**
 * Asks one operation. `fetchImpl` is injectable so the rules above are tested without a network;
 * in a page it is the browser's `fetch`.
 *
 * @param {string} operation a served operation id
 * @param {object} parameters the request's parameters, sent only in the body
 * @param {{contract: object, objectType?: string, fetchImpl?: typeof fetch, signal?: AbortSignal}} options
 */
export async function askV3(operation, parameters, options) {
  const { contract, objectType, signal } = options ?? {};
  const fetchImpl = options?.fetchImpl ?? globalThis.fetch;
  if (!SERVED_OPERATIONS.includes(operation)) {
    throw new Error(`${operation} is not an operation the API serves`);
  }
  if (parameters === null || typeof parameters !== "object" || Array.isArray(parameters)) {
    throw new Error("the parameters are an object");
  }

  let response;
  try {
    response = await fetchImpl(`/api/v3/${operation}`, {
      method: "POST",
      headers: { "content-type": JSON_MEDIA_TYPE },
      body: JSON.stringify({ operation_id: operation, parameters }),
      credentials: "omit",
      cache: "no-store",
      referrerPolicy: "no-referrer",
      redirect: "error",
      signal,
    });
  } catch (error) {
    if (error?.name === "AbortError") throw error;
    return { state: "transport_failure", code: "network_error" };
  }

  const type = mediaType(response);
  if (type === PROBLEM_MEDIA_TYPE) {
    const read = await bodyText(response);
    if (read.failure) return read.failure;
    // A problem body that is not JSON still names a transport failure; its code is unknown.
    const problem = parseJson(read.text).value;
    const code = typeof problem?.code === "string" ? problem.code : "unknown_problem";
    return { state: "transport_failure", code, status: response.status };
  }

  if (response.status !== 200 || type !== JSON_MEDIA_TYPE) {
    return {
      state: "invalid_envelope",
      reason: `the API answered ${response.status} ${type || "with no media type"}, not an envelope`,
    };
  }

  const received = await bodyText(response);
  if (received.failure) return received.failure;
  const parsed = parseJson(received.text);
  if (parsed.invalid) return { state: "invalid_envelope", reason: "the answer is not JSON" };

  const read = readV3Envelope(parsed.value, contract, { operation, objectType });
  return read.ok
    ? { state: read.state, envelope: read.envelope }
    : { state: "invalid_envelope", reason: read.reason };
}
