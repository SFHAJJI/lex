// The trust surface: the components every V3 screen composes, rendered on one page.
//
// This is not a component gallery for its own sake. Contrast, focus order, readable
// separation and reflow at 320 CSS pixels are specified capabilities, WCAG 2.2 AA and
// product spec section 7, and the browser evidence run can only measure what a browser
// actually paints. Exercising the components here means a defect is found once, now, rather
// than twelve times after every screen has its own copy.
//
// Everything on this page is synthetic, and structurally so. The publisher is
// `preview-synthetic` on `preview.invalid`, a TLD RFC 2606 reserves so that it can never
// resolve; the handoff is on `handoff.invalid`; the identities are named synthetic rather
// than dressed as digests; the quoted text says of itself that it has no legal authority.
//
// The first version of this page did not do that. It carried a real statutory excerpt, a
// real Legilux URI and real corpus and code commits under a banner reading "no legal data
// is loaded" and "describes no real legal record". Every one of those sentences was false,
// and a page whose own disclaimer is false is worse than one with no disclaimer, because a
// reader who checks the banner is misled by the act of checking.
//
// Two sections do use the real publisher keys `lu-legilux` and `eu-eurlex`. The key is what
// selects the authenticity rule being demonstrated, and no coordinate, URI, identity or law
// travels with it: the quoted text there is synthetic too.

import { page } from './render.mjs';
import { renderRefusalCard } from './refusal-card.mjs';
import { renderStateBanner } from './state-banner.mjs';
import { renderEnvelopeStrip, renderVerifyCluster } from './verify-cluster.mjs';
import { readingUrl } from './urls.mjs';
import { quotedLaw, renderLocalizationUnavailable, reviewedText } from './localization.mjs';

// Digests from the synthetic S0-05 capture: genuine digests of text that is not law.
const DIGEST = '5512d26f4fcdf962273e5f4ac59b893401b380a128a737ba718d3326cba0ed7e';
const CANDIDATE_A = 'dedbcbe0f53f5e2b41fd98551d5913b0ed56525ec35f7b26a6c0fa9eaad4ba3c';
const CANDIDATE_B = 'cfc9fe90f4f020e99f8da43c8d9e5f74c570eced2ad5d303c6dee7b485eb0212';

const PUBLISHER = 'preview-synthetic';
const WORK = 'synthetic-preview-work';

const APPLICABILITY_ENVELOPE = {
  publisher_name: 'Synthetic preview publisher, applicability semantics',
  timeline_semantics: 'publisher_applicability',
  freshness: { built_at: '2026-01-01T00:00:00Z', stamp_signature_valid: true },
  artifact: {
    corpus_commit: 'synthetic-corpus-commit',
    code_commit: 'synthetic-code-commit',
    manifest_set_id: 'synthetic-manifest-set',
    content_digest: 'synthetic-content-digest',
  },
};

const CONSOLIDATION_ENVELOPE = {
  publisher_name: 'Synthetic preview publisher, consolidation semantics',
  timeline_semantics: 'official_consolidation_state',
  freshness: { built_at: '2026-01-01T00:00:00Z', stamp_signature_valid: false },
  artifact: { code_commit: 'synthetic-code-commit' },
};

const CLOSED_STATE = {
  valid_from: '2001-01-01',
  valid_to: '2002-01-01',
  publication_date: '2000-12-01',
  observed_from: '2026-01-01T00:00:00Z',
};

const OPEN_STATE = {
  valid_from: '2003-01-01',
  valid_to: null,
  publication_date: '2002-12-01',
  observed_from: '2026-01-01T00:00:00Z',
};

const SYNTHETIC_LAW =
  'LEX V3 SYNTHETIC PREVIEW. Article 1. This text is synthetic, has no legal authority, ' +
  'and must not be used for legal research.';

