// The provenance page: the chain behind one served state, for the reader who does not believe the
// answer.
//
// Every data view in this product ends with a Provenance link, and this is the destination. It is
// written against what the V3 platform's `provenance` operation really sends, captured in
// `schemas/v3-platform/answer-samples.json` by driving the real handler, and the tests render that
// captured answer rather than a shape anyone imagined.
//
// THIS PAGE USED TO REQUIRE A SIGNATURE AND PRINT IT AS VALID. It was written against a live V2
// tool whose payload carried `stamp.signature_valid`, `events`, `observations` and `document`, and
// it rendered "stamp signature valid: yes" beside a record digest. The V3 answer carries a fixed
// row saying the opposite, in the platform's own words: "no signature or stamp is held or made:
// this states which digests this mount verified and signs nothing". A page that printed a
// signature here would assert authenticity the platform explicitly refuses to claim, and the
// obvious repair -- give it a stamp -- is the worse outcome, because the only party available to
// mint one is us. So there is no stamp on this page, and `refuseStampShapes` makes feeding it the
// old payload an error rather than a page quietly missing its sections.
//
// The second rule is absence. This answer names what it does not hold, in rows with reasons, and
// every one is rendered. A row that disappears takes the reader's chance to notice it was ever
// expected -- the dossier's discipline, and the same reason.
//
// The third is the sources note. A source's body digest is not the corpus's reference to the
// object holding it, and where the corpus holds no body those fields are null. Null is rendered as
// "not stated by the platform" and never as blank, because a blank cell reads as a fact about the
// document rather than a fact about this corpus.

import { NOT_STATED, escapeHtml } from './render.mjs';

/** Why the rule profiles are on a provenance page at all. */
export const PROFILE_NOTE =
  'The rule profiles are the parsers that produced this state’s articles. Two states made by '
  + 'different profiles mint different article identities, so a difference between them would '
  + 'report parser disagreement as legislation.';

/**
 * What the reader is told where the platform sends null rather than a value.
 *
 * Re-exported rather than defined, because the coverage page needs the same sentence and two copies
 * of one sentence is how two pages come to word the same absence differently. This is an alias for
 * the one in `render.mjs` and holds no value of its own.
 */
export { NOT_STATED };

/** What a language-narrowed answer is, said before a reader reads it as the whole record. */
export function narrowedNote(language) {
  return (
    `This answer was narrowed to ${language} when it was requested. The states below are that `
    + 'language’s, and every language this work is held in is listed above them.'
  );
}

/**
 * The file name the built page for one record takes.
 *
 * A `lex_id` separates its parts with colons, which Windows will not put in a file name, and
 * percent-escaping is not the way out: the static harness decodes a request path before looking on
 * disk, so a name carrying `%3A` is asked for under a name that cannot exist. So the one character
 * in the way is mapped to one that is legal in both a file name and a URL, and `~` is chosen
 * because it cannot occur in a `lex_id`, which keeps the mapping reversible and two records from
 * ever sharing a page. An identifier this cannot name is refused rather than squeezed into a name
 * that might already belong to another record.
 */
const NAMEABLE = /^[A-Za-z0-9._:-]+$/;

export function provenancePageName(lexId) {
  if (typeof lexId !== 'string' || !NAMEABLE.test(lexId)) {
    throw new Error(
      `a provenance page is named after the record it describes, and ${JSON.stringify(lexId)} `
        + 'cannot be named without colliding with some other record',
    );
  }
  return `provenance-${lexId.replaceAll(':', '~')}.html`;
}

const DIGEST = /^[0-9a-f]{64}$/;

function requireOwn(object, key, where) {
  if (!Object.hasOwn(object ?? {}, key)) {
    throw new Error(
      `${where} does not carry ${key}; an absent member and a member with nothing in it are `
        + 'different facts, and only one of them can be reported',
    );
  }
  return object[key];
}

