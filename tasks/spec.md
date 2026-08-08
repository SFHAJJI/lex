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
gh workflow run deploy.yml -f require_manifest=true --repo SFHAJJI/lex
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
