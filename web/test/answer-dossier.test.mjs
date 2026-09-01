import assert from 'node:assert/strict';
import test from 'node:test';

import { CLAIM_KINDS, renderAnswerDossier } from '../scripts/answer-dossier.mjs';

const DIGEST_A = '5512d26f4fcdf962273e5f4ac59b893401b380a128a737ba718d3326cba0ed7e';
const DIGEST_B = 'dedbcbe0f53f5e2b41fd98551d5913b0ed56525ec35f7b26a6c0fa9eaad4ba3c';

const OPERATIONS = [
  {
    call_id: 'call-1',
    operation_id: 'as_of',
    parameters: { work: 'preview-synthetic:synthetic-preview-work', date: '2001-06-01' },
    result_identity: DIGEST_A,
    called_at: '2026-01-01T00:00:00Z',
  },
  {
    call_id: 'call-2',
    operation_id: 'timeline',
    parameters: { work: 'preview-synthetic:synthetic-preview-work' },
    result_identity: DIGEST_B,
    called_at: '2026-01-01T00:00:01Z',
  },
];

const CLAIMS = [
  {
    sentence: 'The state applicable on 2001-06-01 begins on 2001-01-01.',
    kind: 'publisher_asserted',
    bindings: [{ call_id: 'call-1', fact: 'valid_from 2001-01-01' }],
  },
  {
    sentence: 'That state is the second of four the publisher holds.',
    kind: 'derived',
    bindings: [
      { call_id: 'call-2', fact: 'four states' },
      { call_id: 'call-1', fact: 'valid_from 2001-01-01' },
    ],
  },
];

const GOOD = { claims: CLAIMS, operations: OPERATIONS };

test('a sentence that binds to nothing is not emitted', () => {
  // The rule the module exists for. A reader cannot tell a bound sentence from an unbound
  // one by reading it, which is exactly why the check cannot live with the reader.
  for (const bindings of [undefined, [], null]) {
    assert.throws(
      () =>
        renderAnswerDossier({
          ...GOOD,
          claims: [{ sentence: 'Something true sounding.', kind: 'derived', bindings }],
        }),
      /binds to nothing/,
      `${JSON.stringify(bindings)} was rendered`,
    );
  }
});

test('a claim cannot cite a call the trace does not contain', () => {
  assert.throws(
    () =>
      renderAnswerDossier({
        ...GOOD,
        claims: [
          {
            sentence: 'A sentence citing a call nobody recorded.',
            kind: 'publisher_asserted',
            bindings: [{ call_id: 'call-99', fact: 'something' }],
          },
        ],
      }),
    /is not in the operations trace/,
  );
  assert.throws(
    () =>
      renderAnswerDossier({
        ...GOOD,
        claims: [
          {
            sentence: 'A sentence citing a call without saying which fact.',
            kind: 'publisher_asserted',
            bindings: [{ call_id: 'call-1' }],
          },
        ],
      }),
    /names which fact in that result/,
  );
});

test('every claim declares whether it is the publisher or this product speaking', () => {
  assert.deepEqual([...CLAIM_KINDS], ['publisher_asserted', 'derived']);
  for (const kind of [undefined, 'true', 'inferred', 'toString']) {
    assert.throws(
      () =>
        renderAnswerDossier({
          ...GOOD,
          claims: [
            { sentence: 'A sentence.', kind, bindings: [{ call_id: 'call-1', fact: 'x' }] },
          ],
        }),
      /is not a claim kind/,
      `${String(kind)} was accepted`,
    );
  }
});

test('a derived claim is labelled derived and says it is excluded from exports', () => {
  const html = renderAnswerDossier(GOOD);
  assert.ok(html.includes('token--derived'), 'the derived token is missing');
  assert.ok(html.includes('derived, not publisher-asserted'));
  assert.ok(html.includes('excluded from evidence exports'));

  // A publisher assertion is not dressed as a derivation, and does not carry the exclusion.
  const onlyPublisher = renderAnswerDossier({ ...GOOD, claims: [CLAIMS[0]] });
  assert.ok(!onlyPublisher.includes('token--derived'));
  assert.ok(!onlyPublisher.includes('excluded from evidence exports'));
});

