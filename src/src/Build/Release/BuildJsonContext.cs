using System.Text.Json.Serialization;

namespace Purview.Build.Release;

/// <summary>
/// Source-generated serialization metadata for every type the tool reads or writes as JSON.
/// </summary>
/// <remarks>
/// The tool is publishable with NativeAOT, and an AOT build disables reflection-based serialization
/// outright rather than degrading: <c>JsonSerializer.Serialize&lt;T&gt;</c> throws
/// <c>NotSupportedException</c> at the first call. That is how <c>release-explain --format=json</c>
/// failed, while the text rendering beside it worked. Source generation also removes the reflection
/// cost from the ordinary framework-dependent tool, so the metadata is worth having either way.
/// </remarks>
[JsonSerializable(typeof(ReleaseExplanation))]
[JsonSerializable(typeof(string[]))]
partial class BuildJsonContext : JsonSerializerContext;
