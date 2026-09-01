import assert from 'node:assert/strict';
import test from 'node:test';

import {
  REFUSAL_CODES,
  REQUIRED_PAYLOAD,
  RETRYABLE,
  renderRefusalCard,
} from '../scripts/refusal-card.mjs';
import { readingUrl } from '../scripts/urls.mjs';

const GOVERNING = 'Art. L. 121-6. Le salarié incapable de travailler pour cause de maladie...';
const HANDOFF = {
  label: 'Service d’accueil et d’information juridique',
  href: 'https://justice.public.lu/',
};

const HASH_A = '99b621c38dec11dcd362c0db35d9e9c090e62613cc5c20b0727c0b30fd39ce66';
const HASH_B = 'c064f74a9827d610125d25c999f79df626cd987432aa110f2e05ce48388b5eef';

function candidate(validFrom, hash, publicationDate) {
  return {
    valid_from: validFrom,
    hash,
    publication_date: publicationDate,
    href: readingUrl({
      publisher: 'lu-legilux',
      work: 'loi-1993-04-05-n1',
      validFrom,
      hash,
    }),
  };
}

const CANDIDATES = [
  candidate('2025-01-01', HASH_A, '2024-12-20'),
  candidate('2025-01-01', HASH_B, '2024-12-27'),
];

/**
 * One valid example per code. UX spec section 11 wants a refusal registry page carrying
 * "one live example payload each", and the same table is what proves here that every code in
 * the closed registry is constructible under its own rule rather than only in principle.
 */
const EXAMPLES = Object.freeze({
  identifier_unknown: {
    sentence: 'That identifier does not resolve to a held work.',
    payload: {
      population_disclosure:
        '1,402 consolidated LU works and 1,250 EU works are searchable; about 24,579 ' +
        'never-consolidated LU acts are not.',
    },
  },
  ambiguous_identifier: {
    sentence: 'That citation matches more than one instrument.',
    payload: { candidates_named: 'loi-2002-08-02-n2, loi-2002-08-02-n3' },
  },
  out_of_corpus_scope: {
    sentence: 'That instrument is outside the reviewed corpus.',
    payload: {
      classified_as: 'CSSF circular',
      population_disclosure: 'The reviewed corpus holds Legilux and EUR-Lex legislation only.',
    },
  },
  no_version_for_date: {
    sentence: 'No publisher state covers 2015-06-01.',
    payload: {
      history_begins: '2017-01-01',
      nearest_earlier: 'none held',
      nearest_later: '2017-01-01',
    },
  },
  ambiguous_version: {
    sentence: 'Two publisher states cover that date.',
    payload: { candidates: CANDIDATES },
  },
  anchor_not_in_version: {
    sentence: 'art_l121-6 is not an anchor in this version.',
    payload: { nearest_anchors: ['art_l_121-6', 'art_l_121-7'] },
  },
  language_not_available: {
    sentence: 'This work is held in French only.',
    payload: { languages_held: ['fr'] },
  },
  text_not_available: {
    sentence: 'The publisher records this state but serves no text for it.',
    payload: {
      official_uri: 'https://legilux.public.lu/eli/etat/leg/loi/2002/08/02/n2',
      gazette_chain: 'Mémorial A 1993 no 27, A 2002 no 92',
    },
  },
  text_withheld: {
    sentence: 'The publisher licence does not permit serving this text.',
    payload: { licence: 'licenceSCL' },
  },
  format_not_available: {
    sentence: 'This state is held as PDF only.',
    payload: { formats_held: ['pdf'] },
  },
  profiles_differ: {
    sentence: 'These two states came from different extraction profiles.',
    payload: { profiles: ['pdf-lu/1', 'akn-lu/1'] },
  },
  not_transposable: {
    sentence: 'A regulation is not transposed.',
    payload: { execution_acts: ['loi-2018-08-01-n1'] },
  },
  derivation_refused: {
    sentence: 'The transitional provision is served verbatim rather than derived.',
    payload: { reason: 'no official source models transitional provisions as data' },
  },
  retrieval_mode_unavailable: {
    sentence: 'Semantic retrieval is not serving; this search ran on keywords.',
    payload: { fallback_mode: 'keyword' },
  },
  no_corpus_mounted: {
    sentence: 'This build has no index mounted.',
    payload: { mounted_indexes: '0' },
  },
  snapshot_unknown: {
    sentence: 'That snapshot identity is not one this build holds.',
    payload: { snapshots_held: '2026-08-15' },
  },
  upstream_unreachable: {
    sentence: 'The publisher did not answer.',
    payload: { host: 'legilux.public.lu' },
  },
  rate_limited: {
    sentence: 'Too many requests reached the publisher.',
    payload: { retry_after: '30s' },
  },
  advice_boundary: {
    sentence: 'I cannot apply the law to your situation.',
    governingText: GOVERNING,
    handoff: HANDOFF,
  },
});

