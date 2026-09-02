// The provenance rules, held against payloads the live service actually returned.
//
// Fetched 2026-09-01 from the deployed MCP `provenance` and `coverage` tools: two Luxembourg
// records, one of them with no held text, one more Luxembourg record whose title carries the
// accents, colon and trailing full stop the synthetic fixture has none of, the Union GDPR
// consolidation twice (unfiltered and narrowed to French), the refusal that comes back for an
// identifier this corpus holds nothing at, and the corpus census the disclosure figures come
// from. Re-serialised as canonical JSON in the order the service emitted, then digested, so a
// capture that changes later is refused rather than quietly believed.
//
// Five defects were unreachable from a fixture and are reachable from these:
//
//  1. The stamp signature is shared. `lu-recueil`, `lu-loi-1915` and `lu-no-text` are three
//     different records and carry one byte-identical signature; the Union records carry a
//     different one, also shared. A page that printed "signature valid" beside a record digest
//     would assert record authenticity on evidence that does not bind to the record.
//
//  2. `language=fr` narrows `observations` and leaves `document` alone. `eu-gdpr-fr` has an
//     English document, an English `body_sha256`, and a French-only observation list, and
//     nothing in it says it was filtered. A renderer that paired "the open observation" with
//     "the record's body" would show two different texts as one.
//
//  3. Observation windows can be zero wide, and two can open in the same second. `eu-gdpr`
//     carries three. A `<` where this module has `<=` refuses a real Union record.
//
//  4. `unknown_work` is the live refusal status and is not in REFUSAL_CODES. The comment in
//     `refusal-card.mjs` says the live service uses `identifier_unknown`; it does not. The
//     same status also comes back for a work this corpus holds at other states, so the page
//     may not say the work is unknown.
//
//  5. A record can hold no text at all: `extraction_profile` null, `body_sha256` null and an
//     empty observation list, on a work this corpus does hold.
//
// Every assertion below was watched failing before it was trusted.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { verifyCapture } from '../scripts/captured-envelopes.mjs';
import { REFUSAL_CODES, WHAT_WOULD_ANSWER } from '../scripts/refusal-card.mjs';
import {
  NO_TEXT_NOTE,
  PROVENANCE_REFUSAL_STATUSES,
  STAMP_SCOPE_NOTE,
  narrowedNote,
  provenancePageName,
  readProvenance,
  renderProvenance,
} from '../scripts/provenance.mjs';
import { PREVIEW_RECORDS, provenancePreviewPages } from '../scripts/provenance-preview.mjs';
import { renderReadingPreview } from '../scripts/reading-preview.mjs';
import { renderTrustSurface } from '../scripts/trust-surface.mjs';
import { routeSchemePath } from '../scripts/browser-evidence.mjs';
import { Provenance } from '../.react-build/app.mjs';

/**
 * The payloads, exactly as captured, each with the digest of its own bytes.
 *
 * Stored as escaped string literals rather than object literals for the reason
 * `captured-envelopes.mjs` gives: a template literal would reinterpret the escapes inside the
 * PEM key and silently change the digest, which is the one property that makes a captured
 * fixture worth having.
 */
