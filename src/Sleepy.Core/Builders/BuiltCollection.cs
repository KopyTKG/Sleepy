using Sleepy.OpenApi;
using Sleepy.Serialization;
namespace Sleepy.Builders;

/// <summary>One file of a built collection, named relative to the output root.</summary>
public sealed record BuiltFile(string RelativePath, string Content);

/// <summary>
/// What a builder produces. Insomnia and Postman are a single document; Bruno
/// and other folder-based formats are many files, so the result is always a
/// list and single-file formats simply have one entry.
/// </summary>
public sealed record BuiltCollection(string FormatId, IReadOnlyList<BuiltFile> Files)
{
    /// <summary>Convenience for the single-document formats.</summary>
    public static BuiltCollection Single(string formatId, string fileName, string content)
        => new(formatId, [new BuiltFile(fileName, content)]);

    public bool IsSingleFile => Files.Count == 1;

    /// <summary>The content of a single-file collection.</summary>
    public string Content => IsSingleFile
        ? Files[0].Content
        : throw new InvalidOperationException(
            $"the {FormatId} collection has {Files.Count} files; write them individually");
}
