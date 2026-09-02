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

import { parseObjectUrl, readingUrl } from './urls.mjs';

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

/**
 * A publisher's own name for a work, as opposed to a link to it.
 *
 * An ELI or a CELEX is an identifier that happens to be spelled as an HTTP URI. It is a name:
 * it is printed, cited and compared, and it is not somewhere a reader is sent. Putting it
 * through the outbound-link policy was a category error, and it had a consequence rather than
 * being merely untidy: both identifiers the pack cites as live are `http://`, so the dossier
 * could not render a single real work. The only escape was rewriting the scheme, which mints an
 * identifier the publisher never issued and prints it as the work's own name.
 *
 * The live data shows how different the two are. Legilux names a work
 * `http://data.legilux.public.lu/eli/...` and serves its file from
 * `https://legilux.public.lu/eli/...`: different scheme, different host, same work.
 *
 * So this keeps the publisher's host allowlist, because a name on somebody else's namespace is
 * not that publisher's name for anything, and keeps the refusal of userinfo, ports and
 * malformed escapes. It permits the scheme the publisher actually issues. It returns a string
 * for display, and nothing here is an href.
 *
 * @param {object} input
 * @param {string} input.publisher  the publisher whose namespace this name must be in
 * @param {string} input.uri        the identifier as the publisher issues it
 */
