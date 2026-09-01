// The provision reading view: one work's text as it stood on one date.
//
// This screen is a composition and nothing here re-implements a component it composes. The
// two clocks, the three qualifications, the quotation, the typed refusals, the verification
// cluster and the permalink all exist once, elsewhere, and are called. A second copy of a
// rule is a second place for it to be wrong, and the copy that drifts is always the one
// nobody tested.
//
// Eight rules live here. Each one is a sentence this page would otherwise say that is not
// true, and each is a refusal at construction time rather than a warning on the page,
// because a warning about a page of statute is read by nobody who is reading statute.
//
//  1. Publisher text is quoted with the expression's own language. The chrome locale is the
//     language of the interface, not of the law; binding the quotation to it would put
//     `lang="en"` on 38,000 words of French and make a screen reader read French aloud in an
//     English voice. So the language comes off the expression record, the caller cannot
//     supply one, and the chrome locale reaches `quotedLaw` only as the locale of the
//     authenticity note.
//
//  2. The state's own two clocks are shown, and the words "in force" never appear. The EU
//     corpus carries `binding_status: in_force` on wording states that predate entry into
//     force, so a reading view that echoed that flag onto the state it is showing would
//     assert something the publisher never said about that date. The flag belongs in the
//     dossier status strip, with the caption that makes it readable, and a state row
//     carrying it is refused here. No chrome string in this file contains the phrase; the
//     only way it could arrive is through a state key, which is why the key set is closed.
//
//  3. Every provision carries the hash-carrying permalink, built by `readingUrl` from the
//     state hash and the publisher's own anchor. A hand-built string is refused rather than
//     preferred, because a permalink is the one thing on this page whose whole value is that
//     it was checked: a link that carries the hash cannot drift onto different text when the
//     publisher replaces a file, and a link somebody assembled by concatenation has not been
//     checked against the state it names.
//
//  4. A provision whose own wording date differs from its state's renders both. Measured
//     across the Luxembourg corpus this is 46,163 of 116,048 provision states, 39.8 percent,
//     so it is the normal case and not an exotic one. Both dates are the publisher's,
//     neither is derived, and this product does not decide which controlled. The wording
//     date is therefore required rather than defaulted: defaulting it to the state's date is
//     precisely the silent reconciliation the rule forbids, performed by an omission.
//
//  5. Text withheld by licence renders as its digest plus the official link. Not as absent
//     text, which reads as a provision with nothing in it, and not as invented text. The
//     digest names the exact bytes this corpus holds and says when it observed them, because
//     a digest with no observation time is a number about nothing.
//
//  6. An anchor the version does not contain is a typed refusal that offers the anchors it
//     does contain. Never an empty page: an empty page is indistinguishable from a provision
//     that says nothing, and the contract rule is that this product does not fall back to
//     full-text search for a provision of a known work.
//
//  7. A consolidation carries the no-legal-effect sentence wherever its text renders, which
//     means beside every block of text and not once at the top of the page. Whether this is
//     a consolidation is answered by `consolidation_status` on the record: the publisher's
//     own original official expression is not a consolidation, and printing the sentence
//     against it would be false in the other direction.
//
//  8. Nothing is rendered from a caller flag where a record can answer instead. Whether a
//     state is provisional is the state's date against the reader's date; whether the
//     sentence in rule 7 applies is `consolidation_status`; whether a wording conflicts is
//     two dates; where the authentic file lives is the authenticity evidence. Each of those
//     arrived once as a boolean somebody passed, and a boolean somebody passed is a fact
//     about the caller.
//
//  9. The work this page is about, and the vocabulary its dates are in, are read off
//     `state.lex_id` rather than accepted beside it. A `lex_id` is `publisher:work:state`, so
//     a page holding the state is already holding both. Taken separately they can disagree
//     with it, and neither disagreement is visible: permalinks mint for one work while
//     Provenance addresses another, and Luxembourg applicability dates get the Union's word
//     for a consolidated wording state. A caller may still state them, and is then refused
//     when it states something the record contradicts.

import { quotedLaw, renderUnofficialRendering } from './localization.mjs';
import { semanticsOf } from './publisher-vocabulary.mjs';
import { identityOf } from './record-identity.mjs';
import { escapeHtml } from './render.mjs';
import { renderRefusalCard, renderSupersededState } from './refusal-card.mjs';
import { renderStateBanner } from './state-banner.mjs';
import { renderHole, renderProvisional, renderValidityConflict } from './state-qualifiers.mjs';
import { requireCalendarDate, requireUtcInstant } from './temporal.mjs';
import { readingUrl } from './urls.mjs';
import { renderVerifyCluster } from './verify-cluster.mjs';