test('the registry is closed at the nineteen product-spec codes', () => {
  assert.equal(REFUSAL_CODES.length, 19);
  assert.equal(new Set(REFUSAL_CODES).size, 19, 'a code is listed twice');
  for (const code of ['identifier_unknown', 'no_version_for_date', 'advice_boundary']) {
    assert.ok(REFUSAL_CODES.includes(code));
  }
});

test('an unknown code is refused rather than rendered as a generic error', () => {
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'something_went_wrong',
        sentence: 'x',
        payload: { a: 'b' },
      }),
    /the registry is closed/,
  );
  // The UX spec's informal names are not in the versioned registry, and must not silently work.
  assert.throws(
    () => renderRefusalCard({ code: 'unknown_work', sentence: 'x', payload: { a: 'b' } }),
    /the registry is closed/,
  );
});

test('a sterile refusal cannot be constructed', () => {
  // snapshot_unknown has no spec-named payload, so it exercises the generic rule alone.
  assert.throws(
    () => renderRefusalCard({ code: 'snapshot_unknown', sentence: 'That snapshot is unknown.' }),
    /carries no payload, no governing text and no handoff/,
  );
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'snapshot_unknown',
        sentence: 'That snapshot is unknown.',
        payload: {},
      }),
    /carries no payload, no governing text and no handoff/,
  );
  // A payload whose every value is blank is a sterile refusal wearing a payload's clothes.
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'snapshot_unknown',
        sentence: 'That snapshot is unknown.',
        payload: { held: '   ', also: null, list: [] },
      }),
    /carries no payload, no governing text and no handoff/,
  );
});

test('every code in the closed registry has a worked example, and every example renders', () => {
  assert.deepEqual(
    Object.keys(EXAMPLES).sort(),
    [...REFUSAL_CODES].sort(),
    'the registry and the worked examples have drifted apart',
  );
  for (const code of REFUSAL_CODES) {
    const card = renderRefusalCard({ code, ...EXAMPLES[code] });
    assert.ok(card.includes(code), `${code} lost its machine code chip`);
    assert.ok(card.length > 120, `${code} rendered a card too small to be an answer`);
  }
});

test('each spec-named payload requirement is enforced, one key at a time', () => {
  // Dropping any single required key must be refused, naming that key. A rule that only
  // fires on an empty payload is not the rule the spec states.
  for (const [code, requirement] of Object.entries(REQUIRED_PAYLOAD)) {
    const example = EXAMPLES[code];
    assert.ok(example, `${code} has a requirement but no worked example`);
    for (const key of requirement.keys) {
      const withoutKey = { ...example.payload };
      delete withoutKey[key];
      assert.throws(
        () => renderRefusalCard({ ...example, code, payload: withoutKey }),
        new RegExp(`refusal ${code} must carry [^;]*\\b${key}\\b`),
        `${code} rendered without ${key}`,
      );
    }
  }
});

