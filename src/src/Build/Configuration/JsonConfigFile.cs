using System.Text.Json;

namespace Purview.Build.Configuration;

/// <summary>
/// Parse-checks a configuration file before it reaches the configuration binder.
/// </summary>
/// <remarks>
/// The JSON configuration provider reports a malformed file as a bare deserialisation failure with
/// no path, which is useless when six probe locations are possible. Parsing first means the error
/// names the file and, where the parser supplies them, the line and position.
/// </remarks>
static class JsonConfigFile
{
	/// <exception cref="InvalidOperationException">The file exists but is not valid JSON.</exception>
	public static void Validate(string path)
	{
		if (!File.Exists(path))
			return;

		try
		{
			using var stream = File.OpenRead(path);
			using var document = JsonDocument.Parse(
				stream,
				new JsonDocumentOptions
				{
					CommentHandling = JsonCommentHandling.Skip,
					AllowTrailingCommas = true,
				}
			);
		}
		catch (JsonException exception)
		{
			var position =
				exception.LineNumber is { } line && exception.BytePositionInLine is { } bytePosition
					// JsonException counts lines from zero; editors count from one.
					? $" at line {line + 1}, position {bytePosition + 1}"
					: string.Empty;

			throw new InvalidOperationException(
				$"'{path}' is not valid JSON{position}: {exception.Message}",
				exception
			);
		}
	}
}
