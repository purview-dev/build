set quiet

export TESTINGPLATFORM_EXITCODE_IGNORE := "8"
export DOTNET_CLI_TELEMETRY_OPTOUT := "1"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE := "1"
export DO_NOT_TRACK := "1"

root_folder := "src"
project := root_folder / "Build.slnx"
tests_root := root_folder / "tests"
tests_pattern := "*Tests.csproj"
build_configuration := "Debug"
artifacts_folder := "./artifacts"
default_test_filter := "/*/*/*/*/"

pipeline_feed := "https://api.nuget.org/v3/index.json"
pipeline_tool := ".tools/purview-build/purview-build"

# The locally built binary the release/config scenario matrices run, so a sweep does not pay
# `dotnet run` startup per case and needs no network.
local_tool := "src/src/Build/bin/Release/net10.0/Purview.Build"
golden_test_project := tests_root / "Build.UnitTests" / "Build.UnitTests.csproj"
dogfood_artifacts := "./artifacts/dogfood"
dogfood_tool_path := "./.tools/dogfood"

current_version := `bun -p "require('./package.json').version"`

[private]
default:
    just --list

# Install the shared Purview.Build tool from nuget.org if not present
[private]
ensure-pipeline-tool:
    if [ ! -x "{{ pipeline_tool }}" ]; then \
        dotnet tool install Purview.Build --tool-path .tools/purview-build --add-source "{{ pipeline_feed }}"; \
    fi

# Run the PR pipeline (restore, build, lint, tests)
[group('Pipeline')]
pipeline-pr *args:
    just ensure-pipeline-tool
    echo "Running PR pipeline..."
    "{{ pipeline_tool }}" {{ args }}

# Run the build pipeline (restore, build, lint)
[group('Pipeline')]
pipeline-build *args:
    just ensure-pipeline-tool
    echo "Running build pipeline..."
    "{{ pipeline_tool }}" --Build:RunTests=false --Release:Mode=None {{ args }}

# Run the release pipeline (restore, build, lint, tests, pack, publish, GitHub release)
[group('Pipeline')]
pipeline-release *args:
    just ensure-pipeline-tool
    echo "Running release pipeline..."
    "{{ pipeline_tool }}" --Release:Mode=NuGet {{ args }}

# Run the release pipeline (restore, build, lint, tests, pack, local nuget publish)
# Note: `just` runs recipes through the shell, which strips backslashes from unquoted arguments.
# Use the LOCAL_NUGET_FEED_PATH environment variable or forward slashes, e.g.
# just pipeline-local-release --PublishLocalNuGet:LocalFeedPath=p:/_sync-projects/.local-nuget/
[group('Pipeline')]
pipeline-local-release *args:
    just ensure-pipeline-tool
    just lint-fix
    echo "Running local release pipeline..."
    "{{ pipeline_tool }}" --Release:Mode=LocalNuGet {{ args }}

# Run the pipeline with tests enabled
[group('Pipeline')]
pipeline-tests *args:
    just ensure-pipeline-tool
    echo "Running tests pipeline..."
    "{{ pipeline_tool }}" --Build:RunTests=true --Release:Mode=None {{ args }}

# Run the pipeline through pack + validate (restore, build, lint, tests, pack, validate pack contents) without publishing/releasing
[group('Pipeline')]
pipeline-pack-validate *args:
    just ensure-pipeline-tool
    echo "Running pack + validate pipeline..."
    "{{ pipeline_tool }}" --Build:RunPack=true --Build:ValidatePack=true --Release:Mode=None {{ args }}

# Pack this repository's tool from source, install it to a temp tool path, and run it against this repository
[group('Pipeline')]
pipeline-dogfood *args:
    echo "Packing {{ BLUE }}{{ project }}{{ NORMAL }} from source..."
    rm -rf "{{ dogfood_artifacts }}" "{{ dogfood_tool_path }}"
    dotnet pack {{ project }} -c Release -o "{{ dogfood_artifacts }}" \
        -p:Version={{ current_version }} -p:PackageVersion={{ current_version }}
    dotnet tool install Purview.Build --tool-path "{{ dogfood_tool_path }}" \
        --add-source "{{ dogfood_artifacts }}" --version "{{ current_version }}"
    echo "Running the freshly packed tool against this repository..."
    "{{ dogfood_tool_path }}/.config/purview-build" {{ args }}

# Explain the release decision for the working tree, without running any module or mutating anything
[group('Release')]
release-explain *args:
    just scenario-build
    "{{ local_tool }}" release-explain {{ args }}

# Explain the release decision for a simulated ref and version; mutates nothing and makes no network calls
# Example: just release-simulate refs/heads/release/2.0 2.0.2 --Release:Eligibility:Policy=TrunkReservesMinor
[group('Release')]
release-simulate ref version *args:
    just scenario-build
    sh src/tests/scenarios/simulate.sh "{{ ref }}" "{{ version }}" {{ args }}

# Run every eligibility scenario through the real CLI; non-zero on any mismatch
[group('Release')]
release-matrix:
    just scenario-build
    sh src/tests/scenarios/run-eligibility-matrix.sh

# Run every config-resolution scenario through the real CLI; non-zero on any mismatch
[group('Release')]
config-matrix:
    just scenario-build
    sh src/tests/scenarios/run-config-matrix.sh

