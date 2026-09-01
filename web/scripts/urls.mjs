// The URL scheme of UX spec section 1.
//
// Three rules, each of them the reason a link can be trusted.
//
// A shell is a prefix, not a fork. `/ask`, `/w` and `/dev` choose a skin; every deep object
// URL is shell-neutral and identical for every user. If a dossier link carried the shell
// that produced it, two readers looking at the same law would hold different URLs, and the
// citizen who pastes a link to a lawyer would hand over their own reading mode. So the
// builders here cannot produce a shell-prefixed object URL: there is no parameter for it.
//
// The hash is in the reading URL, which is what makes guarantee G1 visible: a replaced
// publisher file mints a new version id, so an old link resolves to the old bytes or to
// nothing, and never silently to different text. A reading URL without a hash is therefore
// refused rather than built.
//
// The anchor is the publisher's, verbatim. `art_1er__2` is not tidied into `art-1er-2`,
// because a re-invented anchor is a coordinate the publisher never minted and cannot be
// checked against them.

/** Shells are skins over one component library, chosen by prefix. */
export const SHELLS = Object.freeze(['ask', 'w', 'dev']);

const SHELL_SET = new Set(SHELLS);
const VERSION_KEY = /^(\d{4}-\d{2}-\d{2})--([0-9a-f]{64})$/;
const SEGMENT = /^[A-Za-z0-9][A-Za-z0-9._-]*$/;

function requireSegment(value, field) {
  if (typeof value !== 'string' || !SEGMENT.test(value)) {
    throw new Error(`${field} is not a safe URL segment: ${JSON.stringify(value)}`);
  }
  if (SHELL_SET.has(value)) {
    throw new Error(
      `${field} is ${value}, which is a shell prefix; an object URL that begins with a shell ` +
        'is not shell-neutral and two readers of the same law would hold different links',
    );
  }
  return value;
}

/** `/{publisher}/{work}` */
export function dossierUrl({ publisher, work }) {
  return `/${requireSegment(publisher, 'publisher')}/${requireSegment(work, 'work')}`;
}

/**
 * `/{publisher}/{work}/{valid_from}--{hash}` plus the publisher's anchor verbatim.
 *
 * @param {object} input
 * @param {string} input.publisher
 * @param {string} input.work
 * @param {string} input.validFrom  ISO date the state applies from
 * @param {string} input.hash       the 64 hex characters that make the link undriftable
 * @param {string} [input.anchor]   the publisher-minted anchor, used exactly as given
 */
export function readingUrl({ publisher, work, validFrom, hash, anchor }) {
  requireSegment(publisher, 'publisher');
  requireSegment(work, 'work');

  if (typeof hash !== 'string' || !/^[0-9a-f]{64}$/.test(hash)) {
    throw new Error(
      'a reading URL requires the 64 hex character state hash; without it the link can drift ' +
        'silently onto different text when the publisher replaces a file',
    );
  }
  if (typeof validFrom !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(validFrom)) {
    throw new Error(`valid_from is not an ISO date: ${JSON.stringify(validFrom)}`);
  }

  const base = `${dossierUrl({ publisher, work })}/${validFrom}--${hash}`;
  if (anchor === undefined || anchor === null || anchor === '') return base;

  if (typeof anchor !== 'string' || /[#\s/]/.test(anchor)) {
    throw new Error(`anchor is not a publisher anchor: ${JSON.stringify(anchor)}`);
  }
  // Verbatim. Not normalised, not lowercased, not re-encoded.
  return `${base}#${anchor}`;
}

/** Applies a shell to a shell-neutral path, for the entry screens only. */
export function shellUrl(shell, path = '/') {
  if (!SHELL_SET.has(shell)) {
    throw new Error(`unknown shell ${JSON.stringify(shell)}`);
  }
  if (!path.startsWith('/')) {
    throw new Error('a shell applies to an absolute path');
  }
  return path === '/' ? `/${shell}` : `/${shell}${path}`;
}

/**
 * Reads a shell-neutral object URL back. Returns null when the path is not one, rather than
 * guessing, because a guessed route is how a reader ends up on the wrong law.
 */
export function parseObjectUrl(path) {
  if (typeof path !== 'string' || !path.startsWith('/')) return null;

  const [withoutFragment, ...fragmentParts] = path.split('#');
  const anchor = fragmentParts.length > 0 ? fragmentParts.join('#') : null;
  const segments = withoutFragment.split('/').filter((part) => part.length > 0);

  if (segments.length === 2) {
    const [publisher, work] = segments;
    if (SHELL_SET.has(publisher)) return null;
    return { kind: 'dossier', publisher, work, anchor: null };
  }

  if (segments.length === 3) {
    const [publisher, work, versionKey] = segments;
    if (SHELL_SET.has(publisher)) return null;
    const match = VERSION_KEY.exec(versionKey);
    if (!match) return null;
    return {
      kind: 'reading',
      publisher,
      work,
      validFrom: match[1],
      hash: match[2],
      anchor,
    };
  }

  return null;
}
