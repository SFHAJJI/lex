# Known defects and deferred work

Every entry states what is broken, what it costs today, how it would be fixed, and what fixing it
buys. Nothing here is a placeholder: an item is either measured or explicitly marked unmeasured.

Sources: `docs/design-verdict.md` (corpus), `docs/design-verdict-assistant.md` (assistant), and
defects measured against the running system on 2026-08-13.

## Closed since the verdicts

| item | closed by |
|---|---|
| Assistant 0, verify what production mounts | Measured: production serves `lex-index/3` with `work_catalog_available: true`. The verdict's leading finding, "deployed indexes missing the entire work catalog", was **refuted** |
| Assistant 1, ship the work catalog | Already true in production, see above |
| Assistant 3c, EMIR and MAR aliases | #167, EU enrichment 31 to 50 entries |
| Assistant 3d, demote amending-clause matches | #172 |
| Assistant 4a, scope the date-guard stand-down | #165 |
| Assistant 4b, enforce runner-up disclosure | #170 |
| Assistant 4c, delete the legacy assistant path | #164, 527 lines |
| Assistant 4d, close the `navigate` phantom | #168 |
| Assistant 6, bound and test the finalizer | #171 deadline, #169 tests |
| Assistant 10, merge the corpus branch | #174 |
| Corpus 1, expressions mislabelled as versions | #174 |
| Corpus 4 and 5, empty bodies stored as covered, blocked primary backfill | #174 |
| Corpus 10, counter test could not fail | #174 |
| Corpus 11, log lines bypassed the injected writer | #174, #177 |
| Corpus 1, positional same-date version identity | `lex-corpus/4`: full publisher version id is persisted and its full SHA-256 forms the stable version key; the one-time migration refuses a missing/replaced held state before body acquisition |

---

## 2. `with_text` measures none of its target gap classes

**What is broken.** The manifest counts an expression as having text when a body file exists,
regardless of whether extraction produced any provisions or whether those provisions hold wording.

**Effect, measured.** The manifest said 3,154 expressions with text while the served coverage number
was 3,114. **23 Luxembourg versions** whose extraction produced zero provisions count as "with text"
in the manifest and are correctly excluded by `text_public`. Separately, **6,424 provisions across
the corpus hash to the empty string** (measured 2026-08-13: LU 2,562, EU 3,862). The manifest number
is strictly weaker than the number already published elsewhere, so the two disagree in public.

**Fix.** Define coverage as provisions carrying non-empty text, derive the manifest number from the
same source the served coverage uses, and publish one number.

**Gain.** The corpus stops contradicting itself in public, and the coverage tool's promise, that it
exists to say what is *not* held, becomes true at the manifest layer too.

**Why not now.** It depends on item 3 below: a threshold has to exist before a metric can be defined
against it.

## 3. No extraction quality gate

**What is broken.** A provision whose trimmed text is empty is neither a skip nor an error. It still
mints a `text_sha256` over the empty string, so it counts as coverage, and the later arrival of real
text reads as an amendment that never happened.

**Effect, measured.** **6,424 of 537,035 provisions (1.2%)** extract empty. Concentrated: the
Luxembourg financial-sector law holds **105 of 145** empty in its 2003 consolidation. The verdict
counted **1,603 false amendment states** produced this way.

**Fix.** The counter shipped in #176. What remains is the gate: a ratchet that fails a run when a
work's empty-provision count *increases*, which does not punish the existing backlog. `derive`
already exits 2 on a non-empty error list, so the mechanism exists.

**Gain.** Kills the false-amendment-history class and stops the backlog growing silently.

**Why not now.** Nothing blocks it. This is the next fix after the current one.

## 4. `pdf-memorial-lu/1` extracts poorly

**What is broken.** The Memorial gazette PDF profile leaves most provisions empty and lets page
furniture into headings.

**Effect, measured.** 105 of 145 provisions empty on `loi-1993-04-05-n1/2003-10-01`. Headings carry
gazette page numbers: `"Les agents administratifs du secteur financier. 3227"`. This is the largest
single contributor to item 3, and it lands on Luxembourg's central financial statute.