/**
 * The sentence a consolidation cannot render text without.
 *
 * Fixed, because it is the sentence that makes the text on this page readable as what it is.
 * Both publishers say it themselves about their own consolidations, and a reader who quotes
 * this page into a filing without it has quoted documentation as law.
 */
export const NO_LEGAL_EFFECT =
  'A consolidation is documentation and has no legal effect. The authentic text is the '
  + 'publisher file linked beside it.';

/** What a licence-withheld provision says instead of nothing, and instead of a guess. */
export const WITHHELD_NOTE =
  'The publisher licence for this file does not permit republishing its wording here. The '
  + 'digest identifies the exact bytes this corpus holds, and the official link leads to them.';

/** What a provision with no held text says. The absence is this corpus, not the law. */
export const NOT_AVAILABLE_NOTE =
  'This corpus holds no text for this provision in this state. The official file and the '
  + 'gazette chain are below.';

/** What a version says when asked for a provision it does not contain. */
export const ANCHOR_NOT_IN_VERSION_NOTE =
  'This version does not contain that provision. These are the provisions it does contain.';

/**
 * Whether the publisher calls this a consolidation or its own original expression.
 *
 * Closed, and the reason rule 7 can be answered by the record rather than by a caller. The
 * two live values mean different things about the same page: one is the publisher
 * consolidating its own law for convenience, the other is the expression the publisher
 * originally published, and only the first has no legal effect.
 */
export const CONSOLIDATION_STATUSES = Object.freeze([
  'published',
  'original_official_expression',
]);

/**
 * What the record says about a provision's text. Closed, because "no text on the page" has
 * three causes with three different answers, and a page that cannot tell them apart tells a
 * reader the provision is empty.
 */
export const TEXT_STATUSES = Object.freeze(['held', 'withheld', 'not_available']);

/**
 * Keys a state row may not carry into this screen.
 *
 * `binding_status` is the publisher's flag about now. Printed against a historical interval
 * it dates a claim the publisher never made about that date, and the held GDPR state
 * applicable from 2016-04-27 carries `in_force` while the regulation did not apply until
 * 2018-05-25. It belongs in the dossier status strip with its caption and nowhere else, so
 * this screen refuses it rather than choosing not to print it: a key that is accepted and
 * ignored is a key the next renderer will print.
 */
export const FORBIDDEN_STATE_KEYS = Object.freeze([
  'binding_status',
  'in_force',
  'force_status',
]);

/**
 * Facts a caller may not assert, because a record answers them.
 *
 * Each of these was a flag on some interface somewhere, and a flag is a fact about whoever
 * set it. `provisional` is the state's date against the reader's date. `language` is the
 * expression's. `consolidation` and `no_legal_effect` are `consolidation_status`.
 * `text_available` is `text_status`, which distinguishes three cases this one collapses.
 */
export const CALLER_DECIDED_KEYS = Object.freeze([
  'language',
  'provisional',
  'consolidation',
  'no_legal_effect',
  'text_available',
]);

/**
 * Keys that would present the validity conflict as settled. There is no settled value: both
 * dates are the publisher's and the publisher did not rank them, so a third date derived
 * from them is this product's assertion wearing the publisher's authority.
 */
export const RECONCILED_KEYS = Object.freeze([
  'effective_valid_from',
  'resolved_valid_from',
  'conflict_resolved',
]);

/**
 * The contract routes out of the two absences this screen can render.
 *
 * Written here rather than taken from the caller, for the reason the refusal card writes its
 * own mandated notes: a caller who has to remember to pass a contract constant is a caller
 * who will eventually not, and the missing one would be missing from the refusal that needed
 * it most.
 */
const ANCHOR_ROUTES = Object.freeze(['corrected_identifier']);
const TEXT_ROUTES = Object.freeze(['new_official_observation']);

/** The badge on a provision whose wording date agrees with its state. */
export function unchangedSince(wordingValidFrom) {
  return `Wording unchanged since ${wordingValidFrom}.`;
}

function ownKeys(object, keys) {
  return keys.filter((key) => Object.hasOwn(object ?? {}, key));
}

