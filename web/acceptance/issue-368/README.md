# #368: pinned citation resolution, bounded repair

Governance: `37b6f5763c1b15778522e16296194efd27799cc2`.
Product base: `65bb24cc154581fc876b6ffa6c693fcff97a6f0c`.
Writer: Codex. Reviewer: Claude. This is implementation evidence, not issue or Stage acceptance.

## Integration follow-up

The preparation checkpoint was frozen at `1008d3bacb3e24d0519c10e06f5e56c1749b8081`, tree
`578ae67e98eaba3f77974f781c1d713f79c964be`, on the base above. Its PR464 required CI run
`34034464727` was personally queried complete with all three required jobs successful.
Claude then returned exact-head READY on #459; PR463 integrated its implementation/preparation
at `28271c22be2f63caf05dd0d5ca95cb1abd535114`. That accepted integration is merged as a later
commit here, with no conflicts and no change to this packet's executing web/eng source.

The review base is now `28271c22be2f63caf05dd0d5ca95cb1abd535114`. Codex personally compared
staged `src`, `tests`, `schemas` and `.github` to that base: no differences. Staged `web`/`eng`
were likewise unchanged from the preparation checkpoint before this evidence update.
From `web`, `npm test` on the combined source passed **707 tests**, zero failures/skips,
1966.6584ms (`integration-web-test.log`). The previously retained browser/mutation evidence
applies to the unchanged executing web source. New-head CI is a separate gate; the earlier
CI result is not substituted for it. #459's production/owner-operation limitations remain open.

## Acceptance reconciliation

