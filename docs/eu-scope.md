# EU temporal corpus scope

The canonical reviewed selector is
[`src/Lex.Sources.EurLex/eu-scope.json`](../src/Lex.Sources.EurLex/eu-scope.json).
It replaces application code as the place where an existing EUR-Lex document class enters scope.

The approved wave is the ingestion boundary. A domain above that wave is visible for review but is
not downloaded. Each enabled domain can seed exact CELEX works and select binding regulations,
directives and decisions through EUR-Lex directory prefixes or EuroVoc concepts. Selection then
closes only the named legal relationships, to a reviewed depth and within explicit per-seed and
total-work limits. Citations never expand the graph.

Selection is not temporal filtering. Once a work is selected, the adapter asks Cellar for French
and English and retains every official consolidated expression available. A work without a
consolidation remains present through its original official expression. Its metadata says that the
merged wording is not published or not required; Lex does not manufacture a merge.

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

The scope file is copied into every EU index release and covered by the signed artifact manifest.
This makes the selection rules independently identifiable beside the index they produced.
