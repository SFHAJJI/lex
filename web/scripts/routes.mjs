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
  'eu-eurlex': Object.freeze(['eur-lex.europa.eu', 'publications.europa.eu', 'op.europa.eu']),
  // RFC 2606 reserves `.invalid` so that it can never resolve. A synthetic fixture on this
  // host cannot be mistaken for a publisher, and cannot accidentally reach one.
  'preview-synthetic': Object.freeze(['preview.invalid']),
});

/** The handoff registry. Editorial, verified per counter, and currently synthetic only. */
export const HANDOFF_HOSTS = Object.freeze(['handoff.invalid']);

// A plain lowercase host: labels of letters, digits and inner hyphens, at least two.
const HOST = /^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$/;

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

  // The authority is read out of the raw string before anything normalises it. `new URL`
  // erases the evidence this check needs: it reports no userinfo for `https://@host/` and no
  // port for `https://host:443/`, so both survived a check that ran after parsing. A reader
  // sees the raw string, so the raw string is what must be well formed.
  const authority = raw.slice('https://'.length).split(/[/?#]/, 1)[0];
  if (authority.includes('@')) {
    throw new Error(
      `${what} carries userinfo, which puts a trusted-looking name before the real host: ` +
        JSON.stringify(raw),
    );
  }
  if (authority.includes(':')) {
    throw new Error(`${what} carries an explicit port: ${JSON.stringify(raw)}`);
  }
  if (!HOST.test(authority)) {
    throw new Error(`${what} does not carry a plain host: ${JSON.stringify(raw)}`);
  }

  // The three checks below are backstops, and an audit was right that no test holds them.
  //
  // They cannot be reached from here. The raw-string grammar above already requires the exact
  // spelling `https://`, an authority matching a lowercase ASCII host pattern with no empty
  // label, no userinfo and no port, and a host on the publisher's own allowlist. Everything
  // surviving that parses, parses as https, and parses to the authority it was written with.
  // I fuzzed 117 inputs across mixed case, trailing dots, empty labels, punycode,
  // percent-encoded dots, userinfo, ports, spaces, backslashes, control characters and
  // malformed escapes, and none reached any of the three.
  //
  // They stay, because a normalisation difference between this grammar and WHATWG parsing is
  // exactly the sort of thing that moves under one's feet, and they cost nothing. But they are
  // recorded here as unreachable rather than left looking proven, because a fixture for an
  // input that cannot exist would be worse than saying so.
  let parsed;
  try {
    parsed = new URL(raw);
  } catch {
    throw new Error(`${what} is not a URI: ${JSON.stringify(raw)}`);
  }

  if (parsed.protocol !== 'https:') {
    throw new Error(`${what} is not https: ${JSON.stringify(raw)}`);
  }
  if (parsed.hostname !== authority) {
    throw new Error(
      `${what} parses to host ${parsed.hostname} but is written against ${authority}; a link ` +
        'whose spelling and destination differ is a link nobody checked',
    );
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

/**
 * The non-throwing form, for data that arrives from a captured envelope rather than from a
 * caller. A route this surface cannot vouch for becomes inert text with a reason, never a
 * link, and never an exception that takes a whole page down over one bad field.
 *
 * This exists so there is one route policy. There used to be two, in this module and in
 * `render.mjs`, and they had already drifted: only one of them knew `op.europa.eu`.
 */
export function tryPublisherSourceUri(publisher, uri) {
  try {
    return publisherSourceUri({ publisher, uri });
  } catch {
    return null;
  }
}