const LIVE = Object.freeze({
  "lu-recueil.json": {
    text:
      "{\n  \"envelope\": {\n    \"publisher\": \"lu-legilux\",\n    \"tier\": \"A\",\n    \"history_begins\": \"publisher\",\n    \"status\": \"ok\",\n    \"provisional\": false,\n    \"freshness\": {\n      \"corpus_commit\": \"c087f9153a8cde5429965ffa897db001f3acdf09\",\n      \"built_at\": \"2026-08-15T09:22:08Z\",\n      \"last_confirmed_at\": \"2026-08-15T09:22:08Z\",\n      \"last_confirmed_source\": \"index-build\",\n      \"stamp_signature_valid\": true\n    },\n    \"jurisdiction\": \"LU\",\n    \"timeline_semantics\": \"publisher_applicability\",\n    \"artifact\": {\n      \"manifest_set_id\": \"4dff34d9e957d469e87ca2b1dbe0e74b5a85519da3631b37ddf2ea81d3553b59\",\n      \"content_digest\": \"c064f74a9827d610125d25c999f79df626cd987432aa110f2e05ce48388b5eef\",\n      \"code_commit\": \"27f0e02cb0da8e0fdf9f8322d3eef3b3ae09c776\",\n      \"index_format\": null\n    }\n  },\n  \"document\": {\n    \"lex_id\": \"lu-legilux:recueil-protection_donnees:2023-09-05--7e00585ca7d30427837996540e0da3bdfe1c141894e2d41e1202f0938f7c832f\",\n    \"version_key\": \"2023-09-05--7e00585ca7d30427837996540e0da3bdfe1c141894e2d41e1202f0938f7c832f\",\n    \"work\": \"recueil-protection_donnees\",\n    \"work_identifier\": \"http://data.legilux.public.lu/eli/etat/leg/recueil/protection_donnees\",\n    \"document_type\": \"RECUEIL\",\n    \"extraction_profile\": \"akn-lu/2\",\n    \"language\": \"fr\",\n    \"valid_from\": \"2023-09-05\",\n    \"valid_to\": null,\n    \"valid_time_source\": \"publisher\",\n    \"publication_date\": \"2024-03-29\",\n    \"title\": \"Protection des données\",\n    \"withdrawn\": false,\n    \"text_available\": true,\n    \"record_sha256\": \"186039205ec6bac550fdb57de1be0ae4218abb6f3da8b06f4f0ed76caa690b88\",\n    \"body_sha256\": \"fbc23154d6fbcffd6ab7119a55ddb9f103de180da6d713e0b19c1165544d3f9f\",\n    \"source_uri\": \"https://legilux.public.lu/eli/etat/leg/recueil/protection_donnees/20230905/fr\",\n    \"observed_from\": \"2026-08-14T23:05:14Z\",\n    \"text\": null,\n    \"permalink\": \"https://law.soufien.lu/lu-legilux/recueil-protection_donnees/2023-09-05--7e00585ca7d30427837996540e0da3bdfe1c141894e2d41e1202f0938f7c832f\"\n  },\n  \"truncated\": false,\n  \"events\": [\n    {\n      \"event\": \"first_sighting\",\n      \"scope\": \"version\",\n      \"observed_from\": \"2026-08-14T23:05:14Z\",\n      \"detail\": null\n    }\n  ],\n  \"observations\": [\n    {\n      \"language\": \"fr\",\n      \"expr_valid_from\": \"2023-09-05\",\n      \"sha256\": \"fbc23154d6fbcffd6ab7119a55ddb9f103de180da6d713e0b19c1165544d3f9f\",\n      \"observed_from\": \"2026-08-14T23:05:14Z\",\n      \"observed_to\": null\n    }\n  ],\n  \"stamp\": {\n    \"signature_valid\": true,\n    \"algorithm\": \"ECDSA-P256-SHA256\",\n    \"public_key\": \"-----BEGIN PUBLIC KEY-----\\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEl15vnowBerveXWu1D4KHtdOmg1d7\\n4Tf6bivetAx2/fseLfHpFHZaoLvPMAgs24nfe1DEU7Ym8qdfrvwvib/BNw==\\n-----END PUBLIC KEY-----\",\n    \"signature\": \"2rvpvZiaSPteIafX7AwJFYOC8kNDBaFSWhQbDJt+88LJixjfo5AGF17dihOFh6fraUvFNYF1mua27PvhW6k/hw==\"\n  }\n}",
    sha256: "17008bac12c1b4c07aaf0b251cb2fc656aa76f98d7e60e32de314fd3cb562ca2",
    bytes: 2831,
  },
  "lu-loi-1915.json": {
    text:
      "{\n  \"envelope\": {\n    \"publisher\": \"lu-legilux\",\n    \"tier\": \"A\",\n    \"history_begins\": \"publisher\",\n    \"status\": \"ok\",\n    \"provisional\": false,\n    \"freshness\": {\n      \"corpus_commit\": \"c087f9153a8cde5429965ffa897db001f3acdf09\",\n      \"built_at\": \"2026-08-15T09:22:08Z\",\n      \"last_confirmed_at\": \"2026-08-15T09:22:08Z\",\n      \"last_confirmed_source\": \"index-build\",\n      \"stamp_signature_valid\": true\n    },\n    \"jurisdiction\": \"LU\",\n    \"timeline_semantics\": \"publisher_applicability\",\n    \"artifact\": {\n      \"manifest_set_id\": \"4dff34d9e957d469e87ca2b1dbe0e74b5a85519da3631b37ddf2ea81d3553b59\",\n      \"content_digest\": \"c064f74a9827d610125d25c999f79df626cd987432aa110f2e05ce48388b5eef\",\n      \"code_commit\": \"27f0e02cb0da8e0fdf9f8322d3eef3b3ae09c776\",\n      \"index_format\": null\n    }\n  },\n  \"document\": {\n    \"lex_id\": \"lu-legilux:loi-1915-08-10-n1:2026-06-02--7f9e57c7e523ad4bc9005a184dc2765645925e1f2c4dc46820d1974780853471\",\n    \"version_key\": \"2026-06-02--7f9e57c7e523ad4bc9005a184dc2765645925e1f2c4dc46820d1974780853471\",\n    \"work\": \"loi-1915-08-10-n1\",\n    \"work_identifier\": \"http://data.legilux.public.lu/eli/etat/leg/loi/1915/08/10/n1\",\n    \"document_type\": \"LOI\",\n    \"extraction_profile\": \"akn-lu/2\",\n    \"language\": \"fr\",\n    \"valid_from\": \"2026-06-02\",\n    \"valid_to\": null,\n    \"valid_time_source\": \"publisher\",\n    \"publication_date\": \"2026-06-16\",\n    \"title\": \"Version consolidée applicable au 02/06/2026 : Loi du 10 août 1915 concernant les sociétés commerciales.\",\n    \"withdrawn\": false,\n    \"text_available\": true,\n    \"record_sha256\": \"8b11fdb59c8f3d6be9b29bb869597ddeb1037a18ffb48f372ffd02e2456160d5\",\n    \"body_sha256\": \"b56dc478c25829041d741717114727040cda6b22441f44eac8f26f0d0f69f924\",\n    \"source_uri\": \"https://legilux.public.lu/eli/etat/leg/loi/1915/08/10/n1/consolide/20260602/fr\",\n    \"observed_from\": \"2026-08-14T23:05:14Z\",\n    \"text\": null,\n    \"permalink\": \"https://law.soufien.lu/lu-legilux/loi-1915-08-10-n1/2026-06-02--7f9e57c7e523ad4bc9005a184dc2765645925e1f2c4dc46820d1974780853471\"\n  },\n  \"truncated\": false,\n  \"events\": [\n    {\n      \"event\": \"first_sighting\",\n      \"scope\": \"version\",\n      \"observed_from\": \"2026-08-14T23:05:14Z\",\n      \"detail\": null\n    }\n  ],\n  \"observations\": [\n    {\n      \"language\": \"fr\",\n      \"expr_valid_from\": \"2026-06-02\",\n      \"sha256\": \"b56dc478c25829041d741717114727040cda6b22441f44eac8f26f0d0f69f924\",\n      \"observed_from\": \"2026-08-14T23:05:14Z\",\n      \"observed_to\": null\n    }\n  ],\n  \"stamp\": {\n    \"signature_valid\": true,\n    \"algorithm\": \"ECDSA-P256-SHA256\",\n    \"public_key\": \"-----BEGIN PUBLIC KEY-----\\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEl15vnowBerveXWu1D4KHtdOmg1d7\\n4Tf6bivetAx2/fseLfHpFHZaoLvPMAgs24nfe1DEU7Ym8qdfrvwvib/BNw==\\n-----END PUBLIC KEY-----\",\n    \"signature\": \"2rvpvZiaSPteIafX7AwJFYOC8kNDBaFSWhQbDJt+88LJixjfo5AGF17dihOFh6fraUvFNYF1mua27PvhW6k/hw==\"\n  }\n}",
    sha256: "4d91e61e32ea89bed2d6f4d66f37a65a2379cfc23e3350980e54e989c6603e07",
    bytes: 2876,
  },
  "lu-no-text.json": {
    text:
      "{\n  \"envelope\": {\n    \"publisher\": \"lu-legilux\",\n    \"tier\": \"A\",\n    \"history_begins\": \"publisher\",\n    \"status\": \"ok\",\n    \"provisional\": false,\n    \"freshness\": {\n      \"corpus_commit\": \"c087f9153a8cde5429965ffa897db001f3acdf09\",\n      \"built_at\": \"2026-08-15T09:22:08Z\",\n      \"last_confirmed_at\": \"2026-08-15T09:22:08Z\",\n      \"last_confirmed_source\": \"index-build\",\n      \"stamp_signature_valid\": true\n    },\n    \"jurisdiction\": \"LU\",\n    \"timeline_semantics\": \"publisher_applicability\",\n    \"artifact\": {\n      \"manifest_set_id\": \"4dff34d9e957d469e87ca2b1dbe0e74b5a85519da3631b37ddf2ea81d3553b59\",\n      \"content_digest\": \"c064f74a9827d610125d25c999f79df626cd987432aa110f2e05ce48388b5eef\",\n      \"code_commit\": \"27f0e02cb0da8e0fdf9f8322d3eef3b3ae09c776\",\n      \"index_format\": null\n    }\n  },\n  \"document\": {\n    \"lex_id\": \"lu-legilux:code-environnement:2026-08-08--5b7581655f9100ff5e32ba7806d0ef095f4c17cdbcad2f3447f85dfaebf1e04c\",\n    \"version_key\": \"2026-08-08--5b7581655f9100ff5e32ba7806d0ef095f4c17cdbcad2f3447f85dfaebf1e04c\",\n    \"work\": \"code-environnement\",\n    \"work_identifier\": \"http://data.legilux.public.lu/eli/etat/leg/code/environnement\",\n    \"document_type\": \"CODE_RECUEIL\",\n    \"extraction_profile\": null,\n    \"language\": \"fr\",\n    \"valid_from\": \"2026-08-08\",\n    \"valid_to\": null,\n    \"valid_time_source\": \"publisher\",\n    \"publication_date\": \"2026-08-07\",\n    \"title\": \"Code de l'environnement\",\n    \"withdrawn\": false,\n    \"text_available\": false,\n    \"record_sha256\": \"e1d6f9fd3e2a85a79cadbbb474ef229e5d3026138fff2f4863cca5497f9fd540\",\n    \"body_sha256\": null,\n    \"source_uri\": \"https://legilux.public.lu/eli/etat/leg/code/environnement/20260808/fr\",\n    \"observed_from\": \"2026-08-14T23:05:14Z\",\n    \"text\": null,\n    \"permalink\": \"https://law.soufien.lu/lu-legilux/code-environnement/2026-08-08--5b7581655f9100ff5e32ba7806d0ef095f4c17cdbcad2f3447f85dfaebf1e04c\"\n  },\n  \"truncated\": false,\n  \"events\": [\n    {\n      \"event\": \"first_sighting\",\n      \"scope\": \"version\",\n      \"observed_from\": \"2026-08-14T23:05:14Z\",\n      \"detail\": null\n    }\n  ],\n  \"observations\": [],\n  \"stamp\": {\n    \"signature_valid\": true,\n    \"algorithm\": \"ECDSA-P256-SHA256\",\n    \"public_key\": \"-----BEGIN PUBLIC KEY-----\\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEl15vnowBerveXWu1D4KHtdOmg1d7\\n4Tf6bivetAx2/fseLfHpFHZaoLvPMAgs24nfe1DEU7Ym8qdfrvwvib/BNw==\\n-----END PUBLIC KEY-----\",\n    \"signature\": \"2rvpvZiaSPteIafX7AwJFYOC8kNDBaFSWhQbDJt+88LJixjfo5AGF17dihOFh6fraUvFNYF1mua27PvhW6k/hw==\"\n  }\n}",
    sha256: "4615c62528c07f68ef79e7bfde372a2b2696308e5332b8564def93b61f75bb46",
    bytes: 2494,
  },
  "eu-gdpr.json": {
    text:
      "{\n  \"envelope\": {\n    \"publisher\": \"eu-eurlex\",\n    \"tier\": \"A\",\n    \"history_begins\": \"publisher\",\n    \"status\": \"ok\",\n    \"provisional\": false,\n    \"freshness\": {\n      \"corpus_commit\": \"e9c4df0981c855855a1a28218cf086ddeb5bb691\",\n      \"built_at\": \"2026-08-15T09:01:06Z\",\n      \"last_confirmed_at\": \"2026-08-15T09:01:06Z\",\n      \"last_confirmed_source\": \"index-build\",\n      \"stamp_signature_valid\": true\n    },\n    \"jurisdiction\": \"EU\",\n    \"timeline_semantics\": \"official_consolidation_state\",\n    \"artifact\": {\n      \"manifest_set_id\": \"4dff34d9e957d469e87ca2b1dbe0e74b5a85519da3631b37ddf2ea81d3553b59\",\n      \"content_digest\": \"158bf28e9cfe5facefe5b728ba221f6d00162b101f79b5d59b937695d4ea20f1\",\n      \"code_commit\": \"27f0e02cb0da8e0fdf9f8322d3eef3b3ae09c776\",\n      \"index_format\": null\n    }\n  },\n  \"document\": {\n    \"lex_id\": \"eu-eurlex:32016r0679:2016-05-04--af3e8edcc8aeb9b8c10e891880377cb0b363a8fa7005a1b45557d21afa592de5\",\n    \"version_key\": \"2016-05-04--af3e8edcc8aeb9b8c10e891880377cb0b363a8fa7005a1b45557d21afa592de5\",\n    \"work\": \"32016r0679\",\n    \"work_identifier\": \"http://publications.europa.eu/resource/celex/32016R0679\",\n    \"document_type\": \"REG\",\n    \"extraction_profile\": \"xhtml-eu/1\",\n    \"language\": \"en\",\n    \"valid_from\": \"2016-05-04\",\n    \"valid_to\": null,\n    \"valid_time_source\": \"publisher\",\n    \"publication_date\": \"2016-05-04\",\n    \"title\": \"Regulation (EU) 2016/679\",\n    \"withdrawn\": false,\n    \"text_available\": true,\n    \"record_sha256\": \"44d09ee49e187e02cf8649106b90badc16600d8227eb1f6f851b2054775bcf84\",\n    \"body_sha256\": \"28524c5589d9c80dee357fe96498302b4fefb29b3cc9ada7dcad52c967e3f15c\",\n    \"source_uri\": \"https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:02016R0679-20160504\",\n    \"observed_from\": \"2026-08-01T00:00:00Z\",\n    \"text\": null,\n    \"hierarchy\": \"secondary_eu_law\",\n    \"act_form\": \"REG\",\n    \"binding_status\": \"in_force\",\n    \"consolidation_status\": \"published\",\n    \"permalink\": \"https://law.soufien.lu/eu-eurlex/32016r0679/2016-05-04--af3e8edcc8aeb9b8c10e891880377cb0b363a8fa7005a1b45557d21afa592de5\"\n  },\n  \"truncated\": false,\n  \"events\": [\n    {\n      \"event\": \"first_sighting\",\n      \"scope\": \"version\",\n      \"observed_from\": \"2026-08-01T00:00:00Z\",\n      \"detail\": null\n    },\n    {\n      \"event\": \"metadata_revised\",\n      \"scope\": \"version\",\n      \"observed_from\": \"2026-08-06T16:31:02Z\",\n      \"detail\": \"fields=in_force_status,raw.binding_status,raw.consolidation_status,raw.domains,raw.hierarchy,raw.legal_form,raw.scope_reasons\"\n    },\n    {\n      \"event\": \"expression_added\",\n      \"scope\": \"fr\",\n      \"observed_from\": \"2026-08-06T19:54:54Z\",\n      \"detail\": \"language=fr\"\n    },\n    {\n      \"event\": \"metadata_revised\",\n      \"scope\": \"version\",\n      \"observed_from\": \"2026-08-09T00:10:14Z\",\n      \"detail\": \"fields=publisher_metadata,document_roles,expressions.fr.title_short\"\n    },\n    {\n      \"event\": \"metadata_revised\",\n      \"scope\": \"version\",\n      \"observed_from\": \"2026-08-15T04:50:52Z\",\n      \"detail\": \"fields=raw,lex_id,publisher_version_identifier\"\n    },\n    {\n      \"event\": \"metadata_revised\",\n      \"scope\": \"version\",\n      \"observed_from\": \"2026-08-15T04:50:52Z\",\n      \"detail\": \"fields=publisher_metadata\"\n    }\n  ],\n  \"observations\": [\n    {\n      \"language\": \"en\",\n      \"expr_valid_from\": \"2016-05-04\",\n      \"sha256\": \"a681ff22f32f125749af6e96947224369efc132edb8a95b12637e357df1056de\",\n      \"observed_from\": \"2026-08-01T02:00:00Z\",\n      \"observed_to\": \"2026-08-02T08:51:25Z\"\n    },\n    {\n      \"language\": \"en\",\n      \"expr_valid_from\": \"2016-05-04\",\n      \"sha256\": \"e0c46677bc013886ba60f434fa9b10993b1f767fff8e339e11f2dbaaf3f4a6c9\",\n      \"observed_from\": \"2026-08-02T08:51:25Z\",\n      \"observed_to\": \"2026-08-02T08:51:25Z\"\n    },\n    {\n      \"language\": \"en\",\n      \"expr_valid_from\": \"2016-05-04\",\n      \"sha256\": \"28524c5589d9c80dee357fe96498302b4fefb29b3cc9ada7dcad52c967e3f15c\",\n      \"observed_from\": \"2026-08-02T08:51:25Z\",\n      \"observed_to\": null\n    },\n    {\n      \"language\": \"fr\",\n      \"expr_valid_from\": \"2016-05-04\",\n      \"sha256\": \"fa7661d464cb4f3f9b4c62ae742c82ec6208b7bd0b8a07b21da2830a13f44a78\",\n      \"observed_from\": \"2026-08-06T19:54:54Z\",\n      \"observed_to\": \"2026-08-06T19:54:54Z\"\n    },\n    {\n      \"language\": \"fr\",\n      \"expr_valid_from\": \"2016-05-04\",\n      \"sha256\": \"d2550e6fc48ac34fd513a55bfb70ec982ab1324d02cd2e40571ffee23de45d4e\",\n      \"observed_from\": \"2026-08-06T19:54:54Z\",\n      \"observed_to\": \"2026-08-06T19:54:54Z\"\n    },\n    {\n      \"language\": \"fr\",\n      \"expr_valid_from\": \"2016-05-04\",\n      \"sha256\": \"99a1375c52852403f04a3d1af5005192b4916320c8233b29ce38a9ac260c3378\",\n      \"observed_from\": \"2026-08-06T19:54:54Z\",\n      \"observed_to\": null\n    }\n  ],\n  \"stamp\": {\n    \"signature_valid\": true,\n    \"algorithm\": \"ECDSA-P256-SHA256\",\n    \"public_key\": \"-----BEGIN PUBLIC KEY-----\\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEl15vnowBerveXWu1D4KHtdOmg1d7\\n4Tf6bivetAx2/fseLfHpFHZaoLvPMAgs24nfe1DEU7Ym8qdfrvwvib/BNw==\\n-----END PUBLIC KEY-----\",\n    \"signature\": \"FpYHsAHqNOopVy2eqYEGatj/5b1O1/YEOi3mL+Uz+aY87rSjz8NovopvpZxZ3g08yNK7PCLl+lcIxMpwCNnrFw==\"\n  }\n}",
    sha256: "90b217d904b7020eff3c95ead019e1ad3fed65b6c69832915e16fc452e92d332",
    bytes: 5111,
  },
  "eu-gdpr-fr.json": {
    text:
      "{\n  \"envelope\": {\n    \"publisher\": \"eu-eurlex\",\n    \"tier\": \"A\",\n    \"history_begins\": \"publisher\",\n    \"status\": \"ok\",\n    \"provisional\": false,\n    \"freshness\": {\n      \"corpus_commit\": \"e9c4df0981c855855a1a28218cf086ddeb5bb691\",\n      \"built_at\": \"2026-08-15T09:01:06Z\",\n      \"last_confirmed_at\": \"2026-08-15T09:01:06Z\",\n      \"last_confirmed_source\": \"index-build\",\n      \"stamp_signature_valid\": true\n    },\n    \"jurisdiction\": \"EU\",\n    \"timeline_semantics\": \"official_consolidation_state\",\n    \"artifact\": {\n      \"manifest_set_id\": \"4dff34d9e957d469e87ca2b1dbe0e74b5a85519da3631b37ddf2ea81d3553b59\",\n      \"content_digest\": \"158bf28e9cfe5facefe5b728ba221f6d00162b101f79b5d59b937695d4ea20f1\",\n      \"code_commit\": \"27f0e02cb0da8e0fdf9f8322d3eef3b3ae09c776\",\n      \"index_format\": null\n    }\n  },\n  \"document\": {\n    \"lex_id\": \"eu-eurlex:32016r0679:2016-05-04--af3e8edcc8aeb9b8c10e891880377cb0b363a8fa7005a1b45557d21afa592de5\",\n    \"version_key\": \"2016-05-04--af3e8edcc8aeb9b8c10e891880377cb0b363a8fa7005a1b45557d21afa592de5\",\n    \"work\": \"32016r0679\",\n    \"work_identifier\": \"http://publications.europa.eu/resource/celex/32016R0679\",\n    \"document_type\": \"REG\",\n    \"extraction_profile\": \"xhtml-eu/1\",\n    \"language\": \"en\",\n    \"valid_from\": \"2016-05-04\",\n    \"valid_to\": null,\n    \"valid_time_source\": \"publisher\",\n    \"publication_date\": \"2016-05-04\",\n    \"title\": \"Regulation (EU) 2016/679\",\n    \"withdrawn\": false,\n    \"text_available\": true,\n    \"record_sha256\": \"44d09ee49e187e02cf8649106b90badc16600d8227eb1f6f851b2054775bcf84\",\n    \"body_sha256\": \"28524c5589d9c80dee357fe96498302b4fefb29b3cc9ada7dcad52c967e3f15c\",\n    \"source_uri\": \"https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:02016R0679-20160504\",\n    \"observed_from\": \"2026-08-01T00:00:00Z\",\n    \"text\": null,\n    \"hierarchy\": \"secondary_eu_law\",\n    \"act_form\": \"REG\",\n    \"binding_status\": \"in_force\",\n    \"consolidation_status\": \"published\",\n    \"permalink\": \"https://law.soufien.lu/eu-eurlex/32016r0679/2016-05-04--af3e8edcc8aeb9b8c10e891880377cb0b363a8fa7005a1b45557d21afa592de5\"\n  },\n  \"truncated\": false,\n  \"events\": [\n    {\n      \"event\": \"first_sighting\",\n      \"scope\": \"version\",\n      \"observed_from\": \"2026-08-01T00:00:00Z\",\n      \"detail\": null\n    },\n    {\n      \"event\": \"metadata_revised\",\n      \"scope\": \"version\",\n      \"observed_from\": \"2026-08-06T16:31:02Z\",\n      \"detail\": \"fields=in_force_status,raw.binding_status,raw.consolidation_status,raw.domains,raw.hierarchy,raw.legal_form,raw.scope_reasons\"\n    },\n    {\n      \"event\": \"expression_added\",\n      \"scope\": \"fr\",\n      \"observed_from\": \"2026-08-06T19:54:54Z\",\n      \"detail\": \"language=fr\"\n    },\n    {\n      \"event\": \"metadata_revised\",\n      \"scope\": \"version\",\n      \"observed_from\": \"2026-08-09T00:10:14Z\",\n      \"detail\": \"fields=publisher_metadata,document_roles,expressions.fr.title_short\"\n    },\n    {\n      \"event\": \"metadata_revised\",\n      \"scope\": \"version\",\n      \"observed_from\": \"2026-08-15T04:50:52Z\",\n      \"detail\": \"fields=raw,lex_id,publisher_version_identifier\"\n    },\n    {\n      \"event\": \"metadata_revised\",\n      \"scope\": \"version\",\n      \"observed_from\": \"2026-08-15T04:50:52Z\",\n      \"detail\": \"fields=publisher_metadata\"\n    }\n  ],\n  \"observations\": [\n    {\n      \"language\": \"fr\",\n      \"expr_valid_from\": \"2016-05-04\",\n      \"sha256\": \"fa7661d464cb4f3f9b4c62ae742c82ec6208b7bd0b8a07b21da2830a13f44a78\",\n      \"observed_from\": \"2026-08-06T19:54:54Z\",\n      \"observed_to\": \"2026-08-06T19:54:54Z\"\n    },\n    {\n      \"language\": \"fr\",\n      \"expr_valid_from\": \"2016-05-04\",\n      \"sha256\": \"d2550e6fc48ac34fd513a55bfb70ec982ab1324d02cd2e40571ffee23de45d4e\",\n      \"observed_from\": \"2026-08-06T19:54:54Z\",\n      \"observed_to\": \"2026-08-06T19:54:54Z\"\n    },\n    {\n      \"language\": \"fr\",\n      \"expr_valid_from\": \"2016-05-04\",\n      \"sha256\": \"99a1375c52852403f04a3d1af5005192b4916320c8233b29ce38a9ac260c3378\",\n      \"observed_from\": \"2026-08-06T19:54:54Z\",\n      \"observed_to\": null\n    }\n  ],\n  \"stamp\": {\n    \"signature_valid\": true,\n    \"algorithm\": \"ECDSA-P256-SHA256\",\n    \"public_key\": \"-----BEGIN PUBLIC KEY-----\\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEl15vnowBerveXWu1D4KHtdOmg1d7\\n4Tf6bivetAx2/fseLfHpFHZaoLvPMAgs24nfe1DEU7Ym8qdfrvwvib/BNw==\\n-----END PUBLIC KEY-----\",\n    \"signature\": \"FpYHsAHqNOopVy2eqYEGatj/5b1O1/YEOi3mL+Uz+aY87rSjz8NovopvpZxZ3g08yNK7PCLl+lcIxMpwCNnrFw==\"\n  }\n}",
    sha256: "f5bcf2c0db70737dadcf2d84fd66b8bc40da4a5c1b84403836cd6bb4a81b1a96",
    bytes: 4376,
  },
  "coverage-census.json": {
    text:
      "[\n  {\n    \"publisher\": \"lu-legilux\",\n    \"publisher_name\": \"Service central de législation (Legilux)\",\n    \"works\": 1402,\n    \"versions\": 4656\n  },\n  {\n    \"publisher\": \"eu-eurlex\",\n    \"publisher_name\": \"Publications Office of the EU (EUR-Lex / Cellar)\",\n    \"works\": 1250,\n    \"versions\": 2366\n  }\n]",
    sha256: "55adf698d534ed60868f56684fa5307f96b979658b6c8e68cc83047896c8d805",
    bytes: 302,
  },
  "refusal-unknown-work.json": {
    text:
      "{\n  \"status\": \"unknown_work\",\n  \"lex_id\": \"lu-legilux:recueil-protection_donnees:1999-01-01--0000000000000000000000000000000000000000000000000000000000000000\"\n}",
    sha256: "44d8d710bb1084b459044f1f305014853d515202d5c2238db4d7a02578ba46e9",
    bytes: 160,
  },
});

