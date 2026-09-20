// Provenance, in the three shapes where the chain says more than it can show.
//
// One page per state, named after the state, because a provenance link that resolved to a page
// about some other state would be worse than the 404 it replaces. The preview links to exactly
// three, so this file holds exactly those three, and `PREVIEW_ANSWERS` is the list the harness
// route and the build both work from.
//
// Every answer here is a `provenance_chain` in the shape the V3 platform really sends. The SHAPE
// is held against the captured answer in `schemas/v3-platform/answer-samples.json` by
// `test/provenance.test.mjs`, so this file cannot drift into teaching a form no producer emits --
// which is exactly what the refusal catalogue did until a guard was written for it. The VALUES are
// synthetic on purpose and none of them is law: the banner says so, and a check that forced real
// coordinates onto this page would be a worse artifact than the drift it prevented.
//
// The three shapes: a state whose corpus holds every body fact; a state whose corpus holds the
// object but no body, so three fields are null and the page must say "not stated by the platform"
// rather than leave a blank cell that reads as a fact about the document; and a state whose source
// carries recorded gaps, which are the corpus's own tokens and are printed verbatim.

import { page } from './render.mjs';
import { provenancePageName, renderProvenance } from './provenance.mjs';
import { skinFor } from './shells.mjs';

const PUBLISHER = 'preview-synthetic';
const WORK = 'synthetic-preview-work';

const CORPUS = 'c0'.repeat(32);
const INDEX = 'd1'.repeat(32);
const REGISTRY = 'e2'.repeat(32);
const PROFILE = 'f3'.repeat(32);

const SCOPE =
  'the chain from the publisher’s identifiers to the digests this mount verified; it holds no '
  + 'first-sighting event and no signature, so none is claimed';

const DERIVATION =
  'state_sha256 is a SHA-256 over the domain tag lex-v3-luxembourg-expression-state/1 and then, '
  + 'each as UTF-8 preceded by its length as four bytes big-endian, the publisher, the work key, '
  + 'the applicability date, the expression, the publisher work IRI, the publisher legal-resource '
  + 'IRI, the language, each rule-profile digest in sorted order and each article identity in '
  + 'sorted order; the article identities are not carried here (they are article_identities in '
  + 'as_of’s answer to the same request, and article_identities_sha256 is the SHA-256 of them '
  + 'in sorted order, each preceded by its length in the same way, so a caller can check the list '
  + 'it holds); this mount’s own reader recomputes the state digest when the index is opened '
  + 'and refuses an index in which it does not match its row';

const SOURCES_NOTE =
  'object_ref_sha256 identifies the source object in the corpus; body_sha256 is the digest of the '
  + 'publisher bytes the corpus retained for it, body_byte_length their length and '
  + 'body_receipt_sha256 the digest of the corpus receipt for that body, each null where the '
  + 'corpus holds none; outcome, rights_disposition and gaps are the corpus manifest’s own '
  + 'tokens for the member, given verbatim, and this answer does not define them';

const NOT_HELD = Object.freeze([
  Object.freeze({
    item: 'first_sighting_event',
    reason: 'no observation time or first-sighting event is held, so nothing here says when the '
      + 'publisher’s bytes were first seen',
  }),
  Object.freeze({
    item: 'signature_stamp',
    reason: 'no signature or stamp is held or made: this states which digests this mount verified '
      + 'and signs nothing',
  }),
  Object.freeze({
    item: 'publisher_revision_history',
    reason: 'no record of corrections or withdrawals of the publisher’s document is held',
  }),
]);

function source({ ref, body, bytes, receipt, gaps = [], rights = null }) {
  return {
    object_ref_sha256: ref,
    body_sha256: body,
    body_byte_length: bytes,
    body_receipt_sha256: receipt,
    outcome: 'admitted',
    rights_disposition: rights,
    gaps,
  };
}

