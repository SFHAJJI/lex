# Groundedness scoring over the eval run's evidence export (Foundry-hybrid, D45 posture:
# keep the in-house loop, adopt Azure AI Foundry's evaluators — no Agent Service needed).
# The evaluator is an LLM judge billed as plain tokens on our own deployment.
#
#   pip install azure-ai-evaluation
#   set AOAI_ENDPOINT / AOAI_KEY / AOAI_CHAT_DEPLOYMENT, then:
#   python evals/groundedness.py [evals/out/results.jsonl]
#
# Exit code = number of cases scoring below 3/5 (same contract as run-evals.ps1).
import json, os, pathlib, sys

from azure.ai.evaluation import GroundednessEvaluator, AzureOpenAIModelConfiguration

path = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else pathlib.Path(__file__).parent / "out" / "results.jsonl")
rows = [json.loads(l) for l in path.read_text(encoding="utf-8").splitlines() if l.strip()]

judge = GroundednessEvaluator(AzureOpenAIModelConfiguration(
    azure_endpoint=os.environ["AOAI_ENDPOINT"],
    api_key=os.environ["AOAI_KEY"],
    azure_deployment=os.environ.get("AOAI_CHAT_DEPLOYMENT", "gpt-5-mini"),
))

low = 0
for row in rows:
    if not row.get("context"):
        print(f"{row['id']:>18}: (no evidence context — skipped)")
        continue
    r = judge(query=row["query"], response=row["response"], context=row["context"])
    score = r.get("groundedness", r.get("gpt_groundedness"))
    flag = "" if (score or 0) >= 3 else "  <-- BELOW BAR"
    if (score or 0) < 3:
        low += 1
    print(f"{row['id']:>18}: groundedness {score}/5{flag}")

print(f"\n{len(rows) - low}/{len(rows)} grounded (threshold 3/5)")
sys.exit(low)