test('a payload that only echoes the question does not satisfy the requirement', () => {
  // The live service returns exactly this today for both codes, and the specs call it bare.
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'no_version_for_date',
        sentence: 'No publisher state covers 2010-01-01.',
        payload: { work: 'lu-legilux:loi-1915-08-10-n1', date: '2010-01-01' },
      }),
    /must carry history_begins, nearest_earlier, nearest_later/,
  );
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'identifier_unknown',
        sentence: 'That identifier does not resolve.',
        payload: { work: 'lu-legilux:loi-2004-11-12-n3' },
      }),
    /must carry population_disclosure/,
  );
});

test('an absent nearest state must be stated, not omitted', () => {
  const stated = renderRefusalCard({
    code: 'no_version_for_date',
    ...EXAMPLES.no_version_for_date,
  });
  assert.ok(stated.includes('none held'));
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'no_version_for_date',
        sentence: 'No publisher state covers 2015-06-01.',
        payload: { history_begins: '2017-01-01', nearest_earlier: '', nearest_later: '2017-01-01' },
      }),
    /must carry nearest_earlier/,
  );
});

test('the ambiguous_version interstitial never defaults and never mislabels a candidate', () => {
  const card = renderRefusalCard({ code: 'ambiguous_version', ...EXAMPLES.ambiguous_version });
  assert.ok(card.includes('applicable from 2025-01-01, hash <code>99b621c3</code>, published'));
  assert.ok(card.includes('c064f74a'));
  assert.ok(card.includes('The publisher ranks neither state'));

  assert.throws(
    () =>
      renderRefusalCard({
        code: 'ambiguous_version',
        sentence: 'Two states cover that date.',
        payload: { candidates: [CANDIDATES[0]] },
      }),
    /shorter than two/,
  );

  assert.throws(
    () =>
      renderRefusalCard({
        code: 'ambiguous_version',
        sentence: 'Two states cover that date.',
        payload: {
          candidates: [{ ...CANDIDATES[0], selected: true }, CANDIDATES[1]],
        },
      }),
    /no default selection/,
  );

  // The label says one hash; the link resolves to another. That is silent resolution.
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'ambiguous_version',
        sentence: 'Two states cover that date.',
        payload: {
          candidates: [{ ...CANDIDATES[0], href: CANDIDATES[1].href }, CANDIDATES[1]],
        },
      }),
    /resolves to a different state than the candidate names/,
  );

  // Eight characters are what the reader sees; they are not what identifies the state.
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'ambiguous_version',
        sentence: 'Two states cover that date.',
        payload: {
          candidates: [{ ...CANDIDATES[0], hash: HASH_A.slice(0, 8) }, CANDIDATES[1]],
        },
      }),
    /64 hex character hash/,
  );
});

test('profiles_differ names both profiles and says it cannot be overridden', () => {
  const card = renderRefusalCard({ code: 'profiles_differ', ...EXAMPLES.profiles_differ });
  assert.ok(card.includes('pdf-lu/1'));
  assert.ok(card.includes('akn-lu/1'));
  assert.ok(card.includes('not overridable'));

  assert.throws(
    () =>
      renderRefusalCard({
        code: 'profiles_differ',
        sentence: 'The profiles differ.',
        payload: { profiles: ['pdf-lu/1'] },
      }),
    /names both profiles/,
  );
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'profiles_differ',
        sentence: 'The profiles differ.',
        payload: { profiles: ['pdf-lu/1', 'pdf-lu/1'] },
      }),
    /one profile named twice/,
  );
});

test('anchor_not_in_version shows nearest anchors and rules out the fallback', () => {
  const card = renderRefusalCard({
    code: 'anchor_not_in_version',
    ...EXAMPLES.anchor_not_in_version,
  });
  assert.ok(card.includes('<code>art_l_121-6</code>'), 'nearest anchors were not rendered as chips');
  assert.ok(card.includes('does not fall back to full-text search'));
});