test('the trace records the call, its parameters and the identity of what came back', () => {
  const html = renderAnswerDossier(GOOD);
  assert.ok(html.includes('<code>as_of</code>'));
  assert.ok(html.includes('2001-06-01'), 'a parameter is missing from the trace');
  assert.ok(html.includes(DIGEST_A), 'the result identity is missing');
  assert.ok(html.includes('called 2026-01-01T00:00:00Z'));

  for (const [field, value, pattern] of [
    ['parameters', undefined, /must record the parameters/],
    ['result_identity', 'not-a-digest', /identity of what came back/],
    ['result_identity', DIGEST_A.slice(0, 8), /identity of what came back/],
    ['called_at', 'yesterday', /must record when it was called/],
    ['called_at', '2026-99-99T00:00:00Z', /must record when it was called/],
  ]) {
    assert.throws(
      () =>
        renderAnswerDossier({
          ...GOOD,
          operations: [{ ...OPERATIONS[0], [field]: value }, OPERATIONS[1]],
        }),
      pattern,
      `${field}=${String(value)} was accepted`,
    );
  }
});

test('an answer with no trace is refused, however well bound its claims look', () => {
  for (const operations of [undefined, [], null]) {
    assert.throws(
      () => renderAnswerDossier({ ...GOOD, operations }),
      /carries its operations trace/,
      `${JSON.stringify(operations)} was accepted`,
    );
  }
});

test('a call id appears once, so a chip cannot point at two different results', () => {
  assert.throws(
    () =>
      renderAnswerDossier({
        ...GOOD,
        operations: [OPERATIONS[0], { ...OPERATIONS[1], call_id: 'call-1' }],
      }),
    /appears twice in one trace/,
  );
});

test('every chip resolves to the trace entry it cites', () => {
  const html = renderAnswerDossier(GOOD);
  const targets = [...html.matchAll(/id="trace-([^"]+)"/g)].map((m) => m[1]);
  const chips = [...html.matchAll(/href="#trace-([^"]+)"/g)].map((m) => m[1]);
  assert.ok(chips.length >= 3);
  for (const chip of chips) {
    assert.ok(targets.includes(chip), `chip ${chip} points at no trace entry`);
  }
});

test('claims are above the fold and the trace is inside it', () => {
  const html = renderAnswerDossier(GOOD);
  assert.ok(html.indexOf('class="claims"') < html.indexOf('<details'));
  assert.ok(html.includes('How this answer was produced, 2 operations'));
  // A reader who wants the answer gets the answer; a reader who wants to check it gets
  // everything needed to re-run it, without either being made to read the other first.
  assert.ok(html.includes('<details class="operations-trace">'));
});

test('values are escaped rather than trusted', () => {
  const html = renderAnswerDossier({
    ...GOOD,
    claims: [
      {
        sentence: '<img src=x onerror=alert(1)>',
        kind: 'publisher_asserted',
        bindings: [{ call_id: 'call-1', fact: '<script>alert(1)</script>' }],
      },
    ],
  });
  assert.ok(!html.includes('<img'));
  assert.ok(!html.includes('<script>'));
  assert.ok(html.includes('&lt;img'));
});

test('an operation without a call id cannot be cited, and cannot stand in for one', () => {
  // The bypass an audit found: with the call-id guard removed, every operation missing one
  // adds `undefined` to the set of recorded calls, and a claim binding to `undefined` then
  // satisfies the rule that a claim must cite a call the trace recorded. The module's whole
  // point is that binding, so the guard that makes the set meaningful gets its own case.
  const nameless = { ...OPERATIONS[0] };
  delete nameless.call_id;
  assert.throws(
    () => renderAnswerDossier({ ...GOOD, operations: [nameless] }),
    /needs a call id to be bound to/,
  );
  for (const bad of ['', null, 7, {}]) {
    assert.throws(
      () => renderAnswerDossier({ ...GOOD, operations: [{ ...OPERATIONS[0], call_id: bad }] }),
      /needs a call id to be bound to/,
      `call_id=${JSON.stringify(bad)} was recorded as a call`,
    );
  }

  // And the same for the operation's own name, which rendered as the literal "undefined" in
  // the trace: a citation pointing at nothing.
  for (const bad of [undefined, '', null]) {
    assert.throws(
      () => renderAnswerDossier({ ...GOOD, operations: [{ ...OPERATIONS[0], operation_id: bad }] }),
      /an operation needs its id/,
    );
  }
});

test('a claim with no sentence is refused rather than rendered as the word undefined', () => {
  for (const bad of [undefined, '', '  ', null]) {
    assert.throws(
      () => renderAnswerDossier({ ...GOOD, claims: [{ ...CLAIMS[0], sentence: bad }] }),
      /sentence/,
      `sentence=${JSON.stringify(bad)} rendered as a claim`,
    );
  }
  assert.ok(!renderAnswerDossier(GOOD).includes('undefined'));
});

test('an answer with no claims is refused rather than rendered empty', () => {
  assert.throws(() => renderAnswerDossier({ ...GOOD, claims: [] }), /claim/);
});
