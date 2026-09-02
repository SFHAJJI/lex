// S9, the citation checker.
//
// Paste a citation, find out whether it resolves, and if a quote came with it, whether the words
// match. The product spec's never-list forbids emitting a citation this screen cannot resolve, so
// this is the surface that decides what the rest of the product is allowed to say.
//
// Three distinctions do the work here, and collapsing any of them turns a checker into a guesser.
//
// **Unresolvable is not out-of-corpus.** "Not a citation I recognise" and "a citation I recognise
// and we do not hold" are different answers to the reader. The second one can be linked out to the
// publisher; the first cannot, and pretending otherwise sends someone to a page that will not
// exist. A CSSF circular number is recognised, classified and not ours.
//
// **Ambiguous is not resolved.** Where more than one held record answers a citation, every
// candidate is listed and none is preselected. This is the same rule the timeline applies to
// overlapping states, for the same reason: choosing silently is on the never-list.
//
// **A quote verdict is mechanical, never semantic.** Characters match or they do not. This screen
// never says a difference is immaterial, because that is applying law to facts.
//
// One resolution rule that looks like an implementation detail and is not: **a citation resolves
// against the publisher's full identifier, never against the work key.** The LU work key is the
// ELI path with its first two segments dropped, and those two segments are `etat/leg` and
// `etat/adm`. Both collapse to nothing, so `/eli/etat/leg/agc/2023/10/06/b3399` and
// `/eli/etat/adm/agc/2023/10/06/b3399` produce the same key. Measured today: 1,384 held works are
// `leg` and 15 are `adm`, and zero collide, which is the only reason nothing has broken. A checker
// keyed on the work key would answer an administrative citation with a legislative act, and the
// reader would have no way to see it happen.

/** What a citation turned out to be. Closed, and ordered from most to least resolved. */
export const VERDICTS = Object.freeze([
  'resolved',
  'ambiguous',
  'out_of_corpus',
  'unrecognised',
]);

/** Citation forms this build can parse. Closed. */
export const FORMS = Object.freeze([
  'lex_id',
  'permalink',
  'lu_eli',
  'lu_eli_dated',
  'eu_celex',
  'eu_celex_consolidated',
]);

/** Said on an unrecognised citation, which is a statement about this parser. */
export const UNRECOGNISED_NOTE =
  'This is not a citation form this build can parse. That is a limit of this checker, not a ' +
  'judgement about the reference.';

/** Said on a recognised citation from a body this corpus does not hold. */
export const OUT_OF_CORPUS_NOTE =
  'Recognised, and outside this corpus. The publisher below is the place to check it.';

/** Said above an ambiguous result, before the candidates. */
export const AMBIGUOUS_NOTE =
  'More than one held record answers this citation. All of them are listed and none is chosen ' +
  'for you.';

/** Bodies this checker recognises and deliberately does not hold, with where to go instead. */
const OUT_OF_CORPUS_BODIES = Object.freeze(
  Object.assign(Object.create(null), {
    cssf_circular: Object.freeze({
      label: 'CSSF circular',
      official: 'https://www.cssf.lu/en/circulars/',
    }),
    cnpd_decision: Object.freeze({
      label: 'CNPD decision',
      official: 'https://cnpd.public.lu/',
    }),
  }),
);

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

// Anchored, because a pattern that matches anywhere in a pasted paragraph would find a CELEX
// inside a URL and report it as a bare citation.
const PATTERNS = Object.freeze([
  // A CELEX is sector, year, type, number: one digit, four digits, one or two letters, four
  // digits. My first pattern read the sector as part of the year and matched nothing at all.
  //
  // A consolidated CELEX is the same shape in sector 0 with its applicability date appended, so
  // it names one state rather than a work.
  Object.freeze({
    form: 'eu_celex_consolidated',
    re: /^0(\d{4})([A-Z]{1,2})(\d{4})-(\d{4})(\d{2})(\d{2})$/,
  }),
  Object.freeze({ form: 'eu_celex', re: /^([1-9])(\d{4})([A-Z]{1,2})(\d{4})$/ }),
  Object.freeze({
    form: 'lu_eli_dated',
    re: /^https?:\/\/(?:data\.)?legilux\.public\.lu\/eli\/etat\/(leg|adm)\/([^/]+)\/(\d{4})\/(\d{2})\/(\d{2})\/([^/]+)\/consolide\/(\d{8})\/?$/,
  }),
  Object.freeze({
    form: 'lu_eli',
    re: /^https?:\/\/(?:data\.)?legilux\.public\.lu\/eli\/etat\/(leg|adm)\/([^/]+)\/(\d{4})\/(\d{2})\/(\d{2})\/([^/]+?)(?:\/jo)?\/?$/,
  }),
  // A product permalink carries the state's own digest, so it identifies one exact text
  // rather than one date. The authority is compared literally against the canonical host,
  // not parsed, because `https://law.soufien.lu@evil.example/` has the canonical host as its
  // userinfo and a URL parser reports the attacker's host as the hostname.
  Object.freeze({
    form: 'permalink',
    re: /^https:\/\/law\.soufien\.lu\/([a-z-]+)\/([a-z0-9_-]+)\/(\d{4}-\d{2}-\d{2})--([0-9a-f]{64})$/,
  }),
  Object.freeze({ form: 'lex_id', re: /^([a-z-]+):([a-z0-9-]+):(\d{4}-\d{2}-\d{2})$/ }),
]);

