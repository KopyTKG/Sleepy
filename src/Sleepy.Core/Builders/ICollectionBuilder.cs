using Sleepy.OpenApi;
using Sleepy.Serialization;
namespace Sleepy.Builders;

/// <summary>
/// Turns the modules sleepy has loaded into one API client's collection format.
///
/// Everything before this point — discovery, parsing, example generation — is
/// format-agnostic, so a new target (Postman, Bruno, Hoppscotch, an HTTP file)
/// is an implementation of this interface plus a registration, and nothing else.
/// </summary>
public interface ICollectionBuilder
{
    /// <summary>Stable identifier used on the command line, e.g. <c>insomnia</c>.</summary>
    string FormatId { get; }

    /// <summary>Name for menus and messages, e.g. "Insomnia v4".</summary>
    string DisplayName { get; }

    /// <summary>
    /// Default output name: a file name for single-document formats, a directory
    /// name for folder-based ones.
    /// </summary>
    string DefaultOutputName { get; }

    /// <summary>Whether the format is a directory of files rather than one document.</summary>
    bool IsFolderFormat { get; }

    BuiltCollection Build(IReadOnlyList<ApiModule> modules, BuildOptions options);
}
