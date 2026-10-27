// The publisher's relation records of a Luxembourg act (`publisher_relations` and `amended_after_consolidation`, beside a dossier
// or a relations answer over lex-v3-luxembourg-index/10; the specification of record's R29, R72 and R21), read for the live
// screens. An edge is a record the publisher keeps: an act this act amends, repeals or rectifies, an act that does so to it, a
// temporary modification either way, or a row of the publisher's table of modifications. Each is listed from this act's side,
// with the other act's identifier, its title where the catalogue states one, its address on this site where it has one, and the
// acting act's publication date and entry into force, each its own value (R21). An edge read from a record kept on another
// resource is derived from the publisher's forward relation, and the view says so (R29). R72's notice says an act published
// after every consolidated version amends this act; it names the act, never its effect. An answer over an older index carries
// neither member, and this reads it as null; a row that breaks the answer's own rules is drift and is thrown, never rendered.
// It renders nothing itself.

import { isCalendarDate } from './temporal.mjs';
import { luxembourgPermalink } from './permalinks.mjs';

/** The views of an edge, in the order a dossier lists them, each with the edge type it is a view of. */
export const RELATION_VIEWS = Object.freeze([
  Object.freeze({ view: 'amended_by', edgeType: 'modifies', direction: 'inbound' }),
  Object.freeze({ view: 'amends', edgeType: 'modifies', direction: 'outbound' }),
  Object.freeze({ view: 'repealed_by', edgeType: 'repeals', direction: 'inbound' }),
  Object.freeze({ view: 'repeals', edgeType: 'repeals', direction: 'outbound' }),
  Object.freeze({ view: 'modified_temporarily_by', edgeType: 'modified_temporarily_by', direction: 'outbound' }),
  Object.freeze({ view: 'modifies_temporarily', edgeType: 'modified_temporarily_by', direction: 'inbound' }),
  Object.freeze({ view: 'rectified_by', edgeType: 'rectifies', direction: 'inbound' }),
  Object.freeze({ view: 'rectifies', edgeType: 'rectifies', direction: 'outbound' }),
  Object.freeze({ view: 'impact_received', edgeType: 'impact', direction: 'inbound' }),
  Object.freeze({ view: 'impact_made', edgeType: 'impact', direction: 'outbound' }),
]);

/** The edge types, each with the relation family that holds it. */
export const RELATION_EDGE_TYPES = Object.freeze(['modifies', 'repeals', 'rectifies', 'modified_temporarily_by', 'impact']);

/** The states a relation family's acquisition takes; only `acquired_complete` supports an absence. */
export const RELATION_FAMILY_STATES = Object.freeze(['acquired_complete', 'unacquired', 'incomplete', 'uncertain']);

/** The statuses of R72's notice. */
export const AMENDED_AFTER_STATUSES = Object.freeze([
  'notice', 'no_later_amending_act', 'undetermined', 'no_amending_act_recorded', 'not_acquired',
]);

const DIGEST = /^[0-9a-f]{64}$/;
const SITE_WORK = /^\/lu-legilux\/[a-z0-9_-]+$/;

function own(holder, key, at) {
  if (holder === null || typeof holder !== 'object' || Array.isArray(holder) || !Object.hasOwn(holder, key)) {
    throw new Error(`${at} does not carry ${key}`);
  }
  return holder[key];
}

function text(holder, key, at) {
  const value = own(holder, key, at);
  if (typeof value !== 'string' || value.trim().length === 0) throw new Error(`${at}.${key} is not text`);
  return value;
}

function textOrNull(holder, key, at) {
  const value = own(holder, key, at);
  if (value !== null && (typeof value !== 'string' || value.trim().length === 0)) throw new Error(`${at}.${key} is neither text nor null`);
  return value;
}

function dateOrNull(holder, key, at) {
  const value = own(holder, key, at);
  if (value !== null && !isCalendarDate(value)) throw new Error(`${at}.${key} is neither a calendar date nor null`);
  return value;
}

function boolean(holder, key, at) {
  const value = own(holder, key, at);
  if (typeof value !== 'boolean') throw new Error(`${at}.${key} is true or false`);
  return value;
}

function count(holder, key, at) {
  const value = own(holder, key, at);
  if (!Number.isInteger(value) || value < 0) throw new Error(`${at}.${key} is not a count`);
  return value;
}

function list(holder, key, at) {
  const value = own(holder, key, at);
  if (!Array.isArray(value)) throw new Error(`${at}.${key} is a list, even an empty one`);
  return value;
}

function digest(holder, key, at) {
  const value = own(holder, key, at);
  if (typeof value !== 'string' || !DIGEST.test(value)) throw new Error(`${at}.${key} is not a SHA-256 digest`);
  return value;
}