const SYNTHETIC_LAW_FR =
  'APERCU SYNTHETIQUE LEX V3. Article 1er. Ce texte est synthetique, sans aucune autorite ' +
  'juridique, et ne doit pas servir a une recherche juridique.';

function candidate(hash, publicationDate) {
  return {
    valid_from: '2004-01-01',
    hash,
    publication_date: publicationDate,
    href: readingUrl({ publisher: PUBLISHER, work: WORK, validFrom: '2004-01-01', hash }),
  };
}

export function renderTrustSurface() {
  const sections = [
    `<section class="surface-block"><h2>Legal time, applicability semantics</h2>${renderStateBanner(
      { envelope: APPLICABILITY_ENVELOPE, state: CLOSED_STATE },
    )}${renderVerifyCluster({
      publisher: PUBLISHER,
      sourceUri: 'https://preview.invalid/synthetic-preview-work/2001-01-01',
      lexId: 'preview-synthetic:synthetic-preview-work:2001-01-01',
      hash: { kind: 'record_sha256', value: DIGEST },
    })}</section>`,

    `<section class="surface-block"><h2>Legal time, consolidation semantics, open ended</h2>${renderStateBanner(
      { envelope: CONSOLIDATION_ENVELOPE, state: OPEN_STATE },
    )}</section>`,

    `<section class="surface-block"><h2>A refusal is an answer</h2>${renderRefusalCard({
      code: 'advice_boundary',
      sentence:
        'I can show you exactly what the published text says, at any date, and how it ' +
        'changed, with citations. I cannot apply the law to your situation.',
      governingText: { publisher: PUBLISHER, language: 'en', text: SYNTHETIC_LAW },
      handoff: { label: 'Synthetic handoff counter', href: 'https://handoff.invalid/counter' },
    })}</section>`,

    `<section class="surface-block"><h2>An absence is an answer too</h2>${renderRefusalCard({
      code: 'no_version_for_date',
      sentence: 'No publisher state covers 1999-06-01.',
      payload: {
        history_begins: '2001-01-01',
        nearest_earlier: 'none held',
        nearest_later: '2001-01-01',
      },
    })}</section>`,

    `<section class="surface-block"><h2>An ambiguity is never resolved for you</h2>${renderRefusalCard(
      {
        code: 'ambiguous_version',
        sentence: 'Two publisher states cover 2004-06-01.',
        payload: {
          candidates: [candidate(CANDIDATE_A, '2003-12-01'), candidate(CANDIDATE_B, '2003-12-15')],
        },
      },
    )}</section>`,

    `<section class="surface-block"><h2>Authenticity, and what is not translated</h2>
      <p>The publisher key selects the rule. A publisher whose statute has one authentic
        language always carries the note; a publisher whose every language expression is
        equally authentic never does. The quoted text below is synthetic in both cases.</p>
      ${quotedLaw({
        publisher: 'lu-legilux',
        language: 'fr',
        text: SYNTHETIC_LAW_FR,
        noteLocale: 'en',
      })}
      ${quotedLaw({ publisher: 'eu-eurlex', language: 'en', text: SYNTHETIC_LAW })}
      <p>Asked for in Luxembourgish, the note is missing rather than substituted:</p>
      ${renderLocalizationUnavailable(reviewedText('law.lu.authenticity_note', 'lb'))}</section>`,

    `<section class="surface-block"><h2>Freshness and identity</h2>${renderEnvelopeStrip({
      envelope: APPLICABILITY_ENVELOPE,
    })}${renderEnvelopeStrip({ envelope: CONSOLIDATION_ENVELOPE })}</section>`,
  ].join('');

  return page({
    state: 'trust-surface',
    title: 'Trust surface',
    main: `      <p class="eyebrow">Trust surface</p>
      <h1>Lex V3</h1>
      <p>The components every screen composes. Every coordinate, identity, host and quoted
        passage on this page is synthetic; the hosts are under reserved TLDs that cannot
        resolve, and nothing here is law.</p>
      ${sections}`,
  });
}