function requireText(value, where) {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new Error(`${where} is not a value this page can print`);
  }
  return value;
}

function requireDigest(value, where) {
  if (typeof value !== 'string' || !DIGEST.test(value)) {
    throw new Error(`${where} is not a SHA-256 digest: ${JSON.stringify(value)}`);
  }
  return value;
}

/**
 * A digest the platform may or may not hold. Null is a fact and is rendered as one; anything else
 * that is not a digest is a producer error and is refused rather than printed.
 */
function optionalDigest(value, where) {
  if (value === null) return null;
  return requireDigest(value, where);
}

/**
 * The old payload's members, refused by name.
 *
 * Without this, feeding the V2 shape to this page produces a page missing its sections rather than
 * an error, and a reader cannot tell a platform that holds no signature from a page that forgot to
 * print one. The four named here are the ones the old page REQUIRED, so their presence means the
 * caller is on the old contract and needs to be told, not handed a degraded page.
 */
function refuseStampShapes(answer) {
  for (const member of ['stamp', 'events', 'observations', 'document']) {
    if (Object.hasOwn(answer ?? {}, member)) {
      throw new Error(
        `this provenance answer carries ${member}, which belongs to the tool this page was `
          + 'written against before V3. This page renders a provenance_chain, which holds no '
          + 'signature and says so; rendering the old shape here would print a claim the platform '
          + 'refuses to make',
      );
    }
  }
}

function readSource(source, where) {
  const gaps = requireOwn(source, 'gaps', where);
  if (!Array.isArray(gaps)) throw new Error(`${where}.gaps is a list, even an empty one`);
  const byteLength = requireOwn(source, 'body_byte_length', where);
  if (byteLength !== null && !(Number.isInteger(byteLength) && byteLength >= 0)) {
    throw new Error(`${where}.body_byte_length is a whole count of bytes, or null`);
  }
  const rights = requireOwn(source, 'rights_disposition', where);
  if (rights !== null && (typeof rights !== 'string' || rights.trim().length === 0)) {
    throw new Error(`${where}.rights_disposition is the corpus's token, or null`);
  }
  return {
    object_ref_sha256: requireDigest(source.object_ref_sha256, `${where}.object_ref_sha256`),
    body_sha256: optionalDigest(requireOwn(source, 'body_sha256', where), `${where}.body_sha256`),
    body_byte_length: byteLength,
    body_receipt_sha256: optionalDigest(
      requireOwn(source, 'body_receipt_sha256', where), `${where}.body_receipt_sha256`),
    outcome: requireText(source.outcome, `${where}.outcome`),
    rights_disposition: rights,
    gaps: gaps.map((gap, index) => requireText(gap, `${where}.gaps[${index}]`)),
  };
}

function readState(state, where) {
  const profiles = requireOwn(state, 'rule_profile_sha256s', where);
  if (!Array.isArray(profiles) || profiles.length === 0) {
    throw new Error(`${where}.rule_profile_sha256s is how the articles were produced; it is never empty`);
  }
  const sources = requireOwn(state, 'sources', where);
  if (!Array.isArray(sources) || sources.length === 0) {
    throw new Error(
      `${where}.sources are the documents this state's articles came from; a state with none `
        + 'would be a state with no provenance, which is the one thing this page cannot show',
    );
  }
  const articles = requireOwn(state, 'articles', where);
  if (!Number.isInteger(articles) || articles < 0) {
    throw new Error(`${where}.articles is a whole count of the state's articles`);
  }

  return {
    language: requireText(state.language, `${where}.language`),
    applicability_date: requireText(state.applicability_date, `${where}.applicability_date`),
    state_sha256: requireDigest(state.state_sha256, `${where}.state_sha256`),
    permalink: requireText(state.permalink, `${where}.permalink`),
    stable_coordinate: requireText(state.stable_coordinate, `${where}.stable_coordinate`),
    expression_iri: requireText(state.expression_iri, `${where}.expression_iri`),
    publisher_work_iri: requireText(state.publisher_work_iri, `${where}.publisher_work_iri`),
    publisher_legal_resource_iri: requireText(
      state.publisher_legal_resource_iri, `${where}.publisher_legal_resource_iri`),
    rule_profile_sha256s: profiles.map(
      (profile, index) => requireDigest(profile, `${where}.rule_profile_sha256s[${index}]`)),
    articles,
    article_identities_sha256: requireDigest(
      state.article_identities_sha256, `${where}.article_identities_sha256`),
    sources: sources.map((source, index) => readSource(source, `${where}.sources[${index}]`)),
  };
}

