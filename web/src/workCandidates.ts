/**
 * Nearest held records beside an unknown-work refusal (Decision 41), workspace side. The wire
 * carries coordinates only; this module validates them fail closed and the component owns the
 * frozen copy. Links are reconstructed from the validated coordinates, never read from the
 * payload, so a malformed or hostile entry can neither render nor navigate anywhere.
 */

export interface WorkCandidate {
  work: string;
  title?: string;
  publisher: string;
}

export const WORK_CANDIDATE_CAP = 5;

/**
 * Decision 41 frozen copy, browser authority for the workspace surface. The heading and the
 * complete body render on EVERY unknown_work gap, with or without candidates: the candidate
 * list is optional evidence, the notice is not, and the absence boundary must never be
 * replaced by a bald non-holding claim.
 */
export const UNKNOWN_WORK_HEADING = "Instrument not found in held records";
export const UNKNOWN_WORK_BODY =
  "Lex does not hold an instrument matching this identifier. This is not evidence that the "
  + "instrument or law does not exist. Check the identifier, choose a possible held record "
  + "below, or search the official publisher.";
export const UNKNOWN_WORK_CANDIDATES_HEADING = "Possible held records";

const IDENTIFIER = /^[a-z0-9][a-z0-9._-]{0,199}$/i;
const PUBLISHER = /^[a-z0-9][a-z0-9-]{0,63}$/i;

export function validateWorkCandidate(value: unknown): WorkCandidate | null {
  if (typeof value !== "object" || value === null || Array.isArray(value)) return null;
  const record = value as Record<string, unknown>;
  const work = record.work;
  const publisher = record.publisher;
  if (typeof work !== "string" || !IDENTIFIER.test(work)) return null;
  if (typeof publisher !== "string" || !PUBLISHER.test(publisher)) return null;
  const title = typeof record.title === "string" && record.title.length > 0
    ? record.title.slice(0, 300)
    : undefined;
  return { work, title, publisher };
}

export function workCandidatesFrom(value: unknown): WorkCandidate[] {
  if (!Array.isArray(value)) return [];
  return value.map(validateWorkCandidate)
    .filter((item): item is WorkCandidate => item !== null)
    .slice(0, WORK_CANDIDATE_CAP);
}

/** The one internal link shape a candidate may navigate to, built from validated parts only. */
export const workCandidateHref = (candidate: WorkCandidate): string =>
  `/${candidate.publisher}/${candidate.work}`;
