# Retrieval and agent experiment v2

This directory contains only the reproducible experiment contract, harness, fixtures, and reports.
It never reads production mounts implicitly and its implementation is never merged wholesale.

Inputs are copied under `C:\lex-retrieval-agent-evidence-v2`. The source release artifacts remain
untouched. `input-manifest.json` proves the copies are byte-identical to those release artifacts;
`scenarios.json` fixes the legal-search and assistant cases before implementation.

Experiment commands will require explicit `--index-dir`, `--model-dir`, and `--output` arguments.
No command signs, publishes, or deploys an artifact.

