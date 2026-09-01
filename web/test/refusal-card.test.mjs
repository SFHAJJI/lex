import assert from 'node:assert/strict';
import test from 'node:test';

import {
  CONTRACT_STATUS,
  REFUSAL_CODES,
  REQUIRED_PAYLOAD,
  RETRYABLE,
  renderRefusalCard,
} from '../scripts/refusal-card.mjs';
import { readingUrl } from '../scripts/urls.mjs';

// Synthetic throughout. A component test that embeds real statute teaches the fixture to
// look like law, and the fixture is what every later screen copies.
const AUTHENTICITY = {
  schema: 'lex-v3-resource-authenticity/1',
  resource_id: 'preview-synthetic:synthetic-preview-work:2001-01-01',
  authentic_languages: ['en'],
  basis: 'synthetic preview evidence',
  asserted_by: 'synthetic preview publisher',
  observed_at: '2026-01-01T00:00:00Z',
};
const GOVERNING = {
  authenticity: AUTHENTICITY,
  language: 'en',
  text: 'LEX V3 SYNTHETIC PREVIEW. Article 1. This text has no legal authority.',
  coverage: 'complete_provision',
  as_of: '2001-01-01',
};
// Decision 41 settles the ending as a referral list, not one counter.
const HANDOFF = [
  { label: 'Synthetic counter one', href: 'https://handoff.invalid/one' },
  { label: 'Synthetic counter two', href: 'https://handoff.invalid/two' },
];

const HASH_A = '99b621c38dec11dcd362c0db35d9e9c090e62613cc5c20b0727c0b30fd39ce66';
const HASH_B = 'c064f74a9827d610125d25c999f79df626cd987432aa110f2e05ce48388b5eef';

function candidate(validFrom, hash, publicationDate) {
  return {
    valid_from: validFrom,
    hash,
    publication_date: publicationDate,
    href: readingUrl({
      publisher: 'preview-synthetic',
      work: 'synthetic-preview-work',
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
    payload: {
      publisher: 'preview-synthetic',
      work: 'synthetic-preview-work',
      candidates: CANDIDATES,
    },
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
        payload: {
          publisher: 'preview-synthetic',
          work: 'synthetic-preview-work',
          candidates: [CANDIDATES[0]],
        },
      }),
    /shorter than two/,
  );

  assert.throws(
    () =>
      renderRefusalCard({
        code: 'ambiguous_version',
        sentence: 'Two states cover that date.',
        payload: {
          publisher: 'preview-synthetic',
          work: 'synthetic-preview-work',
          candidates: [{ ...CANDIDATES[0], selected: true }, CANDIDATES[1]],
        },
      }),
    /undeclared member/,
  );

  // The label says one hash; the link resolves to another. That is silent resolution.
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'ambiguous_version',
        sentence: 'Two states cover that date.',
        payload: {
          publisher: 'preview-synthetic',
          work: 'synthetic-preview-work',
          candidates: [{ ...CANDIDATES[0], href: CANDIDATES[1].href }, CANDIDATES[1]],
        },
      }),
    /resolves to a different object than the candidate names/,
  );

  // Eight characters are what the reader sees; they are not what identifies the state.
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'ambiguous_version',
        sentence: 'Two states cover that date.',
        payload: {
          publisher: 'preview-synthetic',
          work: 'synthetic-preview-work',
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

  // Decision 41 settles the ending as a named referral list. One counter, or an acronym in
  // the payload, is not that list.
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'advice_boundary',
        sentence: 'I cannot apply the law to your situation.',
        payload: { handoff: 'CSL, ITM, SAIJ' },
        governingText: GOVERNING,
      }),
    /must name the referral list, not one counter/,
  );

  const good = renderRefusalCard({ code: 'advice_boundary', ...EXAMPLES.advice_boundary });
  assert.ok(good.includes('The governing text in full, as it stood on 2001-01-01'));
  assert.ok(good.includes('no legal authority'));
  assert.ok(good.includes('href="https://handoff.invalid/one"'));
  assert.ok(good.includes('href="https://handoff.invalid/two"'));
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

