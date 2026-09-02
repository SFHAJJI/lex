// Coverage ported to React, measured against the string renderer rather than asserted.
//
// The claim under test is not that React works. It is that both renderers apply the same rules
// and refuse the same inputs, because a framework quietly becoming a second home for legal rules
// is the worst available outcome of adopting one.
//
// One rule is stronger in the React port, and it is the reason most of this file exists. A facet
// table is a breakdown of a headline number, and the two tables on this page reconcile with
// their headline in two different ways. Document types PARTITION: the publisher gives a state at
// most one type and the untyped row takes the rest, so a complete table must sum to the headline
// exactly. Languages OVERLAP: a state exists as an expression in each language it was published
// in, so the rows may sum far past the headline and only the per-row bound holds.
//
// That is not a modelling preference. It is measured: Luxembourg's live language rows sum to
// 1,406 works against 1,402 held, and the Union's to 4,652 versions against 2,366. A single
// partition rule applied to both tables would make both live coverage pages refuse to render,
// so the two live shapes are fixtures here and the test that keeps them rendering is the point.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { Coverage, UNCODED_LANGUAGE_LABEL } from '../.react-build/app.mjs';
import { RETENTION_SENTENCE, UNTYPED_LABEL, renderCoverage } from '../scripts/coverage.mjs';

const BUILT_AT = '2026-08-15T09:22:08Z';

const COMPLETE = {
  envelope: { freshness: { built_at: BUILT_AT, stamp_signature_valid: true } },
  publisher_name: 'Synthetic preview publisher',
  works: 40,
  scope_expected_works: 40,
  build_inventory_status: 'complete',
  build_complete: true,
  build_issues: [],
  versions: 120,
  valid_from_earliest: '1849-03-14',
  valid_from_latest: '2030-09-15',
  document_types: [
    { code: 'LOI', versions: 52, versions_with_text: 51 },
    { code: 'RGD', versions: 30, versions_with_text: 30 },
    { code: 'RECUEIL', versions: 25, versions_with_text: 3 },
    { code: null, versions: 13, versions_with_text: 0 },
  ],
  document_types_total: 4,
  facets_truncated: false,
  languages: [
    { code: 'fr', works: 40, versions: 120 },
    { code: 'de', works: 1, versions: 1 },
  ],
  text: { versions_with_text_served: 84, versions_without_text: 36 },
  known_gaps: [
    'never-consolidated acts are not ingested; the reviewed corpus is dated consolidations only',
    'coverage density follows the publisher own digitised consolidations',
  ],
};

const INCOMPLETE = {
  ...COMPLETE,
  build_inventory_status: 'partial',
  build_complete: false,
  build_issues: ['one publisher endpoint did not respond', 'one manifest failed verification'],
};

/**
 * Luxembourg, in the proportions the live build reports.
 *
 * The language rows sum to 1,406 works against 1,402 held, because a work published in French
 * and in German is one work counted in two rows. Under a partition rule this page refuses.
 */
const LUXEMBOURG = {
  ...COMPLETE,
  publisher_name: 'Luxembourg, Legilux',
  works: 1402,
  scope_expected_works: 1402,
  versions: 3000,
  document_types: [
    { code: 'LOI', versions: 1800, versions_with_text: 1200 },
    { code: 'RGD', versions: 1000, versions_with_text: 900 },
    { code: null, versions: 200, versions_with_text: 0 },
  ],
  document_types_total: 3,
  languages: [
    { code: 'fr', works: 1400, versions: 2990 },
    { code: 'de', works: 4, versions: 8 },
    { code: 'en', works: 2, versions: 2 },
  ],
  text: { versions_with_text_served: 2100, versions_without_text: 900 },
};

/**
 * The Union, in the proportions the live build reports.
 *
 * The language rows sum to 4,652 versions against 2,366 held, because a consolidated state is
 * published as an expression in every official language it exists in.
 */
const UNION = {
  ...COMPLETE,
  publisher_name: 'European Union, EUR-Lex',
  works: 800,
  scope_expected_works: 800,
  versions: 2366,
  document_types: [
    { code: 'REG', versions: 1366, versions_with_text: 1000 },
    { code: 'DIR', versions: 1000, versions_with_text: 800 },
  ],
  document_types_total: 2,
  languages: [
    { code: 'en', works: 790, versions: 2300 },
    { code: 'fr', works: 780, versions: 2352 },
  ],
  text: { versions_with_text_served: 1800, versions_without_text: 566 },
};

const react = (coverage) => renderToStaticMarkup(h(Coverage, { coverage }));
const string = (coverage) => renderCoverage({ coverage });

