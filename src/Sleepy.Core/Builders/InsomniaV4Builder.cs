using Sleepy.OpenApi;
using Sleepy.Serialization;
namespace Sleepy.Builders;

/// <summary>The Insomnia v4 export: one JSON document holding every resource.</summary>
public sealed class InsomniaV4Builder : ICollectionBuilder
{
    public string FormatId => "insomnia";

    public string DisplayName => "Insomnia v4";

    public string DefaultOutputName => "insomnia.json";

    public bool IsFolderFormat => false;

    public BuiltCollection Build(IReadOnlyList<ApiModule> modules, BuildOptions options)
        => BuiltCollection.Single(
            FormatId,
            DefaultOutputName,
            JsonWriter.Write(InsomniaBuilder.Build(modules, options)));
}
