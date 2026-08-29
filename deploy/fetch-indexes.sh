#!/bin/sh
# Fetch the signed indexes the nightly published, into $1 (default /indexes).
#
# These used to be COPYed into the image from a developer's deploy/indexes directory, which
# meant `az acr build` uploaded ~950 MB of law from a laptop on every deploy, and the served
# corpus was whatever that laptop happened to have downloaded last. It had already drifted:
# GitHub published 867 MB on 5 August while production served the 861 MB build from the 4th,
# and nothing surfaced it except the heartbeat workflow.
#
# The nightly (lex-ops, 02:17 UTC) builds, signs and releases each index to its corpus repo.
# Taking them from there makes the served index provably the published one and removes the
# laptop from production entirely.
#
# The protected deploy supplies one immutable release tag per publisher. Every database,
# companion and benchmark file for that publisher comes from that exact tag; following
# `latest` independently for each file would permit a release created mid-build to mix two
# otherwise valid generations.
set -eu
MAX_CASE_RESULTS_BYTES=67108864

benchmark_member_path() {
  bm_root=$1
  bm_manifest=$2
  bm_report=$3
  bm_report_name=$(basename "$bm_report")
  [ -f "$bm_root/$bm_manifest" ] && [ -f "$bm_root/$bm_report" ] \
    || { echo "ERROR: signed benchmark report or manifest is missing" >&2; return 1; }
  bm_shape=$(jq -er --arg report "$bm_report_name" '
    if (.files | type) != "array" then
      error("benchmark files must be an array")
    elif (.files | length) == 2
         and ([.files[]? | select(.path == $report)] | length) == 1
         and ([.files[]? | select(.path != $report and (.path | endswith(".jsonl")))] | length) == 1 then
      "complete"
    elif (.files | length) == 1 and .files[0].path == $report then
      "legacy"
    else
      error("benchmark manifest must contain one report and at most one case-results member")
    end' "$bm_root/$bm_manifest") \
    || { echo "ERROR: signed benchmark manifest has missing or extra members" >&2; return 1; }
  # Migration order is deliberate: this consumer quarantine ships first. The
  # lex-ops Rebuild 0 publication then produces the v4 report and JSONL member
  # together in the same signed benchmark manifest.
  if [ "$bm_shape" = "legacy" ]; then
    if jq -e '
      (.schema | type) == "string"
      and .schema == "lex-retrieval-benchmark/3"
      and (has("case_results_file") | not)
      and (has("case_results_count") | not)
      and (has("case_results_sha256") | not)' "$bm_root/$bm_report" >/dev/null; then
      echo "LEGACY: signed benchmark has no per-case evidence; hybrid activation stays quarantined" >&2
      return 3
    fi
    echo "ERROR: one-member benchmark manifest does not match the exact legacy report shape" >&2
    return 1
  fi
  bm_member=$(jq -er --arg report "$bm_report_name" '
    [.files[] | select(.path != $report and (.path | endswith(".jsonl")))]
    | if length == 1 then .[0].path else error("case-results member missing") end' \
    "$bm_root/$bm_manifest") \
    || { echo "ERROR: signed benchmark manifest has missing or extra members" >&2; return 1; }
  case "$bm_member" in
    ""|*[!A-Za-z0-9._-]*|/*|*\\*|*/*|..|../*|*/..|*/../*)
      echo "ERROR: unsafe case-results artifact path: $bm_member" >&2
      return 1 ;;
  esac
  bm_bound_member=$(jq -er '
    select(.schema == "lex-retrieval-benchmark/4")
    | .case_results_file | select(type == "string")' "$bm_root/$bm_report") \
    || { echo "ERROR: benchmark report has no v4 case-results binding" >&2; return 1; }
  [ "$bm_member" = "$bm_bound_member" ] \
    || { echo "ERROR: benchmark report and manifest bind different case-results files" >&2; return 1; }
  printf '%s\n' "$bm_member"
}

benchmark_member_size() {
  bm_size_root=$1
  bm_size_manifest=$2
  bm_size_member=$3
  jq -er --arg member "$bm_size_member" '
    .files[] | select(.path == $member) | .size
    | select(type == "number" and . > 0 and floor == .)' \
    "$bm_size_root/$bm_size_manifest"
}

verify_benchmark_evidence() {
  verify_root=$1
  verify_manifest=$2
  verify_report=$3
  if verify_member=$(benchmark_member_path "$verify_root" "$verify_manifest" "$verify_report"); then
    :
  else
    verify_member_status=$?
    return "$verify_member_status"
  fi
  [ -f "$verify_root/$verify_member" ] \
    || { echo "ERROR: signed case-results artifact is missing: $verify_member" >&2; return 1; }
  declared_size=$(benchmark_member_size "$verify_root" "$verify_manifest" "$verify_member") \
    || { echo "ERROR: case-results manifest size is invalid" >&2; return 1; }
  [ "$declared_size" -le "$MAX_CASE_RESULTS_BYTES" ] \
    || { echo "ERROR: case-results manifest size exceeds the fixed ceiling" >&2; return 1; }
  declared_sha=$(jq -er --arg member "$verify_member" '
    .files[] | select(.path == $member) | .sha256
    | select(type == "string" and test("^[0-9a-f]{64}$"))' "$verify_root/$verify_manifest") \
    || { echo "ERROR: case-results manifest digest is invalid" >&2; return 1; }
  report_sha=$(jq -er '
    .case_results_sha256
    | select(type == "string" and test("^[0-9a-f]{64}$"))' "$verify_root/$verify_report") \
    || { echo "ERROR: benchmark report case-results digest is invalid" >&2; return 1; }
  report_count=$(jq -er '
    .case_results_count | select(type == "number" and . > 0 and floor == .)' "$verify_root/$verify_report") \
    || { echo "ERROR: benchmark report case-results count is invalid" >&2; return 1; }
  actual_size=$(wc -c < "$verify_root/$verify_member")
  actual_sha=$(sha256sum < "$verify_root/$verify_member" | cut -d' ' -f1)
  actual_count=$(wc -l < "$verify_root/$verify_member")
  final_byte=$(tail -c 1 "$verify_root/$verify_member" | od -An -t u1 | tr -d ' ')
  [ "$actual_size" -eq "$declared_size" ] \
    || { echo "ERROR: case-results size does not match signed manifest" >&2; return 1; }
  [ "$actual_sha" = "$declared_sha" ] && [ "$actual_sha" = "$report_sha" ] \
    || { echo "ERROR: case-results digest does not match signed evidence" >&2; return 1; }
  [ "$actual_count" -eq "$report_count" ] && [ "$final_byte" = "10" ] \
    || { echo "ERROR: case-results row count or LF termination is invalid" >&2; return 1; }
  printf '%s\n' "$verify_member"
}

if [ "${1:-}" = "--verify-benchmark-evidence" ]; then
  [ "$#" -eq 4 ] \
    || { echo "usage: $0 --verify-benchmark-evidence ROOT MANIFEST REPORT" >&2; exit 2; }
  verify_benchmark_evidence "$2" "$3" "$4"
  exit $?
fi

OUT="${1:-/indexes}"
REQUIRE_MANIFEST="${LEX_REQUIRE_ARTIFACT_MANIFEST:-0}"
mkdir -p "$OUT"

# The indexes are hundreds of megabytes and the connection does drop mid-transfer:
# ACR run dd65 failed the whole deploy with "curl: (56) Connection died, tried 5 times"
# while fetching index-lu-legilux.db. Plain --retry made that worse than it looks,
# because it restarts the transfer from byte zero rather than resuming, so a drop near
# the end throws away everything and the next attempt is just as likely to die.
#
# --continue-at resumes where the last attempt stopped, --retry-all-errors covers the
# transport failures (56 among them) that --retry ignores by default, and the speed
# floor abandons a socket that has stalled instead of holding the build until curl's
# own timeout. The size and SQLite-header checks below still catch a truncated result,
# so resuming can fail the build but cannot silently ship half an index.
fetch() {
  curl -fsSL \
    --retry 8 --retry-delay 5 --retry-all-errors \
    --continue-at - \
    --speed-limit 1024 --speed-time 60 \
    -o "$1" "$2"
}

fetch_bounded() {
  curl -fsSL \
    --retry 8 --retry-delay 5 --retry-all-errors \
    --continue-at - \
    --speed-limit 1024 --speed-time 60 \
    --max-filesize "$3" \
    -o "$1" "$2"
}

# The optional artifacts are small and their absence is a valid answer, so they get a
# short retry: waiting out eight attempts to learn that a manifest was never published
# would add minutes to every build of a publisher that has none.
fetch_optional() {
  curl -fsSL --retry 3 --retry-delay 5 --retry-all-errors -o "$1" "$2"
}

# publisher repo : collection : asset name : immutable release tag
SETS="lex-corpus-lu-legilux:lu-legilux:index-lu-legilux.db:${LEX_RELEASE_TAG_LU_LEGILUX:-}
lex-corpus-eu-eurlex:eu-eurlex:index-eu-eurlex.db:${LEX_RELEASE_TAG_EU_EURLEX:-}"

echo "$SETS" | while IFS=: read -r repo collection asset release_tag; do
  [ -n "$repo" ] || continue
  ticket="${release_tag#index-$collection-}"
  if [ "$ticket" = "$release_tag" ] || [ "${#ticket}" -ne 64 ] \
    || ! printf '%s' "$ticket" | grep -Eq '^[0-9a-f]{64}$'; then
    echo "ERROR: $repo requires an exact index-$collection-<64 hex> release tag" >&2
    exit 1
  fi
  release_base="https://github.com/SFHAJJI/$repo/releases/download/$release_tag"
  echo "fetching $asset from $repo release $release_tag"
  fetch "$OUT/$asset" "$release_base/$asset"

  # A truncated or rate-limited download would otherwise produce a container that starts
  # happily and answers every question with no_corpus_mounted. Fail the build instead.
  size=$(wc -c < "$OUT/$asset")
  if [ "$size" -lt 1000000 ]; then
    echo "ERROR: $asset is only $size bytes, expected a real index" >&2
    exit 1
  fi
  if ! head -c 15 "$OUT/$asset" | grep -q "SQLite format 3"; then
    echo "ERROR: $asset is not a SQLite database" >&2
    exit 1
  fi
  stem="${asset%.db}"
  manifest="$stem.manifest.json"
  signature="$stem.manifest.sig"
  if fetch_optional "$OUT/$manifest" \
       "$release_base/$manifest"; then
    fetch_optional "$OUT/$signature" "$release_base/$signature" \
      || { echo "ERROR: $repo signed artifact manifest has no signature" >&2; exit 1; }
    manifest_ticket=$(jq -er '.sources.queue_ticket_id | select(type == "string")' \
      "$OUT/$manifest") \
      || { echo "ERROR: $repo signed manifest has no queue ticket" >&2; exit 1; }
    if [ "$manifest_ticket" != "$ticket" ]; then
      echo "ERROR: $repo signed queue ticket does not match the exact release tag" >&2
      exit 1
    fi
    manifest_collection=$(jq -er '.sources.collection | select(type == "string")' \
      "$OUT/$manifest")
    manifest_corpus=$(jq -er '.sources.corpus_commit | select(type == "string")' \
      "$OUT/$manifest")
    if [ "$manifest_collection" != "$collection" ] \
      || ! printf '%s' "$manifest_corpus" | grep -Eq '^[0-9a-f]{40}$'; then
      echo "ERROR: $repo signed manifest has another collection or invalid corpus commit" >&2
      exit 1
    fi
    manifest_sha256=$(sha256sum < "$OUT/$manifest" | cut -d' ' -f1)
    jq -r '.files[].path' "$OUT/$manifest" | while IFS= read -r companion; do
      [ "$companion" = "$asset" ] && continue
      case "$companion" in
        ""|/*|*\\*|..|../*|*/..|*/../*) echo "ERROR: unsafe release artifact path: $companion" >&2; exit 1 ;;
      esac
      mkdir -p "$(dirname "$OUT/$companion")"
      fetch "$OUT/$companion" "$release_base/$companion"
    done
    echo "  fetched signed manifest: $manifest"
  elif [ "$REQUIRE_MANIFEST" = "1" ]; then
    echo "ERROR: $repo has no signed artifact manifest" >&2
    exit 1
  else
    rm -f "$OUT/$manifest"
    echo "  migration: no artifact manifest published yet"
  fi
  benchmark="retrieval-benchmark-$collection.json"
  benchmark_manifest="retrieval-benchmark-$collection.manifest.json"
  benchmark_signature="retrieval-benchmark-$collection.manifest.sig"
  has_vectors=false
  if [ -f "$OUT/$manifest" ] \
    && jq -e --arg vector "$stem.vectors" \
      'any(.files[]?; .path == $vector)' "$OUT/$manifest" >/dev/null; then
    has_vectors=true
  fi
  if [ "$has_vectors" = "true" ]; then
    if fetch_optional "$OUT/$benchmark" "$release_base/$benchmark" \
      && fetch_optional "$OUT/$benchmark_manifest" "$release_base/$benchmark_manifest" \
      && fetch_optional "$OUT/$benchmark_signature" "$release_base/$benchmark_signature"; then
      benchmark_ticket=$(jq -er '.sources.queue_ticket_id | select(type == "string")' \
        "$OUT/$benchmark_manifest") \
        || { echo "ERROR: $repo signed benchmark manifest has no queue ticket" >&2; exit 1; }
      if [ "$benchmark_ticket" != "$ticket" ]; then
        echo "ERROR: $repo signed benchmark queue ticket does not match the exact release tag" >&2
        exit 1
      fi
      benchmark_collection=$(jq -er '.sources.collection | select(type == "string")' \
        "$OUT/$benchmark_manifest")
      benchmark_corpus=$(jq -er '.sources.corpus_commit | select(type == "string")' \
        "$OUT/$benchmark_manifest")
      benchmark_index=$(jq -er '.sources.index_manifest_sha256 | select(type == "string")' \
        "$OUT/$benchmark_manifest")
      if [ "$benchmark_collection" != "$collection" ] \
        || [ "$benchmark_corpus" != "$manifest_corpus" ] \
        || [ "$benchmark_index" != "$manifest_sha256" ]; then
        echo "ERROR: $repo signed benchmark manifest does not bind the exact index release" >&2
        exit 1
      fi
      benchmark_evidence_status=complete
      if case_results=$(benchmark_member_path "$OUT" "$benchmark_manifest" "$benchmark"); then
        :
      else
        benchmark_member_status=$?
        if [ "$benchmark_member_status" -eq 3 ]; then
          benchmark_evidence_status=legacy
        else
          echo "ERROR: benchmark_member_path returned an unsupported status: $benchmark_member_status" >&2
          exit 1
        fi
      fi
      if [ "$benchmark_evidence_status" = "complete" ]; then
        case_results_size=$(benchmark_member_size "$OUT" "$benchmark_manifest" "$case_results") \
          || { echo "ERROR: case-results manifest size is invalid" >&2; exit 1; }
        [ "$case_results_size" -le "$MAX_CASE_RESULTS_BYTES" ] \
          || { echo "ERROR: case-results manifest size exceeds the fixed ceiling" >&2; exit 1; }
        fetch_bounded "$OUT/$case_results" "$release_base/$case_results" \
          "$MAX_CASE_RESULTS_BYTES"
        verify_benchmark_evidence "$OUT" "$benchmark_manifest" "$benchmark" >/dev/null
        echo "  fetched signed public retrieval benchmark: $benchmark and $case_results"
      else
        echo "  signed legacy benchmark has no per-case evidence; hybrid activation stays quarantined"
      fi
    else
      rm -f "$OUT/$benchmark" "$OUT/$benchmark_manifest" "$OUT/$benchmark_signature"
      if [ "$REQUIRE_MANIFEST" = "1" ]; then
        echo "ERROR: $repo vector release is missing signed retrieval benchmark evidence" >&2
        exit 1
      fi
      echo "  migration: vectors quarantined because retrieval benchmark evidence is absent"
    fi
  fi
  echo "  ok: $asset $((size / 1024 / 1024)) MB"
done
