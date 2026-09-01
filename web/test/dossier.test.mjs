import assert from 'node:assert/strict';
import test from 'node:test';

import {
  DATE_ROLES,
  NOT_INGESTED,
  STATUS_CAPTION,
  renderDossier,
} from '../scripts/dossier.mjs';

const IDENTITY = {
  title: 'Code du travail',
  title_language: 'fr',
  publisher: 'preview-synthetic',
  work_identifier: 'https://preview.invalid/synthetic-preview-work',
  document_type: 'CODE',
};

const DATES = [
  { role: 'publication', date: '2021-01-26', source: 'publisher record' },
  { role: 'applicable_from', date: '2021-01-26', source: 'publisher record' },
  { role: 'applicable_to', date: '2021-04-23', source: 'publisher record' },
  { role: 'observed_from', date: '2026-08-14T23:05:14Z', source: 'this corpus' },
];

const COVERAGE = { states_held: 12, states_with_text: 2, holes: [] };

const GOOD = {
  identity: IDENTITY,
  dates: DATES,
  status: { binding_status: 'in_force' },
  coverage: COVERAGE,
};

test('the publisher status flag appears here, and never without its caption', () => {
  // This is the one screen where the flag belongs. A held state applicable before entry into
  // force carries in_force, so the chip alone asserts something the publisher did not.
  const html = renderDossier(GOOD);
  assert.ok(html.includes('<code>in_force</code>'));
  assert.ok(html.includes(STATUS_CAPTION));
  assert.equal(STATUS_CAPTION, 'current-state flag, not a historical statement');

  for (const bad of [undefined, '', null, {}]) {
    assert.throws(
      () => renderDossier({ ...GOOD, status: bad === undefined ? undefined : { binding_status: bad } }),
      /a strip with no flag is a caption about nothing/,
      `binding_status=${JSON.stringify(bad)} produced a strip`,
    );
  }
});

test('an absent date is declared with what it is waiting for, never omitted', () => {
  // The Union axiom rows are the case: entry into force and application are not ingested, and
  // a row that simply disappears takes the reader's chance to notice it was expected.
  const html = renderDossier({
    ...GOOD,
    dates: [
      ...DATES,
      {
        role: 'entry_into_force',
        date: null,
        source: 'publisher axiom',
        awaiting: 'the Cellar fd_335 entry-into-force axiom',
      },
    ],
  });
  assert.ok(html.includes('entry into force'));
  assert.equal(NOT_INGESTED, 'not yet ingested');
  assert.ok(html.includes('not yet ingested'));
  assert.ok(html.includes('the Cellar fd_335 entry-into-force axiom'));

  assert.throws(
    () =>
      renderDossier({
        ...GOOD,
        dates: [...DATES, { role: 'application', date: null, source: 'publisher axiom' }],
      }),
    /does not say what it is waiting for/,
  );
});

test('every date says where it came from and which role it plays', () => {
  assert.deepEqual([...DATE_ROLES], [
    'publication',
    'applicable_from',
    'applicable_to',
    'entry_into_force',
    'application',
    'observed_from',
  ]);

  for (const bad of ['valid_from', 'in_force', '', undefined]) {
    assert.throws(
      () => renderDossier({ ...GOOD, dates: [{ role: bad, date: '2021-01-26', source: 's' }] }),
      /the set is closed at/,
      `role=${JSON.stringify(bad)} was labelled`,
    );
  }
  assert.throws(
    () => renderDossier({ ...GOOD, dates: [{ role: 'publication', date: '2021-01-26' }] }),
    /a date with no source is this service's assertion wearing the publisher's authority/,
  );
  assert.throws(
    () => renderDossier({ ...GOOD, dates: [...DATES, DATES[0]] }),
    /lists publication twice/,
  );
  assert.throws(() => renderDossier({ ...GOOD, dates: [] }), /a dossier states its dates by role/);
});

test('the coverage strip pairs its counts and declares its gaps', () => {
  const html = renderDossier(GOOD);
  assert.ok(html.includes('12 states held, text for 2 of 12.'));
  assert.ok(html.includes('No gap between the states held.'));

  const gapped = renderDossier({
    ...GOOD,
    coverage: { ...COVERAGE, holes: [{ from: '2004-04-02', to: '2024-12-28' }] },
  });
  assert.ok(gapped.includes('No publisher state covers 2004-04-02 to 2024-12-28'));
  assert.ok(gapped.includes('Absence of a held state is not evidence the law was unchanged'));

  // Silence about gaps reads as an absence of gaps, so silence is refused.
  assert.throws(
    () => renderDossier({ ...GOOD, coverage: { states_held: 12, states_with_text: 2 } }),
    /a strip that is silent about gaps reads as a strip with none/,
  );
  assert.throws(
    () => renderDossier({ ...GOOD, coverage: { ...COVERAGE, states_with_text: 13 } }),
    /holds text for more states than it holds/,
  );
  for (const field of ['states_held', 'states_with_text']) {
    assert.throws(
      () => renderDossier({ ...GOOD, coverage: { ...COVERAGE, [field]: undefined } }),
      new RegExp(`needs ${field} as a whole count`),
    );
  }
});

