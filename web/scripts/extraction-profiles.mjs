// How the text was obtained, per extraction profile.
//
// A profile identifier like `akn-lu/1` is a fact about provenance, and printed bare it is a fact
// only to whoever wrote the pipeline. The reader is entitled to know whether the words they are
// reading came from the publisher's own XML with the publisher's own article boundaries, or were
// cut out of a scanned gazette with the boundaries inferred. Those are different claims about the
// same screen, and the second one is the reason `diff` refuses across profiles at all.
//
// Two design choices worth stating, because the obvious versions are both wrong.
//
// **The table is closed, but a page never refuses on it.** Refusing to render a state because this
// build has no description for its profile would punish a reader for our gap, on a page whose law
// text is perfectly valid. So an undescribed profile renders its identifier and says plainly that
// no description exists. What must never happen is the third option, printing the identifier with
// silence beside it, which reads as if nothing needed saying.
//
// **The refusal lives in a test instead.** A test asserts that every profile any V3 surface renders
// is one this table describes, so an undescribed profile fails offline at the moment somebody
// introduces it, rather than on a reader's screen weeks later. That is the same shape as rejecting
// target drift at the binder rather than at the executor: put the refusal where a user is not
// needed to discover it.
//
// Scope. This table describes the profiles V3 surfaces actually carry today. V3 has no derivation
// layer yet: it arrives with the Stage 3 builders (#344), and the profile vocabulary it mints is
// that stage's to settle. Describing the eight further profiles the V2 layer emits would be
// asserting provenance for extractions this codebase has never performed, so they are recorded
// on #344 instead and land here when V3 mints them.
//
// The occasion for the file is a real defect in the V2 surface: `akn-lu/3` is minted by
// `AknLuProfileV3` and absent from the catalogue's description switch, which falls through to an
// empty string, so a profile renders with silence beside it. That is the shape this prevents.

/**
 * Profiles this build can describe, and what each one means.
 *
 * `Object.create(null)` rather than a literal, so the table carries no inherited members. Note
 * that it is not what makes `profileNote('constructor')` safe: the `Object.hasOwn` lookup below
 * already ignores the prototype, and a mutation swapping this for a plain literal was not caught
 * by any test, correctly, because it changes nothing observable. The null prototype is defence in
 * depth for a future lookup written with `in` or with plain member access, which is the form the
 * publisher vocabulary was actually bitten by.
 */
const PROFILE_NOTES = Object.freeze(
  Object.assign(Object.create(null), {
    'akn-lu/1':
      "the publisher's own XML (Akoma Ntoso), with the article boundaries the publisher marked",
    'akn-lu/2':
      "the publisher's own XML (Akoma Ntoso), with the article boundaries the publisher marked; " +
      'structural placeholders the publisher left empty are kept as evidence of coverage and are ' +
      'not searchable',
    'xhtml-eu/1':
      "the publisher's own XHTML, with the article boundaries the publisher marked",
    'pdf-lu/1':
      "read from the publisher's PDF, with the article boundaries inferred from the page layout",
  }),
);

/** The profiles this build can describe, ordered, for tests and for the developer surface. */
export const DESCRIBED_PROFILES = Object.freeze(Object.keys(PROFILE_NOTES).sort());

/** Said where a profile has no description, instead of silence. */
export const UNDESCRIBED_NOTE =
  'This build carries no description of how text under this profile was obtained. That is a gap ' +
  'in this page, not a statement about the text.';

/**
 * What a profile means, or null when this build cannot say.
 *
 * Null rather than a thrown error, and rather than a cheerful default. A default would be the
 * worst of the three: it would describe a profile nobody had described, in a sentence a reader
 * would reasonably treat as provenance.
 */
export function profileNote(profile) {
  // The type check is load bearing: a number or an object reaching `Object.hasOwn` would be
  // coerced to a key and could match. A length check is not, and used to be here: the empty
  // string is not a key, so `Object.hasOwn` already returns false for it. Removing it changed
  // nothing any test could see, which is the definition of a shadowed guard, so it is gone rather
  // than left to imply a rule it was not enforcing.
  if (typeof profile !== 'string') {
    return null;
  }
  return Object.hasOwn(PROFILE_NOTES, profile) ? PROFILE_NOTES[profile] : null;
}

/**
 * A profile as a renderable claim: the identifier, what it means, and whether we could say.
 *
 * The `described` flag is returned rather than left for the caller to infer from `note === null`,
 * so that two renderers cannot disagree about what an absent note means.
 *
 * @param {string} profile the extraction profile identifier as the record carries it
 */
export function describeProfile(profile) {
  const note = profileNote(profile);
  return Object.freeze({
    profile,
    note: note ?? UNDESCRIBED_NOTE,
    described: note !== null,
  });
}
