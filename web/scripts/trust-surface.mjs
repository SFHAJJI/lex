// The trust surface: the four components every V3 screen composes, rendered on one page.
//
// This is not a component gallery for its own sake. Contrast, focus order and reflow at 320
// CSS pixels are specified capabilities, WCAG 2.2 AA and product spec section 7, and the
// browser evidence run can only measure what a browser actually paints. Exercising these
// four here means a defect is found once, now, rather than twelve times after every screen
// has its own copy.
//
// The page carries the chrome language on `html` and the quoted statute's own language on
// the quoted span, which is product spec section 7 rule 3: a screen reader has to switch
// voices between English chrome and French law, and it can only do that if the markup says so.

import { page } from './render.mjs';
import { renderRefusalCard } from './refusal-card.mjs';
import { renderStateBanner } from './state-banner.mjs';
import { renderEnvelopeStrip, renderVerifyCluster } from './verify-cluster.mjs';
import { readingUrl } from './urls.mjs';

const DIGEST = '99b621c38dec11dcd362c0db35d9e9c090e62613cc5c20b0727c0b30fd39ce66';

const LU_ENVELOPE = {
  publisher_name: 'Service central de législation (Legilux)',
  timeline_semantics: 'publisher_applicability',
  freshness: { built_at: '2026-08-15T09:22:08Z', stamp_signature_valid: true },
  artifact: {
    corpus_commit: 'c087f9153a8cde5429965ffa897db001f3acdf09',
    code_commit: '27f0e02cb0da8e0fdf9f8322d3eef3b3ae09c776',
    manifest_set_id: '4dff34d9e957d469e87ca2b1dbe0e74b5a85519da3631b37ddf2ea81d3553b59',
    content_digest: 'c064f74a9827d610125d25c999f79df626cd987432aa110f2e05ce48388b5eef',
  },
};

const EU_ENVELOPE = {
  publisher_name: 'Publications Office of the EU (EUR-Lex / Cellar)',
  timeline_semantics: 'official_consolidation_state',
  freshness: { built_at: '2026-08-15T09:01:06Z', stamp_signature_valid: false },
  artifact: { code_commit: '27f0e02cb0da8e0fdf9f8322d3eef3b3ae09c776' },
};

const LU_STATE = {
  valid_from: '2007-09-01',
  valid_to: '2011-09-01',
  publication_date: '2007-08-08',
  observed_from: '2026-08-14T23:05:14Z',
};

const EU_STATE = {
  valid_from: '2016-05-04',
  valid_to: null,
  publication_date: '2016-05-04',
  observed_from: '2026-08-14T23:05:14Z',
};

// The 1993 banking law's 2025 window is the real case behind the interstitial: two publisher
// states cover one date and the publisher ranks neither, so neither may this page.
const AMBIGUOUS_CANDIDATES = [
  {
    valid_from: '2025-01-01',
    hash: DIGEST,
    publication_date: '2024-12-20',
    href: readingUrl({
      publisher: 'lu-legilux',
      work: 'loi-1993-04-05-n1',
      validFrom: '2025-01-01',
      hash: DIGEST,
    }),
  },
  {
    valid_from: '2025-01-01',
    hash: 'c064f74a9827d610125d25c999f79df626cd987432aa110f2e05ce48388b5eef',
    publication_date: '2024-12-27',
    href: readingUrl({
      publisher: 'lu-legilux',
      work: 'loi-1993-04-05-n1',
      validFrom: '2025-01-01',
      hash: 'c064f74a9827d610125d25c999f79df626cd987432aa110f2e05ce48388b5eef',
    }),
  },
];

const GOVERNING =
  'Art. L. 121-6. Le salarié incapable de travailler pour cause de maladie ou d’accident ' +
  'est obligé, le jour même de l’empêchement, d’en avertir personnellement ou par personne ' +
  'interposée l’employeur ou le représentant de celui-ci.';

export function renderTrustSurface() {
  const sections = [
    `<section class="surface-block"><h2>Legal time, Luxembourg</h2>${renderStateBanner({
      envelope: LU_ENVELOPE,
      state: LU_STATE,
    })}${renderVerifyCluster({
      sourceUri:
        'https://legilux.public.lu/eli/etat/leg/loi/2002/08/02/n2/consolide/20070901/fr',
      lexId: 'lu-legilux:loi-2002-08-02-n2:2007-09-01',
      hash: { kind: 'record_sha256', value: DIGEST },
    })}</section>`,

    `<section class="surface-block"><h2>Legal time, European Union, open ended</h2>${renderStateBanner(
      { envelope: EU_ENVELOPE, state: EU_STATE },
    )}</section>`,

    `<section class="surface-block"><h2>A refusal is an answer</h2>${renderRefusalCard({
      code: 'advice_boundary',
      sentence:
        'I can show you exactly what the published text says, at any date, and how it ' +
        'changed, with citations. I cannot apply the law to your situation.',
      governingText: GOVERNING,
      handoff: {
        label: 'Service d’accueil et d’information juridique',
        href: 'https://justice.public.lu/',
      },
    })}</section>`,

    `<section class="surface-block"><h2>An absence is an answer too</h2>${renderRefusalCard({
      code: 'no_version_for_date',
      sentence: 'No publisher state covers 2015-06-01.',
      payload: {
        history_begins: '2017-01-01',
        nearest_earlier: 'none held',
        nearest_later: '2017-01-01',
      },
    })}</section>`,

    `<section class="surface-block"><h2>An ambiguity is never resolved for you</h2>${renderRefusalCard(
      {
        code: 'ambiguous_version',
        sentence: 'Two publisher states cover 2025-03-01.',
        payload: { candidates: AMBIGUOUS_CANDIDATES },
      },
    )}</section>`,

    `<section class="surface-block"><h2>Freshness and identity</h2>${renderEnvelopeStrip({
      envelope: LU_ENVELOPE,
    })}${renderEnvelopeStrip({ envelope: EU_ENVELOPE })}</section>`,
  ].join('');

  return page({
    state: 'trust-surface',
    title: 'Trust surface',
    main: `      <p class="eyebrow">Trust surface</p>
      <h1>Lex V3</h1>
      <p>The components every screen composes. No legal data is loaded; the text below is a
        fixture and must not be used for legal research.</p>
      ${sections}`,
  });
}