test('quoted text carries the expression language, not a hardcoded French', () => {
  // Every governing text used to be emitted with lang="fr", so an English EU expression was
  // announced to a screen reader in a French voice.
  const english = renderRefusalCard({ code: 'advice_boundary', ...EXAMPLES.advice_boundary });
  assert.ok(english.includes('lang="en"'));
  assert.ok(!english.includes('lang="fr"'), 'an English expression was labelled French');

  const french = renderRefusalCard({
    code: 'advice_boundary',
    sentence: 'I cannot apply the law to your situation.',
    governingText: {
      authenticity: {
        schema: 'lex-v3-resource-authenticity/1',
        resource_id: 'preview-synthetic:synthetic-french-act:2001-01-01',
        authentic_languages: ['fr'],
        basis: 'synthetic preview evidence',
        asserted_by: 'synthetic preview publisher',
        observed_at: '2026-01-01T00:00:00Z',
      },
      language: 'fr',
      text: 'APERCU SYNTHETIQUE. Article 1er. Ce texte est synthetique.',
      coverage: 'excerpt',
    },
    handoff: HANDOFF,
  });
  assert.ok(french.includes('lang="fr"'));
});

test('a handoff link is validated, not merely escaped', () => {
  for (const href of [
    'javascript:alert(1)',
    'data:text/html,<script>alert(1)</script>',
    'http://handoff.invalid/counter',
    'https://evil.example/counter',
    'https://handoff.invalid@evil.example/counter',
    'https://handoff.invalid:8443/counter',
  ]) {
    assert.throws(
      () =>
        renderRefusalCard({
          code: 'advice_boundary',
          sentence: 'I cannot apply the law to your situation.',
          governingText: GOVERNING,
          handoff: [{ label: 'Counter', href }, HANDOFF[1]],
        }),
      /a handoff/,
      `${href} was rendered as a working handoff link`,
    );
  }
});

test('a payload shape nobody typed is refused rather than stringified', () => {
  for (const value of [{ nested: 'object' }, [{ nested: 'object' }], () => 'x']) {
    assert.throws(
      () =>
        renderRefusalCard({
          code: 'text_withheld',
          sentence: 'The publisher licence does not permit serving this text.',
          payload: { licence: value },
        }),
      /carries scalars or lists of scalars/,
      `${JSON.stringify(value)} reached the page`,
    );
  }
  // Scalars and lists of scalars still work, including the ones that are not strings.
  const card = renderRefusalCard({
    code: 'format_not_available',
    sentence: 'This state is held as PDF only.',
    payload: { formats_held: ['pdf'], count: 1, ocr_attempted: false },
  });
  assert.ok(card.includes('pdf'));
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

test('the payload contract covers the registry, with no code left undeclared', () => {
  // Ten codes have no payload the pack names. That is a fact about the pack, and it is now
  // written down rather than left as the absence of an entry, so a new code cannot join the
  // registry without somebody deciding which kind it is.
  assert.deepEqual(Object.keys(REQUIRED_PAYLOAD).sort(), [...REFUSAL_CODES].sort());

  for (const [code, requirement] of Object.entries(REQUIRED_PAYLOAD)) {
    assert.ok(requirement.basis.length > 20, `${code} declares no basis`);
    if (requirement.keys.length > 0) {
      assert.match(
        requirement.basis,
        /^3[0-9]-[a-z0-9-]+/,
        `${code} requires keys without citing a numbered architect document`,
      );
      assert.ok(!requirement.unspecified, `${code} is both specified and unspecified`);
    } else {
      assert.ok(
        requirement.unspecified === true || code === 'advice_boundary',
        `${code} requires nothing and does not say why`,
      );
    }
  }

  // The unspecified ones are exactly the codes the pack is silent about.
  const open = Object.entries(REQUIRED_PAYLOAD)
    .filter(([, requirement]) => requirement.unspecified === true)
    .map(([code]) => code);
  assert.equal(open.length, 9);
  assert.ok(!open.includes('no_version_for_date'));
  assert.ok(!open.includes('advice_boundary'));
});

test('a candidate link is bound to the whole coordinate, not to a date and a hash', () => {
  // Two different instruments can share a valid_from and a content hash. Checking only those
  // two let a candidate for one work link to an unrelated publisher and work.
  const elsewhere = readingUrl({
    publisher: 'other-publisher',
    work: 'other-work',
    validFrom: CANDIDATES[0].valid_from,
    hash: CANDIDATES[0].hash,
  });
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'ambiguous_version',
        sentence: 'Two states cover that date.',
        payload: {
          publisher: 'preview-synthetic',
          work: 'synthetic-preview-work',
          candidates: [{ ...CANDIDATES[0], href: elsewhere }, CANDIDATES[1]],
        },
      }),
    /resolves to a different object than the candidate names/,
  );

  // Only the publisher differs, so nothing but the publisher comparison can catch it.
  const otherPublisher = readingUrl({
    publisher: 'other-publisher',
    work: 'synthetic-preview-work',
    validFrom: CANDIDATES[0].valid_from,
    hash: CANDIDATES[0].hash,
  });
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'ambiguous_version',
        sentence: 'Two states cover that date.',
        payload: {
          publisher: 'preview-synthetic',
          work: 'synthetic-preview-work',
          candidates: [{ ...CANDIDATES[0], href: otherPublisher }, CANDIDATES[1]],
        },
      }),
    /resolves to a different object than the candidate names/,
  );

  assert.throws(
    () =>
      renderRefusalCard({
        code: 'ambiguous_version',
        sentence: 'Two states cover that date.',
        payload: { candidates: CANDIDATES },
      }),
    /must carry publisher, work/,
  );
});

