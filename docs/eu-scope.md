# EU temporal corpus scope

The canonical engineering acquisition selector is
[`src/Lex.Sources.EurLex/eu-scope.json`](../src/Lex.Sources.EurLex/eu-scope.json).
It replaces application code as the place where an existing EUR-Lex document class enters scope.
Its group names and selection reasons are not legal classifications and never enter corpus version
metadata, search fields, facets, MCP output, or assistant evidence.

The approved wave is the ingestion boundary. A domain above that wave is visible for review but is
not downloaded. Each enabled domain can seed exact CELEX works and select binding regulations,
directives and decisions through EUR-Lex directory prefixes or EuroVoc concepts. Selection then
closes only the named legal relationships, to a reviewed depth and within explicit per-seed and
total-work limits. Citations never expand the graph.

Selection is not temporal filtering. Once a work is selected, the adapter asks Cellar for French
and English and retains every official consolidated expression available. A work without a
consolidation remains present through its original official expression. Its metadata says that the
merged wording is not published or not required; Lex does not manufacture a merge.

Cellar's negotiated XHTML is the primary body source. If a language-specific expression is
advertised but Cellar returns no body, ingestion retries the expression's official EUR-Lex URL;
the fallback is restricted to HTTPS EU institutional hosts. Searchable XHTML has a separate
32 MiB offline-ingest limit so annex-heavy acts are retained. Optional Formex archives have their
own compressed, per-member and expanded-data limits; skipping an oversized Formex archive does
not remove an already available XHTML expression. Metadata-only expressions remain explicit and
are retried on later runs rather than silently treated as complete.

Run a read-only preview before changing `approved_wave`:

```text
dotnet run --project src/Lex.Ingest -c Release -- scope-preview \
  --scope src/Lex.Sources.EurLex/eu-scope.json \
  --previous-scope path/to/previous-approved-scope.json
```

The JSON report gives added and removed works, original and consolidated expression counts,
language counts, relationship reasons, missing consolidations, metadata gaps and size estimates.
The estimate states its formula until a corpus dry run supplies measured bytes.

Adding a new subject that uses existing EUR-Lex legislation classes is a configuration review,
preview, approval-wave change and backfill. A new publisher or a class with different temporal
semantics, such as CJEU judgments, requires adapter and schema implementation plus new fixtures.

The corpus manifest records the SHA-256 of the raw LF-pinned scope file and the ingester code
commit that contains it; the signed index stamp carries that source-configuration identity. This
makes the selection rules independently identifiable without copying their labels into legal data.

Wave 2 was approved after the engineer-reviewed preview on 2026-08-06. The refreshed preview on
2026-08-11 selects 1,248 works, all with loadable official metadata, and 4,728 French and English
expressions. Relationship closure is language-aware: 385 related corrigenda observed in that
preview existed only in other official EU languages, so they are outside this bilingual corpus
rather than failed EN/FR acquisitions. The planning estimate is 1,239,416,832 download bytes and
464,781,312 lexical index bytes. These estimates are replaced by measured artifact sizes after the
first completed build.

The wave uses reviewed cornerstone CELEX seeds in each engineering acquisition group instead of top-level
directory codes. Relationship closure supplies their bounded legal context. Reverse legal-basis
closure admits regulations and directives, but excludes case-specific decisions that would turn a
legislative corpus into an unbounded administrative case collection. Complete primary-law seeds
are not reverse legal-basis roots: Lex includes the treaty or Charter and follows its own outbound
legal bases, but does not ingest every secondary act that cites the TFEU, TEU or Charter.
