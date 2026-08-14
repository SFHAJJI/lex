# Lex, Technical Specification v4.0

**Point-in-time retrieval of regulatory text.**

Status: complete rewrite, incorporating the 2026-08-01 adversarial review of v3
(45 findings, 8 blocker/major verdicts adversarially verified)
Supersedes: `lex-spec-v3.md` (v3.0) in full
Date: 2026-08-01
Author: VS Code session (reviewer of v3)

> **Licence note (§16 is normative).** The **code** is public under **Apache-2.0**.
> No licence is granted over corpus data or index artefacts beyond the limited
> grant in §16.3 (download and use; no redistribution of any build): Lex's
> compilation and database rights are otherwise **expressly reserved**. The
> data-licence direction is **decided** (§16.3: stars-maximal), applied at each
> corpus's publication. Nothing in this document says "everything is MIT";
> v3's blanket statement was itself the data-licence decision it claimed to
> defer, and it is reversed here (D25-v4).

---

## 0. What this is, in one paragraph

Regulators publish the current rule. They do not publish the rule as it stood on a
past date. Every audit, every investigation and every dispute is about a past date.
Lex ingests regulatory text from many publishers, keeps every version it has ever
seen, and answers "what did this say on 15 March 2022?" with the exact text, the
dates it applied between, the instrument that changed it, and a hash proving the
text was retrieved rather than reconstructed. Where a publisher forbids
republication of the text, Lex keeps and serves the *timeline*, which document,
which version, in force between which dates, and links out for the text. Where
Lex cannot know the answer, it says so in a machine-readable way: an honest
refusal is a feature of the product, not a failure of it.

---

## 1. Scope

### 1.1 Who this is for

Two audiences, deliberately:

1. **Developers** who consume the corpora, the MCP server, and the published
   packages, the audience that produces GitHub stars, which is the settled
   priority (D-PR1).
2. **Organisations subject to supervision**, and the professionals serving them.
   Luxembourg financial firms first, because supervisory density is highest there,
   but the design is **sector-neutral by construction**: a regulator of electrical
   installations, a data-protection authority enforcing the GDPR or the AI Act,
   and a banking supervisor are the same shape of problem. Any regulator, in any
   jurisdiction, whose rules are legal to ingest is in eventual scope via the §1.5
   intake procedure, the architecture requires zero change per new publisher
   beyond one adapter and one corpus repo.

### 1.2 The question we answer

```
"What rules applied to us on <date>?"
"What exactly did <document> say on <date>?"
"What changed between <date A> and <date B>, and which instrument changed it?"
"Prove it."
```

### 1.3 The question we never answer

```
"Were we compliant?"
"Does this rule apply to my situation?"
"What does this rule mean for us?"
```

Those are professional opinions. They require facts we do not have, judgement we
are not qualified to exercise, and insurance we do not carry. Lex establishes what
the rule **was**. A human establishes what it **means**.

This boundary is architectural, not merely editorial: no component in this system
generates interpretive text (§9.9, fitness rule F10).

### 1.4 Sources in scope

Scope is defined by **publisher**, not by sector. A publisher enters scope when
the §1.5 intake completes.

**In scope, with each publisher's current acquisition state:**

| Publisher | What | Access | Gate |
|---|---|---|---|
| Legilux (LU) | Laws, grand-ducal regulations, codes, Constitution, règlements CSSF | SPARQL metadata plus official CC-BY manifestation files | **Cleared by D44**. Verbatim Akoma Ntoso XML is preferred; D49/D73 define bounded official-PDF recovery and explicit gaps. |
| EUR-Lex / Cellar (EU) | Regulations, directives, delegated & implementing acts, consolidated versions | SPARQL endpoint | Volume/rate measurement (R13) before nightly commitment |
| EBA, ESMA, EIOPA (EU) | Guidelines, Q&A, final RTS/ITS reports | Website; reuse permitted with attribution | none, first Tier B source, observation clock starts in increment A (§14.1) |
| BCL (LU) | Règlements and circulaires of the central bank | Website; reuse permitted with attribution | intake refresh before adapter work |
| ECB / SSM (EU) | Regulations, decisions, supervisory guides | Website; reuse permitted with attribution | intake refresh before adapter work |

**In scope, text NOT republishable, timeline only (Tier C, §1.6):**

| Publisher | What | Constraint | Gate |
|---|---|---|---|
| CSSF (LU) | Circulaires | All rights reserved; written consent required for text | **R18**, written request for *metadata/timeline* reuse sent and **answered**, or the documented legal basis recorded per §14.4, before the corpus ships |
| CNPD (LU) | Lignes directrices | Written authorisation required | same |

Note the same authority appears in both tables: **règlements CSSF** are published
in the Journal officiel via Legilux and are official acts; **circulaires CSSF**
are published only on cssf.lu under its terms of service. **Tier follows the
publication channel and its legal regime, not the issuing authority.** This split
is the cleanest public demonstration of why the tier model exists, and it is
documented in both corpus READMEs.

**Candidate publishers, sector expansion. NOT VERIFIED. Each requires the §1.5
intake before any code is written against it:**

- ILR, energy, telecoms, postal, NIS2 outside CSSF-supervised sectors
- ITM, workplace and installation safety prescriptions
- ILNAS, standardisation, accreditation, product safety
- CAA, insurance supervision (reuse terms unknown)
- CRF, financial intelligence unit circulars (reuse terms unknown)
- Administration de l'environnement, and sector equivalents
- Non-LU/EU regulators, wherever ingestion is legal, same intake, same shape

> **R5 discipline is in force.** A publisher that we cannot describe accurately
> does not enter the spec. The list above is a research backlog, not a commitment.
> Intakes for this list **start during increment A** (§14.1) because Tier B
> observation time is unrecoverable (§1.6).

### 1.5 Publisher intake procedure

Before a publisher is added, **six** questions are answered in writing, in the
corpus repo's `README`, with a source URL for each:

1. **What does it publish?** Document types, named as the publisher names them.
2. **What is the authority of each type?** Binding / comply-or-explain /
   supervisory expectation / guidance. Taken from the publisher's own words,
   cited. Never our inference (§3.6).
3. **Is it retrievable mechanically?** Endpoint, feed, sitemap, or paginated
   HTML. Does robots.txt permit the specific paths we would fetch, including
   the paths **bodies** are served from, not only listings (see R19)?
4. **May we republish the text?** Yes / no / unclear. Unclear is treated as no.
5. **May we republish the metadata and timeline** (identifiers, titles, dates,
   status, supersession chain)? Yes / no / unclear. Unclear is treated as no.
   This question exists because the Tier C product *is* republished metadata;
   v3 never asked it and shipped Tier C on silence.
6. **Does the publisher retain superseded versions?** This determines Tier (§1.6).

The intake has **two gates**, because only one question gates fetching:

```
FETCH GATE     Q3 only (robots.txt on the paths we would fetch). A five-minute
               check. Cleared → the observation clock starts that night:
               private archive repo, raw snapshots, no adapter (§11.5).
PUBLISH GATE   Q1, Q2, Q4, Q5, Q6. Cleared → the corpus repo may go public
               and an adapter may be written.
```

Private storage of a public page is not republication, so the legal posture is
unchanged, but a dozen publishers can be under observation within a fortnight
instead of two by an increment's end, and observation time is the one asset
that decays daily (§1.6). A publisher with all six answers gets a corpus repo;
one missing any answer does not. Questions 4 and 5 are independent, a "no" on
text with a "yes" (or documented legal basis) on metadata is exactly Tier C.

### 1.6 Source tiers

```
TIER A   Full text + publisher-supplied version history
         The publisher tells us the validity interval of each version.
         Examples: Legilux (jolux:dateApplicability), EUR-Lex (consolidated versions)
         → We ingest text and dates. History is complete back to what the
           publisher digitised.

TIER B   Full text, NO publisher-supplied version history
         The publisher shows only the current text.
         Examples: ESA guidelines, most sector regulators
         → We ingest text. Valid time comes from publisher-stated dates INSIDE
           the document where they exist (application dates, entry-into-force
           statements), marked asserted_by: publisher; only where the document
           itself is silent do we fall back to observation, marked
           asserted_by: observation (§7.3). TEXT history begins the day we
           started watching; that date is stated in every response, and dates
           before it get an explicit refusal, not a guess (§9.1).

TIER C   No text, metadata and timeline only
         Text republication is forbidden or not mechanically retrievable;
         metadata republication is permitted or has a documented legal basis
         (§1.5 question 5).
         Examples: CSSF circulars
         → We ingest identifier, title, dates, status, supersession chain, and
           a link. We never store the body (fitness rule F13).
```

**Tier is a declared property of each adapter**, surfaced in every response and
in the `coverage` tool. A Tier B source must never be presented as though its
*text* history predates our first observation. That is the single most likely
way this project acquires a credibility failure; fitness rule F7 and the §9.1
refusal status exist for it.

**Tier B observation time is the moat.** Every day a Tier B publisher goes
unobserved is history no competitor, and no future self, can ever recover. The
tool surface is copyable in weeks; an observation record that started earlier is
not. Therefore observation is **decoupled from integration**: a cleared Tier B
publisher gets a minimal nightly fetch-and-commit (raw snapshot + fetch metadata,
no adapter, no index) the week its intake clears, regardless of when its
increment ships (§14.1). Snapshot jobs write to **private archive repos**, never
to the corpus repo, and are dispatched and monitored like any fleet member, mechanics and backfill rule in §11.5.

**Tier C is not a degraded product.** For a supervised firm, the scarce artefact
is the *timeline*, which circular was in force on which day, and what replaced
it. The text has always been one click away. Nobody sells the timeline.

### 1.7 Explicitly out of scope

| Out | Why |
|---|---|
| Switzerland | Dropped (D17). The buyer is in Luxembourg. |
| Court rulings / case law | A judgment is a point, not an interval; needs a parallel design. Later, if ever. |
| ISO / IEC / SAE standards | Paywalled. We may reference a clause number; we never store the text. |
| IFRS standalone standards | Licensed. The EU-adopted text inside Reg. (EU) 2023/1803 carries an EEA-only reproduction notice, not open. |
| Luxembourg Stock Exchange rulebook | Written consent required. |
| Any interpretive or advisory output | §1.3. |
| Preparatory works, doctrine, commentary | Not now. |
| The Legilux flat corpus (~24,579 never-consolidated acts) | Deferred to its own increment with its own layout, index representation, and etiquette budget (§7.7, R20). No manifest field and no tool exposure until then, **except** as a named known-exclusion in `coverage` (§9.7) and in `in_force_on` population disclosures (§9.3), where honesty requires naming it. |

---

## 2. Verified data facts

These are measured, not estimated. Numbers without a source line are **not** to
be repeated in public material. Where verification is *pending*, this section
says so, v3 stated one unverified right as measured, which is the exact failure
this section exists to prevent.

### 2.1 Luxembourg, Legilux SPARQL (`https://data.legilux.public.lu/sparqlendpoint`)

```
Versioned corpus (carry jolux:dateApplicability)
  versions ........................ 4,636
  distinct works .................. ~1,390
  dateApplicability coverage ...... 100%   (the gate, passed)
  isMemberOf ...................... 100%
  inForceStatus ................... 99%
  isRealizedBy .................... 97%
  dateEndApplicability ............ 74%    (missing 26% = open intervals, in force)

Type breakdown of the 4,636
  CODE 188 + CODE_RECUEIL 708 + RECUEIL 747 = 1,643 over 54 works (~30 each)
  LOI ............................. 1,490
  RGD ............................. 1,179
  Constitution .................... 37
  unidentified .................... 287
  LOI+RGD+codes+Constitution = 4,349 = 94%

Flat corpus (no version history), OUT OF SCOPE until R20 resolves (§1.7)
  LOI 9,246 + RGD 15,333 = 24,579 acts
  → only ~6% of lois and ~4% of RGD are ever consolidated

Date range ........................ 1849-03-14 → 2030-09-15
Future-dated versions ............. 27  (2027:12, 2028:6, 2029:4, 2030:5)
Pre-2011 .......................... ~620 (13%)
2017 onward ....................... ~3,535 (76%)
```

**The only truthful coverage claim:** *dense and reliable from 2017 onward; real
but sparse before; isolated snapshots back to 1849; forward to 2030.* "Full
history since 1849" fails the first probe anyone runs.

**Units trap.** 4,636 counts **versions** of works that changed. 24,579 counts
**acts** that never changed. Different units; never in one sentence without the
distinction, and never in one manifest field (v3's `works_search_only` is
deleted).

**Consequences the spec must carry, not just record:**

- **Future-dated versions exist**, so `as_of`/`in_force_on` for future dates will
  be asked and answered from day one. The answer is a *prediction from currently
  enacted text*, revisable by any intervening amendment. Every response whose
  query date exceeds the index's build date carries `provisional: true` (§9.8).
- **`in_force_on` covers the versioned corpus only** until R20 resolves. Every
  `in_force_on` response carries a population disclosure saying exactly that
  (§9.3). The words "the whole set" appear nowhere.
- Act types `PA` (42,802) and `RC` (32,898): not identified, therefore out of
  scope. They enter scope when named, not before.

**robots.txt:** `data.legilux.public.lu/eli/…`, `/file/…`, `/filestore/…` are
disallowed. The SPARQL endpoint is not. → D14: SPARQL-first, never crawl.

**Probe results, 2026-08-01 (executed live against the endpoint, these
supersede the open questions above):**

- **R6 SETTLED.** 503,867 `jolux:Article` entities exist and **100% carry
  `dateApplicability`** (503,867 of 503,867). Article URIs are dated
  (`…/code/procedure_civile/art_127/20150901`). Article-level as-of is a
  lookup, not a segmentation subsystem.
- **R15 SETTLED.** A `jolux:Consolidation` carries `typeDocument` directly
  (`CODE`, `LOI`, `RGD`, …) and `isMemberOf` → its base Work
  (`code/procedure_civile` → `rgd/1998/08/03/n4`). Model: **Work = the
  `isMemberOf` target; DocumentType = the consolidation's own `typeDocument`;
  compilations are Works like any other.** Each consolidation belongs to
  exactly one Work, no double-counting in `in_force_on`.
- **Corpus count now 4,644** consolidations (+8 since the §2.1 measurement, the corpus is live). Codes are deep: code de l'environnement 195 versions,
  santé 121, éducation nationale 115.
- **Historical 2026-08-01 probe, superseded later that day by D44.** SPARQL carries
  no text literals; an Expression's `isEmbodiedBy` manifestation URLs live on
  the robots-disallowed `data.` subdomain; the main domain serves a JavaScript
  shell for every `/eli/` path; the SPA's internal API is unpublished and
  **will not be reverse-engineered or used**. A subsequent manifestation probe found the
  publisher's documented, robots-permitted CC-BY filestore channel. D44 therefore supersedes
  the metadata-only conclusion while preserving the no-SPA-reverse-engineering rule.
