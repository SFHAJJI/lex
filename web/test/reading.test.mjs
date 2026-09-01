import assert from 'node:assert/strict';
import test from 'node:test';

import {
  ANCHOR_NOT_IN_VERSION_NOTE,
  CALLER_DECIDED_KEYS,
  CONSOLIDATION_STATUSES,
  FORBIDDEN_STATE_KEYS,
  NOT_AVAILABLE_NOTE,
  NO_LEGAL_EFFECT,
  RECONCILED_KEYS,
  TEXT_STATUSES,
  WITHHELD_NOTE,
  renderReading,
  unchangedSince,
} from '../scripts/reading.mjs';

const hex = (character) => character.repeat(64);

const STATE_HASH = hex('a');
const RECORD_SHA = hex('b');
const TEXT_SHA = hex('c');
const WITHHELD_SHA = hex('d');

const AUTHENTICITY = {
  schema: 'lex-v3-resource-authenticity/1',
  resource_id: 'preview-synthetic:synthetic-work/2021-01-26/fr',
  authentic_languages: ['fr'],
  basis: 'loi du 24 fevrier 1984, art. 2',
  asserted_by: 'the publisher',
  publisher: 'preview-synthetic',
  official_uri: 'https://preview.invalid/synthetic/2021-01-26/fr',
  observed_at: '2026-08-14T23:05:14Z',
};

const EXPRESSION = {
  resource_id: AUTHENTICITY.resource_id,
  language: 'fr',
  authenticity: AUTHENTICITY,
};

const STATE = {
  lex_id: 'preview-synthetic:synthetic-work:2021-01-26',
  valid_from: '2021-01-26',
  valid_to: '2021-04-23',
  publication_date: '2021-01-26',
  observed_from: '2026-08-14T23:05:14Z',
  hash: STATE_HASH,
  record_sha256: RECORD_SHA,
  source_uri: 'https://preview.invalid/synthetic/2021-01-26/fr',
  consolidation_status: 'published',
  withdrawn: false,
};

const HELD = {
  anchor: 'art_l_121-6',
  num: 'Art. L. 121-6.',
  wording_valid_from: '2020-11-01',
  text_status: 'held',
  text: 'Le salarie incapable de travailler pour cause de maladie.',
  text_sha256: TEXT_SHA,
};

const AGREEING = {
  anchor: 'art_l_121-7',
  num: 'Art. L. 121-7.',
  wording_valid_from: '2021-01-26',
  text_status: 'held',
  text: 'Le salarie avertit son employeur.',
  text_sha256: hex('e'),
};

const WITHHELD = {
  anchor: 'art_l_121-8',
  num: 'Art. L. 121-8.',
  wording_valid_from: '2021-01-26',
  text_status: 'withheld',
  licence: 'licenceSCL',
  digest_observed_at: '2026-08-14T23:05:14Z',
  text_sha256: WITHHELD_SHA,
  official_uri: 'https://preview.invalid/synthetic/2021-01-26/art_l_121-8',
};

const NOT_AVAILABLE = {
  anchor: 'art_l_121-9',
  num: 'Art. L. 121-9.',
  wording_valid_from: '2021-01-26',
  text_status: 'not_available',
  official_uri: 'https://preview.invalid/synthetic/2021-01-26/art_l_121-9',
  gazette_chain: 'Memorial A 2021 no 42',
};

/** The whole input, so a test can vary exactly one thing and nothing else. */
function reading(overrides = {}) {
  return {
    envelope: { timeline_semantics: 'publisher_applicability' },
    work: { publisher: 'preview-synthetic', work: 'synthetic-work' },
    state: STATE,
    expression: EXPRESSION,
    provisions: [HELD],
    holes: [],
    asOf: '2021-03-15',
    ...overrides,
  };
}

const occurrences = (html, needle) => html.split(needle).length - 1;

/** How the verify cluster prints a digest: first eight emphasised, the rest beside them. */
const digest = (value) =>
  `<span class="verify-hash-short">${value.slice(0, 8)}</span>${value.slice(8)}`;