/** The same normalisation the timeline port uses, and for the same two reasons. */
function normalise(html) {
  return html
    .replaceAll('&#x27;', "'")
    .replaceAll('&#39;', "'")
    .replaceAll('colSpan=', 'colspan=');
}

test('both renderers draw the same coverage page, finished and unfinished', () => {
  for (const [name, coverage] of Object.entries({ COMPLETE, INCOMPLETE, LUXEMBOURG, UNION })) {
    assert.equal(
      normalise(react(coverage)),
      normalise(string(coverage)),
      `${name}: the React port and the string renderer disagree`,
    );
  }
  // Non-trivial: an empty baseline agrees with an empty baseline forever.
  assert.ok(react(COMPLETE).includes(RETENTION_SENTENCE));
  assert.ok(react(COMPLETE).length > 900, 'the React coverage page rendered almost nothing');
});

test('language rows may sum past the headline, because a state exists in each language', () => {
  // The regression this guards is a page that refuses to render. Luxembourg's own language rows
  // sum to 1,406 works against 1,402 held and the Union's to 4,652 versions against 2,366; both
  // are correct, and a partition rule applied here would delete both live pages.
  const luWorks = LUXEMBOURG.languages.reduce((sum, row) => sum + row.works, 0);
  assert.equal(luWorks, 1406);
  assert.ok(luWorks > LUXEMBOURG.works);
  assert.ok(react(LUXEMBOURG).includes('1402 works, 3000 dated states'));

  const euVersions = UNION.languages.reduce((sum, row) => sum + row.versions, 0);
  assert.equal(euVersions, 4652);
  assert.ok(euVersions > UNION.versions);
  assert.ok(react(UNION).includes('800 works, 2366 dated states'));
});

test('no language row may count more than the whole it is drawn from', () => {
  // The bound that does hold for an overlapping breakdown. Without it the page rendered a
  // language claiming 9,999 of 30 held states, because every check tested one field at a time.
  const tooManyVersions = {
    ...COMPLETE,
    languages: [...COMPLETE.languages, { code: 'lb', works: 3, versions: 9999 }],
  };
  assert.throws(() => react(tooManyVersions), /the language breakdown row 3 counts 9999 versions against a total of 120/);
  assert.throws(() => react(tooManyVersions), /a part cannot be larger than the whole/);

  const tooManyWorks = {
    ...COMPLETE,
    languages: [...COMPLETE.languages, { code: 'lb', works: 41, versions: 3 }],
  };
  assert.throws(() => react(tooManyWorks), /the language breakdown row 3 counts 41 works against a total of 40/);

  // Both renderers refuse it. When this test was written the string renderer had no facet
  // reconciliation and rendered the impossible table, so this line recorded the divergence. The
  // string renderer gained the same rule while this port was in flight, which is the outcome
  // worth having: one rule, two surfaces, and no divergence for a later reader to discover.
  assert.throws(() => string(tooManyVersions), /a part cannot be larger than the whole/);
  assert.throws(() => string(tooManyWorks), /a part cannot be larger than the whole/);
});

test('a complete document type table must account for every state exactly once', () => {
  // A partition. The publisher gives a state at most one type and the untyped row takes the
  // rest, so a table showing all its rows and not summing to the headline means one of the two
  // numbers was measured against something else.
  const short = {
    ...COMPLETE,
    document_types: COMPLETE.document_types.slice(0, 3),
    document_types_total: 3,
  };
  assert.throws(
    () => react(short),
    /the document type breakdown accounts for 107 versions against a total of 120, and every row is shown/,
  );

  const over = {
    ...COMPLETE,
    document_types: [...COMPLETE.document_types, { code: 'AUTRE', versions: 5, versions_with_text: 0 }],
    document_types_total: 5,
  };
  assert.throws(() => react(over), /accounts for 125 versions against a total of 120/);
});

test('a truncated type table need not sum, and still may not exceed', () => {
  // Truncation weakens the partition rule and only that rule. A table showing some of its rows
  // cannot be expected to account for the whole; no row of it can be larger than the whole.
  const truncated = {
    ...COMPLETE,
    document_types: COMPLETE.document_types.slice(0, 2),
    document_types_total: 7,
    facets_truncated: true,
  };
  assert.ok(react(truncated).includes('Showing 2 of 7 types.'));
  assert.equal(normalise(react(truncated)), normalise(string(truncated)));

  const oversized = {
    ...truncated,
    document_types: [{ code: 'LOI', versions: 999, versions_with_text: 0 }],
  };
  assert.throws(() => react(oversized), /the document type breakdown row 1 counts 999 versions against a total of 120/);
});