/**
 * The no-legal-effect sentence, attached to a block of text rather than to the page.
 *
 * The sentence used to sit once under the state banner, where a reader scrolling to the
 * fourth article never saw it, and where copying one article carried the text away from the
 * only thing that qualified it.
 */
function withAuthenticityFooting(html, consolidated) {
  if (!consolidated) return html;
  return `${html}<p class="reading-no-legal-effect">${escapeHtml(NO_LEGAL_EFFECT)}</p>`;
}

/**
 * Everything one provision will show, checked.
 *
 * The rules live here and the markup lives in the renderers, so the string renderer and the
 * React component apply one implementation rather than two that can drift apart. What comes
 * back is the checked input for the components this screen composes, never their markup. The
 * quotation, the two refusals, the unofficial renderings and the verification cluster each
 * hold their own rules and are called by both renderers.
 *
 * @param {object} provision  one provision as the record carries it
 * @param {number} index      its position, so a refusal can name which one
 * @param {object} context    the state, the expression and the work it belongs to
 */
export function validateProvision(provision, index, context) {
  const where = `provision ${index + 1}`;
  const { state, expression, publisher, work, consolidated, noteLocale } = context;

  if (typeof provision?.anchor !== 'string' || provision.anchor.length === 0) {
    throw new Error(
      `${where} carries no publisher anchor; without one its permalink addresses the whole `
        + 'version, and a link that says article and resolves to a document is a link nobody '
        + 'can check',
    );
  }
  if (typeof provision.num !== 'string' || provision.num.trim().length === 0) {
    throw new Error(
      `${where} carries no publisher number; an unnumbered provision cannot be cited, and a `
        + 'reader cannot tell which article they are looking at',
    );
  }

  // The permalink is built, never accepted. A caller-built string has not been checked
  // against the state it names, and the whole value of this link is that it was.
  const handBuilt = ownKeys(provision, ['permalink', 'href', 'url']);
  if (handBuilt.length > 0) {
    throw new Error(
      `${where} arrives with ${handBuilt.join(', ')} already built; a permalink is built here `
        + 'from the state hash and the publisher anchor, because a hand-built one carries no '
        + 'evidence that it addresses the state it claims to',
    );
  }

  const reconciled = ownKeys(provision, RECONCILED_KEYS);
  if (reconciled.length > 0) {
    throw new Error(
      `${where} arrives with ${reconciled.join(', ')}; the publisher recorded two dates and `
        + 'ranked neither, so a third date derived from them is this product resolving a '
        + 'conflict it is required to show',
    );
  }

  // Required, not defaulted. A missing wording date defaulted to the state's date renders
  // the unchanged badge over a conflict, which is the silent reconciliation performed by an
  // omission rather than by a decision.
  requireCalendarDate(provision.wording_valid_from, `${where} wording_valid_from`);

  const conflicted = provision.wording_valid_from !== state.valid_from;

  // A summarising flag may travel with the record, and it is checked against the dates it
  // summarises rather than rendered from. Two publisher dates and a flag that disagrees with
  // them is a record this screen cannot present honestly either way.
  if (
    Object.hasOwn(provision, 'validity_conflict')
    && provision.validity_conflict !== conflicted
  ) {
    throw new Error(
      `${where} carries validity_conflict ${JSON.stringify(provision.validity_conflict)} while `
        + `its wording date ${provision.wording_valid_from} and its state date `
        + `${state.valid_from} say ${conflicted}; a flag that disagrees with the dates it `
        + 'summarises is a fact about whoever set it',
    );
  }

  if (!TEXT_STATUSES.includes(provision.text_status)) {
    throw new Error(
      `${where} carries text_status ${JSON.stringify(provision.text_status)}; the set is closed `
        + `at ${TEXT_STATUSES.join(', ')}, and an empty text pane whose cause nobody named `
        + 'reads as a provision that says nothing',
    );
  }

  // Exactly one of these is filled, and which one is the record's answer to "why is there no
  // text here". Three causes with three different answers, which is why `text_status` is a
  // closed set rather than a boolean.
  let quotation = null;
  let refusal = null;
  let verify = null;

  if (provision.text_status === 'held') {
    // The expression's own language, from the expression's own record. `noteLocale` is the
    // chrome locale and reaches only the authenticity note.
    quotation = {
      resourceId: expression.resource_id,
      authenticity: expression.authenticity,
      language: expression.language,
      text: provision.text,
      noteLocale,
    };
    verify = {
      publisher,
      sourceUri: state.source_uri,
      lexId: state.lex_id,
      hash: { kind: 'text_sha256', value: provision.text_sha256 },
    };
  } else if (provision.text_status === 'withheld') {
    if (Object.hasOwn(provision, 'text')) {
      throw new Error(
        `${where} is withheld by licence and arrives carrying its text; the licence is not a `
          + 'display preference, and text that may not be republished may not be on the page',
      );
    }
    if (typeof provision.licence !== 'string' || provision.licence.trim().length === 0) {
      throw new Error(
        `${where} is withheld and does not name the licence withholding it; "we cannot show `
          + 'this" without the reason is indistinguishable from "we do not have this"',
      );
    }
    // A digest is evidence about bytes at a time. Without the time it is a number.
    requireUtcInstant(provision.digest_observed_at, `${where} digest_observed_at`);

    refusal = {
      code: 'text_withheld',
      sentence: WITHHELD_NOTE,
      payload: {
        licence: provision.licence,
        digest_observed_at: provision.digest_observed_at,
      },
    };
    // The digest and the official link, through the same cluster every other screen uses, so
    // the link is checked against the publisher's own host set rather than merely escaped.
    verify = {
      publisher,
      sourceUri: provision.official_uri,
      lexId: state.lex_id,
      hash: { kind: 'text_sha256', value: provision.text_sha256 },
    };
  } else {
    refusal = {
      code: 'text_not_available',
      sentence: NOT_AVAILABLE_NOTE,
      payload: {
        official_uri: provision.official_uri,
        gazette_chain: provision.gazette_chain,
        what_would_answer: TEXT_ROUTES,
        asserts_absence_of_law: false,
      },
    };
  }

  const renderings = (provision.renderings ?? []).map((rendering, position) => {
    // The publisher and the official address belong to the authenticity evidence. A
    // rendering that brought its own would put a link nothing bound to the claim under
    // the words that name the authentic text.
    const supplied = ownKeys(rendering, ['publisher', 'official_uri', 'officialUri']);
    if (supplied.length > 0) {
      throw new Error(
        `${where} rendering ${position + 1} supplies ${supplied.join(', ')}; the authentic `
          + 'route is the resource evidence, and a route the caller chose is a route nobody '
          + 'checked against the claim above it',
      );
    }
    return {
      resourceId: expression.resource_id,
      authenticity: expression.authenticity,
      language: rendering?.language,
      text: rendering?.text,
      publisher: expression.authenticity.publisher,
      officialUri: expression.authenticity.official_uri,
    };
  });

  return Object.freeze({
    anchor: provision.anchor,
    num: provision.num,
    conflicted,
    stateValidFrom: state.valid_from,
    wordingValidFrom: provision.wording_valid_from,
    consolidated,
    quotation,
    refusal,
    verify,
    renderings: Object.freeze(renderings),
    permalink: readingUrl({
      publisher,
      work,
      validFrom: state.valid_from,
      hash: state.hash,
      anchor: provision.anchor,
    }),
  });
}