/**
 * What a pasted string is, structurally, before anything is looked up.
 *
 * Parsing and resolution are separate on purpose. "I cannot read this" and "I read it and hold
 * nothing" are different answers, and a function that did both would have to pick one.
 *
 * @param {string} raw the pasted citation
 * @returns {object|null} `{ form, ...fields }`, or null when no form matches
 */
export function parseCitation(raw) {
  if (typeof raw !== 'string') {
    return null;
  }
  const text = raw.trim();
  if (text.length === 0) {
    return null;
  }

  for (const { form, re } of PATTERNS) {
    const match = re.exec(text);
    if (match === null) continue;

    if (form === 'eu_celex') {
      // Held EU lex_ids carry the CELEX case-folded (`eu-eurlex:32013r0575:...`), so a citation
      // written the publisher's way, in upper case, must fold to match. Folding here rather than
      // at the lookup keeps the reader's own spelling for display.
      const [, sector, year, type, number] = match;
      return { form, celex: match[0], sector, year, type, number, key: match[0].toLowerCase() };
    }
    if (form === 'eu_celex_consolidated') {
      const [, year, type, number, y, m, d] = match;
      // Deliberately no base CELEX. A consolidated citation is sector 0 and the corpus keys works
      // by the base act's sector, but this parser does not know which sector that act is in, and
      // guessing 3 because secondary legislation usually sits there would be resolution dressed
      // as parsing. The parts are returned and the caller resolves them.
      return {
        form,
        celex: match[0],
        sector: '0',
        year,
        type,
        number,
        key: match[0].toLowerCase(),
        at: `${y}-${m}-${d}`,
      };
    }
    if (form === 'lu_eli' || form === 'lu_eli_dated') {
      const [, branch, type, y, m, d, num, consolidated] = match;
      const identifier = `http://data.legilux.public.lu/eli/etat/${branch}/${type}/${y}/${m}/${d}/${num}`;
      const parsed = {
        form,
        branch,
        identifier,
        // Carried for display and for reconciliation, never for lookup. See the header note: the
        // key is not unique across the leg and adm branches.
        key: `${type}-${y}-${m}-${d}-${num}`,
      };
      if (consolidated !== undefined) {
        parsed.at =
          `${consolidated.slice(0, 4)}-${consolidated.slice(4, 6)}-${consolidated.slice(6, 8)}`;
      }
      return parsed;
    }
    if (form === 'permalink') {
      const [, publisher, work, at, digest] = match;
      return { form, publisher, key: work, at, digest };
    }
    const [, publisher, work, at] = match;
    return { form, publisher, key: work, at };
  }
  return null;
}

/** Whether a recognised-but-unheld body was cited, or null. */
export function outOfCorpusBody(raw) {
  if (typeof raw !== 'string') return null;
  const text = raw.trim();
  if (/^CSSF\s+(?:circular\s+)?\d{2}\/\d{3}$/i.test(text)) {
    return OUT_OF_CORPUS_BODIES.cssf_circular;
  }
  if (/^CNPD\s+(?:decision\s+)?\d+\/\d{4}$/i.test(text)) {
    return OUT_OF_CORPUS_BODIES.cnpd_decision;
  }
  return null;
}

function requireCandidate(candidate, index) {
  const where = `candidate ${index + 1}`;
  for (const field of ['lex_id', 'identifier', 'valid_from']) {
    if (typeof candidate?.[field] !== 'string' || candidate[field].length === 0) {
      throw new Error(
        `${where} carries no ${field}; a candidate a reader cannot identify is not a candidate`,
      );
    }
  }
  return candidate;
}

