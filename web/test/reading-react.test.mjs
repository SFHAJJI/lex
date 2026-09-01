// The reading view ported to React, measured against the string renderer.
//
// The claim being tested is not that React works. It is that both renderers apply the same
// rules and produce the same page, because a framework quietly becoming a second home for
// legal rules is the worst available outcome of adopting one. This screen composes eight of
// them, so it is the one where a second home would be most expensive.
//
// The strongest available form of that claim is byte equality, and it is what these tests
// assert. The two surfaces agree exactly except in how they spell one entity: `escapeHtml`
// writes an apostrophe as `&#39;` and React writes it as `&#x27;`. Both are the same
// character to every parser, so the comparison normalises that one spelling and nothing else.
// It deliberately does not decode `&lt;` or `&amp;`, because those distinguish escaped text
// from markup and a comparison that decoded them would call an injection equal to its escape.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { Reading } from '../.react-build/app.mjs';
import { ABSENCE_NOTE, SUPERSEDED_NOTE } from '../scripts/refusal-card.mjs';
import {
  CALLER_DECIDED_KEYS,
  FORBIDDEN_STATE_KEYS,
  NOT_AVAILABLE_NOTE,
  NO_LEGAL_EFFECT,
  RECONCILED_KEYS,
  WITHHELD_NOTE,
  renderReading,
} from '../scripts/reading.mjs';

const hex = (character) => character.repeat(64);

const STATE_HASH = hex('a');
const RECORD_SHA = hex('b');
const TEXT_SHA = hex('c');

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

