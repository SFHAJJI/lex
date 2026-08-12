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
# `releases/latest/download/<asset>` is GitHub's own redirect to the newest release asset,
# so this needs no token, no API call and no tag kept in sync.
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

# publisher repo : asset name
SETS="lex-corpus-lu-legilux:index-lu-legilux.db
lex-corpus-eu-eurlex:index-eu-eurlex.db"

echo "$SETS" | while IFS=: read -r repo asset; do
  [ -n "$repo" ] || continue
  url="https://github.com/SFHAJJI/$repo/releases/latest/download/$asset"
  echo "fetching $asset from $repo"
  fetch "$OUT/$asset" "$url"

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
       "https://github.com/SFHAJJI/$repo/releases/latest/download/$manifest"; then
    fetch_optional "$OUT/$signature" \
      "https://github.com/SFHAJJI/$repo/releases/latest/download/$signature"
    jq -r '.files[].path' "$OUT/$manifest" | while IFS= read -r companion; do
      [ "$companion" = "$asset" ] && continue
      case "$companion" in
        ""|/*|*\\*|..|../*|*/..|*/../*) echo "ERROR: unsafe release artifact path: $companion" >&2; exit 1 ;;
      esac
      mkdir -p "$(dirname "$OUT/$companion")"
      fetch "$OUT/$companion" \
        "https://github.com/SFHAJJI/$repo/releases/latest/download/$companion"
    done
    echo "  fetched signed manifest: $manifest"
  elif [ "$REQUIRE_MANIFEST" = "1" ]; then
    echo "ERROR: $repo has no signed artifact manifest" >&2
    exit 1
  else
    rm -f "$OUT/$manifest"
    echo "  migration: no artifact manifest published yet"
  fi
  collection="${stem#index-}"
  benchmark="retrieval-benchmark-$collection.json"
  benchmark_manifest="retrieval-benchmark-$collection.manifest.json"
  benchmark_signature="retrieval-benchmark-$collection.manifest.sig"
  if fetch_optional "$OUT/$benchmark" \
       "https://github.com/SFHAJJI/$repo/releases/latest/download/$benchmark" \
    && fetch_optional "$OUT/$benchmark_manifest" \
       "https://github.com/SFHAJJI/$repo/releases/latest/download/$benchmark_manifest" \
    && fetch_optional "$OUT/$benchmark_signature" \
       "https://github.com/SFHAJJI/$repo/releases/latest/download/$benchmark_signature"; then
    echo "  fetched signed public retrieval benchmark: $benchmark"
  else
    rm -f "$OUT/$benchmark" "$OUT/$benchmark_manifest" "$OUT/$benchmark_signature"
    echo "  retrieval benchmark not published yet"
  fi
  echo "  ok: $asset $((size / 1024 / 1024)) MB"
done
