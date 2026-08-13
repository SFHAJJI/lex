#!/usr/bin/env bash
# Behavioural probes against a RUNNING Lex instance, through the same public /mcp door every
# consumer uses. These are not unit tests: every defect they encode was found by querying a
# running system after the full test suite was green, and several had been invisible to code
# review. A probe that has never failed proves nothing, so the suite's falsifiability is on
# record: run against the 2026-08-11 production revision, probes 1, 2, 3 and 6 fail, which is
# exactly the defect set that revision carries.
#
# Usage:   scripts/probe-live.sh https://law.soufien.lu
#          scripts/probe-live.sh https://<candidate-fqdn>
# Exit:    0 when every probe passes; otherwise the number of failed probes.
#
# Probe 6 requires the 2026-08-13 Luxembourg index (the first carrying recovered
# financial-sector-law text); on older indexes it fails and that failure is correct.
set -u
BASE="${1:?usage: probe-live.sh <base-url>}"
FAIL=0
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
if python3 -c 'import sys; raise SystemExit(sys.version_info < (3, 10))' >/dev/null 2>&1; then
  PYTHON=python3
elif python -c 'import sys; raise SystemExit(sys.version_info < (3, 10))' >/dev/null 2>&1; then
  PYTHON=python
else
  echo "Python 3.10 or newer is required" >&2
  exit 2
fi
CURL_BIN=${CURL_BIN:-curl}

mcp() {
  "$CURL_BIN" --fail-with-body --silent --show-error --max-time 45 -X POST "$BASE/mcp" \
    -H "Content-Type: application/json" \
    -H "Accept: application/json, text/event-stream" \
    -d "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"$1\",\"arguments\":$2}}"
}

check() { # name, python-expr over parsed publisher list `pubs`
  local name="$1" tool="$2" args="$3" expr="$4"
  local raw verdict
  if raw=$(mcp "$tool" "$args"); then
    verdict=$(printf '%s' "$raw" | "$PYTHON" "$SCRIPT_DIR/probe-live-evaluate.py" "$expr")
  else
    verdict="FAIL (transport)"
  fi
  printf '%-52s %s\n' "$name" "$verdict"
  case "$verdict" in PASS) ;; *) FAIL=$((FAIL+1));; esac
}

hits() { echo "[h for p in pubs for h in p.get('hits', [])]"; }

# 1. Cross-publisher fusion: an unfiltered search must give both publishers visible slots.
#    The 2026-08-11 revision gave EU 40 of 40 and Luxembourg 0, with 27 LU matches existing.
check "1 fusion: both publishers get slots" search \
  '{"query":"secteur financier","limit":40}' \
  "exact_publishers(pubs) and all(len(p.get('hits', [])) >= 5 for p in pubs)"

# 2. Scoped search: naming the works lifts the two-per-work fairness cap. Scoped to one law
#    with limit 40, the pre-fix behaviour returned exactly 2 rows.
check "2 scoped search returns more than two rows" search \
  '{"query":"surveillance","limit":40,"publisher":"lu-legilux","works":"loi-2015-12-07-n1"}' \
  "len($(hits)) > 2"

# 3. Snippets, EU: text coverage is complete there, so nearly every hit must carry a window.
#    Before the fix, snippet() ran against a contentless FTS table and every snippet was empty.
check "3 snippets present on EU hits" search \
  '{"query":"data protection officer","limit":10,"publisher":"eu-eurlex"}' \
  "sum(1 for h in $(hits) if (h.get('snippet') or '').strip()) >= 7"

# 4. Snippets, LU: at least some hits carry windows; provisions genuinely holding no text may
#    honestly return null, so the bar is lower than EU's by design.
check "4 snippets present on LU hits" search \
  '{"query":"etablissements de credit","limit":10,"publisher":"lu-legilux"}' \
  "sum(1 for h in $(hits) if (h.get('snippet') or '').strip()) >= 1"

# 5. Jurisdiction filter: asking for lu must select only the Luxembourg reader.
check "5 jurisdiction filter selects one publisher" search \
  '{"query":"secteur financier","limit":5,"jurisdiction":"lu"}' \
  "len(pubs) == 1 and pubs[0].get('envelope', {}).get('publisher') == 'lu-legilux'"

# 6. Recovered text: the financial-sector law's 2003 consolidation extracted 105 of 145
#    provisions empty until 2026-08-13. With the recovered index mounted, a search scoped to
#    the law must return hits and at least one must carry a snippet, which requires text.
check "6 financial-sector law carries text (2026-08-13+)" search \
  '{"query":"secteur financier","limit":10,"publisher":"lu-legilux","works":"loi-1993-04-05-n1"}' \
  "len($(hits)) >= 3 and any((h.get('snippet') or '').strip() for h in $(hits))"

# 7. Coverage: both publishers mounted, non-zero works, and the envelope signature valid,
#    which is the stamp check every reply rides on.
check "7 coverage: two signed publishers" coverage '{}' \
  "exact_publishers(pubs) and all(p.get('works', 0) > 0 and p.get('envelope', {}).get('freshness', {}).get('stamp_signature_valid') is True for p in pubs)"

echo
if [ "$FAIL" -eq 0 ]; then echo "ALL PROBES PASSED"; else echo "$FAIL PROBE(S) FAILED"; fi
exit "$FAIL"
