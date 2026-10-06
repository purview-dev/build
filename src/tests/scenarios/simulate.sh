#!/bin/sh
# Explains the release decision for a simulated ref and version, in a throwaway repository root so
# the working tree's own package.json is untouched.
#
# Mutates nothing and makes no network calls: supplying Release:Context:* suppresses both the git
# lookups and any feed query.
set -eu

if [ "$#" -lt 2 ]; then
	echo "usage: simulate.sh <ref> <version> [extra purview-build arguments]" >&2
	echo "example: simulate.sh refs/heads/release/2.0 2.0.2 --Release:Eligibility:Policy=TrunkReservesMinor" >&2
	exit 2
fi

REF="$1"
VERSION="$2"
shift 2

. "$(dirname "$0")/find-tool.sh"

WORK=".scenario-runs/simulate"
rm -rf "$WORK"
mkdir -p "$WORK"

printf '{ "name": "simulated", "version": "%s" }\n' "$VERSION" > "$WORK/package.json"

# Carry over the real repository's configuration, so a simulation reflects how this repository is
# actually configured rather than bare defaults.
if [ -f "purview-build.json" ]; then
	cp "purview-build.json" "$WORK/purview-build.json"
fi

# The tags that really exist, so "already released" is judged against reality.
if git rev-parse --git-dir >/dev/null 2>&1; then
	git tag --list > "$WORK/tags.txt"
else
	: > "$WORK/tags.txt"
fi

cd "$WORK"

exec "$TOOL" release-explain \
	"--Release:Context:Ref=$REF" \
	"--Release:Context:ExistingTags=tags.txt" \
	"$@"