function answer({ date, digest, identities, articles, sources }) {
  return {
    scope: SCOPE,
    requested_identifier: `/${PUBLISHER}/${WORK}`,
    requested_date: date,
    requested_language: 'fra',
    publisher: PUBLISHER,
    work_key: WORK,
    states: [
      {
        language: 'fra',
        applicability_date: date,
        state_sha256: digest,
        permalink: `/${PUBLISHER}/${WORK}/${date}--${digest.slice(0, 8)}`,
        stable_coordinate: `/${PUBLISHER}/${WORK}/${date}`,
        expression_iri: `https://preview.invalid/${WORK}/${date}/fra`,
        publisher_work_iri: `https://preview.invalid/${WORK}`,
        publisher_legal_resource_iri: `https://preview.invalid/${WORK}/${date}`,
        rule_profile_sha256s: [PROFILE],
        articles,
        article_identities_sha256: identities,
        sources,
      },
    ],
    available_languages: ['fra'],
    verified_by: {
      corpus_sha256: CORPUS,
      index_sha256: INDEX,
      registry_sha256: REGISTRY,
    },
    derivation: DERIVATION,
    sources_note: SOURCES_NOTE,
    not_held: NOT_HELD.map((row) => ({ ...row })),
  };
}

const EVERY_BODY_FACT = {
  lexId: `${PUBLISHER}:${WORK}:2021-01-26`,
  heading: 'A state whose corpus holds every fact about its source',
  note:
    'What the chain looks like when nothing is missing: the object the corpus holds, the digest '
    + 'of the publisher bytes retained for it, their length, and the digest of the receipt for '
    + 'that body. The object reference and the body digest are different things, and the note '
    + 'below the sources says so, because naming one as the other is the overclaim this answer '
    + 'exists to avoid.',
  answer: answer({
    date: '2021-01-26',
    digest: 'a1'.repeat(32),
    identities: 'a2'.repeat(32),
    articles: 42,
    sources: [source({
      ref: '11'.repeat(32),
      body: '12'.repeat(32),
      bytes: 48219,
      receipt: '13'.repeat(32),
      rights: 'reproduction_permitted',
    })],
  }),
};

const NO_BODY_HELD = {
  lexId: `${PUBLISHER}:${WORK}:2001-01-01`,
  heading: 'A state whose corpus holds the object but not the bytes',
  note:
    'Three fields are null here, and the page says "not stated by the platform" in each. A blank '
    + 'cell would read as a fact about the publisher’s document; this is a fact about what '
    + 'this corpus retained. The distinction is the whole reason the sentence exists.',
  answer: answer({
    date: '2001-01-01',
    digest: 'b1'.repeat(32),
    identities: 'b2'.repeat(32),
    articles: 7,
    sources: [source({ ref: '21'.repeat(32), body: null, bytes: null, receipt: null })],
  }),
};

const GAPS_RECORDED = {
  lexId: `${PUBLISHER}:${WORK}:2030-09-15`,
  heading: 'A state whose source carries recorded gaps',
  note:
    'The gaps, the outcome and the rights disposition are the corpus manifest’s own tokens, '
    + 'printed verbatim. This page does not define them and does not translate them: a gloss '
    + 'invented here would be this service’s word wearing the corpus’s authority.',
  answer: answer({
    date: '2030-09-15',
    digest: 'c1'.repeat(32),
    identities: 'c2'.repeat(32),
    articles: 3,
    sources: [source({
      ref: '31'.repeat(32),
      body: '32'.repeat(32),
      bytes: 1204,
      receipt: '33'.repeat(32),
      gaps: ['a_recorded_gap', 'b_second_gap'],
    })],
  }),
};

export const PREVIEW_ANSWERS = Object.freeze([EVERY_BODY_FACT, NO_BODY_HELD, GAPS_RECORDED]);

function shell({ title, heading, note, body }) {
  return page({
    state: 'provenance',
    title,
    shell: 'dev',
    density: skinFor('dev').density,
    main:
      '      <p class="eyebrow">Developer</p>\n'
      + `      <h1>${heading}</h1>\n`
      + `      <p>${note}</p>\n`
      + '      <p>Every value on this page is synthetic and none of it is law.</p>\n'
      + `      ${body}\n`,
  });
}

/**
 * One page per state, returned as `[name, html]` pairs so the build can push them straight into
 * its page list and `pages.json` keeps naming exactly what was written.
 */
export function provenancePreviewPages() {
  return PREVIEW_ANSWERS.map((preview) => [
    provenancePageName(preview.lexId),
    shell({
      title: 'Provenance',
      heading: preview.heading,
      note: preview.note,
      body: renderProvenance(preview.answer),
    }),
  ]);
}
