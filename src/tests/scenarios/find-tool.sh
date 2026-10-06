#!/bin/sh
# Locates the locally built purview-build binary and the appsettings.json that ships beside it.
#
# The scenario matrices run the real CLI, not `dotnet run`, so a 20-case sweep stays fast. The tool
# must therefore have been built first (`just build` or `just scenario-build`). Set
# PURVIEW_BUILD_TOOL to point at a different binary, for example an installed release.
set -eu

# jq on Windows writes CRLF for raw output, and a stray CR ends up inside file names, paths and
# comparisons. Every scenario script reads jq through these wrappers instead of calling it directly.
jqr() {
	jq -r "$@" | tr -d '\r'
}

jqc() {
	jq -c "$@" | tr -d '\r'
}

if [ -z "${PURVIEW_BUILD_TOOL:-}" ]; then
	for candidate in \
		"src/src/Build/bin/Release/net10.0/Purview.Build.exe" \
		"src/src/Build/bin/Release/net10.0/Purview.Build" \
		"src/src/Build/bin/Debug/net10.0/Purview.Build.exe" \
		"src/src/Build/bin/Debug/net10.0/Purview.Build"; do
		if [ -x "$candidate" ]; then
			PURVIEW_BUILD_TOOL="$candidate"
			break
		fi
	done
fi

if [ -z "${PURVIEW_BUILD_TOOL:-}" ]; then
	echo "Could not find a built purview-build binary." >&2
	echo "Run 'just build' first, or set PURVIEW_BUILD_TOOL to a binary." >&2
	exit 1
fi

# Absolute, because every scenario runs with its working directory inside a throwaway repository.
TOOL=$(cd "$(dirname "$PURVIEW_BUILD_TOOL")" && pwd)/$(basename "$PURVIEW_BUILD_TOOL")

# The shipped appsettings.json sits beside the binary. Pinning it explicitly keeps the scenarios
# independent of the compile-time source path the tool would otherwise fall back to, and exercises
# the documented separation between MODULAR_PIPELINES_DIRECTORY and config-file discovery.
MODULAR_PIPELINES_DIRECTORY=$(dirname "$TOOL")
export MODULAR_PIPELINES_DIRECTORY

# A scenario must never pick up the developer's own environment.
unset PURVIEW_BUILD_CONFIG || true
unset PURVIEW_BUILD_USER_CONFIG || true