// ---- the copy this screen ships -------------------------------------------------------
//
// Pinned to literals. A test asserting that the render contains a constant imported from the
// module under test asserts that the module agrees with itself, which it does even when the
// sentence is wrong, empty, or silently changed to something weaker.

test('the copy this screen ships is fixed and says what it means', () => {
  assert.equal(
    NO_LEGAL_EFFECT,
    'A consolidation is documentation and has no legal effect. The authentic text is the '
      + 'publisher file linked beside it.',
  );
  assert.equal(
    WITHHELD_NOTE,
    'The publisher licence for this file does not permit republishing its wording here. The '
      + 'digest identifies the exact bytes this corpus holds, and the official link leads to them.',
  );
  assert.equal(
    NOT_AVAILABLE_NOTE,
    'This corpus holds no text for this provision in this state. The official file and the '
      + 'gazette chain are below.',
  );
  assert.equal(
    ANCHOR_NOT_IN_VERSION_NOTE,
    'This version does not contain that provision. These are the provisions it does contain.',
  );
  assert.equal(unchangedSince('2020-11-01'), 'Wording unchanged since 2020-11-01.');
  assert.equal(unchangedSince('1999-12-31'), 'Wording unchanged since 1999-12-31.');

  assert.deepEqual([...CONSOLIDATION_STATUSES], ['published', 'original_official_expression']);
  assert.deepEqual([...TEXT_STATUSES], ['held', 'withheld', 'not_available']);
  assert.deepEqual([...FORBIDDEN_STATE_KEYS], ['binding_status', 'in_force', 'force_status']);
  assert.deepEqual(
    [...CALLER_DECIDED_KEYS],
    ['language', 'provisional', 'consolidation', 'no_legal_effect', 'text_available'],
  );
  assert.deepEqual(
    [...RECONCILED_KEYS],
    ['effective_valid_from', 'resolved_valid_from', 'conflict_resolved'],
  );
});

// ---- rule 1: the expression's own language ---------------------------------------------

test('publisher text is quoted in the expression language, never the chrome locale', () => {
  // The chrome is English and the law is French. The note follows the chrome; the quotation
  // follows the record. A hardcoded language would pass a test that only looked for `fr`.
  const html = renderReading(reading({ noteLocale: 'en' }));
  assert.ok(html.includes('<blockquote class="law" lang="fr">'), 'the quotation is not marked fr');
  assert.ok(
    html.includes('<p class="law-authenticity" lang="en">'),
    'the authenticity note did not follow the chrome locale',
  );

  // Bound to the record, not to a constant: a German expression is quoted as German.
  const german = renderReading(
    reading({
      expression: {
        resource_id: 'preview-synthetic:synthetic-work/2021-01-26/de',
        language: 'de',
        authenticity: {
          ...AUTHENTICITY,
          resource_id: 'preview-synthetic:synthetic-work/2021-01-26/de',
          authentic_languages: ['de'],
        },
      },
    }),
  );
  assert.ok(german.includes('<blockquote class="law" lang="de">'), 'the language is hardcoded');
  assert.ok(!german.includes('lang="fr"'), 'a French tag survived a German expression');
});

test('an expression that does not carry its own language cannot be quoted', () => {
  for (const language of [undefined, null, '', 'FR', 'fra', 'fr-LU', 7]) {
    assert.throws(
      () => renderReading(reading({ expression: { ...EXPRESSION, language } })),
      /the expression does not carry its own language/,
      `language=${JSON.stringify(language)} was quoted anyway`,
    );
  }
});

// ---- rule 2: two clocks, and never "in force" ------------------------------------------

test('the state shows its own two clocks and no row says in force', () => {
  const html = renderReading(reading());

  // Bound to this state's dates, in this publisher's vocabulary.
  assert.ok(html.includes('Applicable from 2021-01-26 to 2021-04-23 (publisher)'));
  assert.ok(html.includes('Published 2021-01-26 / First observed 2026-08-14T23:05:14Z'));
  assert.ok(!html.includes('in force'), 'the words "in force" reached a state row');

  // The other publisher's vocabulary, from the envelope rather than from this file.
  const union = renderReading(
    reading({ envelope: { timeline_semantics: 'official_consolidation_state' } }),
  );
  assert.ok(union.includes('Consolidated wording state from 2021-01-26 to 2021-04-23'));
  assert.ok(!union.includes('Applicable from'), 'the LU vocabulary survived an EU envelope');
  assert.ok(!union.includes('in force'));
});

