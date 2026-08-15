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
    manifest_sha256=$(sha256sum "$OUT/$manifest" | cut -d' ' -f1)
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
      echo "  fetched signed public retrieval benchmark: $benchmark"
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
