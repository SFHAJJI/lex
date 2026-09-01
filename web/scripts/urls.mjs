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

import { isCalendarDate } from './temporal.mjs';

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
  // `/ask/../../provenance` is a shell prefix that walks out of its own shell, and
  // `/ask/w/work` is a shell nested inside a shell. Every segment has to be one the object
  // builders would mint, which refuses both.
  const [withoutFragment] = path.split('#');
  // The same canonical grammar the object parser uses. This filtered empty segments before
  // validating them, so `//search`, `/search/` and `/a//b` all passed and were preserved
  // verbatim, producing paths the object grammar refuses.
  if (withoutFragment.length > 1 && withoutFragment.endsWith('/')) {
    throw new Error('a shell path carries no trailing separator');
  }
  const shellSegments = withoutFragment === '/' ? [] : withoutFragment.slice(1).split('/');
  for (const segment of shellSegments) {
    if (segment.length === 0) {
      throw new Error(`${JSON.stringify(path)} carries an empty path segment`);
    }
    if (!isSafeSegment(segment)) {
      throw new Error(
        `${JSON.stringify(segment)} is not a safe path segment, so this shell path leaves ` +
          'the shell it claims to be inside',
      );
    }
  }
  return path === '/' ? `/${shell}` : `/${shell}${path}`;
}

/**
 * Reads a shell-neutral object URL back, against one exact canonical grammar.
 *
 * The previous version split on `/` and dropped empty segments, so `/lu//work`, `//lu/work`
 * and `/lu/work/` all parsed as the same dossier, and `/lu/work#art_1` parsed as a dossier
 * while silently discarding the anchor. A parser that normalises accepts coordinates the
 * builders can never mint, and every one of those is a link resolving somewhere nobody
 * minted it to point.
 *
 * Returns null rather than guessing, because a guessed route is how a reader ends up on the
 * wrong law.
 */
export function parseObjectUrl(path) {
  if (typeof path !== 'string' || !path.startsWith('/')) return null;

  const hashIndex = path.indexOf('#');
  const withoutFragment = hashIndex === -1 ? path : path.slice(0, hashIndex);
  const anchor = hashIndex === -1 ? null : path.slice(hashIndex + 1);
  if (anchor !== null && !isSafeAnchor(anchor)) return null;

  // No trailing separator and no empty segment anywhere, including a doubled slash.
  if (withoutFragment.length > 1 && withoutFragment.endsWith('/')) return null;
  const segments = withoutFragment.slice(1).split('/');
  // `isSafeSegment` refuses the empty string, so a doubled or leading separator is refused
  // here too. One check, not two: a property with two guards is a property whose first guard
  // can be deleted without anything going red.
  if (!segments.every((segment) => isSafeSegment(segment))) return null;

  if (segments.length === 2) {
    // A dossier addresses a work, not a provision. An anchor here is a coordinate the
    // builder cannot emit, and accepting it while dropping it is worse than refusing it.
    if (anchor !== null) return null;
    const [publisher, work] = segments;
    return { kind: 'dossier', publisher, work, anchor: null };
  }

  if (segments.length === 3) {
    const [publisher, work, versionKey] = segments;
    const match = VERSION_KEY.exec(versionKey);
    if (!match || !isCalendarDate(match[1])) return null;
    return { kind: 'reading', publisher, work, validFrom: match[1], hash: match[2], anchor };
  }

  return null;
}