test('a slot this corpus cannot fill says what it is and where the publisher keeps it', () => {
  const html = renderDossier({
    ...GOOD,
    slots: [
      {
        what: 'responsible ministry',
        where: "available on the publisher's open channel",
      },
    ],
  });
  assert.ok(html.includes('responsible ministry'));
  assert.equal(NOT_INGESTED, 'not yet ingested');
  assert.ok(html.includes('not yet ingested'));
  // Escaped, as any supplied string is.
  assert.ok(html.includes('available on the publisher&#39;s open channel'));

  // "Not held" with no route is indistinguishable from "does not exist".
  assert.throws(
    () => renderDossier({ ...GOOD, slots: [{ what: 'responsible ministry' }] }),
    /does not say where the publisher keeps it/,
  );
  assert.throws(
    () => renderDossier({ ...GOOD, slots: [{ where: 'somewhere' }] }),
    /does not say what it is/,
  );

  // No slots means no heading, rather than an empty section implying nothing is missing.
  assert.ok(!renderDossier(GOOD).includes('Not held by this corpus'));
});

test('the published title keeps its own language and its own words', () => {
  const html = renderDossier(GOOD);
  assert.ok(html.includes('lang="fr"'));
  assert.ok(html.includes('Code du travail'));

  for (const bad of [undefined, '', 'french']) {
    assert.throws(
      () => renderDossier({ ...GOOD, identity: { ...IDENTITY, title_language: bad } }),
      /the published title carries its own language/,
    );
  }
  assert.throws(
    () => renderDossier({ ...GOOD, identity: { ...IDENTITY, title: '  ' } }),
    /names the work as the publisher titles it/,
  );
});

test('the work identifier is a name in the publisher namespace, not a link', () => {
  // An ELI or CELEX is an identifier spelled as an HTTP URI. It is printed and cited, never
  // followed. Routing it through the outbound-link policy demanded https, and both identifiers
  // the pack cites as live are http, so this screen could not render a single real work.
  for (const [publisher, uri] of [
    ['lu-legilux', 'http://data.legilux.public.lu/eli/etat/leg/loi/2006/07/31/n2'],
    ['eu-eurlex', 'http://publications.europa.eu/resource/celex/32016R0679'],
  ]) {
    const html = renderDossier({
      ...GOOD,
      identity: { ...IDENTITY, publisher, work_identifier: uri },
    });
    assert.ok(html.includes(uri), `${uri} could not be rendered as the work's own name`);
    // And it is not offered as somewhere to go.
    assert.ok(!html.includes(`href="${uri}"`), 'an identifier was rendered as a link');
  }

  // The publisher's namespace still bounds it: a name on somebody else's host is not that
  // publisher's name for anything.
  for (const uri of [
    'http://evil.example/eli/x',
    'ftp://data.legilux.public.lu/x',
    'http://data.legilux.public.lu:8080/x',
    'http://data.legilux.public.lu@evil.example/x',
    'not a url',
  ]) {
    assert.throws(
      () =>
        renderDossier({
          ...GOOD,
          identity: { ...IDENTITY, publisher: 'lu-legilux', work_identifier: uri },
        }),
      /work identifier/,
      `${uri} was rendered as the work identifier`,
    );
  }
});

test('a work this corpus holds no state of is not reported as complete', () => {
  // "No gap" is a claim about a record that exists. With nothing held there is no record to be
  // continuous, and a reader told a work has no gaps concludes the corpus holds its history.
  const html = renderDossier({
    ...GOOD,
    coverage: { states_held: 0, states_with_text: 0, holes: [] },
  });
  assert.ok(html.includes('No state of this work is held by this corpus'));
  assert.ok(!html.includes('No gap between the states held'), 'an empty record claimed no gaps');
  assert.ok(html.includes('Absence from this corpus is not absence from the record'));
});

test('a coverage gap runs forwards, or it is not a gap', () => {
  for (const hole of [
    { from: '2024-12-28', to: '2004-04-02' },
    { from: '2004-04-02', to: '2004-04-02' },
  ]) {
    assert.throws(
      () => renderDossier({ ...GOOD, coverage: { ...COVERAGE, holes: [hole] } }),
      /backwards or empty; a gap that ends before it begins/,
      `${JSON.stringify(hole)} was stated as a gap in the record`,
    );
  }
});

test('each date role carries the clock it belongs to', () => {
  // Accepting either shape lost the UTC instant the record clock requires and gave the legal
  // clock a time of day the publisher never stated.
  assert.throws(
    () =>
      renderDossier({
        ...GOOD,
        dates: [{ role: 'observed_from', date: '2026-08-14', source: 'this corpus' }],
      }),
    /the record clock and is a UTC instant, verbatim/,
  );
  assert.throws(
    () =>
      renderDossier({
        ...GOOD,
        dates: [{ role: 'applicable_from', date: '2021-01-26T13:45:09Z', source: 'p' }],
      }),
    /no time of day the publisher did not state/,
  );
});

test('the publisher flag is the publisher word, not one this service derived', () => {
  for (const bad of ['REPEALED (lex derived)', 'In Force', 'in force', 'derived:repealed', '1']) {
    assert.throws(
      () => renderDossier({ ...GOOD, status: { binding_status: bad } }),
      /is not a bare publisher flag token/,
      `${JSON.stringify(bad)} was printed as the publisher's own flag`,
    );
  }
  assert.ok(renderDossier(GOOD).includes('<code>in_force</code>'));
});

test('values are escaped rather than trusted', () => {
  const html = renderDossier({
    ...GOOD,
    identity: { ...IDENTITY, title: '<img src=x onerror=alert(1)> & more' },
  });
  assert.ok(!html.includes('<img'));
  assert.ok(html.includes('&lt;img'));
  assert.ok(html.includes('&amp; more'));
});
