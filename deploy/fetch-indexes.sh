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
mkdir -p "$OUT"

# publisher repo : asset name
SETS="lex-corpus-lu-legilux:index-lu-legilux.db
lex-corpus-eu-eurlex:index-eu-eurlex.db"

echo "$SETS" | while IFS=: read -r repo asset; do
  [ -n "$repo" ] || continue
  url="https://github.com/SFHAJJI/$repo/releases/latest/download/$asset"
  echo "fetching $asset from $repo"
  curl -fsSL --retry 3 --retry-delay 5 -o "$OUT/$asset" "$url"

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
  echo "  ok: $asset $((size / 1024 / 1024)) MB"
done
