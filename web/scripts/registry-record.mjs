// Written by scripts/write-registry-record.mjs from the MLflow registry export of the evaluation harness (MLflow's own
// client: the registered model lex-assistant, its aliases, every version and its run). Do not edit: run the script again.

export const REGISTRY_RECORD = Object.freeze({
  "mlflow_version": "3.18.0",
  "model": "lex-assistant",
  "aliases": {
    "champion": 12,
    "serving": 12
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
      "aliases": [],
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
    },
    {
      "version": 6,
      "created": "2026-11-15T08:54:59Z",
      "source": "models:/m-0994a040f1c84f49b09f79234a71ffc2",
      "run_id": "b76733dd7cac414f9d7f1aba77ed9b54",
      "run_name": "b635bdb8-278b2dce",
      "tags": {
        "serving_commit": "b635bdb8c495680c34c3e79f6f7eb1bc99bc2ac8",
        "planner_id": "azure-openai/gpt-4.1-mini/prompt-a625e51e3b58",
        "snapshot_id": "corpus-b2c3c1ba55bdda9a",
        "gates": "failed"
      },
      "aliases": [],
      "params": {
        "dataset": "lex-assistant-v2.jsonl",
        "dataset_sha256": "278b2dcee9ffff45181fcfd6c4fef39b1fc3852afc2c7a7fb67d06b4a2e8efcc",
        "base_url": "https://law.soufien.lu"
      },
      "metrics": {
        "cases": 56,
        "verdict_accuracy": 0.44642857142857145,
        "citation_accuracy": 0.3404255319148936,
        "quote_verified_rate": 1,
        "replay_same_verdict_rate": 0.9642857142857143,
        "replay_same_plan_rate": 0.7272727272727273,
        "latency_p50_s": 1.5171085000038147,
        "latency_p95_s": 2.7017917999764904,
        "outcome_right_article": 16,
        "outcome_wrong_article": 0,
        "outcome_not_found": 26,
        "outcome_wrongly_refused": 5,
        "outcome_right_behaviour": 9,
        "cost_eur_per_question_estimate": 0.0010924159090909091
      },
      "artifacts": [
        "results.jsonl",
        "summary.md",
        "traces.jsonl"
      ]
    },
    {
      "version": 7,
      "created": "2026-11-15T09:09:24Z",
      "source": "models:/m-1eb9cbad060c457195cf4fda4f83e24e",
      "run_id": "f09d6f549e804f60a5bc8889686399d1",
      "run_name": "b635bdb8-278b2dce",
      "tags": {
        "serving_commit": "b635bdb8c495680c34c3e79f6f7eb1bc99bc2ac8",
        "planner_id": "azure-openai/gpt-4.1-mini/prompt-a625e51e3b58",
        "snapshot_id": "corpus-b2c3c1ba55bdda9a",
        "gates": "passed"
      },
      "aliases": [],
      "params": {
        "dataset": "lex-assistant-v2.jsonl",
        "dataset_sha256": "278b2dcee9ffff45181fcfd6c4fef39b1fc3852afc2c7a7fb67d06b4a2e8efcc",
        "base_url": "https://law.soufien.lu"
      },
      "metrics": {
        "cases": 56,
        "verdict_accuracy": 0.4642857142857143,
        "citation_accuracy": 0.3617021276595745,
        "quote_verified_rate": 1,
        "replay_same_verdict_rate": 0.9464285714285714,
        "replay_same_plan_rate": 0.6666666666666666,
        "latency_p50_s": 1.4430200000060722,
        "latency_p95_s": 3.2357646000455134,
        "outcome_right_article": 17,
        "outcome_wrong_article": 0,
        "outcome_not_found": 25,
        "outcome_wrongly_refused": 5,
        "outcome_right_behaviour": 9,
        "cost_eur_per_question_estimate": 0.0010916644444444444
      },
      "artifacts": [
        "results.jsonl",
        "summary.md",
        "traces.jsonl"
      ]
    },
    {
      "version": 8,
      "created": "2026-11-15T11:50:27Z",
      "source": "models:/m-a99e629c62cc47939cdf5ac1f83446f5",
      "run_id": "e7505b06cc724db9aaa5b9730b3699f9",
      "run_name": "38c1a47b-278b2dce",
      "tags": {
        "serving_commit": "38c1a47bac9af8ff055568523eb228b0b9f7066a",
        "planner_id": "azure-openai/gpt-4.1-mini/prompt-a625e51e3b58",
        "snapshot_id": "corpus-b2c3c1ba55bdda9a",
        "gates": "passed"
      },
      "aliases": [],
      "params": {
        "dataset": "lex-assistant-v2.jsonl",
        "dataset_sha256": "278b2dcee9ffff45181fcfd6c4fef39b1fc3852afc2c7a7fb67d06b4a2e8efcc",
        "base_url": "https://ca-lex-v3--v3-20261115t092447z-38c1a47bac9a-d5f4861f.whitemushroom-ff26ef2a.francecentral.azurecontainerapps.io"
      },
      "metrics": {
        "cases": 56,
        "verdict_accuracy": 0.4642857142857143,
        "citation_accuracy": 0.3617021276595745,
        "quote_verified_rate": 1,
        "replay_same_verdict_rate": 0.9642857142857143,
        "replay_same_plan_rate": 0.7111111111111111,
        "latency_p50_s": 1.4991818999696989,
        "latency_p95_s": 1.8608310000272468,
        "outcome_right_article": 17,
        "outcome_wrong_article": 0,
        "outcome_not_found": 25,
        "outcome_wrongly_refused": 5,
        "outcome_right_behaviour": 9,
        "cost_eur_per_question_estimate": 0.0010963533333333334
      },
      "artifacts": [
        "results.jsonl",
        "summary.md",
        "traces.jsonl"
      ]
    },
    {
      "version": 9,
      "created": "2026-11-16T06:13:18Z",
      "source": "models:/m-77a942c291ce4bd2b7af0075e5de9a27",
      "run_id": "a2d9ee9f4f254198aace909bb72044a9",
      "run_name": "b117b3fb-278b2dce",
      "tags": {
        "serving_commit": "b117b3fba166f5e84b8ac30f7e025de06b778ef6",
        "planner_id": "azure-openai/gpt-4.1-mini/prompt-a625e51e3b58",
        "snapshot_id": "corpus-b2c3c1ba55bdda9a",
        "gates": "failed"
      },
      "aliases": [],
      "params": {
        "dataset": "lex-assistant-v2.jsonl",
        "dataset_sha256": "278b2dcee9ffff45181fcfd6c4fef39b1fc3852afc2c7a7fb67d06b4a2e8efcc",
        "base_url": "https://ca-lex-v3--v3-20261115t183640z-b117b3fba166-19f24051.whitemushroom-ff26ef2a.francecentral.azurecontainerapps.io"
      },
      "metrics": {
        "cases": 56,
        "verdict_accuracy": 0.4642857142857143,
        "citation_accuracy": 0.3191489361702128,
        "quote_verified_rate": 1,
        "replay_same_verdict_rate": 0.9642857142857143,
        "replay_same_plan_rate": 0.6136363636363636,
        "latency_p50_s": 1.7382933999761008,
        "latency_p95_s": 6.252351400034968,
        "outcome_right_article": 15,
        "outcome_wrong_article": 1,
        "outcome_not_found": 26,
        "outcome_wrongly_refused": 5,
        "outcome_right_behaviour": 9,
        "cost_eur_per_question_estimate": 0.0010942113636363637
      },
      "artifacts": [
        "results.jsonl",
        "summary.md",
        "traces.jsonl"
      ]
    },
    {
      "version": 10,
      "created": "2026-11-16T07:16:13Z",
      "source": "models:/m-c58aae9e6a704511a421f4f0bfb027f0",
      "run_id": "a0ef26620f184245880cc44646e640b2",
      "run_name": "b117b3fb-278b2dce",
      "tags": {
        "serving_commit": "b117b3fba166f5e84b8ac30f7e025de06b778ef6",
        "planner_id": "azure-openai/gpt-4.1-mini/prompt-a625e51e3b58",
        "snapshot_id": "corpus-b2c3c1ba55bdda9a",
        "gates": "failed",
        "serving": "false",
        "owner_exception": "2026-11-16: the owner put Release 14 live although its promotion gate failed twice on ins-cover-start-fr (art_57); the live Release 13, same engine, gave art_57 in 2 of 3 tries at the same time; the release changes the front end only"
      },
      "aliases": [],
      "params": {
        "dataset": "lex-assistant-v2.jsonl",
        "dataset_sha256": "278b2dcee9ffff45181fcfd6c4fef39b1fc3852afc2c7a7fb67d06b4a2e8efcc",
        "base_url": "https://ca-lex-v3--v3-20261115t183640z-b117b3fba166-19f24051.whitemushroom-ff26ef2a.francecentral.azurecontainerapps.io"
      },
      "metrics": {
        "cases": 56,
        "verdict_accuracy": 0.48214285714285715,
        "citation_accuracy": 0.3617021276595745,
        "quote_verified_rate": 1,
        "replay_same_verdict_rate": 0.9821428571428571,
        "replay_same_plan_rate": 0.6444444444444445,
        "latency_p50_s": 1.8698710499738809,
        "latency_p95_s": 5.20790430001216,
        "outcome_right_article": 17,
        "outcome_wrong_article": 1,
        "outcome_not_found": 24,
        "outcome_wrongly_refused": 5,
        "outcome_right_behaviour": 9,
        "cost_eur_per_question_estimate": 0.001095451111111111
      },
      "artifacts": [
        "results.jsonl",
        "summary.md",
        "traces.jsonl"
      ]
    },
    {
      "version": 11,
      "created": "2026-11-19T04:27:23Z",
      "source": "models:/m-17745f8f4a6546b098bf7d9016fab26b",
      "run_id": "9c18d4a6c44d411cb6645dc82c457e8d",
      "run_name": "405abff0-278b2dce",
      "tags": {
        "serving_commit": "405abff0d2263fd817538c302b8936e5bcc73e17",
        "planner_id": "lex-v4-bricks/gpt-4.1-mini/prompt-44a95d72ed4c",
        "snapshot_id": "corpus-b2c3c1ba55bdda9a",
        "gates": "passed",
        "serving": "false"
      },
      "aliases": [],
      "params": {
        "dataset": "lex-assistant-v2.jsonl",
        "dataset_sha256": "278b2dcee9ffff45181fcfd6c4fef39b1fc3852afc2c7a7fb67d06b4a2e8efcc",
        "base_url": "https://ca-lex-v3--v3-20261119t030608z-405abff0d226-ee052a13.whitemushroom-ff26ef2a.francecentral.azurecontainerapps.io"
      },
      "metrics": {
        "cases": 56,
        "verdict_accuracy": 0.8035714285714286,
        "citation_accuracy": 0.7659574468085106,
        "quote_verified_rate": 1,
        "replay_same_verdict_rate": 1,
        "replay_same_plan_rate": 0.84,
        "latency_p50_s": 0.9500303499517031,
        "latency_p95_s": 2.4864255998982117,
        "outcome_right_article": 36,
        "outcome_wrong_article": 0,
        "outcome_not_found": 10,
        "outcome_wrongly_refused": 1,
        "outcome_right_behaviour": 9,
        "cost_eur_per_question_estimate": 0.0019372540000000002
      },
      "artifacts": [
        "results.jsonl",
        "summary.md",
        "traces.jsonl"
      ]
    },
    {
      "version": 12,
      "created": "2026-11-19T06:46:59Z",
      "source": "models:/m-d8813b62dc7a4cc39841eb84127fbbed",
      "run_id": "7ed7e96e34ed4460aee4d98eddf7aa72",
      "run_name": "0af7de23-278b2dce",
      "tags": {
        "serving_commit": "0af7de23ab559fecb7dcaf19340c9b143c76b928",
        "planner_id": "lex-v4-bricks/gpt-4.1-mini/prompt-44a95d72ed4c",
        "snapshot_id": "corpus-b2c3c1ba55bdda9a",
        "gates": "passed",
        "serving": "true"
      },
      "aliases": [
        "champion",
        "serving"
      ],
      "params": {
        "dataset": "lex-assistant-v2.jsonl",
        "dataset_sha256": "278b2dcee9ffff45181fcfd6c4fef39b1fc3852afc2c7a7fb67d06b4a2e8efcc",
        "base_url": "https://ca-lex-v3--v3-20261119t050837z-0af7de23ab55-9ed049f6.whitemushroom-ff26ef2a.francecentral.azurecontainerapps.io"
      },
      "metrics": {
        "cases": 56,
        "verdict_accuracy": 0.8035714285714286,
        "citation_accuracy": 0.7659574468085106,
        "quote_verified_rate": 1,
        "replay_same_verdict_rate": 1,
        "replay_same_plan_rate": 0.86,
        "latency_p50_s": 1.0500214500934817,
        "latency_p95_s": 3.7745232000015676,
        "outcome_right_article": 36,
        "outcome_wrong_article": 0,
        "outcome_not_found": 10,
        "outcome_wrongly_refused": 1,
        "outcome_right_behaviour": 9,
        "cost_eur_per_question_estimate": 0.001941446
      },
      "artifacts": [
        "results.jsonl",
        "summary.md",
        "traces.jsonl"
      ]
    }
  ],
  "experiment": "lex-assistant",
  "runs_logged": 31,
  "exported": "2026-11-19T06:48:20Z"
});
