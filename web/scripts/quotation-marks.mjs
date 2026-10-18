// The quotation marks a page or an export adds around quoted wording (content /3, slice S3b): where the publisher's own
// rendering shows marks and its wording does not hold them (a quotation boundary without `marksInWording`), the
// publisher's own mark when it gives one, else « and » with their no-break spaces. None is added where the wording already
// holds a mark (the review of #981, F1): an opening mark is left out when the nearest character of wording before it,
// white space aside, is already an opening mark ("les mots « <embeddedText>…</embeddedText> »"), or when the quoted
// wording itself opens with an opening mark and closes with a closing one ("<embeddedText>« moyen 1 » et « moyen 2
// »</embeddedText>"); a closing mark mirrors it. A mark that is added stands apart from the word before or after it with
// a space when the wording has none there (the French review of S3b, point 4). The text an article is quoted, cited and
// digested by holds none of these marks.

/** The marks added where the publisher gives none of its own. */
export const QUOTE_OPEN = '« ';
export const QUOTE_CLOSE = ' »';

/** The marks that open and close a quotation in the publisher's wording (never the apostrophes, which are not marks). */
const OPENING = new Set(['«', '“', '„', '‹', '"']);
const CLOSING = new Set(['»', '”', '›', '"']);

const isBoundary = (segment) => segment.kind === 'quotation_start' || segment.kind === 'quotation_end';

/** The nearest character of wording before (`step` -1) or after (+1) a segment, quotation boundaries and white space aside; or null. */
function nearestWording(segments, index, step) {
  for (let at = index + step; at >= 0 && at < segments.length; at += step) {
    if (isBoundary(segments[at])) continue;
    const shown = segments[at].text.replace(/\s+/gu, '');
    if (shown.length > 0) return step < 0 ? shown[shown.length - 1] : shown[0];
  }
  return null;
}

/** Whether the wording between a quotation boundary and the one that pairs with it begins with an opening mark and ends with a closing one. */
function holdsItsMarks(segments, index) {
  const forward = segments[index].kind === 'quotation_start';
  let depth = 0;
  let inside = '';
  for (let at = index; at >= 0 && at < segments.length; at += forward ? 1 : -1) {
    const segment = segments[at];
    if (isBoundary(segment)) {
      depth += (segment.kind === 'quotation_start') === forward ? 1 : -1;
      if (depth === 0) break;
      continue;
    }
    if (segment.kind === 'text') inside = forward ? inside + segment.text : segment.text + inside;
  }
  const trimmed = inside.trim();
  return trimmed.length > 1 && OPENING.has(trimmed[0]) && CLOSING.has(trimmed[trimmed.length - 1]);
}

/** The mark a quotation boundary shows: none when the publisher writes its marks or its wording already holds them. */
function shownMark(segments, index) {
  const segment = segments[index];
  if (segment.marksInWording || holdsItsMarks(segments, index)) return '';
  if (segment.kind === 'quotation_start') {
    return OPENING.has(nearestWording(segments, index, -1)) ? '' : segment.marker ?? QUOTE_OPEN;
  }
  return CLOSING.has(nearestWording(segments, index, 1)) ? '' : segment.marker ?? QUOTE_CLOSE;
}

/** The character shown nearest before (`step` -1) or after (+1) a segment, of wording or of a mark the page shows; or null. */
function nearestShown(segments, index, step) {
  for (let at = index + step; at >= 0 && at < segments.length; at += step) {
    const shown = isBoundary(segments[at]) ? shownMark(segments, at) : segments[at].text;
    if (shown.length > 0) return step < 0 ? shown[shown.length - 1] : shown[0];
  }
  return null;
}

/**
 * The mark a page adds for a quotation boundary, or '' when none is added (the publisher writes its marks, or its wording
 * already holds them), with a space before an opening mark that follows a character of wording (not white space, an
 * opening bracket or mark, a slash or a hyphen), and after a closing mark that a letter or digit follows.
 */
export function addedMark(segments, index) {
  const mark = shownMark(segments, index);
  if (mark.length === 0) return '';
  if (segments[index].kind === 'quotation_start') {
    const before = nearestShown(segments, index, -1);
    return `${before !== null && !/[\s([{«“‘'"/-]/u.test(before) ? ' ' : ''}${mark}`;
  }
  const after = nearestShown(segments, index, 1);
  return `${mark}${after !== null && /[\p{L}\p{N}]/u.test(after) ? ' ' : ''}`;
}