/** One captured payload, refused if its bytes are not the ones that were captured. */
function live(name) {
  if (!Object.hasOwn(LIVE, name ?? '')) {
    throw new Error(`no live payload named ${name}`);
  }
  return JSON.parse(verifyCapture(name, LIVE[name]));
}

/** A mutable copy, so a test can break exactly one thing and watch the guard fire. */
function copy(name) {
  return structuredClone(live(name));
}

const HOLDINGS = live('coverage-census.json');

/** The request a reader makes by arriving at this record's provenance URL. */
function ask(record, language = null, holdings = HOLDINGS) {
  return {
    requested: { lex_id: record.document?.lex_id ?? record.lex_id, language },
    record,
    holdings,
  };
}

function reactHtml(input) {
  return renderToStaticMarkup(h(Provenance, input));
}

const LU = 'lu-legilux:recueil-protection_donnees:'
  + '2023-09-05--7e00585ca7d30427837996540e0da3bdfe1c141894e2d41e1202f0938f7c832f';
const EU = 'eu-eurlex:32016r0679:'
  + '2016-05-04--af3e8edcc8aeb9b8c10e891880377cb0b363a8fa7005a1b45557d21afa592de5';

// ---- the captures themselves ----------------------------------------------------------

test('the captured payloads are the live ones, and none of them is trivial', () => {
  // An empty baseline passes forever, which is this repository's own recorded lesson: the
  // first tool snapshots wrote one byte each and stayed green.
  for (const name of Object.keys(LIVE)) {
    const payload = live(name);
    assert.ok(payload !== null && typeof payload === 'object', `${name} decoded to nothing`);
  }
  const eu = live('eu-gdpr.json');
  assert.equal(eu.document.lex_id, EU);
  assert.equal(eu.events.length, 6, 'the Union event chain lost its shape');
  assert.equal(eu.observations.length, 6, 'the Union observation history lost its shape');
  assert.equal(live('lu-no-text.json').observations.length, 0);
  assert.equal(HOLDINGS.length, 2, 'the census stopped counting both publishers');
});

