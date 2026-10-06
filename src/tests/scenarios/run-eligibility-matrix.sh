#!/bin/sh
# Runs every case in src/tests/fixtures/eligibility-scenarios.json through the real CLI and
# compares the verdict, deciding rule and exit code against the file.
#
# The same fixture drives the in-process TUnit suite, so a mismatch here means the shipped binary
# and the evaluator disagree. Needs no secrets and no network: every case supplies its own ref and
# tag list through Release:Context:*, which suppresses all process and network lookups.
set -eu

SCENARIOS="src/tests/fixtures/eligibility-scenarios.json"
WORK=".scenario-runs/eligibility"

. "$(dirname "$0")/find-tool.sh"

rm -rf "$WORK"
mkdir -p "$WORK"

total=0
failed=0

# The extra policies the scenarios select, written into each generated purview-build.json.
POLICIES=$(jqc '.policies' "$SCENARIOS")

count=$(jq '.scenarios | length' "$SCENARIOS" | tr -d '\r')
index=0

while [ "$index" -lt "$count" ]; do
	scenario=$(jqc ".scenarios[$index]" "$SCENARIOS")
	index=$((index + 1))
	total=$((total + 1))

	name=$(printf '%s' "$scenario" | jqr '.name')
	version=$(printf '%s' "$scenario" | jqr '.version')
	gitref=$(printf '%s' "$scenario" | jqr '.ref')
	policy=$(printf '%s' "$scenario" | jqr '.policy')
	strictness=$(printf '%s' "$scenario" | jqr '.strictness')
	expected_verdict=$(printf '%s' "$scenario" | jqr '.expected.verdict')
	expected_rule=$(printf '%s' "$scenario" | jqr '.expected.ruleId // ""')
	expected_exit=$(printf '%s' "$scenario" | jqr '.expected.exitCode')

	case_dir="$WORK/$name"
	mkdir -p "$case_dir"

	# A throwaway repository root: the version under test, and the policies the case may select.
	printf '{ "name": "scenario", "version": "%s" }\n' "$version" > "$case_dir/package.json"
	printf '{ "Release": { "Eligibility": { "Policies": %s } } }\n' "$POLICIES" \
		> "$case_dir/purview-build.json"

	printf '%s' "$scenario" | jqr '.existingTags[]?' > "$case_dir/tags.txt"

	set -- \
		"--Release:Context:Ref=$gitref" \
		"--Release:Context:ExistingTags=tags.txt" \
		"--Release:Eligibility:Policy=$policy" \
		"--Version:Strictness=$strictness"

	if [ "$(printf '%s' "$scenario" | jqr '.publishedVersions // "null"')" != "null" ]; then
		printf '%s' "$scenario" | jqr '.publishedVersions[]' > "$case_dir/published.txt"
		set -- "$@" "--Release:Context:PublishedVersions=published.txt"
	fi

	if ! out=$(cd "$case_dir" && "$TOOL" release-explain --format=json "$@" 2>&1); then
		printf 'FAIL %-52s the CLI exited non-zero:\n%s\n' "$name" "$out"
		failed=$((failed + 1))
		continue
	fi

	actual_verdict=$(printf '%s' "$out" | jqr '.verdict')
	actual_rule=$(printf '%s' "$out" | jqr '.decidedByRule // ""')
	actual_exit=$(printf '%s' "$out" | jqr '.exitCode')

	if [ "$actual_verdict" = "$expected_verdict" ] \
		&& [ "$actual_rule" = "$expected_rule" ] \
		&& [ "$actual_exit" = "$expected_exit" ]; then
		if [ -n "$actual_rule" ]; then
			printf 'ok   %-52s %s via %s (exit %s)\n' "$name" "$actual_verdict" "$actual_rule" "$actual_exit"
		else
			printf 'ok   %-52s %s (exit %s)\n' "$name" "$actual_verdict" "$actual_exit"
		fi
	else
		failed=$((failed + 1))
		printf 'FAIL %-52s expected %s/%s/exit %s, got %s/%s/exit %s\n' \
			"$name" "$expected_verdict" "${expected_rule:-none}" "$expected_exit" \
			"$actual_verdict" "${actual_rule:-none}" "$actual_exit"
		printf '%s' "$out" | jqr '.rules[] | "       \(.verdict)  \(.message)"'
	fi
done

printf '\n%s scenario(s), %s failed\n' "$total" "$failed"

[ "$failed" -eq 0 ] || exit 1
