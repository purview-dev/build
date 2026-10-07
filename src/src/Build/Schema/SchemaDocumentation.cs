using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Purview.Build.Schema;

/// <summary>
/// Reads the tool's XML documentation so the generated schema can carry the same summaries the
/// settings types already declare, instead of a second, drifting copy of the same prose.
/// </summary>
static partial class SchemaDocumentation
{
	static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

	public static IReadOnlyDictionary<string, string> Load() => Load(typeof(SchemaDocumentation).Assembly);

	public static IReadOnlyDictionary<string, string> Load(Assembly assembly)
	{
		var path = Path.Combine(AppContext.BaseDirectory, $"{assembly.GetName().Name}.xml");
		if (!File.Exists(path))
			return Empty;

		var members = XDocument.Load(path).Root?.Element("members")?.Elements("member");
		if (members is null)
			return Empty;

		Dictionary<string, string> documentation = new(StringComparer.Ordinal);
		foreach (var member in members)
		{
			var name = member.Attribute("name")?.Value;
			if (name is null || member.Element("summary") is not { } summary)
				continue;

			documentation[name] = Flatten(summary);
		}

		return documentation;
	}

	/// <summary>
	/// Renders a documentation node as plain text: XML markup is dropped, a <c>cref</c> becomes the
	/// name it points at, and runs of whitespace collapse to a single space.
	/// </summary>
	static string Flatten(XElement element)
	{
		StringBuilder text = new();

		foreach (var node in element.Nodes())
		{
			switch (node)
			{
				case XText value:
					text.Append(value.Value);
					break;
				case XElement child when child.Name.LocalName is "see" or "seealso":
					text.Append(SeeText(child));
					break;
				// Block-level children separate from their neighbours; inline ones (<c>, <b>) do not.
				case XElement child when child.Name.LocalName is "item" or "para" or "list" or "br":
					text.Append(' ').Append(Flatten(child));
					break;
				case XElement child:
					text.Append(Flatten(child));
					break;
				default:
					break;
			}
		}

		return Whitespace().Replace(text.ToString(), " ").Trim();
	}

	static string SeeText(XElement see)
	{
		// <see langword="true"/> carries its text on the attribute, not as a cref.
		var langword = see.Attribute("langword")?.Value;
		return string.IsNullOrEmpty(langword) ? CrefName(see.Attribute("cref")?.Value) : langword;
	}

	static string CrefName(string? cref)
	{
		if (string.IsNullOrEmpty(cref))
			return string.Empty;

		var name = cref.Length > 2 && cref[1] == ':' ? cref[2..] : cref;
		var separator = name.LastIndexOf('.');

		return separator >= 0 ? name[(separator + 1)..] : name;
	}

	[GeneratedRegex(@"\s+")]
	private static partial Regex Whitespace();
}