/**
 * The work this page is about and the vocabulary its dates are in, both read off the record.
 *
 * See rule 9 at the top of this file. A caller may still state either, and is refused when it
 * states something `state.lex_id` contradicts. Refused rather than corrected: a page told the
 * wrong work has a caller that believes something false, and quietly rendering the right one
 * leaves that belief in place.
 */
function readingIdentity({ envelope, work, state }) {
  const identity = identityOf(state?.lex_id, 'the reading view');

  if (work !== undefined && work !== null
    && (work.publisher !== identity.publisher || work.work !== identity.work)) {
    throw new Error(
      `this page was told it is about ${work.publisher}/${work.work} while its state names `
        + `${identity.workKey}; the work is written on the record, and a second one beside it `
        + 'mints permalinks for a work this page is not showing',
    );
  }

  const semantics = semanticsOf(identity.publisher, 'the reading view');
  const stated = envelope?.timeline_semantics;
  if (stated !== undefined && stated !== semantics) {
    throw new Error(
      `this page was told its dates are ${JSON.stringify(stated)} while ${identity.publisher} `
        + `publishes ${semantics}; which clock a publisher's dates are on is a property of the `
        + "publisher, so a vocabulary chosen beside the record labels one publisher's dates "
        + "with another publisher's words",
    );
  }

  return { identity, semantics };
}