test('a state row carrying the publisher status flag is refused, not merely ignored', () => {
  for (const key of ['binding_status', 'in_force', 'force_status']) {
    assert.throws(
      () => renderReading(reading({ state: { ...STATE, [key]: 'in_force' } })),
      /the publisher status flag is a statement about now/,
      `${key} was accepted on a state row`,
    );
  }
  // Even false, and even null: the key is refused, because a key that is accepted and
  // ignored is a key the next renderer prints.
  assert.throws(
    () => renderReading(reading({ state: { ...STATE, binding_status: null } })),
    /belongs in the dossier status strip/,
  );
});

// ---- rule 3: the hash-carrying permalink -----------------------------------------------

test('every provision carries the hash-carrying permalink built from the state', () => {
  const html = renderReading(reading({ provisions: [HELD, WITHHELD, NOT_AVAILABLE] }));

  for (const provision of [HELD, WITHHELD, NOT_AVAILABLE]) {
    const expected =
      `/preview-synthetic/synthetic-work/2021-01-26--${STATE_HASH}#${provision.anchor}`;
    assert.ok(
      html.includes(`<a href="${expected}">Permalink</a>`),
      `${provision.anchor} has no permalink at ${expected}`,
    );
    // The fragment has to exist in the document it addresses. A permalink whose anchor is
    // nowhere on the page lands the reader at the top of the version and looks like it worked.
    assert.ok(
      html.includes(`<article class="reading-provision" id="${provision.anchor}">`),
      `${provision.anchor} is not an id on this page, so its own permalink misses it`,
    );
  }

  // Bound to the state hash: a different state mints different links, which is the whole
  // reason the hash is in the URL.
  const other = renderReading(reading({ state: { ...STATE, hash: hex('f') } }));
  assert.ok(other.includes(`2021-01-26--${hex('f')}#art_l_121-6`));
  assert.ok(!other.includes(STATE_HASH), 'the old state hash survived into the new links');
});

test('a permalink the caller brought is refused rather than preferred', () => {
  for (const key of ['permalink', 'href', 'url']) {
    assert.throws(
      () => renderReading(reading({ provisions: [{ ...HELD, [key]: '/somewhere/else' }] })),
      /a permalink is built here from the state hash and the publisher anchor/,
      `${key} was accepted from the caller`,
    );
  }
});

test('a provision with no publisher anchor has no address, so it is refused', () => {
  assert.throws(
    () => renderReading(reading({ provisions: [{ ...HELD, anchor: undefined }] })),
    /carries no publisher anchor/,
  );
  assert.throws(
    () => renderReading(reading({ provisions: [{ ...HELD, anchor: '' }] })),
    /carries no publisher anchor/,
  );
});

test('two provisions at one anchor make one permalink resolve to two texts', () => {
  assert.throws(
    () =>
      renderReading(
        reading({ provisions: [HELD, { ...AGREEING, anchor: 'art_l_121-6' }] }),
      ),
    /carries the anchor art_l_121-6 twice/,
  );
});

test('a provision that cannot be cited by number is refused', () => {
  assert.throws(
    () => renderReading(reading({ provisions: [{ ...HELD, num: '  ' }] })),
    /carries no publisher number/,
  );
});

// ---- rule 4: two publisher dates, both shown, neither resolved -------------------------

test('a wording date differing from the state renders both dates and resolves neither', () => {
  const html = renderReading(reading());
  assert.ok(
    html.includes(
      'The publisher dates this wording 2020-11-01 inside a state applicable from 2021-01-26.',
    ),
    'the conflict does not name both dates',
  );
  assert.ok(html.includes('Not resolved.'));
  assert.ok(!html.includes('Wording unchanged since'), 'a conflict rendered as agreement');

  // The other branch, bound to the same two dates.
  const agreeing = renderReading(reading({ provisions: [AGREEING] }));
  assert.ok(agreeing.includes('Wording unchanged since 2021-01-26.'));
  assert.ok(!agreeing.includes('Not resolved.'), 'agreeing dates rendered as a conflict');
});

