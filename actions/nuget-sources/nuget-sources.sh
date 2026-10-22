#!/usr/bin/env bash
# Registers nuget.org and the organisation GitHub Packages feed for the rest of the job.
#
# The source is added to the *user-level* NuGet configuration (the same place `dotnet nuget add source`
# writes when no --configfile is given), so no committed nuget.config is modified and a repository's own
# sources are merged rather than replaced. The password is stored as %GITHUB_TOKEN%, which NuGet expands
# at restore/push time: the token is never written to disk and never appears in a log.
set -euo pipefail

SOURCE_NAME="purview-github-packages"
NUGET_ORG_URL="https://api.nuget.org/v3/index.json"

MODE="${PURVIEW_NUGET_MODE:-configure}"
TOKEN="${PURVIEW_NUGET_TOKEN:-}"
NAMESPACE="${PURVIEW_NUGET_NAMESPACE:-}"
USERNAME="${PURVIEW_NUGET_USERNAME:-github-actions}"
MAPPING="${PURVIEW_NUGET_SOURCE_MAPPING:-}"
CLEAR_HTTP_CACHE="${PURVIEW_NUGET_CLEAR_HTTP_CACHE:-false}"
BACKUP="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/purview-nuget-user-config.bak"

# The user-level config, not the nearest repository file: the repository's nuget.config is the
# consumer's, and CI must not rewrite a tracked file.
if [ -n "${APPDATA:-}" ] && [ -d "${APPDATA}/NuGet" ]; then
	USER_CONFIG="${APPDATA}/NuGet/NuGet.Config"
else
	USER_CONFIG="${HOME}/.nuget/NuGet/NuGet.Config"
fi

if [ "$MODE" = "remove" ]; then
	# Drop an organisation read token exported for this job, if one was used.
	if [ -n "${GITHUB_ENV:-}" ]; then
		echo "PURVIEW_NUGET_TOKEN=" >>"$GITHUB_ENV"
	fi

	if [ -f "$BACKUP" ]; then
		cp "$BACKUP" "$USER_CONFIG"
		rm -f "$BACKUP"
		echo "Restored the user-level NuGet configuration."
	elif [ -f "$USER_CONFIG" ]; then
		dotnet nuget remove source "$SOURCE_NAME" --configfile "$USER_CONFIG" >/dev/null 2>&1 || true
		echo "Removed the temporary '$SOURCE_NAME' NuGet source."
	fi

	exit 0
fi

if [ "$MODE" != "configure" ]; then
	echo "::error::Unknown nuget-sources mode '$MODE'. Use 'configure' or 'remove'."
	exit 1
fi

if [ -z "$NAMESPACE" ]; then
	echo "::error::No GitHub Packages namespace was provided. Set the 'namespace' input (the shared workflows pass github.repository_owner)."
	exit 1
fi

if [ -z "$TOKEN" ]; then
	echo "::error::No token was provided. The shared workflows pass github.token; the token is required to read and write the organisation feed."
	exit 1
fi

FEED_URL="https://nuget.pkg.github.com/${NAMESPACE}/index.json"

mkdir -p "$(dirname "$USER_CONFIG")"

if [ -f "$USER_CONFIG" ]; then
	cp "$USER_CONFIG" "$BACKUP"
else
	rm -f "$BACKUP"
	printf '%s\n' '<?xml version="1.0" encoding="utf-8"?>' '<configuration>' '</configuration>' >"$USER_CONFIG"
fi

# A repository nuget.config with <clear /> discards lower-priority sources, including this one. Nothing
# here can override that without editing the checkout, so say so instead of leaving a restore that
# silently never sees GitHub Packages.
if grep -Ril --include='nuget.config' --include='NuGet.Config' '<clear' . 2>/dev/null | grep -q .; then
	echo "::warning::A repository nuget.config contains <clear />, which hides the user-level GitHub Packages source. Add the source to that file or remove <clear />."
fi

if ! dotnet nuget list source --configfile "$USER_CONFIG" --format detailed |
	grep -qE '^[[:space:]]*[0-9]+\.  nuget\.org([[:space:]]|$)'; then
	# A stable key for the mapping below. If the same URL is already present under another name this
	# adds a harmless duplicate; the key is what NuGet mapping references.
	dotnet nuget add source "$NUGET_ORG_URL" --name nuget.org --configfile "$USER_CONFIG" 2>/dev/null || true