/**
 * Everything one work's text as it stood on one date will show, checked.
 *
 * The rules live here and the markup lives in the renderers, for the reason
 * `validateProvision` gives. The header is settled before the anchor is resolved, so the
 * refusal below is a page about a resolved version rather than a page about nothing: a
 * refusal that dropped the two clocks would leave a reader unable to say which version did
 * not contain their anchor.
 *
 * @param {object} input
 * @param {object} [input.envelope]  may state `timeline_semantics`, and must then agree
 * @param {object} [input.work]      may state the two URL segments, and must then agree
 * @param {object} input.state       the state being read, with both clocks and its digests
 * @param {object} input.expression  the language expression, its own language, its evidence
 * @param {Array}  input.provisions  the provisions this version holds, in publisher order
 * @param {Array}  input.holes       periods around this state no held state covers
 * @param {string} input.asOf        the reader's date, always stated, never defaulted
 * @param {string} [input.anchor]    the provision asked for, if one was
 * @param {object} [input.superseded] the live state, when this one was withdrawn
 * @param {string} [input.noteLocale] the chrome locale, for the authenticity note only
 */
export function validateReading(input) {
  const {
    envelope,
    work,
    state,
    expression,
    provisions,
    holes,
    asOf,
    anchor = null,
    superseded = null,
    noteLocale = 'en',
  } = input ?? {};

  const decided = ownKeys(input, CALLER_DECIDED_KEYS);
  if (decided.length > 0) {
    throw new Error(
      `the reading view was handed ${decided.join(', ')}; every one of those is answered by a `
        + 'record on this page, and a fact taken from the caller is a fact about the caller',
    );
  }

  // Never today by default. The operative date is on the page even when it is today, so a
  // reader can see which date produced what they are reading.
  requireCalendarDate(asOf, 'the reading as-of date');

  const forbidden = ownKeys(state, FORBIDDEN_STATE_KEYS);
  if (forbidden.length > 0) {
    throw new Error(
      `this state row carries ${forbidden.join(', ')}; the publisher status flag is a statement `
        + 'about now and this row is a historical interval, so the flag belongs in the dossier '
        + 'status strip with its caption and nowhere else',
    );
  }

  const { identity, semantics } = readingIdentity({ envelope, work, state });

  if (!CONSOLIDATION_STATUSES.includes(state?.consolidation_status)) {
    throw new Error(
      `consolidation_status ${JSON.stringify(state?.consolidation_status)} is not one of `
        + `${CONSOLIDATION_STATUSES.join(', ')}; whether the text on this page has legal effect `
        + 'is the publisher answering, and an unknown value cannot be read either way',
    );
  }
  const consolidated = state.consolidation_status === 'published';

  if (typeof state?.withdrawn !== 'boolean') {
    throw new Error(
      'this state does not say whether the publisher withdrew it; absent that, a withdrawn '
        + 'state renders exactly like a live one and a reader who followed an old link is '
        + 'never told',
    );
  }
  if (state.withdrawn && superseded === null) {
    throw new Error(
      'this state was withdrawn and no live state is disclosed; a reader who arrived here has '
        + 'to be shown the state that replaced it, because a withdrawn state read as current '
        + 'is the oldest failure in this product',
    );
  }

  if (typeof expression?.language !== 'string' || !/^[a-z]{2}$/.test(expression.language)) {
    throw new Error(
      `the expression does not carry its own language: ${JSON.stringify(expression?.language)}; `
        + 'the chrome language is the language of this interface, and quoting statute in it '
        + 'makes a screen reader read one language in the voice of another',
    );
  }

  if (!Array.isArray(provisions) || provisions.length === 0) {
    throw new Error(
      'this version holds no provisions, so there is nothing to read; an empty text pane is '
        + 'indistinguishable from a version whose provisions say nothing',
    );
  }

  if (!Array.isArray(holes)) {
    throw new Error(
      'the reading view declares the gaps around this state, even as an empty list; a page '
        + 'that is silent about gaps reads as a page with none',
    );
  }

  const anchors = provisions.map((provision) => provision?.anchor);
  const duplicates = anchors.filter((one, index) => anchors.indexOf(one) !== index);
  if (duplicates.length > 0) {
    throw new Error(
      `this version carries the anchor ${duplicates[0]} twice; two provisions at one publisher `
        + 'coordinate make one permalink resolve to two different texts',
    );
  }

  const selected = anchor === null
    ? provisions
    : provisions.filter((provision) => provision?.anchor === anchor);

  // A typed refusal that hands back the coordinates this version does have. Never an empty
  // page, and never a fall back to full-text search: a different provision is not a near
  // miss, which is the note the card writes itself.
  const anchorRefusal = anchor !== null && selected.length === 0
    ? {
      code: 'anchor_not_in_version',
      sentence: ANCHOR_NOT_IN_VERSION_NOTE,
      payload: {
        nearest_anchors: anchors,
        what_would_answer: ANCHOR_ROUTES,
        asserts_absence_of_law: false,
      },
    }
    : null;

  const context = {
    state,
    expression,
    publisher: identity.publisher,
    work: identity.work,
    consolidated,
    noteLocale,
  };

  return Object.freeze({
    identity,
    semantics,
    state,
    expression,
    asOf,
    consolidated,
    // Provisional is the state's date against the reader's date, decided here rather than
    // read from a flag, and the comparison date is the reader's rather than this machine's.
    provisional: state.valid_from > asOf,
    holes: Object.freeze([...holes]),
    anchors: Object.freeze([...anchors]),
    anchorRefusal,
    superseded,
    noteLocale,
    stateVerify: Object.freeze({
      publisher: identity.publisher,
      sourceUri: state.source_uri,
      lexId: state.lex_id,
      hash: { kind: 'record_sha256', value: state.record_sha256 },
    }),
    provisions: Object.freeze(
      anchorRefusal === null
        ? selected.map((provision, index) => validateProvision(provision, index, context))
        : [],
    ),
  });
}