/** A wording the publisher dates before the state carrying it: 39.8 percent of the corpus. */
const CONFLICTED = {
  anchor: 'art_l_121-6',
  num: 'Art. L. 121-6.',
  wording_valid_from: '2020-11-01',
  text_status: 'held',
  text: 'Le salarie incapable de travailler pour cause de maladie.',
  text_sha256: TEXT_SHA,
  renderings: [{ language: 'en', text: 'An employee unable to work through illness.' }],
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
  text_sha256: hex('d'),
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

// The same shapes under the other publisher. Luxembourg publishes applicability and the Union
// publishes consolidated wording states, so a page that spoke one vocabulary whatever record
// it held would pass every test written against one publisher.
const UNION_AUTHENTICITY = {
  ...AUTHENTICITY,
  resource_id: 'eu-eurlex:synthetic-eu-work/2021-01-26/fr',
  publisher: 'eu-eurlex',
  official_uri: 'https://eur-lex.europa.eu/synthetic/2021-01-26/fr',
};

const UNION = {
  state: {
    ...STATE,
    lex_id: 'eu-eurlex:synthetic-eu-work:2021-01-26',
    source_uri: 'https://eur-lex.europa.eu/synthetic/2021-01-26/fr',
  },
  expression: {
    resource_id: UNION_AUTHENTICITY.resource_id,
    language: 'fr',
    authenticity: UNION_AUTHENTICITY,
  },
};

/** The whole input, so a test can vary exactly one thing and nothing else. */
function reading(overrides = {}) {
  return {
    state: STATE,
    expression: EXPRESSION,
    provisions: [CONFLICTED],
    holes: [],
    asOf: '2021-03-15',
    ...overrides,
  };
}

const react = (props) => renderToStaticMarkup(h(Reading, props));

/** One entity spelling, normalised. See the note at the top of this file. */
const sameBytes = (props, why) =>
  assert.equal(
    react(props).replaceAll('&#x27;', '&#39;'),
    renderReading(props).replaceAll('&#x27;', '&#39;'),
    why,
  );

const LIVE = {
  valid_from: '2021-01-26',
  hash: hex('9'),
  publication_date: '2021-02-01',
  href: `/preview-synthetic/synthetic-work/2021-01-26--${hex('9')}`,
  withdrawn: false,
};

const GONE = {
  valid_from: '2021-01-26',
  hash: STATE_HASH,
  publication_date: '2021-01-26',
  href: `/preview-synthetic/synthetic-work/2021-01-26--${STATE_HASH}`,
  withdrawn: true,
};

// ---- the two surfaces are one page ------------------------------------------------------

test('both renderers produce the same page, in every shape this screen has', () => {
  // Each of these is a shape where a page of law says something the publisher did not, which
  // is why they are the five the preview builds. If React and the string surface disagree
  // anywhere, they disagree about a rule, because neither holds anything else.
  sameBytes(reading(), 'a wording the publisher dates before its state');
  sameBytes(
    reading({
      provisions: [WITHHELD, NOT_AVAILABLE],
      holes: [{ kind: 'no_state_held', from: '2004-04-02', to: '2024-12-28' }],
    }),
    'two absences that are not the same absence, and a gap',
  );
  sameBytes(
    reading({ provisions: [CONFLICTED, AGREEING], anchor: 'art_l121-6' }),
    'an anchor this version does not contain',
  );
  sameBytes(
    reading({
      state: {
        ...STATE,
        lex_id: 'preview-synthetic:synthetic-work:2030-09-15',
        valid_from: '2030-09-15',
        valid_to: null,
      },
      provisions: [{ ...AGREEING, wording_valid_from: '2030-09-15' }],
    }),
    'a state the publisher scheduled for a date that has not arrived',
  );
  sameBytes(
    reading({
      state: { ...STATE, withdrawn: true },
      provisions: [AGREEING],
      superseded: { live: LIVE, withdrawn: [GONE] },
    }),
    'a state the publisher withdrew and replaced',
  );
  sameBytes(
    reading({
      state: { ...STATE, consolidation_status: 'original_official_expression' },
      provisions: [AGREEING],
    }),
    "the publisher's own original expression, which is not a consolidation",
  );
  sameBytes(reading(UNION), 'a Union record, which speaks the Union publisher vocabulary');
});

test('the comparison would notice a page that lost a claim', () => {
  // A passing equality test is worth nothing if the two sides are equally empty. These pin
  // the comparison to a page that actually says the things this screen exists to say.
  const html = react(reading({ provisions: [CONFLICTED, WITHHELD, NOT_AVAILABLE] }));
  assert.ok(html.length > 4000, 'the compared page is too small to be the reading view');
  for (const claim of [
    'Applicable from 2021-01-26 to 2021-04-23 (publisher)',
    'Published 2021-01-26 / First observed 2026-08-14T23:05:14Z',
    'Read as of 2021-03-15.',
    'The publisher dates this wording 2020-11-01 inside a state applicable from 2021-01-26.',
    NO_LEGAL_EFFECT,
    WITHHELD_NOTE,
    NOT_AVAILABLE_NOTE,
    ABSENCE_NOTE,
    'Only the fr text is authentic (loi du 24 fevrier 1984, art. 2).',
  ]) {
    assert.ok(html.includes(claim), `the React page does not say: ${claim}`);
  }
});

// ---- the facts a caller may not state ---------------------------------------------------

test('a fact the record answers is refused from the caller in both renderers', () => {
  assert.deepEqual(
    [...CALLER_DECIDED_KEYS],
    ['language', 'provisional', 'consolidation', 'no_legal_effect', 'text_available'],
  );
  for (const key of CALLER_DECIDED_KEYS) {
    const props = reading({ [key]: true });
    assert.throws(() => react(props), /a fact taken from the caller/, `${key} was accepted`);
    assert.throws(() => renderReading(props), /a fact taken from the caller/);
  }
  // Passed as props rather than destructured away, which is what makes the refusal reachable
  // at all: a component that pulled out the keys it knows would silently drop these.
  for (const key of RECONCILED_KEYS) {
    const props = reading({ provisions: [{ ...CONFLICTED, [key]: '2021-01-26' }] });
    assert.throws(() => react(props), /ranked neither/, `${key} resolved the conflict`);
    assert.throws(() => renderReading(props), /ranked neither/);
  }
});

test('the date this page was read on is the reader own, stated and never defaulted', () => {
  // Today is the date a reader will not think to check, precisely because it is the one they
  // would have assumed. A hardcoded operative date answers a question about one day to
  // somebody who asked about another, and nothing on the page says which.
  assert.ok(react(reading({ asOf: '2021-03-15' })).includes('Read as of 2021-03-15.'));
  assert.ok(react(reading({ asOf: '2026-08-31' })).includes('Read as of 2026-08-31.'));

  for (const date of [undefined, null, '', 'today', '2021-02-30']) {
    const props = reading({ asOf: date });
    assert.throws(() => react(props), /the reading as-of date is not a calendar date/);
    assert.throws(() => renderReading(props), /the reading as-of date is not a calendar date/);
  }
});

test('the publisher status flag is refused on a state row by both renderers', () => {
  // The held GDPR state applicable from 2016-04-27 carries `in_force` while the regulation
  // did not apply until 2018-05-25, so the flag on a historical interval is simply false.
  for (const key of FORBIDDEN_STATE_KEYS) {
    const props = reading({ state: { ...STATE, [key]: 'in_force' } });
    assert.throws(() => react(props), /belongs in the dossier status strip/, `${key} passed`);
    assert.throws(() => renderReading(props), /belongs in the dossier status strip/);
  }
  assert.ok(!react(reading()).includes('in force'), '"in force" reached a state row');
});

test('the work and the vocabulary are the record\'s in the React runtime too', () => {
  // Neither is a prop this component needs, and one stated anyway must agree with the state's
  // own identifier. A page told a different work mints permalinks for a work it is not
  // showing, and every one of them resolves.
  const wrongWork = reading({ work: { publisher: 'preview-synthetic', work: 'another-work' } });
  assert.throws(() => react(wrongWork), /a work this page is not showing/);
  assert.throws(() => renderReading(wrongWork), /a work this page is not showing/);

  const wrongClock = reading({ envelope: { timeline_semantics: 'official_consolidation_state' } });
  assert.throws(() => react(wrongClock), /a property of the publisher/);
  assert.throws(() => renderReading(wrongClock), /a property of the publisher/);

  // With neither stated, the page still speaks this publisher's vocabulary, because it read
  // it off the record rather than waiting to be told, and it speaks the other publisher's
  // when the record is the other publisher's.
  assert.ok(react(reading()).includes('Applicable from 2021-01-26 to 2021-04-23 (publisher)'));
  assert.ok(!react(reading()).includes('Consolidated wording state'));

  const union = react(reading(UNION));
  assert.ok(union.includes('Consolidated wording state from 2021-01-26 to 2021-04-23'));
  assert.ok(!union.includes('Applicable from'), 'the LU vocabulary survived a Union record');
});

// ---- the rules this screen exists to keep -----------------------------------------------

test('the quotation carries the expression language, never the chrome locale', () => {
  const html = react(reading({ noteLocale: 'en' }));
  assert.ok(html.includes('<blockquote class="law" lang="fr">'), 'the quotation is not fr');
  assert.ok(html.includes('<p class="law-authenticity" lang="en">'), 'the note lost the chrome');

  // Bound to the record rather than to a constant: a German expression is quoted as German.
  const german = react(
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

test('every provision carries the hash-carrying permalink, and never a caller-built one', () => {
  const html = react(reading({ provisions: [CONFLICTED, WITHHELD, NOT_AVAILABLE] }));
  for (const provision of [CONFLICTED, WITHHELD, NOT_AVAILABLE]) {
    const expected =
      `/preview-synthetic/synthetic-work/2021-01-26--${STATE_HASH}#${provision.anchor}`;
    assert.ok(html.includes(`<a href="${expected}">Permalink</a>`), provision.anchor);
    // The fragment has to exist in the document it addresses, or the link lands the reader at
    // the top of the version and looks like it worked.
    assert.ok(html.includes(`<article class="reading-provision" id="${provision.anchor}">`));
  }
  // Bound to the state hash: a different state mints different links, which is the whole
  // reason the hash is in the URL.
  const other = react(reading({ state: { ...STATE, hash: hex('f') } }));
  assert.ok(other.includes(`2021-01-26--${hex('f')}#art_l_121-6`));
  assert.ok(!other.includes(STATE_HASH), 'the old state hash survived into the new links');

  for (const key of ['permalink', 'href', 'url']) {
    const props = reading({ provisions: [{ ...CONFLICTED, [key]: '/somewhere/else' }] });
    assert.throws(() => react(props), /a permalink is built here/, `${key} was accepted`);
    assert.throws(() => renderReading(props), /a permalink is built here/);
  }
});

test('the no-legal-effect sentence sits beside every block of text, not once at the top', () => {
  // A reader scrolling to the fourth article never saw a sentence printed once at the top,
  // and copying one article carried the text away from the only thing that qualified it.
  const html = react(reading({ provisions: [CONFLICTED, AGREEING] }));
  const blocks = html.split('<blockquote').length - 1;
  const sentences = html.split(NO_LEGAL_EFFECT).length - 1;
  assert.equal(blocks, 3, 'the fixture no longer has two quotations and one rendering');
  assert.equal(sentences, blocks, 'a block of text rendered without the sentence beside it');

  // And never against the publisher's own original expression, which is not a consolidation.
  const original = react(
    reading({ state: { ...STATE, consolidation_status: 'original_official_expression' } }),
  );
  assert.ok(!original.includes(NO_LEGAL_EFFECT), 'an original expression was called documentation');
});

test('a withdrawn state discloses the live one, and is never read as current', () => {
  const html = react(
    reading({
      state: { ...STATE, withdrawn: true },
      provisions: [AGREEING],
      superseded: { live: LIVE, withdrawn: [GONE] },
    }),
  );
  assert.ok(html.includes(SUPERSEDED_NOTE));
  assert.ok(html.includes(`href="${LIVE.href}"`), 'the live state is not reachable');

  const props = reading({ state: { ...STATE, withdrawn: true }, provisions: [AGREEING] });
  assert.throws(() => react(props), /the oldest failure in this product/);
  assert.throws(() => renderReading(props), /the oldest failure in this product/);
});

test('an absence is answered rather than left blank, and asserts nothing about the law', () => {
  // An empty pane is indistinguishable from a provision that says nothing, and this is the
  // one field whose whole job is to be read: a component tree renders a boolean false as
  // nothing at all, so it is text by the time it reaches either renderer.
  const html = react(reading({ provisions: [NOT_AVAILABLE] }));
  assert.ok(html.includes('<dt>asserts_absence_of_law</dt><dd>false</dd>'));
  assert.ok(html.includes(ABSENCE_NOTE));
  assert.ok(html.includes('<dd>Memorial A 2021 no 42</dd>'), 'the gazette chain was dropped');
});

test('an anchor this version does not contain refuses with the anchors it does', () => {
  const html = react(reading({ provisions: [CONFLICTED, AGREEING], anchor: 'art_l121-6' }));
  assert.ok(html.includes('reading-anchor-refused'));
  // The two clocks survive the refusal, so a reader can say which version did not contain it.
  assert.ok(html.includes('Applicable from 2021-01-26 to 2021-04-23 (publisher)'));
  for (const anchor of ['art_l_121-6', 'art_l_121-7']) {
    assert.ok(html.includes(`<code>${anchor}</code>`), `${anchor} was not offered back`);
  }
  assert.ok(!html.includes('<blockquote'), 'a refusal rendered a provision anyway');
});

test('the reading view ships inert markup from React too', () => {
  const html = react(
    reading({
      provisions: [CONFLICTED, WITHHELD, NOT_AVAILABLE],
      holes: [{ kind: 'no_state_held', from: '2004-04-02', to: '2024-12-28' }],
    }),
  );
  assert.ok(!/<script/i.test(html), 'the reading view shipped a script');
  assert.ok(!/\son[a-z]+\s*=/i.test(html), 'the reading view shipped an event handler');
  assert.ok(!/javascript:/i.test(html));
  // A refusal is an answer, not an alert. Announcing it as one is the aural equivalent of the
  // red error toast the spec rules out.
  assert.ok(!html.includes('role="alert"'));
  assert.ok(!html.includes('aria-live'));
});