/**
 * Validates one `provenance_chain` and returns what the renderers lay out. Every rule lives here
 * and is applied once, so the React port can share them rather than reimplement them beside a copy
 * that drifts -- the defect the dossier's two renderers carried until changing one rule found it.
 */
export function readProvenance(answer) {
  refuseStampShapes(answer);

  const where = 'this provenance answer';
  const requestedLanguage = requireOwn(answer, 'requested_language', where);
  if (requestedLanguage !== null && typeof requestedLanguage !== 'string') {
    throw new Error('requested_language is the language asked for, or null when none was asked');
  }

  const availableLanguages = requireOwn(answer, 'available_languages', where);
  if (!Array.isArray(availableLanguages) || availableLanguages.length === 0) {
    throw new Error(
      'available_languages says which languages this work is held in; an empty list would read as '
        + 'a work held in none',
    );
  }

  const verified = requireOwn(answer, 'verified_by', where);

  // Every row with its reason. This is the page's whole account of what it cannot tell a reader,
  // so a row without a reason is worse than no row: it names a gap and explains nothing.
  const notHeld = requireOwn(answer, 'not_held', where);
  if (!Array.isArray(notHeld) || notHeld.length === 0) {
    throw new Error(
      'not_held is this answer’s account of what it does not hold; an empty list would read as an '
        + 'answer that holds everything',
    );
  }

  const states = requireOwn(answer, 'states', where);
  if (!Array.isArray(states) || states.length === 0) {
    throw new Error('a provenance answer describes at least one state');
  }

  return {
    scope: requireText(requireOwn(answer, 'scope', where), 'scope'),
    derivation: requireText(requireOwn(answer, 'derivation', where), 'derivation'),
    sourcesNote: requireText(requireOwn(answer, 'sources_note', where), 'sources_note'),
    publisher: requireText(requireOwn(answer, 'publisher', where), 'publisher'),
    workKey: requireText(requireOwn(answer, 'work_key', where), 'work_key'),
    requestedIdentifier: requireText(
      requireOwn(answer, 'requested_identifier', where), 'requested_identifier'),
    requestedDate: requireText(requireOwn(answer, 'requested_date', where), 'requested_date'),
    requestedLanguage,
    availableLanguages: availableLanguages.map(
      (language, index) => requireText(language, `available_languages[${index}]`)),
    verifiedBy: {
      corpus_sha256: requireDigest(verified?.corpus_sha256, 'verified_by.corpus_sha256'),
      index_sha256: requireDigest(verified?.index_sha256, 'verified_by.index_sha256'),
      registry_sha256: requireDigest(verified?.registry_sha256, 'verified_by.registry_sha256'),
    },
    notHeld: notHeld.map((row, index) => ({
      item: requireText(row?.item, `not_held[${index}].item`),
      reason: requireText(row?.reason, `not_held[${index}].reason`),
    })),
    states: states.map((state, index) => readState(state, `states[${index}]`)),
  };
}

/** A value the platform holds, or the sentence saying it does not. Never a blank cell. */
function cell(value, render) {
  return value === null
    ? `<span class="provenance-not-stated">${escapeHtml(NOT_STATED)}</span>`
    : render(value);
}

const code = (value) => `<code>${escapeHtml(String(value))}</code>`;