# Regenerate the release-explain JSON golden file, then show what changed
[group('Release')]
release-explain-golden:
    PURVIEW_BUILD_UPDATE_GOLDEN=1 dotnet test {{ golden_test_project }} -c Release \
        --treenode-filter "/*/*/ReleaseExplainGoldenTests/*"
    git --no-pager diff -- src/tests/fixtures/release-explain.golden.json

# Regenerate purview-build.schema.json from the settings types, then show what changed
[group('Build and Test')]
schema:
    PURVIEW_BUILD_UPDATE_SCHEMA=1 dotnet test {{ golden_test_project }} -c Release \
        --treenode-filter "/*/*/ConfigSchemaTests/Schema_MatchesTheGeneratedFile"
    git --no-pager diff -- purview-build.schema.json

# Build the tool in Release so the scenario matrices can run the real binary
[private]
scenario-build:
    if [ ! -x "{{ local_tool }}" ] && [ ! -x "{{ local_tool }}.exe" ]; then \
        dotnet build {{ project }} -c Release; \
    fi

# Build the project with the specified configuration, defaulting to "Debug"
[group('Build and Test')]
build *args:
    echo "Building {{ BLUE }}{{ project }}{{ NORMAL }} with configuration {{ YELLOW }}{{ build_configuration }}{{ NORMAL }}"
    dotnet build {{ project }} -c {{ build_configuration }} {{ args }}

# Run tests discovered under the tests root, with the specified configuration, defaulting to "Debug"
[group('Build and Test')]
test filter=default_test_filter *args:
    echo "Running tests for {{ BLUE }}{{ project }}{{ NORMAL }} with configuration {{ YELLOW }}{{ build_configuration }}{{ NORMAL }}"
    echo "  and filter {{ GREEN }}{{ filter }}{{ NORMAL }}."
    @for test_project in `find "{{ tests_root }}" -name "{{ tests_pattern }}" -print 2>/dev/null`; do \
        dotnet test "$test_project" -c {{ build_configuration }} --treenode-filter "{{ filter }}" {{ args }}; \
    done

# Run unit tests only
[group('Build and Test')]
test-unit *args:
    just test "/*/*/*/*[Category=Unit]" {{ args }}

# Run the eligibility and config-resolution suites only
[group('Build and Test')]
test-eligibility *args:
    echo "Running eligibility and config-resolution suites..."
    dotnet test {{ golden_test_project }} -c {{ build_configuration }} \
        --treenode-filter "/*/*/EligibilityScenarioTests|EligibilityPolicyResolutionTests|ConfigResolutionScenarioTests|ReleaseOnMainCharacterisationTests|ReleaseExplainGoldenTests|WorkflowParityTests|ReleaseUnitTests|ReleasePublicationTests/*" {{ args }}

# Clean the project with the specified configuration, defaulting to "Debug"
[group('Build and Test')]
clean *args:
    echo "Cleaning {{ BLUE }}{{ project }}{{ NORMAL }} with configuration {{ YELLOW }}{{ build_configuration }}{{ NORMAL }}"
    dotnet clean {{ project }} -c {{ build_configuration }} {{ args }}

# Clean the project, across Debug and Release configurations
[group('Build and Test')]
clean-all *args:
    echo "Cleaning the project across Debug and Release configurations"
    dotnet clean {{ project }} -c Release {{ args }}
    dotnet clean {{ project }} -c Debug {{ args }}

# Restore dependencies for the project
[group('Build and Test')]
restore *args:
    echo "Restoring dependencies for {{ BLUE }}{{ project }}{{ NORMAL }}"
    dotnet restore {{ project }} {{ args }}

# Create NuGet package for the project
[group('Build and Test')]
pack publish_folder=artifacts_folder *args:
    echo "Packing {{ BLUE }}{{ project }}{{ NORMAL }} with configuration {{ YELLOW }}{{ build_configuration }}{{ NORMAL }} to {{ GREEN }}{{ publish_folder }}{{ NORMAL }}"
    dotnet pack {{ project }} -c {{ build_configuration }} -o {{ publish_folder }} -p:Version={{ current_version }} {{ args }}

# Display the current version of the project
[group('Build and Test')]
version:
    echo "Current version: {{ GREEN }}{{ current_version }}{{ NORMAL }}"

# Open the project in Visual Studio/ Registered application
[group('Utilities')]
vs:
    open {{ project }}

# Check code formatting using CSharpier, and lint the workflow files
[group('Utilities')]
lint-check:
    dotnet csharpier check .
    just lint-yaml

# Lint the GitHub Actions workflow files with actionlint, when it is installed
# (action.yml is a composite action, not a workflow, so actionlint cannot parse it)
# Install it with 'scoop install actionlint', 'brew install actionlint', or
# 'go install github.com/rhysd/actionlint/cmd/actionlint@latest'.
[group('Utilities')]
lint-yaml:
    if command -v actionlint >/dev/null 2>&1; then \
        echo "Running actionlint..."; \
        actionlint .github/workflows/*.yml; \
    else \
        echo "actionlint is not installed; skipping workflow linting (see the recipe comment)."; \
    fi

# Fix code formatting issues using CSharpier
[group('Utilities')]
lint-fix:
    dotnet csharpier format .

# Clean up the repository by removing build artifacts, bin/obj folders etc, and shutting down the build server
[group('Utilities')]
scrub:
    find . -type d \( -name bin -o -name obj \) -exec rm -rf {} +
    just clean
    just restore --force-evaluate
    dotnet build-server shutdown
