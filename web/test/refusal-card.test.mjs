import assert from 'node:assert/strict';
import test from 'node:test';

import {
  ABSENCE_CODES,
  CONTRACT_STATUS,
  REFUSAL_CODES,
  REQUIRED_PAYLOAD,
  RETRYABLE,
  WHAT_WOULD_ANSWER,
  renderRefusalCard,
  renderSupersededState,
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
  resourceId: AUTHENTICITY.resource_id,
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

const HASH_A = 'dedbcbe0f53f5e2b41fd98551d5913b0ed56525ec35f7b26a6c0fa9eaad4ba3c';
const HASH_B = 'cfc9fe90f4f020e99f8da43c8d9e5f74c570eced2ad5d303c6dee7b485eb0212';

function candidate(validFrom, hash, publicationDate, withdrawn = false) {
  return {
    valid_from: validFrom,
    hash,
    publication_date: publicationDate,
    withdrawn,
    href: readingUrl({
      publisher: 'preview-synthetic',
      work: 'synthetic-preview-work',
      validFrom,
      hash,
    }),
  };
}

const CANDIDATES = [
  candidate('2004-01-01', HASH_A, '2003-12-01'),
  candidate('2004-01-01', HASH_B, '2003-12-15'),
];

/**
 * One worked example per code, checked against the registry as a set. It moves into the
 * product as the refusal catalog page in the slice after this one; here it is the fixture.
 */
const EXAMPLES = Object.freeze({
  identifier_unknown: {
    sentence: 'That identifier does not resolve to a held work.',
    payload: {
      population_disclosure:
        '1,402 consolidated LU works and 1,250 EU works are searchable; 23,370 ' +
        'never-consolidated LU acts, of a 24,622 LOI and RGD population, are not.',
      what_would_answer: ['corrected_identifier', 'expanded_official_scope'],
      asserts_absence_of_law: false,
    },
  },
  ambiguous_identifier: {
    sentence: 'That citation matches more than one instrument.',
    payload: { candidates_named: 'synthetic-preview-work-a, synthetic-preview-work-b' },
  },
  out_of_corpus_scope: {
    sentence: 'That instrument is outside the reviewed corpus.',
    payload: {
      population_disclosure: 'The reviewed corpus holds Legilux and EUR-Lex legislation only.',
      what_would_answer: ['expanded_official_scope'],
      asserts_absence_of_law: false,
    },
  },
  no_version_for_date: {
    sentence: 'No publisher state covers 1999-06-01.',
    payload: {
      history_begins: '2001-01-01',
      nearest_earlier: 'none held',
      nearest_later: '2001-01-01',
      what_would_answer: ['new_official_observation'],
      asserts_absence_of_law: false,
    },
  },
  ambiguous_version: {
    sentence: 'Two publisher states cover 2004-06-01.',
    payload: {
      publisher: 'preview-synthetic',
      work: 'synthetic-preview-work',
      candidates: CANDIDATES,
    },
  },
  anchor_not_in_version: {
    sentence: 'art_1 is not an anchor in this version.',
    payload: {
      nearest_anchors: ['art_1er', 'art_1er__2'],
      what_would_answer: ['corrected_identifier'],
      asserts_absence_of_law: false,
    },
  },
  language_not_available: {
    sentence: 'This work is held in French only.',
    payload: { languages_held: ['fr'] },
  },
  text_not_available: {
    sentence: 'The publisher records this state but serves no text for it.',
    payload: {
      official_uri: 'https://preview.invalid/synthetic-preview-work/2001-01-01',
      gazette_chain: 'Synthetic gazette A 2001 no 1',
      what_would_answer: ['new_official_observation'],
      asserts_absence_of_law: false,
    },
  },
  text_withheld: {
    sentence: 'The publisher licence does not permit serving this text.',
    payload: { licence: 'synthetic-licence' },
  },
  format_not_available: {
    sentence: 'This state is held as PDF only.',
    payload: { formats_held: ['pdf'] },
  },
  profiles_differ: {
    sentence: 'These two states came from different extraction profiles.',
    payload: { profiles: ['synthetic-pdf/1', 'synthetic-akn/1'] },
  },
  not_transposable: {
    sentence: 'A regulation is not transposed.',
    payload: { execution_acts: ['synthetic-execution-act'] },
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
    payload: { snapshots_held: '2026-01-01' },
  },
  upstream_unreachable: {
    sentence: 'The publisher did not answer.',
    payload: { host: 'preview.invalid' },
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
  assert.ok(card.includes('applicable from 2004-01-01, hash <code>dedbcbe0</code>, published'));
  assert.ok(card.includes('cfc9fe90'));
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
  assert.ok(card.includes('synthetic-pdf/1'));
  assert.ok(card.includes('synthetic-akn/1'));
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
  assert.ok(card.includes('<code>art_1er</code>'), 'nearest anchors were not rendered as chips');
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
      resourceId: 'preview-synthetic:synthetic-french-act:2001-01-01',
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
      population_disclosure: '23,370 never-consolidated LU acts are not <img src=x onerror=alert(1)>',
      what_would_answer: ['corrected_identifier'],
      asserts_absence_of_law: false,
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

test('a withdrawn-superseded pair is not a live ambiguity', () => {
  // 30-FINAL-VERDICT splits this population per attack 4.4. The census that produced the
  // original requirement conflated two populations with opposite correct behaviours: four
  // live-ambiguity pairs need the interstitial, twelve withdrawn-superseded pairs need the
  // live state with the sibling disclosed. Forcing a choice on the second invents an
  // ambiguity the publisher already resolved.
  const superseded = candidate('2004-01-01', HASH_B, '2003-12-15', true);
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'ambiguous_version',
        sentence: 'Two states cover that date.',
        payload: {
          publisher: 'preview-synthetic',
          work: 'synthetic-preview-work',
          candidates: [CANDIDATES[0], superseded],
        },
      }),
    /not the live ambiguity the interstitial is for/,
  );

  assert.throws(
    () =>
      renderRefusalCard({
        code: 'ambiguous_version',
        sentence: 'Two states cover that date.',
        payload: {
          publisher: 'preview-synthetic',
          work: 'synthetic-preview-work',
          // Two live states, so the live-count rule passes and only the declaration rule
          // can catch the third.
          candidates: [
            CANDIDATES[0],
            CANDIDATES[1],
            { ...candidate('2004-01-01', HASH_A, '2003-12-20'), withdrawn: undefined },
          ],
        },
      }),
    /must declare whether the publisher withdrew it/,
  );
});

test('the superseded pair discloses the sibling instead of asking for a choice', () => {
  const superseded = candidate('2004-01-01', HASH_B, '2003-12-15', true);
  const html = renderSupersededState({
    publisher: 'preview-synthetic',
    work: 'synthetic-preview-work',
    live: CANDIDATES[0],
    withdrawn: [superseded],
  });
  assert.ok(html.includes('The state the publisher holds'));
  assert.ok(html.includes('dedbcbe0'));
  assert.ok(html.includes('cfc9fe90'), 'the withdrawn sibling was hidden rather than disclosed');
  assert.ok(html.includes('no choice is asked of'));
  // It is not a refusal and must not borrow the refusal card's shape.
  assert.ok(!html.includes('refusal-card'));

  assert.throws(
    () =>
      renderSupersededState({
        publisher: 'preview-synthetic',
        work: 'synthetic-preview-work',
        live: superseded,
        withdrawn: [CANDIDATES[0]],
      }),
    /must be the one that is not withdrawn/,
  );
  assert.throws(
    () =>
      renderSupersededState({
        publisher: 'preview-synthetic',
        work: 'synthetic-preview-work',
        live: CANDIDATES[0],
        withdrawn: [],
      }),
    /exists to disclose a withdrawn sibling/,
  );
});

test('an absence must say what would answer it, from the closed contract vocabulary', () => {
  // Not invented here. The vocabulary, the non-emptiness, the uniqueness and the declared
  // enum order are all in schemas/v3-synthetic-preview, and the array is required on every
  // refusal the shipped contract describes.
  assert.deepEqual(
    [...WHAT_WOULD_ANSWER],
    ['corrected_identifier', 'new_official_observation', 'expanded_official_scope'],
  );

  for (const code of ABSENCE_CODES) {
    const example = EXAMPLES[code];
    const { what_would_answer: _drop, ...without } = example.payload;
    assert.throws(
      () => renderRefusalCard({ ...example, code, payload: without }),
      /must say what would answer it/,
      `${code} rendered with no route out`,
    );
    assert.throws(
      () =>
        renderRefusalCard({
          ...example,
          code,
          payload: { ...example.payload, what_would_answer: ['a_new_law'] },
        }),
      /is not in the what_would_answer vocabulary/,
      `${code} accepted an invented route`,
    );
  }

  // The same set in two orders is two different responses, so the order is the contract's.
  assert.throws(
    () =>
      renderRefusalCard({
        ...EXAMPLES.identifier_unknown,
        code: 'identifier_unknown',
        payload: {
          ...EXAMPLES.identifier_unknown.payload,
          what_would_answer: ['expanded_official_scope', 'corrected_identifier'],
        },
      }),
    /must be in declared enum order/,
  );
  assert.throws(
    () =>
      renderRefusalCard({
        ...EXAMPLES.identifier_unknown,
        code: 'identifier_unknown',
        payload: {
          ...EXAMPLES.identifier_unknown.payload,
          what_would_answer: ['corrected_identifier', 'corrected_identifier'],
        },
      }),
    /repeats a what_would_answer value/,
  );
});

test('an absence can never assert that the law does not exist', () => {
  // The contract pins asserts_absence_of_law to the constant false. It is the product's
  // oldest invariant and the one a reader is most likely to get wrong on their own.
  for (const value of [true, 'false', 0]) {
    assert.throws(
      () =>
        renderRefusalCard({
          ...EXAMPLES.no_version_for_date,
          code: 'no_version_for_date',
          payload: { ...EXAMPLES.no_version_for_date.payload, asserts_absence_of_law: value },
        }),
      /must carry asserts_absence_of_law: false/,
      `${JSON.stringify(value)} was accepted`,
    );
  }
  // Absent entirely, which is the realistic way it goes missing.
  const { asserts_absence_of_law: _drop, ...without } = EXAMPLES.no_version_for_date.payload;
  assert.throws(
    () =>
      renderRefusalCard({
        ...EXAMPLES.no_version_for_date,
        code: 'no_version_for_date',
        payload: without,
      }),
    /must carry asserts_absence_of_law: false/,
  );

  const html = renderRefusalCard({
    code: 'no_version_for_date',
    ...EXAMPLES.no_version_for_date,
  });
  assert.ok(html.includes('not evidence that the instrument or the law does not exist'));
  assert.ok(html.includes('What would answer this'));
  assert.ok(html.includes('a new observation, if the publisher publishes this'));
});

test('a code that is not an absence is not made to carry absence evidence', () => {
  // text_withheld is a licence fact, not an absence: the record exists and is held.
  assert.ok(!ABSENCE_CODES.includes('text_withheld'));
  const html = renderRefusalCard({ code: 'text_withheld', ...EXAMPLES.text_withheld });
  assert.ok(!html.includes('What would answer this'));
});

test('a candidate link carries no anchor, because the choice is between states', () => {
  // Links ending in #art_1 and #art_2 both passed while the candidate declared no anchor,
  // so two candidates that looked identical led to different provisions.
  const withAnchor = `${CANDIDATES[0].href}#art_1`;
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'ambiguous_version',
        sentence: 'Two states cover that date.',
        payload: {
          publisher: 'preview-synthetic',
          work: 'synthetic-preview-work',
          candidates: [{ ...CANDIDATES[0], href: withAnchor }, CANDIDATES[1]],
        },
      }),
    /resolves to a different object than the candidate names/,
  );
});

test('a declared payload contract is an allowlist, not a minimum', () => {
  // It was a minimum, so ambiguous_version accepted and rendered `selected: true`, which is
  // exactly the default this refusal exists to refuse.
  assert.throws(
    () =>
      renderRefusalCard({
        code: 'ambiguous_version',
        sentence: 'Two states cover that date.',
        payload: {
          publisher: 'preview-synthetic',
          work: 'synthetic-preview-work',
          candidates: CANDIDATES,
          selected: true,
        },
      }),
    /carries undeclared payload selected/,
  );
  assert.throws(
    () =>
      renderRefusalCard({
        ...EXAMPLES.no_version_for_date,
        code: 'no_version_for_date',
        payload: { ...EXAMPLES.no_version_for_date.payload, nearest_anchors: ['art_1'] },
      }),
    /carries undeclared payload nearest_anchors/,
  );

  // The nine variants Decision 63 defers stay open, because closing a set nobody has
  // specified would be inventing the contract rather than implementing it.
  assert.ok(
    renderRefusalCard({
      code: 'text_withheld',
      sentence: 'The publisher licence does not permit serving this text.',
      payload: { licence: 'synthetic-licence', anything_else: 'still allowed' },
    }).includes('still allowed'),
  );
});