fi

# Remove first so a rerun (or a self-hosted runner left behind by a cancelled job) cannot fail on a
# duplicate name. The remove also drops the old credentials section.
dotnet nuget remove source "$SOURCE_NAME" --configfile "$USER_CONFIG" >/dev/null 2>&1 || true

# The default workflow token is already in the environment, so the configuration references it through
# the %GITHUB_TOKEN% macro. An organisation-level read token is not, so export it for the rest of the
# job and reference it through its own macro. Either way the file holds a macro, never the token.
if [ -n "${GITHUB_TOKEN:-}" ] && [ "$TOKEN" = "$GITHUB_TOKEN" ]; then
	CREDENTIAL_MACRO='%GITHUB_TOKEN%'
	CREDENTIAL_SOURCE='GITHUB_TOKEN'
else
	if [ -n "${GITHUB_ENV:-}" ]; then
		echo "PURVIEW_NUGET_TOKEN=$TOKEN" >>"$GITHUB_ENV"
	else
		export PURVIEW_NUGET_TOKEN="$TOKEN"
	fi
	CREDENTIAL_MACRO='%PURVIEW_NUGET_TOKEN%'
	CREDENTIAL_SOURCE='PURVIEW_NUGET_TOKEN'
fi

dotnet nuget add source "$FEED_URL" \
	--name "$SOURCE_NAME" \
	--username "$USERNAME" \
	--password "$CREDENTIAL_MACRO" \
	--store-password-in-clear-text \
	--configfile "$USER_CONFIG"

# Package source mapping is always written. Central package management (NU1507) fails or warns
# whenever more than one source is configured without it, and every Purview repository uses CPM.
#
# With no explicit patterns, `*` maps to BOTH sources. That is not a priority mechanism: it only makes
# the two-source configuration legal, and NuGet still resolves the highest matching version across
# both. When PURVIEW_NUGET_SOURCE_MAPPING lists patterns, those are pinned to GitHub Packages and
# everything else to nuget.org.
MAPPING_FILE="$(mktemp)"
{
	echo '  <packageSourceMapping>'
	echo "    <packageSource key=\"$SOURCE_NAME\">"
	if [ -n "$MAPPING" ]; then
		printf '%s\n' "$MAPPING" | tr ';,' '\n' | while IFS= read -r pattern; do
			pattern="$(printf '%s' "$pattern" | tr -d '[:space:]')"
			if [ -z "$pattern" ]; then
				continue
			fi
			echo "      <package pattern=\"$pattern\" />"
		done
	else
		echo '      <package pattern="*" />'
	fi
	echo '    </packageSource>'
	echo '    <packageSource key="nuget.org">'
	echo '      <package pattern="*" />'
	echo '    </packageSource>'
	echo '  </packageSourceMapping>'
} >"$MAPPING_FILE"

# Replace any mapping inherited from the user configuration. The backup taken above restores the
# original on cleanup, so a developer's own mapping is not lost on a self-hosted runner.
awk '
	/<packageSourceMapping>/ { skip = 1 }
	skip { if (/<\/packageSourceMapping>/) { skip = 0 } ; next }
	{ print }
' "$USER_CONFIG" >"$USER_CONFIG.tmp"

awk -v mapping_file="$MAPPING_FILE" '
	/<\/configuration>/ && !inserted {
		while ((getline line < mapping_file) > 0) print line
		close(mapping_file)
		inserted = 1
	}
	{ print }
' "$USER_CONFIG.tmp" >"$USER_CONFIG.tmp2"
mv "$USER_CONFIG.tmp2" "$USER_CONFIG"
rm -f "$USER_CONFIG.tmp" "$MAPPING_FILE"

if [ -n "$MAPPING" ]; then
	echo "Applied package source mapping: explicit patterns resolve only from '$SOURCE_NAME'; everything else from nuget.org."
else
	echo "Applied package source mapping: both sources are available for every package (required by central package management)."
fi

if [ "$CLEAR_HTTP_CACHE" = "true" ] || [ -n "$MAPPING" ]; then
	# Source metadata is cached per source for a short window. When a repository explicitly requires a
	# package from the mirror, a cached "not found" would defeat the requirement, so drop it.
	dotnet nuget locals http-cache --clear
fi

echo "NuGet sources ready: nuget.org + ${FEED_URL} (credentials resolved from %${CREDENTIAL_SOURCE}%)."
