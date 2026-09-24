#!/usr/bin/env bash
# Adds the next release to CHANGELOG.md and sets PluginVersion in gradle.properties (Release pipeline).
#
# Input (environment):
#   INCREMENT     Major | Minor | Patch, applied to the newest version in CHANGELOG.md
#   NEW_FEATURES  one entry per line (a leading "- " is optional)
#   IMPROVEMENTS  one entry per line
#   BUGS          one entry per line
#   CHECK_ONLY    1: only validate the input and print the new version, change nothing
# At least one of NEW_FEATURES, IMPROVEMENTS and BUGS must have an entry.
# Prints the new version (MAJOR.MINOR.PATCH) on the last line.
set -euo pipefail

cd "$(dirname "$0")/.."
CHANGELOG=CHANGELOG.md

fail() { echo "error: $*" >&2; exit 1; }

# Entries of a multi-line variable as "- entry" lines, blank lines dropped
entries() {
  printf '%s\n' "${1:-}" | sed -e 's/\r$//' -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' -e 's/^[-*][[:space:]]*//' \
    | awk 'length > 0 { print "- " $0 }'
}

features=$(entries "${NEW_FEATURES:-}")
improvements=$(entries "${IMPROVEMENTS:-}")
bugs=$(entries "${BUGS:-}")
[ -n "$features$improvements$bugs" ] || fail "fill in at least one of NEW_FEATURES, IMPROVEMENTS, BUGS"

current=$(sed -n 's/^## \([0-9]\+\.[0-9]\+\.[0-9]\+\)[[:space:]]*$/\1/p' "$CHANGELOG" | head -n 1)
[ -n "$current" ] || fail "no '## MAJOR.MINOR.PATCH' section in $CHANGELOG"
IFS=. read -r major minor patch <<< "$current"
case "${INCREMENT:-}" in
  Major) version="$((major + 1)).0.0" ;;
  Minor) version="$major.$((minor + 1)).0" ;;
  Patch) version="$major.$minor.$((patch + 1))" ;;
  *) fail "INCREMENT must be Major, Minor or Patch, got '${INCREMENT:-}'" ;;
esac

echo "$current -> $version"
if [ "${CHECK_ONLY:-}" = 1 ]; then
  echo "$version"
  exit 0
fi

section="## $version"
for group in "New Features:$features" "Improvements:$improvements" "Bugs:$bugs"; do
  [ -n "${group#*:}" ] && section+=$'\n'"### ${group%%:*}"$'\n'"${group#*:}"
done

# New section goes right before the newest one
awk -v section="$section" '!done && /^## / { print section "\n"; done = 1 } { print }' "$CHANGELOG" > "$CHANGELOG.tmp"
mv "$CHANGELOG.tmp" "$CHANGELOG"
sed -i "s/^PluginVersion=.*/PluginVersion=$version/" gradle.properties

echo "$version"
