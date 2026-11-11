#!/bin/sh
# Runs every case in src/tests/fixtures/package-json-scenarios.json through the real CLI
# (`purview-build package-validate --format=json`) and compares the profile, the exit code and the
# exact set of failing and warning rules against the file.
#
# The same fixture drives the in-process TUnit suite, so a mismatch here means the shipped binary and
# the runner disagree. Needs no secrets and no network: the check reads one file.
set -eu

SCENARIOS="src/tests/fixtures/package-json-scenarios.json"
WORK=".scenario-runs/package-json"

. "$(dirname "$0")/find-tool.sh"

rm -rf "$WORK"
mkdir -p "$WORK"

total=0
failed=0

count=$(jq '.scenarios | length' "$SCENARIOS" | tr -d '\r')
index=0

while [ "$index" -lt "$count" ]; do
	scenario=$(jqc ".scenarios[$index]" "$SCENARIOS")
	index=$((index + 1))
	total=$((total + 1))

	name=$(printf '%s' "$scenario" | jqr '.name')
	case_dir="$WORK/$name"
	mkdir -p "$case_dir"

	# The file under test, verbatim: an array is deliberately unreadable, and must be reported as
	# PKG000 rather than crashing the CLI.
	printf '%s' "$scenario" | jq '.packageJson' > "$case_dir/package.json"

	# The configuration the scenario resolves. The project shape decides the profile when Profile is
	# Auto; the remaining keys are the run's own switches.
	printf '%s' "$scenario" | jq '{
		Build: ({
			ProjectType: .projectType,
			RunPack: .packsNuGetPackages
		}
		+ (if .runTests == false then { RunTests: false } else {} end)
		+ (if .runLint == false then { RunLint: false } else {} end)),
		PackageJson: {
			Enabled: true,
			Profile: (.profile // "Auto"),
			DisabledRules: (.disabledRules // []),
			AdditionalRequiredFields: (.additionalRequiredFields // []),
			TreatWarningsAsErrors: (.treatWarningsAsErrors // false)
		}
	}' > "$case_dir/purview-build.json"

	# The command exits non-zero when the validation fails, which is the expected outcome for most
	# cases, so the report — not the exit status — is what is compared.
	out=$(cd "$case_dir" && "$TOOL" package-validate --format=json 2>&1) || true

	if ! printf '%s' "$out" | jq -e '.exitCode' >/dev/null 2>&1; then
		failed=$((failed + 1))
		printf 'FAIL %-52s the CLI produced no JSON report:\n%s\n' "$name" "$out"
		continue
	fi

	actual_profile=$(printf '%s' "$out" | jqr '.profile')
	actual_exit=$(printf '%s' "$out" | jqr '.exitCode')
	actual_errors=$(printf '%s' "$out" | jqc '[.results[] | select(.outcome == "fail") | .ruleId]')
	actual_warnings=$(printf '%s' "$out" | jqc '[.results[] | select(.outcome == "warning") | .ruleId]')

	expected_profile=$(printf '%s' "$scenario" | jqr '.expected.profile')
	expected_exit=$(printf '%s' "$scenario" | jqr '.expected.exitCode')
	expected_errors=$(printf '%s' "$scenario" | jqc '.expected.errors // []')
	expected_warnings=$(printf '%s' "$scenario" | jqc '.expected.warnings // []')

	if [ "$actual_profile" = "$expected_profile" ] \
		&& [ "$actual_exit" = "$expected_exit" ] \
		&& [ "$actual_errors" = "$expected_errors" ] \
		&& [ "$actual_warnings" = "$expected_warnings" ]; then
		printf 'ok   %-52s %-13s exit %s\n' "$name" "$actual_profile" "$actual_exit"
	else
		failed=$((failed + 1))
		printf 'FAIL %-52s expected %s exit %s errors=%s warnings=%s, got %s exit %s errors=%s warnings=%s\n' \
			"$name" "$expected_profile" "$expected_exit" "$expected_errors" "$expected_warnings" \
			"$actual_profile" "$actual_exit" "$actual_errors" "$actual_warnings"
		printf '%s' "$out" | jqr '.results[] | "       \(.outcome)  \(.ruleId)  \(.message)"'
	fi
done

printf '\n%s scenario(s), %s failed\n' "$total" "$failed"

[ "$failed" -eq 0 ] || exit 1