export function publisherIdentifier({ publisher, uri }) {
  if (!Object.hasOwn(PUBLISHER_HOSTS, publisher ?? '')) {
    throw new Error(
      `${JSON.stringify(publisher)} is not a publisher this build serves; the publisher set ` +
        `is closed at ${Object.keys(PUBLISHER_HOSTS).join(', ')}`,
    );
  }
  const what = `the ${publisher} work identifier`;
  if (typeof uri !== 'string' || uri.length === 0) {
    throw new Error(`${what} requires a value`);
  }
  const scheme = uri.startsWith('https://') ? 'https://' : uri.startsWith('http://') ? 'http://' : null;
  if (scheme === null) {
    throw new Error(
      `${what} must be an http or https identifier, spelled exactly; ` +
        `${JSON.stringify(uri)} is neither`,
    );
  }
  if (/[\s\<>"']/.test(uri)) {
    throw new Error(`${what} contains whitespace or a delimiter: ${JSON.stringify(uri)}`);
  }
  const authority = uri.slice(scheme.length).split(/[/?#]/, 1)[0];
  if (authority.includes('@')) {
    throw new Error(`${what} carries userinfo: ${JSON.stringify(uri)}`);
  }
  if (authority.includes(':')) {
    throw new Error(`${what} carries an explicit port: ${JSON.stringify(uri)}`);
  }
  if (!HOST.test(authority)) {
    throw new Error(`${what} does not carry a plain host: ${JSON.stringify(uri)}`);
  }
  if (!PUBLISHER_HOSTS[publisher].includes(authority)) {
    throw new Error(
      `${what} is on ${authority}, which is not one of ` +
        `${PUBLISHER_HOSTS[publisher].join(', ')}; a name in somebody else's namespace is not ` +
        "this publisher's name for anything",
    );
  }
  return uri;
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

/**
 * The one host this product serves its own object URLs from.
 *
 * Declared here because it did not exist anywhere: it was a literal inside a single preview
 * fixture, so nothing could check that a link claiming to be one of ours actually was.
 */
export const CANONICAL_HOST = 'law.soufien.lu';

/**
 * A state permalink, validated rather than pattern-matched.
 *
 * The guard this replaces was `permalink.includes('--')`, which `javascript:alert(1)--x`
 * satisfies, and the value was then rendered as an href. Containing a digest separator is not
 * evidence of anything; being a canonical same-origin state URL is.
 *
 * Accepts the absolute form on this product's own host, which is what the service emits, and
 * the root-relative form. Everything else is refused: another host, another scheme,
 * protocol-relative, userinfo, a port, a backslash, or a path the object-URL grammar rejects.
 *
 * @param {unknown} value
 * @returns {{path: string, publisher: string, work: string, validFrom: string, hash: string,
 *   anchor: string|null}|null} the parsed state, or null
 */
export function canonicalStateUrl(value) {
  if (typeof value !== 'string' || value.length === 0) return null;
  // A backslash is a separator to some parsers and not to others, so it never reaches one here.
  // Written as a code point because a backslash literal in a shell heredoc has been mangled
  // twice on this project already, once silently into a backspace byte.
  if (value.includes(String.fromCharCode(92))) return null;

  let path = value;
  if (!value.startsWith('/')) {
    let parsed;
    try {
      parsed = new URL(value);
    } catch {
      return null;
    }
    if (parsed.protocol !== 'https:') return null;
    // One equality, on the authority as written, is the whole host policy.
    //
    // `URL` normalizes before it reports, and every normalization it performs is a way to make
    // a hostile URL read as a familiar one. It lowercases `LAW.SOUFIEN.LU`, drops the default
    // port so `parsed.port` is empty for both `host/` and `host:443/`, and reports
    // `law.soufien.lu` as the hostname of `https://law.soufien.lu@evil.example/`, where the
    // familiar name is userinfo and the host is not. A reader sees the raw string, so the raw
    // string is what has to be well formed.
    //
    // Written as one check rather than as separate host, userinfo and port checks, because the
    // separate ones are all strictly weaker than this and each other's shadow: with this line
    // present, none of them can fail on any input, so a test cannot hold them and deleting one
    // turns nothing red. This equality refuses another host, any userinfo, any port, a
    // different case and a trailing label separator, and it refuses them for the same reason.
    const authority = value.slice('https://'.length).split('/')[0];
    if (authority !== CANONICAL_HOST) return null;
    if (parsed.search !== '') return null;
    path = `${parsed.pathname}${parsed.hash}`;
  } else if (value.startsWith('//')) {
    // Protocol-relative: `//evil.example/x` is off-site and starts with a slash.
    //
    // Kept although `parseObjectUrl` also refuses it today, and this is a deliberate exception
    // to the rule stated above. That refusal is a side effect of its empty-segment rule, in
    // another module, and its own comment records that an earlier version dropped empty
    // segments instead. If that rule ever relaxes again, this is the line that keeps an
    // off-origin link from being published as a permalink. A guard that survives a mutation is
    // worth keeping when the thing shadowing it lives behind a different module's contract.
    return null;
  }

  const object = parseObjectUrl(path);
  if (object === null || object.kind !== 'reading') return null;
  return { path, ...object };
}

/**
 * The absolute permalink for one state, minted rather than written out.
 *
 * The parser above is only worth having if nothing hand-writes what it is meant to check. The
 * preview fixture used to interpolate the host and the version key itself, so the one place
 * that demonstrated the policy was also the one place that bypassed it, and a change to the
 * grammar would have left the fixture asserting the old one.
 *
 * @param {object} input  the same coordinates `readingUrl` takes
 */
/**
 * A same-origin search path, validated rather than assumed from its first character.
 *
 * A leading slash was the whole check where this is used, and `//evil.example/x` has one.
 * Protocol-relative is off-site and begins with a slash, so a control offering the reader their
 * own words back offered a one-tap trip to another origin instead.
 *
 * @param {unknown} value
 * @returns {{path: string, query: string}|null}
 */
export function canonicalSearchPath(value) {
  if (typeof value !== 'string' || !value.startsWith('/')) return null;
  // Shadowed and kept. Removing this line fails no test, because the route-shape check below
  // already refuses `//evil.example/x`: its path is not one of the three search routes. Verified
  // by seeding exactly that mutation rather than assuming it. It stays because protocol-relative
  // is the classic form of this bypass and the route check is the thing most likely to be
  // relaxed later, at which point this becomes the only line refusing it.
  if (value.startsWith('//')) return null;
  // Backslash, written as a code point: some parsers treat it as a separator and some do not,
  // so it never reaches one here.
  if (value.includes(String.fromCharCode(92))) return null;
  if (value.includes('#')) return null;
  // A control character or whitespace inside a path is a parser disagreement waiting to happen.
  if (/[\u0000-\u0020\u007f]/.test(value)) return null;

  const [path, ...rest] = value.split('?');
  if (rest.length > 1) return null;
  if (!/^\/(ask|w|dev)\/search$/.test(path)) return null;
  return { path, query: rest[0] ?? '' };
}

export function canonicalStateHref(input) {
  return `https://${CANONICAL_HOST}${readingUrl(input)}`;
}