/**
 * The verdict for one citation against the records a caller found for it.
 *
 * The caller performs the lookup; this decides what the result means. Candidates are matched on
 * the publisher's full identifier, and a caller who supplies candidates whose identifiers differ
 * from the parsed one is refused rather than trusted, because that is the exact shape the work-key
 * collision would take.
 *
 * @param {object} input
 * @param {string} input.raw          the citation as pasted
 * @param {Array}  [input.candidates] held records the caller found, each `{lex_id, identifier, valid_from, valid_to}`
 */
export function checkCitation({ raw, candidates = [] }) {
  const parsed = parseCitation(raw);

  if (parsed === null) {
    const body = outOfCorpusBody(raw);
    return body === null
      ? { verdict: 'unrecognised', raw, note: UNRECOGNISED_NOTE }
      : { verdict: 'out_of_corpus', raw, body, note: OUT_OF_CORPUS_NOTE };
  }

  if (!Array.isArray(candidates)) {
    throw new Error('candidates must be an array, even when nothing was found');
  }
  candidates.forEach(requireCandidate);

  // The collision guard. A caller resolving on the work key rather than the publisher identifier
  // would hand back an `adm` record for a `leg` citation, and every field below would render
  // consistently around the wrong work. Refused rather than displayed.
  if (parsed.identifier !== undefined) {
    const foreign = candidates.filter((c) => c.identifier !== parsed.identifier);
    if (foreign.length > 0) {
      throw new Error(
        `a candidate for ${parsed.identifier} carries identifier ${foreign[0].identifier}; ` +
          'the Luxembourg work key drops the etat/leg and etat/adm segments, so a lookup keyed ' +
          'on it can answer an administrative citation with a legislative act',
      );
    }
  }

  if (candidates.length === 0) {
    return { verdict: 'out_of_corpus', raw, parsed, note: OUT_OF_CORPUS_NOTE };
  }
  if (candidates.length > 1) {
    return { verdict: 'ambiguous', raw, parsed, candidates, note: AMBIGUOUS_NOTE };
  }
  return { verdict: 'resolved', raw, parsed, state: candidates[0] };
}

/**
 * Whether a quoted passage is character-identical to the held text.
 *
 * Mechanical and nothing else. It reports the first differing offset so a reader can see where,
 * and it never characterises the difference, because saying a change is immaterial would be
 * applying law to facts.
 */
export function checkQuote({ quoted, held }) {
  for (const [name, value] of [
    ['quoted', quoted],
    ['held', held],
  ]) {
    if (typeof value !== 'string' || value.length === 0) {
      throw new Error(`checkQuote needs a ${name} passage; comparing against nothing is not a check`);
    }
  }
  if (quoted === held) {
    return { identical: true, at: null };
  }
  const shortest = Math.min(quoted.length, held.length);
  let at = shortest;
  for (let index = 0; index < shortest; index += 1) {
    if (quoted[index] !== held[index]) {
      at = index;
      break;
    }
  }
  return { identical: false, at };
}

function renderCandidate(candidate) {
  return (
    '<li class="check-candidate">' +
    `<code>${escapeHtml(candidate.lex_id)}</code> ` +
    `<span class="check-interval">${escapeHtml(candidate.valid_from)} to ` +
    `${escapeHtml(candidate.valid_to ?? 'open')}</span>` +
    '</li>'
  );
}

/**
 * One verdict card.
 *
 * @param {object} result what `checkCitation` returned
 */
export function renderVerdict(result) {
  const head =
    `<article class="check-card check-${escapeHtml(result.verdict)}">` +
    `<p class="check-raw"><code>${escapeHtml(result.raw)}</code></p>`;

  if (result.verdict === 'unrecognised') {
    return `${head}<p class="check-note">${escapeHtml(result.note)}</p></article>`;
  }
  if (result.verdict === 'out_of_corpus') {
    const link =
      result.body === undefined
        ? ''
        : `<a class="check-official" href="${escapeHtml(result.body.official)}" rel="external">` +
          `${escapeHtml(result.body.label)}, officially</a>`;
    return `${head}<p class="check-note">${escapeHtml(result.note)}</p>${link}</article>`;
  }
  if (result.verdict === 'ambiguous') {
    return (
      `${head}<p class="check-note">${escapeHtml(result.note)}</p>` +
      `<ul class="check-candidates">${result.candidates.map(renderCandidate).join('')}</ul>` +
      '</article>'
    );
  }
  return (
    `${head}<p class="check-resolved">Resolved to ` +
    `<code>${escapeHtml(result.state.lex_id)}</code>, applicable ` +
    `${escapeHtml(result.state.valid_from)} to ` +
    `${escapeHtml(result.state.valid_to ?? 'open')}.</p></article>`
  );
}
