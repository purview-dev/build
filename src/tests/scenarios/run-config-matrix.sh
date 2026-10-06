#!/bin/sh
# Runs every case in src/tests/fixtures/config-resolution-scenarios.json through the real CLI and
# compares the resolved configuration path, the shadow warnings and the exit code against the file.
#
# Uses `release-explain --format=json`, which reports the whole resolution (resolved path, probe
# trail, shadowed files, user-config state) and runs no module, so nothing is built or mutated.
set -eu

SCENARIOS="src/tests/fixtures/config-resolution-scenarios.json"
WORK=".scenario-runs/config"

. "$(dirname "$0")/find-tool.sh"

rm -rf "$WORK"
mkdir -p "$WORK"

total=0
failed=0

# Paths are compared by suffix, not by prefix-stripping an absolute root: the tool reports native
# paths (C:\... on Windows) while the shell's $PWD is a POSIX path (/p/...), so the two roots never
# match textually even when they denote the same directory.
ends_with() {
	haystack=$(printf '%s' "$1" | tr '\\' '/')
	needle=$(printf '%s' "$2" | tr '\\' '/')

	case "$haystack" in
	*"/$needle") return 0 ;;
	"$needle") return 0 ;;
	*) return 1 ;;
	esac
}

count=$(jq '.scenarios | length' "$SCENARIOS" | tr -d '\r')
index=0