function row(label, value) {
  return `<tr><th scope="row">${escapeHtml(label)}</th><td>${value}</td></tr>`;
}

function renderSource(source) {
  return (
    '<li class="provenance-source">'
    + '<table class="provenance-facts"><tbody>'
    + row('object reference', code(source.object_ref_sha256))
    + row('body digest', cell(source.body_sha256, code))
    + row('body bytes', cell(source.body_byte_length, (bytes) => escapeHtml(String(bytes))))
    + row('body receipt', cell(source.body_receipt_sha256, code))
    + row('outcome', code(source.outcome))
    + row('rights disposition', cell(source.rights_disposition, code))
    + row('gaps recorded', source.gaps.length === 0
      ? 'none recorded'
      : source.gaps.map(code).join(' '))
    + '</tbody></table></li>'
  );
}

function renderState(state) {
  return (
    '<section class="provenance-state">'
    + `<h3>${escapeHtml(state.language)}, applicable from ${escapeHtml(state.applicability_date)}</h3>`
    + '<table class="provenance-facts"><tbody>'
    + row('state digest', code(state.state_sha256))
    + row('permalink', code(state.permalink))
    + row('stable coordinate', code(state.stable_coordinate))
    + row('expression', code(state.expression_iri))
    + row('publisher work', code(state.publisher_work_iri))
    + row('publisher legal resource', code(state.publisher_legal_resource_iri))
    + row('articles', escapeHtml(String(state.articles)))
    + row('article identities digest', code(state.article_identities_sha256))
    + row('rule profiles', state.rule_profile_sha256s.map(code).join(' '))
    + '</tbody></table>'
    + `<p class="provenance-profile-note">${escapeHtml(PROFILE_NOTE)}</p>`
    + '<h4>The documents these articles came from</h4>'
    + `<ul class="provenance-sources">${state.sources.map(renderSource).join('')}</ul>`
    + '</section>'
  );
}

/** The page. Every rule is in readProvenance; this decides only how the result looks. */
export function renderProvenance(answer) {
  const view = readProvenance(answer);
  return (
    '<section class="provenance">'
    + '<section class="provenance-block"><h2>What this page is about</h2>'
    + `<p class="provenance-scope">${escapeHtml(view.scope)}</p>`
    + '<table class="provenance-facts"><tbody>'
    + row('publisher', code(view.publisher))
    + row('work', code(view.workKey))
    + row('identifier asked for', code(view.requestedIdentifier))
    + row('date asked for', escapeHtml(view.requestedDate))
    + row('languages held', view.availableLanguages.map(code).join(' '))
    + '</tbody></table>'
    + (view.requestedLanguage === null
      ? ''
      : `<p class="provenance-narrowed">${escapeHtml(narrowedNote(view.requestedLanguage))}</p>`)
    + '</section>'
    + `<section class="provenance-block"><h2>The states this answer describes</h2>${
      view.states.map(renderState).join('')}</section>`
    + '<section class="provenance-block"><h2>What the corpus recorded for each document</h2>'
    + `<p class="provenance-sources-note">${escapeHtml(view.sourcesNote)}</p></section>`
    + '<section class="provenance-block"><h2>How the state digest is derived</h2>'
    + `<p class="provenance-derivation">${escapeHtml(view.derivation)}</p></section>`
    + '<section class="provenance-block"><h2>What verified this answer</h2>'
    + '<table class="provenance-facts"><tbody>'
    + row('corpus', code(view.verifiedBy.corpus_sha256))
    + row('index', code(view.verifiedBy.index_sha256))
    + row('operation registry', code(view.verifiedBy.registry_sha256))
    + '</tbody></table></section>'
    + '<section class="provenance-block"><h2>What this answer does not hold</h2>'
    + `<ul class="provenance-not-held">${view.notHeld.map((held) =>
      `<li>${code(held.item)}: ${escapeHtml(held.reason)}</li>`).join('')}</ul>`
    + '</section>'
    + '</section>'
  );
}
