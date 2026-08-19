using Sleepy.Gui.Services;

namespace Sleepy.Gui.Models;

/// <summary>One discovered module, with the checkbox state shown in the module list.</summary>
public partial class ModuleEntry : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    public ModuleEntry(ApiModule module)
    {
        Module = module;
        RequestCount = InsomniaBuilder.CountOperations(module);
    }

    public ApiModule Module { get; }

    public string Name => Module.Name;

    public string Source => Module.SourceName;

    public int RequestCount { get; }

    public string Details => Localizer.Count("Count_Requests", RequestCount);

    /// <summary>Pushes a fresh <see cref="Details"/> string after a language change.</summary>
    public void Relocalize() => OnPropertyChanged(nameof(Details));
}