- **Etiquette note:** the ingest User-Agent identifies the project
  (`Lex/x.y (+repository URL)`), not a personal address.

### 2.2 Luxembourg, publication rights: what is actually measured (R2)

- **Measured:** dataset `62c83bfd9794ec8e47b5bc68` on data.public.lu carries a
  CC-BY tag, and the dataset page **self-describes as metadata**, it does not
  facially cover act bodies.
- **Measured:** Luxembourg act *text* carries no copyright, loi du 18 avril
  2001, art. 10, 8°: « les actes officiels de l'autorité et leur traduction
  officielle » are excluded from protection.
- **Open (R2):** the load-bearing right is therefore not copyright but the
  state's **sui generis database right** (same law, arts. 67-70, no
  official-acts exclusion), art. 67(3)'s grand-ducal-regulation conditions on
  copying state databases, and whether the open-data law of 29 November 2021
  (transposing Directive (EU) 2019/1024, art. 1(6) of which bars public-sector
  bodies from wielding the database right to prevent re-use) neutralises it.
- **Action (week one of increment A):** written question to the Service central
  de législation: *may we bulk-extract act text via the SPARQL endpoint and
  republish it (public git repository + public website), with attribution, under
  the loi du 29.11.2021 and/or the CC-BY grant, and do any art. 67(3)
  conditions apply?* Answer in writing, cited in the corpus README per §1.5 Q4.