while [ "$index" -lt "$count" ]; do
	scenario=$(jqc ".scenarios[$index]" "$SCENARIOS")
	index=$((index + 1))
	total=$((total + 1))

	name=$(printf '%s' "$scenario" | jqr '.name')
	config=$(printf '%s' "$scenario" | jqr '.config // ""')
	env_config=$(printf '%s' "$scenario" | jqr '.env // ""')
	user_config=$(printf '%s' "$scenario" | jqr '.userConfig')
	is_local=$(printf '%s' "$scenario" | jqr '.isLocal')
	user_config_file=$(printf '%s' "$scenario" | jqr '.userConfigFile // false')
	malformed=$(printf '%s' "$scenario" | jqr '.malformed // ""')
	expected_path=$(printf '%s' "$scenario" | jqr '.expected.resolvedPath // ""')
	expected_exit=$(printf '%s' "$scenario" | jqr '.expected.exitCode')
	expected_error=$(printf '%s' "$scenario" | jqr '.expected.errorContains // ""')
	expected_near_miss=$(printf '%s' "$scenario" | jqr '.expected.nearMissContains // ""')
	expected_user_active=$(printf '%s' "$scenario" | jqr '.expected.userConfigActive // "null"')

	case_dir="$WORK/$name"
	mkdir -p "$case_dir"
	printf '{ "name": "scenario", "version": "1.0.0" }\n' > "$case_dir/package.json"

	# Every configuration file carries the same relative Build:Solution, so path anchoring is
	# comparable wherever the file was found.
	for file in $(printf '%s' "$scenario" | jqr '.files[]?'); do
		mkdir -p "$case_dir/$(dirname "$file")"
		if [ "$file" = "$malformed" ]; then
			printf '{ "Build": { "Solution": "src/Product.slnx" \n' > "$case_dir/$file"
		else
			printf '{ "Build": { "Solution": "src/Product.slnx" }, "$marker": "%s" }\n' "$file" \
				> "$case_dir/$file"
		fi
	done

	user_home="$case_dir/.user-config-home"
	mkdir -p "$user_home"
	if [ "$user_config_file" = "true" ]; then
		mkdir -p "$user_home/purview-build"
		printf '{ "PublishLocalNuGet": { "LocalFeedPath": "/tmp/scenario-feed" } }\n' \
			> "$user_home/purview-build/purview-build.json"
	fi

	# Absolute, so the run does not depend on the working directory it is launched from.
	abs_case_dir=$(cd "$case_dir" && pwd)
	abs_user_home=$(cd "$user_home" && pwd)

	cli_args=""
	if [ -n "$config" ]; then
		cli_args="--config $config"
	fi
	if [ "$user_config" = "true" ]; then
		cli_args="$cli_args --user-config"
	fi

	# Each case runs in its own subshell so the environment it needs cannot leak into the next.
	set +e
	out=$(
		cd "$abs_case_dir" || exit 97

		if [ -n "$env_config" ]; then
			PURVIEW_BUILD_CONFIG="$env_config"
			export PURVIEW_BUILD_CONFIG
		fi

		XDG_CONFIG_HOME="$abs_user_home"
		export XDG_CONFIG_HOME

		if [ "$is_local" = "true" ]; then
			unset CI
			unset GITHUB_ACTIONS
		else
			GITHUB_ACTIONS=true
			export GITHUB_ACTIONS
		fi

		# shellcheck disable=SC2086
		"$TOOL" release-explain --format=json $cli_args 2>&1
	)
	actual_exit=$?
	set -e

	if [ "$expected_exit" -ne 0 ]; then
		if [ "$actual_exit" -eq 0 ]; then
			failed=$((failed + 1))
			printf 'FAIL %-52s expected a failure, got success\n' "$name"
		elif [ -n "$expected_error" ] && ! printf '%s' "$out" | grep -qF "$expected_error"; then
			failed=$((failed + 1))
			printf 'FAIL %-52s expected the error to mention "%s", got:\n%s\n' \
				"$name" "$expected_error" "$out"
		else
			printf 'ok   %-52s failed as expected (%s)\n' "$name" "$expected_error"
		fi
		continue
	fi

	if [ "$actual_exit" -ne 0 ]; then
		failed=$((failed + 1))
		printf 'FAIL %-52s expected success, got exit %s:\n%s\n' "$name" "$actual_exit" "$out"
		continue
	fi

	actual_path=$(printf '%s' "$out" | jqr '.configuration.resolvedPath // ""')
	actual_user_active=$(printf '%s' "$out" | jqr '.configuration.userConfigActive')

	case_failed=0

	if [ -z "$expected_path" ]; then
		if [ -n "$actual_path" ]; then
			case_failed=1
			printf 'FAIL %-52s expected no configuration, got "%s"\n' "$name" "$actual_path"
		fi
	elif ! ends_with "$actual_path" "$expected_path"; then
		case_failed=1
		printf 'FAIL %-52s expected resolved to end with "%s", got "%s"\n' \
			"$name" "$expected_path" "${actual_path:-none}"
	fi

	expected_shadow_count=$(printf '%s' "$scenario" | jq '.expected.shadowWarnings | length' | tr -d '\r')
	actual_shadow_count=$(printf '%s' "$out" | jq '.configuration.shadowedPaths | length' | tr -d '\r')

	if [ "$expected_shadow_count" != "$actual_shadow_count" ]; then
		case_failed=1
		printf 'FAIL %-52s expected %s shadowed file(s), got %s\n' \
			"$name" "$expected_shadow_count" "$actual_shadow_count"
		printf '%s' "$out" | jqr '.configuration.shadowedPaths[]? | "       \(.)"'
	else
		shadow_index=0
		while [ "$shadow_index" -lt "$expected_shadow_count" ]; do
			expected_shadow=$(printf '%s' "$scenario" \
				| jqr ".expected.shadowWarnings[$shadow_index]")
			matched=0
			actual_shadow_index=0
			while [ "$actual_shadow_index" -lt "$actual_shadow_count" ]; do
				actual_shadow=$(printf '%s' "$out" \
					| jqr ".configuration.shadowedPaths[$actual_shadow_index]")
				if ends_with "$actual_shadow" "$expected_shadow"; then
					matched=1
				fi
				actual_shadow_index=$((actual_shadow_index + 1))
			done

			if [ "$matched" -eq 0 ]; then
				case_failed=1
				printf 'FAIL %-52s expected "%s" to be reported as shadowed\n' "$name" "$expected_shadow"
			fi

			shadow_index=$((shadow_index + 1))
		done
	fi

	if [ -n "$expected_near_miss" ]; then
		if ! printf '%s' "$out" | jqr '.warnings[]?' | grep -qF "$expected_near_miss"; then
			case_failed=1
			printf 'FAIL %-52s expected a near-miss warning naming "%s"\n' "$name" "$expected_near_miss"
		fi
	fi

	if [ "$expected_user_active" != "null" ] \
		&& [ "$actual_user_active" != "$expected_user_active" ]; then
		case_failed=1
		printf 'FAIL %-52s expected userConfigActive=%s, got %s\n' \
			"$name" "$expected_user_active" "$actual_user_active"
	fi

	if [ "$case_failed" -eq 0 ]; then
		if [ -n "$expected_path" ]; then
			printf 'ok   %-52s resolved %s\n' "$name" "$expected_path"
		else
			printf 'ok   %-52s no configuration (built-in defaults)\n' "$name"
		fi
	else
		failed=$((failed + 1))
	fi
done

printf '\n%s scenario(s), %s failed\n' "$total" "$failed"

[ "$failed" -eq 0 ] || exit 1
