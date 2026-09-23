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

# Check code formatting using CSharpier
[group('Utilities')]
lint-check:
    dotnet csharpier check .

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
