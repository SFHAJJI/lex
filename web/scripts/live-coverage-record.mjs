// Two coverage payloads fetched from the production MCP endpoint on 2026-09-01. DATA, NOT A TEST.
//
// WHAT THESE ARE. The bytes the V2 service sent for `coverage`, for both publishers, captured on
// the date above and kept verbatim. They are here because of what they once proved and what that
// proof cost to obtain, and deleting them would delete the evidence while leaving the conclusion.
//
// Nothing renders them. One test in `test/coverage.test.mjs` feeds them to the V3 reader and holds
// it to refusing them by name, which is the one thing these bytes can still prove about code: that
// the retired shape is an error rather than a page quietly missing its date.
//
// WHY THEY ARE NOT A TEST ANY MORE. They lived in `test/live-record.test.mjs`, where one test
// rendered them through `renderCoverage` and a second asserted their arithmetic. The V3 `coverage`
// answer has a different shape, so the reader that used to render them refuses them by name, and
// the rendering test could only have been kept by keeping the retired reader alive beside the new
// one. What remained -- the arithmetic assertions -- compare constants in this file against
// constants in this file. That is a test that renders nothing and goes green forever, which is the
// failure `live-record.test.mjs`'s own header warns about, turned on the file that quotes it.
//
// WHAT THEY PROVED, WHICH IS STILL TRUE AND IS THE REASON TO KEEP THEM. The facet reconciliation
// rules were a design decision that could have been got wrong in a way no synthetic fixture would
// have shown. The thin fixture had one language row, so under it the language table and the
// document-type table looked like the same kind of thing and one rule for both looked obviously
// right. Real data said otherwise and said it loudly:
//
//   * Luxembourg's language rows sum to 1,406 works against 1,402 held, because 3 works are
//     multilingual.
//   * The Union's sum to 4,652 versions against 2,366, because 1,212 of its works carry two
//     languages.
//   * Both publishers' document-type tables sum exactly, which is the other half of the same
//     decision and the reason the stricter rule is worth having where it applies.
//
// Had the partition rule been applied to languages, both live coverage pages would have refused to
// render. That measurement is what stopped it, and the V3 page inherits the lesson in a sharper
// form: its language table is a partition in two columns and an overlap in a third, so the rule is
// per column rather than per table.
//
// WHAT THEY ARE NOT. They are not V3 answers and cannot be made into any. The V3 `coverage` answer
// holds no build time, no publisher name, no document types and no versions; it holds members by
// outcome, capability cells, an operations census and a `not_held` list, none of which is here.
// The real-data sample for the V3 page belongs in `schemas/v3-platform/answer-samples.json` and
// will arrive when there is a real V3 mount to drive; until then that file states its own limit and
// this one states its own date.

/** Where these came from and when, so a reader never has to guess how old they are. */
export const CAPTURE_PROVENANCE = Object.freeze({
  captured_from: 'the production MCP endpoint',
  captured_on: '2026-09-01',
  service_generation: 'V2',
  operation: 'coverage',
  publishers: Object.freeze(['lu-legilux', 'eu-eurlex']),
  retired_from: 'web/test/live-record.test.mjs',
});