- **Until answered: unclear = no** (§1.5's own rule), on **every** surface act
  text could reach the public: the corpus repo stays private, the
  `index-lu-legilux.db` release stays private, and the public `Lex.Web` and
  `Lex.Mcp` instances serve the **text-withheld mode** (D38, §9's
  `text_withheld` status: timeline + metadata + hashes + link-out; no bodies,
  no search snippets, no diff text). Increment A still ships a public URL on
  schedule (§14.1). R11 (does the same open-data law bind the CSSF?) shares the
  statute; one answer likely informs both.

### 2.3 European Union, Cellar (`http://publications.europa.eu/webapi/rdf/sparql`)

- Ontology is **CDM**, a different lineage from JOLux. JOLux extends ELI; CDM
  sits beside it, bridged by `owl:sameAs`. No shared vocabulary to reuse.
- **Corrigenda operate at Expression (language) level.** A corrigendum may
  correct the Italian and not the German, across 24 equally authentic languages.
  No equivalent exists in Luxembourg. → §3.3, and the paper-test pulled into
  increment A (§14.1).
- **Cellar appears uni-temporal**: valid time only; no transaction time located
  (R9). We generate transaction time ourselves (§7.4).
- Consolidated texts carry **no legal effect**, only the OJ acts are authentic.
  Every consolidated version we serve links to its constituent authentic acts
  (§9.6).

### 2.4 Comparable systems, measured, for positioning

| System | What it does | Note |
|---|---|---|
| bundestag/gesetze | German federal law as Markdown in git | 1,900 stars |
| Legilibre/Archeo-Lex | French law converted to git | 104 stars |
| Normattiva (IT) | State point-in-time, "multivigenza", 1861,  | State-operated |
| legislation.gov.uk | State point-in-time, **date in URL** | State-operated |
| e-Gov 法令API v2 (JP) | Public API with amendment history | State-operated |
| Ansvar Luxembourg-law-mcp | 4,551 statutes, 36,014 provisions, ~69 MB SQLite, 13 tools, BM25 | No point-in-time (roadmap item) |

**Consequences internalised into this spec:**

1. Point-in-time law is not novel, it is solved at state level in IT/UK/JP. Our
   claim is narrowed to *this jurisdiction, this combination, independently
   operated, hash-proof, MCP-native*.
2. The 1,900-vs-104 gap says attention accrues to **browsable data**, not
   tooling. Therefore the corpus README is a product surface (§12.3), Lex.Web is
   permalink-first (§12.1), and the increment plan ships browsable corpora
   early and often (§14).
3. Ansvar's `check_currency` reports that a statute was amended without showing
   prior text. Verify by hand before publishing as a claim, under §14.5's
   claim rules (version-pinned, date-stamped, neutrally phrased, no star
   counts).

### 2.5 Publication constraints, measured

| Publisher | Text republishable | Metadata/timeline republishable | Evidence |
|---|---|---|---|
| Legilux | **Pending R2** (metadata grant measured; content scope unverified, §2.2) | Yes (CC-BY metadata grant) | data.public.lu dataset page |
| EUR-Lex | Yes, with third-party carve-outs | Yes | Commission Decision 2011/833/EU |
| BCL | Yes, with attribution | Yes | bcl.lu copyright page |
| ECB / SSM | Yes, with attribution | Yes | bankingsupervision.europa.eu |
| EBA / ESMA / EIOPA | Yes, with attribution; carve-outs | Yes | each authority's legal notice |
| CSSF circulars | No, written consent required | **Pending R18** (request + documented basis) | cssf.lu ToS |
| CNPD | No, written authorisation required | **Pending R18** | cnpd.public.lu |
| LuxSE | No | No | luxse.com terms |

Attribution obligations **propagate into every derived artefact**: the index
stamp table carries attribution, source-terms URL, and the modifications
statement (§8.1), `provenance` surfaces them (§9.6), and the corpus NOTICE
states that they survive into forks (§16.2).

---

## 3. The neutral model, `Lex.Law`

The middle layer is named after no source. It is the vocabulary every adapter
translates **into**, and it contains no knowledge of JOLux, CDM, HTML scraping,
or any publisher.

### 3.1 Entities

```
Publisher     the body that issues the document
Work          the document as an idea, across its whole life
Version       one state of that Work, valid between two dates
Expression    that Version in one language, with its own validity interval
Observation   one sighting of an Expression's body: sha256 + source + when
Relation      a typed link between Works or Versions
Identifier    an opaque string. Never parsed. Never pattern-matched.
DocumentType  a (publisher, code, labels) triple. Never an enum.
Authority     how binding this type is, per the publisher's own words
```

`Observation` is new in v4: it is the unit of transaction time (§7.4).

### 3.2 Shape

```
Publisher ──< Work ──< Version ──< Expression ──< Observation
                 │         │
                 └──< Relation >──┘
```

### 3.3 Expression-level validity, with resolution rules

In Luxembourg, `fr` and `de` move together. In the EU they do not, a
corrigendum may correct one language and not another:

```
Work    "Regulation (EU) 2022/2554"
 └── Version    valid 2023-01-16 → 2025-01-17
      ├── Expression  fr   valid 2023-01-16 → 2025-01-17
      ├── Expression  de   valid 2023-01-16 → 2024-03-02   ← corrected
      ├── Expression  de   valid 2024-03-03 → 2025-01-17   ← the correction
      └── Expression  it   valid 2023-01-16 → 2025-01-17
```

**Language is not an attribute of a Version. It is an entity with its own
dates.** v3 stated this and then defined no mechanics; v4 defines them, because
the corpus layout and lex_ids freeze at increment A:

1. **Resolution rule.** `as_of` and `in_force_on` resolve on **Expression
   intervals** whenever a language is specified. A Version's interval is defined
   as the hull of its Expressions' intervals and is informational only.
   `in_force_on` deduplicates by Work (§9.3).
2. **Envelope rule.** The `valid_from`/`valid_to` in every response are the
   returned **Expression's** dates, consistent with the adjacent `language`
   field. Optional `version_valid_from`/`version_valid_to` carry the hull.
3. **Storage rule.** A language-skewed correction stays **inside the existing
   version directory** as an additional entry in the `expressions` list. **One
   naming rule, shared verbatim with C3 and C1:** the version's initial
   expression in a language is `<lang>.md`; a *further Expression* of the same
   language (a corrigendum with its own interval) is `<lang>.<valid_from>.md`;
   a *re-observation* of an existing expression (same interval, new body, a
   publisher correction, §7.4) appends an observation suffix:
   `<lang>.obs-<observed-date>.md` or `<lang>.<valid_from>.obs-<observed-date>.md`.
   The two cases are thereby distinguishable from the filename alone, and
   start-date naming never needs a rename when an open interval later closes.
   Filenames are conveniences: `meta.json`'s `file` field is the sole
   authority, and F12 verifies the expected name. Version directories
   therefore never overlap.
4. **Coordinate rule.** `lex_id` stays version-level (§7.2). The expression
   coordinate everywhere in the system is `(lex_id, language, valid_from)`.
   Decided now, because lex_ids go public in increment A.

This design is **paper-tested against one real Cellar corrigendum** (e.g. a DORA
language corrigendum) during increment A, not first exercised in increment B
(§14.1).

### 3.4 Identifier discipline

An Identifier is stored, compared and returned. It is **never** parsed to
extract a year, a type or a number. Any code outside an adapter that does string
surgery on an identifier is a defect; fitness rule F4 fails the build on it.
Every entity additionally carries a `lex_id`: stable, opaque, internally
generated (§7.2).

### 3.5 DocumentType is data, not code

```csharp
record DocumentType(
    string PublisherId,                              // "lu-legilux"
    string Code,                                     // "RGD"
    IReadOnlyDictionary<string,string> Labels);      // fr/de/en
```

The **list** of types lives in the corpus manifest, not in the source. A newly
observed type flows into the index, the MCP filter list and the demo dropdown
with no release and no recompile.

*Test for whether a thing belongs in code or in the corpus: would a new value
force a release? If yes, it is in the wrong place.*

**R15 is settled before the corpus writer is written, not before the first
benchmark.** `LOI`/`RGD` describe an *instrument*; `CODE`/`RECUEIL` describe a
*compilation* whose articles originate from many instruments, 35% of the
versioned corpus. If these are two axes, Work identity, the C1 layout, and every
`in_force_on` denominator change (a code version and its constituent lois would
double-count). One SPARQL query settles it; it runs in week one (§14.1), and the
answer is recorded as a decision in §17 before any layout freezes. Re-cutting
Work identity after publication would rewrite public git history, the demo
itself.

### 3.6 Authority is cited data, never our opinion

```json
"authority": {
  "level": "supervisory_expectation",
  "statement": "…",
  "source": "https://…",
  "asserted_by": "publisher"
}
```

`level` is drawn from a closed vocabulary, `binding`, `comply_or_explain`,
`supervisory_expectation`, `guidance`, `unknown`, but the assignment must trace
to the publisher's own words with a URL. Where the publisher does not say,
`unknown` is the correct value and it is surfaced. `unknown` is a feature.

### 3.7 What may enter `Lex.Law`

> **A concept enters the neutral model only when a *second* publisher needs it.**

Until then it lives in the adapter's `raw` output (§6, C3), capped, namespaced,
and read by nothing (fitness rule F11). When a second publisher needs it, it
earns a place in the model via a deliberate, diffed promotion.

---

## 4. Layers and packages

```
APPS
  Lex.Ingest          the ingestion program (a CLI)
  Lex.Mcp             the MCP server, exactly ONE binary (D27)
  Lex.Web             the public demo (server-rendered, model-free, §12)

LAYER 3, ADAPTERS (one per publisher; each knows exactly one world)
  Lex.Sources.Legilux     JOLux / SPARQL          Tier A
  Lex.Sources.EurLex      CDM / SPARQL            Tier A
  Lex.Sources.Esa         HTML listing            Tier B      (EBA/ESMA/EIOPA)
  Lex.Sources.Cssf        HTML listing            Tier C
  Lex.Sources.<next>      …

LAYER 2, NEUTRAL MODEL
  Lex.Law             Publisher, Work, Version, Expression, Observation,
                      Relation, Identifier, DocumentType, Authority
                      Knows no publisher. Knows no serialisation format.

LAYER 1, FOUNDATIONS (know nothing about regulation at all)
  Lex.Temporal        dates, intervals, as-of resolution, interval algebra
  Lex.Index           build the index, query the index
```

**Dependency direction is one-way and enforced** (fitness rule F3):

```
Apps → L3 → L2 → L1        allowed
L1 → L2, L2 → L3           build failure
L2 → any adapter assembly  build failure
```

### 4.1 D27, one MCP binary; the corpus set is deployment configuration

There is exactly one `Lex.Mcp`. The set of publishers it serves is determined by
the index files mounted into it via the index manifest (§8.6), never by a code
fork, never by per-publisher server variants.

The rejected alternative, one MCP server per jurisdiction or regulator, with a
client-side model routing among them, is recorded here so it is not re-proposed:
it multiplies the seven-tool surface into 7N near-duplicate tools (the
pathological case for client-model tool selection); it relocates §8.5's fan-out
and merge into a client that cannot merge rankings; it turns directive→national
questions (§10.5) into fragile multi-server orchestration; it fragments
`coverage` so no single honest "what we hold" answer exists; and it hands a solo
maintainer N images and N version matrices. Per-regulator distribution and
monetisation are achieved by the same binary plus a **licensed subset of index
release assets**, the public demo mounts all public indexes; a bank mounts only
its licensed ones.

### 4.2 Published packages

Two:

| Package | Why it is published |
|---|---|
| `Lex.Temporal` | Interval algebra and as-of resolution, useful to anyone with bitemporal data. No legal knowledge inside. |
| `Lex.Index` | Filtered-then-ranked retrieval over a versioned corpus. Same argument. |

`Lex.Law` and every adapter stay **internal** at least through increment B:
before B there is by definition no evidence the neutral model is right (§3.7's
second-publisher rule), and B is explicitly its falsification test (§14.2).
Publishing earlier adds semver pressure against exactly the churn the test must
permit. Adding a package later is cheap; unpublishing one is not.

### 4.3 One code repository

`lex` is a single repository. The split trigger is a second non-Lex consumer of
a layer-1 package, and nothing else. With one repo there is no repository
boundary doing architectural work, which is why §15 is load-bearing rather than
decorative.

---

## 5. Repositories

```
lex                          all code. Builds offline. References no corpus.
lex-ops                      fleet operations: dispatcher, status, index manifest (§11)

lex-corpus-lu-legilux        Luxembourg law              Tier A
lex-corpus-eu-eurlex         EU law                      Tier A
lex-corpus-eu-esa            EBA / ESMA / EIOPA          Tier B
lex-corpus-lu-cssf           CSSF circular timeline      Tier C
lex-corpus-<jur>-<pub>       one per publisher, thereafter

lex-bench                    the public benchmark
```

**One corpus repository per publisher.** Republication terms, update cadence,
failure modes and retention policy all follow the publisher. Mixing two
publishers means one publisher's bad night rewrites another's files and one set
of terms has to cover both. Per-jurisdiction grouping was considered and
rejected: it solves the wrong problem (repo count is not the solo cost driver, uncoordinated operations are, and §11 centralises those regardless of grouping)
while reintroducing exactly the blast radius this rule avoids.

**A corpus repo contains:** data, a manifest, a `NOTICE` (§16.2), a README that
is a product surface (§12.3) answering the six §1.5 questions, and one workflow
file (~15 lines) that only responds to `workflow_dispatch` from the §11
dispatcher. No cron in corpus repos, GitHub disables scheduled workflows in
repos without commit activity for 60 days, and a quiet corpus repo is *by
design* such a repo. No logic, no secrets beyond the default token.

---

## 6. Contracts

### C1, Corpus on disk

```
lex-corpus-lu-legilux/
  manifest.json
  NOTICE                         ← the §16.2 three-layer data notice
  README.md                      ← product surface (§12.3) + the six §1.5 answers
  works/
    loi-2001-04-18-droits-auteur/
      meta.json                  ← work-level: identifier, type, publisher, relations
      versions/
        2001-05-18--<sha256-of-publisher-version-id>/
          meta.json              ← version-level: validity, events, expressions,
          fr.md                     observations, provenance (C3)
          de.md
        2007-04-18--<sha256-of-publisher-version-id>/
          meta.json
          fr.md
          fr.obs-2019-03-12.md   ← a re-observation of fr: publisher correction (§7.4)
                                    (a corrigendum EXPRESSION would instead be
                                     fr.<valid_from>.md, §3.3 rule 3, one shared scheme)
```

The v4 directory key is
`<valid_from>--<lowercase SHA-256(publisher_version_identifier)>`; `valid_to`
is never in the path. The full digest is an opaque collision-resistant key,
while the unhashed publisher identifier remains in `meta.json`. It is stable
when another publisher version later appears on the same date, unlike the v3
arrival-order suffix. A closure changes only `meta.json`; it never renames the
directory. After the one-time v3→v4 replacement, **no version directory is
renamed**. Paths remain non-authoritative: the index validates the key against
`valid_from`, `publisher_version_identifier`, and `lex_id` before consuming a
record. Body files are equally **append-only**; no incremental ingest path may
open an existing body file for writing (F12).

Human-readable by design: `git log` on this tree must be legible as a
legislative history without tooling. That legibility is the demo. Therefore:
no heartbeat commits, no nightly re-stamps, no operational noise, ever
(§7.4, §11.2).

**Tier C variant**, no body files, same shape:

```
lex-corpus-lu-cssf/
  works/
    circulaire-cssf-12-552/
      meta.json
      versions/
        2012-12-11--<sha256-of-publisher-version-id>/
          meta.json              ← "text": { "available": false, "url": "…" }
```

### C2, `manifest.json` (repository root)

```json
{
  "schema": "lex-corpus/4",
  "publisher": {
    "id": "lu-legilux",
    "name": "Service central de législation",
    "jurisdiction": "LU",
    "sector": "general",
    "homepage": "https://legilux.public.lu"
  },
  "tier": "A",
  "source_endpoint": "https://data.legilux.public.lu/sparqlendpoint",
  "attribution": "Ministère d'État, Service central de législation, Grand-Duché de Luxembourg",
  "source_terms_url": "https://…",
  "text_included": true,
  "text_public": false,
  "modifications": "Converted from source RDF/HTML to Markdown; structure preserved; no text altered.",
  "document_types": [
    { "code": "LOI", "labels": { "fr": "Loi", "de": "Gesetz" },
      "authority": { "level": "binding", "source": "https://…", "asserted_by": "publisher" },
      "versions": 1490 }
  ],
  "languages": ["fr", "de"],
  "works": 1390,
  "versions": 4636,
  "expressions": 4636,
  "expressions_with_text": 4636,
  "expressions_without_text": 0,
  "valid_from_earliest": "1849-03-14",
  "valid_to_latest": "2030-09-15",
  "history_begins": "publisher",
  "ingester_version": "0.4.1",
  "ingester_code_commit": "<full reviewed Lex commit>",
  "migration_baseline_works": 1390,
  "publisher_discovery_schema": "publisher-discovery/1"
}
```

- `history_begins` is `"publisher"` for Tier A, or an ISO date for Tier B, the
  day observation began. Propagated to every response for that publisher.
- `text_public` starts `false` and flips to `true` only when the publisher's
  text gate has cleared with recorded evidence (for Legilux, D44 records the official
  CC-BY content-file channel). All
  public-facing surfaces honour it (D38, §12.2); the index stamp table carries a
  copy (§8.1).
- v3's `works_search_only` field is **deleted** (§1.7): a manifest may not
  advertise a corpus no section builds.
- `ingester_code_commit` is the full Lex commit that materialized the bytes.
  `migration_baseline_works` records the protected v3 baseline only on the
  fresh migration. The migration additionally proves every held baseline work
  and dated state is represented in the publisher plan before requesting any
  body; a count-only or percentage gate is not sufficient.
- The manifest is written only when its content changes, never as a heartbeat
  (freshness lives in `lex-ops`, §11.2).

### C3, version `meta.json`

```json
{
  "lex_id": "lu-legilux:loi-2001-04-18-n1:2007-04-18--<sha256>",
  "publisher_version_identifier": "http://data.legilux.public.lu/eli/etat/leg/loi/2001/04/18/n1/20070418/jo",
  "work_identifier": "http://data.legilux.public.lu/eli/etat/leg/loi/2001/04/18/n1/jo",
  "publisher": "lu-legilux",
  "document_type": "LOI",
  "valid_from": "2007-04-18",
  "valid_to": null,
  "valid_time_source": "publisher",
  "events": [
    { "event": "first_sighting", "observed_from": "2026-07-31T02:14:00Z" }
  ],
  "expressions": [
    { "language": "fr", "valid_from": "2007-04-18", "valid_to": null,
      "valid_time_source": "publisher",
      "observations": [
        { "file": "fr.md", "sha256": "3f9a…",
          "source_uri": "…",
          "retrieved_at": "2026-07-31T02:14:07Z",
          "observed_from": "2026-07-31T02:14:00Z" }
      ] }
  ],
  "relations": [
    { "type": "amended_by", "target": "http://data.legilux.public.lu/eli/…/2007/04/18/n2/jo" }
  ],
  "raw": { "lu-legilux": { } }
}
```

Rules, each load-bearing:

- **`events`** is the transaction-time chain (§7.4). Entries are append-only,
  each with its own `observed_from`, drawn from a **closed vocabulary that F12
  enumerates**, the change→entry mapping is total over every mutable field of
  `meta.json`:
  - `first_sighting`, the record's creation.
  - `interval_closed`, carries the new `valid_to`; **implies an accompanying
    expression-scoped closure entry for every still-open expression** (as_of
    resolves on expression intervals, §3.3 rule 1, a version closure that left
    them open would answer past it).
  - `validity_revised`, a publisher revising already-published dates (Tier A
    `dateApplicability` corrections happen). Carries `scope`, the version, or
    an expression coordinate `(language, valid_from)`, plus `field`, `old`,
    `new`. Directories never move (paths are `valid_from`-only and
    non-authoritative, C1/D41); even a revised `valid_from` leaves the v4 key
    untouched.
  - `withdrawn_from_source`, tombstone; the record is never deleted, and this
    event **closes the open observation intervals** of the record's expressions.
  - `resighted`, reappearance after withdrawal; observation reopens with a
    fresh entry, keeping append-only semantics.
  - `internal_correction`, Lex fixing its **own** ingestion error:
    `asserted_by: "lex"`, with a reason and the superseded value or sha. The
    only F12-legal path for our own mistakes; hashes we served in error stay
    explainable through `provenance` instead of vanishing.
- **`expressions[].observations`** is the expression-level transaction-time
  chain. A new entry is appended **only when the fetched body's sha256 differs**
  from the last entry's, a publisher correction. The corrected body is written
  as a new file per the single §3.3 rule-3 scheme
  (`<lang>[.<valid_from>].obs-<observed-date>.md`); prior body files are never
  overwritten or opened for writing (F12). The current body is the last
  observation's file.
- **`observed_to` is never stored.** It is derived at index build: entry N's
  `observed_to` is entry N+1's `observed_from`; the last entry is open. v3
  stored a field that could never be truthfully populated.
- **`valid_time_source`** is per-field: `publisher` (Tier A, or Tier B dates
  stated inside the document) or `observation` (Tier B fallback). Never
  per-response.
- **`relations`** are stored, not modelled, they come free in SPARQL queries
  already being issued. Refetching 4,636 versions against a rate-limited
  government endpoint is weeks of polite crawling; bytes already paid for are
  not YAGNI.
- **`raw`** holds publisher-specific fields that have not earned a place in the
  neutral model (§3.7). Namespaced per publisher, **capped at 16 KB** (ingest
  validation fails above it, the cap pairs with the clone strategy, §7.5), and
  read by nothing (fitness rule F11).

### C4, `ISourceAdapter`

```csharp
public interface ISourceAdapter
{
    PublisherDescriptor Describe();                  // id, tier, languages, doc types, attribution
    IAsyncEnumerable<WorkRef> EnumerateWorks(CancellationToken ct);
    Task<IReadOnlyList<Version>> FetchVersions(WorkRef work, CancellationToken ct);
    Task<ExpressionBody?> FetchBody(Expression expr, CancellationToken ct);  // null for Tier C
}
```

- No `FetchRelations`. Relations arrive inside `FetchVersions` if free, or not
  at all.
- `FetchBody` returning `null` is the only legitimate mechanism for Tier C. An
  adapter must not fabricate an empty body.
- A Tier A/B adapter whose body channel or text right is pending (R19/R2-class
  gates) runs in a **declared metadata-only mode**: `FetchBody` is not called
  at all, and the corpus records `text: { available: false, reason:
  "pending-gate" }`. Distinct from Tier C, and distinct from fabricating empty
  bodies, which stays forbidden.
- An adapter never writes files, never touches git, never knows the corpus
  layout (fitness rule F8). The corpus writer, one component, in `Lex.Ingest`, owns the mutation rules of C3 and is the single place fitness rule F12's
  commit-time half is implemented.

### C5, There is no internal HTTP API between Lex components

`Lex.Mcp` calls `Lex.Index` in-process. `Lex.Web` is a **server-rendered
application that also calls `Lex.Index` in-process**, C5 forbids a REST layer
*between Lex components*, not the web app serving its own HTML over HTTP. v3
left Lex.Web's transport unstated, which made the increment-A demo unbuildable
without accidentally violating this contract; it is now stated.

### C6, MCP tool surface

Seven tools, contracts as specified in §9, including parameter definitions,
pagination, size caps and refusal statuses. Frozen for increment A **after** the
§9 contracts are complete; v3 froze the surface with `work` undefined, which is
freezing an ambiguity.

### C7, Index artefact

`index-<publisher>.db`, a SQLite file, published as a **release asset** of the
corpus repo. Never committed to git. Release assets are **treated as immutable
by policy**, GitHub does not enforce it, so `Lex.Mcp`'s sha256 verification
against the manifest (§8.6) makes any silent replacement detectable and fatal
at load, and the D40 stamp signature makes the builder attributable. "Latest"
is a pointer in the index manifest, never a mutated asset. The §16.2 NOTICE is
embedded **inside** the `.db` (stamp table, §8.1), not only attached to the
release, the artefact is consumed standalone. Retention: keep the last 12
releases plus one per month; a release referenced by any published index
manifest is never deleted.

The derived `lex-articles` checkout contains one canonical
`lex-articles-generation/2` `generation.json`. Its publisher entry binds the
exact corpus commit and manifest digest, materializing ingester commit,
deriver commit and Git tree, reviewed-configuration digest, extraction
profiles, and profile-set digest. The articles Git commit binds that file; the
file deliberately does not contain its own commit. Index construction refuses
a dirty checkout or any mismatch between these coordinates and the selected
v4 corpus/configuration, then copies the verified identities into the signed
index stamp.

### C8, Adapter plugin seam

An adapter is discovered by implementing `ISourceAdapter` and being registered
in `Lex.Ingest`'s composition root. No reflection scanning, no plugin
directory. A new publisher is a pull request against `lex` plus a new corpus
repo generated from the template.

### C9, Provenance envelope

Every MCP response carries the §9.8 envelope (its core is compile-enforced,
fitness rule F6). There is no response shape without it.

### C10, Honesty fields are mandatory

`tier`, `history_begins`, `text_available`, `provisional`,
`valid_time_source`, population disclosure (where applicable), and the freshness
block (`built_at`, `last_confirmed_at`, `last_confirmed_source`) appear in every
response that could otherwise be read as a completeness or currency claim.

### C11, Status record

Every ingest run, including no-change runs and failed runs, ends by
**uploading a per-publisher status record as a workflow artifact**; the
`lex-ops` dispatcher collects and commits them (§11.2, corpus-repo tokens
cannot push cross-repo, so the write inverts). A run without a status artifact
is itself recorded as `failed` by the dispatcher (fitness rule F14).

### C12, Index manifest

`indexes.json`, the deployment contract for `Lex.Mcp` (§8.6): for each
publisher, the corpus repo, release tag, asset URL, sha256, schema version, and
embedding model. `Lex.Mcp` takes a manifest path/URL as its **only** corpus
configuration.

---

## 7. Versioning of the law itself

### 7.1 Whole snapshots, never deltas

Each version stores the complete text of that version. We never store "article 4
changed to X" and reconstruct. Reasons, in order of weight:

1. **We are never the author.** A reconstructed text is our text. A stored
   snapshot is the publisher's text with a hash proving it.
2. Retrieval is a file read, not a replay.
3. Corruption is bounded to one version.

Storage cost is handled by git's delta compression at the pack level.

### 7.2 `lex_id`

```
<publisher-id> : <work-key> : <version-key>
lu-legilux:W0001392:V04
```

Stable, opaque, generated by us, never derived from publisher URL structure.
`meta.json` is the sole **source of truth** for the `lex_id ↔ publisher
identifier` mapping; the index carries a derived copy for lookup (§8.1, §10.5).
`lex_id` is **version-level**; a **work-level lex_id** is its first two
segments (`lu-legilux:W0001392`), and tools accept either (§9); the expression
coordinate is `(lex_id, language, valid_from)` (§3.3). lex_ids are public API
from increment A onward and never change meaning. The §12.1 permalink `{work}`
segment is the work-key slug.

### 7.3 Valid time

`valid_from` / `valid_to` are what the publisher says, wherever the publisher
says it:

- **Tier A:** supplied as data by the publisher. `valid_time_source: publisher`.
- **Tier B:** most regulator documents *state their own dates*, application
  dates, entry-into-force clauses. The adapter extracts them (this is document
  reading, not identifier parsing) and marks `valid_time_source: publisher`.
  Only where the document is silent does observation supply the boundary:
  `valid_from` = first observation, closed when the text changes, marked
  `valid_time_source: observation`. v3 threw publisher-stated dates away and
  substituted crawl dates, manufacturing false legal boundaries while
  discarding citable ones; that violated its own §3.6 standard.
- Derived (observation-sourced) boundaries are flagged per-field in every
  response, and `as_of` refuses dates before the observation window rather than
  answering from an artefact of the crawl schedule (§9.1).

### 7.4 Transaction time, append-only chains inside hashed content

Transaction time answers "what did *we* believe, when?" It lives entirely inside
committed `meta.json` content, never in mutable commit metadata; no commit
timestamp is ever read. v3's rules made the axis unreconstructable (its
first-sighting rule, its no-commit-timestamps rule and its interval-closure
rewrites were mutually inconsistent, and its stored `observed_to` could never be
truthfully populated). v4 replaces them with **per-state semantics**:

