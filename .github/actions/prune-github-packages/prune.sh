#!/usr/bin/env bash
# Deletes older versions of every NuGet package linked to this repository.
#
# The mirror exists for build acceleration: it holds versions published in the last
# RETENTION_HOURS so downstream repositories can restore them before NuGet.org indexes them. NuGet.org
# remains the durable archive, so this is best effort — a failure warns and leaves the release alone.
# Only packages whose `repository` link is this repository are touched, and the version just published
# is never deleted.
set -euo pipefail

OWNER="${PURVIEW_PRUNE_NAMESPACE:-}"
REPOSITORY="${PURVIEW_PRUNE_REPOSITORY:-}"
KEEP="${PURVIEW_PRUNE_KEEP:-1}"
RETENTION_HOURS="${PURVIEW_PRUNE_RETENTION_HOURS:-24}"
CURRENT_VERSION="${PURVIEW_PRUNE_VERSION:-}"

warn() {
	echo "::warning title=GitHub Packages cleanup::$*"
}

if [ -z "$OWNER" ] || [ -z "$REPOSITORY" ]; then
	warn "No namespace or repository was provided; nothing was pruned."
	exit 0
fi

if ! [[ "$KEEP" =~ ^[0-9]+$ ]]; then
	warn "keep-versions '$KEEP' is not a number; nothing was pruned."
	exit 0
fi

if ! [[ "$RETENTION_HOURS" =~ ^[0-9]+$ ]]; then
	warn "retention-hours '$RETENTION_HOURS' is not a number; nothing was pruned."
	exit 0
fi

if [ "$KEEP" -le 0 ] && [ "$RETENTION_HOURS" -le 0 ]; then
	echo "GitHub Packages version pruning is disabled (keep-versions=$KEEP, retention-hours=$RETENTION_HOURS)."
	exit 0
fi

if ! command -v gh >/dev/null 2>&1; then
	warn "gh is not available; nothing was pruned."
	exit 0
fi

now="$(date -u +%s)"
cutoff="$((now - RETENTION_HOURS * 3600))"

if [ "$RETENTION_HOURS" -gt 0 ]; then
	echo "Retention: keep versions published in the last ${RETENTION_HOURS} hour(s), and the newest ${KEEP}."
else
	echo "Retention: keep the newest ${KEEP} version(s)."
fi

packages_output="$(
	gh api --paginate "/orgs/${OWNER}/packages?package_type=nuget&per_page=100" \
		--jq ".[] | select(.repository.full_name == \"${REPOSITORY}\") | .name" 2>/dev/null
)" || {
	warn "Could not list packages for ${OWNER}; nothing was pruned."
	exit 0
}
packages="$packages_output"

if [ -z "$packages" ]; then
	echo "No NuGet packages are linked to ${REPOSITORY}; nothing to prune."
	exit 0
fi

while IFS= read -r package; do
	if [ -z "$package" ]; then
		continue
	fi

	if ! versions_output="$(
		gh api --paginate "/orgs/${OWNER}/packages/nuget/${package}/versions?state=active&per_page=100" \
			--jq '.[] | [.id, .name, .created_at] | @tsv' 2>/dev/null
	)"; then
		warn "Could not list versions of ${package}; it was left untouched."
		continue
	fi

	versions="$(printf '%s\n' "$versions_output" | sort -t $'\t' -k3,3)"

	if [ -z "$versions" ]; then
		continue
	fi

	# Only prune a package this release actually published. A package linked to the repository but not
	# part of this release (or a run that did not publish at all) must keep its history.
	if [ -n "$CURRENT_VERSION" ] && ! printf '%s\n' "$versions" | cut -f2 | grep -Fxq "$CURRENT_VERSION"; then
		echo "Skipping ${package}: ${CURRENT_VERSION} is not on the mirror, so this release did not publish it."
		continue
	fi

	count=$(printf '%s\n' "$versions" | wc -l | tr -d ' ')
	floor=$((count - KEEP))
	index=0

	while IFS=$'\t' read -r id name created; do
		index=$((index + 1))

		if [ -n "$CURRENT_VERSION" ] && [ "$name" = "$CURRENT_VERSION" ]; then
			echo "Keeping ${package} ${name}: it is the version just published."
			continue
		fi

		# Never empty the mirror: the newest versions stay regardless of age.
		if [ "$index" -gt "$floor" ]; then
			continue
		fi

		if [ "$RETENTION_HOURS" -gt 0 ]; then
			created_epoch="$(date -u -d "$created" +%s 2>/dev/null || true)"
			if [ -z "$created_epoch" ]; then
				warn "Could not read the publication time of ${package} ${name}; it was left in place."
				continue
			fi

			if [ "$created_epoch" -ge "$cutoff" ]; then
				echo "Keeping ${package} ${name}: published within the ${RETENTION_HOURS}-hour retention window."
				continue
			fi
		fi

		if gh api --method DELETE "/orgs/${OWNER}/packages/nuget/${package}/versions/${id}" >/dev/null 2>&1; then
			echo "Deleted ${package} ${name} (created ${created})."
		else
			warn "Could not delete ${package} ${name}; it was left in place."
		fi
	done <<<"$versions"
done <<<"$packages"