| Obligation | Observed implementation and disposition |
| --- | --- |
| Never resolve current content under an old permalink hash (#368; B33-L0178) | **Implemented incorrectly, repaired.** The baseline parsed the pin but resolved any single identifiable candidate. A real `hash` mismatch now returns `pinned_state_unavailable`; the raw citation names the requested digest and the note says the lookup did not return that exact state. No state or alternative candidates are returned in this refusal. This chooses issue option 2 and invents no reason for a historical change. |
| Preserve the requested publisher, work and date | **Implemented incorrectly, repaired.** All permalink candidates must match the publisher/work parsed by the existing shared identity reader and the requested `valid_from`. A foreign coordinate throws, including beside an exact candidate. Impossible calendar dates are unrecognised. |
| Use the same digest the existing UI minted | **Retain existing contract; verify.** `reading.mjs` passes `state.hash` to `readingUrl`; `record_sha256` is a separate verification item and provision `text_sha256` is separate again. Tests reject both as substitutes for `hash`. B34's quote-level text-hash wording does not authorize changing this state-level URL's meaning. This repair does not establish that supplied state bytes recompute to their declared hash; the caller still owns lookup and byte integrity. |
| Refuse ambiguous answers and preserve publisher identity | **Retain baseline behavior; verified.** Exact hash filtering precedes candidate count. Two exact matches stay ambiguous. Non-permalink behavior, full ELI identifier collision guard, literal canonical-host grammar and the URL builder remain unchanged. The pre-change focused suite passed; all retained tests still pass. These mechanics do not mean the entire issue was previously accepted. |
| Show the requested pin and a readable refusal | **Missing, implemented.** Added a closed checker verdict and a synthetic preview case using existing escaped rendering and styles. Browser evidence covers this page. The checker verdict is not a newly published service refusal code and does not amend the service refusal registry. |
| Same publisher input produces the same work key across corpus rebuilds (S3-A04) | **Missing product acceptance; remains open on #368 / #344.** `identityOf` parses an existing key; it does not mint one. The source adapters' current corpus records and the synthetic preview builder are not a production canon/2 builder. No authoritative rebuilt V3 corpus/key map exists in this slice, so no real rebuild comparison is claimed. Stage3 explicitly requires two independent derivations with source-observation and transport-byte lineage. |
| V3-only corpus/6 and canon/2 (S3-A05) | **Retain accepted foundation; product builder remains missing.** No format, V2 reader, compatibility path or canonical serialization changed. This checker repair supplies no production corpus/6-to-canon/2 derivation. #344's complete-input dependency remains. |
| Alias rights, coordinates, totality, collisions and no chains (S3-A06) | **Missing production acceptance; remains open on #344 and its attached #368.** No alias is created here. A checker rejection is not a proof over a complete alias target graph. Rights-disposed, complete Stage2 inputs and Stage3 builders remain prerequisites; no alias publication or signing occurs. |

The repository's existing checker is a pure module called by its static preview and tests. There
is no production lookup/HTTP route in this slice. Root-relative or fragment-bearing citations
remain outside its existing parser grammar and return `unrecognised`; no new anchor or lookup
semantics are invented. The production permalink resolver must eventually consume the same
fail-closed rule against verified bytes. This packet cannot close #368 or Stage3.

## Personally executed evidence

Commands below ran in this worktree with Node `v22.18.0` / npm `10.9.3` on Windows. Logs are retained
beside this file. Text logs are LF-normalized and trailing whitespace is removed for Git;
no publisher response bytes are present.

1. `node --test web/test/citation-checker.test.mjs` before edits: **19 passed** (`baseline.log`).
2. Same command after adding six regression tests, before repair: **25 total, 19 passed, six
   failed** (`red.log`). The stale-pin fixture supplies an actual mismatching `hash`. This is
   stronger than the preliminary issue-claim probe, which supplied only record/text digests.
3. Same command after repair: **25 passed** (`green.log`).
4. From `web`: `npm ci --ignore-scripts`: seven packages added, eight audited, zero vulnerabilities
   reported by that run. No lockfile or dependency changes. This is the install report, not a
   general security assertion.
5. From `web`: `npm test`: **707 passed, zero failures/skips**. Repeated after restoring mutations:
   **707 passed**, 1839.2419ms (`web-test-final.log`). This is the full web suite. No .NET tests are
   claimed for this web-only diff.
6. From `web`: `npm run build`: **32 state pages generated** (`build.log`).
7. `node web/acceptance/issue-368/mutations.mjs` from repository root: eight independent guard
   bypasses each exit **1** with the relevant failing assertion, and source restored byte-for-byte.
   `mutations.log` plus each `mutation-*.log` records the failures. The script mutates only this
   worktree's checker temporarily and restores it in `finally`; run in a clean disposable checkout.
8. From `web`: `node scripts/browser-evidence.mjs`: **495 page/viewport/scheme combinations clean**
   (`browser-evidence.log`). The checker contributes 15 combinations: narrow, tablet, desktop,
   200% and 400% equivalent reflow viewports, each light/dark/forced colors. The harness does not
   set actual browser zoom. All report zero console events, no overflow,
   one h1 and one main, no glued text/inert controls/small targets. Its lowest measured contrast
   is **6.47**. These are synthetic pages, not production-law journeys.
9. `node web/acceptance/issue-368/tree-mutation.mjs`: broadened the new evidence path rule to
   admit every `web/acceptance` descendant. The rejected-neighbor assertion failed with exit
   **1** (`tree-mutation.log`), then the verifier was restored byte-for-byte. An earlier attempt
   hit a syntax error in the mutation script's generated PowerShell; that attempt is not counted
   as guard evidence. The script was corrected and rerun to the specific assertion above.

Separately, Codex opened the built `citation-checker.html` through the existing loopback-only
`serveDist` helper using Chrome DevTools in an isolated `lex-issue-368` context. The accessibility
snapshot contained the full requested digest, “Pinned state unavailable” heading and the exact
refusal note. Device emulation confirmed `innerWidth` **320, 768 and 1440**; the hash/refusal remained
present. The 320px card occupied x12..308, width296, within its 320px main. No console warnings or
errors were listed. A 320px resize request initially yielded an actual 502px viewport; it was
corrected with device emulation, not counted as 320px evidence. Screenshot/text output to this
sibling worktree was denied by the browser connector's root restriction; screenshots and DOM
results were inspected inline. The retained full browser harness log supplies the reproducible
matrix, and no saved screenshot artifact is claimed.

The mutation and focused tests prove the new decision branches can fail. Existing browser gates
were not changed. The tree verifier admits only this issue's evidence names/extensions, with
positive and rejected-neighbor cases; it retains every existing foundation check.
A fresh-context, read-only adversarial self-review found no material source defect and caught
the distinction between equivalent reflow viewports and actual browser zoom, corrected above;
its execution counts are not included here. It is not cross-family READY. Claude must review the
exact pushed head independently, and required CI is a separate gate.

No legal interpretation, live source probe, deployment, release, signature, production routing,
real rebuild determinism, alias acceptance or promotion is established by this packet.
