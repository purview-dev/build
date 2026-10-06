using Purview.Build.Helpers;
using Purview.Build.Infra;
using Purview.Build.Release;
using Purview.Build.Settings;

namespace Purview.Build;

/// <summary>
/// Repository-root resolution and ref/tag discovery against a real <c>.git</c>.
/// </summary>
[NotInParallel]
public class GitContextTests
{
	static readonly string[] SimulatedTagList = ["v9.9.9"];

	static readonly string[] JsonTagList = ["v1.0.0", "v1.1.0"];

	[Test]
	public async Task FindRepositoryRoot_GivenARealRepository_ResolvesToThePackageJsonDirectory()
	{
		// Arrange
		using var fixture = GitFixture.Create("1.0.0");
		var nested = Path.Combine(fixture.Root, "src", "deep", "nested");
		Directory.CreateDirectory(nested);

		// Act
		var root = PathHelpers.FindRepositoryRoot(nested);

		// Assert
		await Assert
			.That(Path.GetFullPath(root))
			.IsEqualTo(Path.GetFullPath(fixture.Root))
			.Because("the root is the nearest ancestor containing package.json.");
	}

	[Test]
	public async Task FindRepositoryRoot_GivenNoPackageJson_FailsWithActionableGuidance()
	{
		// Arrange
		var directory = Path.Combine(Path.GetTempPath(), "purview-build-norepo-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		try
		{
			// Act
			string Act() => PathHelpers.FindRepositoryRoot(directory);

			// Assert
			var exception = await Assert.That((Func<string>)Act).Throws<InvalidOperationException>();
			await Assert.That(exception!.Message).Contains("package.json");
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task Resolve_GivenARealRepository_DiscoversTheCurrentBranchAsARef()
	{
		// Arrange
		using var fixture = GitFixture.Create("1.0.0", branch: "release/2.0");

		// Act
		var context = fixture.InWorkingDirectory(() =>
			ReleaseContextProvider.Resolve(new ReleaseContextSettings(), fixture.Root)
		);

		// Assert
		await Assert.That(context.Ref).IsEqualTo("refs/heads/release/2.0");
		await Assert.That(context.RefSource).Contains("git branch");
		await Assert.That(context.Simulated).IsFalse();
	}

	[Test]
	public async Task Resolve_GivenARealRepository_DiscoversEveryExistingTag()
	{
		// Arrange
		using var fixture = GitFixture.Create("2.0.2", tags: ["v2.0.0", "v2.0.1", "v1.9.9"]);

		// Act
		var context = fixture.InWorkingDirectory(() =>
			ReleaseContextProvider.Resolve(new ReleaseContextSettings(), fixture.Root)
		);

		// Assert
		await Assert.That(context.ExistingTags).Contains("v2.0.0");
		await Assert.That(context.ExistingTags).Contains("v2.0.1");
		await Assert.That(context.ExistingTags).Contains("v1.9.9");
		await Assert.That(context.HasTag("v2.0.1")).IsTrue();
		await Assert.That(context.HasTag("v2.0.2")).IsFalse();
	}

	[Test]
	public async Task Resolve_GivenGithubRefInTheEnvironment_PrefersItOverTheGitBranch()
	{
		// Arrange
		// On a build agent the checked-out branch is not necessarily the triggering ref.
		using var fixture = GitFixture.Create("1.0.0", branch: "main");
		var previous = Environment.GetEnvironmentVariable("GITHUB_REF");

		try
		{
			Environment.SetEnvironmentVariable("GITHUB_REF", "refs/heads/release/3.1");

			// Act
			var context = fixture.InWorkingDirectory(() =>
				ReleaseContextProvider.Resolve(new ReleaseContextSettings(), fixture.Root)
			);

			// Assert
			await Assert.That(context.Ref).IsEqualTo("refs/heads/release/3.1");
			await Assert.That(context.RefSource).IsEqualTo("GITHUB_REF");
		}
		finally
		{
			Environment.SetEnvironmentVariable("GITHUB_REF", previous);
		}
	}

	[Test]
	public async Task Resolve_GivenSimulatedContext_MakesNoGitOrNetworkLookups()
	{
		// Arrange
		// A simulated evaluation must be inert: it must not pick up the developer's current branch
		// or the repository's real tags, or a simulated verdict would silently blend with reality.
		using var fixture = GitFixture.Create("2.0.2", branch: "main", tags: ["v2.0.0"]);
		var tagsPath = fixture.WriteList("tags.txt", ["v9.9.9"]);

		ReleaseContextSettings settings = new() { Ref = "refs/heads/release/2.0", ExistingTags = tagsPath };

		// Act
		var context = fixture.InWorkingDirectory(() => ReleaseContextProvider.Resolve(settings, fixture.Root));

		// Assert
		await Assert.That(context.Simulated).IsTrue();
		await Assert.That(context.Ref).IsEqualTo("refs/heads/release/2.0");
		await Assert.That(context.ExistingTags).IsEquivalentTo(SimulatedTagList);
		await Assert
			.That(context.ExistingTags)
			.DoesNotContain("v2.0.0")
			.Because("the real repository's tags must not leak into a simulated context.");
	}

	[Test]
	public async Task Resolve_GivenSimulatedRefOnly_DoesNotFallBackToTheRealTags()
	{
		// Arrange
		using var fixture = GitFixture.Create("2.0.2", tags: ["v2.0.0", "v2.0.1"]);
		ReleaseContextSettings settings = new() { Ref = "refs/heads/release/2.0" };

		// Act
		var context = fixture.InWorkingDirectory(() => ReleaseContextProvider.Resolve(settings, fixture.Root));

		// Assert
		await Assert.That(context.Simulated).IsTrue();
		await Assert.That(context.ExistingTags).IsEmpty();
		await Assert.That(context.PublishedVersionsKnown).IsFalse();
	}

	[Test]
	public async Task Resolve_GivenAMissingSimulatedTagFile_FailsNamingThePath()
	{
		// Arrange
		using var fixture = GitFixture.Create("1.0.0");
		ReleaseContextSettings settings = new() { ExistingTags = "does-not-exist.txt" };

		// Act
		ReleaseContext Act() =>
			fixture.InWorkingDirectory(() => ReleaseContextProvider.Resolve(settings, fixture.Root));

		// Assert
		var exception = await Assert.That(Act).Throws<InvalidOperationException>();
		await Assert.That(exception!.Message).Contains("Release:Context:ExistingTags");
		await Assert.That(exception!.Message).Contains("does-not-exist.txt");
	}

	[Test]
	public async Task Resolve_GivenJsonTagList_ReadsItAsAnArray()
	{
		// Arrange
		// The documented format is newline- or JSON-delimited; `gh api`-style output is JSON.
		using var fixture = GitFixture.Create("1.0.0");
		var path = Path.Combine(fixture.Root, "tags.json");
		await File.WriteAllTextAsync(path, """["v1.0.0", "v1.1.0"]""");

		ReleaseContextSettings settings = new() { ExistingTags = path };

		// Act
		var context = fixture.InWorkingDirectory(() => ReleaseContextProvider.Resolve(settings, fixture.Root));

		// Assert
		await Assert.That(context.ExistingTags).IsEquivalentTo(JsonTagList);
	}
}
