namespace Purview.Build.PackageJson;

/// <summary>
/// Composes and runs a <c>package.json</c> validation from resolved configuration, so the pipeline
/// module and the standalone <c>package-validate</c> command cannot disagree about what is checked.
/// </summary>
static class PackageJsonValidationRunner
{
	/// <summary>
	/// Runs the validation against the repository root <c>package.json</c>, reading the settings the
	/// caller resolved.
	/// </summary>
	/// <param name="settings">The package.json validation settings, including any rule opt-outs.</param>
	/// <param name="build">The build settings, which decide the project type and the scripts a Web run drives.</param>
	/// <param name="version">The version settings, whose strictness PKG002 reconciles against npm's.</param>
	/// <param name="repositoryRoot">The repository root, under which <c>package.json</c> is read.</param>
	/// <param name="resolvedVersion">
	/// The version the pipeline resolved, or <see langword="null"/> when the caller is the standalone
	/// command and no pipeline has run.
	/// </param>
	public static PackageJsonValidationReport Run(
		PackageJsonValidationSettings settings,
		BuildSettings build,
		VersionSettings version,
		string repositoryRoot,
		string? resolvedVersion = null
	)
	{
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(build);
		ArgumentNullException.ThrowIfNull(version);
		ArgumentNullException.ThrowIfNull(repositoryRoot);

		var path = Path.Combine(repositoryRoot, "package.json");

		if (!PackageJsonDocument.TryLoad(path, out var document, out var error))
			return PackageJsonValidationReport.Unreadable(path, error!);

		var profile = PackageJsonProfiles.Resolve(
			settings.Profile,
			build.ProjectType,
			build.RunPack,
			document!.IsPrivate
		);

		var (requiredScripts, recommendedScripts) = ScriptsFor(build);

		PackageJsonValidationContext context = new(
			Document: document,
			Profile: profile,
			Strictness: version.Strictness,
			ResolvedVersion: resolvedVersion,
			RequiredScripts: requiredScripts,
			RecommendedScripts: recommendedScripts,
			AdditionalRequiredFields: settings.AdditionalRequiredFields
		);

		return PackageJsonValidator.Validate(settings, context);
	}

	/// <summary>
	/// Runs the validation from a resolved configuration root, for the standalone command.
	/// </summary>
	public static PackageJsonValidationReport Run(
		IConfiguration configuration,
		string repositoryRoot,
		string? resolvedVersion = null
	)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		return Run(
			configuration
				.GetSection(PackageJsonValidationSettings.SectionName)
				.Get<PackageJsonValidationSettings>() ?? new(),
			configuration.GetSection(BuildSettings.SectionName).Get<BuildSettings>() ?? new(),
			configuration.GetSection(VersionSettings.SectionName).Get<VersionSettings>() ?? new(),
			repositoryRoot,
			resolvedVersion
		);
	}

	/// <summary>
	/// Splits the scripts a Web run drives into the ones it needs (the build command, whose absence
	/// fails the build step anyway) and the ones best practice would add.
	/// </summary>
	static (IReadOnlyList<string> Required, IReadOnlyList<string> Recommended) ScriptsFor(
		BuildSettings build
	)
	{
		if (build.ProjectType != ProjectType.Web)
			return ([], []);

		List<string> required = [];
		List<string> recommended = [];

		Add(required, build.WebBuildCommand);

		if (build.RunTests)
			Add(recommended, build.WebTestCommand);

		if (build.RunLint)
		{
			Add(recommended, build.WebLintCommand);
			Add(recommended, build.WebFormatCheckCommand);
		}

		return (required, recommended);

		static void Add(List<string> target, string command)
		{
			var name = WebScripts.GetScriptName(command);
			if (name is not null && !target.Contains(name, StringComparer.OrdinalIgnoreCase))
				target.Add(name);
		}
	}
}
