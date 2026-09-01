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
const VERSION_KEY = /^([0-9]{4}-[0-9]{2}-[0-9]{2})--([0-9a-f]{64})$/;
const SEGMENT = /^[A-Za-z0-9][A-Za-z0-9._-]*$/;
const ANCHOR = /^[^#\s/]+$/;

/**
 * One segment rule, used by the builders and by the parser.
 *
 * They used to disagree: the builders refused `..` and `.hidden` while the parser accepted
 * `/../secret` as a dossier and handed back a publisher of `..`. A parser that admits what
 * the builders refuse is not a parser, it is a second, weaker specification, and the
 * candidate links in the ambiguous_version card are checked against exactly this one.
 */
export function isSafeSegment(value) {
  return typeof value === 'string' && SEGMENT.test(value) && !SHELL_SET.has(value);
}

/** The publisher's anchor: no separator, no fragment marker, no whitespace. */
export function isSafeAnchor(value) {
  return typeof value === 'string' && ANCHOR.test(value);
}

/**
 * A date that exists. `2026-99-99` and `2025-02-29` match the ISO shape and are not days,
 * and a URL built on one names a state that can never resolve. Leap years are decidable, so
 * they are decided rather than approximated.
 */
export function isCalendarDate(value) {
  if (typeof value !== 'string') return false;
  const match = /^([0-9]{4})-([0-9]{2})-([0-9]{2})$/.exec(value);
  if (!match) return false;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  if (year < 1 || month < 1 || month > 12 || day < 1) return false;
  const leap = (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0;
  const lengths = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  return day <= lengths[month - 1];
}

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
  if (!isCalendarDate(validFrom)) {
    throw new Error(`valid_from is not a calendar date: ${JSON.stringify(validFrom)}`);
  }

  const base = `${dossierUrl({ publisher, work })}/${validFrom}--${hash}`;
  if (anchor === undefined || anchor === null || anchor === '') return base;

  if (!isSafeAnchor(anchor)) {
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
  // `/ask/../../provenance` is a shell prefix that walks out of its own shell. Every
  // segment has to be one the builders would mint.
  const [withoutFragment] = path.split('#');
  for (const segment of withoutFragment.split('/').filter((part) => part.length > 0)) {
    if (!SEGMENT.test(segment)) {
      throw new Error(
        `${JSON.stringify(segment)} is not a safe path segment, so this shell path leaves ` +
          'the shell it claims to be inside',
      );
    }
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

  if (anchor !== null && !isSafeAnchor(anchor)) return null;

  if (segments.length === 2) {
    const [publisher, work] = segments;
    if (!isSafeSegment(publisher) || !isSafeSegment(work)) return null;
    return { kind: 'dossier', publisher, work, anchor: null };
  }

  if (segments.length === 3) {
    const [publisher, work, versionKey] = segments;
    if (!isSafeSegment(publisher) || !isSafeSegment(work)) return null;
    const match = VERSION_KEY.exec(versionKey);
    if (!match || !isCalendarDate(match[1])) return null;
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
