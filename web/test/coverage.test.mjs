import assert from 'node:assert/strict';
import test from 'node:test';

import { RETENTION_SENTENCE, UNTYPED_LABEL, renderCoverage } from '../scripts/coverage.mjs';

// The shape of the live payload, with the numbers reduced so the fixture is readable. The
// proportions that matter are kept: one type where almost every state has text, one where
// almost none does, and the null-code row that has none at all.
function payload(overrides = {}) {
  return {
    envelope: {
      freshness: { built_at: '2026-08-15T09:22:08Z', stamp_signature_valid: true },
    },
    publisher_name: 'Synthetic preview publisher',
    works: 10,
    scope_expected_works: 10,
    build_inventory_status: 'complete',
    build_complete: true,
    build_issues: [],
    versions: 30,
    valid_from_earliest: '1849-03-14',
    valid_from_latest: '2030-09-15',
    document_types: [
      { code: 'LOI', versions: 12, versions_with_text: 12 },
      { code: 'RECUEIL', versions: 15, versions_with_text: 2 },
      { code: null, versions: 3, versions_with_text: 0 },
    ],
    document_types_total: 3,
    facets_truncated: false,
    languages: [{ code: 'fr', works: 10, versions: 30 }],
    text: { versions_with_text_served: 14, versions_without_text: 16 },
    known_gaps: ['never-consolidated acts are not ingested', 'coverage follows the publisher'],
    ...overrides,
  };
}

test('a versions count cannot be rendered without its text partner', () => {
  const html = renderCoverage({ coverage: payload() });
  assert.ok(html.includes('<td>15</td><td>2</td>'), 'the honest pair is not rendered');

  const { versions_with_text: _omitted, ...withoutPartner } = payload().document_types[1];
  assert.throws(
    () =>
      renderCoverage({
        coverage: payload({
          document_types: [{ code: 'LOI', versions: 12, versions_with_text: 12 }, withoutPartner],
          document_types_total: 2,
        }),
      }),
    /carries a versions count with no versions_with_text/,
  );

  assert.throws(
    () =>
      renderCoverage({
        coverage: payload({
          document_types: [{ code: 'LOI', versions: 2, versions_with_text: 9 }],
          document_types_total: 1,
        }),
      }),
    /holds text for more states than it holds/,
  );
});

test('the row the publisher gave no type for is a row, not a gap in the table', () => {
  const html = renderCoverage({ coverage: payload() });
  assert.ok(html.includes(UNTYPED_LABEL), 'the untyped states were dropped');
  assert.equal(UNTYPED_LABEL, 'untyped (publisher code absent)');
  // And it is exactly the row most likely to be missing its text, so its pair renders too.
  assert.ok(html.includes(`<td>${UNTYPED_LABEL}</td><td>3</td><td>0</td>`));
});

test('every figure comes from the payload and the renderer has no defaults', () => {
  for (const [field, value] of [
    ['works', undefined],
    ['works', -1],
    ['works', '10'],
    ['versions', null],
  ]) {
    assert.throws(
      () => renderCoverage({ coverage: payload({ [field]: value }) }),
      /rather than a count/,
      `${field}=${String(value)} was rendered`,
    );
  }
  assert.throws(
    () => renderCoverage({ coverage: payload({ text: { versions_with_text_served: 14 } }) }),
    /rather than a count/,
  );
});

test('a total that disagrees with its own parts is refused', () => {
  assert.throws(
    () =>
      renderCoverage({
        coverage: payload({ text: { versions_with_text_served: 14, versions_without_text: 15 } }),
      }),
    /do not add up to the versions count/,
  );
});

test('every count carries the build that measured it', () => {
  const html = renderCoverage({ coverage: payload() });
  assert.equal(
    (html.match(/Counts as of index build 2026-08-15T09:22:08Z/g) ?? []).length,
    3,
    'each table and the summary carry their own as-of',
  );

  for (const built of [undefined, '2026-08-15', 'recently']) {
    assert.throws(
      () =>
        renderCoverage({
          coverage: payload({ envelope: { freshness: { built_at: built } } }),
        }),
      /a count with no date is a count a reader will take as current/,
    );
  }
});