> **A record's observation stamps change if and only if observed reality
> changes.** Every real change appends one dated entry to the record's `events`
> or `observations` chain (C3). Nothing is ever overwritten in place; nothing is
> ever re-stamped on a quiet night.

Consequences, each of which resolves a v3 defect:

1. **No nightly rewrites.** A no-change night touches nothing, zero blobs, zero
   commits. The anti-bloat arithmetic that motivated v3's first-sighting rule is
   fully preserved: stamp changes only ride on commits that already happen.
2. **The full transaction-time axis lives in HEAD.** Every chain is in current
   content, so the index build reads HEAD only (§8.2), no git-history walk. Git
   history remains the forensic archive (every prior body file is recoverable),
   but no nightly process depends on walking it.
3. **Publisher corrections are first-class, not silent.** A changed body for an
   existing version appends an observation with the new sha256 and a new file;
   `provenance` (§9.6) returns the full chain, so an earlier response's hash
   remains *explainable* ("superseded by publisher correction observed on X")
   instead of contradicted. lex-bench answers stay reproducible.
4. **Interval closure is a dated event, and nothing else.** Closing `valid_to`
   (26% of Legilux versions are open; closures are guaranteed) appends an
   `interval_closed` event plus expression-scoped closures, no rename, no file
    churn: v4 keys exclude `valid_to` and remain stable (C1/D41).
5. **Disappearance is a dated event, written conservatively.** A record the
   publisher no longer serves gets a `withdrawn_from_source` tombstone event.
   Records are never deleted. Because a tombstone is the one entry a later
   append cannot un-assert, it is gated twice: (a) the ingest job aborts
   **before committing anything** if `works_enumerated` falls more than 5%
   below the last successful run recorded in `lex-ops` (a transient partial
   SPARQL response is the single most likely nightly failure, and it must not
   write history); (b) a tombstone is written only after **N consecutive
   successful runs** (start: 3) show the work absent, absence counters live in
   the `lex-ops` status feed, never in the corpus. A genuine withdrawal is
   still recorded within days; a flaky night records nothing.
6. **`observed_to` is derived, never stored** (C3).

**Enforcement (fitness rule F12), specified per event type** so every clause is
mechanically checkable: `first_sighting` ↔ a new version directory;
observation appends ↔ the sha256 differs from the previous entry;
`interval_closed`/`validity_revised` ↔ the corresponding date field changed in
the same commit; `withdrawn_from_source` ↔ the run's C11 status record shows
the work absent for N consecutive successful runs; `internal_correction` ↔
carries reason + superseded value. Additionally: no ingest code path may open
an existing body file for writing, and no commit may rename a version
directory. F12 runs twice, in the corpus writer at commit time, and as a
nightly corpus-side pass over each new commit (like F13), so manual commits
cannot bypass the discipline. Cross-reference: §7.4 point 3's bench claim is
delivered by §14.5's observation pinning.

