using System.Text;
using Microsoft.Extensions.Configuration;
using Purview.Build.Helpers;
using Purview.Build.Settings;

namespace Purview.Build;

public class PackValidationSettingsTests
{
	[Test]
	public async Task RequiredCompilerFlags_GivenDefaults_IsEmpty()
	{
		PackValidationSettings settings = new();

		await Assert.That(settings.RequiredCompilerFlags).IsEmpty();
	}

	// Mirrors the values appsettings.json ships. The two booleans bind correctly from there, so this
	// is the combination every consuming repository actually runs with unless it opts in.
	[Test]
	public async Task RequiresAssemblyInspection_GivenTheShippedDefaults_ReturnsFalse()
	{
		PackValidationSettings settings = new() { RequireSourceLink = false, RequireDeterministic = false };

		await Assert.That(settings.RequiredCompilerFlags).IsEmpty();
		await Assert.That(PackageInspector.RequiresAssemblyInspection(settings)).IsFalse();
	}

	// The reason RequiredCompilerFlags must default to empty: an empty JSON array contributes no
	// configuration keys, so the binder has nothing to overwrite the initialiser with. A non-empty
	// default therefore survives both appsettings.json and a consumer's purview-build.json, turning
	// assembly inspection on in every consuming repository with no way to switch it off.
	[Test]
	public async Task RequiredCompilerFlags_GivenAnEmptyArrayInConfiguration_StaysEmpty()
	{
		var settings = Bind(
			/*lang=json,strict*/
			"""
			{
				"PackValidation": {
					"RequireSourceLink": false,
					"RequireDeterministic": false,
					"RequiredCompilerFlags": []
				}
			}
			"""
		);

		await Assert.That(settings.RequiredCompilerFlags).IsEmpty();
		await Assert.That(PackageInspector.RequiresAssemblyInspection(settings)).IsFalse();
	}

	[Test]
	public async Task RequiredCompilerFlags_GivenFlagsInConfiguration_BindsThem()
	{
		var settings = Bind( /*lang=json,strict*/
			"""{ "PackValidation": { "RequiredCompilerFlags": [ "optimization=release" ] } }"""
		);

		await Assert.That(settings.RequiredCompilerFlags).IsEquivalentTo(["optimization=release"]);
		await Assert.That(PackageInspector.RequiresAssemblyInspection(settings)).IsTrue();
	}

	static PackValidationSettings Bind(string json)
	{
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));
		var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

		return configuration.GetSection(PackValidationSettings.SectionName).Get<PackValidationSettings>()
			?? new PackValidationSettings();
	}
}