test('the gap strings are reproduced rather than tidied', () => {
  // Including the em dash the live string carries. It is this service's own statement about
  // its own limits, and a renderer that edited it would be editing the disclosure. Where the
  // string is wrong it is wrong at the source.
  const gap = 'never-consolidated LU acts are not ingested; ingestion scheduled — see coverage';
  const html = renderCoverage({ coverage: payload({ known_gaps: [gap] }) });
  assert.ok(html.includes(gap), 'the served gap string was altered');

  assert.throws(
    () => renderCoverage({ coverage: payload({ known_gaps: [] }) }),
    /coverage with no known gaps is a claim of completeness/,
  );
  assert.throws(
    () => renderCoverage({ coverage: payload({ known_gaps: undefined }) }),
    /claim of completeness/,
  );
  assert.throws(
    () => renderCoverage({ coverage: payload({ known_gaps: ['  '] }) }),
    /every known gap is a sentence/,
  );
});

test('an incomplete build shows no counts at all', () => {
  // A build that did not finish is not a smaller corpus, it is an unknown one, and its counts
  // would read as measurements of what is held.
  const html = renderCoverage({
    coverage: payload({
      build_complete: false,
      build_inventory_status: 'partial',
      build_issues: ['one source did not respond'],
    }),
  });
  assert.ok(html.includes('did not complete'));
  assert.ok(html.includes('1 recorded issue'));
  assert.ok(!html.includes('30 dated states'), 'counts from an unfinished build were shown');
  assert.ok(!html.includes('coverage-table'), 'tables from an unfinished build were shown');
});

test('a build that reports itself complete and holds the wrong number is refused', () => {
  assert.throws(
    () => renderCoverage({ coverage: payload({ scope_expected_works: 11 }) }),
    /one of those two numbers is wrong and this page must not choose which/,
  );
});

test('a truncated type table says so', () => {
  assert.throws(
    () => renderCoverage({ coverage: payload({ document_types_total: 9 }) }),
    /a table that simply stops reads as a complete one/,
  );
  const html = renderCoverage({
    coverage: payload({ document_types_total: 9, facets_truncated: true }),
  });
  assert.ok(html.includes('Showing 3 of 9 types.'));
});

test('the retention sentence is the frozen wording', () => {
  assert.ok(renderCoverage({ coverage: payload() }).includes(RETENTION_SENTENCE));
  assert.equal(
    RETENTION_SENTENCE,
    'Observation history begins August 2026; replay depth grows from here.',
  );
});

test('the future horizon is named as scheduled rather than current', () => {
  const html = renderCoverage({ coverage: payload() });
  assert.ok(html.includes('1849-03-14'));
  assert.ok(html.includes('2030-09-15'));
  assert.ok(html.includes('publisher-scheduled'), 'the horizon reads as a current date');

  assert.throws(
    () => renderCoverage({ coverage: payload({ valid_from_latest: 'the future' }) }),
    /valid_from_latest is not a calendar date/,
  );
});

test('values are escaped rather than trusted', () => {
  const html = renderCoverage({
    coverage: payload({ publisher_name: '<img src=x onerror=alert(1)>' }),
  });
  assert.ok(!html.includes('<img'));
  assert.ok(html.includes('&lt;img'));
});

test('O6: counts render only from the complete, signed, scope-declaring tuple', () => {
  // Each leg removed on its own. The gate tested build_complete alone, so an unsigned stamp or
  // an undeclared scope still produced counts that read as a complete account of the corpus.
  const cases = [
    ['the build did not complete', { build_complete: false }],
    [
      'the build stamp is not signed',
      { envelope: { ...payload().envelope, freshness: { built_at: '2026-08-15T09:22:08Z', stamp_signature_valid: false } } },
    ],
    ['the build does not declare the scope', { scope_expected_works: undefined }],
  ];
  for (const [what, override] of cases) {
    const html = renderCoverage({ coverage: payload(override) });
    assert.equal(
      html.includes('coverage-incomplete'),
      true,
      `${what}: counts rendered as a complete account`,
    );
    assert.equal(html.includes('<table'), false, `${what}: a count table was rendered anyway`);
  }
});

