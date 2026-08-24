namespace Sleepy;

/// <summary>How requests are grouped into folders.</summary>
public enum FolderLayout
{
    /// <summary>A folder per module, with a tag subfolder inside it.</summary>
    Nested,

    /// <summary>A single folder level named "&lt;Module&gt; / &lt;Tag&gt;".</summary>
    Flat,

    /// <summary>A folder per module only; requests sit directly inside it.</summary>
    ModuleOnly,
}