test('the mandated notes come from the component, not from the caller', () => {
  // A caller cannot forget them, because there is no parameter through which to pass them.
  for (const code of ['anchor_not_in_version', 'ambiguous_version', 'profiles_differ']) {
    const card = renderRefusalCard({ code, ...EXAMPLES[code] });
    assert.ok(card.includes('class="refusal-note"'), `${code} lost its mandated note`);
  }
  const other = renderRefusalCard({ code: 'text_withheld', ...EXAMPLES.text_withheld });
  assert.ok(!other.includes('class="refusal-note"'), 'a note appeared where none is mandated');
});

test('advice_boundary co-delivers the governing provisions and a reachable counter', () => {
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'advice_boundary',
        sentence: 'I cannot apply the law to your situation.',
        payload: { handoff: 'CSL, ITM, SAIJ' },
      }),
    /must co-deliver the governing provisions/,
  );

  // An acronym in the payload is not a handoff a citizen can reach.
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'advice_boundary',
        sentence: 'I cannot apply the law to your situation.',
        payload: { handoff: 'CSL, ITM, SAIJ' },
        governingText: GOVERNING,
      }),
    /reachable human counter/,
  );

  const good = renderRefusalCard({ code: 'advice_boundary', ...EXAMPLES.advice_boundary });
  assert.ok(good.includes('The published text, in full'));
  assert.ok(good.includes('L. 121-6'));
  assert.ok(good.includes('href="https://justice.public.lu/"'));
});

test('a refusal is not announced as an error', () => {
  const card = renderRefusalCard({ code: 'profiles_differ', ...EXAMPLES.profiles_differ });
  assert.ok(!card.includes('role="alert"'), 'a refusal was announced as an alert');
  assert.ok(!card.includes('aria-live'), 'a refusal was put in a live region');
  assert.ok(card.includes('refusal-card'));
});

test('the machine code is rendered as code, and the sentence carries the token', () => {
  const card = renderRefusalCard({ code: 'text_withheld', ...EXAMPLES.text_withheld });
  assert.ok(card.includes('<code class="refusal-code">text_withheld</code>'));
  assert.ok(card.includes('token-icon'), 'the refusal token lost its icon');
  assert.ok(card.includes('token-label'), 'the refusal token lost its label');
});

test('retryable refusals say so, and the rest do not', () => {
  assert.deepEqual([...RETRYABLE].sort(), ['rate_limited', 'upstream_unreachable']);
  const retryable = renderRefusalCard({
    code: 'upstream_unreachable',
    ...EXAMPLES.upstream_unreachable,
  });
  assert.ok(retryable.includes('worth retrying'));

  const terminal = renderRefusalCard({
    code: 'out_of_corpus_scope',
    ...EXAMPLES.out_of_corpus_scope,
  });
  assert.ok(!terminal.includes('worth retrying'));
});

test('quoted statutory text carries its own language attribute', () => {
  const card = renderRefusalCard({ code: 'advice_boundary', ...EXAMPLES.advice_boundary });
  assert.ok(card.includes('lang="fr"'), 'French statute was not marked as French');
});

test('payload values are escaped rather than trusted', () => {
  const card = renderRefusalCard({
    code: 'identifier_unknown',
    sentence: 'That identifier does not resolve.',
    payload: {
      population_disclosure: 'about 24,579 never-consolidated LU acts are not searchable',
      echoed: '<img src=x onerror=alert(1)>',
    },
  });
  assert.ok(!card.includes('<img'));
  assert.ok(card.includes('&lt;img'));
});

test('a card without a human sentence is refused', () => {
  assert.throws(
    () => renderRefusalCard({ code: 'rate_limited', sentence: '   ', payload: { a: 'b' } }),
    /requires one human sentence/,
  );
});

test('every requirement cites where it came from', () => {
  for (const [code, requirement] of Object.entries(REQUIRED_PAYLOAD)) {
    assert.ok(REFUSAL_CODES.includes(code), `${code} is not in the registry`);
    assert.ok(requirement.keys.length > 0, `${code} requires nothing`);
    assert.match(
      requirement.basis,
      /^3[0-9]-[a-z0-9-]+:/,
      `${code} does not cite a numbered architect document`,
    );
  }
});