/** The other act an edge names: its identifier, its work key and site address where it has one, its title or none, and its dates. */
function readOther(holder, at) {
  const other = own(holder, 'other', at);
  const where = `${at}.other`;
  const iri = text(other, 'iri', where);
  const workKey = textOrNull(other, 'work_key', where);
  const address = textOrNull(other, 'address', where);
  if (address !== null && (!SITE_WORK.test(address) || address !== `/lu-legilux/${workKey}`)) {
    throw new Error(`${where}.address ${address} is not this site's address of its work ${workKey}`);
  }
  const title = textOrNull(other, 'title', where);
  const titleLanguage = textOrNull(other, 'title_language', where);
  if ((title === null) !== (titleLanguage === null)) throw new Error(`${where} names a title language exactly when it names a title`);
  const titleWithheld = boolean(other, 'title_withheld', where);
  if (titleWithheld && title !== null) throw new Error(`${where} names a title it says is withheld`);
  return Object.freeze({
    iri, workKey, address, held: boolean(other, 'held', where), title, titleLanguage, titleWithheld,
    // The other act's own dates, each the one the publisher's facts state (R21: each its own value, never an interval).
    publicationDate: dateOrNull(other, 'publication_date', where),
    entryIntoForce: dateOrNull(other, 'entry_into_force', where),
  });
}

/** A catalogue state an answer names (a consolidated version): its date, its digest, and its permalink or coordinate. */
function readStateSummary(state, at, workKey) {
  if (state === null) return null;
  const applicabilityDate = own(state, 'applicability_date', at);
  if (!isCalendarDate(applicabilityDate)) throw new Error(`${at}.applicability_date is not a calendar date`);
  const stateSha256 = digest(state, 'state_sha256', at);
  const coordinate = text(state, 'stable_coordinate', at);
  if (workKey !== undefined && coordinate !== `/lu-legilux/${workKey}/${applicabilityDate}`) {
    throw new Error(`${at}'s stable coordinate ${coordinate} does not name its work and date`);
  }
  const language = textOrNull(state, 'language', at);
  const permalink = textOrNull(state, 'permalink', at);
  if ((language === null) !== (permalink === null)) throw new Error(`${at} has a permalink exactly when it names its language`);
  const key = coordinate.split('/')[2];
  if (permalink !== null && permalink !== luxembourgPermalink({ workKey: key, date: applicabilityDate, stateSha256, language })) {
    throw new Error(`${at}'s permalink ${permalink} does not pin its coordinate, digest and language`);
  }
  return Object.freeze({
    applicabilityDate,
    stateSha256,
    stableCoordinate: coordinate,
    permalink,
    publicationDate: Object.hasOwn(state, 'publication_date') ? dateOrNull(state, 'publication_date', at) : null,
  });
}

/**
 * The relation records an answer carries, or null when it carries none (an answer over an index of schema 9, 8 or 6).
 *
 * @param {object} answer the `result.value` of a Luxembourg dossier or relations answer
 * @returns {object|null} a frozen view `{families, groups, edgeCount, notSeen, rule, inverseLabel}`, `groups` one per view in
 *   `RELATION_VIEWS` order, each `{view, edgeType, direction, derived, family, edges}`
 */
