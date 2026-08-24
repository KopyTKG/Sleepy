namespace Sleepy;

/// <summary>
/// Everything about the output that is not the specifications themselves. Shared
/// by every builder, so the CLI and the GUI can offer the same choices whichever
/// format is being written.
/// </summary>
public sealed record BuildOptions
{
    /// <summary>Recorded in the export as the source it was built from.</summary>
    public string SourceName { get; init; } = string.Empty;

    /// <summary>Collection name; defaults to the spec title, or the host for several modules.</summary>
    public string? WorkspaceName { get; init; }

    public FolderLayout Layout { get; init; } = FolderLayout.Nested;

    /// <summary>Prefills the environment's <c>bearer</c> variable.</summary>
    public string BearerToken { get; init; } = string.Empty;
}