test('one counter is not the referral list Decision 41 settles', () => {
  // Zero handoffs and one handoff are different failures. A rule tested only against zero
  // passes for a card that names a single service, which is the shape the decision rules out.
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'advice_boundary',
        sentence: 'I cannot apply the law to your situation.',
        governingText: GOVERNING,
        handoff: [HANDOFF[0]],
      }),
    /must name the referral list, not one counter/,
  );
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'advice_boundary',
        sentence: 'I cannot apply the law to your situation.',
        governingText: GOVERNING,
        handoff: HANDOFF[0],
      }),
    /must name the referral list, not one counter/,
  );
  assert.ok(
    renderRefusalCard({ code: 'advice_boundary', ...EXAMPLES.advice_boundary }).includes('<li>'),
  );
});

test('co-delivered text must declare what it is before it is headed', () => {
  for (const coverage of [undefined, 'in_full', 'complete', '']) {
    assert.throws(
      () =>
        renderRefusalCard({
          code: 'advice_boundary',
          sentence: 'I cannot apply the law to your situation.',
          governingText: { ...GOVERNING, coverage },
          handoff: HANDOFF,
        }),
      /must declare its coverage/,
      `coverage=${JSON.stringify(coverage)} was headed anyway`,
    );
  }
  // Completeness is a claim about the publisher's record, so it has to be dated.
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'advice_boundary',
        sentence: 'I cannot apply the law to your situation.',
        governingText: { ...GOVERNING, as_of: undefined },
        handoff: HANDOFF,
      }),
    /as_of is not a calendar date/,
  );
});

test('a candidate date that is not a date is refused', () => {
  for (const [field, value] of [
    ['publication_date', '2026-99-99'],
    ['publication_date', '2025-02-29'],
    ['valid_from', '2026-13-01'],
  ]) {
    assert.throws(
      () =>
        renderRefusalCard({
          code: 'ambiguous_version',
          sentence: 'Two states cover that date.',
          payload: {
            publisher: 'preview-synthetic',
            work: 'synthetic-preview-work',
            candidates: [{ ...CANDIDATES[0], [field]: value }, CANDIDATES[1]],
          },
        }),
      /not a calendar date/,
      `${field}=${value} was accepted`,
    );
  }
});

test('a profile identifier must be a value, not a shape', () => {
  for (const profiles of [['pdf/1', ''], ['pdf/1', null], ['pdf/1', 42], ['pdf/1', {}]]) {
    assert.throws(
      () =>
        renderRefusalCard({
          code: 'profiles_differ',
          sentence: 'The profiles differ.',
          payload: { profiles },
        }),
      /must be a nonempty value|carries scalars or lists of scalars/,
      `${JSON.stringify(profiles)} was accepted`,
    );
  }
});

test('a structured shape belongs to one code, not to a spelling', () => {
  // `candidates` means something on ambiguous_version and nothing anywhere else. Keyed by
  // spelling alone, any code could borrow the exemption and reach a reader as [object Object].
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'ambiguous_identifier',
        sentence: 'That citation matches more than one instrument.',
        payload: { candidates: [{ nested: 'object' }] },
      }),
    /carries scalars or lists of scalars/,
  );
});

test('the module says it is a preview contract rather than the final one', () => {
  // Decision 63 permits this slice only as an explicitly synthetic preview contract. Nine
  // payloads are unfrozen, and a partial table presented as final is a promise nobody made.
  assert.equal(CONTRACT_STATUS.kind, 'synthetic-preview');
  assert.equal(CONTRACT_STATUS.final, false);
  assert.match(CONTRACT_STATUS.reason, /348/);
});
