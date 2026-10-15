// A service failure on a live screen, panel by panel (the specification of record's R60: "Service failures are not
// refusals. A failure says 'Lex could not complete this request. This is not an answer about the law.', with a retry and
// official links. Failures are shown per panel. The operational refusals (rate_limited, upstream_unreachable,
// no_corpus_mounted, snapshot_unknown) render from their payloads with the same line. A failure never shows as an empty
// list, a zero or 'no version'.").
//
// Which states are failures is decided here, once, for every screen: a state that carries no answer about the law because
// the request did not complete. That is a transport failure (the network, a problem the API or the live server sent below
// the envelope: a 5xx, a timeout, an unreachable API), an answer the page could not read (`invalid_envelope`, which is
// also what a 5xx without a problem body becomes, `v3-client.mjs`), and a refusal whose code is operational: it is about
// this service, never about the law. A refusal about the law (an identifier not held, a date before the history) is an
// answer, and stays one. A request the form would not send (`invalid_request`) is the reader's to correct, and is no
// failure: nothing was asked.
//
// Each screen keeps what it said for the state (its sentence, or the refusal card rendered from the refusal's payload) and
// says it inside the failure panel (`FailureFrame`, `LiveAnswer.jsx`), under the line, with a retry that asks the same
// request again and the publishers' official sites. This module is data and pure functions: the server renderer runs it
// too, in an engine with no `URL`, so the sites are constants here and a test holds each to the publisher-link policy
// (`publisherSourceUri`, `routes.mjs`).

/** The operational refusals of the registry (R60): each says what this service could not do, never what the law is. */
export const OPERATIONAL_REFUSALS = Object.freeze(["rate_limited", "upstream_unreachable", "no_corpus_mounted", "snapshot_unknown"]);

/** The states that carry no answer because the request did not complete. */
export const FAILURE_STATES = Object.freeze(["transport_failure", "invalid_envelope"]);

/** Whether a screen's view state is a service failure: shown with the failure line, a retry and the official sites. */
export function isFailure(outcome) {
  if (outcome === null || typeof outcome !== "object") return false;
  if (FAILURE_STATES.includes(outcome.state)) return true;
  return outcome.state === "refusal" && OPERATIONAL_REFUSALS.includes(outcome.code);
}

/**
 * The publishers' official sites a failure points to: Legilux for Luxembourg law and EUR-Lex for EU law, each its home page
 * (the same routes the get-help screen offers, `get-help-preview.mjs`). `key` names its label in the chrome table
 * (`failure.sites`).
 */
export const OFFICIAL_SITES = Object.freeze([
  Object.freeze({ key: "legilux", publisher: "lu-legilux", uri: "https://legilux.public.lu/" }),
  Object.freeze({ key: "eurlex", publisher: "eu-eurlex", uri: "https://eur-lex.europa.eu/" }),
]);
