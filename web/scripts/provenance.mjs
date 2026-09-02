// The provenance page: the proof chain behind one record, for the reader who does not
// believe the answer.
//
// Every data view in this product ends with a Provenance link, and until now that link
// answered 404. A product whose claim is that an answer can be checked rather than trusted
// cannot ship the checking affordance as a dead end, so this is the destination.
//
// The whole page is written against the shape the live `provenance` tool actually returns,
// captured into `test/provenance.test.mjs`. Four things in that payload contradicted what a
// designed-from-the-spec renderer would have assumed, and each one is a rule below rather
// than a comment:
//
//  1. The stamp signature is not per record. Three different Luxembourg records came back
//     carrying byte-identical `stamp.signature`, and the Union records carry a different one,
//     also shared. So the signature travels with the mounted index, not with the bytes of the
//     record above it, and the payload never says what it covers. A page that printed
//     "signature valid" beside a record digest would be asserting authenticity of that record
//     on evidence that does not bind to it. The stamp is therefore rendered in the section
//     about the build that served the answer, with a sentence saying the payload does not
//     state what it signs.
//
//  2. `language` narrows `observations` and leaves `document` alone. Asking for the GDPR with
//     `language=fr` returns a document whose own language is `en` and whose `body_sha256` is
//     the English digest, beside an observation list containing only French bodies. Nothing in
//     the response says it was filtered. So the filter is a fact only the caller holds, it is a
//     required parameter here, and the page says the list is narrowed rather than letting a
//     reader read it as the record's whole history.
//
//  3. The refusal is coarse and its status is not in the client registry. An identifier no
//     work matches and a known work asked for at a state this corpus does not hold both come
//     back as `{"status":"unknown_work","lex_id":...}`, with no envelope at all. `unknown_work`
//     is not a member of REFUSAL_CODES; `refusal-card.mjs` says in a comment that the live
//     service uses `identifier_unknown`, and it does not. The translation is a closed table
//     here, and because the status cannot separate the two cases, the sentence does not
//     separate them either.
//
//  4. Observation windows can be zero wide, and two of them can open at the same instant.
//     The Union record carries rows whose `observed_to` equals their `observed_from`, and two
//     bodies first observed in the same second. Ordering is therefore checked with `<=`; a `<`
//     would have refused a real record, and the synthetic fixture could never have shown it.
//
// Beyond those, four rules are the page's whole reason to exist.
//
// It never asserts authenticity it cannot show. Naming a publisher key does not make a record
// authentic; the only authenticity claim on this page is a link to the publisher's own file,
// validated against that publisher's own host set.
//
// Absence is never evidence of absence in the publisher's holdings. Both branches say what
// THIS CORPUS holds, in figures taken from a corpus census the caller supplies, because the
// provenance payload carries no census and a count this module invented would be a figure
// nobody measured.
//
// The two clocks stay apart. Legal time is the publisher's, in the publisher's own vocabulary,
// derived from the record's publisher and never taken as an argument. Record time is when this
// service saw something, UTC, verbatim, never reformatted.
//
// Every figure comes from the payload. The event and observation counts are the lengths of the
// payload's own arrays, and when the payload declares itself truncated they are stated as a
// floor rather than as a total.

import { mark } from './design-tokens.mjs';
import { semanticsOf } from './publisher-vocabulary.mjs';
import { identityOf } from './record-identity.mjs';
import { renderRefusalCard, WHAT_WOULD_ANSWER } from './refusal-card.mjs';
import { escapeHtml } from './render.mjs';
import { publisherIdentifier, publisherSourceUri } from './routes.mjs';
import { isOrderedInterval, requireCalendarDate, requireUtcInstant } from './temporal.mjs';
import { dossierUrl, isSafeSegment } from './urls.mjs';
import { renderEnvelopeStrip } from './verify-cluster.mjs';
import { legalTimeSentence, renderStateBanner } from './state-banner.mjs';

