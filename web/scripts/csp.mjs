// The content security policy every V3 page carries.
//
// #360 requires replacing the synthetic `scripts=0` and `script-src 'none'` assumptions before
// any browser-quality claim is made, because a hydrated client ships scripts and the old gate
// was only ever a proxy for the property that matters.
//
// The property is not "no JavaScript". It is that nothing executes which was not reviewed and
// served from this origin. `scripts=0` happened to imply that while the pages shipped none; it
// stops implying it the moment one is hydrated, and a gate that silently becomes vacuous is
// worse than one that was never there, because it keeps reporting a pass.
//
// So the directives are enumerated here, as data, and the browser run asserts the served policy
// against this exact object rather than against a substring. A policy is a closed statement, and
// checking that it "contains script-src 'self'" would pass a policy that also contained
// 'unsafe-inline'.

/**
 * The policy, directive by directive.
 *
 * `default-src 'none'` first, so anything not enumerated below is refused rather than inherited.
 * Every later directive is an explicit grant, and a fetch kind nobody thought about fails closed.
 */
export const CSP_DIRECTIVES = Object.freeze({
  'default-src': Object.freeze(["'none'"]),
  // Same-origin only, and no inline. An inline script cannot be reviewed by reading the
  // deployed bundle, and 'unsafe-inline' would readmit every injection this product's escaping
  // exists to stop.
  'script-src': Object.freeze(["'self'"]),
  'style-src': Object.freeze(["'self'"]),
  'img-src': Object.freeze(["'self'"]),
  'font-src': Object.freeze(["'self'"]),
  // The client talks to this origin and nowhere else. A retrieval product that can be made to
  // fetch from another host is a retrieval product whose answers came from somewhere unstated.
  'connect-src': Object.freeze(["'self'"]),
  'form-action': Object.freeze(["'self'"]),
  // A rewritten base element re-points every relative URL on the page at once.
  'base-uri': Object.freeze(["'none'"]),
  'object-src': Object.freeze(["'none'"]),
});

/**
 * Source expressions that must never appear, whatever the directive.
 *
 * Enumerated so the browser run can assert their absence rather than assert the presence of the
 * good parts. A policy grows by accident; these are the growths that make it decorative.
 */
export const FORBIDDEN_SOURCES = Object.freeze([
  "'unsafe-inline'",
  "'unsafe-eval'",
  "'unsafe-hashes'",
  "'strict-dynamic'",
  'data:',
  'blob:',
  '*',
  'http:',
  'https:',
]);

/**
 * Directives a meta element cannot enforce, whatever it says.
 *
 * The browser reported this rather than a document: delivered via meta, `frame-ancestors` is
 * ignored. Declaring it there produces a policy that reads stronger than it is, which is the
 * decorative-policy failure this module exists to prevent, so it is excluded from the meta value
 * and named here as the serving layer's obligation. `sandbox` and `report-uri` are the same
 * shape and are listed so nobody adds them to the meta later.
 *
 * These belong in an HTTP response header. That is the serving layer, which is Codex's lane, and
 * this constant is what the request against it should quote.
 */
export const HEADER_ONLY_DIRECTIVES = Object.freeze({
  'frame-ancestors': Object.freeze(["'none'"]),
});

/** The meta-deliverable policy, directives in declared order. */
export function cspValue() {
  return Object.entries(CSP_DIRECTIVES)
    .map(([directive, sources]) => `${directive} ${sources.join(' ')}`)
    .join('; ');
}
