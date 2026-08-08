# Retrieval and agent experiment v2

This directory contains only the reproducible experiment contract, harness, fixtures, and reports.
It never reads production mounts implicitly and its implementation is never merged wholesale.

Inputs are copied under `C:\lex-retrieval-agent-evidence-v2`. The source release artifacts remain
untouched. `input-manifest.json` proves the copies are byte-identical to those release artifacts;
`scenarios.json` fixes the legal-search and assistant cases before implementation.

Experiment commands require explicit `--index-dir`, `--model-dir`, and `--output` arguments. No
command signs, publishes, or deploys an artifact.

```powershell
dotnet run --project experiments/retrieval-agent-v2/Lex.RetrievalExperiment -- baseline `
  --index-dir C:\lex-retrieval-agent-evidence-v2\baseline `
  --model-dir C:\lex-retrieval-agent-evidence-v2\model `
  --scenarios experiments/retrieval-agent-v2/scenarios.json `
  --output C:\lex-retrieval-agent-evidence-v2\reports\baseline.json
```