test('a wording date is required, because defaulting it reconciles the conflict silently', () => {
  for (const wording of [undefined, null, '', '2020-13-01']) {
    assert.throws(
      () => renderReading(reading({ provisions: [{ ...HELD, wording_valid_from: wording }] })),
      /wording_valid_from is not a calendar date/,
      `wording_valid_from=${JSON.stringify(wording)} was allowed to default`,
    );
  }
});

test('a date derived from the two publisher dates is refused', () => {
  for (const key of ['effective_valid_from', 'resolved_valid_from', 'conflict_resolved']) {
    assert.throws(
      () => renderReading(reading({ provisions: [{ ...HELD, [key]: '2021-01-26' }] })),
      /the publisher recorded two dates and ranked neither/,
      `${key} was accepted`,
    );
  }
});

test('a conflict flag that disagrees with the dates it summarises is refused', () => {
  assert.throws(
    () => renderReading(reading({ provisions: [{ ...HELD, validity_conflict: false }] })),
    /a flag that disagrees with the dates it summarises/,
  );
  assert.throws(
    () => renderReading(reading({ provisions: [{ ...AGREEING, validity_conflict: true }] })),
    /a flag that disagrees with the dates it summarises/,
  );
  // Agreeing with the dates, it changes nothing: the dates are what render.
  const html = renderReading(reading({ provisions: [{ ...HELD, validity_conflict: true }] }));
  assert.ok(html.includes('Not resolved.'));
});

// ---- rule 5: withheld text is a digest and a link, never a blank and never a guess ------

test('text withheld by licence renders its digest and the official link, never the text', () => {
  const html = renderReading(reading({ provisions: [WITHHELD] }));

  assert.ok(html.includes('<code class="refusal-code">text_withheld</code>'));
  assert.ok(
    html.includes(
      'The publisher licence for this file does not permit republishing its wording here.',
    ),
    'the withheld sentence is not on the page',
  );
  assert.ok(html.includes('<dd>licenceSCL</dd>'), 'the licence is not named');
  assert.ok(html.includes('<dd>2026-08-14T23:05:14Z</dd>'), 'the digest has no observation time');

  // The digest, whole, and the publisher's own address for the file it names.
  assert.ok(html.includes(digest(WITHHELD_SHA)), 'the digest is not on the page');
  assert.ok(
    html.includes(
      '<a class="verify-source" href="https://preview.invalid/synthetic/2021-01-26/art_l_121-8"',
    ),
    'the official link is not bound to this provision',
  );
  assert.ok(!html.includes('<blockquote class="law"'), 'withheld text was quoted as law');
});

test('a withheld provision carrying its text is refused, licence or not', () => {
  assert.throws(
    () =>
      renderReading(reading({ provisions: [{ ...WITHHELD, text: 'Le texte retenu.' }] })),
    /text that may not be republished may not be on the page/,
  );
  // Even empty: the key is the claim, and a caller who sets it has decided to send it.
  assert.throws(
    () => renderReading(reading({ provisions: [{ ...WITHHELD, text: '' }] })),
    /text that may not be republished may not be on the page/,
  );
});

test('a withheld provision names the licence and dates its digest', () => {
  const { licence, ...noLicence } = WITHHELD;
  assert.equal(licence, 'licenceSCL');
  assert.throws(
    () => renderReading(reading({ provisions: [noLicence] })),
    /does not name the licence withholding it/,
  );

  for (const instant of [undefined, '2026-08-14', 'yesterday', '2026-08-14T23:05:14+02:00']) {
    assert.throws(
      () =>
        renderReading(reading({ provisions: [{ ...WITHHELD, digest_observed_at: instant }] })),
      /digest_observed_at is not a UTC instant/,
      `digest_observed_at=${JSON.stringify(instant)} passed`,
    );
  }
});