test('a facet row proves itself a row before any arithmetic is done about it', () => {
  // Ordered deliberately: a malformed row reports itself in its own terms rather than as an
  // arithmetic disagreement, which is the difference between a message a reader can act on and
  // one that sends them to count columns.
  const malformed = {
    ...COMPLETE,
    document_types: [{ code: 'LOI', versions: 52 }, ...COMPLETE.document_types.slice(1)],
  };
  assert.throws(() => react(malformed), /document type row 1 carries a versions count with no versions_with_text/);
  assert.throws(() => string(malformed), /document type row 1 carries a versions count with no versions_with_text/);
});

test('a row the publisher gave no code for is labelled, never blank and never dropped', () => {
  // Dropping it would remove exactly the states most likely to be missing their text. React
  // renders null as nothing, so an unguarded cell would simply be empty, and an empty cell in a
  // column of codes reads as a code rather than as this service failing to say.
  const html = react(COMPLETE);
  assert.ok(html.includes(`<td>${UNTYPED_LABEL}</td>`));
  assert.ok(html.includes('<td>13</td>'), 'the untyped row lost its count');

  const uncoded = {
    ...COMPLETE,
    languages: [{ code: 'fr', works: 40, versions: 120 }, { works: 1, versions: 1 }],
  };
  assert.ok(react(uncoded).includes(`<td>${UNCODED_LANGUAGE_LABEL}</td>`));
  assert.ok(!react(uncoded).includes('<td></td>'), 'a language cell rendered blank');
  // The same, on both surfaces. This line recorded the string renderer interpolating the missing
  // code raw and printing the literal word `undefined` into a column of language codes. It does
  // not any more, so the assertion is that it agrees rather than that it differs.
  assert.ok(string(uncoded).includes(`<td>${UNCODED_LANGUAGE_LABEL}</td>`));
  assert.ok(!string(uncoded).includes('<td>undefined</td>'), 'undefined reached a language column');
});

test('a build that did not finish shows no counts at all', () => {
  // A build that did not finish is not a smaller corpus, it is an unknown one, and its figures
  // would read as measurements of what is held.
  const html = react(INCOMPLETE);
  assert.ok(html.includes('coverage-incomplete'));
  assert.ok(html.includes('This index build did not complete'));
  assert.ok(html.includes('2 recorded issues'));
  assert.ok(!html.includes('coverage-held'), 'an unfinished build published its counts');
  assert.ok(!html.includes('120 dated states'));
  assert.ok(!html.includes('coverage-table'), 'an unfinished build published its facet tables');
  // One issue is singular, so the sentence is not written for the plural case alone.
  assert.ok(react({ ...INCOMPLETE, build_issues: ['one'] }).includes('1 recorded issue,'));
});

test('every guard refuses the same input in both renderers', () => {
  // The two implementations are separate, because `scripts/coverage.mjs` exports no validator.
  // This is what holds them together: if either drifts, one of these stops throwing.
  const cases = [
    [{ ...COMPLETE, envelope: { freshness: {} } }, /carries the instant its counts were measured/],
    [{ ...COMPLETE, envelope: { freshness: { built_at: '2026-08-15' } } }, /the instant its counts/],
    [{ ...COMPLETE, publisher_name: '' }, /names the publisher it describes/],
    [{ ...COMPLETE, works: -1 }, /works is -1 rather than a count/],
    [{ ...COMPLETE, versions: 1.5 }, /versions is 1.5 rather than a count/],
    [{ ...COMPLETE, text: { versions_with_text_served: 84, versions_without_text: 1 } }, /do not add up/],
    [{ ...COMPLETE, valid_from_earliest: 'long ago' }, /valid_from_earliest is not a calendar date/],
    [{ ...COMPLETE, known_gaps: [] }, /is a claim of completeness/],
    [{ ...COMPLETE, known_gaps: ['ok', '  '] }, /every known gap is a sentence/],
    [{ ...COMPLETE, document_types: [] }, /lists the document types it holds/],
    [{ ...COMPLETE, document_types_total: null }, /document_types_total is null rather than a count/],
    [{ ...COMPLETE, document_types_total: -7, facets_truncated: true }, /rather than a count/],
    [{ ...COMPLETE, document_types_total: 9 }, /reads as a complete one/],
    [{ ...COMPLETE, scope_expected_works: 41 }, /the build expected 41 works and holds 40/],
    [
      { ...COMPLETE, document_types: [{ code: 'LOI', versions: 120, versions_with_text: 121 }], document_types_total: 1 },
      /holds text for more states than it holds/,
    ],
    [{ ...COMPLETE, languages: [{ code: 'fr', works: -2, versions: 1 }] }, /language row 1 works is -2/],
  ];
  for (const [coverage, pattern] of cases) {
    assert.throws(() => react(coverage), pattern, `the React port accepted ${pattern}`);
    assert.throws(() => string(coverage), pattern, `the string renderer accepted ${pattern}`);
  }
});

