// One typed route validator for every outbound link the interface renders.
//
// Escaping a URI is not validating it. `escapeHtml` makes `javascript:alert(1)` safe to
// place inside an attribute and leaves it a working link, and the first version of the
// refusal card and the verify cluster both did exactly that: any truthy `href` became an
// active link, and any `http://` host rendered under the words "Official source". A reader
// who follows an official-source link and lands somewhere else has been handed the one
// thing this product exists to prevent.
//
// So an outbound link is not a string here. It is a string that survived a policy, and
// there are exactly two policies.
//
// A publisher source must be the publisher's own host over HTTPS. The host set is closed
// per publisher, so a link claiming to be Legilux is checked against Legilux and not
// against "some https URL".
//
// A human handoff must be HTTPS on a host in the handoff registry. That registry is
// editorial work (product spec build item 14, the handoff registry and four-language
// refusal templates) and each counter enters it with a verified address. It is closed and
// currently holds only the synthetic preview host, which is honest: no real counter has
// been verified into this build yet, so none is offered.
//
// Both policies reject userinfo and an explicit port. `https://legilux.public.lu@evil.example/`
// has hostname `evil.example`, and a reader scanning the start of a link sees the publisher.

/** Hosts each publisher actually serves from. Closed, and checked per publisher. */
export const PUBLISHER_HOSTS = Object.freeze({
  'lu-legilux': Object.freeze(['legilux.public.lu', 'data.legilux.public.lu']),
  'eu-eurlex': Object.freeze(['eur-lex.europa.eu', 'publications.europa.eu']),
  // RFC 2606 reserves `.invalid` so that it can never resolve. A synthetic fixture on this
  // host cannot be mistaken for a publisher, and cannot accidentally reach one.
  'preview-synthetic': Object.freeze(['preview.invalid']),
});

/** The handoff registry. Editorial, verified per counter, and currently synthetic only. */
export const HANDOFF_HOSTS = Object.freeze(['handoff.invalid']);

function checkedUri(raw, allowedHosts, what) {
  if (typeof raw !== 'string' || raw.length === 0) {
    throw new Error(`${what} requires a URI`);
  }
  // Before parsing. `new URL` tolerates leading control characters and backslashes, and a
  // string that parses one way here and another way in a browser is a link nobody checked.
  if (!raw.startsWith('https://')) {
    throw new Error(
      `${what} must be an https URI, spelled exactly; ${JSON.stringify(raw)} is not, and a ` +
        'plaintext link under a trusted label is a link a network can rewrite',
    );
  }
  if (/[\s\\<>"']/.test(raw)) {
    throw new Error(`${what} contains whitespace or a delimiter: ${JSON.stringify(raw)}`);
  }

  let parsed;
  try {
    parsed = new URL(raw);
  } catch {
    throw new Error(`${what} is not a URI: ${JSON.stringify(raw)}`);
  }

  if (parsed.protocol !== 'https:') {
    throw new Error(`${what} is not https: ${JSON.stringify(raw)}`);
  }
  if (parsed.username !== '' || parsed.password !== '') {
    throw new Error(
      `${what} carries userinfo, which puts a trusted-looking name before the real host: ` +
        JSON.stringify(raw),
    );
  }
  if (parsed.port !== '') {
    throw new Error(`${what} carries an explicit port: ${JSON.stringify(raw)}`);
  }
  if (!allowedHosts.includes(parsed.hostname)) {
    throw new Error(
      `${what} is on ${parsed.hostname}, which is not one of ${allowedHosts.join(', ')}; a ` +
        'link is not official because the label above it says so',
    );
  }
  return raw;
}

/**
 * The publisher's own address for a state.
 *
 * @param {object} input
 * @param {string} input.publisher  the publisher whose host set applies
 * @param {string} input.uri        the address to check
 */
export function publisherSourceUri({ publisher, uri }) {
  // Own-property lookup. An inherited key such as `toString` would otherwise reach a
  // function and turn a closed registry into an open one.
  if (!Object.hasOwn(PUBLISHER_HOSTS, publisher ?? '')) {
    throw new Error(
      `${JSON.stringify(publisher)} is not a publisher this build serves; the publisher set ` +
        `is closed at ${Object.keys(PUBLISHER_HOSTS).join(', ')}`,
    );
  }
  return checkedUri(uri, PUBLISHER_HOSTS[publisher], `the ${publisher} source URI`);
}

/** A human counter a refusal hands off to. */
export function handoffUri(uri) {
  return checkedUri(uri, HANDOFF_HOSTS, 'a handoff');
}