test('a provision with no held text says so, with the official file and the gazette chain', () => {
  const html = renderReading(reading({ provisions: [NOT_AVAILABLE] }));
  assert.ok(html.includes('<code class="refusal-code">text_not_available</code>'));
  assert.ok(html.includes('This corpus holds no text for this provision in this state.'));
  assert.ok(html.includes('<dd>Memorial A 2021 no 42</dd>'), 'the gazette chain is missing');
  assert.ok(
    html.includes('<dd>https://preview.invalid/synthetic/2021-01-26/art_l_121-9</dd>'),
    'the official file is missing',
  );
  assert.ok(
    html.includes('It is not evidence that the instrument or the law does not exist.'),
    'an absence of text read as an absence of law',
  );
});

test('a cause of missing text that nobody named is refused', () => {
  for (const status of [undefined, 'missing', 'held_but_empty', 'TEXT_WITHHELD']) {
    assert.throws(
      () => renderReading(reading({ provisions: [{ ...HELD, text_status: status }] })),
      /the set is closed at held, withheld, not_available/,
      `text_status=${JSON.stringify(status)} was rendered`,
    );
  }
});

// ---- rule 6: an anchor the version does not have ---------------------------------------

test('an anchor the version does not have is a refusal offering the anchors it does', () => {
  const html = renderReading(
    reading({ provisions: [HELD, AGREEING, WITHHELD], anchor: 'art_l121-6' }),
  );

  assert.ok(html.includes('<code class="refusal-code">anchor_not_in_version</code>'));
  assert.ok(
    html.includes('This version does not contain that provision. These are the provisions it does contain.'),
  );
  // The anchors this version does have, all of them, verbatim.
  for (const anchor of ['art_l_121-6', 'art_l_121-7', 'art_l_121-8']) {
    assert.ok(html.includes(`<li><code>${anchor}</code></li>`), `${anchor} was not offered`);
  }
  // The contract rule, written by the card: a different provision is not a near miss.
  assert.ok(html.includes('Lex does not fall back to full-text search'));
  // Never an empty page, and never the text of a provision nobody asked for.
  assert.ok(!html.includes('Le salarie incapable de travailler'), 'the wrong text was served');
  assert.ok(html.includes('Applicable from 2021-01-26'), 'the refusal dropped the two clocks');
});

test('an anchor the version does have selects exactly that provision', () => {
  const html = renderReading(
    reading({ provisions: [HELD, AGREEING, WITHHELD], anchor: 'art_l_121-7' }),
  );
  assert.ok(html.includes('Le salarie avertit son employeur.'));
  assert.ok(!html.includes('Le salarie incapable de travailler'), 'a provision nobody asked for');
  assert.ok(!html.includes('text_withheld'), 'a provision nobody asked for');
  assert.equal(occurrences(html, '<article class="reading-provision"'), 1);
});

test('a version with no provisions is refused rather than rendered as an empty page', () => {
  for (const provisions of [[], undefined, null, 'art_l_121-6']) {
    assert.throws(
      () => renderReading(reading({ provisions })),
      /an empty text pane is indistinguishable from a version whose provisions say nothing/,
      `provisions=${JSON.stringify(provisions)} rendered`,
    );
  }
});

// ---- rule 7: the no-legal-effect sentence, wherever text renders ------------------------

test('a consolidation carries the no-legal-effect sentence beside every text it renders', () => {
  const sentence =
    'A consolidation is documentation and has no legal effect. The authentic text is the '
    + 'publisher file linked beside it.';

  const one = renderReading(reading());
  assert.equal(occurrences(one, sentence), 1, 'one quoted provision, one sentence');

  // Two provisions and one unofficial rendering are three blocks of text, so three
  // sentences. Once at the top of the page is not "wherever its text renders": a reader
  // scrolling to the second article would never see it.
  const three = renderReading(
    reading({
      provisions: [
        { ...HELD, renderings: [{ language: 'en', text: 'An employee unable to work.' }] },
        AGREEING,
      ],
    }),
  );
  assert.equal(occurrences(three, sentence), 3, 'the sentence did not follow every text block');

  // Withheld and unavailable text does not render, so the sentence about it does not either.
  const none = renderReading(reading({ provisions: [WITHHELD, NOT_AVAILABLE] }));
  assert.equal(occurrences(none, sentence), 0);
});