test('an edited capture is refused rather than rendered as evidence', () => {
  // Not a re-test of `verifyCapture`, which has its own; this holds that THIS file's payloads
  // actually go through it. Without the call the file is my typing wearing a digest.
  const entry = LIVE['lu-recueil.json'];
  assert.throws(
    () => verifyCapture('probe', { ...entry, text: entry.text.replace('"ok"', '"OK"') }),
    /does not match its recorded identity/,
  );
});

test('three different Luxembourg records carry one identical stamp signature', () => {
  // This is the observation the whole stamp rule rests on, asserted rather than remembered.
  // If a later capture gives each record its own signature, this fails and the rule below can
  // be revisited on evidence instead of on my memory of a Saturday.
  const signatures = new Set(
    ['lu-recueil.json', 'lu-loi-1915.json', 'lu-no-text.json'].map(
      (name) => live(name).stamp.signature,
    ),
  );
  assert.equal(signatures.size, 1, 'the live stamp is per record after all');
  assert.notEqual(
    [...signatures][0],
    live('eu-gdpr.json').stamp.signature,
    'both publishers share one signature, so it is not even per index',
  );
});

// ---- what the page says ---------------------------------------------------------------

test('a real Luxembourg record and a real Union record each render in their own vocabulary', () => {
  const lu = renderProvenance(ask(live('lu-recueil.json')));
  const eu = renderProvenance(ask(live('eu-gdpr.json')));

  assert.match(lu, /Applicable from 2023-09-05/);
  assert.ok(!lu.includes('Consolidated wording state'), 'a Union sentence over a LU record');

  assert.match(eu, /Consolidated wording state from 2016-05-04/);
  assert.ok(!eu.includes('Applicable from'), 'an applicability claim over a Union record');

  // Both digests, each named. Sixty-four hex characters with no label is a number.
  assert.match(lu, /record_sha256/);
  assert.match(lu, /186039205ec6bac550fdb57de1be0ae4218abb6f3da8b06f4f0ed76caa690b88/);
  assert.match(lu, /body_sha256/);
  assert.match(lu, /fbc23154d6fbcffd6ab7119a55ddb9f103de180da6d713e0b19c1165544d3f9f/);

  // The publisher's own file, on the publisher's own host.
  assert.match(
    lu,
    /href="https:\/\/legilux\.public\.lu\/eli\/etat\/leg\/recueil\/protection_donnees\/20230905\/fr"/,
  );
});