export const LIVE_LU_COVERAGE = Object.freeze({
  envelope: { freshness: { built_at: '2026-08-15T09:22:08Z', stamp_signature_valid: true } },
  publisher_name: 'Service central de legislation (Legilux)',
  works: 1402,
  scope_expected_works: 1402,
  build_inventory_status: 'complete',
  build_complete: true,
  build_issues: [],
  versions: 4656,
  valid_from_earliest: '1849-03-14',
  valid_from_latest: '2030-09-15',
  document_types: [
    { code: 'LOI', versions: 1536, versions_with_text: 1510 },
    { code: 'RGD', versions: 1200, versions_with_text: 1192 },
    { code: 'RECUEIL', versions: 752, versions_with_text: 72 },
    { code: 'CODE_RECUEIL', versions: 711, versions_with_text: 15 },
    { code: 'CODE', versions: 192, versions_with_text: 189 },
    { code: null, versions: 80, versions_with_text: 0 },
    { code: 'AGD', versions: 39, versions_with_text: 39 },
    { code: 'Constitution', versions: 37, versions_with_text: 37 },
    { code: 'RMIN', versions: 32, versions_with_text: 32 },
    { code: 'RI', versions: 21, versions_with_text: 21 },
    { code: 'AMIN', versions: 17, versions_with_text: 17 },
    { code: 'RGC', versions: 13, versions_with_text: 13 },
    { code: 'PA', versions: 6, versions_with_text: 6 },
    { code: 'CONV', versions: 4, versions_with_text: 4 },
    { code: 'ORD', versions: 3, versions_with_text: 3 },
    { code: 'AGC', versions: 3, versions_with_text: 3 },
    { code: 'TC', versions: 2, versions_with_text: 2 },
    { code: 'ARGD', versions: 2, versions_with_text: 2 },
    { code: 'ST', versions: 1, versions_with_text: 1 },
    { code: 'REG', versions: 1, versions_with_text: 1 },
    { code: 'RBCL', versions: 1, versions_with_text: 1 },
    { code: 'PROT', versions: 1, versions_with_text: 1 },
    { code: 'DIV', versions: 1, versions_with_text: 1 },
    { code: 'A', versions: 1, versions_with_text: 1 },
  ],
  document_types_total: 24,
  facets_truncated: false,
  languages: [
    { code: 'fr', works: 1402, versions: 4656 },
    { code: 'en', works: 2, versions: 2 },
    { code: 'lb', works: 1, versions: 1 },
    { code: 'de', works: 1, versions: 1 },
  ],
  text: { versions_with_text_served: 3163, versions_without_text: 1493 },
  known_gaps: [
    'never-consolidated LU acts (~24,579 as-published lois/RGD) are not ingested; ingestion scheduled, see coverage',
    "coverage density follows the publisher's own digitised consolidations: dense from 2017 onward; sparse before; isolated snapshots back to 1849; forward-dated to 2030",
  ],
});

export const LIVE_EU_COVERAGE = Object.freeze({
  envelope: { freshness: { built_at: '2026-08-15T09:01:06Z', stamp_signature_valid: true } },
  publisher_name: 'Publications Office of the EU (EUR-Lex / Cellar)',
  works: 1250,
  scope_expected_works: 1250,
  build_inventory_status: 'complete',
  build_complete: true,
  build_issues: [],
  versions: 2366,
  valid_from_earliest: '1957-03-25',
  valid_from_latest: '2029-03-29',
  document_types: [
    { code: 'DIR', versions: 774, versions_with_text: 774 },
    { code: 'REG', versions: 646, versions_with_text: 646 },
    { code: 'REG_DEL', versions: 457, versions_with_text: 457 },
    { code: 'REG_IMPL', versions: 383, versions_with_text: 383 },
    { code: 'TREATY', versions: 54, versions_with_text: 54 },
    { code: 'CORRIGENDUM', versions: 26, versions_with_text: 26 },
    { code: 'DIR_DEL', versions: 8, versions_with_text: 8 },
    { code: 'DEC_IMPL', versions: 7, versions_with_text: 7 },
    { code: 'DEC', versions: 7, versions_with_text: 7 },
    { code: 'DEC_ENTSCHEID', versions: 2, versions_with_text: 2 },
    { code: 'DIR_IMPL', versions: 1, versions_with_text: 1 },
    { code: 'DEC_DEL', versions: 1, versions_with_text: 1 },
  ],
  document_types_total: 12,
  facets_truncated: false,
  languages: [
    { code: 'fr', works: 1246, versions: 2360 },
    { code: 'en', works: 1216, versions: 2292 },
  ],
  text: { versions_with_text_served: 2366, versions_without_text: 0 },
  known_gaps: [
    '1,250 EU works from the reviewed scope are currently mounted; the wider acquis is not yet ingested, see coverage',
    "coverage follows the publisher's consolidation practice; future-dated versions are provisional",
  ],
});