test('the publisher own original expression does not carry a sentence about consolidations', () => {
  const html = renderReading(
    reading({ state: { ...STATE, consolidation_status: 'original_official_expression' } }),
  );
  assert.ok(html.includes('<blockquote class="law" lang="fr">'), 'the text did not render');
  assert.ok(
    !html.includes('A consolidation is documentation and has no legal effect.'),
    'an original official expression was labelled a consolidation',
  );
});

test('a consolidation status nobody named cannot be read either way', () => {
  for (const status of [undefined, null, 'consolidated', 'published_consolidation', true]) {
    assert.throws(
      () => renderReading(reading({ state: { ...STATE, consolidation_status: status } })),
      /is not one of published, original_official_expression/,
      `consolidation_status=${JSON.stringify(status)} was rendered`,
    );
  }
});

// ---- rule 8: a record answers, not a caller --------------------------------------------

test('a caller may not assert what a record on this page answers', () => {
  for (const key of CALLER_DECIDED_KEYS) {
    assert.throws(
      () => renderReading(reading({ [key]: true })),
      /every one of those is answered by a record on this page/,
      `${key} was accepted from the caller`,
    );
  }
  // Named individually, so the list cannot shrink without a test noticing.
  for (const key of ['language', 'provisional', 'consolidation', 'no_legal_effect', 'text_available']) {
    assert.throws(() => renderReading(reading({ [key]: false })), /a fact about the caller/);
  }
});

test('a future state is provisional because of its dates, not because of a flag', () => {
  const scheduled = renderReading(
    reading({
      state: { ...STATE, valid_from: '2030-09-15', valid_to: null },
      provisions: [{ ...HELD, wording_valid_from: '2030-09-15' }],
      asOf: '2026-08-31',
    }),
  );
  assert.ok(
    scheduled.includes(
      'Publisher-scheduled state, applicable from 2030-09-15. As of 2026-08-31 it has not begun.',
    ),
    'a future state rendered as current law',
  );

  // The same state read from a date inside it is not provisional.
  const begun = renderReading(
    reading({
      state: { ...STATE, valid_from: '2030-09-15', valid_to: null },
      provisions: [{ ...HELD, wording_valid_from: '2030-09-15' }],
      asOf: '2030-10-01',
    }),
  );
  assert.ok(!begun.includes('Publisher-scheduled state'), 'a begun state was watermarked');
});

test('an unofficial rendering takes its official route from the evidence, not the caller', () => {
  const html = renderReading(
    reading({
      provisions: [{ ...HELD, renderings: [{ language: 'en', text: 'An employee.' }] }],
    }),
  );
  assert.ok(html.includes('<blockquote class="body" lang="en">An employee.</blockquote>'));
  assert.ok(html.includes('This rendering is excluded from evidence exports.'));
  assert.ok(
    html.includes('href="https://preview.invalid/synthetic/2021-01-26/fr" rel="external">The authentic text'),
  );

  for (const key of ['publisher', 'official_uri', 'officialUri']) {
    assert.throws(
      () =>
        renderReading(
          reading({
            provisions: [
              {
                ...HELD,
                renderings: [
                  { language: 'en', text: 'An employee.', [key]: 'https://preview.invalid/other' },
                ],
              },
            ],
          }),
        ),
      /the authentic route is the resource evidence/,
      `a rendering supplied its own ${key}`,
    );
  }
});

// ---- the operative date, the gaps, and the withdrawn state ------------------------------

test('the date this page was read on is stated and never defaulted', () => {
  assert.ok(renderReading(reading({ asOf: '2021-03-15' })).includes('Read as of 2021-03-15.'));
  assert.ok(renderReading(reading({ asOf: '2026-08-31' })).includes('Read as of 2026-08-31.'));

  for (const date of [undefined, null, '', 'today', '2021-02-30']) {
    assert.throws(
      () => renderReading(reading({ asOf: date })),
      /the reading as-of date is not a calendar date/,
      `asOf=${JSON.stringify(date)} was allowed`,
    );
  }
});