**Where "last confirmed" lives:** in `lex-ops` status records (§11.2), not in
the corpus (heartbeat commits would destroy `git log` as legislative history)
and not solely in the index stamp (an index built only on change would go stale
on quiet nights, which was v3's D12/§10.1 deadlock). The envelope's freshness
block states its source (§9.8).

### 7.5 Clone strategy

```
git clone --depth=1
```

The index build reads HEAD only (§7.4): every `meta.json` **and** every body
(needed anyway for FTS and embeddings). A shallow full-tree clone is the exact
access pattern. v3's `--filter=blob:limit=64k` existed to serve a history walk
that no longer exists; it is retired with it (D13-v4). The 16 KB cap on `raw`
(C3) keeps `meta.json` blobs small for fast tree reads regardless.

### 7.6 Growth thresholds, measured continuously, with a pre-decided shape

Every nightly run reports `.git` size and index-build duration into its status
record (§11.2), with a soft alarm at **1 GB / 10 minutes** and a hard threshold
at **2 GB / 20 minutes**. Crossing the hard threshold does not improvise: the
pre-decided migration is **bodies move to object storage; the `meta.json` tree,
README and NOTICE stay in git**, the timeline stays browsable (the corpus
degrades to a self-inflicted Tier C shape rather than disappearing), text
follows a link. R7 measures where we actually are before increment A commits to
nightly operation.

### 7.7 The flat corpus (deferred)

The ~24,579 never-consolidated Legilux acts have no layout, no index
representation, no etiquette budget, and no tool surface in this spec. They
enter scope only through a future increment that defines all four, plus a probe
(R20) of whether JOLux carries publication/entry-into-force dates that would
give each act a coarse single interval. Until then exactly **two** references
to them exist anywhere in the system, the `coverage` tool's known-gaps list
(§9.7) and the `in_force_on` population disclosure (§9.3), and no others.
Honesty beats tidiness; the disclosure is the more important of the two rules.
Note (per the round-2 counter-verdict, accepted): whether unversioned acts
carry usable entry-into-force dates is **unmeasured**, the disclosure says
"not ingested", never "no validity data".

---

## 8. The index

### 8.1 What it is

One SQLite file per publisher:

- a row per Expression **current state**: `lex_id`, publisher, work, document
  type, language, `valid_from`, `valid_to`, `valid_time_source`,
  `observed_from`, `in_force_as_of_build`, `tier`, `text_available`, `sha256`,
  `source_uri`, body path or URL
- an **observation-history table**: `(lex_id, language, expr_valid_from,
  sha256, source_uri, observed_from, observed_to)`, keyed by the full
  expression coordinate (§3.3 rule 4) so two same-language expressions in one
  version never interleave; populated from the C3 chains; serves `provenance`
- an **events table**: `(lex_id, scope, event, observed_from, detail)`
- a `withdrawn` flag on the current-state row, derived from the tombstone
- a full-text index over bodies (Tier A/B) or titles+metadata (Tier C)
- a vector index over chunk embeddings (Tier A/B only; FTS-only until R4)
- a **stamp table**: schema version, embedding model and version, corpus commit
  sha, `built_at`, ingester version, `text_public` (C2), **attribution,
  source-terms URL, the modifications statement, and the full §16.2 NOTICE
  text** (the index is consumed standalone; obligations ride inside it), the
  stamp is **signed** (D40, as amended: ECDSA-P256-SHA256): a signature over the stamp row ships as a
  detached file beside the release asset and inside the table; the public key
  lives in the `lex` README; `provenance` returns it. During the private-corpus
  phase this is what makes envelope hashes *attributable* rather than
  unverifiable assertions; after publication it distinguishes the maintained
  index from a tampered fork.

`in_force_as_of_build` is **display metadata only**. In-force determinations are
always computed from the date columns against the query date at query time, the stored boolean goes stale between builds (future-dated versions cross their
effective dates nightly). No query path uses it as a predicate.

### 8.2 Build

From a `--depth=1` clone, HEAD only (§7.4-7.5). Build time enters as an injected
parameter (fitness rule F9) and is recorded in the stamp table, so builds are
reproducible and auditable. Build cost is O(current corpus), not O(all history).

Reproducibility is claimed at the **logical** level: identical rows, FTS content
and stamps for the same corpus commit and code. Byte-identity is claimed only
for corpus files, where the sha256 chain proves it, embedding outputs are not
bit-stable across hardware and remote APIs, and pretending otherwise would hand
an auditor a false claim.

### 8.3 The one rule that cannot be relaxed, as a construct, not a slogan

> **Filters run before ranking. Always.**

Enforced structurally, not aspirationally (fitness rule F5):

- `Lex.Index` exposes **one** public query entry point, and it takes a
  **non-optional `FilterSet`** (date, language, publisher, document type, tier, each field explicitly `All` or a constraint; an omitted filter is a compile
  error, not a forgotten one).
- SQL predicates apply the FilterSet first; only surviving rows are scored.
- The **vector path is exact scoring over the pre-filtered rows**, no top-k ANN
  before filtering. At 4,636 versions per publisher this is trivially
  affordable; it stays affordable per-publisher because indexes shard by
  publisher (§8.5).

Rank-then-filter produces a confident, incomplete answer, the failure mode this
project exists to demonstrate in others.

### 8.4 Embedding model

Pinned by name and version, recorded in the stamp table, part of the index's
identity. A model change is an index rebuild and a new release, never an
in-place edit. Choosing the model requires the R4 evaluation set (30 real fr/de
questions from real articles) first.

### 8.5 One index per publisher; merging is rank-based

Not one combined file. Independent failure, independent download, independent
pinning. `Lex.Mcp` opens several and fans out; cross-publisher `search` results
are merged by **reciprocal rank fusion**, per-index BM25 scores are not
comparable across SQLite files (different corpus statistics), and merging raw
scores would be §8.3's failure mode one layer up. Vector-score merging is
permitted only when every mounted index carries the identical embedding
model+version (checkable from stamp tables at startup); otherwise `Lex.Mcp`
degrades to per-publisher grouped results **with a stated reason in the
response**. A raw-score cross-index sort fails the build (F5).

### 8.6 Distribution and pinning, the index manifest (C12)

`lex-ops` publishes `indexes.json`: per publisher, corpus repo, release tag,
asset URL, sha256, schema version, embedding model. `Lex.Mcp`'s only corpus
configuration is a manifest path/URL; it verifies each downloaded asset against
the manifest sha256 and refuses mismatches. Consequences:

- **A deployment is pinned by pinning a manifest**, one file under change
  control, which is what a bank's change process needs.
- **A scoped deployment is a subset manifest**, the per-regulator distribution
  and monetisation mechanism (D27), no code involved.
- `Lex.Index` refuses to open an index whose schema version it does not
  recognise, explicitly. It never guesses, never migrates silently.

---

## 9. MCP tool surface

Ten tools. The original increment A contracts below are retained as design history. The current
HTTP and stdio contract is MCP 2.0; see [the migration note](mcp-2-migration.md) and the live
`/developers` page for its complete tool and status surface.

**The `work` parameter, defined once for all tools:** exactly three accepted
forms, a **work-level `lex_id`** (`<publisher-id>:<work-key>`, §7.2), a
**version-level `lex_id`** (resolves to its Work; the version segment is
ignored for date resolution, so the chaining below type-checks), or a
**verbatim publisher identifier** (compared opaquely, §3.4). Titles and
citations are not accepted. The tool descriptions, the only prompt a client
model gets, state the chaining explicitly: *"unknown document → call `search`
first, take `lex_id` from the hit, then `as_of`."* Where a `lex_id` is given,
the `publisher` parameter is redundant and, if contradictory, an error.

**Legal statuses, defined once:** the implementation constants and
[MCP 2.0 migration note](mcp-2-migration.md) define the closed vocabulary.
`outside_observed_window` and `stale_cursor` are not public statuses because no production path
emits them. Bounded collection tools use explicit `limit` and `offset`. Coverage and
`history_begins` describe the observed population; `no_version_for_date` says that a held work has
no publisher version covering the requested date. A flagged wrong answer is still a wrong answer;
refusals keep `as_of` truthful and gated publishers lawful in public (D38).

### 9.1 `as_of`

```
as_of(work, date, language?, max_bytes?, cursor?) → Expression + envelope | refusal
```

The text of one document as it stood on one date. Pure lookup, resolved on
Expression intervals (§3.3). No ranking, no model. Responses above `max_bytes`
(default 256 KB) are truncated at a section boundary with `truncated: true` and
a continuation cursor, a CODE is megabytes of Markdown, and an unbounded
response destroys the client conversation the product exists to serve.
Article-level addressing is R6's outcome (probe already written).

### 9.2 `timeline`

```
timeline(work, limit?, cursor?) → [ Version { valid_from, valid_to, changed_by,
                                             tier, provisional } ] + envelope
```

Every state that document has been in, and what changed it. Paginated with
`total_count`.

### 9.3 `in_force_on`

```
in_force_on(date, publisher?, jurisdiction?, source_class?, document_type?, hierarchy?, act_form?, binding_status?, domain?, language?, limit?, cursor?)
    → [ Version ] + population + envelope
```

The set in force on a date, **computed from validity intervals at query time**
(never from the stored boolean, §8.1), deduplicated by Work. This is the Tier C
product and the compliance question in one call: *"which CSSF circulars were in
force on 15 March 2022?"*

Every response carries a **population disclosure** (C10):

```json
"population": {
  "basis": "versioned works only",
  "works_covered": 1390,
  "known_exclusions": "≈24,579 never-consolidated LU acts (not ingested; date coverage unmeasured, see coverage)"
}
```

The claim "the whole set" appears nowhere. Cross-publisher pagination is
deterministic: results and cursors are ordered by the composite
`(publisher, lex_id)`, this tool has no ranking, so the ordering is total and
stable.

### 9.4 `diff`

```
diff(work, from_date, to_date, language, max_bytes?, cursor?) → unified diff + envelope
```

Two file reads and a subtraction. No model, no interpretation. Size-capped like
`as_of`; the envelope carries both versions' `lex_id`s and sha256s.

### 9.5 `search`

```
search(query, as_of?, publisher?, document_type?, language?, tier?,
       limit?, cursor?) → [ hit ] + envelope
```

Filters first (§8.3), then ranking; cross-publisher merging per §8.5. A hit is
an **envelope without body text**: `lex_id`, dates, tier, a short verbatim
snippet, and sha256. Full text comes from `as_of`, returning bodies × hit
count would flood the client context. Never summarises. `total_count` makes
truncation visible.

### 9.6 `provenance`

```
provenance(lex_id, language?, valid_from?) → proof chain + envelope
```

Source URI, retrieval timestamps, the **full observation chain** (every sha256
this expression has ever had, with observed intervals, publisher corrections
are visible, not silent; `valid_from` selects among same-language expressions,
§3.3 rule 4), corpus commit, index build, **the D40 stamp signature**,
attribution and source terms, and, for EU consolidated text, the constituent
authentic OJ acts with the explicit statement that consolidated text has no
legal effect.

### 9.7 `coverage`

```
coverage(publisher?) → what we hold, tier by tier + envelope
```

Publisher, tier, document types, counts, `history_begins`, date range, last
successful ingest (from the freshness feed, §11.2), observation gaps recorded
by operations (§11.3), and **known exclusions**, including the flat corpus
(§7.7) and any publisher whose ingest is degraded.

**This tool exists to say what we do not have.** A system that cannot state its
own gaps cannot be trusted with a completeness question.

### 9.8 Response envelope

The envelope has a compile-enforced **core** present on every response, and
per-tool extensions. v3 demanded one full envelope on all seven tools, which was
unsatisfiable (`coverage` has no sha256; `diff` spans two versions) and
guaranteed the rule would be quietly weakened.

**EnvelopeCore (every response):**

```json
{
  "publisher": "lu-legilux",
  "tier": "A",
  "history_begins": "publisher",
  "status": "ok",
  "provisional": false,
  "freshness": {
    "corpus_commit": "a3f91c2",
    "built_at": "2026-07-31T02:34:00Z",
    "last_confirmed_at": "2026-08-01T02:19:00Z",
    "last_confirmed_source": "ops-feed"
  }
}
```

- `provisional: true` whenever the query date exceeds the index build date, future-dated law is a prediction from currently enacted text (§2.1).
- `last_confirmed_source` is `ops-feed` when `Lex.Mcp` is configured with the
  §11.2 freshness feed, else `index-build`, an offline bank deployment
  truthfully reports the weaker guarantee instead of faking the stronger one.
- Cross-publisher responses (`search`, `in_force_on`) carry one core per
  contributing publisher.

**Text-bearing extension** (`as_of`, hits, `provenance`): `lex_id`,
`document_type`, `authority`, `language`, `valid_from`, `valid_to`,
`valid_time_source` (per-field, §7.3), `version_valid_from/to` (§3.3),
`text_available`, `text` (where applicable), `source_uri`, `sha256`,
`withdrawn` (publisher no longer serves this record, §7.4 point 5),
`embedding_model` (search only).

**List extension** (`timeline`, `in_force_on`, `search`): `total_count`,
`cursor`, `truncated`. **Diff extension** (`diff`): both versions' `lex_id`s,
sha256s and validity intervals. **Population extension** (`in_force_on`): the
§9.3 block. **Coverage extension** (`coverage`): the §9.7 field list. F6's
compile-time check covers the core plus the extension declared for each tool
kind, the envelope rule is satisfiable *because* it is narrowed per tool;
v3's single-shape rule was not.

### 9.9 No generation in the server

The MCP server retrieves, filters, diffs and reports. It never summarises,
paraphrases, explains or advises. All natural-language output is produced by
the client model from returned evidence. This keeps every claim traceable to a
hash and keeps §1.3 architectural (fitness rule F10).

---

## 10. How the pieces interact

### 10.1 Write path, nightly, dispatched centrally (§11)

```
02:00  lex-ops dispatcher fires (the ONLY cron in the fleet)
       ├─ reads publishers.json (the fleet registry)
       ├─ staggered workflow_dispatch to each corpus repo (GitHub App token)
       │
       │   corpus repo: ONE workflow run, two sequential jobs
       │   ├─ JOB 1  ingest
       │   │   ├─ downloads Lex.Ingest at the version pinned in the reusable
       │   │   │   workflow (single update point, §11.4)
       │   │   ├─ pre-commit anomaly gate: works_enumerated ≥ 95% of the last
       │   │   │   successful run (from the status feed) or ABORT, commit
       │   │   │   nothing, outcome=failed (§7.4 point 5, a flaky endpoint
       │   │   │   must not write history)
       │   │   ├─ runs: lex ingest --publisher lu-legilux
       │   │   │        adapter → Lex.Law → corpus writer (C3/F12 rules)
       │   │   ├─ commits ONLY if observed reality changed
       │   │   └─ uploads its status record as a RUN ARTIFACT (C11), including
       │   │       "ran, no change", which is a SUCCESS state, not silence
       │   └─ JOB 2  index (same workflow run; no cross-workflow trigger, │       GITHUB_TOKEN pushes cannot fire other workflows, which is why
       │       v3's 02:00/02:20 choreography could never run)
       │       ├─ git clone --depth=1
       │       ├─ reads HEAD: meta.json chains + bodies → rows, history tables,
       │       │   FTS (+vectors post-R4)
       │       ├─ stamps + SIGNS (D40): schema, corpus commit, built_at,
       │       │   attribution, NOTICE
       │       ├─ publishes index-<publisher>.db + detached signature as a
       │       │   release asset (only when the corpus commit changed)
       │       └─ uploads index metadata (tag, sha256) as a RUN ARTIFACT
       │
       └─ dispatcher post-run (the only holder of a cross-repo credential):
          polls run conclusions, downloads all artifacts, writes every status
          record + indexes.json in ONE lex-ops commit → fleet summary,
          opens/updates the fleet-status issue on anomalies (§11.3)
```

### 10.2 Read path, on every question

```
client model → MCP call
             → Lex.Mcp (one binary, D27)
             → Lex.Index (in-process), per mounted index from the manifest
             → SQLite: FilterSet first, then rank; RRF merge across publishers
             → rows + envelopes (+ refusals where honesty requires them)
             → Lex.Mcp reads bodies (Tier A/B) or returns links (Tier C)
             → client model composes the answer
```

### 10.3 Why this is not circular

```
NIGHT     lex ──(Lex.Ingest, a released tool)──▶ corpus repos
ANY TIME  corpus repos ──(index-*.db + indexes.json, released data)──▶ lex apps
```

`lex` builds offline with no reference to any corpus. Corpus repos contain no
code. Neither depends on the other in the compiler sense.

### 10.4 Coupling surface

Three versioned artefacts, v3 claimed two and was wrong at fleet level, which
is how the third went unmanaged:

| | Written by | Read by | Meaning |
|---|---|---|---|
| `ingester_version` | the tool, into the manifest | humans, forensics | which code produced these files |
| `index_schema` | the builder, into the .db | `Lex.Index` at startup | can this be opened at all |
| `indexes.json` (C12) | the index job / lex-ops | `Lex.Mcp` at startup | which exact index releases a deployment runs |

A code release that changes none of the three is invisible to the data side. A
schema bump costs one index rebuild, the index is derived data, always
regenerable from the corpus.

### 10.5 Cross-publisher links

A directive implemented by a national law is a `Relation` between Works in two
corpora. It is **stored where it is observed** (C3) and **resolved by `Lex.Mcp`
at startup/query time** across whatever indexes are actually mounted, each
index carries its own `lex_id ↔ identifier` map (§7.2), so resolution is a
lookup, not a build step. v3 resolved at index build time, which cannot work:
each build runs inside a single corpus repo with no other index present, and a
dangling link would never re-resolve because nothing in the referring repo
changes when the target later appears. Under D27 this is also the honest
behaviour for scoped deployments: "target publisher not mounted" is a correct,
stated answer, not a stale artefact. Unresolvable targets are kept as dangling
identifiers with a flag, never dropped, never invented.

---

## 11. Operations, the fleet layer

New in v4. v3 had no observability at all while institutionalising "silence is
normal"; combined with GitHub's platform behaviour that guaranteed silent fleet
death. This section is the difference between twenty corpus repos costing what
five cost, and five silently rotting.

### 11.1 Hub and spoke

- **`lex-ops`** holds: the fleet registry (`publishers.json`), the dispatcher
  workflow (the fleet's only cron), per-publisher status records, the
  `indexes.json` manifest (C12), and the fleet-status issue automation.
- The dispatcher triggers corpus-repo workflows via **`workflow_dispatch`**
  using a cross-repo credential held only in `lex-ops` (corpus repos keep the
  no-secrets rule). The standing mechanism is a **GitHub App installation
  token**; a fine-grained PAT is an acceptable interim **only until the third
  publisher or 90 days, whichever comes first**, PATs expire silently, and
  the deadline is recorded here so the interim cannot ossify. Do not let App
  setup delay the first watcher by a single night.
- Dispatch-triggered workflows are exempt from GitHub's 60-day scheduled-
  workflow auto-disable; the risk is confined to the one dispatcher cron.
  `lex-ops` receives a status commit every night by construction (§11.2), so
  it never approaches the inactivity threshold, no separate keepalive is
  needed; the dead-man's check (§11.3) covers the crash case.
- Dispatches are staggered (02:00 UTC is peak cron congestion; GitHub cron is
  best-effort and routinely late) and rate-capped per publisher (D14).

### 11.2 Status model, three states, never silence

Every run uploads a status artifact; the dispatcher's post-run job, the only
holder of the cross-repo credential, collects them and writes the whole
fleet's records in **one commit per night** to a dedicated `lex-ops` branch
(corpus-repo tokens cannot push cross-repo, and per-run pushes would race;
**never** into corpus repos, where heartbeat commits would destroy `git log`
as legislative history):

```json
{ "publisher": "lu-legilux", "run": "2026-08-01T02:19:00Z",
  "outcome": "ran_no_change | ran_committed | failed",
  "works_enumerated": 1390, "versions_seen": 4636,
  "corpus_commit": "a3f91c2", "git_dir_bytes": 412000000,
  "index_build_seconds": 141 }
```

- The three outcomes are **explicit**: "ran, no change" (healthy quiet), "ran,
  committed", "did not run / failed". v3 could not distinguish a healthy quiet
  publisher from a crashed adapter, a partial SPARQL response, or a
  platform-disabled workflow.
- This feed is also the **freshness source**: `Lex.Mcp` deployments configured
  with it serve true `last_confirmed_at` in every envelope (§9.8), resolving
  v3's deadlock where the honesty field could never advance on quiet nights.
- Growth metrics feed §7.6's thresholds.

### 11.3 Alerting, one issue, loudly

The dispatcher's post-run job evaluates the fleet:

- any publisher whose last successful run exceeds N days (default 3),
- any missing status record,
- an anomalous drop in `works_enumerated` (catches endpoints silently returning
  partial results),
- soft threshold crossings (§7.6),

…and opens/updates a **single fleet-status issue** in `lex-ops`; GitHub's issue
notification delivers the email. Detected observation gaps for Tier B publishers
are written into that publisher's coverage data as **known gaps**, a hole in
the watch is recorded as data (widening derived-interval uncertainty), never
lost (§9.7). **The monitor itself is monitored:** an independent dead-man's
check, a free external uptime monitor, or a trivial second workflow in `lex`, alerts when the newest `lex-ops` status commit is older than N+1 days, so a
dead dispatcher (crash, bad edit, suspended credential) cannot die silently.
The pre-commit anomaly gate (§10.1) runs **inside** the ingest job, before
anything is written, the dispatcher's post-run check is the second line, not
the first.

### 11.4 Version fan-out, one update point

Corpus repos reference the reusable workflow by moving major tag (`@v1`). `lex`
CI advances the tag only after the fitness function and an integration run
against one **canary corpus repo** pass (at N=1 the canary is a template/
fixture corpus repo kept for exactly this purpose). The exact `Lex.Ingest`
version is pinned **inside the reusable workflow**, so upgrading the fleet is
one change in one place, corpus-repo YAML is genuinely write-once, and no repo
drifts to a stale pin. Dependabot (github-actions ecosystem) on corpus repos is
the safety net for rare breaking `@v2` migrations.

### 11.5 Observation archives, and minimum ops at N=1

**Archives.** A publisher that clears the §1.5 **fetch gate** gets a nightly
raw snapshot job the same week: a **private** per-publisher archive repo
(`lex-archive-<publisher>`) receiving raw responses plus fetch metadata, no
adapter, no model, no index. Private storage of a public page is not
republication; §1.5's publish-gate questions govern *publication* only.
Archive jobs are registered in `publishers.json`, dispatched by the §11.1
dispatcher, and upload C11 status artifacts like any fleet member, so §11.3's
gap detection covers them. **Backfill rule:** when the publisher's real corpus
ships, the adapter replays the archive in snapshot order, writing
`observed_from` = the original snapshot timestamp; F12 accepts replayed chains
(they satisfy the change→entry table; they are recorded history, not
fabrication). The archives, not the corpus, are the uncopyable asset: anyone
can re-scrape a publisher tomorrow; nobody can obtain what the live site said
on a day they were not watching.

**Minimum ops at N=1.** Dispatch staggering and cross-publisher anomaly
comparison activate at N≥2. Active from day one, regardless of fleet size: the
dispatcher, the three-state status model, the fleet issue, the pre-commit
anomaly gate, and the dead-man's check.

---

## 12. Lex.Web and the public surfaces

New as a first-class section: v3 spent forty lines on tool signatures a visitor
never sees and one line on the page they land on, while its own §2.3 evidence
said browsable surfaces are where attention accrues.

### 12.1 Lex.Web, deterministic, permalink-first, model-free

- **Stable permalinks:** `/{publisher}/{work}/{date}` resolves to the as-of
  view: the text (where R2 permits; otherwise timeline + metadata + link-out),
  the validity interval, `valid_time_source`, the sha256, tier, and a link to
  the exact corpus file. `/{publisher}/{work}` shows the timeline;
  `/{publisher}/{work}/diff/{dateA}/{dateB}` the diff.
- **Server-rendered** (links unfurl in chats and forums, the
  legislation.gov.uk distribution mechanic), calling `Lex.Index` in-process
  (C5).
- **No model in the loop.** `as_of`, `timeline`, `diff` are pure lookups: the
  demo costs cents, cannot be abused into a bill, has no prompt-injection
  surface, and every answer is hash-backed, the demo *is* the proof. A
  model-driven chat over the MCP server is a later, keyed, rate-limited
  playground, an appendix, never the front door.
- Increment A's definition of done includes: *a stranger can obtain, and
  share, a permalink to any versioned document as of any date* (§14.1).

### 12.2 The MCP server as a public surface

`Lex.Mcp` is distributed as a container image; runs anywhere, including inside a
bank, from a pinned index manifest. The public instance mounts all public
indexes. Scoped instances are subset manifests (D27, §8.6).

### 12.3 The corpus README is a product, not paperwork

Each corpus repo's README opens for the five-minute visitor, in this order:

1. What this is, in two sentences, with the honest coverage claim (§2.1's
   phrasing, verbatim).
2. A worked example: the `git log` of one famous law; one `diff` between two
   dates a reader recognises.
3. An auto-generated coverage table from `manifest.json` (types, counts, date
   range, tier, `history_begins`).
4. How to consume it (clone, index release asset, MCP, permalinks).
5. The six §1.5 intake answers with URLs, attribution, and the NOTICE summary, the diligence layer, demoted below the product layer.

Increments A and B each end with an explicit **publish/announce milestone**
(§14), the star-generating moment is scheduled, not left to chance.

### 12.4 Assistant operation contract

The assistant is a natural-language controller over the same deterministic legal operations as
the workspace, not a second search product. The model selects an operation and its arguments;
`McpCore` validates and executes them; application code derives a typed `UiEffect` only from that
verified result; and the ordinary workspace renderer opens the same coordinates. The model never
authors a route or a rendering directive directly.

| User intent | Authoritative operation | Typed workspace result |
|---|---|---|
| Find a law, article or topic | `search` | Finder query and filters |
| Read wording on a date | `as_of` | Law reader at the held version and article |
| Inspect a work's versions | `timeline` | Law reader with its version rail |
| Follow one article through time | `article_history` | Law reader with the article rail |
| Compare one work between dates | `diff` plus exact `as_of` reads for wording claims | Compare view at the requested dates and optional verified article anchor |
| List publisher states covering a date | `in_force_on` | Dated result list |
| Rank what moved across the corpus | `changes_in_period` | Time report with the same window, order and filters |
| Follow reverse legal references | `cited_by` | Citing-article result list |
| Explain holdings or prove provenance | `coverage` / `provenance` | Grounded answer and evidence links; no invented workspace state |

The raw-user search is an internal authority preflight and is never narrated or rendered as a
research result. A successful work-independent operation cancels unrelated weak candidates.
Clarification is emitted only when a work-specific operation needs an identity that remains
genuinely ambiguous or unresolved. Reviewed aliases and official identifiers resolve
deterministically inside ordinary sentences; weak metadata never authorizes a work-specific tool.
If prose synthesis or its grounding judgment fails after an operation has already produced a
valid typed view, the assistant preserves that verified workspace result and reports it with a
deterministic navigation sentence. Operation status remains part of the typed effect: an
incompatible comparison opens the two verified publisher versions but says explicitly that no
reliable diff can be produced. A new legal operation replaces unrelated prior search and period
coordinates instead of leaking them into its permalink. A deliberate legal-advice or evidence
refusal is never rewritten, and an explicit MCP gap always wins.

---

## 13. Build, release, deployment

- One CI pipeline in `lex`: build, test, **fitness function (§15)**, pack,
  publish. Semantic versioning on both published packages.
- `Lex.Ingest`: self-contained executable + container image.
- `Lex.Mcp`: container image.
- `Lex.Web`: deployed to a public URL.
- Reusable GitHub Actions workflow published from `lex`, consumed by every
  corpus repo at `@v1` (§11.4).
- `lex-ops`: dispatcher + status + manifest (§11).
- Index releases: immutable assets with retention policy (C7); manifest is the
  pointer (C12).
- Secrets: GitHub App key in `lex-ops` only; corpus repos have none beyond the
  default token.

**Ingestion etiquette (D14):** SPARQL first; never fetch what robots.txt
disallows (which currently includes Legilux body paths, see R19; no adapter
ships until the channel is compliant); identifying User-Agent with a contact
address; sequential requests with backoff; a hard nightly cap per publisher.

---

## 14. Increments

Each increment has a definition of done that is **externally observable**.
Ordering serves the settled stars-first priority: browsable corpora ship early;
the text-less timeline product is demand-driven.

### 14.1 Increment A, Legilux end to end, deployed, honestly gated

**Probe batch: EXECUTED 2026-08-01** (results in §2.1). R6, R15 and R19 are
settled; no letters are sent (operating constraint: the maintainer takes no
manual external actions, R2/R18 are standing closed gates, not pending ones).
Still open, non-blocking: the §3.3 Cellar corrigendum paper-test (before
increment B, not A, the LU layout does not depend on it now that expressions
are stored inside version directories); §1.5 fetch-gate checks for ESA and
candidates, whose observation clocks start as soon as each clears (§11.5).

**Build:**

- `Lex.Temporal`, `Lex.Index`, `Lex.Law`, `Lex.Sources.Legilux`, `Lex.Ingest`,
  `Lex.Mcp`, `Lex.Web`.
- `lex-ops` live **before** nightly operation begins, dispatcher, status
  model, fleet issue, index manifest (§11).
- **Fitness function exists and is failing red before the second package is
  created** (§15).
- `lex-corpus-lu-legilux` populated and committing nightly via the dispatcher.
- `index-lu-legilux.db` published; `indexes.json` referencing it.
- All seven MCP tools live with the §9 contracts.

**Done =** a deployed public URL where a stranger asks for a Luxembourg
document as of a past date and gets its validity interval, its timeline, its
metadata record hash, and a shareable permalink with a link-out to the
official text. Under the standing R19/R2 state (§2.1 probes) the pipeline runs
in **metadata-only mode**: no body text is stored or served anywhere, which
also means the corpus repo, being pure CC-BY-licensed metadata with
attribution, **may go public immediately**; the browsable timeline tree in
git *is* the star artefact. Body text and body hashes activate only if a
lawful channel ever appears (D42). Budget a deployment day.

### 14.2 Increment B, EU, the falsification test

- `Lex.Sources.EurLex`, `lex-corpus-eu-eurlex` (R13 volume measurement first).
- **Written prediction, before starting, with a number and a date:** how many
  lines change in `Lex.Law` and `Lex.Temporal` to add EU. If the real number is
  far above the prediction, the neutral model was wrong and increment C stops
  until it is fixed.
- Expression-level validity exercised against real corrigenda (completing what
  §14.1 paper-tested).
- Ends with the announce milestone, a browsable git corpus of EU law is the
  larger-audience dataset and the natural Show HN moment.

### 14.3 Increment C, Tier B, the sector-neutrality proof

- `Lex.Sources.Esa` + `lex-corpus-eu-esa` (intake already clear; observation
  clock already running since A, the corpus opens with real accumulated
  history, marked with its true `history_begins`).
- One **non-financial** publisher from §1.4's candidates, post-intake, proving
  "any regulator, any sector" is a property of the design, not a slogan.
- Exercises the machinery carrying the project's biggest permanent-credibility
  risk (derived history, R16): publisher-stated date extraction (§7.3),
  explicit coverage boundaries and `history_begins` disclosures (§9.1), observation-gap
  recording (§11.3).

### 14.4 Increment D, Tier C, the CSSF timeline (demand-driven)

- Triggered by a concrete buyer conversation, not by sequence. The
  financial-buyer motion and the stars motion pull in different directions;
  this spec says which increment serves which instead of implying one sequence
  serves both.
- `Lex.Sources.Cssf`, `lex-corpus-lu-cssf`. No letter will be sent (operating
  constraint), so the gate is the **documented legal basis alone**, recorded
  in the corpus README before it ships: facts-not-expression (identifiers,
  dates, statuses are unprotectable facts); the spin-off doctrine on the
  database right (BHB/Fixtures, data created by the body's own activity);
  the règlement/circulaire channel distinction. If that written analysis does
  not hold up, the corpus stores identifiers and dates only, no titles.
- `in_force_on` answering "which circulars applied on 15 March 2022?" with its
  population disclosure.
- Zero body text stored, enforced by fitness rule F13, not by promise.

### 14.5 Increment E, the benchmark

- `lex-bench`: published questions; expected answers pinned to
  **observations**, `(lex_id, language, observed_at, sha256)`, verified by
  *chain membership* via `provenance`, never by equality with current `as_of`
  output, so a later publisher correction turns a run yellow-with-explanation
  instead of red (§7.4). Prompts, model names and versions, run dates.
  Re-runnable by a hostile stranger in one command. Scheduled re-runs, results
  committed. Expected answers contain verbatim text excerpts only for
  publishers whose text gate has cleared.
- **Claim rules,** binding on every public statement about a named system:
  verified by hand before publication; **version-pinned and date-stamped** ("as
  of <date>, version <x>"); phrased neutrally ("does not currently support",
  never "dropped"); no popularity metrics (star counts are ridicule, not
  capability facts); a correction channel published in the repo; claims
  re-verified before every re-publication (R17, competitors ship roadmaps).
- One unreproducible or stale claim ends the project's credibility; the
  benchmark is simultaneously the strongest asset and the largest liability.

---

## 15. Fitness function

Runs in CI. Fails the build. Not advisory. With a single code repository there
is no repository boundary enforcing anything, so this **is** the architecture's
enforcement mechanism, which is why every rule below is specified as a
**verifiable construct**, not a slogan. v3's key rules were unsatisfiable (its
F6), unverifiable (its F5), or gameable (its F2 string-grep); rules that cannot
run get quietly weakened, which is worse than their absence.

| # | Rule | Mechanism |
|---|---|---|
| F1 | `Lex.Temporal` and `Lex.Index` reference no legal concept and no publisher | architecture test over type/namespace references |
| F2 | `Lex.Law` references no adapter assembly, no publisher-named type/namespace, and declares no publisher id constant | reference-graph test, not source grep, `"LU"` as *data* in a manifest is legal; publisher *knowledge in code* is not |
| F3 | Dependency direction Apps → L3 → L2 → L1; any reverse or lateral edge fails | project-reference test |
| F4 | No parsing of `Identifier`-typed values outside an adapter | `Identifier` is an opaque struct; analyzer bans string operations on it in L1/L2/apps |
| F5 | Filters before ranking | `Lex.Index` exposes one public query entry point taking a non-optional `FilterSet`; vector scoring is exact over pre-filtered rows; a raw-score cross-index sort fails (§8.3, §8.5) |
| F6 | Every MCP response embeds `EnvelopeCore` | base record with C# `required` members; per-tool extensions defined in §9.8; missing member = compile error |
| F7 | Tier B responses carry `history_begins` as a date, never `"publisher"` | type-level: `HistoryBegins` is a closed union; adapters declare it in `Describe()` |
| F8 | No adapter writes to disk or invokes git | architecture test (no IO/process references from adapter assemblies) |
| F9 | No ambient time in `Lex.Index` build code, time is an injected parameter | analyzer on `DateTime.Now`/`UtcNow`/`DateTimeOffset.Now` |
| F10 | No natural-language generation call in `Lex.Mcp` or below | dependency test: no model-SDK reference below Apps; `Lex.Web` likewise (D27/§12.1) |
| F11 | `raw` is written by its authoring adapter and read by nothing else; ≤ 16 KB | reference test + ingest validation (C3) |
| F12 | meta.json mutation discipline: any change appends the corresponding chain entry; any chain entry accompanies a real change | ingest validation in the corpus writer (§7.4) |
| F13 | A Tier C corpus contains zero body files | corpus validation run in CI and nightly |
| F14 | Every ingest run ends with a status record | the reusable workflow fails the run otherwise (C11) |

---

## 16. Licensing and monetisation posture

Decided now at the boundary level, because publication is irreversible and v3's
header made the maximal giveaway while claiming deferral.

### 16.1 Code, Apache-2.0, permanent

The `lex` repository (and the workflow YAML in corpus repos) is Apache-2.0.
Rationale, recorded because v3 flipped a settled decision silently: the primary
distribution is a developer running `Lex.Mcp` inside a bank; Apache-2.0's
explicit patent grant and §5 inbound-contribution term reduce enterprise OSPO
friction and complement DCO (D9). **The code licence is permanent**: every
published version remains forkable under its licence forever; "relicense later"
is not a lever and no plan may be calibrated against it. The code is not the
moat and never was.

### 16.2 Data, a three-layer NOTICE in every corpus repo and index release

1. **Underlying acts and documents:** official Luxembourg acts are outside
   copyright (loi du 18.4.2001, art. 10, 8°); EU/ESA/BCL/ECB text is reused
   under each publisher's own terms. Attribution as per the manifest's
   `attribution` and `source_terms_url`; the `modifications` statement included
   (Decision 2011/833/EU requires it). **These obligations survive into forks
   and derived artefacts**, and the NOTICE says so.
2. **Lex's compilation**, selection, arrangement, verification, observation
   history, and the index artefacts: sui generis database right and any
   compilation copyright **expressly reserved** pending the §16.3 decision.
   Reservation is reversible; an open grant is not.
3. **The code licence does not apply to the data or the index.**

### 16.3 The data-licence decision, DECIDED: stars-maximal

Decided 2026-08-01, deliberately early (the round-2 counter-verdict is right
that a deadline coinciding with maximum ship pressure is a rubber stamp). The
fork, recorded for the register:

- **Stars-maximal** (consistent with the settled priority): openly license the
  corpus compilation (e.g. CC-BY-4.0), accepting that CC-BY-4.0 licenses the
  database right too, and monetise **freshness, hosting, support and SLA**.
  The durable moat is being the maintained, trusted, nightly-verified source
  with the longest observation history (§1.6), which no licence can transfer.
- **Rights-maximal:** share-alike or reserved rights on the compilation,
  preserving a dual-licence lever at real cost to adoption and stars.

**The decision: stars-maximal.** Published corpus repos carry CC-BY-4.0 on
Lex's compilation (upstream terms pass through per layer 1). Index release
assets carry the limited grant: free to download and use; **redistribution of
any build stays reserved**. Monetisation is freshness, hosting/SLA, scoped
manifests, and signed attestation (D40), the fresh index is the product, the
corpus is the credibility artefact, and the observation archives are the moat.

### 16.4 Monetisation surfaces (what is actually sellable)

(i) index freshness subscriptions; (ii) hosted `Lex.Mcp` / support / SLA;
(iii) licensed scoped index manifests per regulator or sector (D27, §8.6);
(iv) the reserved database right on the compilation, if the rights-maximal fork
is chosen. Never: future relicensing of already-published code or data.

### 16.5 Contribution

DCO, not CLA (D9), compatible with all of the above.

---

## 17. Decision record

Numbering continues from v3; v3 decisions are restated with status.

| # | Decision | Status in v4 |
|---|---|---|
| D1 | Whole snapshots, never deltas | carried |
| D2 | Language is an entity with its own dates | carried, mechanics defined (§3.3) |
| D3 | Filters before ranking | carried, respecified as a construct (F5) |
| D4 | Generation only at the client | carried (F10, incl. Lex.Web) |
| D5 | Embedding model pinned in the index | carried |
| D6 | Fitness function fails the build | carried, every rule now mechanised (§15) |
| D7 | One code repository | carried |
| D8 | Two published packages | carried; `Lex.Law` internal through B minimum |
| D9 | DCO, not CLA | carried |
| D10 | Bitemporal via git, observation stamps inside hashed content | carried, **semantics corrected**: per-state chains (§7.4) |
| D11 | First-sighting only | **superseded** by per-state append-only chains (§7.4), v3's rule made the axis unreconstructable |
| D12 | "Last confirmed" outside the corpus | carried, relocated to lex-ops status feed (§11.2), the index-only version deadlocked on quiet nights |
| D13 | Clone filter | **superseded**: `--depth=1`, HEAD-only build (§7.5) |
| D14 | SPARQL-first, never crawl, identifying UA | carried, extended: body-path compliance is part of it (R19) |
| D15 | No `FetchRelations`; relations stored if free | carried |
| D16 | Deployed demo inside increment A | carried, strengthened: permalinks in the DoD (§12.1) |
| D17 | Switzerland dropped | carried |
| D18 | EU is day-one in the model | carried; increments A and B stay separate (falsification test preserved) |
| D19 | Neutral model named `Lex.Law`; publisher knowledge in adapters | carried |
| D20 | Source tiers declared and surfaced | carried; Tier B date sourcing corrected (§7.3) |
| D21 | One corpus repo per publisher + shared reusable workflow | carried, **conditional on the §11 ops hub** |
| D22 | Expression carries its own validity interval | carried, with resolution/storage/coordinate rules (§3.3) |
| D23 | Authority is cited publisher data; `unknown` valid | carried |
| D24 | A concept enters `Lex.Law` on the second publisher | carried, `raw` enforced by F11 |
| D25 | ~~Everything MIT~~ | **reversed**: Apache-2.0 code only; data rights reserved pending §16.3 (v3's blanket MIT mislicensed third-party text and extinguished the moat) |
| D26 | Seven MCP tools | carried, contracts completed before freeze (§9) |
| **D27** | **One `Lex.Mcp` binary; mounted indexes are deployment configuration; the multi-MCP-per-regulator + AI-router design is rejected** | new |
| **D28** | Per-state transaction time; `observed_to` derived, never stored; mutation/tombstone rules | new (§7.4) |
| **D29** | Hub-and-spoke operations: lex-ops dispatcher, three-state status, fleet alerting, App token | new (§11) |
| **D30** | Index manifest (`indexes.json`) is the deployment/pinning/distribution contract | new (§8.6) |
| **D31** | Lex.Web is deterministic, model-free, permalink-first | new (§12.1) |
| **D32** | Tier B valid time prefers publisher-stated in-document dates; observation is the flagged fallback; pre-window `as_of` refuses | new (§7.3, §9.1) |
| **D33** | Increment order A→B→Tier B proof→Tier C (demand-driven)→benchmark; observation clocks start at intake clearance | new (§14) |
| **D34** | R2/R19 gate public act text (repo and demo alike); Tier C presentation is the interim | new (§2.2) |
| **D35** | Cross-publisher relations resolve in `Lex.Mcp` at runtime, never at index build | new (§10.5) |
| **D36** | Flat corpus deferred whole: no field, no claim, no tool exposure until its own increment | new (§7.7) |
| **D37** | Benchmark claim rules: version-pinned, dated, neutral, no popularity metrics | new (§14.5) |
| **D-PR1** | GitHub stars are the priority over portfolio polish; complexity allowed in the engine, never in the visitor's first five minutes | carried from settled memory |
| **D38** | Rights-pending text withholding: `text_public` flag (C2) honoured by every public surface; `text_withheld` refusal status distinct from `text_not_available` | new (§9, §12.2) |
| **D39** | Observation archives: private per-publisher repos, fetch-gate entry, dispatcher-run, replay backfill with original timestamps | new (§11.5) |
| **D40** | Signed index stamp (key in `lex-ops`, public key in `lex` README + `/pubkey.pem`, signature via `provenance`), attestation is the sellable artefact; built in increment A. **Amended 2026-08-02: the implemented algorithm is ECDSA-P256-SHA256** (stamped in the `algorithm` field); the Ed25519 wording was never implemented, and this amendment lands BEFORE any public key or verify tooling publishes, so no rotation or trust break occurs. | new (§8.1); amended blueprint-verdict §3.1 |
| **D41** | Version directories use `valid_from--SHA256(publisher_version_identifier)`: stable as same-date states are discovered, collision resistant, and never renamed after the one-time v4 migration. The publisher identifier remains explicit in metadata; the digest is only the filesystem/key coordinate. | amended 2026-08-14 (C1) |
| **D42** | ~~Legilux runs in metadata-only mode~~ **Superseded by D44** (the robots-permitted CC-BY filestore channel); the no-SPA-API-reverse-engineering rule stands permanently | superseded 2026-08-01 |
| **D43** | Data licence decided: stars-maximal (§16.3); code licence Apache-2.0, permanent | new (§16) |
| **D44** | LU full-text channel: verbatim Akoma Ntoso XML from `legilux.public.lu/filestore` (robots-permitted; publisher documents CC-BY-4.0 on content files incl. commercial reuse; machine-readable `dct:license` per manifestation). Bodies stored byte-verbatim, append-only; closes R2/R19 with evidence. | supersedes D42/D34 |
| **D45** | `/ask` is the §12.1 model layer, made front-door by owner decision 2026-08-02: its bounded tool loop uses the SAME in-process `McpCore` the public `/mcp` serves (parity by construction, no loopback HTTP). Env-gated, IP+global daily caps (no-login constraint). Generation lives only in the Apps layer; every AI surface carries a visible not-legal-advice / not-part-of-the-record label; refusal statuses render from tool envelopes, never only the model's paraphrase. **Amended 2026-08-09 by D76:** deterministic raw-user resolution and tool authorization remain application code, while Microsoft Agent Framework composes typed claims from the resulting evidence and conditionally judges factual answers. The model never becomes the authority for work identity, tool permission, citation links, or legal text. | blueprint + design workflow + owner; amended by D76 |
| **D46** | Derived consumption layer `lex-articles` (schema `lex-articles/1`): per-provision Markdown+JSON deterministically extracted from the evidence repos into a separate HEAD-is-the-contract repo. Profiles are versioned, IMMUTABLE, permanently runnable (`akn-lu/1`, `xhtml-eu/1`; changes = new profile beside the old, frozen-fingerprint test enforces); publisher-minted anchors only; spans in Unicode scalar values; publisher-date vs observed-text disagreement disclosed as `validity_conflict`, never resolved; renumbering detected mechanically (unique text-hash match) as `anchor_events`. Rule: every increment ends with something a stranger can experience. | blueprint + verdict r1 |
| **D47** | The provision is the retrieval unit: `lex-index/2` stores text once (provisions table, external-content FTS, title-weighted ranking); search hits are provision-level; `as_of` gains `mode=outline\|select`; tool #8 `article_history` serves the per-anchor time axis with refusals `unknown_anchor` / `anchor_not_in_version` / `no_provision_history`. D27 stands: 8 tools, one binary. | blueprint inc 5-6 |
| **D48** | Alternative structural manifestations: an adapter may fetch a second, richer publisher format per expression (EU: Formex 4 via `application/zip;mtype=fmx4`, spaceless, Cellar 500s on the normalized form). The container archive is packaging (its bytes embed fetch-time timestamps); zip MEMBERS are the evidence, stored verbatim under `versions/{date}/{lang}.{format}/` with one observation (sha256 + `format` field) per member. Identity guard in the adapter: `INFO.CONSLEG START.DATE` must equal the requested version's valid_from (CONSLEG.DATE is production date, GDPR's says 2018 for the 2016-05-04 version, corrigenda incorporation), else the fetch is discarded, unverifiable content is not evidence. Derived profile `fmx4-eu/1` (immutable, schema-confidence) extracts the main member. A work+language upgrades all body-bearing versions to Formex only when every one has a verified Formex main. If one expression has no primary XHTML/XML body but does have verified Formex, Formex recovers that expression rather than leaving official wording unserved; the resulting profile boundary is explicit and comparison endpoints refuse to pair provisions across it, preventing parser-format differences from becoming apparent legal changes. Anchors continue the xhtml-eu/1 convention (`art_N`/`anx_<roman>`) so permalinks and history states survive an all-version switch. Formex also fills bodies the XHTML cap excluded (CRR ≥2020) and articles the flat-XHTML heuristic missed (CRR art_50a, d). | amended 2026-08-08 |
| **D49** | Consolidations without XML: the fallback ladder. Measured 2026-08-04 against the publisher's own catalogue: of 4,633 LU consolidations, 2,892 offer XML, 1,611 offer PDF only, and 130 offer no file at all. The gap is never per-language (0 cases with XML in one expression and not another), so it is a manifestation-level fact about the document, not about a translation. Splitting the 1,611 by what they actually are: ~1,371 are RECUEIL / CODE_RECUEIL (Legilux's thematic folders, not instruments; their PDFs reach 50 MB because they concatenate every member act, and are never ingested for text); 64 are born-digital consolidated act PDFs (Antenna House typesetter, real font layer, article markers and structural headings recoverable by deterministic parse, no OCR); 176 are Mémorial gazette scans, i.e. a whole official-gazette issue containing the act among others, which is what Legilux serves when a law was never amended (82 of those 128 works have exactly one consolidation, so the consolidated state IS the original publication). Ladder, in order: (a) link the publisher's exact PDF for that date, always, as a convenience link and never as a citation; (b) profile `pdf-lu/1` over the 64 born-digital PDFs, emitting the same provision contract as `akn-lu/1`, deterministic so the frozen-fingerprint tests hold, and the profile name IS the confidence tier, recorded per version because a work can be XML on one date and PDF on another (Code du travail: PDF-only in 2020, XML in 2026); (c) profile `pdf-memorial-lu/1` over the 176, where layout analysis is genuinely required. For (c) the model call is EVIDENCE ACQUISITION, not derivation: the raw response is stored verbatim and hashed beside the PDF with its pinned model and API version, so derivation stays a deterministic function of two stored inputs and re-derives never re-call the service. The 130 fileless consolidations have no fallback and stay record-only permanently. Rejected: converting PDF to publisher-format XML, which would place bytes the publisher never issued next to bytes it did. | new 2026-08-04 |
| **D50** | The extraction profile is a published confidence marker, carried to the reader. D49 recorded it per version in the derived layer and the index dropped it, so 64 PDF-derived versions served wording indistinguishable from publisher XML under an envelope reading tier A, which is the SOURCE's tier and says nothing about how the words were obtained. `docs` gains a `profile` column, populated from the derived `generator.profile`; `as_of` and the other document-returning tools emit `extraction_profile`; `coverage` reports the mix; /coverage renders it in words; and the reader shows, beside the text, that the words are the publisher's while the division into articles is ours, inferred from layout. Per version throughout, never per work: loi-1980-03-07-n1 is pdf-lu/1 on 2025-03-11 (180 articles) and akn-lu/1 on 2026-07-05 (222), and a work-level answer would be wrong for one of them. Measured after the first run: LU akn-lu/1 2,885 + pdf-lu/1 64, EU fmx4-eu/1 53. | new 2026-08-04 |
| **D51** | The citation graph is published, and the assistant reaches the workspace's controls. The derive layer had always captured the cross-references publishers write into their own text with their ELI target (403 of the Code du travail's 1,197 articles carry them) and surfaced none: they now reach the index (flattened into a `citations` table with the target resolved to a work slug, 128,465 edges over 3,172 cited works), the API (`citations` on a provision), a 10th tool `cited_by` for the reverse direction, and the reader. The reverse is the point: "what depends on this law" is a question about edges and no phrasing of a search query reaches it. Separately, `changes_in_period` and `search` gained the filters the index already understood (document_type, comma-separated, `!` to exclude; language) plus `offset`, exposed as five layers by legal weight rather than fifteen type codes, because a reader asks about laws and regulations, not about RGC. A thematic collection needs no special case: it is simply one more type. Finally UiEffect gains `workspace` (layer, page, language) and `cited_by`: the assistant could say what to SHOW but not how the workspace should be SET, so a filtered answer arrived beside unfiltered controls. D31 holds, this is still a FIELD and never a response type. | new 2026-08-04 |
| **D52** | EU expansion is configuration-led and temporal: reviewed domain seeds plus bounded amendment, corrigendum, repeal, predecessor, successor, legal-basis and directly related delegated or implementing closure. Selection decides which works enter, never which official dated FR/EN expressions survive. Loose citations do not recursively expand scope. | hybrid-eu/1, shipped |
| **D53** | `lex-index/3` content-addresses exact UTF-8 provision text and maps every version occurrence to that text state. FTS indexes distinct work/anchor wordings and semantic chunks embed unique text, while timelines, hashes, JSON, rendering and diffs reconstruct the same authoritative provision bytes. Version 2 and version 3 remain readable during rollout. | hybrid-eu/1, shipped |
| **D54** | Hybrid retrieval is local and deterministic: FTS5/BM25 plus a pinned multilingual encoder, compact vectors and fixed RRF. Azure AI Search is rejected at current scale. Keyword remains default until the public exactness, temporal, relevance, latency and memory gate passes. | hybrid-eu/1, gated |
| **D55** | Query indexes remain local. Container Apps serves a small verified set; any single artifact above 2 GiB moves publication to Blob, a mounted set above 2 GiB triggers a zero-traffic VM benchmark, and a set above 4 GiB or a failed cold-start, latency, memory or two-release retention gate requires a VM-managed data disk. Blob and Azure Files are never the SQLite/vector query path. | building; Container Apps path shipped, oversized releases blocked until VM path exists |
| **D56** | Legal hierarchy is normalized filter metadata, not a database boundary. Physical indexes follow publisher provenance and update semantics; one logical query spans them. Original publisher classifications remain available beside normalized fields. | hybrid-eu/1, shipped |
| **D57** | Lex never synthesizes consolidation. An amended work without publisher-consolidated wording retains its official text and events with `consolidation_status=not_published`; the API never stretches stale wording beyond evidence. | hybrid-eu/1, permanent |
| **D58** | Artifact trust is anchored outside the artifact: a release-pinned public-key fingerprint verifies a signed canonical manifest covering databases, vectors, model, tokenizer, scope configuration, schema and source commits. The embedded stamp remains public provenance, not the runtime trust root. | hybrid-eu/1, shipped |
| **D59** | Fuzzy search is a visible additive fallback only when exact lexical retrieval is weak. It never rewrites quotations, CELEX/ECLI identifiers, article numbers, dates or short tokens, and every expansion returns in the response. | hybrid-eu/1, shipped |
| **D60** | Public architecture claims are separated into current, next, decisions and benchmarks. Runtime counts come from mounted indexes; future status comes from one committed registry; measurements require code, corpus, artifact, environment, timestamp, sample count and review status. | hybrid-eu/1, shipped |
| **D61** | Article and comparison exports are transparent reading aids built lazily from the exact structured MCP payload and diff pieces already on screen. They record publisher sources, dated permalinks, extraction profile and full provision hashes; they never re-fetch, re-diff or alter wording. Markdown is selected over generated PDF so an unofficial export stays inspectable and cannot plausibly resemble a publisher artifact. | hybrid-eu/1, shipped |
| **D62** | Search, dated reading, version history and comparison stay in one URL-addressable temporal research workspace. Separate search, reader and diff products and chatbot-first navigation are rejected because they hide or discard legal context while the user moves from discovery to verification. The cost is a denser interface that requires progressive disclosure and deliberate control grouping on smaller screens. | hybrid-eu/1, shipped |
| **D63** | Luxembourg scope is catalogue-specific. The current adapter is complete for the 1,399-work, 4,638-record Legilux `Consolidation` catalogue measured 2026-08-06, not the broader 150,187-resource `Act` catalogue. Expansion selects normative document classes and implements their publication, validity, manifestation and consolidation-link semantics before later class additions become configuration-led. Bulk-ingesting every `Act` as though it were a consolidation is rejected. | planned; measured boundary in `docs/luxembourg-scope.md` |
| **D64** | Search filter inventories come from verified mounted indexes, not a second hard-coded web list. Jurisdictions, hierarchies, reviewed domains, act forms, binding statuses and languages therefore appear when their indexed data appears. Stable lawyer-facing overrides label known vocabulary; unknown reviewed values receive a deterministic readable fallback. | hybrid-eu/1, shipped |
| **D65** | Offline semantic indexing embeds first-seen unique chunks in deterministic bounded batches. Chunk preparation uses bounded character windows so a multi-megabyte annex is traversed linearly rather than copying every remaining suffix; preparation and embedding batches emit current-item heartbeats. Batch size is an operational memory/throughput setting only: it cannot change chunk order, vector ordinals, content hashes or the single signed SQLite/vector artifact pair. Partial shards are not published or glued together. | hybrid-eu/1, shipped |
| **D66** | Lex owns legal semantics and reproducibility, not commodity engine internals. Temporal selection, provenance, consolidation refusals, hierarchy filters and authoritative provision mapping remain in Lex. SQLite FTS5/BM25, ONNX Runtime, Microsoft tokenization, maintained document parsers and platform cryptography provide the general-purpose primitives. Replacing a primitive requires measured parity and must preserve the signed artifact contract; outsourcing the complete legal index is rejected at current scale. | hybrid-eu/1, shipped |
| **D67** | MCP protocol handling uses the official C# SDK. `Lex.Mcp` is the transport-neutral legal-tool library, `Lex.Mcp.Stdio` is the standalone local host, and Lex.Web composes the official Streamable HTTP adapter over the same in-process tool core. A separate MCP Container App is rejected until measured traffic, release cadence, tenant isolation or SLA needs require independent scaling; splitting it today would duplicate index memory/storage, cold starts and failure modes for no isolated workload. | hybrid-eu/1, shipped |
| **D68** | The bounded, presentation-only line diff uses DiffPlex after Lex has selected the dated versions, decided comparability and aligned provisions. The generic library replaces the handwritten quadratic LCS matrix but cannot choose legal dates, anchors or consolidation semantics. Lex retains the large-change guard, output cap, escaping and compatibility tests. | hybrid-eu/1, shipped |
| **D69** | Semantic indexing is machine-portable and resumable. Large first backfills may use a reviewed local DirectML GPU; ordinary Fleet builds retain CPU fallback. Both run the same deterministic chunk order and final writer, record the ONNX runtime and execution provider in provenance, and must pass the same artifact and retrieval gates. A durable cache keys final quantized records by chunk SHA plus model, tokenizer, vector format, dimensions, runtime and provider, committing each batch so interruptions and later scope additions reuse only compatible work. The final output remains one signed vector/index pair; publishing partial shards or making the cache a serving database is rejected. | hybrid-eu/1, shipped |
| **D70** | Legal chunk boundaries are chosen before hardware optimization: retain an intact provision when it fits, split at paragraphs, and token-split only an overlong paragraph. DirectML then embeds those immutable chunks in fixed 32/64/128/256/512-token buckets with masked transient padding. Padding is never stored, hashed, cited, searched or compared. The bucket policy is stamped and included in the cache identity; changing it invalidates prior cached vectors. Variable per-batch shapes, universal 512-token padding and GPU-driven legal chunking are rejected. | hybrid-eu/1, shipped |
| **D71** | A cross-publisher result list fuses each publisher's independent local ranking with deterministic reciprocal-rank fusion (`k=60`); it never concatenates indexes or compares their raw BM25/vector scores. Exact duplicate hits receive both votes, publisher provenance remains attached, and a single qualifying publisher retains its original order. The MCP continues returning one signed/freshness envelope per publisher; fusion is a shared presentation concern used by the finder and law picker. | hybrid-eu/1, shipped |
| **D72** | Publisher time axes carry explicit semantics. Legilux applicability intervals may be labelled “in force”; EUR-Lex consolidated-expression dates are labelled official publisher wording states and never presented as entry-into-force or application dates. The semantic is committed into new index stamps and MCP envelopes, while clients retain a narrow EUR-Lex compatibility fallback for older verified artifacts. Visual comparison parses the stored Markdown before diffing so presentation delimiters cannot be reported as legal amendments; evidence exports retain the exact stored Markdown and hashes. | hybrid-eu/1, shipped |
| **D73** | Missing official wording is recovered only through additive immutable profiles, never by weakening a published parser. `akn-lu-document/1` exposes a non-empty official Akoma Ntoso body as one document when the publisher supplied no article or annex boundary. `pdf-memorial-lu/2` runs only after frozen v1 refuses, independently verifies the requested act inside the gazette, prefers the latest verified `Texte coordonné` boundary, and normalizes disclosed older article typography in the derived DOM. It uses one document boundary when a strongly identified official section has no unambiguous single article sequence, including an articleless nomenclature or an approving act whose annex restarts at Article 1. A linked issue containing another act is refused even when it has readable articles. Measured on the complete 2026-08-08 Luxembourg corpus: 22 of 23 prior no-provision records recovered, one confirmed publisher source mismatch refused, zero derive errors. | shipped 2026-08-08 |
| **D74** | Embedding throughput is bounded by both item count and padded-token count. The reviewed item batch remains an upper bound, while each fixed token bucket uses at most 32,768 padded tokens per inference call (`effective_batch = min(item_batch, floor(token_budget / bucket_tokens))`, never below one). The token budget is validated and stamped. It changes only transient inference grouping: legal chunks, hashes, cache identity, vector ordinals and final artifact format remain unchanged. This replaces a single item batch across every bucket after an 8 GB DirectML worker completed 82.5% of 106,487 Luxembourg chunks and then exhausted memory in the longest bucket; the content-addressed cache resumes completed batches. | shipped 2026-08-08 |
| **D75** | Search enrichment is a build input, never legal evidence or a runtime database. Production first retains publisher identifiers, titles, short titles and classifications in a typed work catalog, then adds only reviewed collision-aware aliases for verified gaps. Model-derived descriptions, concepts and synonyms remain a separate quarantined trust class until an independent held-out ablation proves incremental recall without ranking, latency or unaffected-query regressions. Work vectors append after provision vectors in the same signed vector artifact with complete typed ordinal validation and a separate enrichment digest. Publisher text, official titles/identifiers, dates, hierarchy, status, relationships, occurrences and hashes are unwritable through this path; a clean corpus re-ingest and pre/post hash, read and diff invariants gate removal of the legacy code-name table. | shipped 2026-08-09; model-derived weak discovery remains quarantined |
| **D76** | Exact navigation remains deterministic. Application code resolves the raw user query, authorizes work-specific tools and runs a bounded tool-calling retrieval loop over the same transport-neutral `McpCore`. Agent Framework then composes claim-typed prose from the evidence and runs a separate conditional grounding judge. Typed clarification, exact `as_of`/article validation, bounded restorable memory and deterministic citation validation remain outside model authority; a peer-agent research hierarchy and chatbot-first navigation remain rejected. | shipped 2026-08-09 with guarded clarification and evidence contracts |
| **D77** | Search and catalogue results use jurisdiction as the only top-level disjoint partition. Luxembourg and EU remain visible in all-scope results; matching passages nest under their work. Practice area, hierarchy, source class, legal form, legal status and language stay as jurisdiction-scoped facets because they overlap or cut across one another. The catalogue exposes every mounted source class (friendly label plus publisher code), clears source class when jurisdiction changes, and distinguishes full, partial, collection-metadata and unavailable-text coverage instead of collapsing them into a work-level boolean. Empty or single-choice facets are hidden after scope selection, incompatible state is cleared, and URLs preserve the chosen scope. | shipped 2026-08-10 |
| **D78** | The assistant is a verified operation controller, not a second search engine. The model chooses an MCP operation; MCP validates and executes it; application code derives a typed workspace effect from the result; the normal workspace renderer then opens the same coordinates. Raw resolution stays internal, clarification exists only at a real identity boundary, and evidence-only operations never invent a visual state. A typed operation replaces unrelated prior workspace coordinates; unavailable comparison status remains explicit and can open verified endpoints without claiming that a diff succeeded. For a standalone catalogue ranking, the normal workspace renders the bounded rows and source links; the chat retains the grounded answer and coverage disclosure in the user's language without duplicating those links as a second list. An omitted assistant ranking class uses the workspace's legal-instrument default (`!RECUEIL,!CODE_RECUEIL`) before MCP execution, while an explicit collection scope is preserved, so trace counts, typed effects and the reloaded workspace always describe one population. | shipped 2026-08-09; amended 2026-08-10 |
| **D79** | Conversation continuity is server-owned, ephemeral and tab-scoped by a random opaque capability. The browser keeps the token and at most six visible turns only in component memory, never web storage or a URL, and sends only the current message. The server retains at most six accepted turns for 30 idle minutes, bounded to 32 KiB per thread, 1,024 threads and 16 MiB per process; it stores only the token's SHA-256 digest. Expired, evicted and forged tokens fail closed, and token-bearing responses are private and `no-store`. Structured subject authority is replaced by a resolving turn and cleared by a fresh unrelated aggregate or search turn; prior raw text and assistant prose are never reinterpreted as authority. A visible new-conversation control resets the thread while preserving the legal workspace. Restarts lose the memory, and durable personal profiling is rejected. | building 2026-08-14; implementation and contract tests complete, production proof pending |
| **D80** | Assistant streaming uses a versioned request envelope with an ingress request ID and strictly increasing sequence. Each accepted operation result is emitted before optional synthesis. The browser ignores stale or duplicate frames and never repeats a failed non-idempotent POST automatically. One in-memory idempotency registry binds a bounded request fingerprint to its completed response for ten minutes; conflicting key reuse fails closed. | building; implementation and contract tests complete, production proof pending |
| **D81** | Public assistant and MCP work is bounded before execution. The explicit public assistant default is 200 accepted turns per ingress-derived client and an independent 400 accepted turns globally per UTC day, with at most 4 executing concurrently. Assistant admission checks per-client quota, concurrency and global quota in that order; invalid, rejected, duplicate and oversized requests consume no accepted turn. Signed release evaluation uses separate bounded admission and consumes neither public daily counter; it never bypasses shared concurrency, queue, deadline, token or spend limits. MCP enforces a 64 KiB streamed-body limit, closed typed arguments, response collection and total legal-text budgets, 8 executing plus 16 queued calls, a 2 second queue deadline, 2 hybrid calls, and rolling 120/client and 600/global minute ceilings. Production remains one application replica while these process-local controls are authoritative. Raw prompts and client addresses are excluded from error bodies and telemetry; only a closed metadata tag set is emitted. | building; public policy and burst tests complete, signed evaluation admission and production proof pending |
| **D82** | Assistant release evidence measures only clocks it owns. The signed SSE report separates planner, MCP, first typed result, optional synthesis, terminal and transport/queue-residual durations and binds zero-traffic Azure evidence before and after the run. Browser `operation_result` received-to-presented timing is measured by Playwright against the same code/revision release; an HTTP client never fabricates a paint or pure-network duration. Reviewed Microsoft Retail Prices meter IDs and rates are part of the signed catalog and cannot be overridden at execution. | building; implementation complete, candidate measurements pending |