**Fix.** A new profile `pdf-memorial-lu/2`. Profiles are immutable by contract, so this ships
alongside `/1` rather than editing it, and the frozen fingerprint tests stay green.

**Gain.** Recovers real wording for the most-cited Luxembourg finance law, and removes page numbers
from headings across the Memorial-derived corpus.

**Why not now.** Medium effort, needs a re-derive and a rebuilt index to be visible, and the
before/after number only became measurable today.

## 5. `work_resolution_status` reports `not_requested` when resolution failed

**What is broken.** `BasicQueryPlan` returns `not_requested` whenever no legal identifier token is
present and no identity match was found. The work search **did** run.

**Effect.** A caller, including the assistant's planner, cannot distinguish "the question named no
work" from "the question named a work and we failed to resolve it". Those warrant opposite
behaviour: the first is a normal corpus-wide search, the second is a clarification.

**Fix.** Report `unresolved` when a work-shaped mention was present and matched nothing, keeping
`not_requested` for genuinely work-free queries.

**Gain.** The assistant can ask a clarifying question instead of silently searching everything, which
is the failure mode behind the CRR/EMIR class.

**Why not now.** Small, but it changes a published contract value, so it needs the MCP status table
and the assistant's branching updated together.

## 6. Manifest counters mix two populations

**What is broken.** The counters read the add-only merged on-disk expression list, while works,
versions, languages and the progress denominator read the current plan.

**Effect.** A dropped language is never pruned, can never be fetched again, and keeps a permanent
`without_text` floor, while the manifest counts a language its own `languages` field no longer lists.

**Fix.** Define the counted population once, current and non-withdrawn, and derive every counter from
it.

**Gain.** The manifest stops being internally inconsistent, and `without_text` stops carrying a floor
nothing can ever clear.

**Why not now.** Interacts with items 1 and 2; doing them separately would mean three passes over the
same code.

## 7. Manifest count fields are checkable by nothing

**What is broken.** `CorpusIntegrity.Verify` does not define or check the population the three count
fields describe.

**Effect.** The counts can drift from reality without any gate noticing. Tonight's failure was the
opposite case, a bound that *did* fire, which is what made the gap visible.

**Fix.** Define the counted population in the contract and assert it during verification.

**Gain.** The integrity check covers the numbers the product publishes, not only the files.

**Why not now.** Blocked on item 6 defining the population.

## 8. Contract versioning of the new manifest fields

**What is broken.** Three non-required, non-nullable ints were added under an unchanged
`lex-corpus/3` schema id. Absence deserializes to `0`, and `0` genuinely occurs, EU `without_text`
is 0, so absent and measured are indistinguishable.

**Effect.** An older manifest and a measured-zero manifest read identically. Spec C2 still documents
the pre-#43 shape.

**Fix.** Make them `int?`, matching `ScopeExpectedWorks` which already follows that pattern, and
update spec C2.

**Gain.** Missing data stops impersonating measured data.

**Why not now.** Small and safe, but it is a schema-shaped change and belongs with items 6 and 7.

## 9. The spec's corrigendum shape is unrepresentable

**What is broken.** Spec 3.3 rule 3 requires a second same-language expression inside one version
directory. The writer's language-only merge silently drops it, `Single()` would throw on it, and
`CorpusIntegrity` flags the conforming shape as an error.

**Effect.** Latent today, because no adapter emits the shape. But spec and code contradict each other
on the system's core coordinate, and the first corrigendum to arrive would be dropped silently.

**Fix.** Either implement the shape or amend the spec to say corrigenda are not represented. Both are
defensible; the current state is not.

**Gain.** Removes a contradiction at the identity layer, which is the worst place to hold one.

**Why not now.** No adapter can trigger it, so it is genuinely latent.

## 10. Luxembourg alias catalogue

**What is broken.** `config/` holds only `eu-work-enrichment.json`. Luxembourg has no alias file.

**Effect, and a correction.** The verdict expected Luxembourg statutes to be unresolvable by name.
Measured on 2026-08-13, that is **not** what happens: a title-matching path exists, reports
`match: "work_identifier_or_title"`, and does surface the financial-sector law with a note telling
the assistant not to report it as missing. What remains unproven is whether the absence of aliases
degrades `work_constraints` scoping in practice.

