# Retrieval and agent experiment v2

This directory contains only the reproducible experiment contract, harness, fixtures, and reports.
It never reads production mounts implicitly and its implementation is never merged wholesale.

Inputs are copied under `C:\lex-retrieval-agent-evidence-v2`. The source release artifacts remain
untouched. `input-manifest.json` proves the copies are byte-identical to those release artifacts;
`scenarios.json` fixes the legal-search and assistant cases before implementation.

Experiment commands require explicit `--index-dir`, `--model-dir`, and `--output` arguments. No
command signs, publishes, or deploys an artifact.

```powershell
dotnet run --project experiments/retrieval-agent-v2/Lex.RetrievalExperiment -- workbench-init `
  --index-dir C:\lex-retrieval-agent-evidence-v2\baseline `
  --output C:\lex-retrieval-agent-evidence-v2\workbench\candidate.db

dotnet run --project experiments/retrieval-agent-v2/Lex.RetrievalExperiment -- enrichment-sample `
  --workbench C:\lex-retrieval-agent-evidence-v2\workbench\candidate.db `
  --endpoint https://oai-soufien-dev.openai.azure.com `
  --deployment gpt-5-mini `
  --works experiments/retrieval-agent-v2/fixture-works.json `
  --runs 2 `
  --output C:\lex-retrieval-agent-evidence-v2\reports\enrichment-sample.json

dotnet run --project experiments/retrieval-agent-v2/Lex.RetrievalExperiment -- enrichment-analyze `
  --report C:\lex-retrieval-agent-evidence-v2\reports\enrichment-sample.json `
  --model-dir C:\lex-retrieval-agent-evidence-v2\model `
  --output C:\lex-retrieval-agent-evidence-v2\reports\enrichment-semantic-analysis.json

dotnet run --project experiments/retrieval-agent-v2/Lex.RetrievalExperiment -- work-search-build `
  --workbench C:\lex-retrieval-agent-evidence-v2\workbench\candidate-a.db `
  --analysis C:\lex-retrieval-agent-evidence-v2\reports\enrichment-semantic-analysis-v1.json `
  --aliases experiments/retrieval-agent-v2/reviewed-aliases.json `
  --threshold 0.9 `
  --output C:\lex-retrieval-agent-evidence-v2\workbench\work-search-v1.db `
  --report C:\lex-retrieval-agent-evidence-v2\reports\work-search-build-v1.json

dotnet run --project experiments/retrieval-agent-v2/Lex.RetrievalExperiment -- work-search-eval `
  --index C:\lex-retrieval-agent-evidence-v2\workbench\work-search-v1.db `
  --scenarios experiments/retrieval-agent-v2/scenarios.json `
  --output C:\lex-retrieval-agent-evidence-v2\reports\work-search-eval-v1.json

dotnet run --project experiments/retrieval-agent-v2/Lex.RetrievalExperiment -- work-vector-build `
  --index C:\lex-retrieval-agent-evidence-v2\workbench\work-search-v1.db `
  --model-dir C:\lex-retrieval-agent-evidence-v2\model `
  --output C:\lex-retrieval-agent-evidence-v2\workbench\work-search-v1.vectors `
  --report C:\lex-retrieval-agent-evidence-v2\reports\work-vector-build-v1.json

dotnet run --project experiments/retrieval-agent-v2/Lex.RetrievalExperiment -- work-hybrid-eval `
  --index C:\lex-retrieval-agent-evidence-v2\workbench\work-search-v1.db `
  --vectors C:\lex-retrieval-agent-evidence-v2\workbench\work-search-v1.vectors `
  --model-dir C:\lex-retrieval-agent-evidence-v2\model `
  --scenarios experiments/retrieval-agent-v2/scenarios.json `
  --output C:\lex-retrieval-agent-evidence-v2\reports\work-hybrid-eval-v1.json

dotnet run --project experiments/retrieval-agent-v2/Lex.RetrievalExperiment -- baseline `
  --index-dir C:\lex-retrieval-agent-evidence-v2\baseline `
  --model-dir C:\lex-retrieval-agent-evidence-v2\model `
  --scenarios experiments/retrieval-agent-v2/scenarios.json `
  --output C:\lex-retrieval-agent-evidence-v2\reports\baseline.json

dotnet run --project experiments/retrieval-agent-v2/Lex.RetrievalExperiment -- agent-typed-plan `
  --endpoint https://oai-soufien-dev.openai.azure.com `
  --deployment gpt-5-mini `
  --scenarios experiments/retrieval-agent-v2/scenarios.json `
  --clarifications experiments/retrieval-agent-v2/clarification-dimensions.json `
  --runs 3 `
  --output C:\lex-retrieval-agent-evidence-v2\reports\typed-plan.json

dotnet run --project experiments/retrieval-agent-v2/Lex.RetrievalExperiment -- agent-typed-exec `
  --plans C:\lex-retrieval-agent-evidence-v2\reports\typed-plan.json `
  --scenarios experiments/retrieval-agent-v2/scenarios.json `
  --work-index C:\lex-retrieval-agent-evidence-v2\workbench\work-search-v4.db `
  --work-vectors C:\lex-retrieval-agent-evidence-v2\workbench\work-search-v2.vectors `
  --index-dir C:\lex-retrieval-agent-evidence-v2\baseline `
  --model-dir C:\lex-retrieval-agent-evidence-v2\model `
  --today 2026-08-08 `
  --output C:\lex-retrieval-agent-evidence-v2\reports\typed-plan-exec.json

dotnet run --project experiments/retrieval-agent-v2/Lex.RetrievalExperiment -- agent-direct `
  --endpoint https://oai-soufien-dev.openai.azure.com `
  --deployment gpt-5-mini `
  --scenarios experiments/retrieval-agent-v2/scenarios.json `
  --clarifications experiments/retrieval-agent-v2/clarification-dimensions.json `
  --work-index C:\lex-retrieval-agent-evidence-v2\workbench\work-search-v4.db `
  --work-vectors C:\lex-retrieval-agent-evidence-v2\workbench\work-search-v2.vectors `
  --index-dir C:\lex-retrieval-agent-evidence-v2\baseline `
  --model-dir C:\lex-retrieval-agent-evidence-v2\model `
  --today 2026-08-08 `
  --runs 3 `
  --output C:\lex-retrieval-agent-evidence-v2\reports\direct-agent.json

dotnet run --project experiments/retrieval-agent-v2/Lex.RetrievalExperiment -- agent-grounding `
  --endpoint https://oai-soufien-dev.openai.azure.com `
  --deployment gpt-5-mini `
  --executions C:\lex-retrieval-agent-evidence-v2\reports\typed-plan-exec.json `
  --output C:\lex-retrieval-agent-evidence-v2\reports\grounded-answer.json
```

The direct variant is retained only as rejected experimental evidence. It must not be copied into
production. See `agent-experiment-summary.md`.