test('an envelope that renames the publisher clock is refused, not preferred', () => {
  const record = copy('eu-gdpr.json');
  record.envelope.timeline_semantics = 'publisher_applicability';
  assert.throws(
    () => readProvenance(ask(record)),
    /eu-eurlex dates are official_consolidation_state/,
  );
});

test('the publisher title carries the record language, never the chrome one', () => {
  const html = renderProvenance(ask(live('lu-loi-1915.json')));
  assert.match(html, /<p class="provenance-title" lang="fr">/);
  // Accents, a colon and a trailing full stop, none of which the synthetic fixture has.
  assert.match(html, /Loi du 10 août 1915 concernant les sociétés commerciales\./);

  const record = copy('lu-loi-1915.json');
  record.document.language = 'french';
  assert.throws(() => readProvenance(ask(record)), /does not carry its own language/);
});

test('the page never presents the stamp as evidence about the record', () => {
  const html = renderProvenance(ask(live('lu-recueil.json')));
  assert.ok(html.includes(STAMP_SCOPE_NOTE), 'the stamp scope note is gone');
  // The stamp is in the build section, which comes after the record and the digests. Order is
  // the assertion here: a signature printed beside a digest reads as signing it.
  assert.ok(
    html.indexOf('What served this answer') > html.indexOf('The bodies this service has held'),
    'the stamp block moved up beside the digests',
  );
  // Whole, not truncated. A signature nobody can copy in full is a signature nobody can check.
  assert.match(html, /2rvpvZiaSPteIafX7AwJFYOC8kNDBaFSWhQbDJt\+88LJixjfo5AGF17dihOFh6fra/);
});