**Fix, if warranted.** A `config/lu-work-enrichment.json` seeded with the statutes practitioners cite
by name, owner-reviewed per D75.

**Gain.** Unquantified, which is exactly the problem.

**Why not now.** Its premise did not survive measurement. It needs a measured failure before it earns
implementation.

## 11. Decompose `AskService`

**What is broken.** One service holds work-resolution guarding, planner client concerns (prompt,
schema, HTTP, repair loop), locale detection, admission, execution and reply assembly.

**Effect.** No user-visible defect. It raises the cost of every future change to the assistant and
makes the blast radius of each one larger than it should be.

**Fix.** Behaviour-neutral extraction along the seams the verdict named: `WorkResolutionGuard`, a
`PlannerClient`, locale detection out of the service. Golden-covered throughout.

**Gain.** A service whose responsibility is one sentence again.

**Why not now.** Zero user-visible change, and a large diff over the component with the most recent
behavioural fixes in it. The risk-to-benefit ratio is wrong while other things are still moving.

## 12. EU citations and relations

**What is broken.** The Formex profile models citation edges it never emits, and corpus relations
(`consolidates`, `amended_by`) exist as JSON that nothing reads or indexes.

**Effect.** `cited_by` works for Luxembourg but has no EU equivalent. Real amendment tracking exists
in the corpus and is invisible to every consumer.

**Fix.** Emit the edges from the profile, index relations into an edges table with validity time, and
extend `cited_by` or add a relations tool.

**Gain.** "What amended this EU regulation, and when" becomes answerable. It is the foundation for
amendment tracking generally.

**Why not now.** Medium to large, spanning pipeline, index and MCP, and it needs a re-derive plus a
rebuilt index.

## 13. Provision-level diff through MCP

**What is broken.** The web computes a rendered diff; the MCP `diff` tool returns metadata only.

**Effect.** The assistant can say *that* something changed between two dates but cannot say *what*
changed in wording, which is the question a lawyer actually asks.

**Fix.** Move the web's diff computation into a shared component and give the `diff` tool an optional
text mode.

**Gain.** "What changed in Article 26 between 2021 and 2024" becomes answerable with wording rather
than dates.

**Why not now.** Depends on provisions holding text on both sides, so items 3 and 4 come first for
Luxembourg.

## 14. Observability

**What is broken.** 17 live diagnostic codes are emitted as sanitized stderr lines with no request
correlation. Telemetry spans carry a closed tag set that cannot join them.

**Effect.** A production incident cannot be reconstructed from one request id. Everything found today
was found by querying the running system by hand, which is precisely the gap.

**Fix.** Route the diagnostics through a levelled logger correlated with `X-Lex-Request-Id`.

**Gain.** Incidents become reconstructable instead of reproducible-only-by-luck.

**Why not now.** Small, and a strong candidate to pull forward. It buys nothing a visitor can see, but
it is the item most likely to pay for itself the next time something breaks.

## 15. `Lex.Temporal` is dead

**What is broken.** 42 lines, zero production callers, still advertised by the spec as a publishable
package.

**Effect.** The spec describes a package the product does not use.

**Fix.** Fold it or delete it, and amend the spec.

**Gain.** One less claim to defend.

**Why not now.** Trivial, and will ride along with the next housekeeping change.

---

## What is deliberately not planned

Carried from the assistant verdict, unchanged, because each remains right:

- **No structural normalisation across extraction profiles.** `profiles_differ` is the product
  telling the truth about its inputs. An anchor-mapping layer would trade an honest refusal for a
  confidence claim the data cannot support.
- **No bi-temporal query surface.** The audit axis is stored and provable through `provenance`. "As
  we knew it on T" has no user until a litigation case names one.
- **No entity extraction.** It would be the first non-publisher-asserted content in the corpus and
  collides with the never-generate rule.
- **No replanning loop.** The measured failures were identity and data failures, not plan-shape
  failures. A frozen plan plus clarification is the right liability posture for a legal product.
- **No framework rewrite.** Typed operations, closed statuses, an evidence ledger and deterministic
  fallback are better than what a generic agent framework would replace them with.