export function readPublisherRelations(answer) {
  if (!Object.hasOwn(answer, 'publisher_relations')) return null;
  const value = answer.publisher_relations;
  const where = 'publisher_relations';
  const families = new Map(list(value, 'families', where).map((family, index) => {
    const at = `${where}.families[${index}]`;
    const edgeType = text(family, 'edge_type', at);
    if (!RELATION_EDGE_TYPES.includes(edgeType)) throw new Error(`${at} is of the edge type ${edgeType}, which is not one of ${RELATION_EDGE_TYPES.join(', ')}`);
    const state = text(family, 'state', at);
    if (!RELATION_FAMILY_STATES.includes(state)) throw new Error(`${at} is ${state}, not one of ${RELATION_FAMILY_STATES.join(', ')}`);
    const keys = list(family, 'family_keys', at);
    if ((state === 'acquired_complete') !== (keys.length > 0)) throw new Error(`${at} names the families that proved it exactly when it is acquired in full`);
    const reason = textOrNull(family, 'reason', at);
    if ((state === 'acquired_complete') !== (reason === null)) throw new Error(`${at} gives a reason exactly when it is not acquired in full`);
    const classes = own(family, 'subject_classes', at);
    if (classes !== null && (!Array.isArray(classes) || classes.some((entry) => typeof entry !== 'string' || !/^[a-z]+$/.test(entry)))) {
      throw new Error(`${at}.subject_classes is neither a list of classes nor null`);
    }
    return [edgeType, Object.freeze({ edgeType, state, acquired: state === 'acquired_complete', subjectClasses: classes === null ? null : Object.freeze([...classes]), reason })];
  }));
  for (const edgeType of RELATION_EDGE_TYPES) {
    if (!families.has(edgeType)) throw new Error(`${where} gives no acquisition state for ${edgeType}`);
  }
  if (families.size !== RELATION_EDGE_TYPES.length) throw new Error(`${where} names an edge type twice`);

  const views = new Map(RELATION_VIEWS.map((entry) => [entry.view, entry]));
  const edges = list(value, 'edges', where).map((edge, index) => {
    const at = `${where}.edges[${index}]`;
    const view = text(edge, 'view', at);
    const kind = views.get(view);
    if (kind === undefined) throw new Error(`${at} is of the view ${view}, which is not one this page lists`);
    if (text(edge, 'edge_type', at) !== kind.edgeType || text(edge, 'direction', at) !== kind.direction) {
      throw new Error(`${at} is a ${view} edge of another type or direction`);
    }
    const derived = boolean(edge, 'derived', at);
    // An edge read from this act's own record is the publisher's assertion on it; any other is derived (R29).
    if (derived !== (kind.direction === 'inbound' || kind.edgeType === 'impact')) {
      throw new Error(`${at} is ${derived ? '' : 'not '}derived, and a ${view} edge is ${derived ? 'not ' : ''}`);
    }
    const impact = own(edge, 'impact', at);
    if ((impact !== null) !== (kind.edgeType === 'impact')) throw new Error(`${at} carries an impact record exactly when it is one`);
    return Object.freeze({
      view,
      edgeType: kind.edgeType,
      derived,
      other: readOther(edge, at),
      isSelfReference: boolean(edge, 'is_self_reference', at),
      actingPublicationDate: dateOrNull(edge, 'acting_publication_date', at),
      actingEntryIntoForce: dateOrNull(edge, 'acting_entry_into_force', at),
      evidenceSha256: digest(edge, 'evidence_sha256', at),
      consolidatedBy: impact === null ? null : readStateSummary(own(impact, 'consolidated_by_state', `${at}.impact`), `${at}.impact.consolidated_by_state`),
      consolidatedByIri: impact === null ? null : textOrNull(impact, 'consolidated_by_iri', `${at}.impact`),
    });
  });
  const edgeCount = count(value, 'edge_count', where);
  if (edgeCount !== edges.length) throw new Error(`${where}.edge_count is ${edgeCount}, and it lists ${edges.length} edges`);
  const counts = own(value, 'edge_counts', where);
  const groups = Object.freeze(RELATION_VIEWS.map((entry) => {
    const ofView = Object.freeze(edges.filter((edge) => edge.view === entry.view));
    if (count(counts, entry.view, `${where}.edge_counts`) !== ofView.length) {
      throw new Error(`${where}.edge_counts.${entry.view} does not count the ${entry.view} edges it lists`);
    }
    return Object.freeze({
      view: entry.view,
      edgeType: entry.edgeType,
      direction: entry.direction,
      derived: entry.direction === 'inbound' || entry.edgeType === 'impact',
      family: families.get(entry.edgeType),
      edges: ofView,
    });
  }));
  return Object.freeze({
    families: Object.freeze([...families.values()]),
    groups,
    edgeCount,
    notSeen: text(value, 'not_seen', where),
    rule: text(value, 'rule', where),
    inverseLabel: text(value, 'inverse_label', where),
  });
}

/**
 * R72's reading of the act an answer carries, or null when it carries none (an answer over an index of schema 9, 8 or 6).
 *
 * @param {object} answer the `result.value` of a Luxembourg dossier or relations answer
 * @param {string} workKey the answer's work, which a consolidated version it names is of
 * @returns {object|null} a frozen view `{status, acts, latestConsolidation, latestConsolidationPublicationDate, notice, rule}`
 */
export function readAmendedAfter(answer, workKey) {
  if (!Object.hasOwn(answer, 'amended_after_consolidation')) return null;
  const value = answer.amended_after_consolidation;
  const where = 'amended_after_consolidation';
  const status = text(value, 'status', where);
  if (!AMENDED_AFTER_STATUSES.includes(status)) throw new Error(`${where}.status is ${status}, not one of ${AMENDED_AFTER_STATUSES.join(', ')}`);
  if (!boolean(value, 'derived', where)) throw new Error(`${where} is derived from the publisher's forward relation, and says it is not`);
  const latestDate = dateOrNull(value, 'latest_consolidation_publication_date', where);
  const acts = Object.freeze(list(value, 'acts', where).map((act, index) => {
    const at = `${where}.acts[${index}]`;
    const publicationDate = own(act, 'publication_date', at);
    if (!isCalendarDate(publicationDate)) throw new Error(`${at}.publication_date is not a calendar date`);
    if (latestDate !== null && publicationDate <= latestDate) {
      throw new Error(`${at} was published on ${publicationDate}, not after the latest consolidated version's ${latestDate}`);
    }
    return Object.freeze({
      actingIri: text(act, 'acting_iri', at),
      publicationDate,
      other: readOther(act, at),
      evidenceSha256: digest(act, 'evidence_sha256', at),
    });
  }));
  if ((status === 'notice') !== (acts.length > 0)) throw new Error(`${where} names the acts exactly when its notice is raised`);
  const notice = textOrNull(value, 'notice', where);
  if ((status === 'notice') !== (notice !== null)) throw new Error(`${where} says its sentence exactly when its notice is raised`);
  const latest = readStateSummary(own(value, 'latest_consolidation', where), `${where}.latest_consolidation`, workKey);
  if ((latest === null) !== (latestDate === null)) throw new Error(`${where} names its latest consolidated version exactly when it has its publication date`);
  return Object.freeze({
    status,
    acts,
    latestConsolidation: latest,
    latestConsolidationPublicationDate: latestDate,
    notice,
    rule: text(value, 'rule', where),
  });
}