const SHA256 = /^[0-9a-f]{64}$/;
const LANGUAGE = /^[a-z]{2}$/;
// The origin half of the service's own permalink: a scheme and a plain host, and no path.
// Anything with a path segment in front of the record's coordinates is a different address
// wearing the record's ending.
const ORIGIN = /^https:\/\/[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$/;

/** What a field says when the record does not carry it. Never an omission. */
export const NOT_RECORDED = 'not recorded';

/**
 * The live tool's refusal statuses, translated into the closed client registry.
 *
 * A Map rather than an object literal, for the reason `state-banner.mjs` gives: an object
 * literal answers `toString` and `constructor`, so a status nobody classified would find an
 * inherited member and pass the membership check that exists to refuse it.
 *
 * One entry, because one is what the live tool was observed to emit. A status not in here is
 * refused rather than rendered as a generic error, which is `refusal-card.mjs`'s first rule:
 * a refusal the client cannot name is exactly the case where a reader most needs the truth
 * about what happened.
 */
const REFUSAL_STATUS = new Map([['unknown_work', 'identifier_unknown']]);

/** The statuses this page can render, for a caller that wants to know before it asks. */
export const PROVENANCE_REFUSAL_STATUSES = Object.freeze([...REFUSAL_STATUS.keys()]);

/**
 * What the refusal says.
 *
 * `unknown_work` is returned both for an identifier no work matches and for a work this corpus
 * holds at other states but not at this one. The payload does not distinguish them, so neither
 * does this sentence. Saying "this work is unknown" over the second case would tell a reader
 * the publisher has no such instrument, which is the one thing this product must never say by
 * accident.
 */
export const UNKNOWN_RECORD_SENTENCE =
  'This service holds no record at that identifier. The refusal does not say whether no work '
  + 'matches the identifier or whether this corpus holds the work but not that state, so this '
  + 'page does not choose between them.';

/**
 * Where the dates on this page came from.
 *
 * Closed, and the reason the `--derived` mark can be applied by the record rather than by a
 * caller. A date this service worked out is not a date the publisher recorded, and the two
 * read identically unless something says which is which.
 */
const VALID_TIME_SOURCES = new Map([
  ['publisher', { derived: false, sentence: 'These dates are the publisher’s own.' }],
  [
    'derived',
    {
      derived: true,
      sentence:
        'These dates were derived by this service from the publisher record. The publisher '
        + 'did not state them.',
    },
  ],
]);

/** What the page says about the stamp, which is the one claim it is careful not to make. */
export const STAMP_SCOPE_NOTE =
  'This stamp travels with the index that served the answer. The payload does not say which '
  + 'bytes it signs, and identical signatures arrive with different records from the same '
  + 'publisher, so this page does not present it as evidence about the record above. The '
  + 'record’s authenticity is the publisher’s own file, linked with its coordinates.';

/** What a narrowed observation list is, said before a reader reads it as the whole history. */
export function narrowedNote(language) {
  return (
    `This list was narrowed to ${language} when it was requested. It is not the whole `
    + 'observation history of this record, and it contains the record’s own body digest '
    + `only if ${language} is the record’s own language.`
  );
}

/** What the absence of held text is, and what it is not. */
export const NO_TEXT_NOTE =
  'This corpus holds no text for this record. That is a gap in what this service ingested. It '
  + 'is not a statement about the publisher’s holdings and not a statement about the law.';

/** Why the extraction profile is on a provenance page at all. */
export const PROFILE_NOTE =
  'The extraction profile is the parser that produced this record. Two records made by '
  + 'different profiles mint different provision anchors, so a difference between them would '
  + 'report parser disagreement as legislation.';

/**
 * The file name the built page for one record takes.
 *
 * A `lex_id` separates its parts with colons, which Windows will not put in a file name, and
 * percent-escaping is not the way out: the static harness decodes a request path before
 * looking on disk, so a name carrying `%3A` is asked for under a name that cannot exist. So
 * the one character in the way is mapped to one that is legal in both a file name and a URL,
 * and `~` is chosen because it cannot occur in a `lex_id`, which keeps the mapping reversible
 * and two records from ever sharing a page.
 *
 * An identifier this cannot name is refused rather than squeezed into a name that might
 * already belong to another record.
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

function requireOwn(object, key, where) {
  if (!Object.hasOwn(object ?? {}, key)) {
    throw new Error(
      `${where} does not carry ${key}; an absent member and a member with nothing in it are `
        + 'different facts, and only one of them can be reported',
    );
  }
  return object[key];
}

function requireText(value, field) {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new Error(`${field} is required and is not a value: ${JSON.stringify(value)}`);
  }
  return value;
}

function requireBoolean(value, field) {
  if (typeof value !== 'boolean') {
    throw new Error(
      `${field} must be a boolean; an absent verdict is not the same as a false one and must `
        + 'never render as one',
    );
  }
  return value;
}

function requireDigest(value, field) {
  if (typeof value !== 'string' || !SHA256.test(value)) {
    throw new Error(`${field} is not 64 lowercase hex characters: ${JSON.stringify(value)}`);
  }
  return value;
}

function requireCount(value, field) {
  if (!Number.isSafeInteger(value) || value < 0) {
    throw new Error(
      `${field} is not a counted whole number: ${JSON.stringify(value)}; a figure this page `
        + 'cannot read is a figure nobody measured',
    );
  }
  return value;
}

/**
 * What this corpus holds, checked against the one thing that makes it sayable.
 *
 * The provenance payload has no census in it, so this is the one fact on the page a caller
 * genuinely holds and the record cannot supply. It is therefore required rather than
 * defaulted, closed against the publishers this interface has classified, and every figure it
 * prints is one of its own numbers. A page that said "this corpus holds few records of this
 * kind" without a number would be this module's opinion in the publisher's voice.
 *
 * @param {Array} rows  one row per mounted publisher, from the coverage payload
 */
function readHoldings(rows) {
  if (!Array.isArray(rows) || rows.length === 0) {
    throw new Error(
      'the provenance page states what this corpus holds, so it requires the corpus census; '
        + 'without it the page can only say a record is absent, which is the sentence this '
        + 'product exists to qualify',
    );
  }
  const seen = new Set();
  const read = rows.map((row, index) => {
    const where = `corpus census row ${index + 1}`;
    const publisher = requireText(row?.publisher, `${where} publisher`);
    // Classified, not merely named. An unclassified publisher's dates are on an unknown clock,
    // and a census row for one would put a figure on the page under a heading nobody can read.
    semanticsOf(publisher, where);
    if (seen.has(publisher)) {
      throw new Error(`${where} repeats ${publisher}; one publisher is counted once`);
    }
    seen.add(publisher);
    return Object.freeze({
      publisher,
      publisher_name: requireText(row?.publisher_name, `${where} publisher_name`),
      works: requireCount(row?.works, `${where} works`),
      versions: requireCount(row?.versions, `${where} versions`),
    });
  });
  return Object.freeze(read);
}

/** A counted noun. The figure comes from the payload; only the noun is ours to inflect. */
function counted(count, noun) {
  return `${count} ${count === 1 ? noun : `${noun}s`}`;
}

/**
 * How many of something the payload carried.
 *
 * A truncated list is stated as a floor. Printed as a total it asserts that nothing else ever
 * happened to this record, which is the strongest sentence on the page and one the payload
 * explicitly declines to make.
 */
function countSentence(noun, count, complete) {
  const measured = counted(count, noun);
  return complete ? measured : `at least ${measured}; the service truncated this list`;
}

/** One census row as a sentence, in its own figures. */
function holdingSentence(row) {
  return `${counted(row.works, 'work')} and ${counted(row.versions, 'version')} from `
    + `${row.publisher_name}`;
}

/**
 * The population disclosure the `identifier_unknown` contract requires.
 *
 * Built from the census rather than written, because the contract's whole point is that the
 * reader be told the size of what was searched. A fixed sentence would keep saying the same
 * thing after the corpus tripled.
 */
export function populationDisclosure(holdings) {
  return (
    `This corpus holds ${holdings.map(holdingSentence).join('; ')}. That is what this service `
    + 'holds. It is not what the publisher holds, and it is not the law.'
  );
}

function readEvents(events, { document, truncated }) {
  if (!Array.isArray(events)) {
    throw new Error(
      'the event chain is a list even when nothing but the first sighting is in it; a page '
        + 'silent about the chain reads as a record with no history',
    );
  }
  let previous = null;
  const read = events.map((event, index) => {
    const where = `event ${index + 1}`;
    const observedFrom = requireUtcInstant(event?.observed_from, `${where} observed_from`);
    if (previous !== null && observedFrom < previous) {
      throw new Error(
        `${where} is dated ${observedFrom}, before the event above it at ${previous}; a chain `
          + 'that runs backwards is not a chain',
      );
    }
    previous = observedFrom;
    const detail = event?.detail ?? null;
    if (detail !== null) requireText(detail, `${where} detail`);
    return Object.freeze({
      // Event names and details are this service's machine vocabulary, not prose and not the
      // publisher's words. They are rendered as code so an event this build has never seen
      // reads as an unfamiliar code rather than as a sentence somebody wrote. Closing the set
      // here would refuse real records over a name nobody has added yet, and refusing a whole
      // proof chain over its vocabulary is worse than showing the code.
      event: requireText(event?.event, `${where} event`),
      scope: requireText(event?.scope, `${where} scope`),
      observed_from: observedFrom,
      detail,
    });
  });

  // Only when the payload says the chain is whole. A truncated chain may be missing its head,
  // and asserting that the first row is the first sighting would then be a claim about rows
  // that are not on the page.
  if (!truncated) {
    const sightings = read.filter((one) => one.event === 'first_sighting');
    if (sightings.length !== 1) {
      throw new Error(
        `this complete event chain carries ${sightings.length} first sightings; a record was `
          + 'first seen once, and a chain that says otherwise cannot be read as a history',
      );
    }
    if (read[0].event !== 'first_sighting') {
      throw new Error(
        `this complete event chain begins with ${read[0].event}; something happened to this `
          + 'record before this service first saw it, which is not a history this service can '
          + 'have observed',
      );
    }
    if (read[0].observed_from !== document.observed_from) {
      throw new Error(
        `the first sighting is dated ${read[0].observed_from} while the record says it was `
          + `first observed ${document.observed_from}; two answers to when this service first `
          + 'saw this record cannot both be shown as the record time',
      );
    }
  }
  return Object.freeze(read);
}

function readObservations(observations, { document, languageFilter }) {
  if (!Array.isArray(observations)) {
    throw new Error(
      'the observation history is a list even when it is empty; an absent list and a record '
        + 'whose bodies were never held are different facts',
    );
  }

  const latestByLanguage = new Map();
  const openByLanguage = new Map();

  const read = observations.map((observation, index) => {
    const where = `observation ${index + 1}`;
    const language = observation?.language;
    if (typeof language !== 'string' || !LANGUAGE.test(language)) {
      throw new Error(
        `${where} does not carry its own language: ${JSON.stringify(language)}; a digest that `
          + 'does not say which expression it is the digest of is a number',
      );
    }
    // The filter is the caller's fact, so the caller's fact is what it is checked against. The
    // response says nothing about having been narrowed, and a row outside the requested
    // language would mean the list is not the list that was asked for.
    if (languageFilter !== null && language !== languageFilter) {
      throw new Error(
        `${where} is a ${language} observation in a list requested as ${languageFilter}; the `
          + 'payload never says it was filtered, so a row outside the filter means this list '
          + 'is not the one that was asked for',
      );
    }
    const observedFrom = requireUtcInstant(observation?.observed_from, `${where} observed_from`);
    const observedTo = observation?.observed_to ?? null;
    if (observedTo !== null) requireUtcInstant(observedTo, `${where} observed_to`);
    // `<=` rather than `<`. The live Union record carries observation windows whose end equals
    // their start, and a strict comparison would refuse a real record over a window that is
    // simply zero wide.
    if (observedTo !== null && observedTo < observedFrom) {
      throw new Error(
        `${where} was observed to ${observedTo}, before ${observedFrom}; a window that closes `
          + 'before it opens is not a period this service can have observed',
      );
    }
    if (observedFrom < document.observed_from) {
      throw new Error(
        `${where} is dated ${observedFrom}, before this service first saw the record at `
          + `${document.observed_from}; this service cannot have held a body of a record it had `
          + 'not met',
      );
    }
    const previous = latestByLanguage.get(language);
    if (previous !== undefined && observedFrom < previous) {
      throw new Error(
        `${where} is dated ${observedFrom}, before the previous ${language} observation at `
          + `${previous}; the history of one expression is read in the order it is listed`,
      );
    }
    latestByLanguage.set(language, observedFrom);

    if (observedTo === null) {
      const open = (openByLanguage.get(language) ?? 0) + 1;
      openByLanguage.set(language, open);
      if (open > 1) {
        throw new Error(
          `this record has ${open} open ${language} observations; two bodies currently held for `
            + 'one expression is two answers to what the text says now',
        );
      }
    }

    requireCalendarDate(observation?.expr_valid_from, `${where} expr_valid_from`);

    return Object.freeze({
      language,
      expr_valid_from: observation.expr_valid_from,
      sha256: requireDigest(observation?.sha256, `${where} sha256`),
      observed_from: observedFrom,
      observed_to: observedTo,
      // Whether this row is the body the record itself names. Derived by comparing the two
      // digests rather than by trusting position, because "the last one" is a guess and the
      // digests are the evidence.
      is_record_body:
        document.body_sha256 !== null && observation.sha256 === document.body_sha256,
    });
  });

  const openInRecordLanguage = read.filter(
    (one) => one.observed_to === null && one.language === document.language,
  );

  if (document.body_sha256 === null) {
    if (openInRecordLanguage.length > 0) {
      throw new Error(
        'this record carries no body digest while an open observation holds a body in its own '
          + 'language; one of the two is about a different record',
      );
    }
  } else if (languageFilter === null) {
    // Only on an unfiltered list. Narrowed to a language the record is not in, the record's own
    // body digest is legitimately absent, and demanding it would refuse a real answer.
    if (openInRecordLanguage.length !== 1) {
      throw new Error(
        `this record names a body digest and its unfiltered history holds `
          + `${openInRecordLanguage.length} open ${document.language} observations; the digest `
          + 'the record carries has to be one this service is currently holding',
      );
    }
    if (openInRecordLanguage[0].sha256 !== document.body_sha256) {
      throw new Error(
        `the record names body ${document.body_sha256} while the open ${document.language} `
          + `observation holds ${openInRecordLanguage[0].sha256}; a page showing both as this `
          + 'record’s text would be showing two different texts',
      );
    }
  }

  return Object.freeze(read);
}

/**
 * Everything the page may say, decided once.
 *
 * Split out for the reason `validateRefusal` is: the React runtime calls this and lays out
 * what it returns, so a rule cannot be repaired in the string renderer while the component
 * keeps the defect. Throws on anything this page must not display.
 *
 * @param {object} input
 * @param {{lex_id: string, language: string|null}} input.requested  what the reader asked for
 * @param {object} input.record    the `provenance` payload, exactly as the service returned it
 * @param {Array}  input.holdings  the corpus census, one row per mounted publisher
 */
export function readProvenance({ requested, record, holdings } = {}) {
  // The reader's own coordinates. The record cannot supply what was asked for, only what was
  // found, and the difference between those two is the entire failure mode of a page that
  // exists to be checked.
  const requestedLexId = requireText(
    requireOwn(requested, 'lex_id', 'the provenance request'),
    'the requested lex_id',
  );
  const requestedLanguage = requireOwn(requested, 'language', 'the provenance request');
  if (requestedLanguage !== null && !LANGUAGE.test(String(requestedLanguage))) {
    throw new Error(
      `the requested language ${JSON.stringify(requestedLanguage)} is neither a two letter code `
        + 'nor null; null is how a caller says it asked for no filter, and leaving it out is how '
        + 'a filtered list comes to be read as a whole history',
    );
  }

  const census = readHoldings(holdings);

  if (typeof record !== 'object' || record === null || Array.isArray(record)) {
    throw new Error('the provenance page renders a provenance payload, not a value beside one');
  }

  const hasEnvelope = Object.hasOwn(record, 'envelope');
  const hasStatus = Object.hasOwn(record, 'status');
  if (hasEnvelope === hasStatus) {
    throw new Error(
      'a provenance payload is an answer with an envelope or a refusal with a status, and this '
        + `one carries ${hasEnvelope ? 'both' : 'neither'}; a page that guesses which it is `
        + 'guesses whether there is a record',
    );
  }

  if (hasStatus) {
    const status = requireText(record.status, 'the refusal status');
    const code = REFUSAL_STATUS.get(status);
    if (code === undefined) {
      throw new Error(
        `the provenance tool refused with ${JSON.stringify(status)}, which this client cannot `
          + `name; the statuses it translates are ${PROVENANCE_REFUSAL_STATUSES.join(', ')}, and `
          + 'a refusal rendered as a generic error is the one case where a reader most needs '
          + 'the truth about what happened',
      );
    }
    const refusedLexId = requireText(record.lex_id, 'the refused lex_id');
    if (refusedLexId !== requestedLexId) {
      throw new Error(
        `this refusal is about ${refusedLexId} and the reader asked about ${requestedLexId}; a `
          + 'page answering about a different identifier than the one in its own URL is the '
          + 'worst thing this screen can do',
      );
    }
    return Object.freeze({
      kind: 'refusal',
      requestedLexId,
      code,
      status,
      sentence: UNKNOWN_RECORD_SENTENCE,
      holdings: census,
      payload: Object.freeze({
        population_disclosure: populationDisclosure(census),
        // All three routes, because the status cannot tell the three cases apart. Narrowing
        // this list would be this page deciding which of them applies.
        what_would_answer: WHAT_WOULD_ANSWER,
        asserts_absence_of_law: false,
      }),
    });
  }

  const envelope = record.envelope;
  const document = requireOwn(record, 'document', 'this provenance answer');
  const truncated = requireBoolean(
    requireOwn(record, 'truncated', 'this provenance answer'),
    'truncated',
  );
  const stamp = requireOwn(record, 'stamp', 'this provenance answer');
  const events = requireOwn(record, 'events', 'this provenance answer');
  const observations = requireOwn(record, 'observations', 'this provenance answer');

  if (envelope?.status !== 'ok') {
    throw new Error(
      `this provenance answer carries envelope status ${JSON.stringify(envelope?.status)}; a `
        + 'proof chain shown under an envelope that is not an answer is a proof of nothing',
    );
  }

  // The identity is parsed in exactly one place, and everything else on the page is checked
  // against it. A page whose heading, permalink and digests were each read off a different
  // member can describe three records.
  const identity = identityOf(document?.lex_id, 'this provenance record');
  if (document.lex_id !== requestedLexId) {
    throw new Error(
      `this page is addressed to ${requestedLexId} and the service answered about `
        + `${document.lex_id}; a provenance page about a record other than the one in its own `
        + 'URL cannot be checked by anybody',
    );
  }
  if (identity.publisher !== envelope.publisher) {
    throw new Error(
      `the record belongs to ${identity.publisher} and the envelope says ${envelope.publisher}; `
        + 'whose record this is is not something this page may choose',
    );
  }
  if (document.work !== identity.work) {
    throw new Error(
      `the record says its work is ${document.work} and its identifier says ${identity.work}`,
    );
  }
  if (document.version_key !== identity.state) {
    throw new Error(
      `the record says its version key is ${document.version_key} and its identifier says `
        + `${identity.state}; two names for one state is two states`,
    );
  }

  // The expression's own language, read before anything that depends on it. It decides which
  // observation is this record's body and which language tag the publisher's title carries,
  // and both are wrong in a way nobody can see if it is read late or defaulted.
  const recordLanguage = document?.language;
  if (typeof recordLanguage !== 'string' || !LANGUAGE.test(recordLanguage)) {
    throw new Error(
      `the record does not carry its own language: ${JSON.stringify(recordLanguage)}; the `
        + 'chrome language is the language of this interface, and labelling a publisher title '
        + 'with it makes a screen reader read one language in the voice of another',
    );
  }
  // Declared, not merely present. `undefined` and `null` read the same at a comparison and
  // mean different things: one is a record that holds no body, the other is a member nobody
  // sent, and the observation cross-check below turns on exactly that distinction.
  requireOwn(document, 'body_sha256', 'this record');

  // Derived from the publisher, never accepted. The envelope also asserts it, so the two are
  // compared: an envelope that labelled a Union consolidation as applicability would be the
  // exact defect `publisher-vocabulary.mjs` exists to end, and silently preferring either one
  // would hide it.
  const semantics = semanticsOf(identity.publisher, 'this provenance record');
  if (envelope.timeline_semantics !== semantics) {
    throw new Error(
      `${identity.publisher} dates are ${semantics} and this envelope says `
        + `${JSON.stringify(envelope.timeline_semantics)}; the publisher's own vocabulary is `
        + 'not something an envelope can override',
    );
  }

  const publisherRow = census.find((row) => row.publisher === identity.publisher);
  if (publisherRow === undefined) {
    throw new Error(
      `the corpus census does not count ${identity.publisher}, so this page cannot say what `
        + 'this corpus holds from the publisher whose record it is describing',
    );
  }

  if (!isSafeSegment(identity.state)) {
    throw new Error(
      `the state segment ${JSON.stringify(identity.state)} is not addressable, so this record `
        + 'has no coordinates a reader could go back to',
    );
  }
  const objectPath = `${dossierUrl({
    publisher: identity.publisher,
    work: identity.work,
  })}/${identity.state}`;

  // The service's own address for this record, checked rather than trusted, and printed as
  // text rather than offered as a link. The check is that it is an origin followed by exactly
  // this record's coordinates: a permalink that ends with them but carries a path in front of
  // them is a different address wearing this record's ending.
  const permalink = requireText(document?.permalink, 'the record permalink');
  if (!permalink.endsWith(objectPath) || !ORIGIN.test(permalink.slice(0, -objectPath.length))) {
    throw new Error(
      `the record permalink ${permalink} is not this service's address for ${objectPath}; a `
        + 'permalink that names a different record than the page it sits on resolves a reader '
        + 'somewhere nobody sent them',
    );
  }

  // Legal time, in the publisher's own vocabulary, and record time, verbatim. Both are
  // rendered by the state banner so this page cannot become a second place where the two
  // clocks are phrased.
  requireCalendarDate(document?.valid_from, 'valid_from');
  const validTo = document?.valid_to ?? null;
  if (validTo !== null) requireCalendarDate(validTo, 'valid_to');
  if (!isOrderedInterval(document.valid_from, validTo)) {
    throw new Error(
      `valid_from ${document.valid_from} is after valid_to ${validTo}; an inverted interval is `
        + 'not a state the publisher can have recorded',
    );
  }
  const publicationDate = document?.publication_date ?? null;
  if (publicationDate !== null) requireCalendarDate(publicationDate, 'publication_date');
  requireUtcInstant(document?.observed_from, 'the record observed_from');

  const validTimeSource = VALID_TIME_SOURCES.get(document?.valid_time_source);
  if (validTimeSource === undefined) {
    throw new Error(
      `valid_time_source ${JSON.stringify(document?.valid_time_source)} is not one of `
        + `${[...VALID_TIME_SOURCES.keys()].join(', ')}; whether a date is the publisher's or `
        + "this service's own is the difference between a record and an inference",
    );
  }

  const textAvailable = requireBoolean(document?.text_available, 'text_available');
  const bodyDigest = document?.body_sha256 ?? null;
  if (bodyDigest !== null) requireDigest(bodyDigest, 'body_sha256');
  if (!textAvailable && bodyDigest !== null) {
    throw new Error(
      'this record says no text is available and carries a body digest; a digest of bytes this '
        + 'corpus says it does not hold is a number about nothing',
    );
  }

  const readEventChain = readEvents(events, { document, truncated });
  const readObservationHistory = readObservations(observations, {
    document,
    languageFilter: requestedLanguage,
  });

  // Two verdicts about one signature. They agree in every live payload, and if they ever
  // disagree the page cannot be read either way, so it is refused rather than shown with one
  // of them chosen.
  const stampValid = requireBoolean(stamp?.signature_valid, 'stamp.signature_valid');
  const envelopeStampValid = requireBoolean(
    envelope?.freshness?.stamp_signature_valid,
    'freshness.stamp_signature_valid',
  );
  if (stampValid !== envelopeStampValid) {
    throw new Error(
      `the stamp says its signature is ${stampValid} and the envelope says `
        + `${envelopeStampValid}; a page carrying two verdicts about one signature cannot be `
        + 'read either way',
    );
  }

  return Object.freeze({
    kind: 'record',
    requestedLexId,
    identity,
    semantics,
    envelope,
    document,
    // Publisher text, so it carries the expression's own language wherever it is rendered.
    title: requireText(document?.title, 'the record title'),
    titleLanguage: recordLanguage,
    // A name, not a link. An ELI and a CELEX are identifiers spelled as HTTP URIs, and both
    // publishers issue them over `http://`.
    workIdentifier: publisherIdentifier({
      publisher: identity.publisher,
      uri: document?.work_identifier,
    }),
    // The one authenticity claim this page makes, and it is the publisher's own file.
    sourceUri: publisherSourceUri({ publisher: identity.publisher, uri: document?.source_uri }),
    permalink,
    objectPath,
    documentType: requireText(document?.document_type, 'document_type'),
    extractionProfile: document?.extraction_profile ?? null,
    withdrawn: requireBoolean(document?.withdrawn, 'withdrawn'),
    textAvailable,
    validTimeSource: Object.freeze({ name: document.valid_time_source, ...validTimeSource }),
    digests: Object.freeze([
      Object.freeze({ kind: 'record_sha256', value: requireDigest(document?.record_sha256, 'record_sha256') }),
      ...(bodyDigest === null ? [] : [Object.freeze({ kind: 'body_sha256', value: bodyDigest })]),
    ]),
    truncated,
    events: readEventChain,
    observations: readObservationHistory,
    languageFilter: requestedLanguage,
    // Measured from the payload's own arrays. When the payload says it truncated them, the
    // number is a floor and the page says so rather than presenting it as a total.
    counts: Object.freeze({
      events: readEventChain.length,
      observations: readObservationHistory.length,
      complete: !truncated,
    }),
    // The sentences both renderers show, composed once. A figure or a qualification composed
    // at the call site is a figure two renderers can disagree about, and the one that drifts
    // is always the one nobody looked at.
    sentences: Object.freeze({
      // Legal time, phrased by the one function that phrases legal time. The string page shows
      // the state banner, which calls the same function on the same values, so the two
      // runtimes cannot spell one publisher's claim two ways.
      legal: legalTimeSentence({
        semantics,
        validFrom: document.valid_from,
        validTo: document.valid_to,
      }),
      // Record time carries no publisher claim, so there is no vocabulary to share: these are
      // this service's own instants, printed exactly as recorded.
      recordTime:
        `Published ${publicationDate ?? 'not recorded by the publisher'} / First observed `
        + `${document.observed_from}`,
      events: countSentence('event', readEventChain.length, !truncated),
      observations: countSentence('observation', readObservationHistory.length, !truncated),
      holdings: `This corpus holds ${holdingSentence(publisherRow)}. This page describes one `
        + 'of them.',
      population: populationDisclosure(census),
      validTime: validTimeSource.sentence,
      narrowed: requestedLanguage === null ? null : narrowedNote(requestedLanguage),
      noText: textAvailable ? null : NO_TEXT_NOTE,
    }),
    stamp: Object.freeze({
      signature_valid: stampValid,
      algorithm: requireText(stamp?.algorithm, 'stamp.algorithm'),
      public_key: requireText(stamp?.public_key, 'stamp.public_key'),
      signature: requireText(stamp?.signature, 'stamp.signature'),
    }),
    holdings: census,
    publisherHoldings: publisherRow,
  });
}

/** One labelled row, in the layout every strip on this site already uses. */
function row(label, value) {
  return (
    `<div class="strip-row"><dt>${escapeHtml(label)}</dt>`
    + `<dd>${value}</dd></div>`
  );
}

/** A value that is evidence: whole, selectable, never truncated for display. */
function evidence(value) {
  return `<code>${escapeHtml(value)}</code>`;
}

function renderEventChain(view) {
  const items = view.events
    .map(
      (event) =>
        '<li class="provenance-event"><dl>'
        + row('event', evidence(event.event))
        + row('scope', evidence(event.scope))
        + row('observed', evidence(event.observed_from))
        + row('detail', event.detail === null ? escapeHtml(NOT_RECORDED) : evidence(event.detail))
        + '</dl></li>',
    )
    .join('');
  return `<ol class="provenance-events">${items}</ol>`;
}

function renderObservationHistory(view) {
  const items = view.observations
    .map(
      (observation) =>
        '<li class="provenance-observation"><dl>'
        + row('language', evidence(observation.language))
        + row('expression dated', evidence(observation.expr_valid_from))
        + row('sha256', evidence(observation.sha256))
        + row('observed from', evidence(observation.observed_from))
        + row(
          'observed to',
          observation.observed_to === null
            ? 'still held'
            : evidence(observation.observed_to),
        )
        + row(
          'is this record’s body',
          observation.is_record_body ? 'yes, this digest is the one the record names' : 'no',
        )
        + '</dl></li>',
    )
    .join('');
  return `<ol class="provenance-observations">${items}</ol>`;
}

/**
 * The provenance page for one record, or the honest refusal when there is none.
 *
 * @param {object} input  the same shape `readProvenance` takes
 */
export function renderProvenance(input) {
  const view = readProvenance(input);

  if (view.kind === 'refusal') {
    return (
      '<section class="provenance provenance-refused">'
      + '<h2>No record at this identifier</h2>'
      + `<p class="provenance-asked">This page was asked about ${evidence(view.requestedLexId)}.</p>`
      + renderRefusalCard({
        code: view.code,
        sentence: view.sentence,
        payload: view.payload,
      })
      + '</section>'
    );
  }

  const identity = view.identity;

  const record =
    '<section class="provenance-block"><h2>The record this page is about</h2>'
    + `<p class="provenance-title" lang="${escapeHtml(view.titleLanguage)}">`
    + `${escapeHtml(view.title)}</p>`
    + '<dl>'
    + row('lex_id', evidence(view.document.lex_id))
    + row('publisher', evidence(identity.publisher))
    + row('work', evidence(identity.work))
    + row('version key', evidence(view.document.version_key))
    + row('document type', evidence(view.documentType))
    + row('language of this record', evidence(view.titleLanguage))
    + row('publisher name for the work', evidence(view.workIdentifier))
    + row('this service’s address for it', evidence(view.permalink))
    + row('withdrawn by the publisher', view.withdrawn ? 'yes' : 'no')
    + row(
      'extraction profile',
      view.extractionProfile === null
        ? escapeHtml(NOT_RECORDED)
        : evidence(view.extractionProfile),
    )
    + '</dl>'
    + `<p class="provenance-note">${escapeHtml(PROFILE_NOTE)}</p>`
    + '<p class="provenance-source">'
    + `<a href="${escapeHtml(view.sourceUri)}" rel="external">The publisher’s own file for `
    + 'this record</a></p>'
    + '</section>';

  const clocks =
    '<section class="provenance-block"><h2>The two clocks</h2>'
    + renderStateBanner({ envelope: view.envelope, state: view.document })
    + `<p class="provenance-note">${escapeHtml(view.sentences.validTime)}</p>`
    + (view.validTimeSource.derived
      ? `<p class="provenance-derived">${mark(
        '--derived',
        'The legal-time dates above were worked out by this service.',
      )}</p>`
      : '')
    + '</section>';

  const chain =
    '<section class="provenance-block"><h2>What this service observed, and when</h2>'
    + `<p class="provenance-count">${escapeHtml(view.sentences.events)}. Every time below is `
    + 'record time, in UTC, as this service recorded it.</p>'
    + renderEventChain(view)
    + '</section>';

  const bodies =
    '<section class="provenance-block"><h2>The bodies this service has held</h2>'
    + '<dl>'
    + view.digests.map((digest) => row(digest.kind, evidence(digest.value))).join('')
    + '</dl>'
    + (view.sentences.noText === null
      ? ''
      : `<p class="provenance-note">${escapeHtml(view.sentences.noText)}</p>`)
    + (view.sentences.narrowed === null
      ? ''
      : `<p class="provenance-note">${escapeHtml(view.sentences.narrowed)}</p>`)
    + `<p class="provenance-count">${escapeHtml(view.sentences.observations)}.</p>`
    + renderObservationHistory(view)
    + '</section>';

  const build =
    '<section class="provenance-block"><h2>What served this answer</h2>'
    + renderEnvelopeStrip({ envelope: view.envelope })
    + '<dl>'
    + row('stamp signature valid', view.stamp.signature_valid ? 'yes' : 'no')
    + row('algorithm', evidence(view.stamp.algorithm))
    + row('public key', `<code class="provenance-pem">${escapeHtml(view.stamp.public_key)}</code>`)
    + row('signature', `<code class="provenance-pem">${escapeHtml(view.stamp.signature)}</code>`)
    + '</dl>'
    + `<p class="provenance-note">${escapeHtml(STAMP_SCOPE_NOTE)}</p>`
    + '</section>';

  const corpus =
    '<section class="provenance-block"><h2>What this corpus holds</h2>'
    + `<p class="provenance-holdings">${escapeHtml(view.sentences.holdings)}</p>`
    + `<p class="provenance-note">${escapeHtml(view.sentences.population)}</p>`
    + '</section>';

  return `<section class="provenance">${record}${clocks}${chain}${bodies}${build}${corpus}</section>`;
}
