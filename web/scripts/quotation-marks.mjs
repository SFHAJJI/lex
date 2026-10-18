// The quotation marks a page or an export adds around quoted wording (content /3, slice S3b): where the publisher's own
// rendering shows marks and its wording does not hold them (a quotation boundary without `marksInWording`), the
// publisher's own mark when it gives one, else « and » with their no-break spaces. A mark stands apart from the word
// before or after it with a space when the wording has none there (the French review of S3b, point 4). The text an article
// is quoted, cited and digested by holds none of these marks.

/** The marks added where the publisher gives none of its own. */
export const QUOTE_OPEN = '« ';
export const QUOTE_CLOSE = ' »';

/** The character shown nearest before (`step` -1) or after (+1) a segment: of wording, or of a mark the page adds; or null. */
function shownNeighbour(segments, index, step) {
  for (let at = index + step; at >= 0 && at < segments.length; at += step) {
    const segment = segments[at];
    const shown = typeof segment.text === 'string'
      ? segment.text
      : segment.marksInWording ? '' : segment.marker ?? (segment.kind === 'quotation_start' ? QUOTE_OPEN : QUOTE_CLOSE);
    if (shown.length > 0) return step < 0 ? shown[shown.length - 1] : shown[0];
  }
  return null;
}

/**
 * The mark a page adds for a quotation boundary the publisher's wording does not mark: the publisher's own mark when it
 * gives one, else « or », with a space before an opening mark that follows a character of wording (not white space, an
 * opening bracket or mark, a slash or a hyphen), and after a closing mark that a letter or digit follows.
 */
export function addedMark(segments, index) {
  const segment = segments[index];
  if (segment.kind === 'quotation_start') {
    const before = shownNeighbour(segments, index, -1);
    return `${before !== null && !/[\s([{«“‘'"/-]/u.test(before) ? ' ' : ''}${segment.marker ?? QUOTE_OPEN}`;
  }
  const after = shownNeighbour(segments, index, 1);
  return `${segment.marker ?? QUOTE_CLOSE}${after !== null && /[\p{L}\p{N}]/u.test(after) ? ' ' : ''}`;
}