test('the gaps around this state are declared, even when there are none', () => {
  const html = renderReading(
    reading({ holes: [{ kind: 'no_state_held', from: '2004-04-02', to: '2024-12-28' }] }),
  );
  assert.ok(html.includes('This corpus holds no state covering 2004-04-02 to 2024-12-28'));

  for (const holes of [undefined, null, {}, 'none']) {
    assert.throws(
      () => renderReading(reading({ holes })),
      /a page that is silent about gaps reads as a page with none/,
      `holes=${JSON.stringify(holes)} was accepted`,
    );
  }
});

test('a withdrawn state discloses the live state that replaced it', () => {
  const live = {
    valid_from: '2021-01-26',
    hash: hex('9'),
    publication_date: '2021-02-01',
    href: `/preview-synthetic/synthetic-work/2021-01-26--${hex('9')}`,
    withdrawn: false,
  };
  const gone = {
    valid_from: '2021-01-26',
    hash: STATE_HASH,
    publication_date: '2021-01-26',
    href: `/preview-synthetic/synthetic-work/2021-01-26--${STATE_HASH}`,
    withdrawn: true,
  };

  const html = renderReading(
    reading({
      state: { ...STATE, withdrawn: true },
      superseded: { live, withdrawn: [gone] },
    }),
  );
  assert.ok(html.includes('The publisher withdrew the state below and replaced it.'));
  assert.ok(html.includes(`href="${live.href}"`), 'the live state is not reachable');

  assert.throws(
    () => renderReading(reading({ state: { ...STATE, withdrawn: true } })),
    /a withdrawn state read as current is the oldest failure in this product/,
  );
});

test('a state that does not say whether it was withdrawn cannot be read as live', () => {
  for (const withdrawn of [undefined, null, 'false', 0]) {
    const { withdrawn: _drop, ...bare } = STATE;
    assert.throws(
      () =>
        renderReading(
          reading({
            state: withdrawn === undefined ? bare : { ...STATE, withdrawn },
          }),
        ),
      /a withdrawn state renders exactly like a live one/,
      `withdrawn=${JSON.stringify(withdrawn)} was read as a live state`,
    );
  }
});

// ---- verification ----------------------------------------------------------------------

test('the state and each held provision carry the digest that answers their own question', () => {
  const html = renderReading(reading({ provisions: [HELD] }));

  assert.ok(html.includes('<span class="verify-hash-kind">record_sha256</span>'));
  assert.ok(html.includes(digest(RECORD_SHA)), 'the state digest is not on the page');
  assert.ok(html.includes('<span class="verify-hash-kind">text_sha256</span>'));
  assert.ok(html.includes(digest(TEXT_SHA)), 'the provision digest is not on the page');

  // Bound to the record: a different digest is a different page.
  const other = renderReading(
    reading({ provisions: [{ ...HELD, text_sha256: hex('7') }] }),
  );
  assert.ok(other.includes(digest(hex('7'))));
  assert.ok(!other.includes(digest(TEXT_SHA)), 'the old provision digest survived');
});

test('an official source that is not the publisher own host is refused', () => {
  assert.throws(
    () => renderReading(reading({ state: { ...STATE, source_uri: 'https://evil.example/fake' } })),
    /is not one of preview\.invalid/,
  );
  assert.throws(
    () =>
      renderReading(
        reading({ provisions: [{ ...WITHHELD, official_uri: 'https://evil.example/fake' }] }),
      ),
    /is not one of preview\.invalid/,
  );
});

// ---- the page ships inert -------------------------------------------------------------

test('the reading view ships inert HTML, with no script and no event handler', () => {
  const html = renderReading(
    reading({ provisions: [HELD, WITHHELD, NOT_AVAILABLE], holes: [{ kind: 'no_state_held', from: '2004-04-02', to: '2024-12-28' }] }),
  );
  assert.ok(!/<script/i.test(html), 'the reading view shipped a script');
  assert.ok(!/\son[a-z]+\s*=/i.test(html), 'the reading view shipped an event handler');
  assert.ok(!/javascript:/i.test(html));
});
