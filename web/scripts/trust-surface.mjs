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
// Authenticity is not demonstrated with a publisher key, because a publisher key does not
// select it. Decision 58 binds authenticity to the exact resource and forbids lifting it from
// a parent or a publisher, so the two quotations below carry their own typed evidence and the
// page shows what that evidence produces.

import { page } from './render.mjs';
import { renderRefusalCard } from './refusal-card.mjs';
import { renderStateBanner } from './state-banner.mjs';
import { renderEnvelopeStrip, renderVerifyCluster } from './verify-cluster.mjs';
import { readingUrl } from './urls.mjs';
import {
  RESOURCE_AUTHENTICITY_SCHEMA,
  quotedLaw,
  renderLocalizationUnavailable,
  renderUnofficialRendering,
  servableText,
} from './localization.mjs';

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
    index_builder_source_commit: 'synthetic-index-builder-commit',
    serving_runtime_source_commit: 'synthetic-serving-runtime-commit',
    manifest_set_id: 'synthetic-manifest-set',
    content_digest: 'synthetic-content-digest',
  },
};

const CONSOLIDATION_ENVELOPE = {
  publisher_name: 'Synthetic preview publisher, consolidation semantics',
  timeline_semantics: 'official_consolidation_state',
  freshness: { built_at: '2026-01-01T00:00:00Z', stamp_signature_valid: false },
  artifact: { index_builder_source_commit: 'synthetic-index-builder-commit' },
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

const SOLE_AUTHENTIC = {
  schema: RESOURCE_AUTHENTICITY_SCHEMA,
  resource_id: 'preview-synthetic:synthetic-preview-work:2001-01-01',
  publisher: 'preview-synthetic',
  official_uri: 'https://preview.invalid/synthetic-preview-work/2001-01-01',
  authentic_languages: ['fr'],
  basis: 'synthetic preview evidence, sole authentic language',
  asserted_by: 'synthetic preview publisher',
  observed_at: '2026-01-01T00:00:00Z',
};

const EQUALLY_AUTHENTIC = {
  schema: RESOURCE_AUTHENTICITY_SCHEMA,
  resource_id: 'preview-synthetic:synthetic-regulation:2001-01-01',
  publisher: 'preview-synthetic',
  official_uri: 'https://preview.invalid/synthetic-regulation/2001-01-01',
  authentic_languages: ['en', 'fr'],
  basis: 'synthetic preview evidence, every expression equally authentic',
  asserted_by: 'synthetic preview publisher',
  observed_at: '2026-01-01T00:00:00Z',
};

const SYNTHETIC_LAW_FR =
  'APERCU SYNTHETIQUE LEX V3. Article 1er. Ce texte est synthetique, sans aucune autorite ' +
  'juridique, et ne doit pas servir a une recherche juridique.';

function candidate(hash, publicationDate, withdrawn = false) {
  return {
    valid_from: '2004-01-01',
    hash,
    publication_date: publicationDate,
    withdrawn,
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
      governingText: {
        resourceId: EQUALLY_AUTHENTIC.resource_id,
        authenticity: EQUALLY_AUTHENTIC,
        language: 'en',
        text: SYNTHETIC_LAW,
        coverage: 'complete_provision',
        as_of: '2001-01-01',
      },
      handoff: [
        { label: 'Synthetic counter one', href: 'https://handoff.invalid/one' },
        { label: 'Synthetic counter two', href: 'https://handoff.invalid/two' },
        { label: 'A lawyer', href: 'https://handoff.invalid/lawyer' },
      ],
    })}</section>`,

    `<section class="surface-block"><h2>An absence is an answer too</h2>${renderRefusalCard({
      code: 'no_version_for_date',
      sentence: 'No publisher state covers 1999-06-01.',
      payload: {
        history_begins: '2001-01-01',
        nearest_earlier: null,
        nearest_later: '2001-01-01',
        what_would_answer: ['new_official_observation'],
        asserts_absence_of_law: false,
      },
    })}</section>`,

    `<section class="surface-block"><h2>An ambiguity is never resolved for you</h2>${renderRefusalCard(
      {
        code: 'ambiguous_version',
        sentence: 'Two publisher states cover 2004-06-01.',
        payload: {
          publisher: PUBLISHER,
          work: WORK,
          candidates: [candidate(CANDIDATE_A, '2003-12-01'), candidate(CANDIDATE_B, '2003-12-15')],
        },
      },
    )}</section>`,

    `<section class="surface-block"><h2>Authenticity, and what is not translated</h2>
      <p>The resource's own evidence decides. A resource with one authentic language always
        carries the note, naming that language and the ground for the claim; a resource whose
        every held expression is equally authentic never does, because there the note would be
        false. No publisher key is consulted, and a quotation with no evidence is refused
        rather than rendered unqualified.</p>
      ${quotedLaw({
        resourceId: SOLE_AUTHENTIC.resource_id,
        authenticity: SOLE_AUTHENTIC,
        language: 'fr',
        text: SYNTHETIC_LAW_FR,
        noteLocale: 'en',
      })}
      ${quotedLaw({
        resourceId: EQUALLY_AUTHENTIC.resource_id,
        authenticity: EQUALLY_AUTHENTIC,
        language: 'en',
        text: SYNTHETIC_LAW,
      })}
      <p>A body that is not the authentic text has its own place, labelled, with the route
        to the text that does count:</p>
      ${renderUnofficialRendering({
        resourceId: SOLE_AUTHENTIC.resource_id,
        authenticity: SOLE_AUTHENTIC,
        language: 'en',
        text: 'An English rendering of the synthetic French text above.',
        publisher: PUBLISHER,
        officialUri: 'https://preview.invalid/synthetic-preview-work/2001-01-01',
      })}
      <p>Asked for in Luxembourgish, the note is missing rather than substituted:</p>
      ${renderLocalizationUnavailable(servableText('law.sole_authentic_note', 'lb'))}</section>`,

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