/**
 * One provision: its number, its dating, its text or the reason there is none, its
 * renderings, its permalink and its digest.
 *
 * @param {object} provision  a provision already through `validateProvision`
 */
function renderProvision(provision) {
  const dating = provision.conflicted
    ? renderValidityConflict({
      stateValidFrom: provision.stateValidFrom,
      wordingValidFrom: provision.wordingValidFrom,
    })
    : `<p class="reading-unchanged">${escapeHtml(unchangedSince(provision.wordingValidFrom))}</p>`;

  const body = provision.quotation === null
    ? renderRefusalCard(provision.refusal)
    : withAuthenticityFooting(quotedLaw(provision.quotation), provision.consolidated);

  const verify = provision.verify === null ? '' : renderVerifyCluster(provision.verify);

  const renderings = provision.renderings
    .map((rendering) => withAuthenticityFooting(
      renderUnofficialRendering(rendering),
      provision.consolidated,
    ))
    .join('');

  return (
    `<article class="reading-provision" id="${escapeHtml(provision.anchor)}">`
    + `<h3 class="reading-num">${escapeHtml(provision.num)}</h3>`
    + dating
    + body
    + renderings
    + '<p class="reading-permalink"><a href="'
    + `${escapeHtml(provision.permalink)}">Permalink</a> `
    + `<code>${escapeHtml(provision.permalink)}</code></p>`
    + verify
    + '</article>'
  );
}

/**
 * One work's text as it stood on one date.
 *
 * @see validateReading, which holds every rule this renders.
 */
export function renderReading(input) {
  const view = validateReading(input);
  const { state } = view;

  const provisional = view.provisional
    ? renderProvisional({ validFrom: state.valid_from, asOf: view.asOf })
    : '';

  // The vocabulary the record decided, handed to the banner in the shape the banner takes.
  const head = '<header class="reading-state">'
    + renderStateBanner({ envelope: { timeline_semantics: view.semantics }, state })
    + provisional
    + `<p class="reading-as-of">Read as of ${escapeHtml(view.asOf)}.</p>`
    + renderVerifyCluster(view.stateVerify)
    + '</header>';

  if (view.anchorRefusal !== null) {
    return (
      '<section class="reading reading-anchor-refused">'
      + head
      + renderRefusalCard(view.anchorRefusal)
      + '</section>'
    );
  }

  const gaps = view.holes.map((hole) => renderHole({
    kind: hole?.kind,
    from: hole?.from,
    to: hole?.to,
  })).join('');

  const disclosure = view.superseded === null
    ? ''
    : renderSupersededState({
      publisher: view.identity.publisher,
      work: view.identity.work,
      live: view.superseded.live,
      withdrawn: view.superseded.withdrawn,
    });

  return (
    '<section class="reading">'
    + head
    + disclosure
    + gaps
    + view.provisions.map((provision) => renderProvision(provision)).join('')
    + '</section>'
  );
}