---

## 18. Open risks

| # | Risk | Kills what | How to settle |
|---|---|---|---|
| **R2** | ~~Does the Legilux reuse grant cover act content?~~ | public republication of act text | **SETTLED by D44**: the official manifestation metadata identifies content files under CC-BY-4.0, including commercial reuse. |
| R4 | Embedding model chosen without an evaluation set | search quality, silently | 30 real fr/de questions from real articles, before model choice |
| R6 | ~~Do articles carry their own `dateApplicability`?~~ |, | **SETTLED 2026-08-01**: yes, 100% of 503,867 articles (§2.1). Article-level as-of is a lookup |
| R7 | Corpus repo growth | the storage model | pack 79 versions of one code, measure; thresholds + pre-decided shape in §7.6 |
| R9 | Cellar has no transaction time | the EU bitemporal claim | we generate it (§7.4); verify no publisher equivalent exists |
| R10 | EU corrigenda semantics at Expression level | the schema | paper-test in A (§14.1); exercised for real in B |
| R11 | Does the LU open-data law bind the CSSF? | CSSF tier | read the scope article; ask; shares a statute with R2 |
| R12 | Tier C link rot | the timeline product | store title + dates + status so a dead link still identifies the document |
| R13 | Cellar volume and rate limits | increment B schedule | measure before committing to nightly |
| R14 | Authority level absent from publisher's words | §3.6 integrity | `unknown` is the answer; never infer |
| R15 | ~~Instrument vs compilation axes~~ |, | **SETTLED 2026-08-01**: Work = `isMemberOf` target; type = the consolidation's `typeDocument`; one axis, no double-counting (§2.1) |
| R16 | Tier B derived history mistaken for real history | credibility, permanently | D32: publisher-dates-first, refusals, F7, gap recording (§11.3) |
| R17 | A competitor ships point-in-time first | positioning | observation clocks are the uncopyable head start (§1.6); claims re-verified per §14.5 |
| **R18** | Tier C metadata republication vs all-rights-reserved ToS | the CSSF/CNPD product; regulator goodwill | §1.5 Q5 letter + documented legal basis before increment D |
| **R19** | ~~Legilux body acquisition channel~~ |, | **SETTLED positive by D44**: official, robots-permitted `legilux.public.lu/filestore` manifestations; the unpublished SPA API remains out of scope. |
| **R20** | Flat-corpus dates: does JOLux carry publication/EIF dates for never-consolidated acts? | whether the flat corpus can ever join `in_force_on` | probe when the flat-corpus increment is scheduled; until then §7.7 |