test('O6: the limitation says which leg failed', () => {
  // A reader who cannot tell an unfinished build from an unsigned one has been told the counts
  // are missing without being told what is wrong with the corpus.
  assert.equal(
    renderCoverage({ coverage: payload({ build_complete: false }) }).includes('the build did not complete'),
    true,
  );
  assert.equal(
    renderCoverage({
      coverage: payload({
        envelope: { ...payload().envelope, freshness: { built_at: '2026-08-15T09:22:08Z', stamp_signature_valid: false } },
      }),
    }).includes('the build stamp is not signed'),
    true,
  );
});

test('O6: an absent language list is refused, not rendered as an empty table', () => {
  // `?? []` rendered an empty table, which reads as a corpus holding no languages rather than
  // as a payload that did not say.
  for (const languages of [undefined, null, []]) {
    assert.throws(
      () => renderCoverage({ coverage: payload({ languages }) }),
      /coverage lists the languages it holds/,
      `${JSON.stringify(languages)} rendered as a language table`,
    );
  }
});

test('O6-R1: a partial inventory or a recorded issue suppresses the counts', () => {
  // Codex's probe: build_complete true, valid signature, matching scope, and the build's own
  // inventory saying partial with "source missing" among its issues. The headline counts
  // rendered as a complete account of the corpus.
  const cases = [
    ['the build inventory reports partial', { build_inventory_status: 'partial' }],
    ['the build inventory reports nothing', { build_inventory_status: undefined }],
    ['the build recorded issues', { build_issues: ['source missing'] }],
    ['the build recorded issues', { build_issues: undefined }],
    ['the build recorded issues', { build_issues: [], build_issues_truncated: true }],
  ];
  for (const [expected, override] of cases) {
    const html = renderCoverage({ coverage: payload(override) });
    assert.equal(
      html.includes('coverage-incomplete'),
      true,
      `${JSON.stringify(override)}: counts rendered as a complete account`,
    );
    assert.equal(html.includes('<table'), false);
    assert.equal(html.includes(expected), true, `${JSON.stringify(override)}: the limitation did not say why`);
  }
});

test('O6-R1: the whole tuple present still renders counts', () => {
  // The counterpart, so the repair cannot be satisfied by suppressing everything.
  const html = renderCoverage({ coverage: payload() });
  assert.equal(html.includes('coverage-incomplete'), false);
  assert.equal(html.includes('<table'), true);
});

test('a latest state already in force is not described as publisher-scheduled', () => {
  // The sentence asserted the latest date had not taken effect, and nothing compared it to
  // anything: both dates only had to be calendar dates. On a corpus whose latest state began
  // years ago it is false in the direction that matters, telling a reader that current law is
  // a future plan.
  const past = renderCoverage({
    coverage: payload({ valid_from_earliest: '1849-03-14', valid_from_latest: '2020-01-01' }),
  });
  assert.equal(past.includes('States run from 1849-03-14 to 2020-01-01'), true);
  assert.equal(
    past.includes('publisher-scheduled'),
    false,
    'a state in force for years was described as scheduled',
  );

  // And the real case it was written for: LU genuinely holds forward-dated states.
  const future = renderCoverage({
    coverage: payload({ valid_from_earliest: '1849-03-14', valid_from_latest: '2030-09-15' }),
  });
  assert.equal(future.includes('publisher-scheduled rather than current'), true);
});

test('the limitation does not report zero issues for a build that supplied no list', () => {
  // "0 recorded issues" is a claim that the build recorded none. An absent list is a build that
  // did not say, and a truncated one said only part. Reporting zero for either turns a silence
  // into a clean bill of health, on the very paragraph that exists because the counts cannot be
  // trusted.
  const absent = renderCoverage({ coverage: payload({ build_issues: undefined }) });
  assert.equal(absent.includes('coverage-incomplete'), true);
  assert.equal(absent.includes('no issue list supplied'), true);
  assert.equal(
    absent.includes('0 recorded issues'),
    false,
    'an absent issue list was reported as zero issues',
  );

  const truncated = renderCoverage({
    coverage: payload({ build_issues: ['source missing'], build_issues_truncated: true }),
  });
  assert.equal(truncated.includes('at least 1 recorded issues, list truncated'), true);

  // And a genuinely empty list still reports zero, so the repair does not make the honest case
  // unsayable.
  const empty = renderCoverage({ coverage: payload({ build_complete: false, build_issues: [] }) });
  assert.equal(empty.includes('0 recorded issues'), true);
});
