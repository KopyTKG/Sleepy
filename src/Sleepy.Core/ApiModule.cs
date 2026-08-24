using System.Text.Json.Nodes;

namespace Sleepy;

/// <summary>
/// One API module: a parsed specification, the name it is known by, and where it
/// came from. Discovery produces these; builders consume them.
/// </summary>
public sealed record ApiModule(string Name, JsonNode? Spec, string BaseUrl, string SourceName);
