// Written by scripts/write-registry-record.mjs from the MLflow registry export of the evaluation harness (MLflow's own
// client: the registered model lex-assistant, its aliases, every version and its run). Do not edit: run the script again.

export const REGISTRY_RECORD = Object.freeze({
  "mlflow_version": "3.18.0",
  "model": "lex-assistant",
  "aliases": {
    "champion": 5
  },
  "versions": [
    {
      "version": 1,
      "created": "2026-11-11T22:45:35Z",
      "source": "models:/m-0deed93be6c34f39a6b65d9cf5059937",
      "run_id": "5bfc10ad6fee4aceb193510d2cfe5d86",
      "run_name": "d8d38105-d1d0a60f",
      "tags": {
        "serving_commit": "d8d38105f51d8be4369b19ff4083de358120f722",
        "planner_id": "azure-openai/gpt-4.1-mini/prompt-41a8df46561a",
        "snapshot_id": "corpus-d12f554e7ad954a6",
        "gates": "failed"
      },
      "aliases": [],
      "params": {
        "dataset_sha256": "d1d0a60f9f44fb11744048b7d0e1fafa3839bc4592dab49b81005c67c380b16f",
        "base_url": "https://law.soufien.lu"
      },
      "metrics": {
        "cases": 15,
        "verdict_accuracy": 0.4,
        "citation_accuracy": 0.4444444444444444,
        "quote_verified_rate": 1,
        "latency_p50_s": 1.9414098000270315,
        "latency_p95_s": 2.3065359000465833
      },
      "artifacts": [
        "results.jsonl",
        "summary.md"
      ]
    },
    {
      "version": 2,
      "created": "2026-11-11T22:48:46Z",
      "source": "models:/m-b90308940e8c4b6a8c81075adaad461b",
      "run_id": "c6e5a92721194a7e8115bc4dc841f805",
      "run_name": "d8d38105-3c614439",
      "tags": {
        "serving_commit": "d8d38105f51d8be4369b19ff4083de358120f722",
        "planner_id": "azure-openai/gpt-4.1-mini/prompt-41a8df46561a",
        "snapshot_id": "corpus-d12f554e7ad954a6",
        "gates": "failed"
      },
      "aliases": [],
      "params": {
        "dataset_sha256": "3c614439674740e400f865ea499038aaf45f1f31471f280f20f6de45ca63e160",
        "base_url": "https://law.soufien.lu"
      },
      "metrics": {
        "cases": 15,
        "verdict_accuracy": 0.6,
        "citation_accuracy": 0.4444444444444444,
        "quote_verified_rate": 1,
        "replay_same_verdict_rate": 1,
        "replay_same_plan_rate": 0.6666666666666666,
        "latency_p50_s": 1.923296399996616,
        "latency_p95_s": 2.280042099999264
      },
      "artifacts": [
        "results.jsonl",
        "summary.md"
      ]
    },
    {
      "version": 3,
      "created": "2026-11-12T22:10:26Z",
      "source": "models:/m-69688a49e100420984c7d6ac1b6de95b",
      "run_id": "9958ec8538664d4c9f22ae4f8a581927",
      "run_name": "8a5b5be5-3c614439",
      "tags": {
        "serving_commit": "8a5b5be569c2af3a3adda6628ea8c27f28643c3d",
        "planner_id": "azure-openai/gpt-4.1-mini/prompt-41a8df46561a",
        "snapshot_id": "corpus-79d53242dce4520f",
        "gates": "passed"
      },
      "aliases": [],
      "params": {
        "dataset_sha256": "3c614439674740e400f865ea499038aaf45f1f31471f280f20f6de45ca63e160",
        "base_url": "https://law.soufien.lu"
      },
      "metrics": {
        "cases": 15,
        "verdict_accuracy": 0.9333333333333333,
        "citation_accuracy": 0.8888888888888888,
        "quote_verified_rate": 1,
        "replay_same_verdict_rate": 1,
        "replay_same_plan_rate": 0.75,
        "latency_p50_s": 2.084039399996982,
        "latency_p95_s": 2.4840261999925133
      },
      "artifacts": [
        "results.jsonl",
        "summary.md"
      ]
    },
    {
      "version": 4,
      "created": "2026-11-13T15:24:44Z",
      "source": "models:/m-5d858e9bf5264b418eea89c8d2214f1b",
      "run_id": "8796a7bc33a040f5b284e11cec0ca7d7",
      "run_name": "c0191d7a-0f555952",
      "tags": {
        "serving_commit": "c0191d7abdfbb3fbd7c238af4b474402a28f8335",
        "planner_id": "azure-openai/gpt-4.1-mini/prompt-41a8df46561a",
        "snapshot_id": "corpus-b2c3c1ba55bdda9a",
        "gates": "passed"
      },
      "aliases": [],
      "params": {
        "dataset_sha256": "0f55595228b3719ad1b7aa4cddf76a027d71632891cf9c3dcafa7dbbf96b4750",
        "base_url": "https://law.soufien.lu"
      },
      "metrics": {
        "cases": 16,
        "verdict_accuracy": 0.875,
        "citation_accuracy": 0.8888888888888888,
        "quote_verified_rate": 1,
        "replay_same_verdict_rate": 1,
        "replay_same_plan_rate": 0.6153846153846154,
        "latency_p50_s": 2.0020569500193233,
        "latency_p95_s": 2.980263300007209
      },
      "artifacts": [
        "results.jsonl",
        "summary.md"
      ]
    },
    {
      "version": 5,
      "created": "2026-11-13T21:14:38Z",
      "source": "models:/m-f3a0edf58cd24630aec18e08cb46c7d3",
      "run_id": "c0be71dbd1ab42d3bbe7097b091e332c",
      "run_name": "17e2ab50-0f555952",
      "tags": {
        "serving_commit": "17e2ab50263e260b7ccbf0ed775ea5e6e5da313d",
        "planner_id": "azure-openai/gpt-4.1-mini/prompt-691b1719c0ab",
        "snapshot_id": "corpus-b2c3c1ba55bdda9a",
        "gates": "passed"
      },
      "aliases": [
        "champion"
      ],
      "params": {
        "dataset_sha256": "0f55595228b3719ad1b7aa4cddf76a027d71632891cf9c3dcafa7dbbf96b4750",
        "base_url": "https://law.soufien.lu"
      },
      "metrics": {
        "cases": 16,
        "verdict_accuracy": 0.9375,
        "citation_accuracy": 0.8888888888888888,
        "quote_verified_rate": 1,
        "replay_same_verdict_rate": 1,
        "replay_same_plan_rate": 0.6153846153846154,
        "latency_p50_s": 1.8838745999964885,
        "latency_p95_s": 2.2986192000098526
      },
      "artifacts": [
        "results.jsonl",
        "summary.md"
      ]
    }
  ],
  "experiment": "lex-assistant",
  "runs_logged": 21,
  "exported": "2026-11-15T08:45:32Z"
});
