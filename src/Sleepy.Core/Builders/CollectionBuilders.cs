using Sleepy.OpenApi;
using Sleepy.Serialization;
namespace Sleepy.Builders;

/// <summary>The output formats sleepy knows how to write.</summary>
public static class CollectionBuilders
{
    /// <summary>Every registered format, in the order they are offered.</summary>
    public static IReadOnlyList<ICollectionBuilder> All { get; } =
    [
        new InsomniaV4Builder(),
        new PostmanV21Builder(),
        new BrunoBuilder(),
    ];

    /// <summary>The format used when none is asked for.</summary>
    public static ICollectionBuilder Default => All[0];

    /// <summary>The ids accepted on the command line, for help and error messages.</summary>
    public static IReadOnlyList<string> Ids { get; } = All.Select(builder => builder.FormatId).ToList();

    /// <summary>Looks a format up by id, case-insensitively.</summary>
    public static ICollectionBuilder? Find(string formatId)
        => All.FirstOrDefault(builder =>
            string.Equals(builder.FormatId, formatId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Looks a format up, failing with the list of ids that would have worked.</summary>
    public static ICollectionBuilder Resolve(string formatId)
        => Find(formatId)
        ?? throw new SleepyException(
            $"unknown format: {formatId} (known formats: {string.Join(", ", Ids)})");
}
