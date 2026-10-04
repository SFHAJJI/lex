// The annexes an EU answer lists as not served (`annexes_not_served`, beside each wording of an EU evidence_bundle and
// each expression of an EU dossier), read for the live screens. An annex is never an article of the EU index, so its
// text is never searched, quoted or exported: every row says so (`served_as` is `text_not_available`), counts the annexes
// of one corpus disposition with their digests, and names the official source a reader can follow, with the platform's
// fixed reason. A row that claims anything else is drift and is thrown, never rendered (the launch contract's annex
// line). It renders nothing itself.

/** How every annex an answer lists is served: never as text. */
export const EUROPE_ANNEX_SERVED_AS = 'text_not_available';

/** The corpus's annex dispositions, closed: what it found against the publisher's PDF. */
export const EUROPE_ANNEX_DISPOSITIONS = Object.freeze([
  'annex_text_not_available',
  'annex_mapping_unresolved',
  'annex_body_contains_text',
  'annex_body_contains_no_image',
  'annex_mapped_page_outside_document',
]);

const DIGEST = /^[0-9a-f]{64}$/;

/**
 * The annex rows of one wording or expression, in the answer's order, one per disposition.
 *
 * @param {object} holder a wording of an EU evidence_bundle, or an expression of an EU dossier
 * @param {string} where how a thrown error names the holder
 * @returns {readonly object[]} frozen rows `{disposition, count, identities, officialIdentity, officialSource, reason}`
 */
export function readAnnexesNotServed(holder, where) {
  if (!Object.hasOwn(holder, 'annexes_not_served')) throw new Error(`${where} has no annexes_not_served`);
  const rows = holder.annexes_not_served;
  if (!Array.isArray(rows)) throw new Error(`${where} annexes_not_served is not a list`);
  const seen = new Set();
  return Object.freeze(rows.map((row, at) => {
    const label = `${where} annexes_not_served[${at}]`;
    if (row === null || typeof row !== 'object' || Array.isArray(row)) throw new Error(`${label} is not an object`);
    const own = (key) => {
      if (!Object.hasOwn(row, key)) throw new Error(`${label} has no ${key}`);
      return row[key];
    };
    const text = (key) => {
      const value = own(key);
      if (typeof value !== 'string' || value.length === 0) throw new Error(`${label}.${key} is not text`);
      return value;
    };
    const servedAs = text('served_as');
    if (servedAs !== EUROPE_ANNEX_SERVED_AS) throw new Error(`${label} is served as ${servedAs}; an annex's text is never served`);
    const disposition = text('disposition');
    if (!EUROPE_ANNEX_DISPOSITIONS.includes(disposition)) throw new Error(`${label} names ${disposition}, not an annex disposition`);
    if (seen.has(disposition)) throw new Error(`${label} repeats the disposition ${disposition}; the answer lists one row for each`);
    seen.add(disposition);
    const count = own('annexes');
    if (!Number.isInteger(count) || count < 1) throw new Error(`${label}.annexes is not a count of at least one`);
    const identities = own('annex_identities_sha256');
    if (!Array.isArray(identities) || identities.length !== count
      || !identities.every((identity) => typeof identity === 'string' && DIGEST.test(identity))) {
      throw new Error(`${label} counts ${count} annexes and does not name exactly that many annex digests`);
    }
    return Object.freeze({
      disposition,
      count,
      identities: Object.freeze([...identities]),
      officialIdentity: text('official_identity'),
      officialSource: text('official_source'),
      reason: text('reason'),
    });
  }));
}
