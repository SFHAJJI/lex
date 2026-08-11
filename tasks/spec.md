# Spec: Publisher-first work discovery and Retrieval Agent v2

## Objective

Let a legal professional find a law by its professional name and let a user discover candidate
laws from a factual description without allowing publisher discovery metadata, LLM enrichment, or
planner output to become legal authority. Normal search remains deterministic and low latency.

## Tech stack

- .NET 10 and C#
- SQLite FTS5 and the existing `intfloat/multilingual-e5-small` ONNX encoder
- React 18 and TypeScript 5 for the primary workspace
- Existing ECDSA P-256 signed index and whole-artifact manifest pipeline
- Official EUR-Lex/Cellar, EuroVoc, and Legilux sources only

## Commands

```powershell
dotnet build -c Release --nologo
dotnet test -c Release --nologo --logger "console;verbosity=normal"
Push-Location web; npm ci --no-audit --no-fund; npm run build; Pop-Location
dotnet run --project src/Lex.Ingest -- index --help
gh workflow run deploy.yml -f promote=false --repo SFHAJJI/lex
```

Corpus and production artifact commands must be taken from the current corpus and `lex-ops`
workflows after their exact revisions and inputs are verified. Production signing uses GitHub OIDC
and Azure Key Vault. A private key is never written or committed by this work.

## Project structure

- `src/Lex.Sources.EurLex`: official publisher metadata ingestion
- `src/Lex.Index`: work/provision indexing, resolution, and retrieval
- `src/Lex.Ingest`: enrichment contract and index build composition
- `src/Lex.Mcp`: shared public search contract used by MCP and the workspace
- `src/Lex.Web`, `web/src`: no-JavaScript and primary browser surfaces
- `src/Lex.Ask`: current assistant and Retrieval Agent v2 integration boundary
- `tests/Lex.Tests`, `web/src/*.test.ts`: behavioral and architecture tests
- `docs`: architecture decisions, migration, benchmark, and release documentation

## Code style

Follow existing records, parameterized SQLite commands, ordinal validation, and explicit refusal
statuses. Keep work discovery separate from authoritative legal text.

```csharp
public enum WorkResolutionStatus { Resolved, Ambiguous, Unresolved, Unavailable }

public sealed record WorkResolution(
    WorkResolutionStatus Status,
    IReadOnlyList<WorkCandidate> Candidates,
    string? Reason);
```

The final implementation may use an equivalent smaller existing type if one already fits.

## Testing strategy

- Write a failing regression test before each behavioral fix.
- Focused tests cover query decomposition, alias/title collisions, role intent, artifact schema,
  publisher metadata validation, and resolver ownership.
- Integration tests prove SPA/MCP/no-JavaScript parity through the shared retrieval contract.
- Frozen FR/EN evaluation sets cover EU and Luxembourg exact names, descriptions, ambiguity,
  comparison, corrigenda, negatives, and corpus gaps.
- Full solution and web builds run before each release checkpoint.
- Candidate and live production smoke tests verify health, coverage, exact names, descriptive
  discovery, temporal reads, artifact identity, and rollback readiness.

## Boundaries

### Always

- Preserve publisher text and protected metadata byte identity.
- Record source, language, snapshot, and trust class for discovery metadata.
- Resolve raw user names before planner-generated candidates.
- Keep normal search free of runtime LLM calls.
- Use scoped `git add` and DCO-signed commits.

### Ask first

- Adding a third-party dependency or publisher source.
- Weakening an existing safety or regression gate.
- Changing the production host or signing authority.

### Never

- Put law names in application code.
- Treat a comma-packed publisher short-title value as an exact identifier without approval.
- Let generated concepts or planner text cite, filter as legal truth, or exact-resolve a work.
- Manufacture around a missing work or failed ingest.
- publish unsigned or partially verified indexes.

## Success criteria

1. FR/EN reviewed professional names resolve deterministically with ambiguity surfaced.
2. Descriptive discovery improves on a frozen holdout without an unaffected-query regression.
3. SPA, no-JavaScript, MCP, and assistant use consistent search defaults and match reasons.
4. Publisher short titles, EuroVoc/directory labels, and document roles are retained with
   provenance; the `CommonNames` code table and its derived corpus contamination are removed.
5. LLM fields are optional, bounded, reversible, and unable to become exact identity or evidence.
6. Coverage distinguishes no match, unsupported scope, known ingest gap, stale source, and
   runtime unavailability as far as the signed build inventory can prove.
7. Signed EU/LU artifacts pass correctness, hash, mapping, size, memory, cold-start, and latency
   gates and are promoted through a zero-traffic production candidate.
8. Production health, critical search flows, artifact identity, logs, and rollback revision are
   verified after promotion.

## Open questions resolved during implementation

- Exact field names in Cellar are verified against the live CDM endpoint and pinned in tests.
- Numeric retrieval and latency gates are frozen from corrected baselines before tuning.
- The existing deployment workflows, not this spec, determine the current signing and promotion
  command line.