test('two verdicts about one signature refuse the page rather than picking one', () => {
  const record = copy('lu-recueil.json');
  record.stamp.signature_valid = false;
  assert.throws(() => readProvenance(ask(record)), /two verdicts about one signature/);

  const absent = copy('lu-recueil.json');
  absent.stamp.signature_valid = 'true';
  assert.throws(() => readProvenance(ask(absent)), /stamp\.signature_valid must be a boolean/);
});

// ---- the identity, and the two ways this page can be about the wrong record -------------

test('a page addressed to one record and answered about another is refused', () => {
  const record = live('lu-recueil.json');
  assert.throws(
    () => readProvenance({ requested: { lex_id: EU, language: null }, record, holdings: HOLDINGS }),
    /this page is addressed to/,
  );
});

test('a permalink naming a different record, or a different origin, is refused', () => {
  const elsewhere = copy('lu-recueil.json');
  elsewhere.document.permalink = 'https://law.soufien.lu/lu-legilux/some-other-work/2023-09-05';
  assert.throws(() => readProvenance(ask(elsewhere)), /is not this service's address for/);

  // Ends with the right coordinates and is served from somewhere else entirely.
  const hostile = copy('lu-recueil.json');
  hostile.document.permalink = `https://evil.example/x/lu-legilux/recueil-protection_donnees/${
    hostile.document.version_key}`;
  assert.throws(() => readProvenance(ask(hostile)), /is not this service's address for/);
});

test('a record whose parts name different works is refused', () => {
  const work = copy('lu-recueil.json');
  work.document.work = 'recueil-something-else';
  assert.throws(() => readProvenance(ask(work)), /and its identifier says/);

  const key = copy('lu-recueil.json');
  key.document.version_key = '2023-09-05--' + 'a'.repeat(64);
  assert.throws(() => readProvenance(ask(key)), /two names for one state is two states/);

  const publisher = copy('lu-recueil.json');
  publisher.envelope.publisher = 'eu-eurlex';
  assert.throws(() => readProvenance(ask(publisher)), /whose record this is/);
});

// ---- the language filter, which the payload never mentions -------------------------------

test('the language a caller asked for is declared, never assumed', () => {
  const record = live('eu-gdpr.json');
  assert.throws(
    () => readProvenance({ requested: { lex_id: EU }, record, holdings: HOLDINGS }),
    /does not carry language/,
  );
  assert.throws(
    () => readProvenance({ requested: { lex_id: EU, language: 'french' }, record, holdings: HOLDINGS }),
    /is neither a two letter code nor null/,
  );
});

test('a narrowed observation list says so, and is not read as the whole history', () => {
  const html = renderProvenance(ask(live('eu-gdpr-fr.json'), 'fr'));
  assert.ok(html.includes(narrowedNote('fr')), 'a filtered list rendered as a whole history');
  // The record's own body digest is English and is legitimately absent from a French list, so
  // no row may claim to be it.
  assert.ok(
    !html.includes('yes, this digest is the one the record names'),
    'a French observation was labelled as the English body this record names',
  );
  // And the digest is still on the page, under its own name, so a reader is not left thinking
  // the French rows are what the record carries.
  assert.match(html, /28524c5589d9c80dee357fe96498302b4fefb29b3cc9ada7dcad52c967e3f15c/);
});

test('a list that is not the one that was asked for is refused', () => {
  // The unfiltered Union payload requested as French: the payload never says it was filtered,
  // so an English row in a French list means this list is not the one that was asked for.
  assert.throws(() => readProvenance(ask(live('eu-gdpr.json'), 'fr')), /in a list requested as fr/);

  // And the French payload requested unfiltered: the record names an English body and the
  // history holds none, which is the pairing that would show two texts as one.
  assert.throws(
    () => readProvenance(ask(live('eu-gdpr-fr.json'))),
    /open en observations; the digest the record carries/,
  );
});

// ---- the observation history --------------------------------------------------------------

test('a zero wide observation window is held, and an inverted one is refused', () => {
  // Three of the six live Union observations close in the second they open. This is the
  // comparison a designed-from-the-spec renderer gets wrong, and no synthetic fixture has it.
  const eu = live('eu-gdpr.json');
  const zeroWidth = eu.observations.filter((one) => one.observed_to === one.observed_from);
  assert.ok(zeroWidth.length > 0, 'the capture lost its zero width windows');
  assert.doesNotThrow(() => readProvenance(ask(eu)));

  const inverted = copy('eu-gdpr.json');
  inverted.observations[0].observed_to = '2026-08-01T00:00:00Z';
  assert.throws(() => readProvenance(ask(inverted)), /a window that closes before it opens/);
});

test('two currently held bodies for one expression are refused', () => {
  // Matched on this guard's own sentence. `/open en observations/` also matches the body
  // digest guard further down, so disabling this one left the suite green: a shadowed guard is
  // a guard nothing holds.
  const record = copy('eu-gdpr.json');
  record.observations[0].observed_to = null;
  assert.throws(
    () => readProvenance(ask(record)),
    /two bodies currently held for one expression/,
  );
});

test('a record that holds no body may not have an open observation in its own language', () => {
  // The other half of the digest cross-check, and the half no live capture reaches: this
  // corpus says it holds no text while its own history says it is holding some.
  const record = copy('lu-no-text.json');
  record.observations.push({
    language: 'fr',
    expr_valid_from: '2026-08-08',
    sha256: 'd'.repeat(64),
    observed_from: '2026-08-14T23:05:14Z',
    observed_to: null,
  });
  assert.throws(
    () => readProvenance(ask(record)),
    /carries no body digest while an open observation holds a body/,
  );
});

test('a body digest no open observation holds is refused', () => {
  const record = copy('lu-recueil.json');
  record.observations[0].sha256 = 'b'.repeat(64);
  assert.throws(() => readProvenance(ask(record)), /would be showing two different texts/);
});

test('an observation older than the record itself is refused', () => {
  const record = copy('lu-recueil.json');
  record.observations[0].observed_from = '2020-01-01T00:00:00Z';
  assert.throws(() => readProvenance(ask(record)), /a record it had not met/);
});

test('the one observation that is the record body is the one that says so', () => {
  const view = readProvenance(ask(live('eu-gdpr.json')));
  const named = view.observations.filter((one) => one.is_record_body);
  assert.equal(named.length, 1);
  assert.equal(named[0].sha256, view.document.body_sha256);
  assert.equal(named[0].language, 'en');
});

// ---- a record with no text ------------------------------------------------------------------

test('a record this corpus holds no text for says so, and says whose gap it is', () => {
  const html = renderProvenance(ask(live('lu-no-text.json')));
  assert.ok(html.includes(NO_TEXT_NOTE));
  // The parser is absent too, and an absent field is declared rather than dropped.
  assert.match(html, /<dt>extraction profile<\/dt><dd>not recorded<\/dd>/);
  // No body digest row, because there is no body.
  assert.ok(!html.includes('body_sha256'), 'a digest of bytes this corpus does not hold');
  assert.equal(readProvenance(ask(live('lu-no-text.json'))).counts.observations, 0);
});

test('a record that holds no text may not carry a body digest', () => {
  const record = copy('lu-no-text.json');
  record.document.body_sha256 = 'c'.repeat(64);
  assert.throws(() => readProvenance(ask(record)), /a number about nothing/);
});

// ---- counts, which are the payload's and not the renderer's ---------------------------------

test('every count on the page is the length of a payload array', () => {
  const html = renderProvenance(ask(live('eu-gdpr.json')));
  assert.match(html, /6 events/);
  assert.match(html, /6 observations/);

  const shorter = copy('eu-gdpr.json');
  shorter.events.pop();
  const shorterHtml = renderProvenance(ask(shorter));
  assert.match(shorterHtml, /5 events/);
  assert.ok(!shorterHtml.includes('6 events'), 'the event count is a literal');
});

test('a truncated payload states its counts as a floor, never as a total', () => {
  const record = copy('eu-gdpr.json');
  record.truncated = true;
  const html = renderProvenance(ask(record));
  assert.match(html, /at least 6 events; the service truncated this list/);
  assert.match(html, /at least 6 observations; the service truncated this list/);

  assert.throws(() => {
    const undeclared = copy('eu-gdpr.json');
    delete undeclared.truncated;
    readProvenance(ask(undeclared));
  }, /does not carry truncated/);
});

test('a complete chain has to begin where the record says it was first seen', () => {
  const moved = copy('eu-gdpr.json');
  moved.events[0].observed_from = '2026-08-05T00:00:00Z';
  assert.throws(() => readProvenance(ask(moved)), /two answers to when this service first saw/);

  const headless = copy('eu-gdpr.json');
  headless.events.shift();
  assert.throws(() => readProvenance(ask(headless)), /carries 0 first sightings/);

  // The same chain, declared truncated, is legitimately missing its head and is accepted.
  const truncated = copy('eu-gdpr.json');
  truncated.events.shift();
  truncated.truncated = true;
  assert.doesNotThrow(() => readProvenance(ask(truncated)));
});

test('an event chain that runs backwards is refused', () => {
  const record = copy('eu-gdpr.json');
  record.events[5].observed_from = '2026-08-02T00:00:00Z';
  assert.throws(() => readProvenance(ask(record)), /before the event above it/);
});

test('two events at the same instant are a real chain, not a collision', () => {
  // The live Union record carries two `metadata_revised` events in the same second. A rule
  // written with `<` instead of `<=` refuses it, and so does a React key built from the pair.
  const eu = live('eu-gdpr.json');
  assert.equal(eu.events[4].observed_from, eu.events[5].observed_from);
  assert.doesNotThrow(() => reactHtml(ask(eu)));
});

// ---- the refusal -----------------------------------------------------------------------------

test('the live refusal status is translated, and it is not in the client registry', () => {
  assert.ok(!REFUSAL_CODES.includes('unknown_work'), 'the registry silently gained the status');
  assert.deepEqual(PROVENANCE_REFUSAL_STATUSES, ['unknown_work']);

  const refusal = live('refusal-unknown-work.json');
  const view = readProvenance(ask(refusal));
  assert.equal(view.kind, 'refusal');
  assert.equal(view.code, 'identifier_unknown');
});

test('the refusal does not say the work is unknown, because the status cannot know', () => {
  // The same status came back for a work this corpus holds at other states. Saying "no such
  // work" over that is the sentence this product exists to avoid.
  const html = renderProvenance(ask(live('refusal-unknown-work.json')));
  assert.match(html, /whether no work matches the identifier or whether this corpus holds the/);
  // The absence note the card writes does contain the words "does not exist", so the check is
  // on the claim rather than on the words: nothing here says this instrument is not held by
  // the publisher, and nothing here names the work as unknown.
  for (const claim of [
    'no such work',
    'this work is unknown',
    'the work does not exist',
    'no such instrument',
  ]) {
    assert.ok(!html.includes(claim), `the refusal asserts ${claim}`);
  }
  // The card's own absence note, and the contract constant behind it.
  assert.match(html, /is not evidence that the instrument or the law does not exist/);
  assert.equal(readProvenance(ask(live('refusal-unknown-work.json'))).payload
    .asserts_absence_of_law, false);
  assert.deepEqual(
    readProvenance(ask(live('refusal-unknown-work.json'))).payload.what_would_answer,
    WHAT_WOULD_ANSWER,
  );
});

test('the refusal discloses the size of what was searched, in the census figures', () => {
  const html = renderProvenance(ask(live('refusal-unknown-work.json')));
  assert.match(html, /1402 works and 4656 versions from Service central de législation/);
  assert.match(html, /1250 works and 2366 versions from Publications Office of the EU/);

  const smaller = structuredClone(HOLDINGS);
  smaller[0].works = 7;
  const other = renderProvenance(ask(live('refusal-unknown-work.json'), null, smaller));
  assert.match(other, /7 works and 4656 versions/);
  assert.ok(!other.includes('1402 works'), 'the disclosure figures are literals');
});

test('a refusal about a different identifier than the one asked for is refused', () => {
  const refusal = live('refusal-unknown-work.json');
  assert.throws(
    () => readProvenance({
      requested: { lex_id: LU, language: null },
      record: refusal,
      holdings: HOLDINGS,
    }),
    /a page answering about a different identifier/,
  );
});

test('a refusal status this client cannot name is refused, not shown as an error', () => {
  const record = copy('refusal-unknown-work.json');
  record.status = 'teapot';
  assert.throws(() => readProvenance(ask(record)), /which this client cannot name/);
});

test('a payload that is neither an answer nor a refusal is refused', () => {
  const both = copy('lu-recueil.json');
  both.status = 'unknown_work';
  assert.throws(() => readProvenance(ask(both)), /and this one carries both/);
  assert.throws(
    () => readProvenance({ requested: { lex_id: LU, language: null }, record: {}, holdings: HOLDINGS }),
    /carries neither/,
  );
});

// ---- what this corpus holds -------------------------------------------------------------------

test('the census is required, closed, and has to count this record publisher', () => {
  const record = live('lu-recueil.json');
  assert.throws(
    () => readProvenance({ requested: { lex_id: LU, language: null }, record }),
    /requires the corpus census/,
  );

  const unclassified = structuredClone(HOLDINGS);
  unclassified[0].publisher = 'xx-somewhere';
  assert.throws(
    () => readProvenance(ask(record, null, unclassified)),
    /is not a publisher this interface has classified/,
  );

  const onlyEu = [HOLDINGS[1]];
  assert.throws(
    () => readProvenance(ask(record, null, onlyEu)),
    /does not count lu-legilux/,
  );

  const uncounted = structuredClone(HOLDINGS);
  uncounted[0].works = '1402';
  assert.throws(() => readProvenance(ask(record, null, uncounted)), /is not a counted whole number/);
});

test('the success page says what this corpus holds, not what the publisher holds', () => {
  const html = renderProvenance(ask(live('lu-recueil.json')));
  assert.match(html, /This corpus holds 1402 works and 4656 versions/);
  assert.match(html, /It is not what the publisher holds, and it is not the law\./);
});

// ---- dates this service worked out --------------------------------------------------------------

test('a date this service derived is marked as derived, and an unknown source is refused', () => {
  // Unreachable from the live captures: every record so far carries `publisher`. The branch is
  // exercised on a mutated capture so it is not shipped unproven.
  const derived = copy('lu-recueil.json');
  derived.document.valid_time_source = 'derived';
  const html = renderProvenance(ask(derived));
  assert.match(html, /derived, not publisher-asserted/);
  assert.match(html, /were derived by this service from the publisher record/);

  const plain = renderProvenance(ask(live('lu-recueil.json')));
  assert.ok(!plain.includes('derived, not publisher-asserted'));

  const unknown = copy('lu-recueil.json');
  unknown.document.valid_time_source = 'somewhere';
  assert.throws(() => readProvenance(ask(unknown)), /is not one of publisher, derived/);
});

// ---- the two runtimes ------------------------------------------------------------------------------

test('React applies the same rules and shows the same sentences as the string renderer', () => {
  for (const name of ['lu-recueil.json', 'eu-gdpr.json', 'lu-no-text.json']) {
    const input = ask(live(name));
    const view = readProvenance(input);
    const string = renderProvenance(input);
    const react = reactHtml(input);

    for (const sentence of [
      view.sentences.legal,
      view.sentences.recordTime,
      view.sentences.holdings,
      view.sentences.population,
      STAMP_SCOPE_NOTE,
    ]) {
      assert.ok(string.includes(sentence), `${name}: the string page dropped ${sentence}`);
      assert.ok(react.includes(sentence), `${name}: the React page dropped ${sentence}`);
    }
    // The record's own digests are on both, whole.
    assert.ok(react.includes(view.document.record_sha256));
    assert.ok(string.includes(view.document.record_sha256));
  }
});

test('React refuses everything the string renderer refuses', () => {
  const record = copy('eu-gdpr.json');
  record.envelope.timeline_semantics = 'publisher_applicability';
  assert.throws(() => reactHtml(ask(record)), /eu-eurlex dates are/);
  assert.throws(() => renderProvenance(ask(record)), /eu-eurlex dates are/);
});

test('the React refusal carries the population disclosure the contract requires', () => {
  const input = ask(live('refusal-unknown-work.json'));
  const html = reactHtml(input);
  assert.match(html, /1402 works and 4656 versions from Service central de législation/);
  assert.match(html, /<code class="refusal-code">identifier_unknown<\/code>/);
  // A refusal is an answer, never an alert, in this runtime too.
  assert.ok(!html.includes('role="alert"'));
  assert.ok(!html.includes('aria-live'));
});

// ---- the link that started all this ------------------------------------------------------------

test('every provenance link the preview renders has a page built for it', () => {
  // The two modules that emit a verify cluster. A provenance link with no page behind it is
  // the defect this whole screen exists to end, and it came back the moment a preview added a
  // state nobody built a page for.
  const linked = new Set();
  for (const html of [renderReadingPreview(), renderTrustSurface()]) {
    for (const match of html.matchAll(/href="\/provenance\/([^"]+)"/g)) {
      linked.add(decodeURIComponent(match[1]));
    }
  }
  assert.ok(linked.size > 0, 'no provenance link was found, so this test proves nothing');

  const built = new Set(provenancePreviewPages().map(([name]) => name));
  for (const lexId of linked) {
    assert.ok(
      built.has(provenancePageName(lexId)),
      `the preview links to ${lexId} and no provenance page was built for it`,
    );
  }
});

test('the harness routes a provenance URL to the page the build wrote for that record', () => {
  // One rule, two files. The route and the file name used to be able to disagree, and a
  // disagreement here is a 404 that only the browser run would find.
  for (const preview of PREVIEW_RECORDS) {
    assert.equal(
      routeSchemePath(`/provenance/${preview.lexId}`),
      `/${provenancePageName(preview.lexId)}`,
    );
  }
  // A record with no page still resolves to a name nothing wrote, so it 404s, which is what
  // keeps the build honest about which records it rendered.
  assert.equal(
    routeSchemePath('/provenance/lu-legilux:nothing:2020-01-01'),
    '/provenance-lu-legilux~nothing~2020-01-01.html',
  );
  // And a name is never shared. The character the colon becomes cannot occur in a lex_id, so
  // an identifier carrying one is refused rather than mapped onto a page that already belongs
  // to a different record.
  assert.equal(provenancePageName('lu-legilux:a:b'), 'provenance-lu-legilux~a~b.html');
  assert.throws(() => provenancePageName('lu-legilux~a~b'), /cannot be named without colliding/);
  assert.throws(() => provenancePageName('lu-legilux/../secret'), /cannot be named without/);
});

test('the built preview pages are whole documents and describe different records', () => {
  const pages = provenancePreviewPages();
  assert.equal(pages.length, PREVIEW_RECORDS.length + 1, 'the refusal page went missing');
  const bodies = new Set();
  for (const [name, html] of pages) {
    assert.ok(name.startsWith('provenance-') && name.endsWith('.html'));
    assert.ok(html.startsWith('<!doctype html>'), `${name} is not a whole document`);
    assert.match(html, /<h1>/);
    bodies.add(html);
  }
  // Four pages that were the same page would satisfy every count above and prove nothing.
  assert.equal(bodies.size, pages.length, 'two provenance pages rendered identically');
});

// ---- what the page refuses to put on itself ------------------------------------------------

test('an official-source link is validated against the publisher own hosts, not escaped', () => {
  // `escapeHtml` makes `http://evil.example/fake` a safe attribute value and leaves it a
  // working link under the words that name the publisher's file.
  const record = copy('lu-recueil.json');
  record.document.source_uri = 'https://evil.example/eli/etat/leg/recueil/protection_donnees';
  assert.throws(() => readProvenance(ask(record)), /which is not one of legilux\.public\.lu/);

  const scheme = copy('lu-recueil.json');
  scheme.document.source_uri = 'http://legilux.public.lu/eli/x';
  assert.throws(() => readProvenance(ask(scheme)), /must be an https URI/);
});

test('the publisher name for the work is a name, and is never offered as a link', () => {
  const html = renderProvenance(ask(live('lu-recueil.json')));
  const identifier = 'http://data.legilux.public.lu/eli/etat/leg/recueil/protection_donnees';
  assert.ok(html.includes(identifier), 'the publisher name for the work is not on the page');
  assert.ok(!html.includes(`href="${identifier}"`), 'an ELI was rendered as a link');

  // And a name in somebody else's namespace is not this publisher's name for anything.
  const borrowed = copy('lu-recueil.json');
  borrowed.document.work_identifier = 'http://eur-lex.europa.eu/eli/etat/leg/recueil/x';
  assert.throws(() => readProvenance(ask(borrowed)), /is not this publisher/);
});

test('the provenance page never becomes a reading view', () => {
  // `document.text` is null in every live payload, so this is the case a capture cannot show
  // and a future service change could introduce. A proof chain that quotes the law has stopped
  // being a proof chain.
  const record = copy('lu-recueil.json');
  record.document.text = 'Art. 1er. Le present texte est publie.';
  const html = renderProvenance(ask(record));
  assert.ok(!html.includes('Le present texte est publie'), 'the proof chain quoted the law');
  assert.ok(!reactHtml(ask(record)).includes('Le present texte est publie'));
});

test('the publisher current-state flag never reaches this page', () => {
  // The live Union record carries `binding_status: in_force` on a wording state from 2016,
  // three years before the regulation applied. It is a statement about now, and this page is
  // about one historical record, so it belongs in the dossier strip with its caption.
  const record = live('eu-gdpr.json');
  assert.equal(record.document.binding_status, 'in_force');
  const html = renderProvenance(ask(record));
  // Matched as a whole rendered value rather than as a substring: the live event chain records
  // `fields=in_force_status,...` as one of the members this service revised, which is this
  // service's own machine detail and not a claim about the law.
  assert.ok(!html.includes('>in_force<'), 'a current-state flag was printed against a 2016 state');
  assert.ok(!reactHtml(ask(record)).includes('>in_force<'));
});

test('the React title carries the record language, not the chrome one', () => {
  const html = reactHtml(ask(live('lu-loi-1915.json')));
  assert.match(html, /<p class="provenance-title" lang="fr">/);
});
