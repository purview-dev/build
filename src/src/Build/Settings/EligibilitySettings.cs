namespace Purview.Build.Settings;

/// <summary>
/// One named release-eligibility policy: which <c>REL0nn</c> rules apply, and the refs they judge
/// against.
/// </summary>
/// <remarks>
/// <see cref="Rules"/> is either an absolute list (<c>["REL001", "REL002"]</c>) or a list of deltas
/// against <see cref="Inherits"/> (<c>["+REL006", "-REL005"]</c>). Mixing the two forms in one list
/// is a configuration error, because the intended precedence would not be obvious.
/// </remarks>
public sealed record EligibilityPolicySettings
{
	/// <summary>
	/// Name of the policy this one is expressed as a diff from. A missing parent is an error naming
	/// the unresolved policy.
	/// </summary>
	public string? Inherits { get; init; }

	public string[] Rules { get; init; } = [];

	/// <summary>
	/// Refs a stable (non-prerelease) version may be released from (<c>REL003</c>). Entries may use
	/// a trailing <c>*</c>, for example <c>refs/heads/release/*</c>.
	/// </summary>
	public string[] StableRefs { get; init; } = [];

	/// <summary>
	/// Refs a version with a non-zero PATCH component may be released from (<c>REL004</c>).
	/// </summary>
	public string[] ServicingRefs { get; init; } = [];

	/// <summary>
	/// Refs that represent the trunk, where a release is not cut. Recorded so a policy can describe
	/// the branch model it belongs to; no rule reads it directly today.
	/// </summary>
	public string[] TrunkRefs { get; init; } = [];

	/// <summary>
	/// Refs a four-part version may be released from (<c>REL006</c>).
	/// </summary>
	public string[] FourPartRefs { get; init; } = [];

	/// <summary>
	/// Whether four-part versions are permitted at all (<c>REL006</c>).
	/// </summary>
	public bool AllowFourPart { get; init; }
}

public sealed record EligibilitySettings
{
	public const string SectionName = "Eligibility";

	/// <summary>
	/// Name of the selected policy. Defaults to the policy that reproduces the pipeline's
	/// pre-eligibility behaviour, so a repository with no <c>Release:Eligibility</c> section is
	/// unaffected.
	/// </summary>
	public string Policy { get; init; } = "ReleaseOnMain";

	/// <summary>
	/// Policies by name. Merged over the built-in policies, so a repository can select
	/// <c>TrunkReservesMinor</c> without redeclaring it, and can override a built-in by name.
	/// </summary>
	public Dictionary<string, EligibilityPolicySettings> Policies { get; init; } = [];

	/// <summary>
	/// The settings a repository that configures nothing gets.
	/// </summary>
	public static EligibilitySettings Default { get; } = new();
}