---

# Spec: Persistent legal-research assistant shell

## Objective

Make the existing grounded assistant available from every legal-research surface without hiding
the law or turning the assistant into the primary navigation. First-time visitors see a fixed,
closed launcher. Opening it docks the panel beside desktop content and uses an accessible modal
sheet on narrower screens. The same assistant conversation contract, typed `UiEffect` values and
session history are reused everywhere; no second chat implementation or free-form DOM control is
introduced.

Research surfaces are `/`, `/browse`, `/find`, `/search`, `/changed`, `/in-force-on`, `/stories`
and mounted publisher document/version/diff routes. Engineering and portfolio pages such as
`/about`, `/architecture`, `/decisions`, `/verify`, `/developers`, `/ai`, `/built` and
`/how-it-works` do not load the assistant.

Conversation continuity is explicit rather than implied: the panel renders the bounded tab-scoped
transcript, exposes a new-conversation control, and sends it with each stateless request. The
backend may restore typed work authority only by deterministically resolving earlier user-authored
text. Earlier assistant prose and weak discovery never become authority or legal evidence.

## Tech stack and commands

- React 18 + TypeScript, mounted into the ASP.NET Core server-rendered shell.
- Native CSS/media queries, `sessionStorage`, `localStorage` and focus management; no dependency.
- Web test: `npm test --prefix web`
- Web production build: `npm run build --prefix web`
- .NET tests: `dotnet test tests/Lex.Tests/Lex.Tests.csproj --configuration Release`

## Project structure

- `web/src/AskPanel.tsx`: one visual panel for workspace and server-rendered research pages.
- `web/src/AssistantController.tsx`: shared conversation/session state and streaming contract.
- `web/src/assistantShell.ts`: persisted panel state, starter prompts and typed-result workspace links.
- `web/src/main.tsx`: mounts the workspace or the standalone assistant root.
- `web/src/styles.css`: fixed launcher, desktop dock and mobile modal sheet.
- `src/Lex.Web/PageShell.cs`: emits the standalone mount only on research routes.
- `web/src/*.test.ts`, `web/smoke.mjs`, `tests/Lex.Tests`: behavior and integration contracts.

## Code style

Use small pure helpers for policies and keep the model away from browser authority:

```ts
export function assistantWorkspaceUrl(ui?: UiEffect): string | undefined {
  if (ui?.diff?.subject.work)
    return workspaceUrl({ work: ui.diff.subject.work, date: ui.diff.from_date,
      to: ui.diff.to_date, mode: "compare", space: "law" });
  return undefined;
}
```

The helper maps already-validated typed effects to known workspace state. It never executes a URL,
selector or instruction supplied in model prose.

## Testing strategy

- Pure unit tests pin first-run default state, session restoration, starter prompts and typed-result
  URL mapping.
- Component/static tests pin a fixed launcher, desktop reflow, mobile backdrop/modal semantics,
  Escape/close behavior and reduced-motion support.
- Bundle smoke mounts both `#workspace` and `#assistant-root` and proves only one assistant mounts.
- ASP.NET tests prove research pages emit the assistant assets while engineering pages do not.
- Chrome DevTools verifies desktop and mobile layout, keyboard focus, console/network health and
  that legal content is not obscured.

## Boundaries

### Always

- Default closed for a first visit and remember open/minimised state only for the current tab.
- Keep a visible close action, restore focus to the launcher and trap focus only in modal mode.
- Reuse `/api/ask/stream`, bounded history, clarification validation and typed `UiEffect` values.
- Show the existing AI/not-legal-advice notice before any answer.

### Ask first

- Adding a browser dependency, changing the assistant API or changing a legal retrieval contract.
- Enabling the assistant on engineering/portfolio pages.

### Never

- Open the assistant by default for a first-time visitor.
- Cover legal content on desktop or allow model prose to manipulate the DOM.
- Duplicate the assistant protocol or silently submit a starter prompt.

## Success criteria

1. A fixed `Ask Lex` launcher appears on every agreed research route and nowhere else.
2. First visit is closed; opening/minimising/closing survives navigation in the same tab.
3. Desktop content reflows beside the open panel. At narrower widths the panel is a modal sheet
   with backdrop, focus containment, Escape and an explicit close button.
4. The four approved examples cover point-in-time reading, comparison, article history and
   corpus-wide change ranking; typed result/follow-up actions open the correct workspace state.
5. Existing assistant safeguards, history bounds, clarification behavior and all current web/.NET
   tests remain green, followed by live production verification.
6. Corpus-wide tools answer from their aggregate evidence without requiring a single-work
   confirmation. Parallel publisher results merge into one typed view whose complete scope is
   applied to the workspace URL; the internal raw-resolution preflight is not narrated as a find.

## Open questions

None. The user approved the route policy, closed-by-default behavior, responsive model, prompt set
and typed-effect boundary before implementation.