test('every facet table scrolls in its own box, and each box says which table it is', () => {
  // Two scroll boxes on one page, so one shared name would leave a reader unable to tell which
  // region they had landed in.
  const html = react(COMPLETE);
  const boxes = [...html.matchAll(/<div class="coverage-scroll"[^>]*>/g)].map((one) => one[0]);
  assert.equal(boxes.length, 2);
  for (const box of boxes) {
    assert.ok(box.includes('role="region"'));
    assert.ok(box.includes('tabindex="0"'));
  }
  assert.ok(boxes[0].includes('aria-label="Held states by publisher document type, scrollable"'));
  assert.ok(boxes[1].includes('aria-label="Held works and states by language, scrollable"'));
  assert.notEqual(boxes[0], boxes[1], 'both scroll regions carry the same name');
  // A table read on its own still says when it was measured.
  assert.equal(html.split(`Counts as of index build ${BUILT_AT}.`).length - 1, 3);
});

test('the counts carry their denominators and their date', () => {
  const html = react(COMPLETE);
  assert.ok(html.includes('40 works, 120 dated states. Text is held for 84 of them and not for 36.'));
  assert.ok(html.includes(`Counts as of index build ${BUILT_AT}.`));
  assert.ok(html.includes(RETENTION_SENTENCE));
  // Pinned to a literal rather than compared against itself: imported and asserted against
  // itself, this constant could be redefined to anything and both renderers would agree.
  assert.equal(
    RETENTION_SENTENCE,
    'Observation history begins August 2026; replay depth grows from here.',
  );
  for (const gap of COMPLETE.known_gaps) {
    assert.ok(html.includes(gap), 'a publisher gap sentence was edited or dropped');
  }
});

test('values are escaped rather than trusted, in the React port too', () => {
  const hostile = {
    ...COMPLETE,
    publisher_name: '<script>alert(1)</script>',
    known_gaps: ['<img src=x onerror=alert(1)>'],
  };
  for (const html of [react(hostile), string(hostile)]) {
    assert.ok(!html.includes('<script>'), 'a script tag survived');
    assert.ok(!html.includes('<img src=x'), 'an image tag survived');
    assert.ok(html.includes('&lt;script&gt;'));
  }
});

test('both renderers refuse a truncated table that serves more rows than its total', () => {
  const oversizedTruncation = {
    ...COMPLETE,
    document_types: COMPLETE.document_types.slice(0, 2),
    document_types_total: 1,
    facets_truncated: true,
  };

  assert.throws(() => string(oversizedTruncation), /never more/);
  assert.throws(() => react(oversizedTruncation), /never more/);
});

test('both renderers refuse a repeated facet key', () => {
  const repeated = {
    ...COMPLETE,
    document_types: [COMPLETE.document_types[0], COMPLETE.document_types[0]],
    document_types_total: 2,
    facets_truncated: true,
  };

  assert.throws(() => string(repeated), /listed twice/);
  assert.throws(() => react(repeated), /listed twice/);
});

test('both renderers treat two untyped rows as a repeat', () => {
  // The untyped row is one row, and a null code is a key like any other. Tested on both sides
  // because a mutation that made null codes never collide died in the string renderer and
  // survived here, which is the asymmetry a second copy of the rules produces.
  const twoUntyped = {
    ...COMPLETE,
    document_types: [
      COMPLETE.document_types[0],
      { code: null, versions: 1, versions_with_text: 0 },
      { code: null, versions: 1, versions_with_text: 0 },
    ],
    document_types_total: 3,
    facets_truncated: true,
  };

  assert.throws(() => string(twoUntyped), /listed twice/);
  assert.throws(() => react(twoUntyped), /listed twice/);
});

test('both renderers refuse a repeated language row', () => {
  // The language table overlaps rather than partitions, so neither the sum rule nor the row-count
  // rule reaches it, and only the key rule does.
  const repeatedLanguage = {
    ...COMPLETE,
    languages: [COMPLETE.languages[0], COMPLETE.languages[0]],
  };

  assert.throws(() => string(repeatedLanguage), /listed twice/);
  assert.throws(() => react(repeatedLanguage), /listed twice/);
});
