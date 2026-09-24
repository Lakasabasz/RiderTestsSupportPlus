#!/usr/bin/env bash
# Finds the artifact version built from a release commit: master builds publish MAJOR.MINOR.PATCH.<build run id>.
# Fails while there is none yet, so the action's retries wait for the Build pipeline (Release pipeline).
#
# Input (environment):
#   NEW_VERSION             MAJOR.MINOR.PATCH being released
#   BUDDY_API_TOKEN         Buddy personal access token with ARTIFACT_READ (the workspace needs the Developer API enabled)
#   RELEASE_ARTIFACT_ID     hash ID of the artifact (the ID button on the artifact page), not its name
#   BUDDY_WORKSPACE_DOMAIN  set by Buddy
#   BUDDY_API_URL           optional, default https://api.eu.buddy.works
# Prints the artifact version (e.g. 0.3.0.57) on the last line.
set -euo pipefail

fail() { echo "error: $*" >&2; exit 1; }

[[ "${NEW_VERSION:-}" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || fail "NEW_VERSION must be MAJOR.MINOR.PATCH, got '${NEW_VERSION:-}'"
: "${BUDDY_API_TOKEN:?}" "${RELEASE_ARTIFACT_ID:?}" "${BUDDY_WORKSPACE_DOMAIN:?}"
api="${BUDDY_API_URL:-https://api.eu.buddy.works}"

# Newest first; only the first page is needed, the release build is one of the latest versions
response=$(curl -fsS -H "Authorization: Bearer $BUDDY_API_TOKEN" \
  "$api/workspaces/$BUDDY_WORKSPACE_DOMAIN/artifacts/$RELEASE_ARTIFACT_ID/versions?sort_by=created_date&sort_direction=DESC&per_page=50") \
  || fail "listing artifact versions failed"

pattern="^${NEW_VERSION//./\\.}\\.[0-9]+$"
version=$(printf '%s' "$response" | grep -oE '"version"[[:space:]]*:[[:space:]]*"[^"]*"' \
  | sed -E 's/.*"([^"]*)"$/\1/' | grep -E "$pattern" | head -n 1 || true)
[ -n "$version" ] || fail "no artifact version $NEW_VERSION.<build> yet"

echo "$version"
