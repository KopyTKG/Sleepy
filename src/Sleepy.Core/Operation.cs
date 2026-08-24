using System.Text.Json.Nodes;

namespace Sleepy;

/// <summary>One operation of a specification, flattened out of the paths object.</summary>
public sealed record Operation(
    string Method,
    string Path,
    string Tag,
    string OperationId,
    string Summary,
    JsonNode? Node);