---

## 19. Explicitly not built

Interpretation, advice, or compliance conclusions. Case law. ISO/IFRS/LuxSE
text. Switzerland. A REST layer between Lex components. Per-publisher MCP
servers or an AI router across MCP servers (D27). A plugin discovery mechanism.
Delta storage. A user account system.
The flat corpus, until its increment (§7.7). Anything that writes to a
publisher's systems.

---

## 20. External gates, what blocks "doing everything"

Everything in this spec is buildable by one person except where an external
party or a deliberate choice gates it. Recorded so nothing is discovered
mid-build:

**Operating constraint, recorded 2026-08-01:** the maintainer takes no manual
external actions, no letters, no consent requests. Every gate that required
one is a **standing closed gate**, and the design operates fully inside it.

| Gate | Blocks | State |
|---|---|---|
| R2 (Legilux act-content right) | public act text on any surface | **cleared by D44** with the publisher's machine-readable CC-BY manifestation evidence |
| R19 (body channel) | body ingestion | **cleared by D44**; official filestore manifestations are used, never the unpublished SPA API |
| R18 (CSSF/CNPD metadata basis) | increment D shipping | documented-legal-basis path only (§14.4); demand-driven anyway |
| R11 (open-data-law scope) | CSSF tier upgrade | CSSF stays Tier C |
| Dispatcher credential | §11 ops hub | fine-grained PAT interim (deadline: N=3 publishers or 90 days, §11.1) |
| R4 evaluation set | vector search | FTS-only search until done |
| §16.3 data licence |, | **decided: stars-maximal (D43)** |
| R6 / R15 |, | **settled** (§2.1) |

Nothing blocks increment A. The corpus repo (pure CC-BY metadata) and the
demo may both go public immediately.
